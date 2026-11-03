using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>Where funds come from.</summary>
    public interface ITransferSource
    {
        /// <summary>Funds that may leave (e.g. realized funds only).</summary>
        Money Eligible();
        Money Balance();
        /// <summary>Mutate only; must NOT publish/notify. Returns false (changing nothing) if refused.</summary>
        bool Debit(Money amount, long time, string memo);
        /// <summary>Notify observers after BOTH sides have changed.</summary>
        void Publish();
    }

    /// <summary>Where funds go.</summary>
    public interface ITransferTarget
    {
        Money Balance();
        /// <summary>Mutate only; must NOT publish/notify.</summary>
        void Credit(Money amount, long time, string memo);
        void Publish();
    }

    public enum TransferDirection { ToCity, ToTrading }
    public enum TransferFailure { InvalidAmount, ExceedsEligible, Duplicate }

    public sealed class TransferRecord
    {
        public string Id, RequestId;
        public long Time;
        public Money Amount;
        /// <summary>ToCity moves trading funds to the treasury; ToTrading moves treasury cash back.</summary>
        public TransferDirection Direction;
        public Money TradingBalanceAfter, TreasuryBalanceAfter;
    }

    public sealed class TransferResult
    {
        public bool Ok;
        public TransferRecord Record;
        public TransferFailure Reason;
        public string Message;
    }

    /// <summary>The two accounts, seen from the other direction: the treasury pays out, the trading account receives.</summary>
    public sealed class ReverseRoute
    {
        public ITransferSource Source;
        public ITransferTarget Target;
    }

    /// <summary>
    /// The only normal mechanism for moving money between the trading account and the city treasury, in either direction.
    /// Atomic: everything is validated first; then both balances change in one synchronous block and observers are
    /// notified only afterwards. Idempotent: each request carries an id and a repeated id is refused. Money is neither
    /// created nor destroyed: what leaves one account arrives in the other.
    /// </summary>
    public sealed class CapitalTransferService
    {
        readonly HashSet<string> _processed = new HashSet<string>();
        readonly IdSequence _nextId = new IdSequence("x");
        readonly ITransferSource _source;
        readonly ITransferTarget _target;
        readonly Func<long> _now;
        readonly ReverseRoute _reverse;
        List<TransferRecord> _records = new List<TransferRecord>();

        /// <summary>Transfer history (newest last).</summary>
        public IReadOnlyList<TransferRecord> History { get { return _records; } }
        public event Action Changed;

        public CapitalTransferService(ITransferSource source, ITransferTarget target, Func<long> now, ReverseRoute reverse = null)
        {
            _source = source; _target = target; _now = now; _reverse = reverse;
        }

        /// <summary>Restore history from a save. Processed request ids are deliberately NOT restored (they only guard double submits within a session).</summary>
        public void Restore(IEnumerable<TransferRecord> records)
        {
            _processed.Clear();
            _records = records.ToList();
            _nextId.ResetAfter(_records.Select(r => r.Id));
            if (Changed != null) Changed();
        }

        /// <summary>Trading funds that may move to the city (realized, not tied up as margin).</summary>
        public Money EligibleAmount() { return _source.Eligible(); }

        /// <summary>Treasury cash that may move to the trading account.</summary>
        public Money EligibleToTrading() { return _reverse != null ? _reverse.Source.Eligible() : Money.Zero; }

        /// <summary>Move funds from the trading account to the city treasury.</summary>
        public TransferResult Transfer(string requestId, Money amount)
        {
            return Move(requestId, amount, TransferDirection.ToCity, _source, _target, "Transfer to city treasury", "Transfer from trading account",
                "No eligible funds. Close profitable trades to realize them; margin in use cannot be transferred.",
                e => "Only " + e + " is eligible. Unrealized profit and margin in use cannot be transferred.");
        }

        /// <summary>Move cash from the city treasury to the trading account (a deliberate, recorded top-up).</summary>
        public TransferResult TransferToTrading(string requestId, Money amount)
        {
            if (_reverse == null) return new TransferResult { Ok = false, Reason = TransferFailure.ExceedsEligible, Message = "Transfers to trading are not available." };
            return Move(requestId, amount, TransferDirection.ToTrading, _reverse.Source, _reverse.Target, "Transfer to trading account", "Transfer from city treasury",
                "The city treasury has no cash to transfer.", e => "Only " + e + " of cash is available in the city treasury.");
        }

        TransferResult Move(string requestId, Money amount, TransferDirection direction, ITransferSource from, ITransferTarget to,
            string sourceMemo, string targetMemo, string none, Func<Money, string> some)
        {
            if (_processed.Contains(requestId)) return new TransferResult { Ok = false, Reason = TransferFailure.Duplicate, Message = "This transfer was already processed." };
            if (amount.Cents <= 0) return new TransferResult { Ok = false, Reason = TransferFailure.InvalidAmount, Message = "Enter an amount greater than $0.00." };
            var eligible = from.Eligible();
            if (amount > eligible)
                return new TransferResult { Ok = false, Reason = TransferFailure.ExceedsEligible, Message = eligible.Cents > 0 ? some(eligible) : none };

            // ---- atomic section: no notifications ----
            long time = _now();
            if (!from.Debit(amount, time, sourceMemo))
                return new TransferResult { Ok = false, Reason = TransferFailure.ExceedsEligible, Message = "The source account refused the withdrawal." };
            to.Credit(amount, time, targetMemo);
            _processed.Add(requestId);
            var sourceAfter = from.Balance();
            var targetAfter = to.Balance();
            var record = new TransferRecord
            {
                Id = _nextId.Next(), RequestId = requestId, Time = time, Amount = amount, Direction = direction,
                TradingBalanceAfter = direction == TransferDirection.ToCity ? sourceAfter : targetAfter,
                TreasuryBalanceAfter = direction == TransferDirection.ToCity ? targetAfter : sourceAfter,
            };
            _records = new List<TransferRecord>(_records) { record };
            // ---- end atomic section ----

            from.Publish();
            to.Publish();
            if (Changed != null) Changed();
            return new TransferResult { Ok = true, Record = record };
        }
    }

    /// <summary>Connects the real trading session and city; the only place the two accounts are wired together.</summary>
    public static class TransferWiring
    {
        sealed class TradingSource : ITransferSource
        {
            readonly TradingSession _t;
            public TradingSource(TradingSession t) { _t = t; }
            public Money Eligible() { return _t.EligibleForTransfer(); }
            public Money Balance() { return _t.Account.Balance; }
            public bool Debit(Money amount, long time, string memo) { return _t.ApplyTransferOut(amount, time, memo); }
            public void Publish() { _t.Publish(); }
        }

        sealed class TradingTarget : ITransferTarget
        {
            readonly TradingSession _t;
            public TradingTarget(TradingSession t) { _t = t; }
            public Money Balance() { return _t.Account.Balance; }
            public void Credit(Money amount, long time, string memo) { _t.ApplyTransferIn(amount, time, memo); }
            public void Publish() { _t.Publish(); }
        }

        sealed class CitySource : ITransferSource
        {
            readonly CityGame _c;
            public CitySource(CityGame c) { _c = c; }
            public Money Eligible() { return _c.SpendableCash(); }
            public Money Balance() { return _c.TreasuryBalance; }
            public bool Debit(Money amount, long time, string memo) { return _c.ApplyTransferOut(amount, time, memo); }
            public void Publish() { _c.PublishTreasury(); }
        }

        sealed class CityTarget : ITransferTarget
        {
            readonly CityGame _c;
            public CityTarget(CityGame c) { _c = c; }
            public Money Balance() { return _c.TreasuryBalance; }
            public void Credit(Money amount, long time, string memo) { _c.ApplyTransferIn(amount, time, memo); }
            public void Publish() { _c.PublishTreasury(); }
        }

        public static CapitalTransferService Create(TradingSession trading, CityGame city)
        {
            return new CapitalTransferService(new TradingSource(trading), new CityTarget(city), () => city.Clock.Time,
                new ReverseRoute { Source = new CitySource(city), Target = new TradingTarget(trading) });
        }
    }
}
