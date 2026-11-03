using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public struct Cell : IEquatable<Cell>
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell o) { return X == o.X && Y == o.Y; }
        public override bool Equals(object obj) { return obj is Cell c && Equals(c); }
        public override int GetHashCode() { return X * 73856093 ^ Y * 19349663; }
        public override string ToString() { return X + "," + Y; }
    }

    public struct Footprint
    {
        public int W, H;
        public Footprint(int w, int h) { W = w; H = h; }
    }

    public struct RectI
    {
        public int X, Y, W, H;
        public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    public enum PlacementReason { Ok, OutsideLand, Occupied, Road }

    /// <summary>
    /// Pure grid logic (no engine types). Cell (x, y) maps to world (x, z); the grid is centred on the world origin.
    /// A footprint's origin is its minimum-x / minimum-y corner cell. Rotation is in quarter turns clockwise (0..3);
    /// odd rotations swap footprint width and height.
    /// </summary>
    public sealed class CityGrid
    {
        readonly Dictionary<Cell, string> _occupancy = new Dictionary<Cell, string>();
        readonly Dictionary<string, List<Cell>> _cellsById = new Dictionary<string, List<Cell>>();
        RectI _area;

        public readonly int Width, Height;
        public readonly float CellSize;
        /// <summary>Cells that can't be built on (streets). Set by the world; null in plain grids.</summary>
        public Func<Cell, bool> RoadRule;

        public CityGrid(int width, int height, float cellSize, RectI area)
        {
            Width = width; Height = height; CellSize = cellSize; _area = area;
        }

        public RectI Buildable { get { return _area; } }

        public static Footprint Rotated(Footprint fp, int rotation)
        {
            return rotation % 2 == 0 ? fp : new Footprint(fp.H, fp.W);
        }

        public static int NextRotation(int r) { return (r + 1) % 4; }

        /// <summary>Clear all buildings and set the owned land (used when loading a save).</summary>
        public void Reset(RectI area)
        {
            _occupancy.Clear();
            _cellsById.Clear();
            _area = area;
        }

        /// <summary>Grow the owned land. The new area must contain the old one and fit inside the world.</summary>
        public void ExpandBuildable(RectI next)
        {
            var a = _area;
            bool contains = next.X <= a.X && next.Y <= a.Y && next.X + next.W >= a.X + a.W && next.Y + next.H >= a.Y + a.H;
            bool inside = next.X >= 0 && next.Y >= 0 && next.X + next.W <= Width && next.Y + next.H <= Height;
            if (!contains || !inside) throw new ArgumentOutOfRangeException("next", "Invalid land expansion");
            _area = next;
        }

        public bool IsInBounds(Cell c) { return c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height; }
        public bool IsBuildable(Cell c) { var r = _area; return c.X >= r.X && c.Y >= r.Y && c.X < r.X + r.W && c.Y < r.Y + r.H; }

        public string OccupantAt(Cell c)
        {
            return _occupancy.TryGetValue(c, out var id) ? id : null;
        }

        public List<Cell> CellsFor(Cell origin, Footprint fp, int rotation)
        {
            var r = Rotated(fp, rotation);
            var cells = new List<Cell>();
            for (int dy = 0; dy < r.H; dy++)
                for (int dx = 0; dx < r.W; dx++) cells.Add(new Cell(origin.X + dx, origin.Y + dy));
            return cells;
        }

        public PlacementReason Check(Cell origin, Footprint fp, int rotation, bool ignoreRoads = false)
        {
            var cells = CellsFor(origin, fp, rotation);
            if (!cells.All(c => IsInBounds(c) && IsBuildable(c))) return PlacementReason.OutsideLand;
            var rule = RoadRule;
            if (!ignoreRoads && rule != null && cells.Any(c => rule(c))) return PlacementReason.Road;
            if (cells.Any(c => _occupancy.ContainsKey(c))) return PlacementReason.Occupied;
            return PlacementReason.Ok;
        }

        /// <summary>Marks cells occupied. Throws if the placement is invalid (callers should Check first).</summary>
        public void Occupy(string id, Cell origin, Footprint fp, int rotation)
        {
            if (_cellsById.ContainsKey(id)) throw new InvalidOperationException("Building " + id + " already placed");
            // Roads are ignored here so a save made before streets existed still loads.
            var result = Check(origin, fp, rotation, true);
            if (result != PlacementReason.Ok) throw new InvalidOperationException("Invalid placement: " + result);
            var cells = CellsFor(origin, fp, rotation);
            foreach (var c in cells) _occupancy[c] = id;
            _cellsById[id] = cells;
        }

        public void Release(string id)
        {
            if (!_cellsById.TryGetValue(id, out var cells)) return;
            foreach (var c in cells) _occupancy.Remove(c);
            _cellsById.Remove(id);
        }

        /// <summary>World position (x, z) of a cell's centre.</summary>
        public (float x, float z) CellToWorld(Cell c)
        {
            return ((c.X - Width / 2f + 0.5f) * CellSize, (c.Y - Height / 2f + 0.5f) * CellSize);
        }

        /// <summary>World position of the centre of a footprint.</summary>
        public (float x, float z) FootprintCenter(Cell origin, Footprint fp, int rotation)
        {
            var r = Rotated(fp, rotation);
            return ((origin.X + r.W / 2f - Width / 2f) * CellSize, (origin.Y + r.H / 2f - Height / 2f) * CellSize);
        }

        public Cell WorldToCell(float x, float z)
        {
            return new Cell((int)Math.Floor(x / CellSize + Width / 2f), (int)Math.Floor(z / CellSize + Height / 2f));
        }
    }

    /// <summary>
    /// Street layout. Roads run along every grid line ROAD_ANCHOR + n * ROAD_SPACING (both axes), so a block between
    /// two roads is ROAD_SPACING - 1 cells wide. The lines are fixed in world cells, so buying land extends the same
    /// street grid. Road cells are never buildable.
    /// </summary>
    public static class Roads
    {
        public const int Spacing = 7;
        public const int Anchor = 16;

        public enum Kind { None, NorthSouth, EastWest, Cross }

        public static bool IsRoadLine(int n) { return (((n - Anchor) % Spacing) + Spacing) % Spacing == 0; }

        public static Kind KindOf(Cell c)
        {
            bool v = IsRoadLine(c.X), h = IsRoadLine(c.Y);
            if (v && h) return Kind.Cross;
            if (v) return Kind.NorthSouth;
            if (h) return Kind.EastWest;
            return Kind.None;
        }

        public static bool IsRoadCell(Cell c) { return KindOf(c) != Kind.None; }

        public static List<(Cell cell, Kind kind)> RoadCellsIn(RectI area)
        {
            var list = new List<(Cell, Kind)>();
            for (int y = area.Y; y < area.Y + area.H; y++)
                for (int x = area.X; x < area.X + area.W; x++)
                {
                    var c = new Cell(x, y);
                    var k = KindOf(c);
                    if (k != Kind.None) list.Add((c, k));
                }
            return list;
        }
    }
}
