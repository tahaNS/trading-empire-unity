using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>The trade journal tab: pick a closed trade, then add a note, tags and a rating, or read the insights.</summary>
    public sealed partial class TerminalView
    {
        string _journalSig;
        string _journalKey;
        bool _insights;
        int _journalVer;

        void AttachJournal() { Session.Journal.Changed += OnJournalChanged; }
        void OnJournalChanged() { _journalVer++; _dirty = true; }

        string JournalSignature(SessionState s) { return s.Closed.Count + "|" + _journalKey + "|" + _journalVer + "|" + _insights; }

        static string Stars(int n) { return new string('*', n) + new string('-', 5 - n); }

        void JournalTab(SessionState s)
        {
            var closed = s.Closed.Where(p => p.Exit != null).Reverse().Take(7).ToList();
            if (closed.Count == 0) { _tabBody.text = "<color=#8594ad>Close a trade, then add a note, tags and a star rating here. The insights show which habits help.</color>"; return; }
            if (_journalKey == null || !closed.Any(p => JournalStore.KeyOf(p) == _journalKey)) _journalKey = JournalStore.KeyOf(closed[0]);
            var entries = Session.Journal.Entries;

            for (int i = 0; i < closed.Count; i++)
            {
                var p = closed[i];
                string key = JournalStore.KeyOf(p);
                var e = Session.Journal.Get(key);
                int d = Instruments.DigitsFor(p.Symbol, 5);
                string color = p.Exit.Net.Cents >= 0 ? "#26c295" : "#ed5259";
                var r = JournalStore.TradeR(p);
                string label = (p.Direction == Direction.Buy ? "BUY " : "SELL") + " " + Units.FormatLots(p.Lots) + "  " + Units.FormatPrice(p.EntryPrice, d) + " > " + Units.FormatPrice(p.Exit.Price, d)
                    + "  <color=" + color + ">" + p.Exit.Net.ToSignedString() + "</color>" + (r.HasValue ? "  " + r.Value.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture) + "R" : "") + "  " + Stars(e.Rating)
                    + (e.Note.Length > 0 || e.Tags.Count > 0 ? "  *" : "");
                var row = Ui.Button(_positionRows, label, () => { _journalKey = key; _journalSig = null; _dirty = true; }, key == _journalKey ? Theme.Blue : Theme.PanelAlt, 11);
                var t = row.transform.Find("Label").GetComponent<Text>();
                t.alignment = TextAnchor.MiddleLeft; t.fontStyle = FontStyle.Normal; t.font = Theme.Mono;
                Ui.Stretch(t.rectTransform, 8, 0, 4, 0);
                Ui.Place((RectTransform)row.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -(i + 1) * 23), new Vector2(600, -i * 23 - 1));
                _rows.Add(row.gameObject);
            }

            var sel = closed.First(p => JournalStore.KeyOf(p) == _journalKey);
            var entry = Session.Journal.Get(_journalKey);
            const float x0 = 620;

            var toggle = Ui.Button(_positionRows, _insights ? "< Back to the trade" : "Insights >", () => { _insights = !_insights; _journalSig = null; _dirty = true; }, Theme.PanelAlt, 11);
            Ui.Place((RectTransform)toggle.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1110, -26), new Vector2(1226, -2));
            _rows.Add(toggle.gameObject);

            if (_insights) { InsightsView(entries); return; }

            var noteLabel = Ui.Label(_positionRows, "NOTE", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(noteLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -16), new Vector2(x0 + 100, -2));
            _rows.Add(noteLabel.gameObject);
            var input = Ui.Input(_positionRows, entry.Note, 12);
            input.lineType = InputField.LineType.MultiLineNewline;
            input.characterLimit = JournalStore.MaxNote;
            input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Place((RectTransform)input.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -78), new Vector2(x0 + 400, -18));
            string key2 = _journalKey;
            input.onEndEdit.AddListener(text => Session.Journal.Set(key2, text));
            var ph = Ui.Label(input.transform, "What was the plan? What did you see?", 11, Theme.Dim, TextAnchor.UpperLeft, true);
            Ui.Stretch(ph.rectTransform, 8, 2, 8, 2);
            input.placeholder = ph;
            _rows.Add(input.gameObject);

            var rl = Ui.Label(_positionRows, "YOUR RATING", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(rl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + 420, -16), new Vector2(x0 + 620, -2));
            _rows.Add(rl.gameObject);
            for (int n = 1; n <= 5; n++)
            {
                int rating = n;
                var b = Ui.Button(_positionRows, n.ToString(), () => Session.Journal.Set(key2, null, null, entry.Rating == rating ? 0 : rating), n <= entry.Rating ? Theme.Accent : Theme.PanelAlt, 12, n <= entry.Rating ? Color.black : (Color?)null);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + 420 + (n - 1) * 36, -48), new Vector2(x0 + 420 + (n - 1) * 36 + 32, -20));
                _rows.Add(b.gameObject);
            }

            var tl = Ui.Label(_positionRows, "TAGS", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(tl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -94), new Vector2(x0 + 100, -80));
            _rows.Add(tl.gameObject);
            for (int i = 0; i < JournalStore.PresetTags.Length; i++)
            {
                string tag = JournalStore.PresetTags[i];
                bool on = entry.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
                var b = Ui.Button(_positionRows, tag, () => Session.Journal.ToggleTag(key2, tag), on ? Theme.Blue : Theme.PanelAlt, 11);
                int col = i % 6, row = i / 6;
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + col * 102, -122 - row * 28), new Vector2(x0 + col * 102 + 98, -98 - row * 28));
                _rows.Add(b.gameObject);
            }
            var why = Ui.Label(_positionRows, "Closed by " + sel.Exit.Reason + ". Rate how well you traded it, not how it ended.", 10, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(why.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0 + 50, -94), new Vector2(x0 + 600, -80));
            _rows.Add(why.gameObject);
        }

        void InsightsView(IReadOnlyDictionary<string, JournalEntry> entries)
        {
            var st = JournalStore.StatsOf(Session.State.Closed, entries);
            Func<GroupStat, string> line = g => g.Label.PadRight(12) + (g.Trades + " trades").PadRight(12) + (g.Trades > 0 ? ((double)g.Wins / g.Trades * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% win" : "-").PadRight(10)
                + ("avg R " + (g.AvgR.HasValue ? g.AvgR.Value.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) : "-")).PadRight(14) + g.Net.ToSignedString();
            var sb = new System.Text.StringBuilder();
            sb.Append("BY SESSION\n");
            foreach (var g in st.BySession.Where(x => x.Trades > 0)) sb.Append(line(g)).Append('\n');
            sb.Append("\nBY TAG  (tag your trades to see which habits cost or earn money)\n");
            foreach (var g in st.ByTag.Take(4)) sb.Append(line(g)).Append('\n');
            if (st.Untagged.Trades > 0) sb.Append(line(st.Untagged)).Append('\n');
            var best = st.ByHour.Where(h => h.Trades >= 2).OrderByDescending(h => h.Net.Cents).FirstOrDefault();
            if (best != null) sb.Append("\nBest hour (UTC): ").Append(best.Label).Append(":00  ").Append(best.Net.ToSignedString());
            var lab = Ui.Label(_positionRows, sb.ToString(), 11, Theme.Text, TextAnchor.UpperLeft, true);
            Ui.Place(lab.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(620, -180), new Vector2(1230, 0));
            _rows.Add(lab.gameObject);
        }
    }
}
