using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TradingEmpire.Core
{
    public enum SoundId { Click, Success, Error, Build, Upgrade, Finished, Coin, TradeOpen, TradeWin, TradeLoss, Mission }
    public enum Wave { Sine, Triangle, Square, Sawtooth }

    public sealed class Note
    {
        public double Freq;
        /// <summary>Start offset in seconds.</summary>
        public double Start, Duration;
        public Wave Type;
        public double Gain;
        /// <summary>Optional pitch glide target (0 = none).</summary>
        public double SlideTo;
        public Note(double freq, double start, double duration, Wave type, double gain, double slideTo = 0)
        {
            Freq = freq; Start = start; Duration = duration; Type = type; Gain = gain; SlideTo = slideTo;
        }
    }

    /// <summary>All sounds are synthesised from these recipes, so there are no audio files to ship or license (identical to the web version).</summary>
    public static class Sounds
    {
        public static readonly IReadOnlyDictionary<SoundId, Note[]> Recipes = new Dictionary<SoundId, Note[]>
        {
            [SoundId.Click] = new[] { new Note(620, 0, 0.04, Wave.Square, 0.05) },
            [SoundId.Success] = new[] { new Note(523, 0, 0.09, Wave.Triangle, 0.18), new Note(784, 0.08, 0.16, Wave.Triangle, 0.18) },
            [SoundId.Error] = new[] { new Note(170, 0, 0.22, Wave.Sawtooth, 0.14, 120) },
            [SoundId.Build] = new[] { new Note(130, 0, 0.18, Wave.Sine, 0.35, 60), new Note(330, 0.04, 0.1, Wave.Triangle, 0.1) },
            [SoundId.Upgrade] = new[] { new Note(392, 0, 0.09, Wave.Triangle, 0.16), new Note(523, 0.09, 0.09, Wave.Triangle, 0.16), new Note(659, 0.18, 0.18, Wave.Triangle, 0.16) },
            [SoundId.Finished] = new[]
            {
                new Note(523, 0, 0.08, Wave.Triangle, 0.17), new Note(659, 0.07, 0.08, Wave.Triangle, 0.17), new Note(784, 0.14, 0.08, Wave.Triangle, 0.17),
                new Note(1047, 0.21, 0.3, Wave.Sine, 0.2), new Note(1568, 0.24, 0.2, Wave.Sine, 0.07),
            },
            [SoundId.Coin] = new[] { new Note(988, 0, 0.07, Wave.Sine, 0.14), new Note(1319, 0.06, 0.18, Wave.Sine, 0.14) },
            [SoundId.TradeOpen] = new[] { new Note(440, 0, 0.06, Wave.Square, 0.07), new Note(660, 0.06, 0.08, Wave.Square, 0.07) },
            [SoundId.TradeWin] = new[] { new Note(523, 0, 0.09, Wave.Triangle, 0.17), new Note(659, 0.08, 0.09, Wave.Triangle, 0.17), new Note(784, 0.16, 0.2, Wave.Triangle, 0.17) },
            [SoundId.TradeLoss] = new[] { new Note(330, 0, 0.14, Wave.Triangle, 0.16, 250), new Note(247, 0.13, 0.22, Wave.Triangle, 0.16, 196) },
            [SoundId.Mission] = new[]
            {
                new Note(523, 0, 0.1, Wave.Triangle, 0.2), new Note(659, 0.1, 0.1, Wave.Triangle, 0.2), new Note(784, 0.2, 0.1, Wave.Triangle, 0.2), new Note(1047, 0.3, 0.35, Wave.Triangle, 0.22),
            },
        };

        /// <summary>Which sound (if any) a notice toast should play. Info notices are silent.</summary>
        public static SoundId? ForNotice(NoticeKind kind, string text)
        {
            if (text.StartsWith("Mission complete", StringComparison.Ordinal)) return SoundId.Mission;
            if (kind == NoticeKind.Error) return Regex.IsMatch(text, "Closed|Stop-loss hit|Stop-out") ? SoundId.TradeLoss : SoundId.Error;
            if (kind == NoticeKind.Info && text.Contains("finished")) return SoundId.Finished;
            if (kind == NoticeKind.Success)
            {
                if (Regex.IsMatch(text, "upgraded", RegexOptions.IgnoreCase)) return SoundId.Upgrade;
                if (Regex.IsMatch(" " + text + " ", " built | purchased| ordered ")) return SoundId.Build;
                if (text.StartsWith("Opened", StringComparison.Ordinal)) return SoundId.TradeOpen;
                if (text.Contains("Take-profit hit") || text.StartsWith("Closed", StringComparison.Ordinal)) return SoundId.TradeWin;
                return SoundId.Success;
            }
            return null;
        }
    }

    /// <summary>Turns a recipe into samples, the way the web version's oscillators and gain envelopes do.</summary>
    public static class SoundSynth
    {
        public const int SampleRate = 44100;

        static double Osc(Wave w, double phase)
        {
            double f = phase - Math.Floor(phase); // 0..1
            switch (w)
            {
                case Wave.Sine: return Math.Sin(2 * Math.PI * f);
                case Wave.Square: return f < 0.5 ? 1 : -1;
                case Wave.Sawtooth: return 2 * f - 1;
                default: return f < 0.25 ? 4 * f : f < 0.75 ? 2 - 4 * f : 4 * f - 4; // triangle
            }
        }

        /// <summary>Total length in seconds of a recipe (the last note's end, plus a short tail).</summary>
        public static double LengthOf(IEnumerable<Note> notes)
        {
            double end = 0;
            foreach (var n in notes) end = Math.Max(end, n.Start + n.Duration + 0.02);
            return end;
        }

        public static float[] Render(IEnumerable<Note> notes, int sampleRate = SampleRate)
        {
            var list = new List<Note>(notes);
            var samples = new float[(int)Math.Ceiling(LengthOf(list) * sampleRate)];
            foreach (var n in list)
            {
                int first = (int)(n.Start * sampleRate);
                int count = (int)(n.Duration * sampleRate);
                double phase = 0;
                for (int i = 0; i < count && first + i < samples.Length; i++)
                {
                    double t = (double)i / sampleRate;
                    double freq = n.SlideTo > 0 ? n.Freq * Math.Pow(n.SlideTo / n.Freq, t / n.Duration) : n.Freq; // exponential glide
                    phase += freq / sampleRate;
                    // attack: 0.0001 -> gain over 8 ms (linear), then exponential decay back to 0.0001
                    double attack = Math.Min(0.008, n.Duration);
                    double env = t < attack ? 0.0001 + (n.Gain - 0.0001) * (t / attack) : n.Gain * Math.Pow(0.0001 / n.Gain, (t - attack) / Math.Max(n.Duration - attack, 1e-6));
                    samples[first + i] += (float)(Osc(n.Type, phase) * env);
                }
            }
            for (int i = 0; i < samples.Length; i++) samples[i] = Math.Max(-1f, Math.Min(1f, samples[i]));
            return samples;
        }

        /// <summary>Looping brown noise for the city hum (low frequencies only), deterministic for a seed.</summary>
        public static float[] BrownNoise(int seconds = 2, int sampleRate = SampleRate, uint seed = 7)
        {
            var rng = new Mulberry32(seed);
            var data = new float[seconds * sampleRate];
            double last = 0;
            for (int i = 0; i < data.Length; i++)
            {
                last = (last + 0.02 * (rng.Next() * 2 - 1)) / 1.02;
                data[i] = (float)Math.Max(-1, Math.Min(1, last * 3.5));
            }
            // remove the slow drift so the last sample meets the first and the loop has no click
            float drift = data[data.Length - 1] - data[0];
            for (int i = 0; i < data.Length; i++) data[i] = Math.Max(-1f, Math.Min(1f, data[i] - drift * i / (data.Length - 1)));
            return data;
        }
    }
}
