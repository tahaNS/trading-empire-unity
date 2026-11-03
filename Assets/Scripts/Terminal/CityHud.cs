using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The city overlay: clock and speed, treasury, mood and events, the build bar, the building inspector, capital
    /// transfers and policies. It reads <see cref="CityGame"/> and only calls its methods and the transfer service.
    /// </summary>
    public sealed class CityHud : MonoBehaviour
    {
        GameWorld _world;
        CityGame City { get { return _world.City; } }
        Action _openTerminal, _openMenu;

        Text _date, _treasury, _net, _chips, _toast;
        readonly List<Button> _speedButtons = new List<Button>();
        readonly Dictionary<string, Button> _buildButtons = new Dictionary<string, Button>();
        Button _landButton;
        RectTransform _inspector, _transferPanel, _policyPanel;
        Text _inspectorTitle, _inspectorBody;
        Button _upgrade, _demolish;
        Text _transferInfo, _transferMessage;
        InputField _amount;
        readonly Dictionary<string, Button> _policyButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, Text> _policyTexts = new Dictionary<string, Text>();
        bool _dirty = true;
        float _toastUntil;
        int _lastToast;
        string _inspectedId;
        /// <summary>The 3D city, for placing floating income numbers over buildings.</summary>
        public CityView View;
        RectTransform _floaterLayer;
        readonly List<(Text text, float t0, string id, Color color)> _floaters = new List<(Text, float, string, Color)>();
        int _lastPayoutSeq;
        const float FloatSeconds = 1.8f;

        public static CityHud Build(Transform canvas, GameWorld world, Action openTerminal, Action openMenu = null)
        {
            var root = Ui.Rect(canvas, "CityHud");
            Ui.Stretch(root);
            var hud = root.gameObject.AddComponent<CityHud>();
            hud._world = world;
            hud._openTerminal = openTerminal;
            hud._openMenu = openMenu;
            hud.BuildUi(root);
            world.City.Changed += hud.MarkDirty;
            world.Clock.ViewChanged += hud.MarkDirty;
            world.Trading.Changed += hud.MarkDirty;
            return hud;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_world != null) { _world.City.Changed -= MarkDirty; _world.Clock.ViewChanged -= MarkDirty; _world.Trading.Changed -= MarkDirty; } }

        public void RefreshNow() { _dirty = false; Refresh(); }

        // ---------------------------------------------------------------- layout

        void BuildUi(RectTransform root)
        {
            // top bar
            var bar = Ui.Rect(root, "TopBar");
            Ui.Place(bar, new Vector2(0, 1), Vector2.one, new Vector2(8, -64), new Vector2(-8, -8));
            Ui.Panel(bar, "Bg", new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.96f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)bar.Find("Bg"));

            _date = Ui.Label(bar, "", 14, Theme.Text, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_date.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(14, 0), new Vector2(190, 0));
            float x = 196;
            int[] speeds = GameClock.Speeds;
            string[] names = { "II", "1x", "2x", "5x", "10x" };
            for (int i = 0; i < speeds.Length; i++)
            {
                int sp = speeds[i];
                var b = Ui.Button(bar, names[i], () => _world.Clock.SetSpeed(sp), Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, Vector2.zero, new Vector2(0, 1), new Vector2(x, 10), new Vector2(x + 42, -10));
                _speedButtons.Add(b);
                x += 46;
            }

            _treasury = Ui.Label(bar, "", 22, Theme.Text, TextAnchor.MiddleLeft, true, true);
            Ui.Place(_treasury.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(x + 24, 6), new Vector2(x + 250, -4));
            _net = Ui.Label(bar, "", 12, Theme.Dim, TextAnchor.MiddleLeft, true);
            Ui.Place(_net.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(x + 250, 0), new Vector2(x + 520, 0));
            _chips = Ui.Label(bar, "", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_chips.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(x + 520, 0), new Vector2(x + 760, 0));

            float rx = -10;
            if (_openMenu != null) AddRight(bar, "Menu", ref rx, 70, () => _openMenu());
            AddRight(bar, "Terminal", ref rx, 96, () => _openTerminal());
            AddRight(bar, "Transfer", ref rx, 90, () => Toggle(_transferPanel, _policyPanel));
            AddRight(bar, "Policies", ref rx, 86, () => Toggle(_policyPanel, _transferPanel));
            _landButton = AddRight(bar, "Buy land", ref rx, 190, () => City.ExpandLand());

            BuildBuildBar(root);
            BuildInspector(root);
            BuildTransferPanel(root);
            BuildPolicyPanel(root);

            _floaterLayer = Ui.Rect(root, "Floaters");
            Ui.Stretch(_floaterLayer);
            var toastBg = Ui.Box(root, "Toast", new Color(0.1f, 0.14f, 0.2f, 0.96f));
            Ui.Place(toastBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-330, 100), new Vector2(330, 140));
            _toast = Ui.Label(toastBg.transform, "", 14, Theme.Text, TextAnchor.MiddleCenter);
            toastBg.gameObject.SetActive(false);
        }

        Button AddRight(RectTransform bar, string label, ref float rx, float w, Action act)
        {
            var b = Ui.Button(bar, label, act, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)b.transform, new Vector2(1, 0), Vector2.one, new Vector2(rx - w, 10), new Vector2(rx, -10));
            rx -= w + 6;
            return b;
        }

        void Toggle(RectTransform show, RectTransform other)
        {
            other.gameObject.SetActive(false);
            show.gameObject.SetActive(!show.gameObject.activeSelf);
            _dirty = true;
        }

        void BuildBuildBar(RectTransform root)
        {
            var bar = Ui.Rect(root, "BuildBar");
            Ui.Place(bar, Vector2.zero, new Vector2(1, 0), new Vector2(8, 8), new Vector2(-8, 88));
            Ui.Panel(bar, "Bg", new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.96f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)bar.Find("Bg"));
            var hint = Ui.Label(bar, "BUILD   (click a building, then the map. R rotates, Esc cancels)", 11, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(hint.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(10, -20), new Vector2(-10, -2));
            int n = Buildings.All.Count;
            for (int i = 0; i < n; i++)
            {
                var def = Buildings.All[i];
                string id = def.Id;
                var b = Ui.Button(bar, def.Name + "\n" + def.Cost, () => City.StartPlacement(id), Theme.PanelAlt, 11);
                float w = 1f / n;
                Ui.Place((RectTransform)b.transform, new Vector2(w * i, 0), new Vector2(w * (i + 1), 1), new Vector2(6, 6), new Vector2(-2, -22));
                _buildButtons[id] = b;
            }
        }

        void BuildInspector(RectTransform root)
        {
            _inspector = Ui.Rect(root, "Inspector");
            Ui.Place(_inspector, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-330, 100), new Vector2(-8, -72));
            Ui.Panel(_inspector, "Bg", new Color(Theme.Panel.r, Theme.Panel.g, Theme.Panel.b, 0.97f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_inspector.Find("Bg"));
            _inspectorTitle = Ui.Label(_inspector, "", 16, Theme.Text, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_inspectorTitle.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -36), new Vector2(-12, -6));
            _inspectorBody = Ui.Label(_inspector, "", 12, Theme.Text, TextAnchor.UpperLeft, true);
            _inspectorBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_inspectorBody.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 90), new Vector2(-12, -42));
            _upgrade = Ui.Button(_inspector, "Upgrade", () => { if (_inspectedId != null) City.Upgrade(_inspectedId); }, new Color(0.1f, 0.5f, 0.4f), 13);
            Ui.Place((RectTransform)_upgrade.transform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 48), new Vector2(-12, 82));
            _demolish = Ui.Button(_inspector, "Demolish", () => { if (_inspectedId != null) City.Demolish(_inspectedId); }, new Color(0.55f, 0.2f, 0.22f), 13);
            Ui.Place((RectTransform)_demolish.transform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 10), new Vector2(-12, 44));
            _inspector.gameObject.SetActive(false);
        }

        void BuildTransferPanel(RectTransform root)
        {
            _transferPanel = Ui.Rect(root, "TransferPanel");
            Ui.Place(_transferPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -330), new Vector2(470, -72));
            Ui.Panel(_transferPanel, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.99f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_transferPanel.Find("Bg"));
            var h = Ui.Label(_transferPanel, "CAPITAL TRANSFER", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -26), new Vector2(-12, -4));
            _transferInfo = Ui.Label(_transferPanel, "", 12, Theme.Text, TextAnchor.UpperLeft, true);
            Ui.Place(_transferInfo.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -112), new Vector2(-12, -30));
            _amount = Ui.Input(_transferPanel, "1000", 14, InputField.ContentType.DecimalNumber);
            Ui.Place((RectTransform)_amount.transform, new Vector2(0, 1), Vector2.one, new Vector2(12, -150), new Vector2(-12, -118));
            var toCity = Ui.Button(_transferPanel, "To city  >", () => DoTransfer(true), new Color(0.1f, 0.5f, 0.4f), 13);
            Ui.Place((RectTransform)toCity.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, -190), new Vector2(-4, -158));
            var toTrading = Ui.Button(_transferPanel, "<  To trading", () => DoTransfer(false), Theme.Blue, 13);
            Ui.Place((RectTransform)toTrading.transform, new Vector2(0.5f, 1), Vector2.one, new Vector2(4, -190), new Vector2(-12, -158));
            _transferMessage = Ui.Label(_transferPanel, "", 11, Theme.Dim, TextAnchor.UpperLeft);
            _transferMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_transferMessage.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 6), new Vector2(-12, -196));
            _transferPanel.gameObject.SetActive(false);
        }

        void DoTransfer(bool toCity)
        {
            Money amount;
            try { amount = Money.ParseDollars((_amount.text ?? "").Replace(",", "")); }
            catch (FormatException) { _transferMessage.text = "Enter an amount like 1500 or 1500.50."; return; }
            string request = Guid.NewGuid().ToString();
            var r = toCity ? _world.Transfers.Transfer(request, amount) : _world.Transfers.TransferToTrading(request, amount);
            _transferMessage.text = r.Ok ? "Moved " + amount + (toCity ? " to the city treasury." : " to the trading account.") : r.Message;
            _transferMessage.color = r.Ok ? new Color(0.55f, 1f, 0.8f) : new Color(1f, 0.6f, 0.6f);
            _dirty = true;
        }

        void BuildPolicyPanel(RectTransform root)
        {
            _policyPanel = Ui.Rect(root, "PolicyPanel");
            float h = 44 + Modifiers.Policies.Count * 84;
            Ui.Place(_policyPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -72 - h), new Vector2(470, -72));
            Ui.Panel(_policyPanel, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.99f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_policyPanel.Find("Bg"));
            var head = Ui.Label(_policyPanel, "POLICIES", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(head.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -26), new Vector2(-12, -4));
            for (int i = 0; i < Modifiers.Policies.Count; i++)
            {
                var p = Modifiers.Policies[i];
                float top = -34 - i * 84;
                var title = Ui.Label(_policyPanel, p.Name, 13, Theme.Text, TextAnchor.MiddleLeft, false, true);
                Ui.Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, top - 22), new Vector2(-100, top));
                var desc = Ui.Label(_policyPanel, p.Description, 11, Theme.Dim, TextAnchor.UpperLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Place(desc.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, top - 76), new Vector2(-100, top - 24));
                string id = p.Id;
                var b = Ui.Button(_policyPanel, "OFF", () => City.SetPolicy(id, !City.State.Policies.Contains(id)), Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-88, top - 30), new Vector2(-12, top - 2));
                _policyButtons[id] = b;
                _policyTexts[id] = desc;
            }
            _policyPanel.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- refresh

        void SpawnFloaters(PayoutBatch batch)
        {
            if (View == null || batch == null) return;
            int n = 0;
            foreach (var item in batch.Items)
            {
                if (n++ >= 10 || item.Amount.Cents == 0) continue;
                var t = Ui.Label(_floaterLayer, item.Amount.ToSignedString().Replace(".00", ""), 17, item.Amount.Cents > 0 ? new Color(0.45f, 1f, 0.7f) : new Color(1f, 0.5f, 0.5f), TextAnchor.MiddleCenter, true, true);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(140, 26);
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(1.5f, -1.5f);
                _floaters.Add((t, Time.unscaledTime, item.Id, t.color));
            }
        }

        void AnimateFloaters()
        {
            if (View == null || View.Camera == null) return;
            var canvas = GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var (text, t0, id, color) = _floaters[i];
                float k = (Time.unscaledTime - t0) / FloatSeconds;
                if (k >= 1f || text == null) { if (text != null) Ui.Kill(text.gameObject); _floaters.RemoveAt(i); continue; }
                var top = View.TopOf(id);
                var screen = (Vector2)View.Camera.WorldToScreenPoint(top);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_floaterLayer, screen, cam, out var local);
                text.rectTransform.anchoredPosition = local + new Vector2(0, k * 60f);
                text.color = new Color(color.r, color.g, color.b, 1f - k * k);
            }
        }

        void Update()
        {
            AnimateFloaters();
            if (_dirty) { _dirty = false; Refresh(); }
            if (_toast != null && _toast.transform.parent.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
        }

        static string Signed(Money m) { return m.ToSignedString(); }

        void Refresh()
        {
            var s = City.State;
            _date.text = DateTimeOffset.FromUnixTimeMilliseconds(_world.Clock.Time).UtcDateTime.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            for (int i = 0; i < _speedButtons.Count; i++) Ui.SetButtonColor(_speedButtons[i], GameClock.Speeds[i] == _world.Clock.Speed ? Theme.Blue : Theme.PanelAlt);

            _treasury.text = s.Balance.ToString();
            _treasury.color = s.Balance.Cents < 0 ? Theme.Bear : Theme.Text;
            var proj = CityEconomy.ProjectedNetPer30Days(s.Buildings, false, City.Context());
            _net.text = "net " + Signed(proj) + " / 30 days";
            _net.color = proj.Cents >= 0 ? Theme.Bull : Theme.Bear;

            var chips = new List<string>();
            if (s.Mood == Mood.Booming) chips.Add("<color=#26c295>BOOM</color>");
            if (s.Mood == Mood.Panic) chips.Add("<color=#ed5259>PANIC</color>");
            foreach (var e in s.Events)
            {
                int days = (int)Math.Ceiling((e.EndsAt - _world.Clock.Time) / (double)GameClock.DayMs);
                var def = EventConfig.Get(e.Kind);
                chips.Add((e.Kind == EventKind.Fire ? "<color=#ed5259>" : e.Kind == EventKind.Storm ? "<color=#f5bf3d>" : "<color=#26c295>") + def.Name.ToUpperInvariant() + " " + Math.Max(days, 0) + "d</color>");
            }
            _chips.text = string.Join("   ", chips);

            var tier = City.NextLandTier();
            _landButton.gameObject.SetActive(tier != null);
            if (tier != null) Ui.SetButtonLabel(_landButton, "Buy " + tier.Name.Replace(" District", "") + "  " + tier.Cost);

            foreach (var def in Buildings.All)
            {
                var b = _buildButtons[def.Id];
                bool locked = !City.IsUnlocked(def.UnlockId);
                bool afford = s.Balance >= def.Cost;
                bool active = s.Placement != null && s.Placement.DefId == def.Id;
                Ui.SetButtonLabel(b, (locked ? "[locked] " : "") + def.Name + "\n" + def.Cost);
                Ui.SetButtonColor(b, active ? Theme.Blue : locked ? new Color(0.12f, 0.14f, 0.18f) : afford ? Theme.PanelAlt : new Color(0.2f, 0.12f, 0.14f));
            }

            if (s.Payouts != null && s.Payouts.Seq != _lastPayoutSeq) { _lastPayoutSeq = s.Payouts.Seq; SpawnFloaters(s.Payouts); }
            RefreshInspector(s);
            RefreshTransfer();
            RefreshPolicies(s);

            if (s.Notice != null && s.Notice.Seq != _lastToast)
            {
                _lastToast = s.Notice.Seq;
                _toast.text = s.Notice.Text;
                _toast.color = s.Notice.Kind == NoticeKind.Error ? new Color(1f, 0.6f, 0.6f) : s.Notice.Kind == NoticeKind.Success ? new Color(0.55f, 1f, 0.8f) : Theme.Text;
                _toast.transform.parent.gameObject.SetActive(true);
                _toastUntil = Time.unscaledTime + 4.5f;
            }
        }

        void RefreshInspector(CityState s)
        {
            var b = s.SelectedId != null ? s.Buildings.FirstOrDefault(x => x.Id == s.SelectedId) : null;
            _inspectedId = b != null ? b.Id : null;
            _inspector.gameObject.SetActive(b != null);
            if (b == null) return;
            var def = Buildings.Get(b.DefId);
            int level = Landmarks.EffectiveLevel(b.Level, def, s.Record);
            _inspectorTitle.text = def.Levels[level - 1].Name;
            var lines = new List<string>();
            lines.Add("<color=#8594ad>" + def.Description + "</color>");
            lines.Add("");
            if (b.Status == BuildStatus.Building)
            {
                double days = Math.Max(0, (b.CompletesAt - _world.Clock.Time) / (double)GameClock.DayMs);
                lines.Add("Under construction: ready in " + Math.Ceiling(days) + " days");
            }
            else
            {
                var mods = CityEconomy.ModsFor(b, s.Buildings, City.Context());
                var lvl = def.Levels[b.Level - 1];
                lines.Add("Level " + level + " of " + def.Levels.Length);
                lines.Add("Income   " + lvl.Income + "  per " + def.IncomeIntervalDays + " days");
                lines.Add("Upkeep   " + lvl.Expense);
                lines.Add("Net / 30 days   " + Signed(CityEconomy.NetPer30Days(b, mods)));
                if (mods.Closed) lines.Add("<color=#ed5259>Closed for repairs after a fire</color>");
                if (mods.Park > 0) lines.Add("+" + mods.Park.ToString("0.#", CultureInfo.InvariantCulture) + "% nearby recreation");
                foreach (var sy in mods.Synergies) lines.Add("+" + sy.Pct + "% " + sy.Label);
                if (mods.Mood != 0) lines.Add((mods.Mood > 0 ? "+" : "") + mods.Mood + "% market mood");
                if (mods.MoodExpense != 0) lines.Add("+" + mods.MoodExpense + "% upkeep (panic)");
                if (mods.PolicyIncome != 0) lines.Add((mods.PolicyIncome > 0 ? "+" : "") + mods.PolicyIncome + "% policies (rent)");
                if (mods.PolicyExpense != 0) lines.Add((mods.PolicyExpense > 0 ? "+" : "") + mods.PolicyExpense + "% policies (upkeep)");
                if (mods.Event != 0) lines.Add((mods.Event > 0 ? "+" : "") + mods.Event + "% event");
                var next = Landmarks.NextLevelRequirements(def, s.Record);
                if (next.HasValue)
                {
                    lines.Add("");
                    lines.Add("Next: " + next.Value.name);
                    foreach (var l in next.Value.lines) lines.Add((l.Met ? "<color=#26c295>[x]</color> " : "[ ] ") + l.Label);
                }
            }
            _inspectorBody.text = string.Join("\n", lines);

            bool landmark = def.TradeLevels != null;
            var cost = def.Levels[b.Level - 1].UpgradeCost;
            _upgrade.gameObject.SetActive(!landmark);
            if (!landmark) { Ui.SetButtonLabel(_upgrade, cost.HasValue ? "Upgrade  " + cost.Value : "Max level"); Ui.SetButtonColor(_upgrade, cost.HasValue && b.Status == BuildStatus.Complete && s.Balance >= cost.Value ? new Color(0.1f, 0.5f, 0.4f) : Theme.PanelAlt); }
            Ui.SetButtonLabel(_demolish, "Demolish  (+" + City.DemolishRefund(b.Id) + ")");
        }

        void RefreshTransfer()
        {
            if (!_transferPanel.gameObject.activeSelf) return;
            var a = _world.Trading.State.Account;
            _transferInfo.text = "Trading account    " + a.Balance + "\nMovable to city    " + _world.Transfers.EligibleAmount()
                + "\nCity treasury      " + City.TreasuryBalance + "\nMovable to trading " + _world.Transfers.EligibleToTrading()
                + "\n<color=#8594ad>Only realized funds can leave the trading account.</color>";
        }

        void RefreshPolicies(CityState s)
        {
            foreach (var p in Modifiers.Policies)
            {
                bool on = s.Policies.Contains(p.Id);
                bool locked = !City.IsUnlocked(p.UnlockId);
                var b = _policyButtons[p.Id];
                Ui.SetButtonLabel(b, locked ? "LOCKED" : on ? "ON" : "OFF");
                Ui.SetButtonColor(b, on ? new Color(0.1f, 0.5f, 0.4f) : locked ? new Color(0.12f, 0.14f, 0.18f) : Theme.PanelAlt);
                _policyTexts[p.Id].text = locked && p.LockedText != null ? "<color=#f5bf3d>" + p.LockedText + "</color>\n" + p.Description : p.Description;
            }
        }
    }
}
