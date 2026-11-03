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

        static readonly string[] TabNames = { "Game", "Missions", "Achievements", "Settings" };

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
            _goal = Ui.Rect(root, "Goal");
            Ui.Place(_goal, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 96), new Vector2(358, 190));
            Ui.Panel(_goal, "Bg", new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.96f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_goal.Find("Bg"));
            _goalTitle = Ui.Label(_goal, "", 13, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_goalTitle.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -26), new Vector2(-12, -4));
            _goalText = Ui.Label(_goal, "", 11, Theme.Text, TextAnchor.UpperLeft);
            _goalText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_goalText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 24), new Vector2(-12, -28));
            var barBg = Ui.Box(_goal, "BarBg", new Color(0.03f, 0.04f, 0.07f));
            Ui.Place(barBg.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 8), new Vector2(-92, 20));
            _goalFill = Ui.Box(barBg.transform, "Fill", Theme.Bull);
            _goalFill.rectTransform.anchorMin = Vector2.zero; _goalFill.rectTransform.anchorMax = new Vector2(0, 1); _goalFill.rectTransform.offsetMin = Vector2.zero; _goalFill.rectTransform.offsetMax = Vector2.zero;
            _goalProgress = Ui.Label(_goal, "", 11, Theme.Dim, TextAnchor.MiddleRight, true);
            Ui.Place(_goalProgress.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-88, 4), new Vector2(-10, 24));

            // the modal menu
            _modal = Ui.Rect(root, "MenuModal");
            Ui.Stretch(_modal);
            var dim = Ui.Box(_modal, "Dim", new Color(0, 0, 0, 0.55f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var win = Ui.Rect(_modal, "Window");
            Ui.Place(win, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-340, -300), new Vector2(340, 300));
            Ui.Panel(win, "Bg", new Color(0.09f, 0.12f, 0.17f, 1f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)win.Find("Bg"));
            _title = Ui.Label(win, "MENU", 14, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -34), new Vector2(-60, -6));
            var close = Ui.Button(win, "Close", () => Show(false), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-74, -32), new Vector2(-12, -8));
            float x = 12;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var b = Ui.Button(win, TabNames[i], () => { _tab = index; _dirty = true; }, Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -68), new Vector2(x + 130, -40));
                _tabs.Add(b);
                x += 134;
            }
            _content = Ui.Rect(win, "Content");
            Ui.Place(_content, Vector2.zero, Vector2.one, new Vector2(16, 40), new Vector2(-16, -78));
            _body = Ui.Label(_content, "", 12, Theme.Text, TextAnchor.UpperLeft, true);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _message = Ui.Label(win, "", 12, Theme.Dim, TextAnchor.MiddleLeft);
            _message.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_message.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(16, 6), new Vector2(-16, 38));
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
            var again = Ui.Button(ow, "Start a new game", () => _app.StartOver(), new Color(0.1f, 0.5f, 0.4f), 15);
            Ui.Place((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-120, 24), new Vector2(120, 64));
            _over.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- refresh

        void Update() { if (_dirty) { _dirty = false; Refresh(); } }

        void Clear() { foreach (var g in _dynamic) Ui.Kill(g); _dynamic.Clear(); }

        Button AddButton(string label, Action act, float x, float y, float w, float h, Color? color = null)
        {
            var b = Ui.Button(_content, label, act, color ?? Theme.PanelAlt, 12);
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
            for (int i = 0; i < _tabs.Count; i++) Ui.SetButtonColor(_tabs[i], i == _tab ? Theme.Blue : Theme.PanelAlt);
            Clear();
            _title.text = "MENU  —  " + TabNames[_tab].ToUpperInvariant();
            _body.text = "";
            if (_tab == 0) GameTab();
            else if (_tab == 1) MissionsTab();
            else if (_tab == 2) AchievementsTab();
            else SettingsTab();
        }

        void RefreshGoal()
        {
            var goal = Progress.NextGoalOf(World.Progression.Completed, World.Progression.Stats);
            _goal.gameObject.SetActive(goal != null && !_app.TerminalOpen);
            if (goal == null) return;
            var m = goal.Mission;
            _goalTitle.text = "GOAL  —  " + m.Title;
            _goalText.text = m.Description + "\n<color=#8594ad>" + m.Hint + "</color>";
            float frac = goal.Target > 0 ? Mathf.Clamp01((float)goal.Value / goal.Target) : 0;
            _goalFill.rectTransform.anchorMax = new Vector2(frac, 1);
            _goalProgress.text = Missions.IsMoney(m.Metric) ? Money.FromCents(goal.Value) + " / " + Money.FromCents(goal.Target) : goal.Value + " / " + goal.Target;
            if (Missions.IsMoney(m.Metric)) _goalProgress.text = Money.FromCents(goal.Value).ToString().Replace(".00", "") + "/" + Money.FromCents(goal.Target).ToString().Replace(".00", "");
        }

        // ---------------------------------------------------------------- tabs

        void GameTab()
        {
            float y = 0;
            AddButton("Save game", () => Report(_app.Save() ? "Game saved." : "Could not save: " + _app.LastError), 0, y, 200, 34, new Color(0.1f, 0.5f, 0.4f));
            AddButton("Load last save", () => Report(_app.LoadSaved()), 210, y, 200, 34);
            y += 44;
            AddButton("Export to a file", () => Report(_app.Export()), 0, y, 200, 34);
            AddButton("Import from path", () => Report(_app.Import(_path.text)), 210, y, 200, 34);
            y += 44;
            _path = Ui.Input(_content, _path != null ? _path.text : "", 12);
            Ui.Place((RectTransform)_path.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 30), new Vector2(0, -y));
            var ph = Ui.Label(_path.transform, "Full path to an exported save (from this game or the web version)", 11, Theme.Dim, TextAnchor.MiddleLeft, true);
            Ui.Stretch(ph.rectTransform, 8, 2, 8, 2);
            _path.placeholder = ph;
            _dynamic.Add(_path.gameObject);
            y += 44;
            var backups = _app.Saves.ListBackups();
            var label = Ui.Label(_content, "DATED BACKUPS (one per day, the newest " + SaveManager.DailyBackups + ")", 11, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y));
            _dynamic.Add(label.gameObject);
            y += 28;
            if (backups.Count == 0) { var none = Ui.Label(_content, "None yet: one is made the first time you save each day.", 12, Theme.Dim, TextAnchor.MiddleLeft); Ui.Place(none.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 24), new Vector2(0, -y)); _dynamic.Add(none.gameObject); }
            foreach (var day in backups)
            {
                string d = day;
                AddButton("Restore " + d, () => Report(_app.RestoreBackup(d)), 0, y, 200, 30);
                y += 34;
            }
            y += 14;
            AddButton("Start a new game", () => _app.StartOver(), 0, y, 200, 34, new Color(0.55f, 0.2f, 0.22f));
            var note = Ui.Label(_content, "A new game replaces the current one at the next save; the previous save is kept as a backup.", 11, Theme.Dim, TextAnchor.MiddleLeft);
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
                string progress = done ? "" : "   <color=#8594ad>" + (Missions.IsMoney(m.Metric) ? Money.FromCents(value) + " / " + Money.FromCents(m.Target) : value + " / " + m.Target) + "</color>";
                sb.Append(done ? "<color=#26c295>[x]</color> " : "[ ] ").Append(m.Title).Append(progress).Append('\n');
                sb.Append("      <color=#8594ad>").Append(m.Description);
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
                sb.Append(got ? "<color=#f5bf3d>*</color> " : "<color=#4a5568>-</color> ").Append(got ? a.Name : "<color=#8594ad>" + a.Name + "</color>").Append("   <color=#8594ad>").Append(a.Text).Append("</color>\n");
            }
            _body.text = sb.ToString();
        }

        void SettingsTab()
        {
            float y = 0;
            var l1 = Ui.Label(_content, "INTERFACE SIZE", 11, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(l1.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y)); _dynamic.Add(l1.gameObject);
            y += 28;
            for (int i = 0; i < GameSettings.Scales.Length; i++)
            {
                int index = i;
                var b = AddButton(GameSettings.ScaleNames[i], () => { GameSettings.SetScale(index); _app.ApplySettings(); _dirty = true; }, i * 140, y, 130, 32, i == GameSettings.ScaleIndex ? Theme.Blue : (Color?)null);
            }
            y += 56;
            var l2 = Ui.Label(_content, "CANDLE COLOURS", 11, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(l2.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 22), new Vector2(0, -y)); _dynamic.Add(l2.gameObject);
            y += 28;
            AddButton("Green and red", () => { GameSettings.SetColorBlind(false); _app.ApplySettings(); _dirty = true; }, 0, y, 200, 32, !Theme.ColorBlind ? Theme.Blue : (Color?)null);
            AddButton("Colour-blind (blue and orange)", () => { GameSettings.SetColorBlind(true); _app.ApplySettings(); _dirty = true; }, 210, y, 240, 32, Theme.ColorBlind ? Theme.Blue : (Color?)null);
            y += 56;
            var tip = Ui.Label(_content, "Keys:  Space play or pause the market,  Right arrow next candle,  Shift+Right next bar,  L / S long or short,  mouse wheel zoom,  R rotate a building,  Esc cancel or back.", 12, Theme.Dim, TextAnchor.UpperLeft);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(tip.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -y - 80), new Vector2(0, -y)); _dynamic.Add(tip.gameObject);
        }

        void Report(string text) { _message.text = text ?? ""; _dirty = true; }
    }
}
