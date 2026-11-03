using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;

namespace TradingEmpire.Terminal
{
    /// <summary>The static look of the city, ported from the web version: grass, the owned plot, sidewalks, streets with lane dashes and zebra crossings, trees, lamps, lots under buildings.</summary>
    public static class CityScenery
    {
        static Color H(int rgb) { return MeshBuilder.Hex(rgb); }

        static float Jitter(int x, int y, int salt)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
            h = (h ^ (h >> 13)) * 0x5bd1e995u;
            return ((h ^ (h >> 15)) & 0xffff) / 65535f; // 0..1
        }

        static Color Vary(Color c, float amount, float noise) { float k = 1f + (noise - 0.5f) * amount; return new Color(c.r * k, c.g * k, c.b * k); }

        /// <summary>The whole map: a slab of locked grass with speckle, edged by soil.</summary>
        public static Mesh Ground(CityGrid grid)
        {
            var b = new MeshBuilder();
            int w = grid.Width, h = grid.Height;
            float cs = grid.CellSize;
            b.Box(w * cs + 0.1f, 0.55f, h * cs + 0.1f, 0, -0.6f, 0, H(0x6c7a45)); // the soil under the grass (its top sits below the grass, so the two never fight)
            var a = H(0x8a9a5b); var c = H(0x7e8e50);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var (wx, wz) = grid.CellToWorld(new Cell(x, y));
                    float n = Jitter(x, y, 1);
                    b.Box(cs, 0.01f, cs, wx, -0.01f, wz, Vary((x + y) % 2 == 0 ? a : c, 0.1f, n));
                }
            return b.ToMesh("Ground");
        }

        /// <summary>The owned land: brighter grass with a chequer, a light sidewalk around it, and streets with lane dashes and zebra crossings.</summary>
        public static Mesh Plot(CityGrid grid)
        {
            var b = new MeshBuilder();
            var r = grid.Buildable;
            float cs = grid.CellSize;
            var a = H(0x7bcb68); var c = H(0x78c765);
            for (int y = r.Y; y < r.Y + r.H; y++)
                for (int x = r.X; x < r.X + r.W; x++)
                {
                    var (wx, wz) = grid.CellToWorld(new Cell(x, y));
                    b.Box(cs, 0.02f, cs, wx, 0f, wz, Vary((x + y) % 2 == 0 ? a : c, 0.04f, Jitter(x, y, 2)));
                }
            // light paved border around the owned land
            const float t = 0.5f;
            var sw = H(0xd9dde2);
            Action<float, float, float, float> strip = (sizeX, sizeZ, gx, gy) => b.Box(sizeX, 0.06f, sizeZ, (gx - grid.Width / 2f) * cs, 0, (gy - grid.Height / 2f) * cs, sw);
            float L = r.W + 2 * t, Hh = r.H + 2 * t;
            strip(L, t, r.X + r.W / 2f, r.Y - t / 2);
            strip(L, t, r.X + r.W / 2f, r.Y + r.H + t / 2);
            strip(t, Hh, r.X - t / 2, r.Y + r.H / 2f);
            strip(t, Hh, r.X + r.W + t / 2, r.Y + r.H / 2f);

            // streets
            float rw = Scenery.RoadWidth, sw2 = Scenery.SidewalkWidth, edge = (rw + sw2) / 2f;
            var asphalt = H(0x4a4f57); var walk = H(0xd9dde2); var dash = H(0xf2e6a0); var zebra = H(0xf4f4f4);
            Action<float, float, float, float, float, float, Color> put = (px, py, pz, sx, sz, height, color) => b.Box(sx, height, sz, px, py - height / 2f, pz, color);
            foreach (var (cell, kind) in Roads.RoadCellsIn(r))
            {
                var (px, pz) = grid.CellToWorld(cell);
                if (kind == Roads.Kind.Cross)
                {
                    put(px, 0.03f, pz, cs, cs, 0.03f, asphalt);
                    foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                        put(px + sx * cs * (1 - sw2) / 2f, 0.05f, pz + sz * cs * (1 - sw2) / 2f, sw2 * cs, sw2 * cs, 0.05f, walk);
                }
                else if (kind == Roads.Kind.NorthSouth)
                {
                    put(px, 0.03f, pz, rw * cs, cs, 0.03f, asphalt);
                    foreach (int s in new[] { -1, 1 }) put(px + s * edge * cs, 0.05f, pz, sw2 * cs, cs, 0.05f, walk);
                    put(px, 0.035f, pz, 0.05f * cs, 0.4f * cs, 0.004f, dash);
                }
                else
                {
                    put(px, 0.03f, pz, cs, rw * cs, 0.03f, asphalt);
                    foreach (int s in new[] { -1, 1 }) put(px, 0.05f, pz + s * edge * cs, cs, sw2 * cs, 0.05f, walk);
                    put(px, 0.035f, pz, 0.4f * cs, 0.05f * cs, 0.004f, dash);
                }
            }
            foreach (var (cell, ns) in Scenery.Crosswalks(r))
            {
                var (px, pz) = grid.CellToWorld(cell);
                foreach (float off in new[] { -0.26f, -0.13f, 0f, 0.13f, 0.26f })
                    if (ns) put(px + off, 0.036f, pz, 0.06f, 0.34f, 0.004f, zebra); else put(px, 0.036f, pz + off, 0.34f, 0.06f, 0.004f, zebra);
            }
            return b.ToMesh("Plot");
        }

        static void LowPolyTree(MeshBuilder b, float x, float z, float scale)
        {
            b.Cylinder(0.075f * scale, 0.5f * scale, x, 0, z, H(0x7a5a3a), 0, 6);
            b.Icosahedron(0.42f * scale, x, 0.85f * scale, z, H(0x3f8f4a));
        }

        /// <summary>Trees on land you do not own yet, trees along the streets, and street lamps (one merged mesh).</summary>
        public static Mesh Props(CityGrid grid)
        {
            var b = new MeshBuilder();
            var r = grid.Buildable;
            foreach (var t in Scenery.TreePlacements(grid.Width, grid.Height, grid.CellSize, r)) LowPolyTree(b, t.X, t.Z, t.Scale);
            float edge = (Scenery.RoadWidth + Scenery.SidewalkWidth) / 2f;
            foreach (var (cell, side) in Scenery.StreetTrees(r))
            {
                var (px, pz) = grid.CellToWorld(cell);
                bool ns = Roads.KindOf(cell) == Roads.Kind.NorthSouth;
                LowPolyTree(b, px + (ns ? side * edge * grid.CellSize : 0), pz + (ns ? 0 : side * edge * grid.CellSize), 0.6f);
            }
            Action<float, float> lamp = (x, z) =>
            {
                b.Cylinder(0.03f, 0.9f, x, 0, z, H(0x4a525a), 0, 6);
                b.Icosahedron(0.08f, x, 0.95f, z, H(0xfff2c0), 0.9f);
            };
            foreach (var l in Scenery.LampPlacements(grid.Width, grid.Height, grid.CellSize, r)) lamp(l.X, l.Z);
            float off = (Scenery.RoadWidth / 2f + Scenery.SidewalkWidth / 2f) * grid.CellSize;
            foreach (var (cell, sx, sy) in Scenery.IntersectionCorners(r)) { var (px, pz) = grid.CellToWorld(cell); lamp(px + sx * off, pz + sy * off); }
            return b.ToMesh("Props");
        }

        // ---------- lots (the paved pad under every building) ----------

        static (int top, int kerb) LotColors(DistrictId d)
        {
            switch (d)
            {
                case DistrictId.Residential: return (0xe7dcc2, 0xf3eee2);
                case DistrictId.Commercial: return (0xf1d9a3, 0xf4ede0);
                case DistrictId.Business: return (0xc4d0de, 0xe9edf2);
                case DistrictId.Luxury: return (0xe6c4dc, 0xf6eaf2);
                case DistrictId.Recreation: return (0x93d68f, 0xe3efd9);
                default: return (0xa9d6dc, 0xe4f2f4);
            }
        }

        /// <summary>The lot of a building of w x h cells: bare earth while it is being built, the district's paving with its corner props once finished.</summary>
        public static Mesh Lot(DistrictId district, int w, int h, bool built)
        {
            var b = new MeshBuilder();
            var (top, kerb) = LotColors(district);
            b.Box(w, 0.045f, h, 0, 0, 0, H(kerb));
            b.Box(w - 0.08f, 0.052f, h - 0.08f, 0, 0, 0, built ? H(top) : H(0x9a7b55));
            if (built)
            {
                float cx = w / 2f - 0.09f, cz = h / 2f - 0.09f;
                var front = new[] { (-cx, cz), (cx, cz) };
                var all = new[] { (-cx, cz), (cx, cz), (-cx, -cz), (cx, -cz) };
                switch (district)
                {
                    case DistrictId.Residential: foreach (var (x, z) in all) b.Box(0.13f, 0.13f, 0.13f, x, 0.05f, z, H(0x4f9d4f)); break;
                    case DistrictId.Luxury: foreach (var (x, z) in front) { b.Box(0.14f, 0.14f, 0.14f, x, 0.05f, z, H(0x3d8a4a)); b.Box(0.06f, 0.04f, 0.06f, x, 0.19f, z, H(0xe86a92)); } break;
                    case DistrictId.Commercial: foreach (var (x, z) in front) b.Cylinder(0.04f, 0.16f, x, 0.05f, z, H(0xf0b429), 0, 10); break;
                    case DistrictId.Business: foreach (var (x, z) in front) { b.Box(0.16f, 0.1f, 0.16f, x, 0.05f, z, H(0x8b95a1)); b.Box(0.12f, 0.06f, 0.12f, x, 0.15f, z, H(0x4f9d4f)); } break;
                    case DistrictId.Financial:
                        b.Cylinder(0.02f, 0.9f, -cx, 0.05f, cz, H(0xcfd6dc), 0, 8);
                        b.Box(0.28f, 0.14f, 0.02f, -cx + 0.15f, 0.78f, cz, H(0xffc857), 0, null, 0, 0, 0, 1, 0.7f);
                        break;
                }
            }
            return b.ToMesh("Lot");
        }

        // ---------- traffic ----------

        public static Mesh Car(Color color)
        {
            var b = new MeshBuilder();
            b.Box(0.36f, 0.1f, 0.18f, 0, 0.02f, 0, color);
            b.Box(0.18f, 0.08f, 0.15f, -0.02f, 0.12f, 0, H(0x2b3a4a));
            return b.ToMesh("Car");
        }

        public static Mesh Walker(Color color)
        {
            var b = new MeshBuilder();
            b.Box(0.06f, 0.17f, 0.06f, 0, 0.015f, 0, color);
            return b.ToMesh("Walker");
        }

        public static Mesh Scaffold(float w, float d, float height)
        {
            var b = new MeshBuilder();
            var pole = H(0x9aa4ae); var board = H(0xc9a25d);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.Box(0.04f, height, 0.04f, sx * (w / 2f + 0.04f), 0, sz * (d / 2f + 0.04f), pole);
            for (float y = 0.4f; y < height; y += 0.6f)
            {
                b.Box(w + 0.12f, 0.025f, 0.03f, 0, y, d / 2f + 0.04f, board);
                b.Box(w + 0.12f, 0.025f, 0.03f, 0, y, -d / 2f - 0.04f, board);
                b.Box(0.03f, 0.025f, d + 0.12f, w / 2f + 0.04f, y, 0, board);
                b.Box(0.03f, 0.025f, d + 0.12f, -w / 2f - 0.04f, y, 0, board);
            }
            return b.ToMesh("Scaffold");
        }
    }

    /// <summary>Cars and pedestrians that loop along the streets. Purely visual: they never interact with anything.</summary>
    public sealed class TrafficSystem
    {
        sealed class Mover { public Lane Lane; public float S0, Speed; public bool Car; public Transform T; }

        readonly List<Mover> _movers = new List<Mover>();
        readonly CityGrid _grid;
        double _time;

        static readonly int[] CarColors = { 0xd9534f, 0xf0b429, 0x4a90d9, 0xf4f1ea, 0x3fa56b, 0x7c5cbf };

        public TrafficSystem(CityGrid grid, Transform parent, Material material, RectI area)
        {
            _grid = grid;
            var carMeshes = CarColors.Select(c => CityScenery.Car(MeshBuilder.Hex(c))).ToArray();
            var walkMeshes = Enumerable.Range(0, 8).Select(i => CityScenery.Walker(Color.HSVToRGB(i / 8f, 0.55f, 0.85f))).ToArray();
            int li = 0;
            foreach (var lane in Scenery.CarLanes(area))
            {
                int n = Scenery.MoversFor(lane, 3.5f);
                for (int i = 0; i < n; i++) Add(new Mover { Lane = lane, S0 = (i + (li % 2) * 0.5f) * (lane.Length / (float)n), Speed = 0.9f, Car = true }, carMeshes[_movers.Count % carMeshes.Length], parent, material);
                li++;
            }
            li = 0;
            foreach (var lane in Scenery.WalkLanes(area))
            {
                int n = Scenery.MoversFor(lane, 3f);
                for (int i = 0; i < n; i++) Add(new Mover { Lane = lane, S0 = (i + 0.3f + (li % 3) * 0.2f) * (lane.Length / (float)n), Speed = 0.22f * (0.8f + ((i * 7 + li) % 5) * 0.1f), Car = false }, walkMeshes[(_movers.Count + i) % walkMeshes.Length], parent, material);
                li++;
            }
            Animate(0, true);
        }

        void Add(Mover m, Mesh mesh, Transform parent, Material material)
        {
            var go = new GameObject(m.Car ? "Car" : "Walker", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.T = go.transform;
            _movers.Add(m);
        }

        public void Animate(float dt, bool running)
        {
            if (running) _time += dt;
            foreach (var m in _movers)
            {
                var (x, y) = Scenery.LanePoint(m.Lane, m.S0 + m.Speed * _time);
                m.T.localPosition = new Vector3((x - _grid.Width / 2f) * _grid.CellSize, 0f, (y - _grid.Height / 2f) * _grid.CellSize);
                // cars face along their lane; their long side is the x axis
                float yaw = m.Lane.NorthSouth ? (m.Lane.Dir == 1 ? -90f : 90f) : (m.Lane.Dir == 1 ? 0f : 180f);
                m.T.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        public void Destroy() { foreach (var m in _movers) if (m.T != null) Ui.Kill(m.T.gameObject); _movers.Clear(); }
    }
}
