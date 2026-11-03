using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>One stretch of a made-up market: how it drifts, how wild it is, and whether it snaps back.</summary>
    public sealed class Regime
    {
        public int Candles;
        /// <summary>Average move per candle, in points (positive = rising).</summary>
        public double Drift;
        /// <summary>Size of the random moves per candle, in points.</summary>
        public double Vol;
        /// <summary>0-1: how strongly the price is pulled back to where the regime started (a range market).</summary>
        public double Revert;
        public Regime(int candles, double drift, double vol, double revert = 0) { Candles = candles; Drift = drift; Vol = vol; Revert = revert; }
    }

    public static class Scenarios
    {
        public static readonly long ScenarioStart = new DateTimeOffset(2024, 3, 4, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(); // a Monday
        public const long ScenarioStartPrice = 110000;

        static long JsRound(double x) { return (long)Math.Floor(x + 0.5); }

        /// <summary>SYNTHETIC candles for a challenge: the regimes one after another, deterministic for a seed. Not real market data; no weekend gaps.</summary>
        public static List<Candle> GenerateRegimeCandles(uint seed, IEnumerable<Regime> regimes, long timeframeMs = 15 * 60000)
        {
            var rand = new Mulberry32(seed);
            var output = new List<Candle>();
            double price = ScenarioStartPrice;
            long t = ScenarioStart;
            foreach (var r in regimes)
            {
                double anchor = price;
                for (int i = 0; i < r.Candles; i++)
                {
                    double open = price, hi = open, lo = open, p = open;
                    for (int k = 0; k < 4; k++)
                    {
                        double noise = (rand.Next() + rand.Next() + rand.Next() - 1.5) * r.Vol * 0.8;
                        p += r.Drift / 4 + noise + (r.Revert != 0 ? (anchor - p) * (r.Revert / 4) : 0);
                        hi = Math.Max(hi, p);
                        lo = Math.Min(lo, p);
                    }
                    long close = JsRound(p);
                    output.Add(new Candle(t, (long)open, JsRound(Math.Max(hi, Math.Max(open, close))), JsRound(Math.Min(lo, Math.Min(open, close))), close) { Volume = JsRound(200 + rand.Next() * 800) });
                    price = close;
                    t += timeframeMs;
                }
            }
            return output;
        }
    }

    /// <summary>Rules a challenge enforces; breaking one ends the run as lost.</summary>
    public sealed class ChallengeRules
    {
        public int? MaxTrades;
        public bool RequireStopLoss;
        /// <summary>No trade may risk more than this percentage of the starting balance.</summary>
        public double? MaxRiskPct;
    }

    public sealed class ChallengeDefinition
    {
        public string Id, Name, Blurb;
        public uint Seed;
        /// <summary>The market, in order. The first regime is the warm-up you can look at before you trade.</summary>
        public Regime[] Regimes;
        /// <summary>Finish at least this much above the starting balance (percent).</summary>
        public double GoalPct;
        /// <summary>Lose if equity falls this far (percent) from its highest point.</summary>
        public double MaxDrawdownPct;
        public ChallengeRules Rules = new ChallengeRules();
        /// <summary>A city reward for the first win: an unlock id and the words shown for it.</summary>
        public string RewardUnlock, RewardText;
    }

    public static class Challenges
    {
        /// <summary>Every challenge starts with this balance, in a separate account from your game.</summary>
        public const double StartBalanceDollars = 10000;

        public static readonly IReadOnlyList<ChallengeDefinition> All = new[]
        {
            new ChallengeDefinition
            {
                Id = "trend", Name = "Catch the trend", Seed = 11, GoalPct = 1.5, MaxDrawdownPct = 3,
                Blurb = "A market that climbs, dips and climbs again. Ride it without being shaken out by the dip.",
                Regimes = new[] { new Regime(150, 0, 22), new Regime(130, 7, 30), new Regime(40, -6, 32), new Regime(140, 8, 30) },
            },
            new ChallengeDefinition
            {
                Id = "range", Name = "Range day", Seed = 23, GoalPct = 1, MaxDrawdownPct = 3,
                Blurb = "The price goes nowhere and traps breakout traders. Make money without overtrading.",
                Regimes = new[] { new Regime(150, 0, 22), new Regime(320, 0, 55, 0.06) },
                Rules = new ChallengeRules { MaxTrades = 10 }, RewardUnlock = "policy-austerity", RewardText = "unlocks the Cost cutting policy",
            },
            new ChallengeDefinition
            {
                Id = "spike", Name = "Spike survival", Seed = 37, GoalPct = 0.5, MaxDrawdownPct = 5,
                Blurb = "Calm, then a violent drop, then a slow recovery. Protect your account first and finish ahead.",
                Regimes = new[] { new Regime(150, 0, 18), new Regime(90, 0, 16), new Regime(18, -28, 110), new Regime(140, 8, 46) },
                Rules = new ChallengeRules { RequireStopLoss = true }, RewardUnlock = "policy-green", RewardText = "unlocks the Green city policy",
            },
            new ChallengeDefinition
            {
                Id = "discipline", Name = "Disciplined trader", Seed = 53, GoalPct = 2, MaxDrawdownPct = 4,
                Blurb = "A mixed market and strict rules: a stop-loss on every trade, small risk, few trades.",
                Regimes = new[] { new Regime(150, 0, 24), new Regime(100, 5, 30), new Regime(100, 0, 34, 0.1), new Regime(100, -6, 32), new Regime(100, 4, 28) },
                Rules = new ChallengeRules { RequireStopLoss = true, MaxRiskPct = 1.5, MaxTrades = 8 },
            },
        };

        public static ChallengeDefinition Get(string id) { return All.FirstOrDefault(c => c.Id == id); }
    }

    public enum ChallengePhase { Running, Won, Lost }

    /// <summary>A look at a challenge run, taken after every change of its trading account.</summary>
    public sealed class RunSnapshot
    {
        public Money Balance, Equity;
        public List<Position> Open, Closed;
        public int CandlesLeft;
    }

    public sealed class ChallengeStatus
    {
        public ChallengePhase Phase;
        /// <summary>Why the run was lost (a rule or the drawdown limit), or null.</summary>
        public string Reason;
        /// <summary>Return on the starting balance, in percent, by equity (open trades included while running).</summary>
        public double ReturnPct;
        /// <summary>The deepest fall of equity from its highest point, in percent.</summary>
        public double DrawdownPct;
        public int Trades, CandlesLeft;
        /// <summary>0 until won; 1-3 once won.</summary>
        public int Stars;
    }

    /// <summary>
    /// Judges a challenge from what happens in its account. Pure: the run feeds it snapshots. It loses the run at once when a
    /// rule is broken or the drawdown limit is hit, and decides win or loss on the final balance when the run is finished.
    /// </summary>
    public sealed class ChallengeTracker
    {
        readonly ChallengeDefinition _def;
        readonly Money _start;
        Money _peak;
        double _maxDd;
        string _lost;
        bool _done;

        public ChallengeTracker(ChallengeDefinition def, Money start) { _def = def; _start = start; _peak = start; }

        double Pct(double a) { return _start.Cents > 0 ? a / _start.Cents * 100 : 0; }

        string Violation(List<Position> all)
        {
            var r = _def.Rules;
            if (r.MaxTrades.HasValue && all.Count > r.MaxTrades.Value) return "Too many trades: the limit is " + r.MaxTrades.Value + ".";
            if (r.RequireStopLoss && all.Any(p => !p.InitialRisk.HasValue)) return "A trade was opened without a stop-loss.";
            if (r.MaxRiskPct.HasValue)
            {
                double limit = _start.Cents * r.MaxRiskPct.Value / 100;
                if (all.Any(p => p.InitialRisk.HasValue && p.InitialRisk.Value.Cents > limit + 1)) return "A trade risked more than " + r.MaxRiskPct.Value + "% of the starting balance.";
            }
            return null;
        }

        ChallengeStatus Status(RunSnapshot snap, bool finished)
        {
            double returnPct = Pct((snap.Equity - _start).Cents);
            int trades = snap.Open.Count + snap.Closed.Count;
            var phase = ChallengePhase.Running;
            int stars = 0;
            if (_lost != null) phase = ChallengePhase.Lost;
            else if (finished)
            {
                double balancePct = Pct((snap.Balance - _start).Cents);
                if (balancePct >= _def.GoalPct)
                {
                    phase = ChallengePhase.Won;
                    stars = 1 + (_maxDd <= _def.MaxDrawdownPct / 2 ? 1 : 0) + (balancePct >= _def.GoalPct * 1.5 ? 1 : 0);
                }
                else
                {
                    phase = ChallengePhase.Lost;
                    _lost = "You finished at " + (balancePct >= 0 ? "+" : "") + balancePct.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + "%, short of the +" + _def.GoalPct.ToString(System.Globalization.CultureInfo.InvariantCulture) + "% goal.";
                }
            }
            return new ChallengeStatus { Phase = phase, Reason = _lost, ReturnPct = returnPct, DrawdownPct = _maxDd, Trades = trades, CandlesLeft = snap.CandlesLeft, Stars = stars };
        }

        /// <summary>Take a snapshot while the run is going; returns the status (it may have just been lost).</summary>
        public ChallengeStatus Update(RunSnapshot snap)
        {
            if (_done) return Status(snap, true);
            _peak = Money.Max(_peak, snap.Equity);
            if (_peak.Cents > 0) _maxDd = Math.Max(_maxDd, (double)(_peak.Cents - snap.Equity.Cents) / _peak.Cents * 100);
            if (_lost == null)
            {
                var v = Violation(snap.Open.Concat(snap.Closed).ToList());
                if (v != null) _lost = v;
                else if (_maxDd > _def.MaxDrawdownPct)
                    _lost = "Drawdown limit hit: equity fell " + _maxDd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "% from its peak (limit " + _def.MaxDrawdownPct + "%).";
            }
            if (_lost != null) _done = true;
            return Status(snap, false);
        }

        /// <summary>The run is over (candles ran out, or the player finished): decide win or loss.</summary>
        public ChallengeStatus Finish(RunSnapshot snap)
        {
            Update(snap);
            _done = true;
            return Status(snap, true);
        }
    }

    /// <summary>One attempt at a challenge: its own trading account and market, apart from your game.</summary>
    public sealed class ChallengeRun
    {
        public readonly ChallengeDefinition Def;
        public readonly TradingSession Session = new TradingSession();
        public ChallengeStatus Status { get; private set; }
        public event Action Changed;
        /// <summary>Called once when the run ends (won or lost).</summary>
        public Action<ChallengeStatus> OnSettled;
        readonly ChallengeTracker _tracker;
        bool _settled, _busy;

        public ChallengeRun(ChallengeDefinition def)
        {
            Def = def;
            Session.LoadDataset(Scenarios.GenerateRegimeCandles(def.Seed, def.Regimes), "CHALLENGE: " + def.Name + " (synthetic)", true, null, def.Seed);
            _tracker = new ChallengeTracker(def, Session.State.Account.Balance);
            Status = _tracker.Update(Snapshot());
            Session.Changed += Check;
        }

        RunSnapshot Snapshot()
        {
            var s = Session.State;
            return new RunSnapshot
            {
                Balance = s.Account.Balance, Equity = s.Account.Equity, Open = s.Positions.Select(p => p.Position).ToList(), Closed = s.Closed.ToList(),
                CandlesLeft = Math.Max(s.Total - s.RevealedCount, 0),
            };
        }

        public bool Finished { get { return _settled; } }

        void Check()
        {
            if (_settled || _busy) return;
            _busy = true;
            try
            {
                var st = _tracker.Update(Snapshot());
                if (st.Phase == ChallengePhase.Lost) Settle(false);
                else if (st.CandlesLeft <= 0) Settle(true);
                else { Status = st; if (Changed != null) Changed(); }
            }
            finally { _busy = false; }
        }

        /// <summary>End the run now: open trades are closed at the market, then the result is decided.</summary>
        public void FinishNow()
        {
            if (_settled) return;
            _busy = true;
            try { Settle(true); } finally { _busy = false; }
        }

        void Settle(bool finish)
        {
            _settled = true;
            Session.Pause();
            Session.CloseAll();
            var snap = Snapshot();
            var st = finish ? _tracker.Finish(snap) : _tracker.Update(snap);
            // a run lost on a rule or the drawdown limit stays lost, whatever the final balance says
            var final = _tracker.Finish(snap);
            var result = st.Phase == ChallengePhase.Lost ? st : final;
            Status = result;
            if (Changed != null) Changed();
            if (OnSettled != null) OnSettled(result);
        }
    }

    public sealed class ChallengeResult
    {
        public int Stars, Wins, Attempts;
        public double BestReturnPct;
    }

    /// <summary>What you have achieved in each challenge. Saved with the game.</summary>
    public sealed class ChallengeStore
    {
        Dictionary<string, ChallengeResult> _results = new Dictionary<string, ChallengeResult>();
        public IReadOnlyDictionary<string, ChallengeResult> Results { get { return _results; } }
        public event Action Changed;

        /// <summary>Record an attempt. Returns true if this was the first win of that challenge.</summary>
        public bool Record(string id, ChallengeStatus status)
        {
            _results.TryGetValue(id, out var prev);
            prev = prev ?? new ChallengeResult();
            bool won = status.Phase == ChallengePhase.Won;
            var next = new Dictionary<string, ChallengeResult>(_results)
            {
                [id] = new ChallengeResult
                {
                    Stars = Math.Max(prev.Stars, status.Stars), BestReturnPct = won ? Math.Max(prev.BestReturnPct, status.ReturnPct) : prev.BestReturnPct,
                    Wins = prev.Wins + (won ? 1 : 0), Attempts = prev.Attempts + 1,
                },
            };
            _results = next;
            if (Changed != null) Changed();
            return won && prev.Wins == 0;
        }

        public Dictionary<string, ChallengeResult> Snapshot()
        {
            return _results.ToDictionary(kv => kv.Key, kv => new ChallengeResult { Stars = kv.Value.Stars, Wins = kv.Value.Wins, Attempts = kv.Value.Attempts, BestReturnPct = kv.Value.BestReturnPct });
        }

        public void Restore(IReadOnlyDictionary<string, ChallengeResult> data)
        {
            var known = new HashSet<string>(Challenges.All.Select(c => c.Id));
            var output = new Dictionary<string, ChallengeResult>();
            foreach (var kv in data)
            {
                if (!known.Contains(kv.Key)) continue;
                var v = kv.Value;
                output[kv.Key] = new ChallengeResult
                {
                    Stars = Math.Min(Math.Max(v.Stars, 0), 3), Wins = Math.Max(v.Wins, 0), Attempts = Math.Max(v.Attempts, 0),
                    BestReturnPct = double.IsNaN(v.BestReturnPct) || double.IsInfinity(v.BestReturnPct) ? 0 : v.BestReturnPct,
                };
            }
            _results = output;
            if (Changed != null) Changed();
        }
    }
}
