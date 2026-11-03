using System;
using TradingEmpire.Core;

namespace TradingEmpire.Terminal
{
    /// <summary>Runs trading challenges: a separate account and market, apart from your game. Records results and grants rewards.</summary>
    public sealed class ChallengeController
    {
        readonly GameWorld _world;
        readonly Action<TradingSession> _bind;

        public ChallengeRun Run { get; private set; }
        /// <summary>The outcome once a run has ended, until the player dismisses it.</summary>
        public ChallengeStatus Result { get; private set; }
        public string RewardText { get; private set; }
        public event Action Changed;

        public ChallengeController(GameWorld world, Action<TradingSession> bind) { _world = world; _bind = bind; }

        public System.Collections.Generic.IReadOnlyDictionary<string, ChallengeResult> Results { get { return _world.Challenges.Results; } }

        public bool Active { get { return Run != null && !Run.Finished; } }

        public void Start(ChallengeDefinition def)
        {
            Run = new ChallengeRun(def);
            Result = null;
            RewardText = null;
            Run.OnSettled = OnSettled;
            Run.Changed += Notify;
            _bind(Run.Session);
            Notify();
        }

        void OnSettled(ChallengeStatus status)
        {
            bool firstWin = _world.Challenges.Record(Run.Def.Id, status);
            Result = status;
            if (firstWin && Run.Def.RewardUnlock != null)
            {
                if (_world.City.GrantUnlock(Run.Def.RewardUnlock)) RewardText = "Reward: " + Run.Def.RewardText + ".";
                _world.City.Announce(NoticeKind.Success, "Challenge won: " + Run.Def.Name + (RewardText != null ? " — " + Run.Def.RewardText : ""));
            }
            Notify();
        }

        /// <summary>Leave the challenge and return to your own game. An unfinished run is simply abandoned (nothing is recorded).</summary>
        public void Leave()
        {
            Run = null;
            Result = null;
            RewardText = null;
            _bind(_world.Trading);
            Notify();
        }

        public void Retry()
        {
            var def = Run != null ? Run.Def : null;
            if (def != null) Start(def);
        }

        void Notify() { if (Changed != null) Changed(); }
    }
}
