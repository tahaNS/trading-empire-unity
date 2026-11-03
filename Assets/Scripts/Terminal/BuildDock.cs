using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The bottom construction dock, as in the web game: district tabs, a card for every building (cost, size, build time, what it
    /// would earn, or why it is locked), the placement hint, the empty-treasury tip, and the 1-9 hotkeys. It only calls the city game.
    /// </summary>
    public sealed class BuildDock : MonoBehaviour
    {
        CityGame _city;
        RectTransform _root, _tabsBar, _gridCard, _toggleCard, _hintCard, _bannerCard;
        Text _hintText, _bannerText;
        Button _toggle;
        readonly List<Button> _tabButtons = new List<Button>();
        readonly Dictionary<string, Button> _cards = new Dictionary<string, Button>();
        readonly Dictionary<string, Text> _cardText = new Dictionary<string, Text>();
        readonly List<GameObject> _gridObjects = new List<GameObject>();
        readonly Dictionary<string, Outline> _cardBg = new Dictionary<string, Outline>();
        string _gridKey;
        DistrictId? _filter;
        bool _open = true;
        bool _dirty = true;
        float _lastWidth;
        int _lastVisible = -1;

        const int Columns = 6;
        const float CardH = 64f, Gap = 6f, Margin = 12f;

        /// <summary>Distance from the bottom of the screen to the top of the dock, hints included (the toast sits above it).</summary>
        public float TopY { get; private set; } = 100f;

        static readonly Color[] DistrictColors =
        {
            Hex("#6fb05a"), Hex("#e0a23a"), Hex("#6c84b0"), Hex("#c870a8"), Hex("#4fb59a"), Hex("#3aa6b8"),
        };

        static Color Hex(string s) { Color c; ColorUtility.TryParseHtmlString(s, out c); return c; }
        static Color DistrictColor(DistrictId d) { return DistrictColors[(int)d]; }
        static string HexOf(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        public static BuildDock Build(RectTransform parent, CityGame city)
        {
            var root = Ui.Rect(parent, "BuildDock");
            Ui.Stretch(root);
            var dock = root.gameObject.AddComponent<BuildDock>();
            dock._city = city;
            dock._root = root;
            dock.BuildUi();
            city.Changed += dock.MarkDirty;
            return dock;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (_city != null) _city.Changed -= MarkDirty; }

        public void RefreshNow() { _dirty = false; Refresh(); }

        void BuildUi()
        {
            // the hints (placement, empty treasury) float above the dock, centred
            _bannerCard = Ui.Card(_root, "Banner", 10f).rectTransform;
            _bannerText = Ui.Label(_bannerCard, "", 12, Theme.Ink, TextAnchor.MiddleCenter);
            _bannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Stretch(_bannerText.rectTransform, 12, 4, 12, 4);
            _hintCard = Ui.Card(_root, "PlacementHint", 10f).rectTransform;
            _hintText = Ui.Label(_hintCard, "", 12, Theme.Ink, TextAnchor.MiddleCenter);
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Stretch(_hintText.rectTransform, 12, 4, 12, 4);

            _tabsBar = Ui.Card(_root, "Tabs", 15f).rectTransform;
            var names = new List<string> { "All" };
            foreach (DistrictId d in Enum.GetValues(typeof(DistrictId))) names.Add(d.ToString());
            float x = 8;
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                float w = 22 + names[i].Length * 6.4f + (i == 0 ? 0 : 12);
                var b = Ui.Button(_tabsBar, names[i], () => { _filter = index == 0 ? (DistrictId?)null : (DistrictId)(index - 1); _dirty = true; }, Color.clear, 11, Theme.Ink);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 4), new Vector2(x + w, -4));
                Ui.Rounded((Image)b.targetGraphic, 11f);
                if (i > 0)
                {
                    var dot = Ui.Box(b.transform, "Dot", DistrictColor((DistrictId)(i - 1)));
                    Ui.Rounded(dot, 4f);
                    Ui.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(7, -4), new Vector2(15, 4));
                    var label = b.transform.Find("Label");
                    ((RectTransform)label).offsetMin = new Vector2(12, 0);
                }
                _tabButtons.Add(b);
                x += w + 4;
            }
            _tabsBar.sizeDelta = new Vector2(x + 4, 34);

            _toggleCard = Ui.Card(_root, "Toggle", 15f).rectTransform;
            _toggle = Ui.Button(_toggleCard, "Hide buildings", () => { _open = !_open; _dirty = true; }, Color.clear, 11, Theme.Muted);
            Ui.Stretch((RectTransform)_toggle.transform, 4, 4, 4, 4);

            _gridCard = Ui.Card(_root, "Grid", 14f).rectTransform;
            Ui.Place(_gridCard, Vector2.zero, new Vector2(1, 0), new Vector2(Margin, Margin), new Vector2(-Margin, Margin + 80));
        }

        // ---------------------------------------------------------------- refresh

        void Update()
        {
            float w = ((RectTransform)_root).rect.width;
            if (!Mathf.Approximately(w, _lastWidth)) { _lastWidth = w; _dirty = true; }
            HandleHotkeys();
            if (_dirty) { _dirty = false; Refresh(); }
        }

        void HandleHotkeys()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && sel.GetComponent<InputField>() != null) return;
            for (int i = 0; i < 9 && i < Buildings.All.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) _city.StartPlacement(Buildings.All[i].Id);
        }

        IEnumerable<BuildingDefinition> Visible()
        {
            foreach (var d in Buildings.All) if (_filter == null || d.District == _filter.Value) yield return d;
        }

        static string Plain(Money m) { return m.ToString(); }

        string NetText(BuildingDefinition d, bool locked, bool shortOfMoney, Money balance)
        {
            var l0 = d.Levels[0];
            if (shortOfMoney) return "<color=#b3261e>need " + Plain(d.Cost - (balance > Money.Zero ? balance : Money.Zero)) + " more</color>";
            if (l0.NeighborBonus != null) return "<color=#167a43>+" + l0.NeighborBonus.Pct + "% rent nearby</color>";
            var net = l0.Income - l0.Expense;
            return "<color=#167a43>" + net.ToSignedString() + " / " + d.IncomeIntervalDays + "d</color>";
        }

        void Refresh()
        {
            var s = _city.State;
            // tabs
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool on = (i == 0 && _filter == null) || (i > 0 && _filter.HasValue && (int)_filter.Value == i - 1);
                Ui.SetButtonColor(_tabButtons[i], on ? Theme.Primary : Color.clear);
                _tabButtons[i].transform.Find("Label").GetComponent<Text>().color = on ? Color.white : Theme.Ink;
            }
            Ui.SetButtonLabel(_toggle, _open ? "v  Hide buildings" : "^  Buildings" + (s.Placement != null ? " (placing)" : ""));

            // the grid of cards: built again only when the filter, the open state or the width change, so a click is never lost to a refresh
            var list = Visible().ToList();
            string key = (_filter.HasValue ? _filter.Value.ToString() : "all") + "|" + _open + "|" + Mathf.RoundToInt(_lastWidth);
            float gridH = 0;
            if (key != _gridKey)
            {
                _gridKey = key;
                foreach (var g in _gridObjects) Ui.Kill(g);
                _gridObjects.Clear(); _cards.Clear(); _cardText.Clear(); _cardBg.Clear();
                if (_open)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var def = list[i];
                        int row = i / Columns, col = i % Columns;
                        string id = def.Id;
                        var b = Ui.Button(_gridCard, "", () => { if (_city.State.Placement != null && _city.State.Placement.DefId == id) _city.CancelPlacement(); else _city.StartPlacement(id); }, Color.white, 11, Theme.Ink);
                        Ui.Rounded((Image)b.targetGraphic, 8f);
                        var ol = b.gameObject.AddComponent<Outline>();
                        ol.effectDistance = new Vector2(1.2f, 1.2f);
                        float w0 = col / (float)Columns, w1 = (col + 1) / (float)Columns;
                        float top = -(row * (CardH + Gap) + Gap);
                        Ui.Place((RectTransform)b.transform, new Vector2(w0, 1), new Vector2(w1, 1), new Vector2(Gap * 0.5f + (col == 0 ? 2 : 0), top - CardH), new Vector2(-Gap * 0.5f - (col == Columns - 1 ? 2 : 0), top));
                        var stripe = Ui.Box(b.transform, "Stripe", DistrictColor(def.District));
                        Ui.Place(stripe.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(0, 6), new Vector2(4, -6));
                        var label = b.transform.Find("Label").GetComponent<Text>();
                        label.alignment = TextAnchor.UpperLeft;
                        label.fontStyle = FontStyle.Normal;
                        label.horizontalOverflow = HorizontalWrapMode.Wrap;
                        label.fontSize = 11;
                        ((RectTransform)label.transform).offsetMin = new Vector2(12, 4);
                        ((RectTransform)label.transform).offsetMax = new Vector2(-6, -6);
                        _cards[def.Id] = b; _cardText[def.Id] = label; _cardBg[def.Id] = ol;
                        _gridObjects.Add(b.gameObject);
                    }
                }
            }
            _gridCard.gameObject.SetActive(_open);
            if (_open)
            {
                int rows = Mathf.Max(1, (list.Count + Columns - 1) / Columns);
                gridH = rows * (CardH + Gap) + Gap + 2;
                _gridCard.offsetMax = new Vector2(-Margin, Margin + gridH);
                foreach (var def in list)
                {
                    Button b; if (!_cards.TryGetValue(def.Id, out b)) continue;
                    bool locked = !_city.IsUnlocked(def.UnlockId);
                    bool shortOf = !locked && s.Balance < def.Cost;
                    bool active = s.Placement != null && s.Placement.DefId == def.Id;
                    int number = Buildings.All.ToList().IndexOf(def) + 1;
                    var mission = def.UnlockId != null ? Missions.Unlocking(def.UnlockId) : null;
                    Ui.SetButtonColor(b, locked ? new Color(0.93f, 0.945f, 0.955f) : active ? Theme.Tint : Color.white);
                    _cardBg[def.Id].effectColor = active ? Theme.Primary : Theme.Line;
                    string name = (number <= 9 ? "<color=#5c6b75>" + number + "</color> " : "") + "<b>" + def.Name + "</b>";
                    string meta = locked
                        ? "<color=#5c6b75>locked: " + (mission != null ? mission.Title : "a mission") + "</color>"
                        : "<color=#5c6b75>" + Plain(def.Cost) + " · " + def.Footprint.W + "×" + def.Footprint.H + " · " + def.ConstructionDays + "d build</color>";
                    var label = _cardText[def.Id];
                    label.text = name + "\n" + meta + (locked ? "" : "\n" + NetText(def, locked, shortOf, s.Balance));
                    label.color = locked ? Theme.Muted : Theme.Ink;
                }
            }

            // stack the pieces above the grid
            float y = Margin + (_open ? gridH + 6 : 0);
            Place(_tabsBar, 12, y, 0, 34, false);
            float tabsRight = _tabsBar.sizeDelta.x + 12 + 8;
            _toggleCard.anchorMin = _toggleCard.anchorMax = new Vector2(0, 0);
            _toggleCard.pivot = new Vector2(0, 0);
            _toggleCard.sizeDelta = new Vector2(150, 34);
            _toggleCard.anchoredPosition = new Vector2(tabsRight, y);
            y += 34 + 6;

            // placement hint
            bool placing = s.Placement != null;
            _hintCard.gameObject.SetActive(placing);
            if (placing)
            {
                _hintText.text = HintText(s);
                float hw = Mathf.Min(900, _lastWidth - 40);
                _hintCard.anchorMin = _hintCard.anchorMax = new Vector2(0.5f, 0);
                _hintCard.pivot = new Vector2(0.5f, 0);
                _hintCard.sizeDelta = new Vector2(hw, 32);
                _hintCard.anchoredPosition = new Vector2(0, y);
                y += 32 + 6;
            }
            bool tip = s.Balance <= Money.Zero && s.Buildings.Count == 0;
            _bannerCard.gameObject.SetActive(tip);
            if (tip)
            {
                _bannerText.text = "The treasury is empty. Open the Terminal, trade, then use <b>Finances</b> to move money to the city.";
                _bannerCard.anchorMin = _bannerCard.anchorMax = new Vector2(0.5f, 0);
                _bannerCard.pivot = new Vector2(0.5f, 0);
                _bannerCard.sizeDelta = new Vector2(Mathf.Min(620, _lastWidth - 40), 32);
                _bannerCard.anchoredPosition = new Vector2(0, y);
                y += 32 + 6;
            }
            TopY = y;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h, bool stretchWidth)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w > 0 ? w : rt.sizeDelta.x, h);
        }

        string HintText(CityState s)
        {
            var def = Buildings.Get(s.Placement.DefId);
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(def.Name).Append(" · ").Append(Plain(def.Cost)).Append(" · ").Append(def.ConstructionDays).Append(" days to build</b>");
            var bonus = def.Levels[0].NeighborBonus;
            if (bonus != null && s.Hover != null)
            {
                var fp = CityGrid.Rotated(def.Footprint, s.Placement.Rotation);
                var rect = new RectI(s.Hover.Origin.X, s.Hover.Origin.Y, fp.W, fp.H);
                int n = s.Buildings.Count(b => Buildings.Get(b.DefId).Category != BuildingCategory.Recreation && CityEconomy.RectGap(rect, CityEconomy.BuildingRect(b)) <= bonus.Radius);
                sb.Append("   <color=").Append(n > 0 ? "#167a43" : "#5c6b75").Append(">+").Append(bonus.Pct).Append("% rent for ").Append(n).Append(n == 1 ? " building" : " buildings").Append(" in range</color>");
            }
            if (s.Hover != null)
            {
                var chk = s.Hover.Check;
                sb.Append("   <color=").Append(chk.Ok ? "#167a43" : "#b3261e").Append(">").Append(CityGame.Describe(chk)).Append("</color>");
            }
            sb.Append("   <color=#5c6b75>Click place · R rotate · Esc or right-click cancel</color>");
            return sb.ToString();
        }
    }
}
