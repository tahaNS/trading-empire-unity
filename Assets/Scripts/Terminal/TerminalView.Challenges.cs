using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>The challenge list, the bar shown during a run, and the result dialog.</summary>
    public sealed partial class TerminalView
    {
        RectTransform _chList, _chResult, _chBanner;
        Text _chBannerText, _chResultTitle, _chResultText;
        readonly List<GameObject> _chRows = new List<GameObject>();

        void BuildChallengeUi(RectTransform root)
        {
            if (Challenges == null) return;
            Challenges.Changed += () => _dirty = true;

            _chList = Modal(root, "ChallengeList", 760, 500, out var listWin);
            var head = Ui.Label(listWin, "TRADING CHALLENGES", 14, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -34), new Vector2(-100, -6));
            var intro = Ui.Label(listWin, "Practice runs with a fresh $10,000 on a made-up market. They never touch your real account or city. Win them for stars and, for some, a city policy.", 11, Theme.Dim, TextAnchor.UpperLeft);
            intro.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(intro.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -72), new Vector2(-16, -36));
            var close = Ui.Button(listWin, "Close", () => ToggleChallengeList(false), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-74, -32), new Vector2(-12, -8));

            _chResult = Modal(root, "ChallengeResult", 520, 300, out var resWin);
            _chResultTitle = Ui.Label(resWin, "", 22, Theme.Text, TextAnchor.MiddleCenter, false, true);
            Ui.Place(_chResultTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -56), new Vector2(0, -12));
            _chResultText = Ui.Label(resWin, "", 13, Theme.Text, TextAnchor.UpperCenter);
            _chResultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_chResultText.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 70), new Vector2(-20, -64));
            var back = Ui.Button(resWin, "Back to my game", () => Challenges.Leave(), Theme.PanelAlt, 13);
            Ui.Place((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(20, 20), new Vector2(-6, 56));
            var retry = Ui.Button(resWin, "Try again", () => Challenges.Retry(), Theme.Blue, 13);
            Ui.Place((RectTransform)retry.transform, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(6, 20), new Vector2(-20, 56));
            _chResult.gameObject.SetActive(false);

            _chBanner = Ui.Rect(root, "ChallengeBanner");
            Ui.Place(_chBanner, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -122), new Vector2(-346, -88));
            Ui.Panel(_chBanner, "Bg", new Color(0.35f, 0.25f, 0.05f, 0.95f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_chBanner.Find("Bg"));
            _chBannerText = Ui.Label(_chBanner, "", 12, Theme.Accent, TextAnchor.MiddleLeft, true, true);
            Ui.Place(_chBannerText.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-230, 0));
            var finish = Ui.Button(_chBanner, "Finish now", () => { if (Challenges.Run != null) Challenges.Run.FinishNow(); }, Theme.PanelAlt, 11);
            Ui.Place((RectTransform)finish.transform, Vector2.one, Vector2.one, new Vector2(-216, -28), new Vector2(-116, -4));
            var leave = Ui.Button(_chBanner, "Leave", () => Challenges.Leave(), Theme.PanelAlt, 11);
            Ui.Place((RectTransform)leave.transform, Vector2.one, Vector2.one, new Vector2(-108, -28), new Vector2(-8, -4));
            _chBanner.gameObject.SetActive(false);
            _chList.gameObject.SetActive(false);
        }

        static RectTransform Modal(RectTransform root, string name, float w, float h, out RectTransform window)
        {
            var m = Ui.Rect(root, name);
            Ui.Stretch(m);
            var dim = Ui.Box(m, "Dim", new Color(0, 0, 0, 0.6f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            window = Ui.Rect(m, "Window");
            Ui.Place(window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-w / 2, -h / 2), new Vector2(w / 2, h / 2));
            Ui.Panel(window, "Bg", new Color(0.09f, 0.12f, 0.17f, 1f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)window.Find("Bg"));
            return m;
        }

        void ToggleChallengeList(bool show)
        {
            if (_chList == null) return;
            if (show) PopulateChallengeList();
            _chList.gameObject.SetActive(show);
        }

        static string RulesText(ChallengeDefinition d)
        {
            var parts = new List<string> { "finish +" + d.GoalPct.ToString(CultureInfo.InvariantCulture) + "% or better", "stay within a " + d.MaxDrawdownPct.ToString(CultureInfo.InvariantCulture) + "% drawdown" };
            if (d.Rules.MaxTrades.HasValue) parts.Add("at most " + d.Rules.MaxTrades.Value + " trades");
            if (d.Rules.RequireStopLoss) parts.Add("a stop-loss on every trade");
            if (d.Rules.MaxRiskPct.HasValue) parts.Add("risk under " + d.Rules.MaxRiskPct.Value.ToString(CultureInfo.InvariantCulture) + "% per trade");
            return string.Join(",  ", parts);
        }

        void PopulateChallengeList()
        {
            foreach (var g in _chRows) Ui.Kill(g);
            _chRows.Clear();
            var win = _chList.Find("Window");
            var results = Challenges != null ? Challenges.Results : null;
            float y = 84;
            foreach (var d in TradingEmpire.Core.Challenges.All)
            {
                var res = results != null && results.TryGetValue(d.Id, out var r) ? r : null;
                string stars = res != null ? new string('*', res.Stars) + new string('-', 3 - res.Stars) + "   best " + res.BestReturnPct.ToString("+0.00", CultureInfo.InvariantCulture) + "%   won " + res.Wins + " of " + res.Attempts : "not tried yet";
                var name = Ui.Label(win, d.Name + "   <color=#f5bf3d>" + stars + "</color>", 13, Theme.Text, TextAnchor.MiddleLeft, false, true);
                Ui.Place(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -y - 22), new Vector2(-130, -y));
                var info = Ui.Label(win, d.Blurb + "\n<color=#8594ad>" + RulesText(d) + (d.RewardText != null ? "\nFirst win " + d.RewardText + "." : "") + "</color>", 11, Theme.Text, TextAnchor.UpperLeft);
                info.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Place(info.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -y - 84), new Vector2(-130, -y - 22));
                var def = d;
                var start = Ui.Button(win, "Start", () => { ToggleChallengeList(false); Challenges.Start(def); }, new Color(0.1f, 0.5f, 0.4f), 13);
                Ui.Place((RectTransform)start.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-112, -y - 40), new Vector2(-16, -y - 6));
                _chRows.Add(name.gameObject); _chRows.Add(info.gameObject); _chRows.Add(start.gameObject);
                y += 100;
            }
        }

        void RefreshChallenge()
        {
            if (Challenges == null) return;
            var run = Challenges.Run;
            _chBanner.gameObject.SetActive(run != null && !run.Finished);
            if (run != null && !run.Finished)
            {
                var st = run.Status;
                _chBannerText.text = "CHALLENGE  " + run.Def.Name + "   return " + st.ReturnPct.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "% of +" + run.Def.GoalPct.ToString(CultureInfo.InvariantCulture)
                    + "   drawdown " + st.DrawdownPct.ToString("0.0", CultureInfo.InvariantCulture) + "% / " + run.Def.MaxDrawdownPct.ToString(CultureInfo.InvariantCulture) + "%   trades " + st.Trades
                    + "   candles left " + st.CandlesLeft;
            }
            var result = Challenges.Result;
            _chResult.gameObject.SetActive(result != null);
            if (result != null)
            {
                bool won = result.Phase == ChallengePhase.Won;
                _chResultTitle.text = won ? "CHALLENGE WON" : "CHALLENGE LOST";
                _chResultTitle.color = won ? Theme.Bull : Theme.Bear;
                _chResultText.text = (won ? new string('*', result.Stars) + new string('-', 3 - result.Stars) + "\n\n" : "") + (result.Reason ?? "") + "\nReturn " + result.ReturnPct.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "%   drawdown "
                    + result.DrawdownPct.ToString("0.00", CultureInfo.InvariantCulture) + "%   trades " + result.Trades + (Challenges.RewardText != null ? "\n\n" + Challenges.RewardText : "");
            }
        }
    }
}
