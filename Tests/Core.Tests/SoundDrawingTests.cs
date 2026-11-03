using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class SoundTests
{
    [Fact]
    public void Every_sound_has_a_recipe_that_renders_to_audible_bounded_samples()
    {
        foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
        {
            Assert.True(Sounds.Recipes.ContainsKey(id), id + " has no recipe");
            var samples = SoundSynth.Render(Sounds.Recipes[id]);
            Assert.True(samples.Length > 1000, id + " is too short");
            Assert.True(samples.Length < SoundSynth.SampleRate * 1.2, id + " is too long");
            float peak = samples.Max(Math.Abs);
            Assert.InRange(peak, 0.01f, 1f); // audible, and never clipped
            Assert.True(samples.All(x => !float.IsNaN(x)));
            Assert.True(Math.Abs(samples[0]) < 0.05f, id + " starts with a click");
            Assert.True(Math.Abs(samples[samples.Length - 1]) < 0.05f, id + " ends with a click");
        }
    }

    [Fact]
    public void Notes_start_when_they_should_and_pitch_slides_down_for_errors()
    {
        var notes = new[] { new Note(440, 0.1, 0.05, Wave.Sine, 0.2) };
        var s = SoundSynth.Render(notes);
        Assert.True(s.Take((int)(0.09 * SoundSynth.SampleRate)).All(x => x == 0)); // silent before the note starts
        Assert.True(s.Skip((int)(0.1 * SoundSynth.SampleRate)).Take(2000).Any(x => Math.Abs(x) > 0.01f));
        // a downward glide has fewer zero crossings late in the note than early
        var err = SoundSynth.Render(new[] { new Note(300, 0, 0.3, Wave.Sine, 0.3, 100) });
        Func<int, int, int> crossings = (a, b) => Enumerable.Range(a + 1, b - a - 1).Count(i => (err[i - 1] < 0) != (err[i] < 0));
        Assert.True(crossings(0, 4000) > crossings(8000, 12000));
    }

    [Fact]
    public void Brown_noise_loops_without_a_jump_and_is_deterministic()
    {
        var a = SoundSynth.BrownNoise();
        var b = SoundSynth.BrownNoise();
        Assert.Equal(a, b);
        Assert.Equal(2 * SoundSynth.SampleRate, a.Length);
        Assert.True(Math.Abs(a[a.Length - 1] - a[0]) < 0.05f);
        Assert.InRange(a.Max(Math.Abs), 0.01f, 1f);
    }

    [Theory]
    [InlineData(NoticeKind.Success, "Mission complete: First trade", SoundId.Mission)]
    [InlineData(NoticeKind.Error, "Mission complete: odd", SoundId.Mission)]
    [InlineData(NoticeKind.Error, "Stop-loss hit: buy 0.10 -$20.00", SoundId.TradeLoss)]
    [InlineData(NoticeKind.Error, "Closed buy 0.10 lots: -$3.00", SoundId.TradeLoss)]
    [InlineData(NoticeKind.Error, "Order rejected: No market price yet.", SoundId.Error)]
    [InlineData(NoticeKind.Error, "Can't build House: Space is occupied", SoundId.Error)]
    [InlineData(NoticeKind.Info, "House finished", SoundId.Finished)]
    [InlineData(NoticeKind.Success, "House upgraded to level 2", SoundId.Upgrade)]
    [InlineData(NoticeKind.Success, "Park ordered for $6,000.00, ready in 3 days", SoundId.Build)]
    [InlineData(NoticeKind.Success, "Riverside District purchased for $15,000.00", SoundId.Build)]
    [InlineData(NoticeKind.Success, "Opened buy 0.10 EUR/USD @ 1.10264", SoundId.TradeOpen)]
    [InlineData(NoticeKind.Success, "Take-profit hit: buy 0.10 +$40.00", SoundId.TradeWin)]
    [InlineData(NoticeKind.Success, "Closed buy 0.10: +$5.00", SoundId.TradeWin)]
    [InlineData(NoticeKind.Success, "Game loaded.", SoundId.Success)]
    public void Notices_choose_their_sound(NoticeKind kind, string text, SoundId expected)
    {
        Assert.Equal(expected, Sounds.ForNotice(kind, text));
    }

    [Fact]
    public void Plain_information_is_silent()
    {
        Assert.Null(Sounds.ForNotice(NoticeKind.Info, "Loaded 30,000 candles (synthetic test data)"));
        Assert.Null(Sounds.ForNotice(NoticeKind.Info, "Order cancelled"));
        Assert.Null(Sounds.ForNotice(NoticeKind.Info, "Marketing push switched on"));
    }
}

public class DrawingTests
{
    static readonly string Col = DrawingKinds.Colors[0];

