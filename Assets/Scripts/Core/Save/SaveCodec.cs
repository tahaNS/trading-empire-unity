using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>The decoded, validated content of a save file.</summary>
    public sealed class SaveData
    {
        public string SavedAt;
        // city
        public long ClockTime;
        public int ClockSpeed;
        public Money CityBalance;
        public List<TransactionRecord> CityTransactions = new List<TransactionRecord>();
        public List<BuildingState> Buildings = new List<BuildingState>();
        public List<string> Unlocked = new List<string>();
        public int LandTier;
        public List<string> Policies = new List<string>();
        public List<CityEvent> Events = new List<CityEvent>();
        public uint? EventSeed;
        // trading
        public AccountSnapshot Account;
        public int RunId, ReplayRevealed, ReplaySpeed;
        public long? ChartTfMs;
        public Dictionary<string, JournalEntry> Journal = new Dictionary<string, JournalEntry>();
        /// <summary>Chart drawings per dataset id, kept exactly as saved.</summary>
        public List<KeyValuePair<string, List<Drawing>>> Drawings = new List<KeyValuePair<string, List<Drawing>>>();
        public List<IndicatorConfig> Indicators = new List<IndicatorConfig>();
        public DatasetInfo Dataset;
        public long? DatasetTimeframeMs;
        public int? DatasetCount;
        /// <summary>Embedded candles of an uploaded dataset (exported files), else null.</summary>
        public List<Candle> Candles;
        // the rest
        public List<TransferRecord> Transfers = new List<TransferRecord>();
        public List<string> Achievements = new List<string>();
        public Dictionary<string, ChallengeResult> Challenges = new Dictionary<string, ChallengeResult>();
        public Dictionary<string, long> Completed = new Dictionary<string, long>();
        public Money PeakTreasury;
    }

    public sealed class ParsedSave
    {
        public bool Ok;
        public SaveData Data;
        public string Error;
    }

    /// <summary>
    /// Reads and writes the save format. It is the web version's format (version 4), so a save exported from the web game loads here
    /// and the other way round; chart drawings and indicators are saved and loaded as the web version stores them.
    /// </summary>
    public static class SaveCodec
    {
        public const string Format = "trading-empire-save";
        public const int CurrentVersion = 4;

        // ---------------------------------------------------------------- enums as strings

        static string Dir(Direction d) { return d == Direction.Buy ? "buy" : "sell"; }
        static Direction DirOf(string s) { if (s == "buy") return Direction.Buy; if (s == "sell") return Direction.Sell; throw new JsonException("Bad direction \"" + s + "\""); }

        static readonly string[] TxKinds = { "initial-funding", "transfer", "transfer-out", "construction", "upgrade", "demolition", "land", "income", "expense" };
        static readonly string[] AccKinds = { "initial", "deposit", "withdrawal", "trade-close" };
        static readonly string[] Reasons = { "manual", "stop-loss", "take-profit", "stop-out" };
        static readonly string[] EventKinds = { "fire", "storm", "festival" };

        static string Name(int index, string[] names) { return names[index]; }
        static int Index(string s, string[] names, string what)
        {
            int i = Array.IndexOf(names, s);
            if (i < 0) throw new JsonException("Unknown " + what + " \"" + s + "\"");
            return i;
        }

        // ---------------------------------------------------------------- capture

        static JsonValue NullableLong(long? v) { return v.HasValue ? JsonValue.Of(v.Value) : JsonValue.Null; }

        static JsonValue PositionJson(Position p)
        {
            var o = JsonValue.Obj()
                .Set("id", p.Id).Set("symbol", p.Symbol).Set("direction", Dir(p.Direction)).Set("lots", p.Lots).Set("entryPrice", p.EntryPrice)
                .Set("entryTime", p.EntryTime).Set("entryCandleIndex", p.EntryCandleIndex).Set("runId", p.RunId)
                .Set("stopLoss", NullableLong(p.StopLoss)).Set("takeProfit", NullableLong(p.TakeProfit))
                .Set("margin", p.Margin.Cents).Set("commission", p.Commission.Cents).Set("initialRisk", NullableLong(p.InitialRisk.HasValue ? p.InitialRisk.Value.Cents : (long?)null))
                .Set("orderType", p.OrderType == EntryClass.Market ? "market" : p.OrderType == EntryClass.Limit ? "limit" : "stop")
                .Set("status", p.IsOpen ? "open" : "closed");
            if (p.Exit != null)
                o.Set("exit", JsonValue.Obj().Set("price", p.Exit.Price).Set("time", p.Exit.Time).Set("candleIndex", p.Exit.CandleIndex)
                    .Set("reason", Name((int)p.Exit.Reason, Reasons)).Set("gross", p.Exit.Gross.Cents).Set("net", p.Exit.Net.Cents));
            return o;
        }

        /// <summary>Snapshot the world into a JSON document. <paramref name="embedDataset"/> adds uploaded candles so an exported file is self-contained.</summary>
        public static JsonValue Capture(GameWorld world, DateTimeOffset savedAt, bool embedDataset)
        {
            var city = world.City.State;
            var st = world.Trading.SaveState();
            var ds = st.dataset;
            if (ds == null) throw new InvalidOperationException("Cannot save before a dataset is loaded");
            var account = world.Trading.Account.Snapshot();

            var buildings = JsonValue.Arr();
            foreach (var b in city.Buildings)
                buildings.Add(JsonValue.Obj().Set("id", b.Id).Set("defId", b.DefId).Set("origin", JsonValue.Obj().Set("x", b.Origin.X).Set("y", b.Origin.Y))
                    .Set("rotation", b.Rotation).Set("level", b.Level).Set("purchaseCost", b.PurchaseCost.Cents).Set("builtAt", b.BuiltAt)
                    .Set("status", b.Status == BuildStatus.Building ? "building" : "complete").Set("completesAt", b.CompletesAt).Set("nextPayoutAt", b.NextPayoutAt)
                    .Set("lifetimeIncome", b.LifetimeIncome.Cents).Set("lifetimeExpense", b.LifetimeExpense.Cents));
            var cityTx = JsonValue.Arr();
            foreach (var t in city.Transactions)
                cityTx.Add(JsonValue.Obj().Set("id", t.Id).Set("time", t.Time).Set("kind", Name((int)t.Kind, TxKinds)).Set("amount", t.Amount.Cents).Set("description", t.Description)
                    .Set("buildingId", t.BuildingId != null ? JsonValue.Of(t.BuildingId) : null).Set("balanceAfter", t.BalanceAfter.Cents));
            var events = JsonValue.Arr();
            foreach (var e in city.Events)
                events.Add(JsonValue.Obj().Set("seq", e.Seq).Set("kind", Name((int)e.Kind, EventKinds)).Set("startsAt", e.StartsAt).Set("endsAt", e.EndsAt)
                    .Set("buildingId", e.BuildingId != null ? JsonValue.Of(e.BuildingId) : null));
            var unlocked = JsonValue.Arr(); foreach (var u in city.Unlocked) unlocked.Add(JsonValue.Of(u));
            var policies = JsonValue.Arr(); foreach (var p in city.Policies) policies.Add(JsonValue.Of(p));

            var accTx = JsonValue.Arr();
            foreach (var t in account.Transactions)
                accTx.Add(JsonValue.Obj().Set("id", t.Id).Set("time", t.Time).Set("kind", Name((int)t.Kind, AccKinds)).Set("amount", t.Amount.Cents).Set("description", t.Description).Set("balanceAfter", t.BalanceAfter.Cents));
            var open = JsonValue.Arr(); foreach (var p in account.Open) open.Add(PositionJson(p));
            var closed = JsonValue.Arr(); foreach (var p in account.Closed) closed.Add(PositionJson(p));
            var pending = JsonValue.Arr();
            foreach (var o in account.Pending)
                pending.Add(JsonValue.Obj().Set("id", o.Id).Set("symbol", o.Symbol).Set("direction", Dir(o.Direction)).Set("kind", o.Kind == PendingKind.Limit ? "limit" : "stop")
                    .Set("lots", o.Lots).Set("price", o.Price).Set("stopLoss", NullableLong(o.StopLoss)).Set("takeProfit", NullableLong(o.TakeProfit))
                    .Set("createdTime", o.CreatedTime).Set("createdCandleIndex", o.CreatedCandleIndex).Set("runId", o.RunId));

            var journal = JsonValue.Obj();
            foreach (var kv in world.Trading.Journal.Entries.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var tags = JsonValue.Arr(); foreach (var t in kv.Value.Tags) tags.Add(JsonValue.Of(t));
                var e = JsonValue.Obj().Set("note", kv.Value.Note).Set("tags", tags);
                if (kv.Value.Rating > 0) e.Set("rating", kv.Value.Rating);
                journal.Set(kv.Key, e);
            }

            var dataset = JsonValue.Obj().Set("id", ds.Id).Set("name", ds.Name).Set("synthetic", ds.Synthetic);
            if (!string.IsNullOrEmpty(ds.Symbol)) dataset.Set("symbol", ds.Symbol);
            if (ds.Synthetic) dataset.Set("seed", (long)(ds.Seed ?? 0)).Set("count", ds.Total).Set("timeframeMs", ds.TimeframeMs);
            else if (embedDataset) dataset.Set("candles", EncodeCandles(world.Trading.DatasetCandles()));

            var transfers = JsonValue.Arr();
            foreach (var t in world.Transfers.History)
                transfers.Add(JsonValue.Obj().Set("id", t.Id).Set("requestId", t.RequestId).Set("time", t.Time).Set("amount", t.Amount.Cents)
                    .Set("direction", t.Direction == TransferDirection.ToCity ? "to-city" : "to-trading").Set("tradingBalanceAfter", t.TradingBalanceAfter.Cents).Set("treasuryBalanceAfter", t.TreasuryBalanceAfter.Cents));

            var challenges = JsonValue.Obj();
            foreach (var kv in world.Challenges.Results.OrderBy(k => k.Key, StringComparer.Ordinal))
                challenges.Set(kv.Key, JsonValue.Obj().Set("stars", kv.Value.Stars).Set("bestReturnPct", kv.Value.BestReturnPct).Set("wins", kv.Value.Wins).Set("attempts", kv.Value.Attempts));
            var ach = JsonValue.Arr(); foreach (var a in world.Achievements.Earned) ach.Add(JsonValue.Of(a));
            var completed = JsonValue.Obj(); foreach (var kv in world.Progression.Completed.OrderBy(k => k.Key, StringComparer.Ordinal)) completed.Set(kv.Key, kv.Value);

            var tradingObj = JsonValue.Obj()
                .Set("account", JsonValue.Obj().Set("startingBalance", account.StartingBalance.Cents).Set("balance", account.Balance.Cents).Set("realizedPnl", account.RealizedPnl.Cents)
                    .Set("deposits", account.Deposits.Cents).Set("withdrawals", account.Withdrawals.Cents).Set("transactions", accTx).Set("open", open).Set("closed", closed))
                .Set("runId", st.runId).Set("pending", pending).Set("drawings", DrawingsJson(world.Trading.Drawings)).Set("indicators", IndicatorsJson(world.Trading.Indicators)).Set("journal", journal).Set("chartTfMs", st.chartTfMs)
                .Set("replay", JsonValue.Obj().Set("revealed", st.revealed).Set("speed", st.speed)).Set("dataset", dataset);

            return JsonValue.Obj()
                .Set("format", Format).Set("version", CurrentVersion).Set("savedAt", savedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z")
                .Set("city", JsonValue.Obj()
                    .Set("clock", JsonValue.Obj().Set("time", world.Clock.Time).Set("speed", world.Clock.Speed))
                    .Set("treasury", JsonValue.Obj().Set("balance", city.Balance.Cents).Set("transactions", cityTx))
                    .Set("buildings", buildings).Set("unlocked", unlocked).Set("landTier", city.LandTier).Set("policies", policies).Set("events", events)
                    .Set("eventSeed", (long)world.City.EventSeed))
                .Set("trading", tradingObj)
                .Set("transfers", JsonValue.Obj().Set("history", transfers))
                .Set("achievements", ach).Set("challenges", challenges)
                .Set("progression", JsonValue.Obj().Set("completed", completed).Set("peakTreasury", world.Progression.PeakTreasury.Cents))
                .Set("settings", JsonValue.Obj());
        }

        static JsonValue IndicatorsJson(IndicatorStore store)
        {
            var arr = JsonValue.Arr();
            foreach (var c in store.Items)
            {
                var ps = JsonValue.Obj(); foreach (var kv in c.Params) ps.Set(kv.Key, kv.Value);
                var o = JsonValue.Obj().Set("id", c.Id).Set("type", c.Type).Set("params", ps);
                if (c.Colors.Count > 0) { var cs = JsonValue.Obj(); foreach (var kv in c.Colors) cs.Set(kv.Key, kv.Value); o.Set("colors", cs); }
                if (c.Width.HasValue) o.Set("width", c.Width.Value);
                if (c.Hidden) o.Set("hidden", true);
                arr.Add(o);
            }
            return arr;
        }

        static void DecodeIndicators(JsonValue arr, List<IndicatorConfig> into)
        {
            if (arr == null || arr.Kind != JsonKind.Array) return;
            foreach (var item in arr.Array)
            {
                if (item.Kind != JsonKind.Object || item.Get("id") == null || item.Get("id").Kind != JsonKind.String || item.Get("type") == null || item.Get("type").Kind != JsonKind.String) continue;
                var c = new IndicatorConfig { Id = item.Get("id").String, Type = item.Get("type").String };
                var ps = item.Get("params");
                if (ps != null && ps.Kind == JsonKind.Object) foreach (var kv in ps.Members) if (kv.Value.Kind == JsonKind.Number) c.Params[kv.Key] = kv.Value.Number;
                var cs = item.Get("colors");
                if (cs != null && cs.Kind == JsonKind.Object) foreach (var kv in cs.Members) if (kv.Value.Kind == JsonKind.String) c.Colors[kv.Key] = kv.Value.String;
                var w = item.Get("width"); if (w != null && w.Kind == JsonKind.Number) c.Width = w.Number;
                var h = item.Get("hidden"); if (h != null && h.Kind == JsonKind.Bool) c.Hidden = h.Bool;
                into.Add(c);
            }
        }

        static JsonValue DrawingsJson(DrawingStore store)
        {
            var arr = JsonValue.Arr();
            foreach (var g in store.Snapshot())
            {
                var items = JsonValue.Arr();
                foreach (var d in g.Value) items.Add(d.Data);
                arr.Add(JsonValue.Obj().Set("datasetId", g.Key).Set("items", items));
            }
            return arr;
        }

        /// <summary>[time, open, high, low, close, volume | null, spreadPts | null]</summary>
        public static JsonValue EncodeCandles(IReadOnlyList<Candle> candles)
        {
            var arr = JsonValue.Arr();
            foreach (var c in candles)
                arr.Add(JsonValue.Arr().Add(JsonValue.Of(c.Time)).Add(JsonValue.Of(c.Open)).Add(JsonValue.Of(c.High)).Add(JsonValue.Of(c.Low)).Add(JsonValue.Of(c.Close))
                    .Add(c.Volume.HasValue ? JsonValue.Of(c.Volume.Value) : JsonValue.Null).Add(c.SpreadPts.HasValue ? JsonValue.Of(c.SpreadPts.Value) : JsonValue.Null));
            return arr;
        }

        /// <summary>Decode stored candle tuples, rejecting anything that isn't valid ascending OHLC data (null when invalid).</summary>
        public static List<Candle> DecodeCandles(JsonValue tuples)
        {
            var output = new List<Candle>();
            long prev = long.MinValue;
            foreach (var t in tuples.AsArray())
            {
                var a = t.AsArray();
                if (a.Count < 7) return null;
                long time = a[0].AsLong(), o = a[1].AsLong(), h = a[2].AsLong(), l = a[3].AsLong(), c = a[4].AsLong();
                if (time <= prev || o <= 0 || h < Math.Max(o, c) || l > Math.Min(o, c) || l <= 0) return null;
                prev = time;
                var candle = new Candle(time, o, h, l, c);
                if (!a[5].IsNull) candle.Volume = a[5].AsDouble();
                if (!a[6].IsNull) candle.SpreadPts = a[6].AsLong();
                output.Add(candle);
            }
            return output;
        }

        // ---------------------------------------------------------------- migrations (older web saves)

        /// <summary>Upgrade an older save to the current version in place. Returns an error text or null.</summary>
        static string Migrate(JsonValue root)
        {
            var ver = root.Get("version");
            if (ver == null || ver.Kind != JsonKind.Number) return "The save has no version.";
            long v = (long)ver.Number;
            if (v > CurrentVersion) return "This save is from a newer version of the game (" + v + ").";
            if (v < 1) return "The save version is not valid.";
            for (; v < CurrentVersion; v++)
            {
                if (v == 1)
                {
                    // v1 -> v2: pending orders, drawings and chart timeframe were added; old synthetic datasets were always M15.
                    var trading = root.Get("trading");
                    var dataset = trading != null ? trading.Get("dataset") : null;
                    if (dataset != null && dataset.Get("synthetic") != null && dataset.Get("synthetic").Kind == JsonKind.Bool && dataset.Get("synthetic").Bool && dataset.Get("timeframeMs") == null) dataset.Set("timeframeMs", 15 * 60000L);
                    if (trading != null && trading.Get("pending") == null) trading.Set("pending", JsonValue.Arr());
                    if (trading != null && trading.Get("drawings") == null) trading.Set("drawings", JsonValue.Arr());
                }
                else if (v == 2)
                {
                    // v2 -> v3: every transfer records its direction; all earlier ones went to the city.
                    var h = root.Get("transfers") != null ? root.Get("transfers").Get("history") : null;
                    if (h != null && h.Kind == JsonKind.Array) foreach (var t in h.Array) if (t.Get("direction") == null) t.Set("direction", "to-city");
                }
                else if (v == 3)
                {
                    // v3 -> v4: buildings take time to construct; every earlier building was built instantly.
                    var bs = root.Get("city") != null ? root.Get("city").Get("buildings") : null;
                    if (bs != null && bs.Kind == JsonKind.Array)
                        foreach (var b in bs.Array)
                        {
                            if (b.Get("status") == null) b.Set("status", "complete");
                            if (b.Get("completesAt") == null && b.Get("builtAt") != null) b.Set("completesAt", b.Get("builtAt"));
                        }
                }
            }
            root.Set("version", CurrentVersion);
            return null;
        }

        // ---------------------------------------------------------------- decode

        static JsonValue Req(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null) throw new JsonException("missing \"" + key + "\"");
            return v;
        }

        static long L(JsonValue o, string key)
        {
            try { return Req(o, key).AsLong(); } catch (JsonException e) { throw new JsonException("\"" + key + "\": " + e.Message); }
        }

        static long? NL(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.IsNull) return null;
            try { return v.AsLong(); } catch (JsonException e) { throw new JsonException("\"" + key + "\": " + e.Message); }
        }

        static string S(JsonValue o, string key)
        {
            try { return Req(o, key).AsString(); } catch (JsonException e) { throw new JsonException("\"" + key + "\": " + e.Message); }
        }

        static List<JsonValue> A(JsonValue o, string key)
        {
            try { return Req(o, key).AsArray(); } catch (JsonException e) { throw new JsonException("\"" + key + "\": " + e.Message); }
        }

        static JsonValue O(JsonValue o, string key)
        {
            var v = Req(o, key);
            if (v.Kind != JsonKind.Object) throw new JsonException("\"" + key + "\": expected an object");
            return v;
        }

        static Position DecodePosition(JsonValue p)
        {
            var pos = new Position
            {
                Id = S(p, "id"), Symbol = S(p, "symbol"), Direction = DirOf(S(p, "direction")), Lots = L(p, "lots"), EntryPrice = L(p, "entryPrice"), EntryTime = L(p, "entryTime"),
                EntryCandleIndex = (int)L(p, "entryCandleIndex"), RunId = (int)L(p, "runId"), StopLoss = NL(p, "stopLoss"), TakeProfit = NL(p, "takeProfit"),
                Margin = Money.FromCents(L(p, "margin")), Commission = Money.FromCents(L(p, "commission")),
            };
            var risk = NL(p, "initialRisk");
            pos.InitialRisk = risk.HasValue ? Money.FromCents(risk.Value) : (Money?)null;
            string ot = p.Get("orderType") != null ? S(p, "orderType") : "market";
            pos.OrderType = ot == "limit" ? EntryClass.Limit : ot == "stop" ? EntryClass.Stop : EntryClass.Market;
            string status = S(p, "status");
            if (status != "open" && status != "closed") throw new JsonException("a trade has an invalid status");
            pos.IsOpen = status == "open";
            var ex = p.Get("exit");
            if (ex != null && !ex.IsNull)
                pos.Exit = new PositionExit
                {
                    Price = L(ex, "price"), Time = L(ex, "time"), CandleIndex = (int)L(ex, "candleIndex"), Reason = (CloseReason)Index(S(ex, "reason"), Reasons, "close reason"),
                    Gross = Money.FromCents(L(ex, "gross")), Net = Money.FromCents(L(ex, "net")),
                };
            return pos;
        }

        /// <summary>Text -> JSON -> migrated -> decoded. Never throws.</summary>
        public static ParsedSave Parse(string text)
        {
            JsonValue root;
            try { root = Json.Parse(text); }
            catch (JsonException) { return Fail("The save is not valid JSON (it may be truncated or corrupted)."); }
            if (root.Kind != JsonKind.Object) return Fail("The save is not a game save.");
            if (root.Get("format") == null || root.Get("format").Kind != JsonKind.String || root.Get("format").String != Format) return Fail("This file is not a Trading Empire save.");
            var migrated = Migrate(root);
            if (migrated != null) return Fail(migrated);
            try { return new ParsedSave { Ok = true, Data = Decode(root) }; }
            catch (JsonException e) { return Fail("The save is malformed: " + e.Message + "."); }
            catch (OverflowException) { return Fail("The save is malformed (a number is out of range)."); }
        }

        static ParsedSave Fail(string error) { return new ParsedSave { Ok = false, Error = error }; }

        static SaveData Decode(JsonValue root)
        {
            var d = new SaveData { SavedAt = root.Get("savedAt") != null ? S(root, "savedAt") : "" };
            var city = O(root, "city");
            var clock = O(city, "clock");
            d.ClockTime = L(clock, "time");
            d.ClockSpeed = (int)L(clock, "speed");
            if (Array.IndexOf(GameClock.Speeds, d.ClockSpeed) < 0) throw new JsonException("\"clock.speed\" is not a valid speed");
            var treasury = O(city, "treasury");
            d.CityBalance = Money.FromCents(L(treasury, "balance"));
            foreach (var t in A(treasury, "transactions"))
                d.CityTransactions.Add(new TransactionRecord
                {
                    Id = S(t, "id"), Time = L(t, "time"), Kind = (TransactionKind)Index(S(t, "kind"), TxKinds, "transaction kind"), Amount = Money.FromCents(L(t, "amount")),
                    Description = S(t, "description"), BuildingId = t.Get("buildingId") != null ? S(t, "buildingId") : null, BalanceAfter = Money.FromCents(L(t, "balanceAfter")),
                });
            foreach (var b in A(city, "buildings"))
            {
                var origin = O(b, "origin");
                int rot = (int)L(b, "rotation");
                if (rot < 0 || rot > 3) throw new JsonException("a building has an invalid rotation");
                string status = S(b, "status");
                if (status != "building" && status != "complete") throw new JsonException("a building has an invalid status");
                d.Buildings.Add(new BuildingState
                {
                    Id = S(b, "id"), DefId = S(b, "defId"), Origin = new Cell((int)L(origin, "x"), (int)L(origin, "y")), Rotation = rot, Level = (int)L(b, "level"),
                    PurchaseCost = Money.FromCents(L(b, "purchaseCost")), BuiltAt = L(b, "builtAt"), Status = status == "building" ? BuildStatus.Building : BuildStatus.Complete,
                    CompletesAt = L(b, "completesAt"), NextPayoutAt = L(b, "nextPayoutAt"), LifetimeIncome = Money.FromCents(L(b, "lifetimeIncome")), LifetimeExpense = Money.FromCents(L(b, "lifetimeExpense")),
                });
            }
            foreach (var u in A(city, "unlocked")) d.Unlocked.Add(u.AsString());
            d.LandTier = (int)L(city, "landTier");
            if (d.LandTier < 0) throw new JsonException("\"landTier\" is negative");
            if (city.Get("policies") != null) foreach (var p in A(city, "policies")) d.Policies.Add(p.AsString());
            if (city.Get("events") != null)
                foreach (var e in A(city, "events"))
                    d.Events.Add(new CityEvent { Seq = (int)L(e, "seq"), Kind = (EventKind)Index(S(e, "kind"), EventKinds, "event kind"), StartsAt = L(e, "startsAt"), EndsAt = L(e, "endsAt"), BuildingId = e.Get("buildingId") != null ? S(e, "buildingId") : null });
            var seed = NL(city, "eventSeed");
            if (seed.HasValue) d.EventSeed = (uint)seed.Value;

            var trading = O(root, "trading");
            var acc = O(trading, "account");
            var txs = new List<AccountTransaction>();
            foreach (var t in A(acc, "transactions"))
                txs.Add(new AccountTransaction
                {
                    Id = S(t, "id"), Time = L(t, "time"), Kind = (AccountTransactionKind)Index(S(t, "kind"), AccKinds, "account transaction kind"), Amount = Money.FromCents(L(t, "amount")),
                    Description = S(t, "description"), BalanceAfter = Money.FromCents(L(t, "balanceAfter")),
                });
            var pending = new List<PendingOrder>();
            foreach (var o in A(trading, "pending"))
            {
                string kind = S(o, "kind");
                if (kind != "limit" && kind != "stop") throw new JsonException("an order has an invalid kind");
                pending.Add(new PendingOrder
                {
                    Id = S(o, "id"), Symbol = S(o, "symbol"), Direction = DirOf(S(o, "direction")), Kind = kind == "limit" ? PendingKind.Limit : PendingKind.Stop, Lots = L(o, "lots"), Price = L(o, "price"),
                    StopLoss = NL(o, "stopLoss"), TakeProfit = NL(o, "takeProfit"), CreatedTime = L(o, "createdTime"), CreatedCandleIndex = (int)L(o, "createdCandleIndex"), RunId = (int)L(o, "runId"),
                });
            }
            d.Account = new AccountSnapshot
            {
                StartingBalance = Money.FromCents(L(acc, "startingBalance")), Balance = Money.FromCents(L(acc, "balance")), RealizedPnl = Money.FromCents(L(acc, "realizedPnl")),
                Deposits = Money.FromCents(L(acc, "deposits")), Withdrawals = Money.FromCents(L(acc, "withdrawals")), Transactions = txs,
                Open = A(acc, "open").Select(DecodePosition).ToList(), Closed = A(acc, "closed").Select(DecodePosition).ToList(), Pending = pending,
            };
            d.RunId = (int)L(trading, "runId");
            var replay = O(trading, "replay");
            d.ReplayRevealed = (int)L(replay, "revealed");
            d.ReplaySpeed = (int)L(replay, "speed");
            if (Array.IndexOf(MarketReplay.Speeds, d.ReplaySpeed) < 0) throw new JsonException("\"replay.speed\" is not a valid speed");
            d.ChartTfMs = NL(trading, "chartTfMs");
            var drawings = trading.Get("drawings");
            if (drawings != null && drawings.Kind == JsonKind.Array)
                foreach (var g in drawings.Array)
                {
                    var items = new List<Drawing>();
                    var seenIds = new HashSet<string>();
                    foreach (var item in A(g, "items"))
                    {
                        if (item.Kind != JsonKind.Object || item.Get("id") == null || item.Get("id").Kind != JsonKind.String || item.Get("kind") == null || item.Get("kind").Kind != JsonKind.String)
                            throw new JsonException("a drawing has no id or kind");
                        if (!seenIds.Add(item.Get("id").String)) throw new JsonException("duplicate drawing ids");
                        items.Add(new Drawing(item));
                    }
                    d.Drawings.Add(new KeyValuePair<string, List<Drawing>>(S(g, "datasetId"), items));
                }
            DecodeIndicators(trading.Get("indicators"), d.Indicators);
            var journal = trading.Get("journal");
            if (journal != null && journal.Kind == JsonKind.Object)
                foreach (var kv in journal.Members)
                {
                    var e = new JournalEntry { Note = kv.Value.Get("note") != null ? S(kv.Value, "note") : "" };
                    if (kv.Value.Get("tags") != null) foreach (var t in A(kv.Value, "tags")) e.Tags.Add(t.AsString());
                    if (kv.Value.Get("rating") != null) e.Rating = (int)L(kv.Value, "rating");
                    d.Journal[kv.Key] = e;
                }

            var ds = O(trading, "dataset");
            bool synthetic = Req(ds, "synthetic").AsBool();
            d.Dataset = new DatasetInfo { Id = S(ds, "id"), Name = S(ds, "name"), Synthetic = synthetic, Symbol = ds.Get("symbol") != null ? S(ds, "symbol") : "EURUSD" };
            var dseed = NL(ds, "seed");
            if (dseed.HasValue) d.Dataset.Seed = (uint)dseed.Value;
            var count = NL(ds, "count");
            if (count.HasValue) d.DatasetCount = (int)count.Value;
            d.DatasetTimeframeMs = NL(ds, "timeframeMs");
            if (ds.Get("candles") != null)
            {
                d.Candles = DecodeCandles(ds.Get("candles"));
                if (d.Candles == null) throw new JsonException("the saved candles are not valid");
            }

            foreach (var t in A(O(root, "transfers"), "history"))
            {
                string dir = S(t, "direction");
                if (dir != "to-city" && dir != "to-trading") throw new JsonException("a transfer has an invalid direction");
                d.Transfers.Add(new TransferRecord
                {
                    Id = S(t, "id"), RequestId = S(t, "requestId"), Time = L(t, "time"), Amount = Money.FromCents(L(t, "amount")), Direction = dir == "to-city" ? TransferDirection.ToCity : TransferDirection.ToTrading,
                    TradingBalanceAfter = Money.FromCents(L(t, "tradingBalanceAfter")), TreasuryBalanceAfter = Money.FromCents(L(t, "treasuryBalanceAfter")),
                });
            }
            if (root.Get("achievements") != null) foreach (var a in A(root, "achievements")) d.Achievements.Add(a.AsString());
            var ch = root.Get("challenges");
            if (ch != null && ch.Kind == JsonKind.Object)
                foreach (var kv in ch.Members)
                    d.Challenges[kv.Key] = new ChallengeResult { Stars = (int)L(kv.Value, "stars"), BestReturnPct = Req(kv.Value, "bestReturnPct").AsDouble(), Wins = (int)L(kv.Value, "wins"), Attempts = (int)L(kv.Value, "attempts") };
            var prog = O(root, "progression");
            foreach (var kv in O(prog, "completed").Members) d.Completed[kv.Key] = kv.Value.AsLong();
            d.PeakTreasury = Money.FromCents(L(prog, "peakTreasury"));
            return d;
        }

        // ---------------------------------------------------------------- consistency

        /// <summary>
        /// Cross-checks the format cannot express: ledgers add up, buildings fit on the land, no money was created by editing, the replay
        /// position is inside the dataset. Returns an error or null.
        /// </summary>
        public static string CheckConsistency(SaveData d, int candleCount)
        {
            long run = 0;
            foreach (var t in d.CityTransactions)
            {
                run += t.Amount.Cents;
                if (t.BalanceAfter.Cents != run) return "The city ledger does not add up.";
            }
            if (run != d.CityBalance.Cents) return "The city treasury balance does not match its ledger.";

            var a = d.Account;
            run = 0;
            long deposits = 0, withdrawals = 0;
            foreach (var t in a.Transactions)
            {
                run += t.Amount.Cents;
                if (t.BalanceAfter.Cents != run) return "The trading ledger does not add up.";
                if (t.Kind == AccountTransactionKind.Deposit) deposits += t.Amount.Cents;
                if (t.Kind == AccountTransactionKind.Withdrawal) withdrawals -= t.Amount.Cents;
            }
            if (run != a.Balance.Cents) return "The trading balance does not match its ledger.";
            if (deposits != a.Deposits.Cents || withdrawals != a.Withdrawals.Cents) return "Trading deposits/withdrawals do not match.";
            if (a.Closed.Sum(p => p.Exit != null ? p.Exit.Net.Cents : 0) != a.RealizedPnl.Cents) return "Trading realized P/L does not match closed trades.";
            if (a.Balance.Cents != a.StartingBalance.Cents + a.Deposits.Cents - a.Withdrawals.Cents + a.RealizedPnl.Cents) return "The trading balance is inconsistent with its history.";
            if (a.Open.Any(p => !p.IsOpen || p.Exit != null) || a.Closed.Any(p => p.IsOpen || p.Exit == null)) return "A trade has an invalid status.";
            var ids = a.Open.Concat(a.Closed).Select(p => p.Id).ToList();
            if (ids.Distinct().Count() != ids.Count) return "Duplicate trade ids.";

            // the three records of every transfer must agree (no created/destroyed money)
            long toCity = d.Transfers.Where(t => t.Direction == TransferDirection.ToCity).Sum(t => t.Amount.Cents);
            long toTrading = d.Transfers.Where(t => t.Direction == TransferDirection.ToTrading).Sum(t => t.Amount.Cents);
            long cityIn = d.CityTransactions.Where(t => t.Kind == TransactionKind.Transfer).Sum(t => t.Amount.Cents);
            long cityOut = -d.CityTransactions.Where(t => t.Kind == TransactionKind.TransferOut).Sum(t => t.Amount.Cents);
            if (toCity != cityIn || toCity != a.Withdrawals.Cents || toTrading != cityOut || toTrading != a.Deposits.Cents)
                return "Transfer records do not match between the trading account and the city.";

            if (d.LandTier > Land.Tiers.Count) return "Unknown land tier.";
            var grid = new CityGrid(Land.WorldSize, Land.WorldSize, 1f, d.LandTier == 0 ? Land.StartingArea : Land.Tiers[d.LandTier - 1].Area);
            var seen = new HashSet<string>();
            foreach (var b in d.Buildings)
            {
                if (!seen.Add(b.Id)) return "Duplicate building ids.";
                var def = Buildings.All.FirstOrDefault(x => x.Id == b.DefId);
                if (def == null) return "Unknown building type \"" + b.DefId + "\".";
                if (b.Level < 1 || b.Level > def.Levels.Length) return "Building " + b.Id + " has an invalid level.";
                if (b.CompletesAt < b.BuiltAt) return "Building " + b.Id + " finishes before it was started.";
                if (def.UnlockId != null && !d.Unlocked.Contains(def.UnlockId)) return "Building " + b.Id + " (" + def.Name + ") is not unlocked.";
                if (grid.Check(b.Origin, def.Footprint, b.Rotation) != PlacementReason.Ok) return "Building " + b.Id + " is outside your land or overlaps another building.";
                grid.Occupy(b.Id, b.Origin, def.Footprint, b.Rotation);
            }

            var orderIds = d.Account.Pending.Select(o => o.Id).ToList();
            if (orderIds.Distinct().Count() != orderIds.Count) return "Duplicate order ids.";
            foreach (var o in d.Account.Pending)
            {
                bool buy = o.Direction == Direction.Buy;
                if (o.StopLoss.HasValue && (buy ? o.StopLoss.Value >= o.Price : o.StopLoss.Value <= o.Price)) return "Order " + o.Id + " has its stop-loss on the wrong side.";
                if (o.TakeProfit.HasValue && (buy ? o.TakeProfit.Value <= o.Price : o.TakeProfit.Value >= o.Price)) return "Order " + o.Id + " has its take-profit on the wrong side.";
                if (o.CreatedCandleIndex >= d.ReplayRevealed) return "Order " + o.Id + " was created after the replay position.";
            }

            if (candleCount < 1) return "The saved dataset is empty.";
            if (d.ReplayRevealed < 1 || d.ReplayRevealed > candleCount) return "The replay position is outside the dataset.";
            if (a.Open.Any(p => p.EntryCandleIndex >= d.ReplayRevealed)) return "An open trade starts after the replay position.";
            return null;
        }

        // ---------------------------------------------------------------- apply

        /// <summary>The dataset's candles for a save: embedded ones, or regenerated from the seed for synthetic data. Null if neither.</summary>
        public static List<Candle> CandlesFor(SaveData d)
        {
            if (d.Candles != null) return d.Candles;
            if (d.Dataset.Synthetic && d.Dataset.Seed.HasValue && d.DatasetCount.HasValue)
                return SyntheticData.Generate(d.Dataset.Seed.Value, d.DatasetCount.Value, d.DatasetTimeframeMs ?? 15 * 60000);
            return null;
        }

        /// <summary>Apply an already-validated save to the world. Progression and achievements are suspended while the other systems are replaced.</summary>
        public static void Apply(GameWorld world, SaveData d, IReadOnlyList<Candle> candles)
        {
            world.GameOver.WithSuspended(() => world.Achievements.WithPaused(() => world.Progression.WithSuspended(() =>
            {
                // a loaded game starts paused, so you never come back to a city running at 10x
                world.Clock.Restore(d.ClockTime, 0);
                world.City.Restore(d.Buildings, d.Unlocked, d.LandTier, d.CityBalance, d.CityTransactions, d.Policies, d.Events, d.EventSeed);
                var info = new DatasetInfo { Id = d.Dataset.Id, Name = d.Dataset.Name, Synthetic = d.Dataset.Synthetic, Symbol = d.Dataset.Symbol, Seed = d.Dataset.Seed, Total = candles.Count };
                world.Trading.Restore(info, candles, d.ReplayRevealed, d.ReplaySpeed, d.RunId, d.Account, d.ChartTfMs, d.Journal, d.Drawings);
                world.Trading.Indicators.Restore(d.Indicators);
                world.Transfers.Restore(d.Transfers);
            })));
            world.Challenges.Restore(d.Challenges);
            world.Achievements.Restore(d.Achievements);
            world.Progression.Restore(d.Completed, d.PeakTreasury);
        }
    }
}
