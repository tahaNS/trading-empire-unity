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

        string JournalSignature(SessionState s) { return s.Closed.Count + "|" + _journalKey + "|" + _journalVer + "|" + _insights + "|" + Mathf.RoundToInt(_positionRows.rect.width); }

        static string Stars(int n) { return new string('*', n) + new string('-', 5 - n); }

        void JournalTab(SessionState s)
        {
            var closed = s.Closed.Where(p => p.Exit != null).Reverse().Take(7).ToList();
            // everything is laid out from the width the tab really has, so a narrow window never makes the parts overlap
            float W = _positionRows.rect.width < 400 ? 1180f : _positionRows.rect.width;
            float listW = Mathf.Clamp(W * 0.27f, 250f, 460f), chartW = Mathf.Clamp(W * 0.24f, 220f, 330f), x0 = listW + 16f, rw = W - x0 - chartW - 22f;
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
                    + "  <color=" + color + ">" + p.Exit.Net.ToSignedString() + "</color>" + (r.HasValue ? "  " + r.Value.ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture) + "R" : "")
                    + (listW >= 400 ? "  " + Stars(e.Rating) + (e.Note.Length > 0 || e.Tags.Count > 0 ? "  *" : "") : "");
                var row = Ui.Button(_positionRows, label, () => { _journalKey = key; _journalSig = null; _dirty = true; }, key == _journalKey ? Theme.Blue : Theme.PanelAlt, 11);
                var t = row.transform.Find("Label").GetComponent<Text>();
                t.alignment = TextAnchor.MiddleLeft; t.fontStyle = FontStyle.Normal; t.font = Theme.Mono; t.fontSize = listW < 330 ? 10 : 11;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Stretch(t.rectTransform, 8, 0, 4, 0);
                Ui.Place((RectTransform)row.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -(i + 1) * 23), new Vector2(listW, -i * 23 - 1));
                _rows.Add(row.gameObject);
            }

            var sel = closed.First(p => JournalStore.KeyOf(p) == _journalKey);
            var entry = Session.Journal.Get(_journalKey);
            // the small chart of the trade: candles around it, entry, exit, stop and target
            _rows.Add(TradeChart.Create(_positionRows, Session, sel, new Rect(W - chartW, 0, chartW, 176)).gameObject);
            Func<float, float, float, float, (Vector2, Vector2)> box = (x, y, w, h) => (new Vector2(x, -(y + h)), new Vector2(x + w, -y));

            var toggle = Ui.Button(_positionRows, _insights ? "< Back to the trade" : "Insights >", () => { _insights = !_insights; _journalSig = null; _dirty = true; }, Theme.PanelAlt, 11);
            var tb = box(x0 + rw - 120, 0, 120, 22);
            Ui.Place((RectTransform)toggle.transform, new Vector2(0, 1), new Vector2(0, 1), tb.Item1, tb.Item2);
            _rows.Add(toggle.gameObject);

            if (_insights) { InsightsView(entries, x0, rw); return; }

            var noteLabel = Ui.Label(_positionRows, "NOTE", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            var nb = box(x0, 0, 100, 16);
            Ui.Place(noteLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), nb.Item1, nb.Item2);
            _rows.Add(noteLabel.gameObject);
            var input = Ui.Input(_positionRows, entry.Note, 12);
            input.lineType = InputField.LineType.MultiLineNewline;
            input.characterLimit = JournalStore.MaxNote;
            input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            var ib = box(x0, 24, rw, 44);
            Ui.Place((RectTransform)input.transform, new Vector2(0, 1), new Vector2(0, 1), ib.Item1, ib.Item2);
            string key2 = _journalKey;
            input.onEndEdit.AddListener(text => Session.Journal.Set(key2, text));
            var ph = Ui.Label(input.transform, "What was the plan? What did you see?", 11, Theme.Dim, TextAnchor.UpperLeft, true);
            Ui.Stretch(ph.rectTransform, 8, 2, 8, 2);
            input.placeholder = ph;
            _rows.Add(input.gameObject);

            var rl = Ui.Label(_positionRows, "YOUR RATING", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            var rb = box(x0, 74, 96, 24);
            Ui.Place(rl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), rb.Item1, rb.Item2);
            _rows.Add(rl.gameObject);
            for (int n = 1; n <= 5; n++)
            {
                int rating = n;
                var b = Ui.Button(_positionRows, n.ToString(), () => Session.Journal.Set(key2, null, null, entry.Rating == rating ? 0 : rating), n <= entry.Rating ? Theme.Accent : Theme.PanelAlt, 12, n <= entry.Rating ? Color.black : (Color?)null);
                var bb = box(x0 + 100 + (n - 1) * 36, 74, 32, 24);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), bb.Item1, bb.Item2);
                _rows.Add(b.gameObject);
            }
            var why = Ui.Label(_positionRows, "Closed by " + sel.Exit.Reason + ". Rate how well you traded it, not how it ended.", 10, Theme.Dim, TextAnchor.MiddleLeft);
            var wb = box(x0 + 290, 74, Mathf.Max(0, rw - 290), 24);
            why.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(why.rectTransform, new Vector2(0, 1), new Vector2(0, 1), wb.Item1, wb.Item2);
            if (rw - 290 < 140) Ui.Kill(why.gameObject); // no room beside the rating buttons: leave the note out rather than let it wrap into a column
            else _rows.Add(why.gameObject);

            var tl = Ui.Label(_positionRows, "TAGS", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            var tgb = box(x0, 104, 100, 14);
            Ui.Place(tl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), tgb.Item1, tgb.Item2);
            _rows.Add(tl.gameObject);
            // the tags flow like words: as many per line as fit, and the chips shrink when the panel is short
            float availH = Mathf.Max(40f, (_tabContent.rect.height < 60 ? 190f : _tabContent.rect.height) - 122f);
            float chipH = availH >= 3 * 28 ? 24f : availH >= 2 * 26 ? 22f : 18f;
            int fontSize = chipH >= 22f ? 10 : 9;
            float cx = 0, cy = 0;
            for (int i = 0; i < JournalStore.PresetTags.Length; i++)
            {
                string tag = JournalStore.PresetTags[i];
                bool on = entry.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
                float w = Mathf.Min(rw, tag.Length * (fontSize * 0.62f) + 20f);
                if (cx + w > rw && cx > 0) { cx = 0; cy += chipH + 4f; }
                var b = Ui.Button(_positionRows, tag, () => Session.Journal.ToggleTag(key2, tag), on ? Theme.Blue : Theme.PanelAlt, fontSize);
                var cb = box(x0 + cx, 122 + cy, w, chipH);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), cb.Item1, cb.Item2);
                _rows.Add(b.gameObject);
                cx += w + 4f;
            }
        }

        void InsightsView(IReadOnlyDictionary<string, JournalEntry> entries, float x0, float rw)
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
            Ui.Place(lab.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -30), new Vector2(x0 + rw, 0));
            _rows.Add(lab.gameObject);
        }
    }
}
