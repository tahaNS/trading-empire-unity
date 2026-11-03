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
        Action _openTerminal;
        Action<int> _openMenu;
        /// <summary>Set by the app after building: photo mode, sound on/off, and whether sound is off.</summary>
        public Action OnPhoto, OnSound;
        public Func<bool> IsMuted;
        Button _soundButton;
        RectTransform _topLeft, _topRight;
        float _scaleWidth = -1;

        /// <summary>The cards and buttons on the top row are laid out for about 1500 pixels; on a narrower screen the whole row scales down so nothing ever overlaps (like the web page does).</summary>
        void FitTopRow()
        {
            float width = ((RectTransform)transform).rect.width;
            if (Mathf.Approximately(width, _scaleWidth) || width < 100) return;
            _scaleWidth = width;
            float scale = Mathf.Clamp(width / 1500f, 0.45f, 1f);
            _topLeft.localScale = new Vector3(scale, scale, 1f);
            _topRight.localScale = new Vector3(scale, scale, 1f);
        }

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

        public static CityHud Build(Transform canvas, GameWorld world, Action openTerminal, Action<int> openMenu = null)
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

        public void RefreshNow() { _dirty = false; _scaleWidth = -1; FitTopRow(); Refresh(); }

        // ---------------------------------------------------------------- layout

        static readonly Color Segment = new Color(0.925f, 0.941f, 0.953f);

        static void Style(Button b, Color fill, Color text)
        {
            Ui.SetButtonColor(b, fill);
            b.transform.Find("Label").GetComponent<Text>().color = text;
        }

        static Text Caption(Transform parent, string text, float x, float w, float top, float height, int size, Color color, bool bold = false, bool mono = false)
        {
            var t = Ui.Label(parent, text, size, color, TextAnchor.MiddleLeft, mono, bold);
            Ui.Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -top - height), new Vector2(x + w, -top));
            return t;
        }

        void BuildUi(RectTransform root)
        {
            // top-left cards: treasury, net income, date and speed
            _topLeft = Ui.Rect(root, "TopLeft");
            Ui.Place(_topLeft, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            _topLeft.pivot = new Vector2(0, 1); _topLeft.sizeDelta = new Vector2(870, 100); _topLeft.anchoredPosition = Vector2.zero;
            var treasury = Ui.Card(_topLeft, "TreasuryCard");
            Ui.Place(treasury.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -84), new Vector2(212, -12));
            Caption(treasury.transform, "TREASURY", 14, 170, 8, 18, 11, Theme.Muted, true);
            _treasury = Caption(treasury.transform, "", 14, 180, 28, 34, 22, Theme.Ink, true, true);

            var net = Ui.Card(_topLeft, "NetCard");
            Ui.Place(net.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(220, -84), new Vector2(450, -12));
            Caption(net.transform, "NET INCOME / 30 DAYS", 14, 210, 8, 18, 11, Theme.Muted, true);
            _net = Caption(net.transform, "", 14, 210, 26, 26, 19, Theme.Good, true, true);
            _chips = Caption(net.transform, "", 14, 210, 50, 18, 11, Theme.Good, true);

            var date = Ui.Card(_topLeft, "DateCard");
            Ui.Place(date.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(458, -84), new Vector2(868, -12));
            Caption(date.transform, "DATE", 14, 120, 8, 18, 11, Theme.Muted, true);
            _date = Caption(date.transform, "", 14, 130, 30, 30, 16, Theme.Ink, true);
            var seg = Ui.Box(date.transform, "Segments", Segment);
            Ui.Rounded(seg, 8f);
            Ui.Place(seg.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(150, 14), new Vector2(-10, -14));
            int[] speeds = GameClock.Speeds;
            string[] names = { "Pause", "1x", "2x", "5x", "10x" };
            for (int i = 0; i < speeds.Length; i++)
            {
                int sp = speeds[i];
                var btn = Ui.Button(seg.transform, names[i], () => _world.Clock.SetSpeed(sp), Color.clear, 12, Theme.Ink);
                float w = 1f / speeds.Length;
                Ui.Place((RectTransform)btn.transform, new Vector2(w * i, 0), new Vector2(w * (i + 1), 1), new Vector2(3, 3), new Vector2(-3, -3));
                _speedButtons.Add(btn);
            }

            // top-right pills
            _topRight = Ui.Rect(root, "TopRight");
            Ui.Place(_topRight, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            _topRight.pivot = Vector2.one; _topRight.sizeDelta = new Vector2(640, 64); _topRight.anchoredPosition = new Vector2(-12, -12);
            var row = Ui.Rect(_topRight, "Pills");
            Ui.Place(row, new Vector2(0, 1), Vector2.one, new Vector2(0, -40), Vector2.zero);
            float rx = 0;
            AddRight(row, "Open Terminal", ref rx, 128, () => _openTerminal(), true);
            AddRight(row, "?", ref rx, 30, () => { if (_openMenu != null) _openMenu(4); });
            AddRight(row, "Photo", ref rx, 54, () => { if (OnPhoto != null) OnPhoto(); });
            _soundButton = AddRight(row, "Sound", ref rx, 58, () => { if (OnSound != null) OnSound(); _dirty = true; });
            if (_openMenu != null) AddRight(row, "Game", ref rx, 56, () => _openMenu(0));
            AddRight(row, "Policies", ref rx, 74, () => Toggle(_policyPanel, _transferPanel));
            if (_openMenu != null) AddRight(row, "Progress", ref rx, 74, () => _openMenu(1));
            AddRight(row, "Finances", ref rx, 74, () => Toggle(_transferPanel, _policyPanel));

            BuildBuildBar(root);
            BuildInspector(root);
            BuildTransferPanel(root);
            BuildPolicyPanel(root);

            _floaterLayer = Ui.Rect(root, "Floaters");
            Ui.Stretch(_floaterLayer);
            var toastBg = Ui.Card(root, "Toast", 10f, Color.white);
            Ui.Place(toastBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-330, 108), new Vector2(330, 148));
            _toast = Ui.Label(toastBg.transform, "", 14, Theme.Ink, TextAnchor.MiddleCenter);
            toastBg.gameObject.SetActive(false);
        }

        Button AddRight(RectTransform bar, string label, ref float rx, float w, Action act, bool primary = false)
        {
            var b = Ui.Pill(bar, label, act, primary, 13);
            Ui.Place((RectTransform)b.transform, new Vector2(1, 0), Vector2.one, new Vector2(rx - w, 0), new Vector2(rx, 0));
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
            var bar = Ui.Card(root, "BuildBar", 12f);
            Ui.Place(bar.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(12, 12), new Vector2(-12, 96));
            var hint = Ui.Label(bar.transform, "BUILD   click a building, then the map.  R rotates, Esc cancels", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(hint.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -22), new Vector2(-12, -3));
            _landButton = Ui.Pill(bar.transform, "Buy land", () => City.ExpandLand(), true, 11);
            Ui.Place((RectTransform)_landButton.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-232, -22), new Vector2(-8, -2));
            int n = Buildings.All.Count;
            for (int i = 0; i < n; i++)
            {
                var def = Buildings.All[i];
                string id = def.Id;
                var b = Ui.Button(bar.transform, def.Name + "\n" + def.Cost, () => City.StartPlacement(id), Color.white, 11, Theme.Ink);
                var ol = b.gameObject.AddComponent<Outline>();
                ol.effectColor = Theme.Line; ol.effectDistance = new Vector2(1.5f, 1.5f);
                float w = 1f / n;
                Ui.Place((RectTransform)b.transform, new Vector2(w * i, 0), new Vector2(w * (i + 1), 1), new Vector2(8, 8), new Vector2(-2, -26));
                _buildButtons[id] = b;
            }
        }

        void BuildInspector(RectTransform root)
        {
            _inspector = Ui.Card(root, "Inspector", 12f).rectTransform;
            Ui.Place(_inspector, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-340, 108), new Vector2(-12, -76));
            _inspectorTitle = Ui.Label(_inspector, "", 17, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_inspectorTitle.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -40), new Vector2(-16, -8));
            _inspectorBody = Ui.Label(_inspector, "", 12, Theme.Ink, TextAnchor.UpperLeft, true);
            _inspectorBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_inspectorBody.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 98), new Vector2(-16, -46));
            _upgrade = Ui.Pill(_inspector, "Upgrade", () => { if (_inspectedId != null) City.Upgrade(_inspectedId); }, true, 13);
            Ui.Place((RectTransform)_upgrade.transform, Vector2.zero, new Vector2(1, 0), new Vector2(16, 56), new Vector2(-16, 90));
            _demolish = Ui.Pill(_inspector, "Demolish", () => { if (_inspectedId != null) City.Demolish(_inspectedId); }, false, 13);
            Ui.Place((RectTransform)_demolish.transform, Vector2.zero, new Vector2(1, 0), new Vector2(16, 14), new Vector2(-16, 48));
            Style(_demolish, Color.white, Theme.Bad);
            _inspector.gameObject.SetActive(false);
        }

        void PanelHeader(RectTransform panel, string title)
        {
            var h = Ui.Label(panel, title, 17, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -44), new Vector2(-56, -8));
            var close = Ui.Pill(panel, "x", () => panel.gameObject.SetActive(false), false, 13);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -42), new Vector2(-12, -12));
        }

        void BuildTransferPanel(RectTransform root)
        {
            _transferPanel = Ui.Card(root, "TransferPanel", 12f).rectTransform;
            Ui.Place(_transferPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -392), new Vector2(412, -96));
            PanelHeader(_transferPanel, "Finances");
            var sub = Ui.Label(_transferPanel, "CAPITAL TRANSFER", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(sub.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -70), new Vector2(-16, -50));
            _transferInfo = Ui.Label(_transferPanel, "", 12, Theme.Ink, TextAnchor.UpperLeft, true);
            Ui.Place(_transferInfo.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -158), new Vector2(-16, -74));
            _amount = Ui.Input(_transferPanel, "1000", 14, InputField.ContentType.DecimalNumber);
            var amountBg = (Image)_amount.targetGraphic;
            amountBg.color = new Color(0.94f, 0.955f, 0.965f);
            _amount.textComponent.color = Theme.Ink;
            Ui.Place((RectTransform)_amount.transform, new Vector2(0, 1), Vector2.one, new Vector2(16, -198), new Vector2(-16, -166));
            var toCity = Ui.Pill(_transferPanel, "To city  >", () => DoTransfer(true), true, 13);
            Ui.Place((RectTransform)toCity.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(16, -240), new Vector2(-4, -206));
            Style(toCity, Theme.Good, Color.white);
            var toTrading = Ui.Pill(_transferPanel, "<  To trading", () => DoTransfer(false), true, 13);
            Ui.Place((RectTransform)toTrading.transform, new Vector2(0.5f, 1), Vector2.one, new Vector2(4, -240), new Vector2(-16, -206));
            _transferMessage = Ui.Label(_transferPanel, "", 11, Theme.Muted, TextAnchor.UpperLeft);
            _transferMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_transferMessage.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-16, -248));
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
            _transferMessage.color = r.Ok ? Theme.Good : Theme.Bad;
            _dirty = true;
        }

        void BuildPolicyPanel(RectTransform root)
        {
            const float Row = 96f;
            float h = 60 + Modifiers.Policies.Count * Row;
            _policyPanel = Ui.Card(root, "PolicyPanel", 12f).rectTransform;
            Ui.Place(_policyPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -96 - h), new Vector2(412, -96));
            PanelHeader(_policyPanel, "City policies");
            for (int i = 0; i < Modifiers.Policies.Count; i++)
            {
                var p = Modifiers.Policies[i];
                float top = -56 - i * Row;
                var card = Ui.Box(_policyPanel, "Policy " + p.Id, new Color(0.965f, 0.973f, 0.98f));
                Ui.Rounded(card, 8f);
                Ui.Place(card.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, top - Row + 6), new Vector2(-12, top));
                var title = Ui.Label(card.transform, p.Name, 13, Theme.Ink, TextAnchor.MiddleLeft, false, true);
                Ui.Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -30), new Vector2(-96, -6));
                var desc = Ui.Label(card.transform, p.Description, 11, Theme.Muted, TextAnchor.UpperLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Place(desc.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -32));
                string id = p.Id;
                var b = Ui.Pill(card.transform, "OFF", () => City.SetPolicy(id, !City.State.Policies.Contains(id)), false, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-88, -30), new Vector2(-10, -6));
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
            FitTopRow();
            AnimateFloaters();
            if (_dirty) { _dirty = false; Refresh(); }
            if (_toast != null && _toast.transform.parent.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
        }

        static string Signed(Money m) { return m.ToSignedString(); }

        void Refresh()
        {
            var s = City.State;
            _date.text = DateTimeOffset.FromUnixTimeMilliseconds(_world.Clock.Time).UtcDateTime.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            for (int i = 0; i < _speedButtons.Count; i++) { bool on = GameClock.Speeds[i] == _world.Clock.Speed; Style(_speedButtons[i], on ? Theme.Primary : Color.clear, on ? Color.white : Theme.Ink); }

            if (_soundButton != null) Ui.SetButtonLabel(_soundButton, IsMuted != null && IsMuted() ? "Muted" : "Sound");
            _treasury.text = s.Balance.ToString();
            _treasury.color = s.Balance.Cents < 0 ? Theme.Bad : Theme.Ink;
            var proj = CityEconomy.ProjectedNetPer30Days(s.Buildings, false, City.Context());
            _net.text = Signed(proj);
            _net.color = proj.Cents >= 0 ? Theme.Good : Theme.Bad;

            var chips = new List<string>();
            if (s.Mood == Mood.Booming) chips.Add("<color=#167a43>BOOM</color>");
            if (s.Mood == Mood.Panic) chips.Add("<color=#b3261e>PANIC</color>");
            foreach (var e in s.Events)
            {
                int days = (int)Math.Ceiling((e.EndsAt - _world.Clock.Time) / (double)GameClock.DayMs);
                var def = EventConfig.Get(e.Kind);
                chips.Add((e.Kind == EventKind.Fire ? "<color=#b3261e>" : e.Kind == EventKind.Storm ? "<color=#9e6b05>" : "<color=#167a43>") + def.Name.ToUpperInvariant() + " " + Math.Max(days, 0) + "d</color>");
            }
            _chips.text = string.Join("   ", chips);

            var tier = City.NextLandTier();
            _landButton.gameObject.SetActive(tier != null);
            if (tier != null) Ui.SetButtonLabel(_landButton, "Buy " + tier.Name.Replace(" District", "") + "  " + tier.Cost.ToString().Replace(".00", ""));

            foreach (var def in Buildings.All)
            {
                var b = _buildButtons[def.Id];
                bool locked = !City.IsUnlocked(def.UnlockId);
                bool afford = s.Balance >= def.Cost;
                bool active = s.Placement != null && s.Placement.DefId == def.Id;
                Ui.SetButtonLabel(b, (locked ? "[locked] " : "") + def.Name + "\n<color=" + (afford || locked ? "#5c6b75" : "#b3261e") + ">" + def.Cost + "</color>");
                Style(b, active ? Theme.Tint : locked ? Segment : Color.white, locked ? Theme.Muted : Theme.Ink);
                b.GetComponent<Outline>().effectColor = active ? Theme.Primary : Theme.Line;
            }

            if (s.Payouts != null && s.Payouts.Seq != _lastPayoutSeq) { _lastPayoutSeq = s.Payouts.Seq; SpawnFloaters(s.Payouts); }
            RefreshInspector(s);
            RefreshTransfer();
            RefreshPolicies(s);

            if (s.Notice != null && s.Notice.Seq != _lastToast)
            {
                _lastToast = s.Notice.Seq;
                _toast.text = s.Notice.Text;
                _toast.color = s.Notice.Kind == NoticeKind.Error ? Theme.Bad : s.Notice.Kind == NoticeKind.Success ? Theme.Good : Theme.Ink;
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
            lines.Add("<color=#5c6b75>" + def.Description + "</color>");
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
                if (mods.Closed) lines.Add("<color=#b3261e>Closed for repairs after a fire</color>");
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
                    foreach (var l in next.Value.lines) lines.Add((l.Met ? "<color=#167a43>[x]</color> " : "[ ] ") + l.Label);
                }
            }
            _inspectorBody.text = string.Join("\n", lines);

            bool landmark = def.TradeLevels != null;
            var cost = def.Levels[b.Level - 1].UpgradeCost;
            _upgrade.gameObject.SetActive(!landmark);
            if (!landmark) { Ui.SetButtonLabel(_upgrade, cost.HasValue ? "Upgrade  " + cost.Value : "Max level"); Style(_upgrade, cost.HasValue && b.Status == BuildStatus.Complete && s.Balance >= cost.Value ? Theme.Primary : Segment, cost.HasValue && b.Status == BuildStatus.Complete && s.Balance >= cost.Value ? Color.white : Theme.Muted); }
            Ui.SetButtonLabel(_demolish, "Demolish  (+" + City.DemolishRefund(b.Id) + ")");
        }

        void RefreshTransfer()
        {
            if (!_transferPanel.gameObject.activeSelf) return;
            var a = _world.Trading.State.Account;
            _transferInfo.text = "Trading account    " + a.Balance + "\nMovable to city    " + _world.Transfers.EligibleAmount()
                + "\nCity treasury      " + City.TreasuryBalance + "\nMovable to trading " + _world.Transfers.EligibleToTrading()
                + "\n<color=#5c6b75>Only realized funds can leave the trading account.</color>";
        }

        void RefreshPolicies(CityState s)
        {
            foreach (var p in Modifiers.Policies)
            {
                bool on = s.Policies.Contains(p.Id);
                bool locked = !City.IsUnlocked(p.UnlockId);
                var b = _policyButtons[p.Id];
                Ui.SetButtonLabel(b, locked ? "LOCKED" : on ? "ON" : "OFF");
                Style(b, on ? Theme.Good : locked ? Segment : Color.white, on ? Color.white : locked ? Theme.Muted : Theme.Ink);
                _policyTexts[p.Id].text = locked && p.LockedText != null ? "<color=#9e6b05>" + p.LockedText + "</color>\n" + p.Description : p.Description;
            }
        }
    }
}
