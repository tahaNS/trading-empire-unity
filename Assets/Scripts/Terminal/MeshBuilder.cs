using System;
using System.Collections.Generic;
using UnityEngine;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Builds one merged mesh out of boxes, cylinders and cones with vertex colours (a whole building is a single mesh and a single draw call).
    /// Coordinates follow the web version (x east, y up, z south), so its models port number for number. Box faces can carry window facades:
    /// uv0 = window tile coordinates, uv1.x = facade kind, uv1.y = glow.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Color32> _c = new List<Color32>();
        readonly List<Vector2> _uv0 = new List<Vector2>(), _uv1 = new List<Vector2>();
        readonly List<int> _t = new List<int>();

        public int VertexCount { get { return _v.Count; } }

        public static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }
        public static Color Shade(Color c, float f) { return new Color(c.r * f, c.g * f, c.b * f); }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color32 color, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int kind, float glow)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(normal); _c.Add(color); _uv1.Add(new Vector2(kind, glow)); }
            _uv0.Add(ua); _uv0.Add(ub); _uv0.Add(uc); _uv0.Add(ud);
            Tri(i, i + 1, i + 2, normal);
            Tri(i, i + 2, i + 3, normal);
        }

        /// <summary>Add a triangle, turned so that it faces the way the normal points.</summary>
        void Tri(int a, int b, int c, Vector3 normal)
        {
            var cross = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(cross, normal) >= 0) { _t.Add(a); _t.Add(b); _t.Add(c); }
            else { _t.Add(a); _t.Add(c); _t.Add(b); }
        }

        static Vector3 RotX(Vector3 p, float c, float s) { return new Vector3(p.x, p.y * c - p.z * s, p.y * s + p.z * c); }

        /// <summary>A box of size w x h x d with its base centre at (x, y, z), optionally turned about the x axis around its own centre.</summary>
        public void Box(float w, float h, float d, float x, float y, float z, Color color, float rotX = 0f, Color? roof = null, int facade = 0, int repSide = 0, int repEnd = 0, int repY = 1, float glow = 0f)
        {
            float cs = Mathf.Cos(rotX), sn = Mathf.Sin(rotX);
            var center = new Vector3(x, y + h / 2f, z);
            float hx = w / 2f, hy = h / 2f, hz = d / 2f;
            Func<float, float, float, Vector3> P = (a, b, c) => center + RotX(new Vector3(a * hx, b * hy, c * hz), cs, sn);
            Func<Vector3, Vector3> N = n => RotX(n, cs, sn);
            Color32 wall = color, top = roof ?? color;
            Vector2 z0 = Vector2.zero;
            // +x and -x use the depth for their window columns, +z and -z the width
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), N(Vector3.right), wall, new Vector2(0, 0), new Vector2(0, repY), new Vector2(repSide, repY), new Vector2(repSide, 0), facade, glow);
            Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), N(Vector3.left), wall, new Vector2(0, 0), new Vector2(0, repY), new Vector2(repSide, repY), new Vector2(repSide, 0), facade, glow);
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), N(Vector3.up), top, z0, z0, z0, z0, 0, glow);
            Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), N(Vector3.down), top, z0, z0, z0, z0, 0, glow);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), N(Vector3.forward), wall, new Vector2(0, 0), new Vector2(repEnd, 0), new Vector2(repEnd, repY), new Vector2(0, repY), facade, glow);
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), N(Vector3.back), wall, new Vector2(0, 0), new Vector2(repEnd, 0), new Vector2(repEnd, repY), new Vector2(0, repY), facade, glow);
        }

        /// <summary>A windowed block: window facade on the four sides, flat roof colour on top (like the web version's block()).</summary>
        public void Block(int kind, Color wall, Color roof, float w, float h, float d, float x, float y, float z, int floors)
        {
            float perUnit = kind == 3 ? 1.6f : 2.2f;
            Func<float, int> rep = span => Math.Max(1, Mathf.RoundToInt(span * perUnit * 0.5f));
            int ry = Math.Max(1, Mathf.RoundToInt(floors * 0.5f));
            Box(w, h, d, x, y, z, wall, 0f, roof, kind, rep(d), rep(w), ry);
        }

        /// <summary>An upright prism with smooth sides (a cylinder; many segments look round).</summary>
        public void Cylinder(float r, float h, float x, float y, float z, Color color, float glow = 0f, int segments = 14)
        {
            Ring(r, r, h, x, y, z, color, glow, segments, true);
        }

        /// <summary>A cone with its base at y.</summary>
        public void Cone(float r, float h, float x, float y, float z, Color color, float glow = 0f, int segments = 12)
        {
            Ring(r, 0f, h, x, y, z, color, glow, segments, false);
        }

        void Ring(float r0, float r1, float h, float x, float y, float z, Color color, float glow, int seg, bool cap)
        {
            Color32 c = color;
            int i0 = _v.Count;
            float slope = (r0 - r1) / Math.Max(h, 1e-4f);
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var n = new Vector3(ca, slope, sa).normalized;
                _v.Add(new Vector3(x + ca * r0, y, z + sa * r0)); _n.Add(n); _c.Add(c); _uv0.Add(Vector2.zero); _uv1.Add(new Vector2(0, glow));
                _v.Add(new Vector3(x + ca * r1, y + h, z + sa * r1)); _n.Add(n); _c.Add(c); _uv0.Add(Vector2.zero); _uv1.Add(new Vector2(0, glow));
            }
            for (int s = 0; s < seg; s++)
            {
                int a = i0 + s * 2;
                var mid = (_n[a] + _n[a + 2]).normalized;
                Tri(a, a + 1, a + 3, mid);
                Tri(a, a + 3, a + 2, mid);
            }
            if (cap && r1 > 0)
            {
                int top = _v.Count;
                _v.Add(new Vector3(x, y + h, z)); _n.Add(Vector3.up); _c.Add(c); _uv0.Add(Vector2.zero); _uv1.Add(new Vector2(0, glow));
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    _v.Add(new Vector3(x + Mathf.Cos(a) * r1, y + h, z + Mathf.Sin(a) * r1)); _n.Add(Vector3.up); _c.Add(c); _uv0.Add(Vector2.zero); _uv1.Add(new Vector2(0, glow));
                }
                for (int s = 0; s < seg; s++) Tri(top, top + 1 + s, top + 2 + s, Vector3.up);
            }
        }

        /// <summary>A flat-shaded icosahedron (a low-poly ball), centred on (x, y, z).</summary>
        public void Icosahedron(float r, float x, float y, float z, Color color, float glow = 0f)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var p = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            Color32 c = color;
            var center = new Vector3(x, y, z);
            for (int i = 0; i < f.Length; i += 3)
            {
                var a = p[f[i]].normalized * r + center; var b = p[f[i + 1]].normalized * r + center; var d = p[f[i + 2]].normalized * r + center;
                var n = ((a + b + d) / 3f - center).normalized;
                int k = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(d);
                for (int j = 0; j < 3; j++) { _n.Add(n); _c.Add(c); _uv0.Add(Vector2.zero); _uv1.Add(new Vector2(0, glow)); }
                Tri(k, k + 1, k + 2, n);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetColors(_c);
            m.SetUVs(0, _uv0);
            m.SetUVs(1, _uv1);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
