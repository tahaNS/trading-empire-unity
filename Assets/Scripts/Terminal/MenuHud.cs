using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The game menu: save / load / export / import / backups, missions, achievements and settings; plus the goal card and the
    /// game-over screen. It reads the world and calls <see cref="SaveManager"/> and the game app; it never changes balances.
    /// </summary>
    public sealed class MenuHud : MonoBehaviour
    {
        GameApp _app;
        GameWorld World { get { return _app.World; } }
        RectTransform _modal, _content, _goal, _over;
        Text _title, _body, _message, _goalTitle, _goalText, _goalProgress, _overText;
        Image _goalFill;
        InputField _path;
        readonly List<Button> _tabs = new List<Button>();
        readonly List<GameObject> _dynamic = new List<GameObject>();
        int _tab;
        bool _dirty = true;

        static readonly string[] TabNames = { "Game", "Missions", "Achievements", "Settings", "Help" };
        bool _photo;
        /// <summary>Photo mode hides the goal card with the rest of the interface.</summary>
        public void SetPhoto(bool on) { _photo = on; _dirty = true; }

        public static MenuHud Build(Transform canvas, GameApp app)
        {
            var root = Ui.Rect(canvas, "MenuHud");
            Ui.Stretch(root);
            var hud = root.gameObject.AddComponent<MenuHud>();
            hud._app = app;
            hud.BuildUi(root);
            app.World.City.Changed += hud.MarkDirty;
            app.World.Progression.Changed += hud.MarkDirty;
            app.World.GameOver.Changed += hud.MarkDirty;
            app.World.Achievements.Changed += hud.MarkDirty;
            return hud;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy()
        {
            if (_app == null || _app.World == null) return;
            _app.World.City.Changed -= MarkDirty; _app.World.Progression.Changed -= MarkDirty; _app.World.GameOver.Changed -= MarkDirty; _app.World.Achievements.Changed -= MarkDirty;
        }

        public bool Open { get { return _modal.gameObject.activeSelf; } }
        public void Show(bool show) { _modal.gameObject.SetActive(show); _dirty = true; }
        public void RefreshNow() { _dirty = false; Refresh(); }
        public void SelectTab(int tab) { _tab = tab; _dirty = true; }

        // ---------------------------------------------------------------- layout

        void BuildUi(RectTransform root)
        {
            // goal card: the next mission, with a progress bar
            _goal = Ui.Card(root, "Goal", 12f).rectTransform;
            Ui.Place(_goal, new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 104), new Vector2(372, 198));
            _goalTitle = Ui.Label(_goal, "", 13, Theme.Primary, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_goalTitle.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(14, -28), new Vector2(-14, -6));
            _goalText = Ui.Label(_goal, "", 11, Theme.Ink, TextAnchor.UpperLeft);
            _goalText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_goalText.rectTransform, Vector2.zero, Vector2.one, new Vector2(14, 28), new Vector2(-14, -30));
            var barBg = Ui.Box(_goal, "BarBg", new Color(0.90f, 0.925f, 0.945f));
            Ui.Rounded(barBg, 4f);
            Ui.Place(barBg.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(14, 12), new Vector2(-96, 22));
            _goalFill = Ui.Box(barBg.transform, "Fill", Theme.Good);
            Ui.Rounded(_goalFill, 4f);
            _goalFill.rectTransform.anchorMin = Vector2.zero; _goalFill.rectTransform.anchorMax = new Vector2(0, 1); _goalFill.rectTransform.offsetMin = Vector2.zero; _goalFill.rectTransform.offsetMax = Vector2.zero;
            _goalProgress = Ui.Label(_goal, "", 11, Theme.Muted, TextAnchor.MiddleRight, true);
            Ui.Place(_goalProgress.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-92, 6), new Vector2(-12, 26));

            // the modal menu
            _modal = Ui.Rect(root, "MenuModal");
            Ui.Stretch(_modal);
            var dim = Ui.Box(_modal, "Dim", new Color(0.05f, 0.1f, 0.15f, 0.45f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var winBg = Ui.Card(_modal, "Window", 14f, Color.white);
            var win = winBg.rectTransform;
            Ui.Place(win, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-340, -300), new Vector2(340, 300));
            _title = Ui.Label(win, "MENU", 15, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -38), new Vector2(-80, -8));
            var close = Ui.Pill(win, "Close", () => Show(false), false, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-84, -36), new Vector2(-14, -10));
            float x = 14;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var b = Ui.Pill(win, TabNames[i], () => { _tab = index; _dirty = true; }, false, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -72), new Vector2(x + 118, -44));
                _tabs.Add(b);
                x += 124;
            }
            _content = Ui.Rect(win, "Content");
            Ui.Place(_content, Vector2.zero, Vector2.one, new Vector2(18, 40), new Vector2(-18, -82));
            _body = Ui.Label(_content, "", 12, Theme.Ink, TextAnchor.UpperLeft, true);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _message = Ui.Label(win, "", 12, Theme.Muted, TextAnchor.MiddleLeft);
            _message.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_message.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(18, 6), new Vector2(-18, 38));
            _modal.gameObject.SetActive(false);

            // game over
            _over = Ui.Rect(root, "GameOver");
            Ui.Stretch(_over);
            var odim = Ui.Box(_over, "Dim", new Color(0.02f, 0.02f, 0.04f, 0.82f));
            Ui.Stretch(odim.rectTransform);
            odim.raycastTarget = true;
            var ow = Ui.Rect(_over, "Window");
            Ui.Place(ow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, -170), new Vector2(260, 170));
            Ui.Panel(ow, "Bg", new Color(0.12f, 0.09f, 0.11f, 1f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)ow.Find("Bg"));
            var oh = Ui.Label(ow, "GAME OVER", 24, Theme.Bear, TextAnchor.MiddleCenter, false, true);
            Ui.Place(oh.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, -12));
            _overText = Ui.Label(ow, "", 14, Theme.Text, TextAnchor.UpperCenter);
            _overText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_overText.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 80), new Vector2(-24, -70));
            var again = Ui.Button(ow, "Start a new game", () => _app.StartOver(), Theme.Good, 15);
            Ui.Place((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-120, 24), new Vector2(120, 64));
            _over.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- refresh

        void Update() { if (_dirty) { _dirty = false; Refresh(); } }

        void Clear() { foreach (var g in _dynamic) Ui.Kill(g); _dynamic.Clear(); }

        Button AddButton(string label, Action act, float x, float y, float w, float h, Color? color = null)
        {
            var b = Ui.Pill(_content, label, act, false, 12);
            var fill = color ?? Color.white;
            bool tinted = color.HasValue;
            Ui.SetButtonColor(b, fill);
            b.transform.Find("Label").GetComponent<Text>().color = tinted ? Color.white : Theme.Ink;
            Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - h), new Vector2(x + w, -y));
            _dynamic.Add(b.gameObject);
            return b;
        }

        void Refresh()
        {
            RefreshGoal();
            var over = World.GameOver.State;
            _over.gameObject.SetActive(over != null);
            if (over != null)
                _overText.text = "You can no longer trade, and the city can't save you.\n\n" + over.Days + " days   " + over.Buildings + " buildings   " + over.TradesClosed + " trades\nnet trading " + over.NetTradingPnl.ToSignedString()
                    + "   " + over.MissionsDone + " missions\n\nYour saved game was not touched: you can also load it from the menu.";

            if (!_modal.gameObject.activeSelf) return;
            for (int i = 0; i < _tabs.Count; i++) { Ui.SetButtonColor(_tabs[i], i == _tab ? Theme.Primary : Color.white); _tabs[i].transform.Find("Label").GetComponent<Text>().color = i == _tab ? Color.white : Theme.Ink; }
            Clear();
            _title.text = "MENU  —  " + TabNames[_tab].ToUpperInvariant();
            _body.text = "";
            if (_tab == 0) GameTab();
            else if (_tab == 1) MissionsTab();
            else if (_tab == 2) AchievementsTab();
            else if (_tab == 3) SettingsTab();
            else HelpTab();
        }

        void RefreshGoal()
        {
            var goal = Progress.NextGoalOf(World.Progression.Completed, World.Progression.Stats);
            _goal.gameObject.SetActive(goal != null && !_app.TerminalOpen && !_photo);
            if (goal == null) return;
            var m = goal.Mission;
            _goalTitle.text = "GOAL  —  " + m.Title;
            _goalText.text = m.Description + "\n<color=#5c6b75>" + m.Hint + "</color>";
            float frac = goal.Target > 0 ? Mathf.Clamp01((float)goal.Value / goal.Target) : 0;
            _goalFill.rectTransform.anchorMax = new Vector2(frac, 1);
            _goalProgress.text = Missions.IsMoney(m.Metric) ? Money.FromCents(goal.Value) + " / " + Money.FromCents(goal.Target) : goal.Value + " / " + goal.Target;
            if (Missions.IsMoney(m.Metric)) _goalProgress.text = Money.FromCents(goal.Value).ToString().Replace(".00", "") + "/" + Money.FromCents(goal.Target).ToString().Replace(".00", "");
        }

        // ---------------------------------------------------------------- tabs

        void GameTab()
        {
            float y = 0;
            AddButton("Save game", () => Report(_app.Save() ? "Game saved." : "Could not save: " + _app.LastError), 0, y, 200, 34, Theme.Good);
            AddButton("Load last save", () => Report(_app.LoadSaved()), 210, y, 200, 34);
            y += 44;
            AddButton("Export to a file", () => Report(_app.Export()), 0, y, 200, 34);
            AddButton("Import from path", () => Report(_app.Import(_path.text)), 210, y, 200, 34);
            y += 44;
            _path = Ui.Input(_content, _path != null ? _path.text : "", 12);
            ((Image)_path.targetGraphic).color = new Color(0.94f, 0.955f, 0.965f);
            _path.textComponent.color = Theme.Ink;
            Ui.Place((RectTransform)_path.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 30), new Vector2(0, -y));
            var ph = Ui.Label(_path.transform, "Full path to an exported save (from this game or the web version)", 11, Theme.Muted, TextAnchor.MiddleLeft, true);
            Ui.Stretch(ph.rectTransform, 8, 2, 8, 2);
            _path.placeholder = ph;
            _dynamic.Add(_path.gameObject);
            y += 44;
            var backups = _app.Saves.ListBackups();
            var label = Ui.Label(_content, "DATED BACKUPS (one per day, the newest " + SaveManager.DailyBackups + ")", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y));
            _dynamic.Add(label.gameObject);
            y += 28;
            if (backups.Count == 0) { var none = Ui.Label(_content, "None yet: one is made the first time you save each day.", 12, Theme.Muted, TextAnchor.MiddleLeft); Ui.Place(none.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 24), new Vector2(0, -y)); _dynamic.Add(none.gameObject); }
            foreach (var day in backups)
            {
                string d = day;
                AddButton("Restore " + d, () => Report(_app.RestoreBackup(d)), 0, y, 200, 30);
                y += 34;
            }
            y += 14;
            AddButton("Start a new game", () => _app.StartOver(), 0, y, 200, 34, Theme.Bad);
            var note = Ui.Label(_content, "A new game replaces the current one at the next save; the previous save is kept as a backup.", 11, Theme.Muted, TextAnchor.MiddleLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(note.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(210, -y - 34), new Vector2(0, -y));
            _dynamic.Add(note.gameObject);
        }

        void MissionsTab()
        {
            var sb = new System.Text.StringBuilder();
            var stats = World.Progression.Stats;
            foreach (var m in Missions.All)
            {
                bool done = World.Progression.Completed.ContainsKey(m.Id);
                long value = Math.Min(stats.Get(m.Metric), m.Target);
                string progress = done ? "" : "   <color=#5c6b75>" + (Missions.IsMoney(m.Metric) ? Money.FromCents(value) + " / " + Money.FromCents(m.Target) : value + " / " + m.Target) + "</color>";
                sb.Append(done ? "<color=#167a43>[x]</color> " : "[ ] ").Append(m.Title).Append(progress).Append('\n');
                sb.Append("      <color=#5c6b75>").Append(m.Description);
                if (m.Unlocks.Length > 0) sb.Append("  Unlocks: ").Append(string.Join(", ", m.Unlocks.Select(UnlockName)));
                sb.Append("</color>\n");
            }
            _body.text = sb.ToString();
        }

        static string UnlockName(string id)
        {
            var t = Land.Tiers.FirstOrDefault(x => x.UnlockId == id);
            if (t != null) return t.Name;
            var b = Buildings.All.FirstOrDefault(x => x.Id == id);
            return b != null ? b.Name : id;
        }

        void AchievementsTab()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(World.Achievements.Earned.Count).Append(" of ").Append(Achievements.All.Count).Append(" earned\n\n");
            foreach (var a in Achievements.All)
            {
                bool got = World.Achievements.Earned.Contains(a.Id);
                sb.Append(got ? "<color=#9e6b05>*</color> " : "<color=#9aa5ae>-</color> ").Append(got ? a.Name : "<color=#5c6b75>" + a.Name + "</color>").Append("   <color=#5c6b75>").Append(a.Text).Append("</color>\n");
            }
            _body.text = sb.ToString();
        }

        void SettingsTab()
        {
            float y = 0;
            var l1 = Ui.Label(_content, "INTERFACE SIZE", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(l1.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y)); _dynamic.Add(l1.gameObject);
            y += 28;
            for (int i = 0; i < GameSettings.Scales.Length; i++)
            {
                int index = i;
                var b = AddButton(GameSettings.ScaleNames[i], () => { GameSettings.SetScale(index); _app.ApplySettings(); _dirty = true; }, i * 140, y, 130, 32, i == GameSettings.ScaleIndex ? Theme.Primary : (Color?)null);
            }
            y += 56;
            var l2 = Ui.Label(_content, "CANDLE COLOURS", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(l2.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y)); _dynamic.Add(l2.gameObject);
            y += 28;
            AddButton("Green and red", () => { GameSettings.SetColorBlind(false); _app.ApplySettings(); _dirty = true; }, 0, y, 200, 32, !Theme.ColorBlind ? Theme.Primary : (Color?)null);
            AddButton("Colour-blind (blue and orange)", () => { GameSettings.SetColorBlind(true); _app.ApplySettings(); _dirty = true; }, 210, y, 240, 32, Theme.ColorBlind ? Theme.Primary : (Color?)null);
            y += 56;
            var l3 = Ui.Label(_content, "SOUND", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(l3.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y)); _dynamic.Add(l3.gameObject);
            y += 28;
            bool muted = _app.Audio != null && _app.Audio.Muted;
            AddButton("Sound on", () => { if (_app.Audio != null) _app.Audio.SetMuted(false); _dirty = true; }, 0, y, 200, 32, !muted ? Theme.Primary : (Color?)null);
            AddButton("Sound off", () => { if (_app.Audio != null) _app.Audio.SetMuted(true); _dirty = true; }, 210, y, 200, 32, muted ? Theme.Primary : (Color?)null);
            y += 56;
            var tip = Ui.Label(_content, "Keys:  Space play or pause the market,  Right arrow next candle,  Shift+Right next bar,  L / S long or short,  mouse wheel zoom,  R rotate a building,  Esc cancel or back.", 12, Theme.Muted, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(tip.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 80), new Vector2(0, -y)); _dynamic.Add(tip.gameObject);
        }

        void HelpTab()
        {
            _body.text =
                "<b>The idea</b>\nTrade real historical forex in the terminal. Profit moves into the city treasury (Finances > To city), where it builds and upgrades buildings that earn rent. Your trading record changes the city too: a winning streak starts a boom, a losing one a panic.\n\n"
                + "<b>The city</b>\nPick a building in the build bar, click the map to place it (R rotates, Esc cancels). Click a building to upgrade or demolish it. Buy land when you run out of room. Policies trade income against upkeep; some unlock by winning challenges.\n\n"
                + "<b>The terminal</b>\nPlay or step the market, then buy or sell with a stop-loss and take-profit. Only candles already revealed are shown: no peeking ahead. When a stop and a target are both hit in one candle, the stop-loss counts first. Use the tools on the left to draw, and Indicators (top right of the chart) for moving averages, RSI, MACD and more.\n\n"
                + "<b>Keys</b>\nSpace play or pause the market,  Right arrow next candle,  Shift+Right next bar,  L / S long or short,  mouse wheel zoom,  W A S D or arrows and right-drag move the city camera,  R rotate a building,  Esc cancel or back.\n\n"
                + "<b>Photo mode</b>\nThe Photo button hides the interface: save a screenshot (to Pictures/Trading Empire) or start a slow fly-over of the city. Esc leaves.";
            AddButton("Guide me through a first trade", () => { Show(false); _app.Tutorial.StartTutorial(); }, 0, 430, 260, 34, Theme.Primary);
            AddButton("Show the welcome", () => { Show(false); _app.Tutorial.ShowWelcome(); }, 270, 430, 200, 34);
        }

        void Report(string text) { _message.text = text ?? ""; _dirty = true; }
    }
}
