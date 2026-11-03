using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum BuildReason { Ok, OutsideLand, Occupied, Road, Limit, InsufficientFunds, Locked }

    public struct BuildCheck
    {
        public BuildReason Reason;
        public bool Ok { get { return Reason == BuildReason.Ok; } }
        public BuildCheck(BuildReason r) { Reason = r; }
    }

    public sealed class Placement
    {
        public string DefId;
        public int Rotation;
    }

    public sealed class Hover
    {
        public Cell Origin;
        public BuildCheck Check;
    }

    public sealed class PayoutItem
    {
        public string Id;
        public Money Amount;
    }

    public sealed class PayoutBatch
    {
        public int Seq;
        public List<PayoutItem> Items;
    }

    public sealed class CityState
    {
        public IReadOnlyList<BuildingState> Buildings = new List<BuildingState>();
        public string SelectedId;
        public Placement Placement;
        public Hover Hover;
        public Notice Notice;
        /// <summary>Treasury snapshot (the CityTreasury is the source of truth).</summary>
        public Money Balance;
        public IReadOnlyList<TransactionRecord> Transactions = new List<TransactionRecord>();
        /// <summary>Unlock ids granted so far (buildings by id, land as "land-N").</summary>
        public IReadOnlyList<string> Unlocked = new List<string>();
        /// <summary>Number of land expansions bought (0 = starting plot only).</summary>
        public int LandTier;
        /// <summary>Policies that are switched on (saved).</summary>
        public IReadOnlyList<string> Policies = new List<string>();
        /// <summary>Fire, storm or festival going on now (saved).</summary>
        public IReadOnlyList<CityEvent> Events = new List<CityEvent>();
        /// <summary>The city's mood from your latest trades (derived from Record, never saved).</summary>
        public Mood Mood = Mood.Steady;
        /// <summary>Read-only summary of the player's closed trades, for landmarks (never saved).</summary>
        public TradingRecord Record = TradingRecord.None;
        /// <summary>The latest settlement's net payout per building, for on-map feedback (never saved).</summary>
        public PayoutBatch Payouts;
    }

    /// <summary>
    /// City domain facade: placement, selection, construction, upgrades, events, policies and the economy tick. Pure C#;
    /// the view reads <see cref="State"/> and calls these methods. Subscribe to <see cref="Changed"/> to redraw.
    /// </summary>
    public sealed class CityGame
    {
        public readonly CityGrid Grid;
        public readonly GameClock Clock;
        public CityState State { get; private set; }
        public event Action Changed;
        /// <summary>Optional: the mission title that unlocks something, for "locked" messages (set by the progression layer).</summary>
        public Func<string, string> MissionTitleFor;

        readonly CityTreasury _treasury;
        readonly IdSequence _nextId = new IdSequence("b");
        readonly RectI _startArea;
        int _noticeSeq, _payoutSeq, _eventSeq;
        long? _eventDay;

        /// <summary>Seeds the random events (a new game picks one at random; the same seed repeats the same luck).</summary>
        public uint EventSeed = 1337;

        public CityGame(CityGrid grid, GameClock clock, Money? startingFunds = null)
        {
            Grid = grid;
            Clock = clock;
            _startArea = grid.Buildable;
            _treasury = new CityTreasury(new IdSequence("t"));
            if (startingFunds.HasValue && startingFunds.Value.Cents > 0)
                _treasury.Credit(TransactionKind.InitialFunding, startingFunds.Value, clock.Time, "Starting funds");
            State = new CityState { Balance = _treasury.Balance, Transactions = _treasury.History };
        }

        // ---------- state plumbing ----------

        void Patch(Action<CityState> change)
        {
            var s = Copy(State);
            if (change != null) change(s);
            State = s;
            if (Changed != null) Changed();
        }

        static CityState Copy(CityState s) { return (CityState)MemberwiseCloneState(s); }

        static object MemberwiseCloneState(CityState s)
        {
            return new CityState
            {
                Buildings = s.Buildings, SelectedId = s.SelectedId, Placement = s.Placement, Hover = s.Hover, Notice = s.Notice, Balance = s.Balance,
                Transactions = s.Transactions, Unlocked = s.Unlocked, LandTier = s.LandTier, Policies = s.Policies, Events = s.Events, Mood = s.Mood,
                Record = s.Record, Payouts = s.Payouts,
            };
        }

        /// <summary>Patch plus a treasury snapshot and a refreshed placement preview (funds may have changed).</summary>
        void PatchWithTreasury(Action<CityState> change)
        {
            var hover = State.Placement != null && State.Hover != null
                ? new Hover { Origin = State.Hover.Origin, Check = Validate(State.Placement.DefId, State.Hover.Origin, State.Placement.Rotation) }
                : State.Hover;
            Patch(s =>
            {
                if (change != null) change(s);
                s.Hover = hover;
                s.Balance = _treasury.Balance;
                s.Transactions = _treasury.History;
            });
        }

        void Notify(NoticeKind kind, string text)
        {
            var n = new Notice { Seq = ++_noticeSeq, Kind = kind, Text = text };
            Patch(s => s.Notice = n);
        }

        public static string Describe(BuildCheck check)
        {
            switch (check.Reason)
            {
                case BuildReason.Ok: return "Valid location";
                case BuildReason.Locked: return "Locked: complete its mission first";
                case BuildReason.OutsideLand: return "Outside your land";
                case BuildReason.Occupied: return "Space is occupied";
                case BuildReason.Road: return "Streets can't be built on";
                case BuildReason.Limit: return "You already have the only one allowed";
                default: return "Insufficient funds";
            }
        }

        string Locked(string what, string unlockId)
        {
            var title = unlockId != null && MissionTitleFor != null ? MissionTitleFor(unlockId) : null;
            return what + " is locked" + (title != null ? ": complete \"" + title + "\"" : "");
        }

        // ---------- placement ----------

        /// <summary>Spatial validity first, then affordability. Nothing is charged here.</summary>
        public BuildCheck Validate(string defId, Cell origin, int rotation)
        {
            var def = Buildings.Get(defId);
            if (!IsUnlocked(def.UnlockId)) return new BuildCheck(BuildReason.Locked);
            if (def.MaxCount.HasValue && State.Buildings.Count(b => b.DefId == def.Id) >= def.MaxCount.Value) return new BuildCheck(BuildReason.Limit);
            var spatial = Grid.Check(origin, def.Footprint, rotation);
            if (spatial != PlacementReason.Ok)
                return new BuildCheck(spatial == PlacementReason.OutsideLand ? BuildReason.OutsideLand : spatial == PlacementReason.Road ? BuildReason.Road : BuildReason.Occupied);
            if (!_treasury.CanAfford(def.Cost)) return new BuildCheck(BuildReason.InsufficientFunds);
            return new BuildCheck(BuildReason.Ok);
        }

        public void StartPlacement(string defId)
        {
            var def = Buildings.Get(defId); // throws on unknown id
            if (!IsUnlocked(def.UnlockId)) { Notify(NoticeKind.Error, Locked(def.Name, def.UnlockId)); return; }
            Patch(s => { s.Placement = new Placement { DefId = defId, Rotation = 0 }; s.SelectedId = null; s.Hover = null; });
        }

        public void CancelPlacement()
        {
            if (State.Placement != null) Patch(s => { s.Placement = null; s.Hover = null; });
        }

        public void Rotate()
        {
            var p = State.Placement;
            if (p == null) return;
            int rotation = CityGrid.NextRotation(p.Rotation);
            Hover hover = State.Hover != null ? new Hover { Origin = State.Hover.Origin, Check = Validate(p.DefId, State.Hover.Origin, rotation) } : null;
            Patch(s => { s.Placement = new Placement { DefId = p.DefId, Rotation = rotation }; s.Hover = hover; });
        }

        /// <summary>Update the preview position; does nothing (no change event) when nothing changed.</summary>
        public void SetHover(Cell? origin)
        {
            var p = State.Placement;
            var cur = State.Hover;
            if (p == null || !origin.HasValue)
            {
                if (cur != null) Patch(s => s.Hover = null);
                return;
            }
            var check = Validate(p.DefId, origin.Value, p.Rotation);
            if (cur != null && cur.Origin.Equals(origin.Value) && cur.Check.Ok == check.Ok) return;
            Patch(s => s.Hover = new Hover { Origin = origin.Value, Check = check });
        }

        /// <summary>Validates, charges the construction cost, then places the building. Nothing is deducted unless every validation passes.</summary>
        public bool TryPlace(Cell origin)
        {
            var p = State.Placement;
            if (p == null) return false;
            var def = Buildings.Get(p.DefId);
            var check = Validate(p.DefId, origin, p.Rotation);
            if (!check.Ok)
            {
                string detail = check.Reason == BuildReason.InsufficientFunds ? "need " + def.Cost + ", have " + _treasury.Balance : Describe(check);
                Notify(NoticeKind.Error, "Can't build " + def.Name + ": " + detail);
                return false;
            }
            string id = _nextId.Next();
            long now = Clock.Time;
            long completesAt = now + (long)(def.ConstructionDays * GameClock.DayMs);
            var paid = _treasury.Debit(TransactionKind.Construction, def.Cost, now, "Built " + def.Name, id);
            if (!paid.Ok) return false; // unreachable after Validate(); kept to stay atomic
            Grid.Occupy(id, origin, def.Footprint, p.Rotation);
            var building = new BuildingState
            {
                Id = id, DefId = def.Id, Origin = origin, Rotation = p.Rotation, Level = 1, PurchaseCost = def.Cost, BuiltAt = now,
                Status = BuildStatus.Building, CompletesAt = completesAt, NextPayoutAt = completesAt + def.IncomeIntervalDays * GameClock.DayMs,
                LifetimeIncome = Money.Zero, LifetimeExpense = Money.Zero,
            };
            // The new building is NOT auto-selected: the inspection panel would cover the map while the player is placing more.
            PatchWithTreasury(s => s.Buildings = State.Buildings.Concat(new[] { building }).ToList());
            Notify(NoticeKind.Success, def.Name + " ordered for " + def.Cost + ", ready in " + def.ConstructionDays + " days");
            return true;
        }

        /// <summary>What demolishing a building would pay back right now.</summary>
        public Money DemolishRefund(string id)
        {
            var b = State.Buildings.FirstOrDefault(x => x.Id == id);
            return b != null ? b.PurchaseCost.Scale(Balance.DemolishRefundPct / 100.0) : Money.Zero;
        }

        /// <summary>Remove a building and free its cells. Pays back part of the construction cost; its scheduled payouts disappear with it.</summary>
        public bool Demolish(string id)
        {
            var b = State.Buildings.FirstOrDefault(x => x.Id == id);
            if (b == null) return false;
            var def = Buildings.Get(b.DefId);
            var refund = DemolishRefund(id);
            if (refund.Cents > 0) _treasury.Credit(TransactionKind.Demolition, refund, Clock.Time, "Demolished " + def.Name, id);
            Grid.Release(id);
            PatchWithTreasury(s => { s.Buildings = State.Buildings.Where(x => x.Id != id).ToList(); s.SelectedId = null; });
            Notify(NoticeKind.Info, refund.Cents > 0 ? def.Name + " demolished, " + refund + " refunded" : def.Name + " demolished");
            return true;
        }

        /// <summary>Upgrade a building one level. Charges the upgrade cost; no-op with feedback on failure.</summary>
        public bool Upgrade(string id)
        {
            var b = State.Buildings.FirstOrDefault(x => x.Id == id);
            if (b == null) return false;
            var def = Buildings.Get(b.DefId);
            if (b.Status == BuildStatus.Building) { Notify(NoticeKind.Error, def.Name + " is still under construction"); return false; }
            var cost = def.Levels[b.Level - 1].UpgradeCost;
            if (!cost.HasValue) { Notify(NoticeKind.Error, def.Name + " is already at max level"); return false; }
            var paid = _treasury.Debit(TransactionKind.Upgrade, cost.Value, Clock.Time, "Upgraded " + def.Name + " to level " + (b.Level + 1), id);
            if (!paid.Ok)
            {
                Notify(NoticeKind.Error, "Can't upgrade: need " + cost.Value + ", have " + _treasury.Balance);
                return false;
            }
            PatchWithTreasury(s => s.Buildings = State.Buildings.Select(x => { if (x.Id != id) return x; var c = x.Clone(); c.Level++; return c; }).ToList());
            Notify(NoticeKind.Success, def.Name + " upgraded to level " + (b.Level + 1));
            return true;
        }

        // ---------- the economy tick ----------

        /// <summary>
        /// Economy tick (called by the GameClock). Posts every due income and expense as recorded transactions. Income is
        /// credited before the expense; operating expenses are mandatory and may take the treasury below zero (debt).
        /// </summary>
        public void Tick(long now)
        {
            int noticeBefore = _noticeSeq;
            RunEvents(now);
            bool eventNews = _noticeSeq != noticeBefore;
            bool changed = false;
            Money received = Money.Zero, spent = Money.Zero;
            var finished = new List<string>();
            var paid = new List<PayoutItem>();
            var current = State.Buildings;
            var ctx = Context();
            var buildings = new List<BuildingState>();
            foreach (var site in current)
            {
                var b = site;
                if (b.Status == BuildStatus.Building && now >= b.CompletesAt)
                {
                    b = b.Clone();
                    b.Status = BuildStatus.Complete;
                    changed = true;
                    finished.Add(Buildings.Get(b.DefId).Name);
                }
                if (b.Status == BuildStatus.Building) { buildings.Add(b); continue; }
                var (payouts, nextPayoutAt) = CityEconomy.CollectDuePayouts(b, now, CityEconomy.ModsFor(b, current, ctx));
                if (payouts.Count == 0) { buildings.Add(b); continue; }
                changed = true;
                var def = Buildings.Get(b.DefId);
                Money income = b.LifetimeIncome, expense = b.LifetimeExpense;
                foreach (var p in payouts)
                {
                    if (p.Income.Cents > 0)
                    {
                        _treasury.Credit(TransactionKind.Income, p.Income, p.Time, def.Name + " income", b.Id);
                        income = income + p.Income;
                        received = received + p.Income;
                    }
                    if (p.Expense.Cents > 0)
                    {
                        _treasury.Debit(TransactionKind.Expense, p.Expense, p.Time, def.Name + " operating costs", b.Id, true);
                        expense = expense + p.Expense;
                        spent = spent + p.Expense;
                    }
                }
                paid.Add(new PayoutItem { Id = b.Id, Amount = (income - b.LifetimeIncome) - (expense - b.LifetimeExpense) });
                var updated = b.Clone();
                updated.NextPayoutAt = nextPayoutAt; updated.LifetimeIncome = income; updated.LifetimeExpense = expense;
                buildings.Add(updated);
            }
            if (!changed) return;
            // One small summary per settlement, so income is noticed without a toast per building.
            var parts = new List<string>();
            if (finished.Count > 0) parts.Add(string.Join(", ", finished) + " finished");
            if (received.Cents > 0 || spent.Cents > 0) parts.Add("Rent collected " + received + ", running costs " + spent);
            var batch = paid.Count > 0 ? new PayoutBatch { Seq = ++_payoutSeq, Items = paid } : State.Payouts;
            var summary = new Notice { Seq = ++_noticeSeq, Kind = NoticeKind.Info, Text = string.Join(". ", parts) };
            PatchWithTreasury(s =>
            {
                s.Buildings = buildings;
                s.Payouts = batch;
                // a fire or storm announced in this same tick is not replaced by the rent summary
                s.Notice = eventNews ? State.Notice : summary;
            });
        }

        // ---------- random events ----------

        double Shield() { return State.Policies.Sum(id => { var p = Modifiers.GetPolicy(id); return p != null ? p.EventShield : 0; }); }

        /// <summary>End events that are over, and once a day maybe start a new one. Nothing is rolled while one runs.</summary>
        void RunEvents(long now)
        {
            var before = State.Events;
            var live = before.Where(e => e.EndsAt > now).ToList();
            foreach (var e in before)
                if (e.EndsAt <= now) { var d = EventConfig.Get(e.Kind); Notify(NoticeKind.Info, (d != null ? d.Name : "Event") + " is over"); }
            long day = (long)Math.Floor(now / (double)GameClock.DayMs);
            if (!_eventDay.HasValue) _eventDay = day;
            int rolls = 0;
            CityEvent started = null;
            while (_eventDay.Value < day && rolls < EventConfig.MaxRollsPerTick)
            {
                _eventDay++;
                rolls++;
                if (live.Count > 0 || started != null) continue;
                var ready = State.Buildings.Where(b => b.Status == BuildStatus.Complete).Select(b => b.Id).ToList();
                var r = CityEvents.Roll(EventSeed, _eventDay.Value, ready);
                if (r == null) continue;
                started = new CityEvent { Seq = ++_eventSeq, Kind = r.Kind, StartsAt = now, EndsAt = CityEvents.EndsAt(r.Kind, now), BuildingId = r.BuildingId };
                live.Add(started);
            }
            if (_eventDay.Value < day) _eventDay = day; // a long jump never floods the city with events
            if (live.Count == before.Count && started == null) return;
            PatchWithTreasury(s => s.Events = live);
            if (started != null) AnnounceEvent(started, now);
        }

        void AnnounceEvent(CityEvent ev, long now)
        {
            var def = EventConfig.Get(ev.Kind);
            if (def == null) return;
            if (ev.Kind == EventKind.Fire)
            {
                var b = State.Buildings.FirstOrDefault(x => x.Id == ev.BuildingId);
                if (b == null) return;
                string name = Buildings.Get(b.DefId).Name;
                var bill = CityEvents.RepairCost(b.PurchaseCost, Shield());
                var pay = Money.Min(bill, SpendableCash());
                if (pay.Cents > 0)
                {
                    _treasury.Debit(TransactionKind.Expense, pay, now, "Fire repairs: " + name, b.Id);
                    PatchWithTreasury(null);
                }
                string insured = Shield() > 0 ? " (insured)" : "";
                Notify(NoticeKind.Error, "Fire at " + name + "! Closed " + def.Days + " days, repairs " + pay + insured);
            }
            else
            {
                Notify(ev.Kind == EventKind.Storm ? NoticeKind.Error : NoticeKind.Success, def.Name + ": " + def.Text + " for " + def.Days + " days");
            }
        }

        // ---------- mood, policies ----------

        /// <summary>Called by the progression layer with the trading record; does nothing when nothing changed.</summary>
        public void SetTradingRecord(TradingRecord record)
        {
            if (State.Record.SameAs(record)) return;
            var mood = Landmarks.MoodOf(record);
            bool changed = mood != State.Mood;
            Patch(s => { s.Record = record; s.Mood = mood; });
            // a change of mood is news; it never moves money by itself
            if (changed) Notify(NoticeKind.Info, mood == Mood.Steady ? "The city calms down." : Modifiers.EffectOf(mood).Text);
        }

        /// <summary>The mood and policies that shape income and upkeep right now.</summary>
        public CityContext Context() { return new CityContext { Mood = State.Mood, Policies = State.Policies, Events = State.Events }; }

        /// <summary>Switch a policy on or off. It applies to the next payouts; nothing is charged or refunded.</summary>
        public void SetPolicy(string id, bool on)
        {
            var policy = Modifiers.GetPolicy(id);
            if (policy == null) return;
            bool has = State.Policies.Contains(id);
            if (has == on) return;
            if (on && !IsUnlocked(policy.UnlockId)) { Notify(NoticeKind.Error, policy.Name + " is locked"); return; }
            Patch(s => s.Policies = on ? State.Policies.Concat(new[] { id }).ToList() : State.Policies.Where(p => p != id).ToList());
            Notify(NoticeKind.Info, policy.Name + (on ? " switched on" : " switched off"));
        }

        // ---------- test tools ----------

        /// <summary>Test tools only (god mode): add money to the treasury as a recorded funding entry.</summary>
        public void GrantFunds(Money amount)
        {
            _treasury.Credit(TransactionKind.InitialFunding, amount, Clock.Time, "God mode funds");
            PatchWithTreasury(null);
            Notify(NoticeKind.Info, "God mode: +" + amount + " to the treasury");
        }

        /// <summary>Test tools only (god mode): unlock every building and district.</summary>
        public void UnlockEverything()
        {
            var ids = Buildings.All.Where(d => d.UnlockId != null).Select(d => d.UnlockId).Concat(Land.Tiers.Select(t => t.UnlockId));
            int n = 0;
            foreach (var id in ids) if (GrantUnlock(id)) n++;
            Notify(NoticeKind.Info, n > 0 ? "God mode: unlocked " + n + " items" : "Everything is already unlocked");
        }

        // ---------- treasury and capital transfers ----------

        /// <summary>Treasury cash (the source of truth for what the city can spend).</summary>
        public Money TreasuryBalance { get { return _treasury.Balance; } }

        /// <summary>Part of an atomic capital transfer: mutates the treasury WITHOUT notifying observers.</summary>
        public void ApplyTransferIn(Money amount, long time, string memo) { _treasury.Credit(TransactionKind.Transfer, amount, time, memo); }

        /// <summary>Cash the city can pay out right now (never negative).</summary>
        public Money SpendableCash() { return _treasury.Balance.Cents > 0 ? _treasury.Balance : Money.Zero; }

        /// <summary>Part of an atomic transfer TO the trading account: takes cash from the treasury WITHOUT notifying. Refuses if the cash isn't there.</summary>
        public bool ApplyTransferOut(Money amount, long time, string memo) { return _treasury.Debit(TransactionKind.TransferOut, amount, time, memo).Ok; }

        /// <summary>Publish the treasury snapshot (called by the transfer service after both sides changed).</summary>
        public void PublishTreasury() { PatchWithTreasury(null); }

        // ---------- saving ----------

        /// <summary>Replace the city from a validated save. Placements were checked before this is called.</summary>
        public void Restore(IReadOnlyList<BuildingState> buildings, IReadOnlyList<string> unlocked, int landTier, Money balance, IReadOnlyList<TransactionRecord> transactions,
            IReadOnlyList<string> policies = null, IReadOnlyList<CityEvent> events = null, uint? eventSeed = null)
        {
            Grid.Reset(landTier == 0 ? _startArea : (landTier - 1 < Land.Tiers.Count ? Land.Tiers[landTier - 1].Area : _startArea));
            foreach (var b in buildings) Grid.Occupy(b.Id, b.Origin, Buildings.Get(b.DefId).Footprint, b.Rotation);
            EventSeed = eventSeed ?? EventSeed;
            _eventDay = null;
            var evs = (events ?? new CityEvent[0]).Where(e => EventConfig.Get(e.Kind) != null).ToList();
            _eventSeq = evs.Count > 0 ? evs.Max(e => e.Seq) : 0;
            _treasury.Restore(balance, transactions);
            _nextId.ResetAfter(buildings.Select(b => b.Id));
            var pol = (policies ?? new string[0]).Where(id => Modifiers.GetPolicy(id) != null).ToList();
            var record = State.Record;
            // a policy that was already on keeps working in saves from before it was locked
            var unlockedSet = unlocked.Concat(pol.Select(id => Modifiers.GetPolicy(id).UnlockId).Where(u => u != null)).Distinct().ToList();
            State = new CityState
            {
                Buildings = buildings.ToList(), Balance = _treasury.Balance, Transactions = _treasury.History, Unlocked = unlockedSet, LandTier = landTier,
                Record = record, Policies = pol, Events = evs, Mood = Landmarks.MoodOf(record),
            };
            if (Changed != null) Changed();
        }

        // ---------- progression hooks ----------

        public bool IsUnlocked(string unlockId) { return string.IsNullOrEmpty(unlockId) || State.Unlocked.Contains(unlockId); }

        /// <summary>Granted by the progression layer when a mission completes. Returns true if newly unlocked.</summary>
        public bool GrantUnlock(string unlockId)
        {
            if (State.Unlocked.Contains(unlockId)) return false;
            Patch(s => s.Unlocked = State.Unlocked.Concat(new[] { unlockId }).ToList());
            return true;
        }

        /// <summary>Show a toast (used by progression for mission completion).</summary>
        public void Announce(NoticeKind kind, string text) { Notify(kind, text); }

        // ---------- land expansion ----------

        /// <summary>The next purchasable land tier, or null when the whole world is owned.</summary>
        public LandTier NextLandTier() { return State.LandTier < Land.Tiers.Count ? Land.Tiers[State.LandTier] : null; }

        /// <summary>Buy the next land tier: needs its mission unlock and the cost in the treasury.</summary>
        public bool ExpandLand()
        {
            var tier = NextLandTier();
            if (tier == null) { Notify(NoticeKind.Info, "You already own all available land"); return false; }
            if (!IsUnlocked(tier.UnlockId)) { Notify(NoticeKind.Error, Locked(tier.Name, tier.UnlockId)); return false; }
            var paid = _treasury.Debit(TransactionKind.Land, tier.Cost, Clock.Time, "Bought " + tier.Name);
            if (!paid.Ok) { Notify(NoticeKind.Error, "Can't buy " + tier.Name + ": need " + tier.Cost + ", have " + _treasury.Balance); return false; }
            Grid.ExpandBuildable(tier.Area);
            PatchWithTreasury(s => s.LandTier = State.LandTier + 1);
            Notify(NoticeKind.Success, tier.Name + " purchased for " + tier.Cost);
            return true;
        }

        public void Select(string id)
        {
            if (State.SelectedId != id) Patch(s => s.SelectedId = id);
        }

        public void SelectAt(Cell cell) { Select(Grid.OccupantAt(cell)); }

        /// <summary>Escape behaviour: cancel placement first, otherwise clear selection.</summary>
        public void Escape()
        {
            if (State.Placement != null) CancelPlacement();
            else Select(null);
        }
    }
}
