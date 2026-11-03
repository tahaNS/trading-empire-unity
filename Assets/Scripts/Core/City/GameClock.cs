using System;
using System.Collections.Generic;

namespace TradingEmpire.Core
{
    /// <summary>
    /// Central simulation clock with a fixed timestep: real elapsed time is accumulated and the simulation advances in
    /// whole ticks (1 sim hour). Pure C#; driven by the game loop.
    /// </summary>
    public sealed class GameClock
    {
        public const long HourMs = 3600000;
        public const long DayMs = 24 * HourMs;
        /// <summary>Simulation start: 1 Jan 2024 00:00 UTC.</summary>
        public static readonly long SimStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        public static readonly int[] Speeds = { 0, 1, 2, 5, 10 };
        /// <summary>At 1x, one real second advances the simulation by this many sim days (a 30-day month takes 150 s at 1x).</summary>
        public const double SimDaysPerRealSecond = 0.2;
        /// <summary>Max real seconds accepted per advance(), so a hidden tab or a long frame never produces a burst of income.</summary>
        public const double MaxRealDtSeconds = 0.25;

        readonly long _tickMs;
        long _simTime;
        double _accumulator;
        int _speed = 1;

        /// <summary>Raised once per simulated tick with the new sim time.</summary>
        public event Action<long> Tick;
        /// <summary>Raised when the day or the speed changed.</summary>
        public event Action ViewChanged;

        public GameClock(long start = -1, long tickMs = HourMs)
        {
            _simTime = start < 0 ? SimStart : start;
            _tickMs = tickMs;
        }

        public long Time { get { return _simTime; } }
        public int Speed { get { return _speed; } }
        public long Day { get { return FloorDiv(_simTime, DayMs); } }

        static long FloorDiv(long a, long b) { return (long)Math.Floor(a / (double)b); }

        /// <summary>Set time and speed from a save. Never advances time and never fires ticks.</summary>
        public void Restore(long time, int speed)
        {
            _simTime = time;
            _accumulator = 0;
            _speed = speed;
            if (ViewChanged != null) ViewChanged();
        }

        public void SetSpeed(int speed)
        {
            if (Array.IndexOf(Speeds, speed) < 0) throw new ArgumentOutOfRangeException("speed");
            _speed = speed;
            if (ViewChanged != null) ViewChanged();
        }

        /// <summary>Advance by real elapsed seconds. Returns the number of ticks run.</summary>
        public int Advance(double realDtSeconds)
        {
            if (_speed == 0 || !(realDtSeconds > 0)) return 0;
            double dt = Math.Min(realDtSeconds, MaxRealDtSeconds);
            _accumulator += dt * _speed * SimDaysPerRealSecond * DayMs;
            int ticks = 0;
            while (_accumulator >= _tickMs)
            {
                _accumulator -= _tickMs;
                _simTime += _tickMs;
                ticks++;
                if (Tick != null) Tick(_simTime);
            }
            if (ticks > 0 && ViewChanged != null) ViewChanged();
            return ticks;
        }

        /// <summary>Test tools only: run the simulation forward by whole days, tick by tick (income settles normally).</summary>
        public int SkipDays(double days)
        {
            int ticks = (int)Math.Floor(Math.Max(days, 0) * DayMs / _tickMs);
            for (int i = 0; i < ticks; i++)
            {
                _simTime += _tickMs;
                if (Tick != null) Tick(_simTime);
            }
            if (ticks > 0 && ViewChanged != null) ViewChanged();
            return ticks;
        }
    }
}