    [Fact]
    public void Fibonacci_prices_and_geometry_match_the_web_version()
    {
        Assert.Equal(110500, DrawingKinds.FibPrice(110000, 111000, 0.5));
        Assert.Equal(111000, DrawingKinds.FibPrice(110000, 111000, 0)); // 0 is the second anchor
        Assert.Equal(110000, DrawingKinds.FibPrice(110000, 111000, 1));
        Assert.Equal(111618, DrawingKinds.FibExtPrice(110000, 111000, 111000, 0.618), 6);
        var a = new Anchor(0, 100); var b = new Anchor(10, 200);
        Assert.Equal(150, DrawingKinds.LineAt(a, b, 5));
        Assert.Equal(300, DrawingKinds.LineAt(a, b, 20)); // the line goes on beyond its anchors
        Assert.Equal(100, DrawingKinds.LineAt(a, new Anchor(0, 200), 7)); // a vertical pair does not divide by zero
        Assert.Equal(25, DrawingKinds.ChannelOffset(a, b, new Anchor(5, 175)));
        Assert.Equal(21, DrawingKinds.AnchorCount.Count);
        Assert.Equal(new[] { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1 }, DrawingKinds.DefaultFibValues("fib"));
    }

    [Fact]
    public void Store_adds_updates_orders_hides_and_removes_per_dataset()
    {
        var s = new DrawingStore();
        s.SetDataset("a");
        string id = s.Add(DrawingKinds.HLine(110000, Col));
        string id2 = s.Add(DrawingKinds.Text(new Anchor(1, 2), "note", Col));
        Assert.Equal("d1", id);
        Assert.Equal(2, s.Items.Count);
        s.SetDataset("b");
        Assert.Empty(s.Items);
        s.Add(DrawingKinds.VLine(5, Col));
        s.SetDataset("a");
        Assert.Equal(new[] { "hline", "text" }, s.Items.Select(d => d.Kind).ToArray());
        s.Update(id, d => { d.Data.Set("price", 111000.0); d.Data.Set("id", "hacked"); d.Data.Set("kind", "rect"); });
        Assert.Equal(111000, s.Get(id).Num("price"));
        Assert.Equal("hline", s.Get(id).Kind); // id and kind can never change
        s.Move(id, "top");
        Assert.Equal(new[] { id2, id }, s.Items.Select(d => d.Id).ToArray());
        s.Move(id, "bottom");
        Assert.Equal(id, s.Items[0].Id);
        s.SetAllHidden(true);
        Assert.All(s.Items, d => Assert.True(d.Hidden));
        s.Remove(id);
        Assert.Single(s.Items);
        s.Clear();
        Assert.Empty(s.Items);
    }

    [Fact]
    public void Items_are_snapshots_so_changing_one_never_edits_the_store_by_accident()
    {
        var s = new DrawingStore();
        s.SetDataset("a");
        string id = s.Add(DrawingKinds.Trend(new Anchor(1, 10), new Anchor(2, 20), Col));
        var copy = s.Get(id);
        copy.Data.Set("color", "#000000");
        Assert.Equal(Col, s.Get(id).Color);
        s.SetDataset("b"); s.SetDataset("a");
        Assert.Equal(Col, s.Get(id).Color);
    }

    [Fact]
    public void Snapshot_and_restore_round_trip_and_continue_ids()
    {
        var s = new DrawingStore();
        s.SetDataset("a");
        s.Add(DrawingKinds.Trend(new Anchor(1, 2), new Anchor(3, 4), Col));
        s.Add(DrawingKinds.HLine(5, Col));
        s.SetDataset("b");
        s.Add(DrawingKinds.Marker(new Anchor(9, 9), true, Col));
        var snap = s.Snapshot();
        var s2 = new DrawingStore();
        s2.Restore(snap, "a");
        Assert.Equal(2, s2.Items.Count);
        string next = s2.Add(DrawingKinds.VLine(1, Col));
        Assert.Equal("d4", next); // numbering continues after the highest id
        s2.SetDataset("b");
        Assert.Equal("marker", s2.Items[0].Kind);
    }

    [Fact]
    public void Moving_and_reshaping_anchors()
    {
        var d = DrawingKinds.Trend(new Anchor(10, 100), new Anchor(20, 200), Col);
        DrawingKinds.Translate(d, 5, -10);
        Assert.Equal(new Anchor(15, 90), d.GetAnchor("a").Value);
        Assert.Equal(new Anchor(25, 190), d.GetAnchor("b").Value);
        DrawingKinds.SetAnchorAt(d, 1, new Anchor(40, 300));
        Assert.Equal(new Anchor(40, 300), d.GetAnchor("b").Value);
        var h = DrawingKinds.HLine(50, Col);
        DrawingKinds.Translate(h, 99, 5);
        Assert.Equal(55, h.Num("price"));
        Assert.Empty(h.AnchorKeys());
        Assert.Single(DrawingKinds.Text(new Anchor(1, 1), "x", Col).AnchorKeys());
    }

