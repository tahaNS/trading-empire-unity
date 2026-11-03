using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>A decoration position in world units (cell centres are +0.5), with a size and a turn.</summary>
    public struct Spot
    {
        public float X, Z, Scale, Rotation;
    }

    /// <summary>A straight lane along a road, in grid-cell coordinates. Purely visual: cars and pedestrians loop along these lanes.</summary>
    public sealed class Lane
    {
        /// <summary>True: runs along the grid's y axis (a road with constant x).</summary>
        public bool NorthSouth;
        public int Line;
        public float Offset;
        public int Start, End;
        public int Dir;
        public int Length { get { return End - Start; } }
    }

    /// <summary>Pure, deterministic decoration layout (no engine types) so it can be tested, identical to the web version's.</summary>
    public static class Scenery
    {
        public const float RoadWidth = 0.7f, SidewalkWidth = 0.15f;
        public const int StreetTreeEvery = 3;
        const float CarOffset = 0.17f, WalkOffset = 0.43f;

        /// <summary>Trees on land the player does not own yet. The same seed always yields the same forest; buying land simply clears them.</summary>
        public static List<Spot> TreePlacements(int width, int height, float cellSize, RectI owned, uint seed = 7, double density = 0.2)
        {
            var rand = new Mulberry32(seed);
            var output = new List<Spot>();
            for (int cy = 0; cy < height; cy++)
                for (int cx = 0; cx < width; cx++)
                {
                    // draw the random numbers for every cell so a cell's tree doesn't change when land is bought
                    double r1 = rand.Next(), jx = rand.Next(), jz = rand.Next(), rs = rand.Next(), rr = rand.Next();
                    bool nearOwned = cx >= owned.X - 1 && cx < owned.X + owned.W + 1 && cy >= owned.Y - 1 && cy < owned.Y + owned.H + 1;
                    if (nearOwned || r1 > density) continue;
                    output.Add(new Spot
                    {
                        X = (float)((cx + 0.15 + jx * 0.7 - width / 2.0) * cellSize), Z = (float)((cy + 0.15 + jz * 0.7 - height / 2.0) * cellSize),
                        Scale = (float)(0.8 + rs * 0.6), Rotation = (float)(rr * Math.PI * 2),
                    });
                }
            return output;
        }

        /// <summary>Street lamps just outside the owned area's border, every <paramref name="spacing"/> cells.</summary>
        public static List<Spot> LampPlacements(int width, int height, float cellSize, RectI owned, int spacing = 4)
        {
            var output = new List<Spot>();
            Func<float, float, Spot> toWorld = (gx, gy) => new Spot { X = (gx - width / 2f) * cellSize, Z = (gy - height / 2f) * cellSize, Scale = 1, Rotation = 0 };
            const float off = 0.35f;
            for (int i = 0; i <= owned.W; i += spacing)
            {
                output.Add(toWorld(owned.X + i, owned.Y - off));
                output.Add(toWorld(owned.X + i, owned.Y + owned.H + off));
            }
            for (int j = spacing; j < owned.H; j += spacing)
            {
                output.Add(toWorld(owned.X - off, owned.Y + j));
                output.Add(toWorld(owned.X + owned.W + off, owned.Y + j));
            }
            return output;
        }

        /// <summary>Trees on the sidewalks of straight road cells: every few cells along the road, alternating sides, never next to an intersection.</summary>
        public static List<(Cell cell, int side)> StreetTrees(RectI area)
        {
            var output = new List<(Cell, int)>();
            foreach (var (cell, kind) in Roads.RoadCellsIn(area))
            {
                if (kind == Roads.Kind.Cross) continue;
                int along = kind == Roads.Kind.NorthSouth ? cell.Y : cell.X;
                if (along % StreetTreeEvery != 0) continue;
                if (Roads.IsRoadLine(along - 1) || Roads.IsRoadLine(along + 1)) continue;
                output.Add((cell, (int)Math.Floor(along / (double)StreetTreeEvery) % 2 == 0 ? -1 : 1));
            }
            return output;
        }

        /// <summary>Zebra crossings on the road cell next to every intersection, on each side of it.</summary>
        public static List<(Cell cell, bool northSouth)> Crosswalks(RectI area)
        {
            var output = new List<(Cell, bool)>();
            Func<Cell, bool> inside = c => c.X >= area.X && c.Y >= area.Y && c.X < area.X + area.W && c.Y < area.Y + area.H;
            foreach (var (cell, kind) in Roads.RoadCellsIn(area))
            {
                if (kind == Roads.Kind.Cross) continue;
                bool ns = kind == Roads.Kind.NorthSouth;
                var ahead = ns ? new Cell(cell.X, cell.Y + 1) : new Cell(cell.X + 1, cell.Y);
                var behind = ns ? new Cell(cell.X, cell.Y - 1) : new Cell(cell.X - 1, cell.Y);
                Func<Cell, bool> crossIn = n => Roads.KindOf(n) == Roads.Kind.Cross && inside(n);
                if (crossIn(ahead) || crossIn(behind)) output.Add((cell, ns));
            }
            return output;
        }

        /// <summary>The four sidewalk corners of every intersection, where street lamps stand.</summary>
        public static List<(Cell cell, int sx, int sy)> IntersectionCorners(RectI area)
        {
            var output = new List<(Cell, int, int)>();
            foreach (var (cell, kind) in Roads.RoadCellsIn(area))
            {
                if (kind != Roads.Kind.Cross) continue;
                foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) output.Add((cell, sx, sy));
            }
            return output;
        }

        static List<Lane> Lanes(RectI area, float offset)
        {
            var output = new List<Lane>();
            for (int x = area.X; x < area.X + area.W; x++)
                if (Roads.IsRoadLine(x))
                    foreach (int dir in new[] { 1, -1 }) output.Add(new Lane { NorthSouth = true, Line = x, Offset = dir * offset, Start = area.Y, End = area.Y + area.H, Dir = dir });
            for (int y = area.Y; y < area.Y + area.H; y++)
                if (Roads.IsRoadLine(y))
                    foreach (int dir in new[] { 1, -1 }) output.Add(new Lane { NorthSouth = false, Line = y, Offset = dir * offset, Start = area.X, End = area.X + area.W, Dir = dir });
            return output;
        }

        public static List<Lane> CarLanes(RectI area) { return Lanes(area, CarOffset); }
        public static List<Lane> WalkLanes(RectI area) { return Lanes(area, WalkOffset); }

        /// <summary>Position <paramref name="s"/> cells along a lane (wraps around, so a loop never ends), in continuous grid coordinates.</summary>
        public static (float x, float y) LanePoint(Lane l, double s)
        {
            double len = l.Length;
            double m = ((s % len) + len) % len;
            double along = l.Dir == 1 ? l.Start + m : l.End - m;
            double side = l.Line + 0.5 + l.Offset;
            return l.NorthSouth ? ((float)side, (float)along) : ((float)along, (float)side);
        }

        /// <summary>How many movers to put on a lane: one every <paramref name="spacing"/> cells, at least one on any lane.</summary>
        public static int MoversFor(Lane l, float spacing) { return Math.Max(1, (int)Math.Floor(l.Length / spacing)); }
    }
}
