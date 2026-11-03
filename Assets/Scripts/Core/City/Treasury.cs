using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum TransactionKind { InitialFunding, Transfer, TransferOut, Construction, Upgrade, Demolition, Land, Income, Expense }

    /// <summary>One auditable financial event. Amount is signed: positive = credit, negative = debit.</summary>
    public sealed class TransactionRecord
    {
        public string Id;
        /// <summary>Simulation time (ms since epoch).</summary>
        public long Time;
        public TransactionKind Kind;
        public Money Amount;
        public string Description;
        public string BuildingId;
        public Money BalanceAfter;
    }

    public sealed class PeriodSummary
    {
        public Money Income, Expenses, NetOperating, CapitalSpending, TransfersIn, TransfersOut;
    }

    public static class Transactions
    {
        /// <summary>Summarise transactions with fromExclusive &lt; time &lt;= toInclusive.</summary>
        public static PeriodSummary Summarize(IEnumerable<TransactionRecord> history, long fromExclusive, long toInclusive)
        {
            Money income = Money.Zero, expenses = Money.Zero, capital = Money.Zero, transfers = Money.Zero, transfersOut = Money.Zero;
            foreach (var r in history)
            {
                if (r.Time <= fromExclusive || r.Time > toInclusive) continue;
                switch (r.Kind)
                {
                    case TransactionKind.Income: income = income + r.Amount; break;
                    case TransactionKind.Expense: expenses = expenses - r.Amount; break;
                    case TransactionKind.Transfer: transfers = transfers + r.Amount; break;
                    case TransactionKind.TransferOut: transfersOut = transfersOut - r.Amount; break;
                    case TransactionKind.Construction:
                    case TransactionKind.Upgrade:
                    case TransactionKind.Land:
                    case TransactionKind.Demolition: capital = capital - r.Amount; break;
                }
            }
            return new PeriodSummary { Income = income, Expenses = expenses, NetOperating = income - expenses, CapitalSpending = capital, TransfersIn = transfers, TransfersOut = transfersOut };
        }
    }

    public sealed class SpendResult
    {
        public bool Ok;
        public TransactionRecord Record;
        public Money Shortfall;
    }

    /// <summary>
    /// The city's cash. Every change goes through Credit()/Debit() and is recorded, so balance == sum of all recorded
    /// signed amounts. The history list is replaced (copy-on-write) on each change so it can be used as a snapshot.
    /// </summary>
    public sealed class CityTreasury
    {
        Money _cash = Money.Zero;
        IReadOnlyList<TransactionRecord> _records = new List<TransactionRecord>();
        readonly IdSequence _nextId;

        public CityTreasury(IdSequence nextId) { _nextId = nextId; }

        /// <summary>Replace the treasury state from a validated save.</summary>
        public void Restore(Money balance, IEnumerable<TransactionRecord> history)
        {
            _cash = balance;
            _records = history.ToList();
            _nextId.ResetAfter(_records.Select(r => r.Id));
        }

        public Money Balance { get { return _cash; } }
        public IReadOnlyList<TransactionRecord> History { get { return _records; } }
        public bool CanAfford(Money amount) { return _cash >= amount; }

        TransactionRecord Post(TransactionKind kind, Money signed, long time, string description, string buildingId)
        {
            _cash = _cash + signed;
            var record = new TransactionRecord { Id = _nextId.Next(), Time = time, Kind = kind, Amount = signed, Description = description, BalanceAfter = _cash, BuildingId = buildingId };
            _records = new List<TransactionRecord>(_records) { record };
            return record;
        }

        public TransactionRecord Credit(TransactionKind kind, Money amount, long time, string description, string buildingId = null)
        {
            if (amount.Cents <= 0) throw new ArgumentOutOfRangeException("amount", "credit amount must be positive");
            return Post(kind, amount, time, description, buildingId);
        }

        /// <summary>
        /// Spend money. Fails (and changes nothing) if funds are insufficient, unless <paramref name="allowNegative"/> is
        /// set. Only mandatory operating expenses use allowNegative.
        /// </summary>
        public SpendResult Debit(TransactionKind kind, Money amount, long time, string description, string buildingId = null, bool allowNegative = false)
        {
            if (amount.Cents <= 0) throw new ArgumentOutOfRangeException("amount", "debit amount must be positive");
            if (!allowNegative && _cash < amount) return new SpendResult { Ok = false, Shortfall = amount - _cash };
            return new SpendResult { Ok = true, Record = Post(kind, -amount, time, description, buildingId) };
        }
    }
}
