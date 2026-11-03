using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public struct Anchor
    {
        /// <summary>Candle time (ms). May lie beyond the last candle: that is only a position on the time axis.</summary>
        public long Time;
        /// <summary>Price in points.</summary>
        public double Price;
        public Anchor(long time, double price) { Time = time; Price = price; }
    }

    /// <summary>
    /// One chart drawing. It wraps the JSON object of the web version's save format, so every kind of drawing (including the ones
    /// this version cannot draw yet) is kept exactly when a game is loaded and saved again.
    /// </summary>
    public sealed class Drawing
    {
        public JsonValue Data { get; private set; }
        public Drawing(JsonValue data) { Data = data; }

        public string Id { get { return Str("id"); } }
        public string Kind { get { return Str("kind"); } }
        public string Color { get { return Str("color") ?? "#6ea8ff"; } }
        public double Width { get { return Num("width") ?? 1.5; } }
        public string Dash { get { return Str("dash") ?? "solid"; } }
        public bool Locked { get { return Flag("locked"); } }
        public bool Hidden { get { return Flag("hidden"); } }
        public string Name { get { return Str("name"); } }
        public string Text { get { return Str("text"); } }

        public string Str(string key) { var v = Data.Get(key); return v != null && v.Kind == JsonKind.String ? v.String : null; }
        public double? Num(string key) { var v = Data.Get(key); return v != null && v.Kind == JsonKind.Number ? v.Number : (double?)null; }
        public bool Flag(string key) { var v = Data.Get(key); return v != null && v.Kind == JsonKind.Bool && v.Bool; }

        public Anchor? GetAnchor(string key)
        {
            var v = Data.Get(key);
            if (v == null || v.Kind != JsonKind.Object || v.Get("time") == null || v.Get("price") == null) return null;
            return new Anchor((long)v.Get("time").AsDouble(), v.Get("price").AsDouble());
        }

        public void SetAnchor(string key, Anchor a) { Data.Set(key, JsonValue.Obj().Set("time", a.Time).Set("price", a.Price)); }

        /// <summary>The draggable anchors in order: "at", or "a", "b", "c" (empty for horizontal and vertical lines).</summary>
        public List<string> AnchorKeys()
        {
            var keys = new List<string>();
            if (GetAnchor("at").HasValue) { keys.Add("at"); return keys; }
            foreach (var k in new[] { "a", "b", "c" }) if (GetAnchor(k).HasValue) keys.Add(k);
            return keys;
        }

        public List<Anchor> Anchors() { return AnchorKeys().Select(k => GetAnchor(k).Value).ToList(); }

        public Drawing Clone() { return new Drawing(Json.Parse(Json.Serialize(Data))); }
    }

    public static class DrawingKinds
    {
        /// <summary>How many clicks / anchors a drawing needs (0 = it is placed by a single click).</summary>
        public static readonly IReadOnlyDictionary<string, int> AnchorCount = new Dictionary<string, int>
        {
            ["hline"] = 0, ["vline"] = 0, ["hray"] = 1, ["text"] = 1, ["pricelabel"] = 1, ["marker"] = 1,
            ["trend"] = 2, ["regression"] = 2, ["gannfan"] = 2, ["rect"] = 2, ["ellipse"] = 2, ["fib"] = 2, ["fibtime"] = 2, ["fibfan"] = 2, ["fibarcs"] = 2,
            ["channel"] = 3, ["pitchfork"] = 3, ["triangle"] = 3, ["fibext"] = 3, ["fibchannel"] = 3, ["position"] = 3,
        };

        public static readonly string[] Colors = { "#6ea8ff", "#f2c25b", "#2fcfae", "#f2686b", "#c792ea", "#e6edf3" };

        /// <summary>Default levels of each Fibonacci tool (all on).</summary>
        public static double[] DefaultFibValues(string kind)
        {
            switch (kind)
            {
                case "fib": return new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1 };
                case "fibext": return new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1, 1.272, 1.618, 2, 2.618 };
                case "fibchannel": return new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1, 1.618, 2.618 };
                case "fibtime": return new double[] { 0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89 };
                case "fibfan": return new[] { 0.382, 0.5, 0.618, 0.786, 1 };
                case "fibarcs": return new[] { 0.382, 0.5, 0.618, 1 };
                default: return new double[0];
            }
        }

        /// <summary>The levels a Fibonacci drawing shows (on ones): its own list, or the defaults.</summary>
        public static List<double> FibLevelsOf(Drawing d)
        {
            var own = d.Data.Get("levels");
            if (own != null && own.Kind == JsonKind.Array && own.Array.Count > 0)
                return own.Array.Where(l => l.Get("on") == null || l.Get("on").Kind != JsonKind.Bool || l.Get("on").Bool).Select(l => l.Get("v").AsDouble()).ToList();
            return DefaultFibValues(d.Kind).ToList();
        }

        public static bool IsFib(string kind) { return kind.StartsWith("fib", StringComparison.Ordinal); }
        public static bool Known(string kind) { return AnchorCount.ContainsKey(kind); }

        /// <summary>Fibonacci retracement price: 0 is the second anchor, 1 the first (levels retrace back towards it).</summary>
        public static double FibPrice(double a, double b, double level) { return b - (b - a) * level; }

        /// <summary>Trend-based extension: the first move (a to b) projected from the third point.</summary>
        public static double FibExtPrice(double a, double b, double c, double level) { return c + (b - a) * level; }

        /// <summary>The price of the line through a and b at a time (it may extend either way).</summary>
        public static double LineAt(Anchor a, Anchor b, long time)
        {
            if (b.Time == a.Time) return a.Price;
            return a.Price + (b.Price - a.Price) * ((double)(time - a.Time) / (b.Time - a.Time));
        }

        /// <summary>Parallel channel: how far the second line sits from the first (price), measured at the third anchor.</summary>
        public static double ChannelOffset(Anchor a, Anchor b, Anchor c) { return c.Price - LineAt(a, b, c.Time); }

        // ---------- builders (the web version's field names) ----------

        public static Drawing Make(string kind, string color, params (string key, Anchor a)[] anchors)
        {
            var o = JsonValue.Obj().Set("kind", kind).Set("color", color);
            foreach (var (key, a) in anchors) o.Set(key, JsonValue.Obj().Set("time", a.Time).Set("price", a.Price));
            return new Drawing(o);
        }

        public static Drawing HLine(double price, string color) { return new Drawing(JsonValue.Obj().Set("kind", "hline").Set("color", color).Set("price", price)); }
        public static Drawing VLine(long time, string color) { return new Drawing(JsonValue.Obj().Set("kind", "vline").Set("color", color).Set("time", time)); }
        public static Drawing Trend(Anchor a, Anchor b, string color) { var d = Make("trend", color, ("a", a), ("b", b)); d.Data.Set("ray", false); return d; }
        public static Drawing Text(Anchor at, string text, string color) { var d = Make("text", color, ("at", at)); d.Data.Set("text", text); return d; }
        public static Drawing Marker(Anchor at, bool up, string color) { var d = Make("marker", color, ("at", at)); d.Data.Set("dir", up ? "up" : "down"); return d; }

        public static Drawing Position(bool longSide, Anchor entry, long rightTime, double target, double stop, string color)
        {
            var d = Make("position", color, ("a", entry), ("b", new Anchor(rightTime, target)), ("c", new Anchor(rightTime, stop)));
            d.Data.Set("side", longSide ? "long" : "short");
            return d;
        }

        /// <summary>
        /// Dragging a handle of a position box: the entry handle (0) moves the whole box, the target (1) and stop (2) handles move
        /// only their price and share the pointer's time (the box width).
        /// </summary>
        public static void DragPositionHandle(Drawing d, int index, Anchor to)
        {
            var a = d.GetAnchor("a").Value; var b = d.GetAnchor("b").Value; var c = d.GetAnchor("c").Value;
            if (index == 0)
            {
                long dt = to.Time - a.Time;
                double dp = to.Price - a.Price;
                d.SetAnchor("a", new Anchor(a.Time + dt, a.Price + dp)); d.SetAnchor("b", new Anchor(b.Time + dt, b.Price + dp)); d.SetAnchor("c", new Anchor(c.Time + dt, c.Price + dp));
                return;
            }
            long time = Math.Max(to.Time, a.Time);
            d.SetAnchor("b", new Anchor(time, index == 1 ? to.Price : b.Price));
            d.SetAnchor("c", new Anchor(time, index == 2 ? to.Price : c.Price));
        }

        /// <summary>The same plan on the other side: target and stop mirrored around the entry.</summary>
        public static void FlipPosition(Drawing d)
        {
            var a = d.GetAnchor("a").Value; var b = d.GetAnchor("b").Value; var c = d.GetAnchor("c").Value;
            d.Data.Set("side", d.Str("side") == "long" ? "short" : "long");
            d.SetAnchor("b", new Anchor(b.Time, 2 * a.Price - b.Price));
            d.SetAnchor("c", new Anchor(c.Time, 2 * a.Price - c.Price));
        }

        /// <summary>Replace the index-th anchor (handles of a position box move as described above).</summary>
        public static void SetAnchorAt(Drawing d, int index, Anchor to)
        {
            if (d.Kind == "position") { DragPositionHandle(d, index, to); return; }
            if (d.Kind == "hline") { d.Data.Set("price", to.Price); return; }
            if (d.Kind == "vline") { d.Data.Set("time", to.Time); return; }
            var keys = d.AnchorKeys();
            if (index >= 0 && index < keys.Count) d.SetAnchor(keys[index], to);
        }

        /// <summary>Move the whole drawing by a time and a price difference.</summary>
        public static void Translate(Drawing d, long dt, double dp)
        {
            if (d.Kind == "hline") { d.Data.Set("price", d.Num("price").Value + dp); return; }
            if (d.Kind == "vline") { d.Data.Set("time", (long)d.Num("time").Value + dt); return; }
            foreach (var k in d.AnchorKeys()) { var a = d.GetAnchor(k).Value; d.SetAnchor(k, new Anchor(a.Time + dt, a.Price + dp)); }
        }
    }

    /// <summary>
    /// Chart drawings, kept per dataset (their anchors are times and prices of that data) and saved with the game. Pure state: no chart
    /// knowledge. <see cref="Items"/> are read-only; change a drawing through <see cref="Update"/>.
    /// </summary>
    public sealed class DrawingStore
    {
        readonly Dictionary<string, List<Drawing>> _byDataset = new Dictionary<string, List<Drawing>>();
        string _datasetId;
        readonly IdSequence _nextId = new IdSequence("d");
        List<Drawing> _items = new List<Drawing>();

        public IReadOnlyList<Drawing> Items { get { return _items; } }
        public event Action Changed;

        /// <summary>Switch the active dataset; drawings of other datasets are kept for when they come back.</summary>
        public void SetDataset(string id)
        {
            _datasetId = id;
            _items = id != null && _byDataset.TryGetValue(id, out var list) ? list.Select(d => d.Clone()).ToList() : new List<Drawing>();
            Fire();
        }

        void Commit(List<Drawing> items)
        {
            _items = items;
            if (_datasetId != null) _byDataset[_datasetId] = items.Select(d => d.Clone()).ToList();
            Fire();
        }

        void Fire() { if (Changed != null) Changed(); }

        public string Add(Drawing d)
        {
            string id = _nextId.Next();
            var copy = d.Clone();
            copy.Data.Set("id", id);
            Commit(new List<Drawing>(_items) { copy });
            return id;
        }

        /// <summary>A copy of one drawing (change it through <see cref="Update"/>). <see cref="Items"/> are the live objects and must be treated as read-only.</summary>
        public Drawing Get(string id) { var d = _items.FirstOrDefault(x => x.Id == id); return d != null ? d.Clone() : null; }

        /// <summary>Change a drawing. The id and the kind can never change.</summary>
        public void Update(string id, Action<Drawing> change)
        {
            var list = _items.Select(d => d.Id == id ? d.Clone() : d).ToList();
            var target = list.FirstOrDefault(d => d.Id == id);
            if (target == null) return;
            string kind = target.Kind;
            change(target);
            target.Data.Set("id", id);
            target.Data.Set("kind", kind);
            Commit(list);
        }

        /// <summary>Move a drawing in the stacking order (later = drawn on top).</summary>
        public void Move(string id, string to)
        {
            var list = new List<Drawing>(_items);
            int i = list.FindIndex(d => d.Id == id);
            if (i < 0) return;
            var d = list[i];
            list.RemoveAt(i);
            int j = to == "top" ? list.Count : to == "bottom" ? 0 : Math.Min(Math.Max(i + (to == "up" ? 1 : -1), 0), list.Count);
            list.Insert(j, d);
            Commit(list);
        }

        public void SetAllHidden(bool hidden)
        {
            Commit(_items.Select(d => { var c = d.Clone(); c.Data.Set("hidden", hidden); return c; }).ToList());
        }

        public void Remove(string id) { Commit(_items.Where(d => d.Id != id).ToList()); }
        public void Clear() { Commit(new List<Drawing>()); }

        /// <summary>For saving: every dataset's drawings.</summary>
        public List<KeyValuePair<string, List<Drawing>>> Snapshot()
        {
            return _byDataset.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new KeyValuePair<string, List<Drawing>>(kv.Key, kv.Value.Select(d => d.Clone()).ToList())).ToList();
        }

        /// <summary>Replace everything from a validated save, then activate <paramref name="activeDataset"/>.</summary>
        public void Restore(IEnumerable<KeyValuePair<string, List<Drawing>>> all, string activeDataset)
        {
            _byDataset.Clear();
            var ids = new List<string>();
            foreach (var g in all)
            {
                _byDataset[g.Key] = g.Value.Select(d => d.Clone()).ToList();
                ids.AddRange(g.Value.Select(d => d.Id));
            }
            _nextId.ResetAfter(ids);
            SetDataset(activeDataset);
        }
    }
}
