using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The trading terminal screen: top bar, chart, account and order panels, and the positions / orders / history /
    /// statistics tabs. All state comes from <see cref="TradingSession"/>; the UI never changes balances itself.
    /// </summary>
    public sealed partial class TerminalView : MonoBehaviour
    {
        public TradingSession Session { get; private set; }
        public ChallengeController Challenges;
        public ChartView Chart { get { return _chart; } }

        ChartView _chart;
        Text _symbol, _datasetName, _badge, _progress, _toast;
        Text _balance, _equity, _margin, _freeMargin, _unrealized, _realized;
        Text _tabBody;
        Text _positionHint;
        Button _play;
        readonly List<Button> _speedButtons = new List<Button>();
        readonly List<Button> _tfButtons = new List<Button>();
        readonly List<Button> _tabButtons = new List<Button>();
        RectTransform _tfRow, _tabContent, _positionRows, _loadPopover;
        InputField _lots, _sl, _tp, _path;
        Button _symbolButton;
        Text _loadMessage;
        string[] _symbolChoices;
        int _symbolIndex;
        int _tab;
        bool _dirty = true;
        float _toastUntil;
        int _lastToastSeq;
        long _tfBuiltFor = -1;
        readonly List<GameObject> _rows = new List<GameObject>();

        static readonly int[] Speeds = MarketReplay.Speeds;

        Action _onBack;

        public static TerminalView Build(Transform canvas, TradingSession session, Action onBack = null, ChallengeController challenges = null)
        {
            var root = Ui.Rect(canvas, "Terminal");
            Ui.Stretch(root);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = Theme.Bg;
            var view = root.gameObject.AddComponent<TerminalView>();
            view.Session = session;
            view._onBack = onBack;
            view.Challenges = challenges;
            view.BuildUi(root);
            view.AttachJournal();
            session.Changed += view.MarkDirty;
            return view;
        }

        void MarkDirty() { _dirty = true; }

        /// <summary>Show another trading session (a challenge run, or back to the game's own).</summary>
        public void SetSession(TradingSession session)
        {
            if (session == Session) return;
            Session.Changed -= MarkDirty;
            Session.Journal.Changed -= OnJournalChanged;
            Session.Drawings.Changed -= MarkDirty;
            Session = session;
            Session.Changed += MarkDirty;
            Session.Journal.Changed += OnJournalChanged;
            Session.Drawings.Changed += MarkDirty;
            _chart.Bind(Session);
            _tfBuiltFor = -1;
            _journalSig = null;
            _dirty = true;
        }

        /// <summary>Redraw immediately (used by the editor screenshot tool, where Update does not run).</summary>
        public void RefreshNow() { _dirty = false; _barWidth = -1; _topFitWidth = -1; FitRightColumn(); FitTopBar(); FitChartTop(); Refresh(); }

        public void SelectTab(int tab) { _tab = tab; _journalSig = null; _dirty = true; }

        public void OpenChallengeList() { ToggleChallengeList(true); }

        public void ShowLoadPopover(bool show) { _loadPopover.gameObject.SetActive(show); }
        void OnDestroy() { if (Session != null) Session.Changed -= MarkDirty; }

        // ---------------------------------------------------------------- layout

        void BuildUi(RectTransform root)
        {
            BuildTopBar(root);

            const float rightW = 330f, bottomH = 230f, topH = 84f, chartTopH = 38f;
            var chartHost = Ui.Rect(root, "ChartHost");
            Ui.Place(chartHost, Vector2.zero, Vector2.one, new Vector2(8, bottomH + 8), new Vector2(-rightW - 16, -topH - 8 - chartTopH));
            _chart = ChartView.Create(chartHost);
            Ui.Stretch((RectTransform)_chart.transform);
            _chart.Bind(Session);
            _chart.PlanFromPosition = PlanFrom;
            _chart.ToggleFull = ToggleFull;
            _chartHost = chartHost;
            BuildChartTop(root, topH, rightW, chartTopH);
            BuildFullBar(root);
            Session.Drawings.Changed += MarkDirty;

            var right = Ui.Rect(root, "RightPanel");
            _right = right;
            Ui.Place(right, new Vector2(1, 0), Vector2.one, new Vector2(-rightW - 8, 8), new Vector2(-8, -topH - 8)); // the full height: account, order and transfer stack in it
            // the right column scrolls when the screen is too short for account, order and transfer together
            var col = Ui.Rect(right, "Column");
            col.anchorMin = new Vector2(0, 1); col.anchorMax = new Vector2(1, 1); col.pivot = new Vector2(0.5f, 1);
            col.offsetMin = Vector2.zero; col.offsetMax = Vector2.zero;
            col.sizeDelta = new Vector2(0, 820);
            _rightCol = col;
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0, 0, 0, 0.001f);
            right.gameObject.AddComponent<RectMask2D>();
            var rightScroll = right.gameObject.AddComponent<ScrollRect>();
            rightScroll.viewport = right; rightScroll.content = col; rightScroll.horizontal = false;
            rightScroll.movementType = ScrollRect.MovementType.Clamped; rightScroll.scrollSensitivity = 24f;
            BuildRightPanel(col);
            BuildTransferCard(col);

            var bottom = Ui.Rect(root, "BottomPanel");
            _bottomPanel = bottom;
            Ui.Place(bottom, Vector2.zero, new Vector2(1, 0), new Vector2(8, 8), new Vector2(-rightW - 16, bottomH));
            BuildBottomPanel(bottom);

            BuildToast(root);
            _guide = TerminalGuide.Build(root, () => { if (StartTutorial != null) StartTutorial(); });
            BuildLoadPopover(root);
            BuildChallengeUi(root);
        }

        void BuildTopBar(RectTransform root)
        {
            var bar = Ui.Rect(root, "TopBar");
            _topBar = bar;
            Ui.Place(bar, new Vector2(0, 1), Vector2.one, new Vector2(8, -76), new Vector2(-8, -8));
            Ui.Panel(bar, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)bar.Find("Bg"));

            // row 1: identity
            var title = Ui.Label(bar, "TRADING TERMINAL", 15, Theme.Text, TextAnchor.MiddleLeft, false, true);
            Ui.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -30), new Vector2(190, -4));

            _symbol = Ui.Label(bar, "EUR/USD", 14, Color.black, TextAnchor.MiddleCenter, false, true);
            var symBg = Ui.Box(bar, "SymbolBg", Theme.Accent);
            Ui.Place(symBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(190, -28), new Vector2(300, -6));
            _symbol.transform.SetParent(symBg.transform, false);
            Ui.Stretch(_symbol.rectTransform);

            _datasetName = Ui.Label(bar, "", 13, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(_datasetName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(312, -30), new Vector2(760, -4));

            var badgeBg = Ui.Box(bar, "BadgeBg", new Color(0.35f, 0.25f, 0.05f));
            Ui.Place(badgeBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(748, -28), new Vector2(906, -6));
            _badge = Ui.Label(badgeBg.transform, "SYNTHETIC TEST DATA", 11, Theme.Accent, TextAnchor.MiddleCenter, false, true);

            if (_onBack != null)
            {
                var back = Ui.Button(bar, "< Back to city", () => _onBack(), Theme.PanelAlt, 12);
                Ui.Place((RectTransform)back.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-260, -28), new Vector2(-130, -6));
            }
            if (Challenges != null)
            {
                var ch = Ui.Button(bar, "Challenges", () => ToggleChallengeList(true), Theme.PanelAlt, 12);
                Ui.Place((RectTransform)ch.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-390, -28), new Vector2(-270, -6));
            }
            var guideBtn = Ui.Button(bar, "Guide", () => _guide.Show(true), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)guideBtn.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-460, -28), new Vector2(-398, -6));
            var load = Ui.Button(bar, "Load data...", () => _loadPopover.gameObject.SetActive(!_loadPopover.gameObject.activeSelf), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)load.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-120, -28), new Vector2(-10, -6));

            _progress = Ui.Label(bar, "", 12, Theme.Dim, TextAnchor.MiddleRight, true);
            Ui.Place(_progress.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-760, -30), new Vector2(-470, -4));

            // row 2: replay controls + timeframes
            float x = 12;
            _play = AddTopButton(bar, "Play", ref x, 84, () => Session.TogglePlay());
            AddTopButton(bar, "Next candle", ref x, 100, () => Session.Step());
            AddTopButton(bar, "Next bar", ref x, 84, () => Session.StepBar());
            x += 8;
            var sp = Ui.Label(bar, "SPEED", 11, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(sp.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 6), new Vector2(x + 44, 32));
            x += 44;
            foreach (int s in Speeds)
            {
                int speed = s;
                var b = AddTopButton(bar, s + "/s", ref x, 40, () => Session.SetSpeed(speed));
                _speedButtons.Add(b);
            }
            x += 8;
            AddTopButton(bar, "Restart", ref x, 74, () => Session.Restart());

            // the current replay time, at the right of row 2
            _clockText = Ui.Label(bar, "", 12, Theme.Text, TextAnchor.MiddleRight, true);
            Ui.Place(_clockText.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-420, 6), new Vector2(-12, 32));
        }

        Button AddTopButton(RectTransform bar, string label, ref float x, float w, Action onClick)
        {
            var b = Ui.Button(bar, label, onClick, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 6), new Vector2(x + w, 32));
            x += w + 4;
            return b;
        }

        void BuildRightPanel(RectTransform right)
        {
            // account card
            var acct = Ui.Rect(right, "Account");
            Ui.Place(acct, new Vector2(0, 1), Vector2.one, new Vector2(0, -226), Vector2.zero);
            Ui.Panel(acct, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)acct.Find("Bg"));
            var head = Ui.Label(acct, "ACCOUNT", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(head.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -26), new Vector2(-12, -4));
            _equity = Ui.Label(acct, "", 28, Theme.Text, TextAnchor.MiddleLeft, true, true);
            Ui.Place(_equity.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -66), new Vector2(-12, -28));
            var eqLabel = Ui.Label(acct, "EQUITY", 10, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(eqLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -80), new Vector2(-12, -64));

            _balance = StatCard(acct, "BALANCE", 0, 0);
            _freeMargin = StatCard(acct, "FREE MARGIN", 1, 0);
            _margin = StatCard(acct, "USED MARGIN", 0, 1);
            _unrealized = StatCard(acct, "UNREALIZED P/L", 1, 1);
            _realized = StatCard(acct, "REALIZED P/L", 0, 2);
            var eqNote = Ui.Label(acct, "Equity = balance + unrealized P/L (net of costs). Margin = notional / leverage.", 10, Theme.Dim, TextAnchor.UpperLeft);
            eqNote.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(eqNote.rectTransform, new Vector2(0.5f, 1), Vector2.one, new Vector2(6, -216), new Vector2(-12, -176));

            // order card
            var order = Ui.Rect(right, "Order");
            Ui.Place(order, new Vector2(0, 1), Vector2.one, new Vector2(0, -526), new Vector2(0, -232));
            Ui.Panel(order, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)order.Find("Bg"));
            var oh = Ui.Label(order, "ORDER", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(oh.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -26), new Vector2(-12, -4));

            // no plan yet: Long / Short start one
            _orderStart = Ui.Rect(order, "Start");
            Ui.Stretch(_orderStart, 0, 44, 0, 28);
            var longBtn = Ui.Button(_orderStart, "LONG  (L)", () => Session.StartDraft(Direction.Buy), new Color(0.1f, 0.5f, 0.4f), 15);
            Ui.Place((RectTransform)longBtn.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, -50), new Vector2(-4, -8));
            var shortBtn = Ui.Button(_orderStart, "SHORT  (S)", () => Session.StartDraft(Direction.Sell), new Color(0.65f, 0.2f, 0.24f), 15);
            Ui.Place((RectTransform)shortBtn.transform, new Vector2(0.5f, 1), Vector2.one, new Vector2(4, -50), new Vector2(-12, -8));
            var note = Ui.Label(_orderStart, "Plan a trade: three lines appear on the chart (entry, stop-loss, take-profit) that you can drag. Drag the entry away from the price to place a limit or stop order instead of a market order.", 11, Theme.Dim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(note.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -62));

            // a plan is open: its fields, what it would cost, and Place order
            _orderDraft = Ui.Rect(order, "Draft");
            Ui.Stretch(_orderDraft, 0, 44, 0, 28);
            _draftTitle = Ui.Label(order, "", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_draftTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(70, -26), new Vector2(-12, -4));
            _entry = DraftField(_orderDraft, "Entry (blank = market)", 0);
            _sl = DraftField(_orderDraft, "Stop-loss (pips)", 1);
            _tp = DraftField(_orderDraft, "Take-profit (pips)", 2);
            _sizeMode = Ui.Button(_orderDraft, "Risk %", () => Session.UpdateDraft(d => d.RiskSizing = !d.RiskSizing), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)_sizeMode.transform, new Vector2(0, 1), new Vector2(0.55f, 1), new Vector2(12, -4 - 3 * 29 - 26), new Vector2(-4, -4 - 3 * 29));
            _lots = Ui.Input(_orderDraft, "1", 14, InputField.ContentType.DecimalNumber);
            Ui.Place((RectTransform)_lots.transform, new Vector2(0.55f, 1), Vector2.one, new Vector2(0, -4 - 3 * 29 - 26), new Vector2(-12, -4 - 3 * 29));
            _draftInfo = Ui.Label(_orderDraft, "", 11, Theme.Text, TextAnchor.UpperLeft, true);
            _draftInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_draftInfo.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 40), new Vector2(-12, -4 - 4 * 29 - 2));
            _place = Ui.Button(_orderDraft, "Place order", () => Session.SubmitDraft(), new Color(0.1f, 0.5f, 0.4f), 14);
            Ui.Place((RectTransform)_place.transform, new Vector2(0, 0), new Vector2(0.62f, 0), new Vector2(12, 4), new Vector2(-4, 36));
            var cancel = Ui.Button(_orderDraft, "Cancel", () => Session.CancelDraft(), Theme.PanelAlt, 13);
            Ui.Place((RectTransform)cancel.transform, new Vector2(0.62f, 0), new Vector2(1, 0), new Vector2(4, 4), new Vector2(-12, 36));
            _entry.onEndEdit.AddListener(CommitEntry);
            _sl.onEndEdit.AddListener(text => CommitPips(text, true));
            _tp.onEndEdit.AddListener(text => CommitPips(text, false));
            _lots.onEndEdit.AddListener(CommitSize);

            var closeAll = Ui.Button(order, "Close all", () => Session.CloseAll(), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)closeAll.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 36));
            _orderDraft.gameObject.SetActive(false);
            _draftTitle.gameObject.SetActive(false);
        }

        RectTransform _chartHost, _right, _bottomPanel;
        Transform _chartTop;
        RectTransform _chartTopInner, _fullBar;
        Text _fullBarText;
        float _topFitWidth = -1;

        /// <summary>The chart strip is laid out for about 1180 pixels; when narrower, its buttons scale down together instead of overlapping.</summary>
        void FitChartTop()
        {
            if (_chartTopInner == null) return;
            float w = ((RectTransform)_chartTop).rect.width;
            if (Mathf.Approximately(w, _topFitWidth) || w < 100) return;
            _topFitWidth = w;
            float scale = Mathf.Clamp(w / 1180f, 0.5f, 1f);
            _chartTopInner.localScale = new Vector3(scale, scale, 1f);
            _chartTopInner.offsetMax = new Vector2(w / scale - w, 0f);
        }
        Text _clockText;
        Button _autoBtn, _objectsBtn, _indicatorsBtn, _topLong, _topShort, _fullBtn;

        /// <summary>The strip above the chart, as on the web: timeframes, auto scale, objects, indicators, and Long / Short / Full chart.</summary>
        void BuildChartTop(RectTransform root, float topH, float rightW, float chartTopH)
        {
            var top = Ui.Rect(root, "ChartTop");
            _chartTop = top;
            Ui.Place(top, new Vector2(0, 1), Vector2.one, new Vector2(8, -topH - 8 - chartTopH), new Vector2(-rightW - 16, -topH - 8));
            Ui.Panel(top, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)top.Find("Bg"));
            var inner = Ui.Rect(top, "Inner");
            _chartTopInner = inner;
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.pivot = new Vector2(0, 0.5f);
            inner.offsetMin = Vector2.zero; inner.offsetMax = Vector2.zero;
            _tfRow = Ui.Rect(inner, "Timeframes");
            Ui.Place(_tfRow, new Vector2(0, 0), new Vector2(0, 1), new Vector2(6, 0), new Vector2(520, 0));
            _autoBtn = Ui.Button(inner, "Auto scale on  A", () => _chart.SetAuto(!_chart.AutoScale), Theme.PanelAlt, 11);
            Ui.Place((RectTransform)_autoBtn.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(530, 4), new Vector2(648, -4));
            _objectsBtn = Ui.Button(inner, "Objects", () => _chart.ToggleObjectPanel(), Theme.PanelAlt, 11);
            Ui.Place((RectTransform)_objectsBtn.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(654, 4), new Vector2(752, -4));
            _indicatorsBtn = Ui.Button(inner, "Indicators", () => _chart.ToggleIndicatorPanel(), Theme.PanelAlt, 11);
            Ui.Place((RectTransform)_indicatorsBtn.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(758, 4), new Vector2(868, -4));
            _fullBtn = Ui.Button(inner, "Full chart  F", () => ToggleFull(), Theme.PanelAlt, 11);
            Ui.Place((RectTransform)_fullBtn.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-112, 4), new Vector2(-6, -4));
            _topShort = Ui.Button(inner, "Short  S", () => Session.StartDraft(Direction.Sell), new Color(0.65f, 0.2f, 0.24f), 11);
            Ui.Place((RectTransform)_topShort.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-196, 4), new Vector2(-118, -4));
            _topLong = Ui.Button(inner, "Long  L", () => Session.StartDraft(Direction.Buy), new Color(0.1f, 0.5f, 0.4f), 11);
            Ui.Place((RectTransform)_topLong.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-278, 4), new Vector2(-202, -4));
            _chart.StateChanged = RefreshChartTop;
        }

        /// <summary>In full-chart mode the order panel is hidden: a small bar at the bottom still lets you place or drop the plan.</summary>
        void BuildFullBar(RectTransform root)
        {
            var card = Ui.Card(root, "FullOrderBar", 10f, new Color(0.09f, 0.12f, 0.17f, 0.97f));
            _fullBar = card.rectTransform;
            Ui.Place(_fullBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-330, 22), new Vector2(330, 66));
            _fullBarText = Ui.Label(_fullBar, "", 12, Theme.Text, TextAnchor.MiddleLeft, true);
            Ui.Place(_fullBarText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-250, 0));
            var place = Ui.Button(_fullBar, "Place order", () => Session.SubmitDraft(), new Color(0.1f, 0.5f, 0.4f), 13);
            Ui.Place((RectTransform)place.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-240, 6), new Vector2(-96, -6));
            _fullPlace = place;
            var cancel = Ui.Button(_fullBar, "Cancel", () => Session.CancelDraft(), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)cancel.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-90, 6), new Vector2(-8, -6));
            _fullBar.gameObject.SetActive(false);
        }
        Button _fullPlace;

        void RefreshChartTop()
        {
            if (_autoBtn == null) return;
            Ui.SetButtonLabel(_autoBtn, _chart.AutoScale ? "Auto scale on  A" : "Auto scale off  A");
            Ui.SetButtonColor(_autoBtn, _chart.AutoScale ? Theme.PanelAlt : Theme.Blue);
            Ui.SetButtonLabel(_objectsBtn, _chart.ObjectCount > 0 ? "Objects (" + _chart.ObjectCount + ")" : "Objects");
            Ui.SetButtonColor(_objectsBtn, _chart.ObjectPanelOpen ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonLabel(_indicatorsBtn, _chart.IndicatorCount > 0 ? "Indicators (" + _chart.IndicatorCount + ")" : "Indicators");
            Ui.SetButtonColor(_indicatorsBtn, _chart.IndicatorPanelOpen ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonColor(_fullBtn, _full ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonLabel(_fullBtn, _full ? "Exit full chart  F" : "Full chart  F");
            bool can = Session.State.Status == SessionStatus.Ready && Session.Draft == null;
            _topLong.interactable = can; _topShort.interactable = can;
        }
        bool _full;

        /// <summary>Full chart: hide the account, order and positions panels so the chart fills the screen.</summary>
        public void ToggleFull()
        {
            _full = !_full;
            _right.gameObject.SetActive(!_full);
            _bottomPanel.gameObject.SetActive(!_full);
            _chartHost.offsetMin = _full ? new Vector2(8, 8) : new Vector2(8, 230 + 8);
            _chartHost.offsetMax = _full ? new Vector2(-8, -84 - 8 - 38) : new Vector2(-330 - 16, -84 - 8 - 38);
            var ct = (RectTransform)_chartTop;
            ct.offsetMax = new Vector2(_full ? -8 : -330 - 16, ct.offsetMax.y);
            _dirty = true;
        }

        TerminalGuide _guide;
        public TerminalGuide Guide { get { return _guide; } }
        public Action StartTutorial;
        RectTransform _orderStart, _orderDraft;
        Text _draftTitle, _draftInfo;
        InputField _entry;
        Button _sizeMode, _place;

        InputField DraftField(RectTransform parent, string label, int index)
        {
            float top = -4 - index * 29;
            var l = Ui.Label(parent, label, 11, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(l.rectTransform, new Vector2(0, 1), new Vector2(0.55f, 1), new Vector2(12, top - 26), new Vector2(0, top));
            var f = Ui.Input(parent, "", 14, InputField.ContentType.DecimalNumber);
            Ui.Place((RectTransform)f.transform, new Vector2(0.55f, 1), new Vector2(1, 1), new Vector2(0, top - 26), new Vector2(-12, top));
            return f;
        }

        // ---- the order plan: the fields edit the same draft the chart lines do

        void CommitEntry(string text)
        {
            var v = Session.DescribeDraft();
            if (v == null) return;
            long? next = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { next = Units.ParsePrice(text.Trim(), Session.Instrument.Digits); }
                catch (Exception) { Session.UpdateDraft(d => { }); return; }
            }
            long current = v.Resolution.EntryPrice;
            long target = next ?? OrderBuilder.EntryFor(v.Draft.Direction, Session.State.Bid.HasValue && Session.State.Ask.HasValue ? new Quote { Bid = Session.State.Bid.Value, Ask = Session.State.Ask.Value } : new Quote { Bid = current, Ask = current });
            long delta = target - current; // the stop and target keep their distances
            Session.UpdateDraft(d => { d.Entry = next; d.StopLoss += delta; if (d.TakeProfit.HasValue) d.TakeProfit += delta; });
        }

        void CommitPips(string text, bool stop)
        {
            var v = Session.DescribeDraft();
            if (v == null) return;
            double n;
            bool blank = string.IsNullOrWhiteSpace(text);
            if (blank && !stop) { Session.UpdateDraft(d => d.TakeProfit = null); return; }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || !(n > 0)) { Session.UpdateDraft(d => { }); return; }
            int sign = v.Draft.Direction == Direction.Buy ? 1 : -1;
            long entry = v.Resolution.EntryPrice;
            long level = (long)Math.Round(entry + (stop ? -sign : sign) * n * Session.Instrument.PipPoints);
            Session.UpdateDraft(d => { if (stop) d.StopLoss = level; else d.TakeProfit = level; });
        }

        void CommitSize(string text)
        {
            var v = Session.DescribeDraft();
            if (v == null) return;
            if (v.Draft.RiskSizing)
            {
                double pct;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out pct)) Session.UpdateDraft(d => d.RiskPct = pct); else Session.UpdateDraft(d => { });
            }
            else
            {
                var lots = Units.ParseLots(text);
                if (lots.HasValue) Session.UpdateDraft(d => d.Lots = lots.Value); else Session.UpdateDraft(d => { });
            }
        }

        static void SetIfIdle(InputField f, string text) { if (!f.isFocused && f.text != text) f.text = text; }

        void RefreshOrder()
        {
            var v = Session.DescribeDraft();
            bool open = v != null;
            if (_fullBar != null)
            {
                _fullBar.gameObject.SetActive(_full && open);
                if (_full && open)
                {
                    string fullKind = v.Resolution.Kind == EntryClass.Market ? "market" : v.Resolution.Kind == EntryClass.Limit ? "limit" : "stop";
                    _fullBarText.text = (v.Draft.Direction == Direction.Buy ? "LONG " : "SHORT ") + fullKind + "  " + (v.Resolution.Lots.HasValue ? Units.FormatLots(v.Resolution.Lots.Value) + " lots" : "-")
                        + (v.Preview != null ? "\nrisk " + (v.Preview.Risk.HasValue ? v.Preview.Risk.Value.ToString() : "-") + "  R:R " + (v.Preview.RewardRisk.HasValue ? v.Preview.RewardRisk.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-") : "")
                        + ((v.Resolution.Issue ?? v.Rejection) != null ? "\n" + (v.Resolution.Issue ?? v.Rejection) : "");
                    _fullPlace.interactable = v.Valid;
                }
            }
            _orderStart.gameObject.SetActive(!open);
            _orderDraft.gameObject.SetActive(open);
            _draftTitle.gameObject.SetActive(open);
            if (!open) return;
            var inst = Session.Instrument;
            var d = v.Draft; var r = v.Resolution;
            string kind = r.Kind == EntryClass.Market ? "market order" : r.Kind == EntryClass.Limit ? "limit order" : "stop order";
            _draftTitle.text = (d.Direction == Direction.Buy ? "LONG" : "SHORT") + "  -  " + kind;
            _draftTitle.color = d.Direction == Direction.Buy ? Theme.Bull : Theme.Bear;
            Func<long, string> pips = p => (Math.Abs(p - r.EntryPrice) / (double)inst.PipPoints).ToString("0.#", CultureInfo.InvariantCulture);
            SetIfIdle(_entry, d.Entry.HasValue ? Units.FormatPrice(d.Entry.Value, inst.Digits) : "");
            SetIfIdle(_sl, pips(d.StopLoss));
            SetIfIdle(_tp, d.TakeProfit.HasValue ? pips(d.TakeProfit.Value) : "");
            Ui.SetButtonLabel(_sizeMode, d.RiskSizing ? "Risk % of balance" : "Fixed lots");
            SetIfIdle(_lots, d.RiskSizing ? d.RiskPct.ToString("0.##", CultureInfo.InvariantCulture) : Units.FormatLots(d.Lots));
            var sb = new StringBuilder();
            sb.Append("Size   ").Append(r.Lots.HasValue ? Units.FormatLots(r.Lots.Value) + " lots" : "-").Append('\n');
            if (v.Preview != null)
            {
                sb.Append("Risk at SL   ").Append(v.Preview.Risk.HasValue ? v.Preview.Risk.Value.ToString() : "no stop").Append("    Margin   ").Append(v.Preview.Margin).Append('\n');
                sb.Append("Reward   ").Append(v.Preview.Reward.HasValue ? v.Preview.Reward.Value.ToString() : "no target").Append("    R:R   ").Append(v.Preview.RewardRisk.HasValue ? v.Preview.RewardRisk.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-");
            }
            string problem = r.Issue ?? v.Rejection;
            if (problem != null) sb.Append("\n<color=#ed5259>").Append(problem).Append("</color>");
            _draftInfo.text = sb.ToString();
            _place.interactable = v.Valid;
            Ui.SetButtonLabel(_place, r.Kind == EntryClass.Market ? "Place order" : "Place " + (r.Kind == EntryClass.Limit ? "limit" : "stop") + " order");
        }

        // ---- the capital transfer card (the same service the city's Finances panel uses)

        public GameWorld World;
        bool _transferToCity = true;
        Button _dirTrading, _dirCity, _moveButton;
        InputField _moveAmount;
        Text _transferText, _transferNote;

        void BuildTransferCard(RectTransform right)
        {
            var card = Ui.Rect(right, "TransferCard");
            Ui.Place(card, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -532));
            Ui.Panel(card, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)card.Find("Bg"));
            var h = Ui.Label(card, "TRANSFER FUNDS", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -26), new Vector2(-12, -4));
            _dirTrading = Ui.Button(card, "Trading > City", () => { _transferToCity = true; _dirty = true; }, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)_dirTrading.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, -56), new Vector2(-3, -30));
            _dirCity = Ui.Button(card, "City > Trading", () => { _transferToCity = false; _dirty = true; }, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)_dirCity.transform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(3, -56), new Vector2(-12, -30));
            _transferText = Ui.Label(card, "", 11, Theme.Text, TextAnchor.UpperLeft, true);
            _transferText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_transferText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -108), new Vector2(-12, -62));
            _moveAmount = Ui.Input(card, "1000", 13, InputField.ContentType.DecimalNumber);
            Ui.Place((RectTransform)_moveAmount.transform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, -144), new Vector2(-3, -114));
            _moveButton = Ui.Button(card, "Move", () => DoMove(), new Color(0.1f, 0.5f, 0.4f), 13);
            Ui.Place((RectTransform)_moveButton.transform, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(3, -144), new Vector2(-12, -114));
            _transferNote = Ui.Label(card, "", 11, Theme.Dim, TextAnchor.UpperLeft);
            _transferNote.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_transferNote.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -150));
        }

        void DoMove()
        {
            if (World == null || Challenges != null && Challenges.Active) return;
            Money amount;
            try { amount = Money.ParseDollars((_moveAmount.text ?? "").Replace(",", "")); }
            catch (FormatException) { _transferNote.text = "Enter an amount like 1500 or 1500.50."; _transferNote.color = new Color(0.93f, 0.32f, 0.36f); return; }
            string request = Guid.NewGuid().ToString();
            var r = _transferToCity ? World.Transfers.Transfer(request, amount) : World.Transfers.TransferToTrading(request, amount);
            _transferNote.text = r.Ok ? "Moved " + amount + (_transferToCity ? " to the city treasury." : " to the trading account.") : r.Message;
            _transferNote.color = r.Ok ? Theme.Bull : Theme.Bear;
            _dirty = true;
        }

        void RefreshTransfer()
        {
            if (_transferText == null) return;
            bool locked = World == null || Challenges != null && Challenges.Active;
            Ui.SetButtonColor(_dirTrading, _transferToCity ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonColor(_dirCity, !_transferToCity ? Theme.Blue : Theme.PanelAlt);
            _moveButton.interactable = !locked;
            if (locked) { _transferText.text = World == null ? "" : "Not available in a challenge."; return; }
            var a = World.Trading.State.Account;
            _transferText.text = "Trading balance  " + a.Balance + "\nCity treasury    " + World.City.TreasuryBalance + "\nYou can move     " + (_transferToCity ? World.Transfers.EligibleAmount() : World.Transfers.EligibleToTrading())
                ;
            if (string.IsNullOrEmpty(_transferNote.text)) { _transferNote.text = "Only realized funds can leave the trading account."; _transferNote.color = Theme.Dim; }
        }

        /// <summary>A small rounded card with a caption and a value, two to a row, as in the web account panel.</summary>
        Text StatCard(RectTransform parent, string caption, int col, int row)
        {
            float w = 0.5f;
            var card = Ui.Box(parent, "Stat " + caption, Theme.PanelAlt);
            Ui.Rounded(card, 6f);
            float top = -94 - row * 44;
            Ui.Place(card.rectTransform, new Vector2(col * w, 1), new Vector2((col + 1) * w, 1), new Vector2(col == 0 ? 8 : 3, top - 40), new Vector2(col == 0 ? -3 : -8, top));
            var cap = Ui.Label(card.transform, caption, 9, Theme.Dim, TextAnchor.MiddleLeft, false, true);
            Ui.Place(cap.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -16), new Vector2(-4, -3));
            var v = Ui.Label(card.transform, "", 13, Theme.Text, TextAnchor.MiddleLeft, true, true);
            Ui.Place(v.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(8, 2), new Vector2(-4, -16));
            return v;
        }

        Text StatRow(RectTransform parent, string label, int index)
        {
            float top = -92 - index * 22;
            var l = Ui.Label(parent, label, 12, Theme.Dim, TextAnchor.MiddleLeft);
            Ui.Place(l.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(12, top - 20), new Vector2(0, top));
            var v = Ui.Label(parent, "", 13, Theme.Text, TextAnchor.MiddleRight, true);
            Ui.Place(v.rectTransform, new Vector2(0.4f, 1), new Vector2(1, 1), new Vector2(0, top - 20), new Vector2(-12, top));
            return v;
        }

        void BuildBottomPanel(RectTransform bottom)
        {
            Ui.Panel(bottom, "Bg", Theme.Panel).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)bottom.Find("Bg"));
            string[] names = { "Open positions", "Orders", "History", "Journal", "Statistics" };
            float x = 10;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = Ui.Button(bottom, names[i], () => { _tab = index; _dirty = true; }, Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -34), new Vector2(x + 128, -8));
                _tabButtons.Add(b);
                x += 132;
            }
            _tabContent = Ui.Rect(bottom, "TabContent");
            Ui.Place(_tabContent, Vector2.zero, Vector2.one, new Vector2(10, 6), new Vector2(-10, -40));
            _tabBody = Ui.Label(_tabContent, "", 12, Theme.Text, TextAnchor.UpperLeft, true);
            _tabBody.verticalOverflow = VerticalWrapMode.Truncate;
            _positionRows = Ui.Rect(_tabContent, "Rows");
            // the tab content scrolls (mouse wheel) when it is taller than the panel, so nothing is ever cut off
            var tabImg = _tabContent.gameObject.AddComponent<Image>();
            tabImg.color = new Color(0, 0, 0, 0.001f);
            _tabContent.gameObject.AddComponent<RectMask2D>();
            _positionRows.anchorMin = new Vector2(0, 1); _positionRows.anchorMax = new Vector2(1, 1); _positionRows.pivot = new Vector2(0.5f, 1);
            _positionRows.offsetMin = Vector2.zero; _positionRows.offsetMax = Vector2.zero;
            _tabScroll = _tabContent.gameObject.AddComponent<ScrollRect>();
            _tabScroll.viewport = _tabContent; _tabScroll.content = _positionRows; _tabScroll.horizontal = false;
            _tabScroll.movementType = ScrollRect.MovementType.Clamped; _tabScroll.scrollSensitivity = 24f;
        }

        ScrollRect _tabScroll;

        /// <summary>Size the scrolling area to what the tab built (rows hang from the top), at least as tall as the panel.</summary>
        void FitRowsHeight()
        {
            float needed = 0;
            foreach (Transform child in _positionRows)
            {
                var rt = (RectTransform)child;
                if (!rt.gameObject.activeSelf) continue;
                needed = Mathf.Max(needed, -rt.offsetMin.y);
            }
            float view = _tabContent.rect.height;
            _positionRows.sizeDelta = new Vector2(0, Mathf.Max(view, needed + 6f));
        }

        void BuildToast(RectTransform root)
        {
            var bg = Ui.Box(root, "Toast", new Color(0.1f, 0.14f, 0.2f, 0.96f));
            Ui.Place(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-300, -140), new Vector2(300, -100));
            _toast = Ui.Label(bg.transform, "", 14, Theme.Text, TextAnchor.MiddleCenter);
            bg.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- load data

        void BuildLoadPopover(RectTransform root)
        {
            _loadPopover = Ui.Rect(root, "LoadPopover");
            Ui.Place(_loadPopover, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-520, -250), new Vector2(-12, -86));
            Ui.Panel(_loadPopover, "Bg", new Color(0.09f, 0.12f, 0.17f, 0.99f)).rectTransform.SetAsFirstSibling();
            Ui.Stretch((RectTransform)_loadPopover.Find("Bg"));

            var h = Ui.Label(_loadPopover, "LOAD A CSV / MT4 / MT5 FILE", 12, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -28), new Vector2(-12, -4));

            _path = Ui.Input(_loadPopover, "", 13);
            Ui.Place((RectTransform)_path.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -64), new Vector2(-12, -34));
            var ph = Ui.Label(_path.transform, "Full path to the file, e.g. /Users/you/EURUSD_M1.csv", 12, Theme.Dim, TextAnchor.MiddleLeft, true);
            Ui.Stretch(ph.rectTransform, 8, 2, 8, 2);
            _path.placeholder = ph;

            _symbolChoices = new[] { "auto" }.Concat(Instruments.All.Select(i => i.Symbol)).ToArray();
            _symbolButton = Ui.Button(_loadPopover, "Symbol: Auto (from file name)", () =>
            {
                _symbolIndex = (_symbolIndex + 1) % _symbolChoices.Length;
                Ui.SetButtonLabel(_symbolButton, "Symbol: " + SymbolLabel());
            }, Theme.PanelAlt, 12);
            Ui.Place((RectTransform)_symbolButton.transform, new Vector2(0, 1), new Vector2(0.62f, 1), new Vector2(12, -98), new Vector2(-4, -72));
            var go = Ui.Button(_loadPopover, "Load", LoadFromPath, new Color(0.1f, 0.5f, 0.4f), 13);
            Ui.Place((RectTransform)go.transform, new Vector2(0.62f, 1), new Vector2(1, 1), new Vector2(4, -98), new Vector2(-12, -72));

            _loadMessage = Ui.Label(_loadPopover, "", 11, Theme.Dim, TextAnchor.UpperLeft);
            Ui.Place(_loadMessage.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 6), new Vector2(-12, -104));
            _loadMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            _loadPopover.gameObject.SetActive(false);
        }

        string SymbolLabel()
        {
            string s = _symbolChoices[_symbolIndex];
            return s == "auto" ? "Auto (from file name)" : Instruments.Find(s).Display;
        }

        void LoadFromPath()
        {
            string path = (_path.text ?? "").Trim().Trim('"');
            if (path == "") { _loadMessage.text = "Type the path to a file first."; return; }
            if (!File.Exists(path)) { _loadMessage.text = "File not found: " + path; return; }
            string name = Path.GetFileName(path);
            var inst = (_symbolChoices[_symbolIndex] == "auto" ? Instruments.Detect(name) : Instruments.Find(_symbolChoices[_symbolIndex])) ?? Session.Instrument;
            Session.BeginLoading();
            var result = CsvParser.Parse(File.ReadAllText(path), new ParseOptions { Digits = inst.Digits });
            var report = new LoadReport { RowsRead = result.RowsRead, RowsRejected = result.RowsRejected, DuplicatesDropped = result.DuplicatesDropped, WasUnsorted = result.WasUnsorted, Errors = result.Errors };
            string wrong = result.Ok ? Instruments.CheckPrices(inst, result.Candles.Select(c => c.Close).ToList()) : null;
            if (!result.Ok) { Session.Fail(result.Fatal ?? "Could not read the file.", report); _loadMessage.text = result.Fatal; }
            else if (wrong != null) { Session.Fail(wrong, report); _loadMessage.text = wrong; }
            else
            {
                Session.LoadDataset(result.Candles, name, false, report, null, inst.Symbol);
                _loadMessage.text = "Loaded " + result.Candles.Count + " candles as " + inst.Display + ".";
                _loadPopover.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- actions

        /// <summary>A position box on the chart fills the order panel: its stop and target distances in pips.</summary>
        void PlanFrom(Drawing d)
        {
            var a = d.GetAnchor("a"); var b = d.GetAnchor("b"); var c = d.GetAnchor("c");
            if (!a.HasValue || !b.HasValue || !c.HasValue) return;
            var dir = d.Str("side") == "short" ? Direction.Sell : Direction.Buy;
            Session.StartDraft(dir);
            var v = Session.DescribeDraft();
            if (v == null) return;
            long entry = v.Resolution.EntryPrice;
            long sl = (long)Math.Round(Math.Abs(a.Value.Price - c.Value.Price)), tp = (long)Math.Round(Math.Abs(b.Value.Price - a.Value.Price));
            int sign = dir == Direction.Buy ? 1 : -1;
            Session.UpdateDraft(x => { x.StopLoss = entry - sign * sl; x.TakeProfit = entry + sign * tp; });
            _toast.text = "Plan filled from the " + (dir == Direction.Buy ? "long" : "short") + " box: check it and press Place order";
            _toast.color = Theme.Text;
            _toast.transform.parent.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 4f;
        }

        static double? ParsePips(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : (double?)null;
        }

        // ---------------------------------------------------------------- per-frame

        RectTransform _topBar, _rightCol;
        float _barWidth = -1;

        /// <summary>The top bar is laid out for about 1640 pixels; on a narrower screen it scales down (keeping the full width) so nothing overlaps.</summary>
        void FitTopBar()
        {
            float width = ((RectTransform)transform).rect.width;
            if (Mathf.Approximately(width, _barWidth) || width < 100) return;
            _barWidth = width;
            float scale = Mathf.Clamp(width / 1640f, 0.45f, 1f);
            _topBar.pivot = new Vector2(0, 1);
            _topBar.localScale = new Vector3(scale, scale, 1f);
            float inner = width - 16f;
            _topBar.offsetMin = new Vector2(8f, -76f);
            _topBar.offsetMax = new Vector2(inner / scale - width + 8f, -8f);
        }

        void FitRightColumn()
        {
            if (_rightCol == null) return;
            float h = Mathf.Max(((RectTransform)_rightCol.parent).rect.height, 820f);
            if (!Mathf.Approximately(_rightCol.sizeDelta.y, h)) _rightCol.sizeDelta = new Vector2(0, h);
        }

        void Update()
        {
            FitRightColumn();
            FitTopBar();
            FitChartTop();
            HandleKeys();
            if (_dirty) { _dirty = false; Refresh(); }
            if (_toast != null && _toast.transform.parent.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.transform.parent.gameObject.SetActive(false);
        }

        void HandleKeys()
        {
            // typing in a text field must never trigger the game's shortcuts
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<InputField>() != null) return;
            if (_guide != null && _guide.Open) return; // the guide window has the keyboard
            if (Input.GetKeyDown(KeyCode.Escape) && _full) { ToggleFull(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && Session.Draft != null) { Session.CancelDraft(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && _onBack != null) _onBack();
            if (Input.GetKeyDown(KeyCode.Space)) Session.TogglePlay();
            if (Input.GetKeyDown(KeyCode.RightArrow)) { if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Session.StepBar(); else Session.Step(); }
            if (Input.GetKeyDown(KeyCode.L)) Session.StartDraft(Direction.Buy);
            if (Input.GetKeyDown(KeyCode.S)) Session.StartDraft(Direction.Sell);
            if (Input.GetKeyDown(KeyCode.Return) && Session.Draft != null) Session.SubmitDraft();
        }

        void Refresh()
        {
            var s = Session.State;
            var inst = Session.Instrument;
            _symbol.text = inst.Display;
            _datasetName.text = s.Dataset != null ? s.Dataset.Name : "No dataset";
            _badge.transform.parent.gameObject.SetActive(s.Dataset != null && s.Dataset.Synthetic);
            if (_clockText != null) _clockText.text = s.Now.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(s.Now.Value).UtcDateTime.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture) + " UTC" : "";
            _progress.text = s.Total > 0 ? "candle " + s.RevealedCount.ToString("N0", CultureInfo.InvariantCulture) + " / " + s.Total.ToString("N0", CultureInfo.InvariantCulture) : "";
            Ui.SetButtonLabel(_play, s.Playing ? "Pause" : "Play");
            for (int i = 0; i < _speedButtons.Count; i++) Ui.SetButtonColor(_speedButtons[i], Speeds[i] == s.Speed ? Theme.Blue : Theme.PanelAlt);
            BuildTimeframes(s);

            var a = s.Account;
            _equity.text = a.Equity.ToString();
            _balance.text = a.Balance.ToString();
            _margin.text = a.UsedMargin.ToString();
            _freeMargin.text = a.FreeMargin.ToString();
            Signed(_unrealized, a.UnrealizedPnl);
            Signed(_realized, a.RealizedPnl);

            _chart.Refresh(Session.ChartData());
            RefreshChallenge();
            RefreshChartTop();
            RefreshOrder();
            RefreshTransfer();
            RefreshTabs(s, inst);
            FitRowsHeight();

            if (s.Notice != null && s.Notice.Seq != _lastToastSeq)
            {
                _lastToastSeq = s.Notice.Seq;
                _toast.text = s.Notice.Text;
                _toast.color = s.Notice.Kind == NoticeKind.Error ? new Color(1f, 0.55f, 0.55f) : s.Notice.Kind == NoticeKind.Success ? new Color(0.55f, 1f, 0.8f) : Theme.Text;
                _toast.transform.parent.gameObject.SetActive(true);
                _toastUntil = Time.unscaledTime + 4f;
            }
            if (s.Error != null && s.Status != SessionStatus.Loading) { /* shown through the load popover message */ }
        }

        static void Signed(Text t, Money m)
        {
            t.text = m.ToSignedString();
            t.color = m.Cents > 0 ? Theme.Bull : m.Cents < 0 ? Theme.Bear : Theme.Text;
        }

        void BuildTimeframes(SessionState s)
        {
            if (_tfBuiltFor != s.NativeMs)
            {
                foreach (var b in _tfButtons) Ui.Kill(b.gameObject);
                _tfButtons.Clear();
                var tfs = Timeframes.Available(s.NativeMs);
                float w = 40f;
                for (int i = 0; i < tfs.Count; i++)
                {
                    long ms = tfs[i].Ms;
                    var b = Ui.Button(_tfRow, tfs[i].Label, () => Session.SetTimeframe(ms), Theme.PanelAlt, 11);
                    Ui.Place((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(i * (w + 2), 3), new Vector2(i * (w + 2) + w, -3));
                    b.name = "TF " + ms;
                    _tfButtons.Add(b);
                }
                _tfBuiltFor = s.NativeMs;
            }
            var all = Timeframes.Available(s.NativeMs);
            for (int i = 0; i < _tfButtons.Count && i < all.Count; i++) Ui.SetButtonColor(_tfButtons[i], all[i].Ms == s.ChartTfMs ? Theme.Blue : Theme.PanelAlt);
        }

        void RefreshTabs(SessionState s, InstrumentDefinition inst)
        {
            for (int i = 0; i < _tabButtons.Count; i++) Ui.SetButtonColor(_tabButtons[i], i == _tab ? Theme.Blue : Theme.PanelAlt);
            Ui.SetButtonLabel(_tabButtons[0], "Open positions (" + s.Positions.Count + ")");
            Ui.SetButtonLabel(_tabButtons[1], "Orders (" + s.Pending.Count + ")");
            Ui.SetButtonLabel(_tabButtons[2], "History (" + s.Closed.Count + ")");
            if (_tab == 3)
            {
                // the journal has text fields: rebuild it only when its own content changed, so typing is never interrupted
                string sig = JournalSignature(s);
                if (sig == _journalSig) return;
                _journalSig = sig;
            }
            else _journalSig = null;
            foreach (var r in _rows) Ui.Kill(r);
            _rows.Clear();
            _tabBody.alignment = TextAnchor.UpperLeft;
            _tabBody.text = "";
            int d = inst.Digits;
            if (_tab == 0) PositionRows(s, d);
            else if (_tab == 1) OrderRows(s, d);
            else if (_tab == 2) _tabBody.text = History(s);
            else if (_tab == 3) JournalTab(s);
            else _tabBody.text = Stats(s);
        }

        void PositionRows(SessionState s, int digits)
        {
            if (s.Positions.Count == 0) { _tabBody.alignment = TextAnchor.MiddleCenter; _tabBody.text = "<color=#8594ad>No open positions.\nPlan a trade with Long or Short.</color>"; return; }
            _tabBody.text = Pad("SIDE", 6) + Pad("SIZE", 7) + Pad("ENTRY", 12) + Pad("SL", 12) + Pad("TP", 12) + "P/L";
            int row = 0;
            foreach (var pv in s.Positions.Take(7))
            {
                var p = pv.Position;
                string color = pv.Unrealized.Cents >= 0 ? "#26c295" : "#ed5259";
                var line = Ui.Label(_positionRows, Pad(p.Direction == Direction.Buy ? "BUY" : "SELL", 6) + Pad(Units.FormatLots(p.Lots), 7)
                    + Pad(Units.FormatPrice(p.EntryPrice, digits), 12) + Pad(p.StopLoss.HasValue ? Units.FormatPrice(p.StopLoss.Value, digits) : "-", 12)
                    + Pad(p.TakeProfit.HasValue ? Units.FormatPrice(p.TakeProfit.Value, digits) : "-", 12) + "<color=" + color + ">" + pv.Unrealized.ToSignedString() + "</color>", 12, Theme.Text, TextAnchor.UpperLeft, true);
                Ui.Place(line.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -22 - (row + 1) * 24), new Vector2(-110, -(row + 1) * 24 + 2));
                string id = p.Id;
                var close = Ui.Button(_positionRows, "Close", () => Session.ClosePosition(id), Theme.PanelAlt, 11);
                Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96, -22 - (row + 1) * 24), new Vector2(-6, -(row + 1) * 24 + 2));
                _rows.Add(line.gameObject); _rows.Add(close.gameObject);
                row++;
            }
        }

        void OrderRows(SessionState s, int digits)
        {
            if (s.Pending.Count == 0) { _tabBody.text = "<color=#8594ad>No resting orders. Limit and stop orders appear here.</color>"; return; }
            var sb = new StringBuilder(Pad("SIDE", 6) + Pad("TYPE", 7) + Pad("SIZE", 7) + Pad("PRICE", 12) + Pad("SL", 12) + "TP\n");
            foreach (var o in s.Pending.Take(8))
                sb.Append(Pad(o.Direction == Direction.Buy ? "BUY" : "SELL", 6) + Pad(o.Kind == PendingKind.Limit ? "LIMIT" : "STOP", 7) + Pad(Units.FormatLots(o.Lots), 7)
                    + Pad(Units.FormatPrice(o.Price, digits), 12) + Pad(o.StopLoss.HasValue ? Units.FormatPrice(o.StopLoss.Value, digits) : "-", 12)
                    + (o.TakeProfit.HasValue ? Units.FormatPrice(o.TakeProfit.Value, digits) : "-") + "\n");
            _tabBody.text = sb.ToString();
        }

        static string History(SessionState s)
        {
            if (s.Closed.Count == 0) return "<color=#8594ad>No closed trades yet.</color>";
            var sb = new StringBuilder(Pad("SIDE", 6) + Pad("SIZE", 7) + Pad("ENTRY", 12) + Pad("EXIT", 12) + Pad("REASON", 13) + "NET\n");
            foreach (var p in s.Closed.AsEnumerable().Reverse().Take(8))
            {
                if (p.Exit == null) continue;
                int d = Instruments.DigitsFor(p.Symbol, 5);
                string color = p.Exit.Net.Cents >= 0 ? "#26c295" : "#ed5259";
                sb.Append(Pad(p.Direction == Direction.Buy ? "BUY" : "SELL", 6) + Pad(Units.FormatLots(p.Lots), 7) + Pad(Units.FormatPrice(p.EntryPrice, d), 12)
                    + Pad(Units.FormatPrice(p.Exit.Price, d), 12) + Pad(Reason(p.Exit.Reason), 13) + "<color=" + color + ">" + p.Exit.Net.ToSignedString() + "</color>\n");
            }
            return sb.ToString();
        }

        static string Reason(CloseReason r)
        {
            return r == CloseReason.StopLoss ? "stop-loss" : r == CloseReason.TakeProfit ? "take-profit" : r == CloseReason.StopOut ? "stop-out" : "manual";
        }

        static string Stats(SessionState s)
        {
            var t = s.Stats;
            if (t.TotalTrades == 0) return "<color=#8594ad>Statistics appear after your first closed trade.</color>";
            Func<double?, string> pct = v => v.HasValue ? (v.Value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";
            Func<double?, string> num = v => v.HasValue ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
            return "Trades " + t.TotalTrades + "   Wins " + t.Wins + "   Losses " + t.Losses + "   Win rate " + pct(t.WinRate)
                + "\nNet " + t.NetPnl.ToSignedString() + "   Gross profit " + t.GrossProfit + "   Gross loss " + t.GrossLoss
                + "\nAvg win " + (t.AvgWin.HasValue ? t.AvgWin.Value.ToString() : "-") + "   Avg loss " + (t.AvgLoss.HasValue ? t.AvgLoss.Value.ToString() : "-")
                + "   Profit factor " + num(t.ProfitFactor) + "   Max drawdown " + t.MaxDrawdown + "   Avg R " + num(t.AvgRMultiple);
        }

        static string Pad(string s, int width) { return s.Length >= width ? s + " " : s.PadRight(width); }
    }
}
