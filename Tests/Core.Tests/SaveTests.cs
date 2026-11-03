using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using Xunit;
using static H;

public class JsonTests
{
    [Fact]
    public void Round_trips_values_and_escapes()
    {
        var doc = JsonValue.Obj().Set("s", "a \"quoted\"\nline\t\u0001 \u00e9").Set("n", 12345678901234L).Set("d", 1.25).Set("b", true).Set("z", JsonValue.Null)
            .Set("a", JsonValue.Arr().Add(JsonValue.Of(1L)).Add(JsonValue.Obj()));
        var back = Json.Parse(Json.Serialize(doc));
        Assert.Equal("a \"quoted\"\nline\t\u0001 \u00e9", back.Get("s").String);
        Assert.Equal(12345678901234L, back.Get("n").AsLong());
        Assert.Equal(1.25, back.Get("d").AsDouble());
        Assert.True(back.Get("b").Bool);
        Assert.True(back.Get("z").IsNull);
        Assert.Equal(2, back.Get("a").AsArray().Count);
        Assert.Equal(Json.Serialize(doc), Json.Serialize(Json.Parse(Json.Serialize(doc, true))));
    }

    [Fact]
    public void Rejects_bad_input_without_crashing()
    {
        foreach (var bad in new[] { "", "{", "[1,", "{\"a\":}", "{\"a\" 1}", "nul", "\"abc", "[1 2]", "{} x", "[\"\\q\"]" })
            Assert.Throws<JsonException>(() => Json.Parse(bad));
        Assert.Throws<JsonException>(() => Json.Parse(new string('[', 200) + new string(']', 200))); // nesting limit
        Assert.Throws<JsonException>(() => JsonValue.Of(1.5).AsLong());
    }
}

public class SaveTests
{
    static GameWorld PlayedWorld()
    {
        var w = W.Rising();
        W.Profit(w);
        w.Trading.Journal.Set(JournalStore.KeyOf(w.Trading.Account.Closed[0]), note: "textbook", tags: new[] { "Patient" }, rating: 4);
        Assert.True(w.Transfers.Transfer("t1", D(9500)).Ok);
        w.City.StartPlacement("house");
        Assert.True(w.City.TryPlace(new Cell(11, 11)));
        w.City.StartPlacement("park");
        Assert.True(w.City.TryPlace(new Cell(13, 11)));
        C.RunDays(w.Clock, 40);
        w.City.SetPolicy("marketing", true);
        w.Trading.QuickOrder(Direction.Sell, 10, 30, 60); // an open trade at save time
        w.Trading.SetTimeframe(5 * 60_000);
        return w;
    }

    static void ExpectSame(GameWorld a, GameWorld b)
    {
        Assert.Equal(a.City.State.Balance, b.City.State.Balance);
        Assert.Equal(a.City.State.Buildings.Select(x => x.Id + x.DefId + x.Level + x.Status + x.LifetimeIncome.Cents), b.City.State.Buildings.Select(x => x.Id + x.DefId + x.Level + x.Status + x.LifetimeIncome.Cents));
        Assert.Equal(a.City.State.Policies.ToArray(), b.City.State.Policies.ToArray());
        Assert.Equal(a.City.State.Unlocked.OrderBy(x => x).ToArray(), b.City.State.Unlocked.OrderBy(x => x).ToArray());
        Assert.Equal(a.Trading.Account.Balance, b.Trading.Account.Balance);
        Assert.Equal(a.Trading.Account.Closed.Count, b.Trading.Account.Closed.Count);
        Assert.Equal(a.Trading.Account.Open.Select(Describe), b.Trading.Account.Open.Select(Describe));
        Assert.Equal(a.Trading.State.RevealedCount, b.Trading.State.RevealedCount);
        Assert.Equal(a.Trading.State.ChartTfMs, b.Trading.State.ChartTfMs);
        Assert.Equal(a.Transfers.History.Count, b.Transfers.History.Count);
        Assert.Equal(a.Progression.Completed.Keys.OrderBy(x => x).ToArray(), b.Progression.Completed.Keys.OrderBy(x => x).ToArray());
        Assert.Equal(a.Achievements.Earned.OrderBy(x => x).ToArray(), b.Achievements.Earned.OrderBy(x => x).ToArray());
        Assert.Equal(a.Trading.Journal.Snapshot().Select(kv => kv.Key + kv.Value.Note + string.Join(",", kv.Value.Tags) + kv.Value.Rating), b.Trading.Journal.Snapshot().Select(kv => kv.Key + kv.Value.Note + string.Join(",", kv.Value.Tags) + kv.Value.Rating));
        Assert.Equal(a.Clock.Time, b.Clock.Time);
        Assert.Equal(b.Trading.Account.ExpectedBalance, b.Trading.Account.Balance);
    }

