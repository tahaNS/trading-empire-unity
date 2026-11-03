using System;
using System.Collections.Generic;

namespace TradingEmpire.Core
{
    /// <summary>What the tutorial can see of the game. Steps finish when these numbers move, never from a click on the tutorial itself.</summary>
    public struct TutorialFacts
    {
        public bool Ready;
        public int Revealed, Orders, Closed, JournalEntries, Drawings, Indicators, Transfers;
    }

    public sealed class TutorialStep
    {
        public string Id, Title, Body;
        public bool Optional;
        public Func<TutorialFacts, TutorialFacts, bool> Done;
    }

    public enum TutorialStatus { New, Active, Done, Dismissed }

    /// <summary>The guided first trade: each step asks for one real action. Pure logic, no UI.</summary>
    public sealed class Tutorial
    {
        public static readonly TutorialStep[] Steps =
        {
            new TutorialStep { Id = "move", Title = "Move the market",
                Body = "Press Play, or Next candle, to reveal new candles. Time only moves when you tell it to, so you can stop and think at any moment.",
                Done = (n, s) => n.Revealed >= s.Revealed + 3 },
            new TutorialStep { Id = "place", Title = "Plan and place a trade",
                Body = "Click Long to bet the price goes up, or Short to bet it goes down. Three lines appear on the chart: your entry, a stop-loss and a take-profit. Check \"Risk at SL\" in the order panel (the most you lose if you are wrong), drag the lines if you want, then click Place order.",
                Done = (n, s) => n.Orders > s.Orders },
            new TutorialStep { Id = "close", Title = "See it through",
                Body = "Press Play and let the market move. The trade closes by itself when it reaches your stop-loss or take-profit, or you can close it yourself from the Open positions table.",
                Done = (n, s) => n.Closed > s.Closed },
            new TutorialStep { Id = "journal", Title = "Learn from it",
                Body = "Open the Journal tab, pick your trade, and add a tag or a short note about why you took it. Reviewing trades is how traders improve.",
                Done = (n, s) => n.JournalEntries > s.JournalEntries },
            new TutorialStep { Id = "tools", Title = "Mark up the chart", Optional = true,
                Body = "Try a drawing tool from the rail on the left: a trend line, a Fibonacci retracement or the Long / Short position box. Click one, then draw on the chart.",
                Done = (n, s) => n.Drawings > s.Drawings },
            new TutorialStep { Id = "indicators", Title = "Add an indicator", Optional = true,
                Body = "Open Indicators (top right of the chart) and add a moving average or the RSI. Values only use candles you have already seen.",
                Done = (n, s) => n.Indicators > s.Indicators },
            new TutorialStep { Id = "transfer", Title = "Fund your city", Optional = true,
                Body = "Profit is what builds the city. Go back to the city, open Finances and move some money from your trading account to the city treasury, then build.",
                Done = (n, s) => n.Transfers > s.Transfers },
        };

        public TutorialStatus Status = TutorialStatus.New;
        public int Step;
        TutorialFacts _start;

        public bool Finished { get { return Step >= Steps.Length; } }
        public TutorialStep Current { get { return Step < Steps.Length ? Steps[Step] : null; } }

        public void Start(TutorialFacts now) { Status = TutorialStatus.Active; Step = 0; _start = now; }

        /// <summary>Resume at a step (after a restart), with the current facts as the baseline.</summary>
        public void Resume(int step, TutorialFacts now) { Status = TutorialStatus.Active; Step = Math.Max(0, Math.Min(step, Steps.Length)); _start = now; if (Finished) Status = TutorialStatus.Done; }

        /// <summary>Move on if the current step's action has happened (one step at a time). True when it changed.</summary>
        public bool Advance(TutorialFacts now)
        {
            if (Status != TutorialStatus.Active || Finished || !now.Ready) return false;
            if (!Current.Done(now, _start)) return false;
            Step++; _start = now;
            if (Finished) Status = TutorialStatus.Done;
            return true;
        }

        /// <summary>Skip the current step; only optional ones may be skipped.</summary>
        public bool Skip(TutorialFacts now)
        {
            if (Status != TutorialStatus.Active || Finished || !Current.Optional) return false;
            Step++; _start = now;
            if (Finished) Status = TutorialStatus.Done;
            return true;
        }

        public void Dismiss() { if (Status == TutorialStatus.Active) Status = TutorialStatus.Dismissed; }
    }
}
