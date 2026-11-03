using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    public enum AccountTransactionKind { Initial, Deposit, Withdrawal, TradeClose }

    public sealed class AccountTransaction
    {
        public string Id;
        public long Time;
        public AccountTransactionKind Kind;
        /// <summary>Signed: + increases balance.</summary>
        public Money Amount;
        public string Description;
        public Money BalanceAfter;
    }

    public sealed class AccountSnapshot
    {
        public Money StartingBalance, Balance, RealizedPnl, Deposits, Withdrawals;
        public IReadOnlyList<AccountTransaction> Transactions;
        public IReadOnlyList<Position> Open;
        public IReadOnlyList<Position> Closed;
        public IReadOnlyList<PendingOrder> Pending;
    }

    public sealed class AccountView
    {
        public Money Balance;
        /// <summary>balance + unrealized P/L - estimated commission on open positions.</summary>
        public Money Equity;
        public Money UsedMargin;
        public Money FreeMargin;
        /// <summary>Unrealized P/L net of the commission that will be charged on close.</summary>
        public Money UnrealizedPnl;
        public Money RealizedPnl;
    }

    /// <summary>
    /// Trading account state. Balance only changes through recorded transactions:
    /// balance = starting balance + deposits - withdrawals + realized net P/L of closed trades.
    /// </summary>
    public sealed class TradingAccount
    {
        Money _start;
        Money _cash;
        Money _realized = Money.Zero;
        Money _deposited = Money.Zero;
        Money _withdrawn = Money.Zero;
        List<AccountTransaction> _txs = new List<AccountTransaction>();
        readonly IdSequence _nextId;

        public readonly List<Position> Open = new List<Position>();
        /// <summary>Resting orders. They reserve no margin; margin is checked when they fill.</summary>
        public readonly List<PendingOrder> Pending = new List<PendingOrder>();
        public List<Position> Closed = new List<Position>();

        public TradingAccount(Money start, IdSequence nextId, long startTime)
        {
            _start = start;
            _cash = start;
            _nextId = nextId;
            _txs.Add(new AccountTransaction
            {
                Id = _nextId.Next(), Time = startTime, Kind = AccountTransactionKind.Initial,
                Amount = start, Description = "Starting balance", BalanceAfter = start,
            });
        }

        public Money StartingBalance { get { return _start; } }
        public Money Balance { get { return _cash; } }
        public Money RealizedPnl { get { return _realized; } }
        public Money TotalDeposits { get { return _deposited; } }
        public Money TotalWithdrawals { get { return _withdrawn; } }
        public IReadOnlyList<AccountTransaction> Transactions { get { return _txs; } }

        void Post(AccountTransactionKind kind, Money amount, long time, string description)
        {
            _cash = _cash + amount;
            _txs = new List<AccountTransaction>(_txs)
            {
                new AccountTransaction { Id = _nextId.Next(), Time = time, Kind = kind, Amount = amount, Description = description, BalanceAfter = _cash },
            };
        }

        /// <summary>Apply the final result of a closed trade (net of commission) to the balance.</summary>
        public void SettleTrade(Position position, Money net, long time)
        {
            _realized = _realized + net;
            Post(AccountTransactionKind.TradeClose, net, time,
                "Closed " + (position.Direction == Direction.Buy ? "buy" : "sell") + " " + position.Symbol);
            int i = Open.FindIndex(p => p.Id == position.Id);
            if (i >= 0) Open.RemoveAt(i);
            Closed = new List<Position>(Closed) { position };
        }

        public void Deposit(Money amount, long time, string description = "Deposit")
        {
            if (amount.Cents <= 0) throw new ArgumentOutOfRangeException("amount", "Deposit must be positive");
            _deposited = _deposited + amount;
            Post(AccountTransactionKind.Deposit, amount, time, description);
        }

        /// <summary>Test tools only: add money as extra starting capital, keeping the ledger consistent.</summary>
        public void Grant(Money amount, long time, string description = "God mode funds")
        {
            if (amount.Cents <= 0) throw new ArgumentOutOfRangeException("amount", "Grant must be positive");
            _start = _start + amount;
            Post(AccountTransactionKind.Initial, amount, time, description);
        }

        /// <summary>Withdraw from the balance. Callers must check eligibility first; this refuses overdrafts.</summary>
        public bool Withdraw(Money amount, long time, string description = "Withdrawal")
        {
            if (amount.Cents <= 0 || amount > _cash) return false;
            _withdrawn = _withdrawn + amount;
            Post(AccountTransactionKind.Withdrawal, -amount, time, description);
            return true;
        }

        public AccountSnapshot Snapshot()
        {
            return new AccountSnapshot
            {
                StartingBalance = _start, Balance = _cash, RealizedPnl = _realized,
                Deposits = _deposited, Withdrawals = _withdrawn,
                Transactions = _txs, Open = Open.ToList(), Closed = Closed.ToList(),
                Pending = Pending.Select(o => o).ToList(),
            };
        }

        /// <summary>Replace the account state from a validated save.</summary>
        public void Restore(AccountSnapshot s)
        {
            _start = s.StartingBalance;
            _cash = s.Balance;
            _realized = s.RealizedPnl;
            _deposited = s.Deposits;
            _withdrawn = s.Withdrawals;
            _txs = new List<AccountTransaction>(s.Transactions);
            Open.Clear(); Open.AddRange(s.Open);
            Closed = new List<Position>(s.Closed);
            Pending.Clear(); Pending.AddRange(s.Pending);
            _nextId.ResetAfter(s.Transactions.Select(t => t.Id));
        }

        /// <summary>Realized-funds check helper used by tests.</summary>
        public Money ExpectedBalance { get { return _start + _deposited + _realized - _withdrawn; } }
    }
}
