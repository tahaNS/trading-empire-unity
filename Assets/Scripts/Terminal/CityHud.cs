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

        /// <summary>The cards and buttons on the top row are laid out for about 1760 pixels; on a narrower screen the whole row scales down so nothing ever overlaps (like the web page does).</summary>
        void FitTopRow()
        {
            float width = ((RectTransform)transform).rect.width;
            if (Mathf.Approximately(width, _scaleWidth) || width < 100) return;
            _scaleWidth = width;
            float scale = Mathf.Clamp(width / 1760f, 0.45f, 1f);
            _topLeft.localScale = new Vector3(scale, scale, 1f);
            _topRight.localScale = new Vector3(scale, scale, 1f);
        }

        Text _date, _treasury, _net, _chips, _toast;
        readonly List<Button> _speedButtons = new List<Button>();
        BuildDock _dock;
        RectTransform _progressPanel, _progressContent;
        string _progressSig;
        Button _landButton, _progressButton, _policiesButton;
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

        /// <summary>True while the Finances or Policies panel is showing at the top left.</summary>
        public bool PanelOpen { get { return (_transferPanel != null && _transferPanel.gameObject.activeSelf) || (_policyPanel != null && _policyPanel.gameObject.activeSelf) || (_progressPanel != null && _progressPanel.gameObject.activeSelf); } }

        /// <summary>The building card and the side panels stop above the dock, whatever its height.</summary>
        void FitAbove()
        {
            if (_dock != null)
            {
                float above = _dock.TopY + 8f; // the cards stop above the dock
                if (_inspector != null) _inspector.offsetMin = new Vector2(_inspector.offsetMin.x, above);
                if (_transferPanel != null) _transferPanel.offsetMin = new Vector2(_transferPanel.offsetMin.x, above);
                if (_progressPanel != null) _progressPanel.offsetMin = new Vector2(_progressPanel.offsetMin.x, above);
            }
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_world != null) { _world.City.Changed -= MarkDirty; _world.Clock.ViewChanged -= MarkDirty; _world.Trading.Changed -= MarkDirty; } }

        public void RefreshNow() { _dirty = false; _scaleWidth = -1; FitTopRow(); _dock.RefreshNow(); FitAbove(); Refresh(); }

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
            _topRight.pivot = Vector2.one; _topRight.sizeDelta = new Vector2(880, 64); _topRight.anchoredPosition = new Vector2(-12, -12);
            var row = Ui.Rect(_topRight, "Pills");
            Ui.Place(row, new Vector2(0, 1), Vector2.one, new Vector2(0, -40), Vector2.zero);
            float rx = 0;
            AddRight(row, "Open Terminal", ref rx, 128, () => _openTerminal(), true);
            AddRight(row, "?", ref rx, 30, () => { if (_openMenu != null) _openMenu(4); });
            AddRight(row, "Photo", ref rx, 54, () => { if (OnPhoto != null) OnPhoto(); });
            _soundButton = AddRight(row, "Sound", ref rx, 58, () => { if (OnSound != null) OnSound(); _dirty = true; });
            if (_openMenu != null) AddRight(row, "Game", ref rx, 56, () => _openMenu(0));
            _policiesButton = AddRight(row, "Policies", ref rx, 90, () => Toggle(_policyPanel, _transferPanel));
            if (_openMenu != null) _progressButton = AddRight(row, "Progress", ref rx, 104, () => Toggle(_progressPanel, _transferPanel));
            AddRight(row, "Finances", ref rx, 74, () => Toggle(_transferPanel, _policyPanel));
            _landButton = AddRight(row, "Land", ref rx, 210, () => City.ExpandLand());

            _dock = BuildDock.Build(root, City);
            BuildInspector(root);
            BuildTransferPanel(root);
            BuildPolicyPanel(root);
            BuildProgressPanel(root);

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
            foreach (var p in new[] { _transferPanel, _policyPanel, _progressPanel }) if (p != null && p != show) p.gameObject.SetActive(false);
            show.gameObject.SetActive(!show.gameObject.activeSelf);
            _dirty = true;
        }

        Image _inspectorStripe, _siteFill;
        RectTransform _siteBar;
        Text _inspectorSub, _inspectorDesc, _inspectorVals, _siteText;
        string _confirmId;
        float _confirmUntil;

        void BuildInspector(RectTransform root)
        {
            _inspector = Ui.Card(root, "Inspector", 12f).rectTransform;
            Ui.Place(_inspector, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-350, 108), new Vector2(-12, -76));
            _inspectorStripe = Ui.Box(_inspector, "Stripe", Theme.Primary);
            Ui.Place(_inspectorStripe.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 10), new Vector2(5, -10));
            _inspectorTitle = Ui.Label(_inspector, "", 18, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_inspectorTitle.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(18, -38), new Vector2(-16, -8));
            _inspectorSub = Ui.Label(_inspector, "", 11, Theme.Muted, TextAnchor.UpperLeft);
            _inspectorSub.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_inspectorSub.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(18, -60), new Vector2(-16, -38));
            _siteBar = Ui.Rect(_inspector, "Site");
            Ui.Place(_siteBar, new Vector2(0, 1), Vector2.one, new Vector2(18, -78), new Vector2(-16, -62));
            var siteBg = Ui.Box(_siteBar, "Bg", new Color(0.90f, 0.925f, 0.945f));
            Ui.Rounded(siteBg, 4f); Ui.Stretch(siteBg.rectTransform);
            _siteFill = Ui.Box(_siteBar, "Fill", Theme.Primary);
            Ui.Rounded(_siteFill, 4f);
            _siteFill.rectTransform.anchorMin = Vector2.zero; _siteFill.rectTransform.anchorMax = new Vector2(0, 1); _siteFill.rectTransform.offsetMin = Vector2.zero; _siteFill.rectTransform.offsetMax = Vector2.zero;
            _siteText = Ui.Label(_siteBar, "", 10, Theme.Ink, TextAnchor.MiddleCenter);
            _inspectorDesc = Ui.Label(_inspector, "", 12, Theme.Ink, TextAnchor.UpperLeft);
            _inspectorDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_inspectorDesc.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(18, -124), new Vector2(-16, -84));
            _inspectorBody = Ui.Label(_inspector, "", 12, Theme.Muted, TextAnchor.UpperLeft);
            _inspectorBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_inspectorBody.rectTransform, Vector2.zero, Vector2.one, new Vector2(18, 100), new Vector2(-16, -128));
            _inspectorVals = Ui.Label(_inspector, "", 12, Theme.Ink, TextAnchor.UpperRight, false, true);
            Ui.Place(_inspectorVals.rectTransform, Vector2.zero, Vector2.one, new Vector2(18, 100), new Vector2(-16, -128));
            _upgrade = Ui.Pill(_inspector, "Upgrade", () => { if (_inspectedId != null) City.Upgrade(_inspectedId); }, true, 13);
            Ui.Place((RectTransform)_upgrade.transform, Vector2.zero, new Vector2(1, 0), new Vector2(16, 58), new Vector2(-16, 92));
            _demolish = Ui.Pill(_inspector, "Demolish", OnDemolish, false, 13);
            Ui.Place((RectTransform)_demolish.transform, Vector2.zero, new Vector2(0.62f, 0), new Vector2(16, 14), new Vector2(-4, 48));
            Style(_demolish, Color.white, Theme.Bad);
            var close = Ui.Pill(_inspector, "Close", () => City.Select(null), false, 13);
            Ui.Place((RectTransform)close.transform, new Vector2(0.62f, 0), new Vector2(1, 0), new Vector2(4, 14), new Vector2(-16, 48));
            _inspector.gameObject.SetActive(false);
        }

        /// <summary>Demolishing asks twice: the first click shows the refund, the second confirms.</summary>
        void OnDemolish()
        {
            if (_inspectedId == null) return;
            if (_confirmId == _inspectedId && Time.unscaledTime < _confirmUntil) { string id = _inspectedId; _confirmId = null; City.Demolish(id); return; }
            _confirmId = _inspectedId; _confirmUntil = Time.unscaledTime + 4f;
            _dirty = true;
        }

        void PanelHeader(RectTransform panel, string title)
        {
            var h = Ui.Label(panel, title, 17, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -44), new Vector2(-56, -8));
            var close = Ui.Pill(panel, "x", () => panel.gameObject.SetActive(false), false, 13);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -42), new Vector2(-12, -12));
        }

        RectTransform _financeContent;
        Text _financeLabels, _financeValues;

        /// <summary>A scrolling card with the panel header and the viewport; returns the content rectangle.</summary>
        RectTransform ScrollCard(RectTransform root, string name, string title, float width, out RectTransform card)
        {
            card = Ui.Card(root, name, 12f).rectTransform;
            Ui.Place(card, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -96), new Vector2(12 + width, -96));
            card.anchorMax = new Vector2(0, 1); card.anchorMin = new Vector2(0, 0);
            card.offsetMin = new Vector2(12, 120); card.offsetMax = new Vector2(12 + width, -96);
            PanelHeader(card, title);
            var view = Ui.Rect(card, "Viewport");
            Ui.Stretch(view, 4, 6, 4, 52);
            var vimg = view.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24f;
            return content;
        }

        static Text Block(RectTransform parent, TextAnchor anchor, bool bold = false, int size = 12)
        {
            var t = Ui.Label(parent, "", size, Theme.Ink, anchor, false, bold);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        void BuildTransferPanel(RectTransform root)
        {
            RectTransform card;
            var c = ScrollCard(root, "TransferPanel", "City finances", 400, out card);
            _transferPanel = card;
            _financeContent = c;
            var sub = Ui.Label(c, "CAPITAL TRANSFER", 11, Theme.Muted, TextAnchor.MiddleLeft, false, true);
            Ui.Place(sub.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -26), new Vector2(-12, -6));
            _transferInfo = Ui.Label(c, "", 12, Theme.Ink, TextAnchor.UpperLeft, true);
            Ui.Place(_transferInfo.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -100), new Vector2(-12, -28));
            _amount = Ui.Input(c, "1000", 14, InputField.ContentType.DecimalNumber);
            ((Image)_amount.targetGraphic).color = new Color(0.94f, 0.955f, 0.965f);
            _amount.textComponent.color = Theme.Ink;
            Ui.Place((RectTransform)_amount.transform, new Vector2(0, 1), Vector2.one, new Vector2(12, -138), new Vector2(-12, -106));
            var toCity = Ui.Pill(c, "To city  >", () => DoTransfer(true), true, 13);
            Ui.Place((RectTransform)toCity.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, -180), new Vector2(-4, -146));
            Style(toCity, Theme.Good, Color.white);
            var toTrading = Ui.Pill(c, "<  To trading", () => DoTransfer(false), true, 13);
            Ui.Place((RectTransform)toTrading.transform, new Vector2(0.5f, 1), Vector2.one, new Vector2(4, -180), new Vector2(-12, -146));
            _transferMessage = Ui.Label(c, "", 11, Theme.Muted, TextAnchor.UpperLeft);
            _transferMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_transferMessage.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -222), new Vector2(-12, -186));
            _financeLabels = Block(c, TextAnchor.UpperLeft);
            Ui.Place(_financeLabels.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -1200), new Vector2(-12, -230));
            _financeValues = Block(c, TextAnchor.UpperRight, true);
            Ui.Place(_financeValues.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(12, -1200), new Vector2(-12, -230));
            _transferPanel.gameObject.SetActive(false);
        }

        void FinanceText()
        {
            var s = City.State;
            long now = _world.Clock.Time;
            var last30 = Transactions.Summarize(s.Transactions, now - 30L * GameClock.DayMs, now);
            var all = Transactions.Summarize(s.Transactions, long.MinValue, now);
            var l = new List<string>(); var v = new List<string>();
            Action<string, string> row = (a, b) => { l.Add(a); v.Add(b); };
            Action<string> head = h => { l.Add("<b>" + h + "</b>"); v.Add(""); };
            row("Cash on hand", s.Balance.ToString()); row("", "");
            foreach (var (title, sm) in new[] { ("Last 30 days", last30), ("All time", all) })
            {
                head(title);
                row("Income", sm.Income.ToString()); row("Expenses", sm.Expenses.ToString()); row("Net operating", sm.NetOperating.ToSignedString());
                row("Construction, upgrades & land", sm.CapitalSpending.ToString()); row("Transfers in", sm.TransfersIn.ToString()); row("Transfers out", sm.TransfersOut.ToString());
                row("", "");
            }
            head("Income by building");
            if (s.Buildings.Count == 0) row("<color=#5c6b75>No buildings yet.</color>", "");
            foreach (var b in s.Buildings) row(Buildings.Get(b.DefId).Name + " <color=#5c6b75>(" + b.Id + ", L" + b.Level + ")</color>", b.LifetimeIncome + " - " + b.LifetimeExpense);
            row("", "");
            head("Recent transactions");
            for (int i = s.Transactions.Count - 1, n = 0; i >= 0 && n < 12; i--, n++)
            {
                var t = s.Transactions[i];
                row("<color=#5c6b75>" + DateTimeOffset.FromUnixTimeMilliseconds(t.Time).UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + "</color> " + t.Description, t.Amount.ToSignedString());
            }
            _financeLabels.text = string.Join("\n", l);
            _financeValues.text = string.Join("\n", v);
            float h = 230 + l.Count * 16 + 20;
            _financeContent.sizeDelta = new Vector2(0, h);
            _financeLabels.rectTransform.offsetMin = new Vector2(12, -h); _financeValues.rectTransform.offsetMin = new Vector2(12, -h);
        }

        void BuildProgressPanel(RectTransform root)
        {
            RectTransform card;
            _progressContent = ScrollCard(root, "ProgressPanel", "Progress", 400, out card);
            _progressPanel = card;
            _progressPanel.gameObject.SetActive(false);
        }

        static string UnlockName(string id)
        {
            var t = Land.Tiers.FirstOrDefault(x => x.UnlockId == id);
            if (t != null) return t.Name;
            var b = Buildings.All.FirstOrDefault(x => x.Id == id);
            return b != null ? b.Name + " (" + b.District + ")" : id;
        }

        /// <summary>Land, the missions with their progress bars, and the achievements: rebuilt only when something in them changed.</summary>
        void RefreshProgress(CityState s)
        {
            if (_progressPanel == null || !_progressPanel.gameObject.activeSelf) return;
            var prog = _world.Progression;
            var stats = prog.Stats;
            var sig = new System.Text.StringBuilder();
            sig.Append(s.LandTier).Append('|').Append(s.Balance.Cents).Append('|').Append(prog.Completed.Count).Append('|').Append(_world.Achievements.Earned.Count);
            foreach (var m in Missions.All) sig.Append('|').Append(Math.Min(stats.Get(m.Metric), m.Target));
            if (sig.ToString() == _progressSig) return;
            _progressSig = sig.ToString();
            for (int i = _progressContent.childCount - 1; i >= 0; i--) Ui.Kill(_progressContent.GetChild(i).gameObject);
            float y = 6;
            Func<string, int, Color, TextAnchor, bool, float, float, float, Text> text = (str, size, color, anchor, bold, x, w, h) =>
            {
                var t = Ui.Label(_progressContent, str, size, color, anchor, false, bold);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - h), new Vector2(x + w, -y));
                return t;
            };
            text("LAND", 11, Theme.Muted, TextAnchor.MiddleLeft, true, 12, 300, 18); y += 22;
            var tier = City.NextLandTier();
            if (tier == null) { text("You own all available land.", 12, Theme.Muted, TextAnchor.UpperLeft, false, 12, 360, 18); y += 24; }
            else
            {
                bool open = City.IsUnlocked(tier.UnlockId);
                var mission = Missions.Unlocking(tier.UnlockId);
                text("<b>" + tier.Name + "</b> · " + tier.Cost + " · " + tier.Area.W + "×" + tier.Area.H + " plots", 12, Theme.Ink, TextAnchor.UpperLeft, false, 12, 370, 18); y += 20;
                if (!open) { text("Locked: complete \"" + (mission != null ? mission.Title : "a mission") + "\" to unlock this land.", 11, Theme.Muted, TextAnchor.UpperLeft, false, 12, 370, 18); y += 20; }
                bool can = open && s.Balance >= tier.Cost;
                var buy = Ui.Pill(_progressContent, open && !can ? "Need " + tier.Cost : "Buy land", () => City.ExpandLand(), can, 12);
                Ui.Place((RectTransform)buy.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 30), new Vector2(190, -y));
                buy.interactable = can; y += 40;
            }
            text("MISSIONS", 11, Theme.Muted, TextAnchor.MiddleLeft, true, 12, 300, 18); y += 24;
            foreach (var m in Missions.All)
            {
                bool done = prog.Completed.ContainsKey(m.Id);
                long value = Math.Min(stats.Get(m.Metric), m.Target);
                Func<long, string> fmt = n => Missions.IsMoney(m.Metric) ? Money.FromCents(n).ToString().Replace(".00", "") : n.ToString();
                text((done ? "<color=#167a43>[x]</color> " : "") + "<b>" + m.Title + "</b>", 12, Theme.Ink, TextAnchor.UpperLeft, false, 12, 270, 18);
                text(done ? "Done" : fmt(value) + " / " + fmt(m.Target), 11, Theme.Muted, TextAnchor.UpperRight, false, 250, 130, 18); y += 18;
                text(m.Description, 11, Theme.Muted, TextAnchor.UpperLeft, false, 12, 366, 16); y += 17;
                if (m.Unlocks.Length > 0) { text("Unlocks: " + string.Join(", ", m.Unlocks.Select(UnlockName)), 11, Theme.Good, TextAnchor.UpperLeft, false, 12, 366, 16); y += 17; }
                if (!done)
                {
                    var bg = Ui.Box(_progressContent, "Bar", new Color(0.90f, 0.925f, 0.945f));
                    Ui.Rounded(bg, 3f);
                    Ui.Place(bg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -y - 7), new Vector2(378, -y));
                    var fill = Ui.Box(bg.transform, "Fill", Theme.Primary);
                    Ui.Rounded(fill, 3f);
                    fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(m.Target > 0 ? Mathf.Clamp01((float)value / m.Target) : 0, 1);
                    fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
                    y += 12;
                }
                y += 8;
            }
            var earned = _world.Achievements.Earned;
            text("ACHIEVEMENTS  " + earned.Count + "/" + Achievements.All.Count, 11, Theme.Muted, TextAnchor.MiddleLeft, true, 12, 300, 18); y += 24;
            foreach (var a in Achievements.All)
            {
                bool got = earned.Contains(a.Id);
                text((got ? "<color=#9e6b05>*</color> " : "<color=#9aa5ae>-</color> ") + "<b>" + a.Name + "</b>", 12, got ? Theme.Ink : Theme.Muted, TextAnchor.UpperLeft, false, 12, 366, 18); y += 17;
                text(a.Text, 11, Theme.Muted, TextAnchor.UpperLeft, false, 12, 366, 16); y += 22;
            }
            _progressContent.sizeDelta = new Vector2(0, y + 10);
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
            FitAbove();
            AnimateFloaters();
            if (_dirty) { _dirty = false; Refresh(); }
            if (_toast != null && _toast.transform.parent.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.transform.parent.gameObject.SetActive(false);
        }

        static string Signed(Money m) { return m.ToSignedString(); }

        void Refresh()
        {
            var s = City.State;
            _date.text = DateTimeOffset.FromUnixTimeMilliseconds(_world.Clock.Time).UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
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
            if (tier != null)
            {
                bool open = City.IsUnlocked(tier.UnlockId);
                bool ready = open && s.Balance >= tier.Cost;
                var mission = Missions.Unlocking(tier.UnlockId);
                string text = !open ? "More land: " + (mission != null ? mission.Title : "a mission")
                    : ready ? "Expand land · " + tier.Cost.ToString().Replace(".00", "")
                    : "Land needs " + (tier.Cost - (s.Balance > Money.Zero ? s.Balance : Money.Zero)).ToString().Replace(".00", "") + " more";
                Ui.SetButtonLabel(_landButton, text);
                Style(_landButton, ready ? Theme.Good : Color.white, ready ? Color.white : open ? Theme.Ink : Theme.Muted);
            }
            if (_progressButton != null) Ui.SetButtonLabel(_progressButton, "Progress " + _world.Progression.Completed.Count + "/" + Missions.All.Count);
            if (_policiesButton != null) Ui.SetButtonLabel(_policiesButton, s.Policies.Count > 0 ? "Policies (" + s.Policies.Count + ")" : "Policies");
            _dock.RefreshNow();
            var toastRt = (RectTransform)_toast.transform.parent;
            toastRt.offsetMin = new Vector2(-330, _dock.TopY + 6); toastRt.offsetMax = new Vector2(330, _dock.TopY + 46);

            if (s.Payouts != null && s.Payouts.Seq != _lastPayoutSeq) { _lastPayoutSeq = s.Payouts.Seq; SpawnFloaters(s.Payouts); }
            RefreshInspector(s);
            RefreshTransfer();
            RefreshProgress(s);
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

        static readonly Color[] DistrictColors = { new Color(0.435f, 0.69f, 0.353f), new Color(0.878f, 0.635f, 0.227f), new Color(0.424f, 0.518f, 0.69f), new Color(0.784f, 0.439f, 0.659f), new Color(0.31f, 0.71f, 0.604f), new Color(0.227f, 0.651f, 0.722f) };

        static string Signed(double v) { return (v > 0 ? "+" : "") + (Math.Round(v * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture); }

        void RefreshInspector(CityState s)
        {
            var b = s.SelectedId != null ? s.Buildings.FirstOrDefault(x => x.Id == s.SelectedId) : null;
            if (b == null || (_confirmId != null && b.Id != _confirmId)) _confirmId = null;
            _inspectedId = b != null ? b.Id : null;
            _inspector.gameObject.SetActive(b != null);
            if (b == null) return;
            var def = Buildings.Get(b.DefId);
            int level = Landmarks.EffectiveLevel(b.Level, def, s.Record);
            var lvl = def.Levels[level - 1];
            _inspectorStripe.color = DistrictColors[(int)def.District];
            _inspectorTitle.text = lvl.Name;
            bool building = b.Status == BuildStatus.Building;
            double daysLeft = Math.Max(1, Math.Ceiling((b.CompletesAt - _world.Clock.Time) / (double)GameClock.DayMs));
            _inspectorSub.text = def.District + " · Level " + level + " / " + def.Levels.Length + (building ? " · under construction, " + daysLeft + " days left" : "");
            _siteBar.gameObject.SetActive(building);
            if (building)
            {
                float k = Mathf.Clamp01((float)((_world.Clock.Time - b.BuiltAt) / Math.Max(b.CompletesAt - b.BuiltAt, 1.0)));
                _siteFill.rectTransform.anchorMax = new Vector2(k, 1);
                _siteText.text = daysLeft + " days left";
            }
            _inspectorDesc.text = def.Description;
            _inspectorDesc.rectTransform.offsetMax = new Vector2(-16, building ? -84 : -66);
            _inspectorBody.rectTransform.offsetMax = new Vector2(-16, building ? -128 : -110);
            _inspectorVals.rectTransform.offsetMax = new Vector2(-16, building ? -128 : -110);
            var labels = new List<string>(); var vals = new List<string>();
            Action<string, string> row = (l, v) => { labels.Add(l); vals.Add(v); };
            var mods = CityEconomy.ModsFor(b, s.Buildings, City.Context());
            row("Purchase cost", b.PurchaseCost.ToString());
            row("Income / " + def.IncomeIntervalDays + " days", lvl.Income.ToString());
            if (mods.Park > 0) row("Nearby parks", "+" + mods.Park.ToString("0.#", CultureInfo.InvariantCulture) + "% rent");
            foreach (var sy in mods.Synergies) row("Synergy: " + sy.Label, "+" + sy.Pct + "% rent");
            if (mods.Mood != 0) row("Market mood (" + s.Mood + ")", Signed(mods.Mood) + "% rent");
            if (mods.MoodExpense != 0) row("Market mood (" + s.Mood + ")", Signed(mods.MoodExpense) + "% upkeep");
            if (mods.PolicyIncome != 0) row("Policies", Signed(mods.PolicyIncome) + "% rent");
            if (mods.PolicyExpense != 0) row("Policies", Signed(mods.PolicyExpense) + "% upkeep");
            if (mods.Closed) row("Fire", "closed for repairs");
            else if (mods.Event != 0) row("Event", Signed(mods.Event) + "% rent");
            if (lvl.NeighborBonus != null) row("Boosts neighbours", "+" + lvl.NeighborBonus.Pct + "% within " + lvl.NeighborBonus.Radius);
            row("Operating expenses", lvl.Expense.ToString());
            row("Net income", CityEconomy.NetPer30Days(b, mods).ToSignedString());
            bool landmark = def.TradeLevels != null;
            if (!landmark) row("Upgrade cost", lvl.UpgradeCost.HasValue ? lvl.UpgradeCost.Value.ToString() : "Max level");
            if (!landmark && level < def.Levels.Length)
            {
                var next = def.Levels[level];
                row("Upgrades to", next.Name);
                row("Net after upgrade", (next.Income - next.Expense).ToSignedString());
            }
            row("Earned so far", b.LifetimeIncome.ToString());
            row("Costs so far", b.LifetimeExpense.ToString());
            row(building ? "First payout" : "Next payout", DateTimeOffset.FromUnixTimeMilliseconds(b.NextPayoutAt).UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
            string extra = "";
            if (landmark)
            {
                var next = Landmarks.NextLevelRequirements(def, s.Record);
                extra = "\n\n<color=#5c6b75>Its level follows your closed trades, not money. It can fall if your record does.</color>";
                if (next.HasValue)
                {
                    extra += "\n<b>Next: " + next.Value.name + "</b>";
                    foreach (var l in next.Value.lines) extra += "\n" + (l.Met ? "<color=#167a43>[x]</color> " : "[ ] ") + l.Label;
                }
                else extra += "\n<b>Top level reached</b>";
            }
            _inspectorBody.text = string.Join("\n", labels) + extra;
            _inspectorVals.text = string.Join("\n", vals);

            var cost = lvl.UpgradeCost;
            _upgrade.gameObject.SetActive(!landmark);
            if (!landmark)
            {
                bool can = cost.HasValue && !building && s.Balance >= cost.Value;
                Ui.SetButtonLabel(_upgrade, cost.HasValue ? "Upgrade (" + cost.Value + ")" : "Max level");
                Style(_upgrade, can ? Theme.Primary : Segment, can ? Color.white : Theme.Muted);
            }
            bool confirming = _confirmId == b.Id && Time.unscaledTime < _confirmUntil;
            Ui.SetButtonLabel(_demolish, confirming ? "Confirm: refund " + City.DemolishRefund(b.Id) : "Demolish");
            Style(_demolish, confirming ? Theme.Bad : Color.white, confirming ? Color.white : Theme.Bad);
        }

        void RefreshTransfer()
        {
            if (!_transferPanel.gameObject.activeSelf) return;
            var a = _world.Trading.State.Account;
            FinanceText();
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
