using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingEmpire.Core
{
    /// <summary>A note, tags and a star rating for one closed trade. Rating: 1-5 stars, how well you traded it, not how it ended; 0 = none.</summary>
    public sealed class JournalEntry
    {
        public string Note = "";
        public List<string> Tags = new List<string>();
        public int Rating;

        public bool IsEmpty { get { return Note.Length == 0 && Tags.Count == 0 && Rating == 0; } }
        public JournalEntry Copy() { return new JournalEntry { Note = Note, Tags = new List<string>(Tags), Rating = Rating }; }
    }

    public sealed class GroupStat
    {
        public string Label;
        public int Trades, Wins;
        public Money Net;
        /// <summary>Average R over the trades that had a stop-loss; null if none did.</summary>
        public double? AvgR;
    }

    public sealed class JournalStats
    {
        public List<GroupStat> ByTag, BySession, ByHour;
        public GroupStat Untagged;
    }

    /// <summary>The trade journal: a note, tags and a rating per closed trade, plus statistics by tag, session and hour. Saved with the game.</summary>
    public sealed class JournalStore
    {
        public static readonly string[] PresetTags =
        {
            "Good setup", "Followed plan", "Patient", "Early entry", "Late entry", "Chased price", "Revenge trade", "FOMO", "Moved stop",
            "Cut winner early", "No stop-loss", "Overtraded", "News",
        };

        public const int MaxNote = 1000, MaxTags = 12, MaxTagLength = 30;

        Dictionary<string, JournalEntry> _entries = new Dictionary<string, JournalEntry>();
        public IReadOnlyDictionary<string, JournalEntry> Entries { get { return _entries; } }
        public event Action Changed;

        /// <summary>A trade's key: position ids restart with each replay run, so the run is part of it.</summary>
        public static string KeyOf(Position p) { return p.RunId + ":" + p.Id; }

        public static string CleanTag(string raw)
        {
            string t = System.Text.RegularExpressions.Regex.Replace((raw ?? "").Trim(), @"\s+", " ");
            return t.Length > MaxTagLength ? t.Substring(0, MaxTagLength) : t;
        }

        public static JournalEntry Clean(string note, IEnumerable<string> tags, int rating)
        {
            var clean = new List<string>();
            foreach (var t in tags ?? new string[0])
            {
                var c = CleanTag(t);
                if (c.Length > 0 && !clean.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))) clean.Add(c);
            }
            string n = note ?? "";
            return new JournalEntry { Note = n.Length > MaxNote ? n.Substring(0, MaxNote) : n, Tags = clean.Take(MaxTags).ToList(), Rating = rating >= 1 && rating <= 5 ? rating : 0 };
        }

        public JournalEntry Get(string key) { return _entries.TryGetValue(key, out var e) ? e : new JournalEntry(); }

        /// <summary>Change part of an entry (null = keep); an entry with nothing in it is removed.</summary>
        public void Set(string key, string note = null, IEnumerable<string> tags = null, int? rating = null)
        {
            var cur = Get(key);
            var next = Clean(note ?? cur.Note, tags ?? cur.Tags, rating ?? cur.Rating);
            var rest = new Dictionary<string, JournalEntry>(_entries);
            if (next.IsEmpty) rest.Remove(key); else rest[key] = next;
            _entries = rest;
            if (Changed != null) Changed();
        }

        public void ToggleTag(string key, string tag)
        {
            var cur = Get(key).Tags;
            bool has = cur.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            Set(key, null, has ? cur.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList() : cur.Concat(new[] { tag }).ToList());
        }

        public Dictionary<string, JournalEntry> Snapshot() { return _entries.ToDictionary(kv => kv.Key, kv => kv.Value.Copy()); }

        public void Restore(IReadOnlyDictionary<string, JournalEntry> entries)
        {
            var output = new Dictionary<string, JournalEntry>();
            foreach (var kv in entries)
            {
                var e = Clean(kv.Value.Note, kv.Value.Tags, kv.Value.Rating);
                if (!e.IsEmpty) output[kv.Key] = e;
            }
            _entries = output;
            if (Changed != null) Changed();
        }

        // ---------- statistics ----------

        /// <summary>Which forex session an hour (UTC) belongs to, one label per hour.</summary>
        public static string SessionOfHour(int hour)
        {
            if (hour >= 0 && hour < 7) return "Asia";
            if (hour >= 7 && hour < 13) return "London";
            if (hour >= 13 && hour < 21) return "New York";
            return "Off hours";
        }

        /// <summary>Result in R (net / the risk planned at entry); null if the trade had no stop-loss.</summary>
        public static double? TradeR(Position p)
        {
            if (p.Exit == null || !p.InitialRisk.HasValue || p.InitialRisk.Value.Cents <= 0) return null;
            return (double)p.Exit.Net.Cents / p.InitialRisk.Value.Cents;
        }

        static GroupStat Group(string label, IReadOnlyCollection<Position> ps)
        {
            Money net = Money.Zero;
            int wins = 0, rN = 0;
            double rSum = 0;
            foreach (var p in ps)
            {
                var n = p.Exit != null ? p.Exit.Net : Money.Zero;
                net = net + n;
                if (n.Cents > 0) wins++;
                var r = TradeR(p);
                if (r.HasValue) { rSum += r.Value; rN++; }
            }
            return new GroupStat { Label = label, Trades = ps.Count, Wins = wins, Net = net, AvgR = rN > 0 ? rSum / rN : (double?)null };
        }

        public static int EntryHour(Position p) { return DateTimeOffset.FromUnixTimeMilliseconds(p.EntryTime).UtcDateTime.Hour; }

        public static JournalStats StatsOf(IEnumerable<Position> closed, IReadOnlyDictionary<string, JournalEntry> entries)
        {
            var done = closed.Where(p => p.Exit != null).ToList();
            var tags = new Dictionary<string, List<Position>>();
            var untagged = new List<Position>();
            foreach (var p in done)
            {
                var t = entries.TryGetValue(KeyOf(p), out var e) ? e.Tags : new List<string>();
                if (t.Count == 0) untagged.Add(p);
                foreach (var tag in t)
                {
                    if (!tags.TryGetValue(tag, out var list)) tags[tag] = list = new List<Position>();
                    list.Add(p);
                }
            }
            string[] sessions = { "Asia", "London", "New York", "Off hours" };
            return new JournalStats
            {
                ByTag = tags.Select(kv => Group(kv.Key, kv.Value)).OrderByDescending(g => g.Trades).ThenBy(g => g.Label, StringComparer.Ordinal).ToList(),
                BySession = sessions.Select(s => Group(s, done.Where(p => SessionOfHour(EntryHour(p)) == s).ToList())).ToList(),
                ByHour = Enumerable.Range(0, 24).Select(h => Group(h.ToString("00"), done.Where(p => EntryHour(p) == h).ToList())).ToList(),
                Untagged = Group("Untagged", untagged),
            };
        }
    }
}