    [Fact]
    public void Saves_and_loads_the_whole_game_including_an_uploaded_dataset()
    {
        var storage = new MemorySaveStorage();
        var w = PlayedWorld();
        var err = new[] { "" };
        Assert.True(new SaveManager(w, storage).Save(false, err), err[0]);
        Assert.Contains("dataset-upload_", string.Join(",", storage.Items.Keys)); // stored once, apart from the save
        var w2 = GameWorld.Create();
        var outcome = new SaveManager(w2, storage).Load();
        Assert.True(outcome.Ok, outcome.Error);
        ExpectSame(w, w2);
        Assert.Equal(0, w2.Clock.Speed); // a loaded game starts paused
        Assert.Equal(1, w2.Trading.State.Positions.Count);
    }

    [Fact]
    public void A_synthetic_dataset_is_regenerated_from_its_seed_not_stored()
    {
        var storage = new MemorySaveStorage();
        var w = GameWorld.Create();
        w.StartNewGame();
        w.Trading.Step();
        Assert.True(new SaveManager(w, storage).Save());
        Assert.DoesNotContain(storage.Items.Keys, k => k.StartsWith("dataset-"));
        Assert.True(storage.Items["current"].Length < 20_000); // small: no candles inside
        var w2 = GameWorld.Create();
        Assert.True(new SaveManager(w2, storage).Load().Ok);
        Assert.Equal(w.Trading.State.RevealedCount, w2.Trading.State.RevealedCount);
        Assert.Equal(w.Trading.State.Dataset.Id, w2.Trading.State.Dataset.Id);
        Assert.Equal(w.Trading.ChartData().Candles.Last().Close, w2.Trading.ChartData().Candles.Last().Close);
    }

    [Fact]
    public void Export_is_self_contained_and_imports_into_a_fresh_game()
    {
        var w = PlayedWorld();
        string text = new SaveManager(w, new MemorySaveStorage()).ExportText();
        var doc = Json.Parse(text);
        Assert.Equal("trading-empire-save", doc.Get("format").String); // the web version's format
        Assert.Equal(4, doc.Get("version").AsLong());
        Assert.NotNull(doc.Get("trading").Get("dataset").Get("candles")); // embedded
        var w2 = GameWorld.Create();
        var outcome = new SaveManager(w2, new MemorySaveStorage()).Import(text);
        Assert.True(outcome.Ok, outcome.Error);
        ExpectSame(w, w2);
    }

    [Fact]
    public void Editing_a_save_to_create_money_is_refused_and_changes_nothing()
    {
        var w = PlayedWorld();
        string text = new SaveManager(w, new MemorySaveStorage()).ExportText();
        var cheat = Json.Parse(text);
        var treasury = cheat.Get("city").Get("treasury");
        treasury.Set("balance", treasury.Get("balance").AsLong() + 1_000_000);
        var fresh = GameWorld.Create();
        fresh.StartNewGame();
        long before = fresh.Trading.Account.Balance.Cents;
        var outcome = new SaveManager(fresh, new MemorySaveStorage()).Import(Json.Serialize(cheat));
        Assert.False(outcome.Ok);
        Assert.Contains("treasury balance does not match", outcome.Error);
        Assert.Equal(before, fresh.Trading.Account.Balance.Cents);

        var cheat2 = Json.Parse(text);
        var acct = cheat2.Get("trading").Get("account");
        acct.Set("withdrawals", acct.Get("withdrawals").AsLong() + 100);
        Assert.Contains("do not match", new SaveManager(fresh, new MemorySaveStorage()).Import(Json.Serialize(cheat2)).Error);

        var cheat3 = Json.Parse(text);
        cheat3.Get("city").Get("buildings").AsArray()[0].Get("origin").Set("x", 0); // outside your land
        Assert.Contains("outside your land", new SaveManager(fresh, new MemorySaveStorage()).Import(Json.Serialize(cheat3)).Error);
    }

    [Fact]
    public void Rejects_garbage_truncated_files_and_other_formats_with_friendly_errors()
    {
        var m = new SaveManager(GameWorld.Create(), new MemorySaveStorage());
        Assert.Contains("not valid JSON", m.Import("{ not json").Error);
        Assert.Contains("not a Trading Empire save", m.Import("{\"format\":\"other\"}").Error);
        Assert.Contains("not a game save", m.Import("[1,2]").Error);
        var full = new SaveManager(PlayedWorld(), new MemorySaveStorage()).ExportText();
        Assert.False(m.Import(full.Substring(0, full.Length / 2)).Ok);
        var newer = Json.Parse(full); newer.Set("version", 99);
        Assert.Contains("newer version", m.Import(Json.Serialize(newer)).Error);
        var malformed = Json.Parse(full); malformed.Get("city").Set("landTier", "two");
        Assert.Contains("malformed", m.Import(Json.Serialize(malformed)).Error);
    }

