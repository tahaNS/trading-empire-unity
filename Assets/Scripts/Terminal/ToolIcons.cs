using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>Tiny line icons (like the web version's pixel icons), drawn as UI meshes so the chart tools need no image assets.</summary>
    public sealed class IconGraphic : MaskableGraphic
    {
        public string Icon = "cursor";
        public float Weight = 1.5f;

        public override Texture mainTexture { get { return s_WhiteTexture; } }

        static readonly Dictionary<string, float[]> Segments = Build();

        /// <summary>Segments are x0,y0,x1,y1 in a 0..1 box (y up). A segment of zero length is drawn as a dot.</summary>
        static Dictionary<string, float[]> Build()
        {
            var d = new Dictionary<string, float[]>();
            Func<float, float, float, float, float[]> L = (a, b, c, e) => new[] { a, b, c, e };
            Func<float, float, float[]> dot = (x, y) => new[] { x, y, x, y };
            Func<float[][], float[]> cat = parts => { var l = new List<float>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); };
            Func<float[], float[]> poly = pts => { var l = new List<float>(); for (int i = 0; i + 3 < pts.Length; i += 2) { l.Add(pts[i]); l.Add(pts[i + 1]); l.Add(pts[i + 2]); l.Add(pts[i + 3]); } return l.ToArray(); };
            Func<float, float, float, float, float, float, int, float[]> arc = (cx, cy, rx, ry, a0, a1, n) =>
            {
                var l = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    float t0 = Mathf.Lerp(a0, a1, i / (float)n), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)n);
                    l.Add(cx + Mathf.Cos(t0) * rx); l.Add(cy + Mathf.Sin(t0) * ry); l.Add(cx + Mathf.Cos(t1) * rx); l.Add(cy + Mathf.Sin(t1) * ry);
                }
                return l.ToArray();
            };
            d["cursor"] = poly(new[] { 0.25f, 0.92f, 0.25f, 0.1f, 0.85f, 0.62f, 0.55f, 0.62f, 0.25f, 0.92f });
            d["ruler"] = cat(new[] { L(0.08f, 0.2f, 0.92f, 0.8f), L(0.3f, 0.36f, 0.22f, 0.5f), L(0.5f, 0.5f, 0.42f, 0.64f), L(0.7f, 0.65f, 0.62f, 0.79f) });
            d["trend"] = cat(new[] { L(0.14f, 0.2f, 0.86f, 0.8f), dot(0.14f, 0.2f), dot(0.86f, 0.8f) });
            d["ray"] = cat(new[] { L(0.14f, 0.25f, 1f, 0.78f), dot(0.14f, 0.25f) });
            d["extline"] = cat(new[] { L(0f, 0.15f, 1f, 0.85f), dot(0.3f, 0.34f), dot(0.7f, 0.62f) });
            d["arrow"] = cat(new[] { L(0.1f, 0.2f, 0.88f, 0.78f), L(0.88f, 0.78f, 0.62f, 0.8f), L(0.88f, 0.78f, 0.78f, 0.54f) });
            d["hline"] = cat(new[] { L(0f, 0.5f, 1f, 0.5f), dot(0.5f, 0.5f) });
            d["hray"] = cat(new[] { L(0.2f, 0.5f, 1f, 0.5f), dot(0.2f, 0.5f) });
            d["vline"] = cat(new[] { L(0.5f, 0f, 0.5f, 1f), dot(0.5f, 0.5f) });
            d["channel"] = cat(new[] { L(0.08f, 0.1f, 0.92f, 0.62f), L(0.08f, 0.42f, 0.92f, 0.94f) });
            d["regression"] = cat(new[] { L(0.06f, 0.22f, 0.94f, 0.76f), L(0.06f, 0.05f, 0.94f, 0.59f), L(0.06f, 0.4f, 0.94f, 0.93f) });
            d["pitchfork"] = cat(new[] { L(0.1f, 0.5f, 0.95f, 0.5f), L(0.35f, 0.3f, 0.95f, 0.78f), L(0.35f, 0.7f, 0.95f, 0.22f), L(0.1f, 0.5f, 0.35f, 0.3f), L(0.1f, 0.5f, 0.35f, 0.7f) });
            d["gannfan"] = cat(new[] { L(0.1f, 0.1f, 0.95f, 0.95f), L(0.1f, 0.1f, 0.95f, 0.55f), L(0.1f, 0.1f, 0.95f, 0.3f), L(0.1f, 0.1f, 0.55f, 0.95f) });
            d["fib"] = cat(new[] { L(0.05f, 0.88f, 0.95f, 0.88f), L(0.05f, 0.64f, 0.95f, 0.64f), L(0.05f, 0.4f, 0.95f, 0.4f), L(0.05f, 0.14f, 0.95f, 0.14f) });
            d["fibext"] = cat(new[] { L(0.05f, 0.9f, 0.95f, 0.9f), L(0.05f, 0.62f, 0.95f, 0.62f), L(0.05f, 0.34f, 0.95f, 0.34f), L(0.1f, 0.1f, 0.4f, 0.9f) });
            d["fibchannel"] = cat(new[] { L(0.06f, 0.08f, 0.94f, 0.4f), L(0.06f, 0.3f, 0.94f, 0.62f), L(0.06f, 0.52f, 0.94f, 0.84f), L(0.06f, 0.74f, 0.94f, 1f) });
            d["fibtime"] = cat(new[] { L(0.1f, 0.05f, 0.1f, 0.95f), L(0.3f, 0.05f, 0.3f, 0.95f), L(0.52f, 0.05f, 0.52f, 0.95f), L(0.88f, 0.05f, 0.88f, 0.95f) });
            d["fibfan"] = cat(new[] { L(0.08f, 0.1f, 0.95f, 0.95f), L(0.08f, 0.1f, 0.95f, 0.65f), L(0.08f, 0.1f, 0.95f, 0.4f), L(0.08f, 0.1f, 0.95f, 0.1f) });
            d["fibarcs"] = cat(new[] { arc(0.1f, 0.1f, 0.5f, 0.5f, 0f, Mathf.PI / 2, 8), arc(0.1f, 0.1f, 0.85f, 0.85f, 0f, Mathf.PI / 2, 10) });
            d["rect"] = poly(new[] { 0.12f, 0.2f, 0.88f, 0.2f, 0.88f, 0.8f, 0.12f, 0.8f, 0.12f, 0.2f });
            d["ellipse"] = arc(0.5f, 0.5f, 0.4f, 0.3f, 0f, Mathf.PI * 2, 18);
            d["triangle"] = poly(new[] { 0.5f, 0.88f, 0.1f, 0.15f, 0.9f, 0.15f, 0.5f, 0.88f });
            d["text"] = cat(new[] { L(0.15f, 0.88f, 0.85f, 0.88f), L(0.5f, 0.88f, 0.5f, 0.1f) });
            d["pricelabel"] = cat(new[] { poly(new[] { 0.05f, 0.5f, 0.28f, 0.82f, 0.95f, 0.82f, 0.95f, 0.18f, 0.28f, 0.18f, 0.05f, 0.5f }) });
            d["up"] = poly(new[] { 0.5f, 0.88f, 0.12f, 0.2f, 0.88f, 0.2f, 0.5f, 0.88f });
            d["down"] = poly(new[] { 0.5f, 0.12f, 0.12f, 0.8f, 0.88f, 0.8f, 0.5f, 0.12f });
            d["long"] = cat(new[] { poly(new[] { 0.1f, 0.12f, 0.9f, 0.12f, 0.9f, 0.88f, 0.1f, 0.88f, 0.1f, 0.12f }), L(0.1f, 0.4f, 0.9f, 0.4f), L(0.5f, 0.55f, 0.5f, 0.8f), L(0.5f, 0.8f, 0.4f, 0.7f), L(0.5f, 0.8f, 0.6f, 0.7f) });
            d["short"] = cat(new[] { poly(new[] { 0.1f, 0.12f, 0.9f, 0.12f, 0.9f, 0.88f, 0.1f, 0.88f, 0.1f, 0.12f }), L(0.1f, 0.6f, 0.9f, 0.6f), L(0.5f, 0.45f, 0.5f, 0.2f), L(0.5f, 0.2f, 0.4f, 0.3f), L(0.5f, 0.2f, 0.6f, 0.3f) });
            d["trash"] = cat(new[] { L(0.15f, 0.82f, 0.85f, 0.82f), L(0.38f, 0.82f, 0.38f, 0.94f), L(0.38f, 0.94f, 0.62f, 0.94f), L(0.62f, 0.94f, 0.62f, 0.82f), poly(new[] { 0.22f, 0.82f, 0.28f, 0.08f, 0.72f, 0.08f, 0.78f, 0.82f }), L(0.42f, 0.66f, 0.42f, 0.24f), L(0.58f, 0.66f, 0.58f, 0.24f) });
            d["list"] = cat(new[] { L(0.3f, 0.78f, 0.92f, 0.78f), L(0.3f, 0.5f, 0.92f, 0.5f), L(0.3f, 0.22f, 0.92f, 0.22f), dot(0.1f, 0.78f), dot(0.1f, 0.5f), dot(0.1f, 0.22f) });
            d["orders"] = cat(new[] { arc(0.5f, 0.5f, 0.4f, 0.4f, 0f, Mathf.PI * 2, 16), L(0.5f, 0.5f, 0.5f, 0.78f), L(0.5f, 0.5f, 0.7f, 0.4f) });
            d["history"] = cat(new[] { arc(0.5f, 0.5f, 0.38f, 0.38f, 0.5f, Mathf.PI * 2 - 0.4f, 14), L(0.5f, 0.5f, 0.5f, 0.72f), L(0.5f, 0.5f, 0.66f, 0.42f), L(0.12f, 0.62f, 0.2f, 0.84f), L(0.12f, 0.62f, 0.34f, 0.7f) });
            d["journal"] = cat(new[] { poly(new[] { 0.2f, 0.1f, 0.8f, 0.1f, 0.8f, 0.9f, 0.2f, 0.9f, 0.2f, 0.1f }), L(0.34f, 0.68f, 0.66f, 0.68f), L(0.34f, 0.48f, 0.66f, 0.48f), L(0.34f, 0.28f, 0.54f, 0.28f) });
            d["stats"] = cat(new[] { L(0.2f, 0.1f, 0.2f, 0.4f), L(0.45f, 0.1f, 0.45f, 0.7f), L(0.7f, 0.1f, 0.7f, 0.55f), L(0.9f, 0.1f, 0.9f, 0.85f) });
            d["bar"] = cat(new[] { L(0.15f, 0.1f, 0.15f, 0.5f), L(0.38f, 0.1f, 0.38f, 0.8f), L(0.62f, 0.1f, 0.62f, 0.35f), L(0.85f, 0.1f, 0.85f, 0.65f), L(0.05f, 0.1f, 0.95f, 0.1f) });
            d["expand"] = cat(new[] { L(0.1f, 0.9f, 0.1f, 0.62f), L(0.1f, 0.9f, 0.38f, 0.9f), L(0.9f, 0.1f, 0.9f, 0.38f), L(0.9f, 0.1f, 0.62f, 0.1f), L(0.1f, 0.9f, 0.4f, 0.6f), L(0.9f, 0.1f, 0.6f, 0.4f) });
            d["close"] = cat(new[] { L(0.15f, 0.15f, 0.85f, 0.85f), L(0.15f, 0.85f, 0.85f, 0.15f) });
            return d;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float[] seg;
            if (!Segments.TryGetValue(Icon, out seg)) return;
            var r = GetPixelAdjustedRect();
            float size = Mathf.Min(r.width, r.height);
            var origin = new Vector2(r.center.x - size / 2, r.center.y - size / 2);
            float half = Mathf.Max(0.6f, Weight / 2f);
            for (int i = 0; i + 3 < seg.Length; i += 4)
            {
                var a = origin + new Vector2(seg[i], seg[i + 1]) * size;
                var b = origin + new Vector2(seg[i + 2], seg[i + 3]) * size;
                var d = b - a;
                if (d.sqrMagnitude < 1e-6f) { Quad(vh, a.x - half * 1.3f, a.y - half * 1.3f, a.x + half * 1.3f, a.y + half * 1.3f); continue; }
                var n = new Vector2(-d.y, d.x).normalized * half;
                int idx = vh.currentVertCount;
                var v = UIVertex.simpleVert; v.color = color;
                v.position = a - n; vh.AddVert(v); v.position = a + n; vh.AddVert(v); v.position = b + n; vh.AddVert(v); v.position = b - n; vh.AddVert(v);
                vh.AddTriangle(idx, idx + 1, idx + 2); vh.AddTriangle(idx + 2, idx + 3, idx);
            }
        }

        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert; v.color = color;
            v.position = new Vector3(x0, y0); vh.AddVert(v); v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v); v.position = new Vector3(x1, y0); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>Put an icon at the left of a button's label and move the label over a little.</summary>
        public static IconGraphic AddTo(Button b, string icon, Color color, float size = 14f)
        {
            var ic = Add(b.transform, icon, color, 1.4f);
            var rt = ic.rectTransform;
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(size, size); rt.anchoredPosition = new Vector2(8, 0);
            var label = b.transform.Find("Label");
            if (label != null) ((RectTransform)label).offsetMin = new Vector2(size + 6, 0);
            return ic;
        }

        public static IconGraphic Add(Transform parent, string icon, Color color, float weight = 1.5f)
        {
            var rt = Ui.Rect(parent, "Icon " + icon);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var g = rt.gameObject.AddComponent<IconGraphic>();
            g.Icon = icon; g.color = color; g.Weight = weight; g.raycastTarget = false;
            Ui.Stretch(rt, 5, 5, 5, 5);
            return g;
        }
    }
}