    [Fact]
    public void Position_boxes_drag_and_flip_like_the_web_version()
    {
        var d = DrawingKinds.Position(true, new Anchor(100, 1000), 200, 1040, 980, Col);
        DrawingKinds.DragPositionHandle(d, 1, new Anchor(300, 1060)); // the target: price moves, time is shared by target and stop
        Assert.Equal(new Anchor(300, 1060), d.GetAnchor("b").Value);
        Assert.Equal(new Anchor(300, 980), d.GetAnchor("c").Value);
        DrawingKinds.DragPositionHandle(d, 2, new Anchor(50, 970)); // never narrower than the entry's time
        Assert.Equal(100, d.GetAnchor("c").Value.Time);
        DrawingKinds.DragPositionHandle(d, 0, new Anchor(150, 1010)); // the entry moves the whole box
        Assert.Equal(new Anchor(150, 1010), d.GetAnchor("a").Value);
        Assert.Equal(1070, d.GetAnchor("b").Value.Price);
        DrawingKinds.FlipPosition(d);
        Assert.Equal("short", d.Str("side"));
        Assert.Equal(2 * 1010 - 1070, d.GetAnchor("b").Value.Price);
    }
}

public class DrawingSaveTests
{
    [Fact]
    public void Drawings_survive_a_save_including_kinds_this_version_cannot_draw()
    {
        var w = W.Rising();
        var s = w.Trading.Drawings;
        s.Add(DrawingKinds.HLine(110500, "#f2c25b"));
        s.Add(DrawingKinds.Trend(new Anchor(1704153600000, 110100), new Anchor(1704157200000, 110400), "#2fcfae"));
        // a kind the Unity chart cannot draw yet, with fields of its own: it must come back untouched
        var odd = DrawingKinds.Make("pitchfork", "#c792ea", ("a", new Anchor(1, 1)), ("b", new Anchor(2, 2)), ("c", new Anchor(3, 3)));
        odd.Data.Set("width", 3.0); odd.Data.Set("dash", "dashed"); odd.Data.Set("name", "my fork");
        s.Add(odd);
        s.Add(DrawingKinds.Position(false, new Anchor(5, 100), 9, 90, 110, "#f2686b"));

        w.Clock.SetSpeed(0); // a loaded game starts paused, so compare at the same speed
        string text = new SaveManager(w, new MemorySaveStorage()).ExportText();
        var w2 = GameWorld.Create();
        Assert.True(new SaveManager(w2, new MemorySaveStorage()).Import(text).Ok);
        Assert.Equal(4, w2.Trading.Drawings.Items.Count);
        var back = w2.Trading.Drawings.Items.First(d => d.Kind == "pitchfork");
        Assert.Equal("my fork", back.Name);
        Assert.Equal("dashed", back.Dash);
        Assert.Equal(3.0, back.Width);
        Assert.Equal(new Anchor(2, 2), back.GetAnchor("b").Value);
        Assert.Equal(Json.Serialize(w.Trading.Drawings.Items.First(d => d.Kind == "position").Data), Json.Serialize(w2.Trading.Drawings.Items.First(d => d.Kind == "position").Data));
        // and they are saved back identically
        Assert.Equal(text, new SaveManager(w2, new MemorySaveStorage(), () => DateTimeOffset.Parse(Json.Parse(text).Get("savedAt").String)).ExportText());
    }

    [Fact]
    public void Drawings_belong_to_their_dataset_and_return_with_it()
    {
        var storage = new MemorySaveStorage();
        var w = W.Rising();
        w.Trading.Drawings.Add(DrawingKinds.HLine(110000, "#fff"));
        var other = Enumerable.Range(0, 300).Select(i => new Candle(Utc(2024, 2, 1) + i * 60_000L, 120000 + i, 120000 + i + 5, 120000 + i - 5, 120000 + i + 2)).ToList();
        w.Trading.LoadDataset(other, "other.csv", false);
        Assert.Empty(w.Trading.Drawings.Items);
        w.Trading.Drawings.Add(DrawingKinds.VLine(5, "#fff"));
        Assert.True(new SaveManager(w, storage).Save());
        var w2 = GameWorld.Create();
        Assert.True(new SaveManager(w2, storage).Load().Ok);
        Assert.Single(w2.Trading.Drawings.Items); // the active dataset's one
        Assert.Equal("vline", w2.Trading.Drawings.Items[0].Kind);
        Assert.Equal(2, w2.Trading.Drawings.Snapshot().Count); // the other dataset's drawing is still kept
    }

    [Fact]
    public void A_save_with_broken_drawings_is_refused_with_a_clear_message()
    {
        var w = W.Rising();
        w.Trading.Drawings.Add(DrawingKinds.HLine(1, "#fff"));
        var doc = Json.Parse(new SaveManager(w, new MemorySaveStorage()).ExportText());
        var items = doc.Get("trading").Get("drawings").AsArray()[0].Get("items").AsArray();
        items.Add(items[0]); // the same id twice
        Assert.Contains("duplicate drawing ids", new SaveManager(GameWorld.Create(), new MemorySaveStorage()).Import(Json.Serialize(doc)).Error);
        var doc2 = Json.Parse(new SaveManager(w, new MemorySaveStorage()).ExportText());
        doc2.Get("trading").Get("drawings").AsArray()[0].Get("items").AsArray()[0].Members.RemoveAll(m => m.Key == "kind");
        Assert.Contains("no id or kind", new SaveManager(GameWorld.Create(), new MemorySaveStorage()).Import(Json.Serialize(doc2)).Error);
    }
}