    [Fact]
    public void Migrates_older_web_saves()
    {
        var w = PlayedWorld();
        string text = new SaveManager(w, new MemorySaveStorage()).ExportText();
        var old = Json.Parse(text);
        old.Set("version", 3);
        foreach (var b in old.Get("city").Get("buildings").AsArray())
        {
            b.Members.RemoveAll(m => m.Key == "status" || m.Key == "completesAt");
        }
        var w2 = GameWorld.Create();
        var outcome = new SaveManager(w2, new MemorySaveStorage()).Import(Json.Serialize(old));
        Assert.True(outcome.Ok, outcome.Error);
        Assert.All(w2.City.State.Buildings, b => Assert.Equal(BuildStatus.Complete, b.Status));

        var v2 = Json.Parse(text);
        v2.Set("version", 2);
        foreach (var t in v2.Get("transfers").Get("history").AsArray()) t.Members.RemoveAll(m => m.Key == "direction");
        Assert.True(new SaveManager(GameWorld.Create(), new MemorySaveStorage()).Import(Json.Serialize(v2)).Ok);
    }

    [Fact]
    public void A_damaged_newest_save_falls_back_to_the_backup_and_says_so()
    {
        var storage = new MemorySaveStorage();
        var w = PlayedWorld();
        var m = new SaveManager(w, storage);
        Assert.True(m.Save());
        w.Trading.Step();
        Assert.True(m.Save()); // the first save becomes the backup
        storage.Items["current"] = storage.Items["current"].Replace("\"checksum\":\"", "\"checksum\":\"0");
        var w2 = GameWorld.Create();
        var outcome = new SaveManager(w2, storage).Load();
        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("previous one was loaded", outcome.Notice);
        // a damaged save is parked, never overwritten, on the next save
        w2.Trading.Step();
        Assert.True(new SaveManager(w2, storage).Save());
        Assert.True(storage.Items.ContainsKey("corrupt"));
        Assert.Equal("There is no saved game yet.", new SaveManager(GameWorld.Create(), new MemorySaveStorage()).Load().Error);
    }

    [Fact]
    public void Autosave_writes_only_when_something_changed()
    {
        var storage = new MemorySaveStorage();
        var w = W.Rising();
        var m = new SaveManager(w, storage);
        Assert.True(m.Save(true));
        string first = storage.Items["current"];
        storage.Items["marker"] = "x";
        Assert.True(m.Save(true));
        Assert.Equal(first, storage.Items["current"]); // unchanged: not rewritten
        Assert.False(storage.Items.ContainsKey("backup"));
        w.Trading.Step();
        Assert.True(m.Save(true));
        Assert.NotEqual(first, storage.Items["current"]);
    }

    [Fact]
    public void Keeps_one_dated_backup_per_day_for_the_newest_five_days()
    {
        var storage = new MemorySaveStorage();
        var w = W.Rising();
        var day = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var m = new SaveManager(w, storage, () => day);
        for (int i = 0; i < 7; i++)
        {
            day = new DateTimeOffset(2026, 3, 1 + i, 12, 0, 0, TimeSpan.Zero);
            w.Trading.Step();
            Assert.True(m.Save());
            Assert.True(m.Save()); // the same day again: still one backup for it
        }
        Assert.Equal(new[] { "2026-03-07", "2026-03-06", "2026-03-05", "2026-03-04", "2026-03-03" }, m.ListBackups().ToArray());
        Assert.False(storage.Items.ContainsKey("daily-2026-03-01"));
        int revealed = w.Trading.State.RevealedCount;
        var w2 = GameWorld.Create();
        Assert.True(new SaveManager(w2, storage).RestoreBackup("2026-03-04").Ok);
        Assert.True(w2.Trading.State.RevealedCount < revealed);
        Assert.False(new SaveManager(w2, storage).RestoreBackup("2026-03-01").Ok);
    }

    [Fact]
    public void File_storage_writes_atomically_and_survives_odd_keys()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "te-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fs = new FileSaveStorage(dir);
            Assert.Null(fs.Read("current"));
            fs.Write("current", "héllo ✓");
            Assert.Equal("héllo ✓", fs.Read("current"));
            fs.Write("current", "second");
            Assert.Equal("second", fs.Read("current"));
            fs.Write("dataset-upload:abc/../x", "ok"); // the key is made safe as a file name
            Assert.Equal("ok", fs.Read("dataset-upload:abc/../x"));
            Assert.Empty(System.IO.Directory.GetFiles(dir, "*.tmp"));
            var w = PlayedWorld();
            Assert.True(new SaveManager(w, fs).Save());
            var w2 = GameWorld.Create();
            Assert.True(new SaveManager(w2, fs).Load().Ok);
            fs.Delete("current");
            Assert.Null(fs.Read("current"));
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }
}
