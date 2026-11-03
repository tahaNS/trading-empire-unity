using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>The terminal guide: how to open a position, the drawing tools, loading MT4 / MT5 history, and the shortcuts.</summary>
    public sealed class TerminalGuide : MonoBehaviour
    {
        static readonly string[] Tabs = { "Open a position", "Drawing tools", "Load MT4 / MT5 data", "Shortcuts" };
        RectTransform _window;
        Text _body;
        readonly List<Button> _tabButtons = new List<Button>();
        Action _startTutorial;
        int _tab;

        public bool Open { get { return gameObject.activeSelf; } }

        public static TerminalGuide Build(Transform parent, Action startTutorial)
        {
            var root = Ui.Rect(parent, "Guide");
            Ui.Stretch(root);
            var g = root.gameObject.AddComponent<TerminalGuide>();
            g._startTutorial = startTutorial;
            g.BuildUi(root);
            root.gameObject.SetActive(false);
            return g;
        }

        void BuildUi(RectTransform root)
        {
            var dim = Ui.Box(root, "Dim", new Color(0, 0, 0, 0.6f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var win = Ui.Panel(root, "Window", new Color(0.09f, 0.12f, 0.17f, 1f));
            Ui.Rounded(win, 12f);
            _window = win.rectTransform;
            Ui.Place(_window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-360, -310), new Vector2(360, 310));
            var title = Ui.Label(_window, "TERMINAL GUIDE", 14, Theme.Accent, TextAnchor.MiddleLeft, false, true);
            Ui.Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(18, -36), new Vector2(-100, -8));
            var close = Ui.Button(_window, "Close", () => Show(false), Theme.PanelAlt, 12);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, -34), new Vector2(-12, -10));
            float x = 14;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                float w = i == 2 ? 170 : 150;
                var b = Ui.Button(_window, Tabs[i], () => { _tab = index; Refresh(); }, Theme.PanelAlt, 12);
                Ui.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -72), new Vector2(x + w, -44));
                _tabButtons.Add(b);
                x += w + 6;
            }
            var guide = Ui.Button(_window, "Start the guided first trade", () => { Show(false); if (_startTutorial != null) _startTutorial(); }, new Color(0.1f, 0.5f, 0.4f), 12);
            Ui.Place((RectTransform)guide.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(14, 10), new Vector2(264, 38));
            _body = Ui.Label(_window, "", 12, Theme.Text, TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 48), new Vector2(-20, -86));
        }

        public void Show(bool show) { gameObject.SetActive(show); if (show) Refresh(); }
        public void SelectTab(int tab) { _tab = tab; Refresh(); }

        void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Show(false); }

        void Refresh()
        {
            for (int i = 0; i < _tabButtons.Count; i++) Ui.SetButtonColor(_tabButtons[i], i == _tab ? Theme.Blue : Theme.PanelAlt);
            _body.text = _tab == 0 ? Trade : _tab == 1 ? Draw : _tab == 2 ? Data : Keys;
        }

        const string Trade =
            "<b>1  Move the market.</b>  Press Space to play, or Right arrow for one candle at a time. Time only moves when you do this, so you can stop and think at any moment.\n\n"
            + "<b>2  Choose a direction.</b>  In the Order panel click LONG (buy, key L) or SHORT (sell, key S). Three lines appear on the chart: the entry, a red stop-loss and a green take-profit.\n\n"
            + "<b>3  Place the lines.</b>  Drag the red line to where your idea is wrong and the green line to your target (or type pips in the panel). The panel shows the reward : risk ratio as you go.\n\n"
            + "<b>4  Market or pending.</b>  Leave the entry on the price for a market order. Drag the entry line away from the price to place a limit or stop order that waits until the market reaches it.\n\n"
            + "<b>5  Set the size.</b>  Risk % (default 1% of your balance: the lot size is worked out from your stop-loss) or Fixed lots. Check Risk at SL, Reward, margin and R:R in the panel.\n\n"
            + "<b>6  Place it.</b>  Click Place order or press Enter. Cancel drops the plan.\n\n"
            + "<b>7  Manage it.</b>  The trade is listed under Open positions below the chart. It closes by itself when a candle reaches the stop-loss or the take-profit, or you can close it by hand.\n\n"
            + "<color=#8594ad>Good to know: if one candle touches both lines, the stop-loss counts first. Only closed trades count as profit you can move to the city.</color>";

        const string Draw =
            "Pick a tool from the rail on the left of the chart, then click on the chart (some tools take a drag, others several clicks; the hint at the top says which). Click a drawing with the cursor tool (Sel) to select it: drag it, drag its handles, then use the bar that appears to change colour, width, dashes, lock, hide or delete it.\n\n"
            + "<b>Lines</b>  H horizontal, V vertical, Ray, Tr trend line.\n"
            + "<b>Channels</b>  Ch parallel channel (two points, then the width), Rg regression channel, Pf Andrews' pitchfork, Gn Gann fan.\n"
            + "<b>Fibonacci</b>  Fib retracement, FX trend-based extension, FC channel, FT time zones, FF speed fan, FA arcs.\n"
            + "<b>Shapes</b>  Rc rectangle, El ellipse.\n"
            + "<b>Notes</b>  Txt note (type its text in the field that appears), $ price label, Up / Dn arrows.\n"
            + "<b>Trade planning</b>  Lg / Sh position boxes: entry, stop and target with money at risk and R:R; \"Use in the order panel\" turns one into a trade plan.\n"
            + "<b>Clr</b> removes every drawing of this chart.\n\n"
            + "Indicators (top right of the chart) add moving averages, Bollinger bands, RSI, MACD and more. Drawings and indicators are saved with the game.";

        const string Data =
            "The terminal replays candles from a file (EUR/USD, GBP/USD, USD/JPY, AUD/USD, NZD/USD, USD/CHF, USD/CAD or gold). Export the history from MetaTrader, then load it with Load data...\n\n"
            + "<b>MetaTrader 5</b>\n  View > Symbols (Ctrl+U), Bars tab. Pick the symbol (e.g. EURUSD), a timeframe and a date range, click Request, then Export Bars and save the .csv. It is tab separated with a header row; the terminal reads it as is, including the spread column.\n\n"
            + "<b>MetaTrader 4</b>\n  Tools > History Center (F2). Open the symbol and a timeframe (Download first if it is empty or short), then Export. MT4 files have no header row and no spread, so the terminal uses a fixed spread for the symbol.\n\n"
            + "<b>Then here</b>\n  Click Load data..., set the server UTC offset if you want true UTC times (the hours your broker is ahead, e.g. 2), and give the full path of the file. Rows with impossible prices are skipped and listed, never guessed. Restart replays the same file.\n\n"
            + "<color=#8594ad>Which timeframe? The chart can show your file's timeframe and any whole multiple of it. Export M1 for the most freedom.\nWhich symbol? It is read from the file name (EURUSD_M1_....csv); if the name does not say, choose it in Load data. If the prices do not look like the chosen symbol the file is refused. Pairs without USD (such as EUR/GBP) are not supported. A plain CSV with timestamp,open,high,low,close also works.</color>";

        const string Keys =
            "<b>Space</b>  play / pause the replay\n<b>Right arrow</b>  next candle\n<b>Shift + Right</b>  next bar of the chart timeframe\n"
            + "<b>L / S</b>  plan a long / short trade\n<b>Enter</b>  place the planned order\n<b>Esc</b>  cancel the tool or selection, then back to the city\n"
            + "<b>V</b>  cursor     <b>H</b>  horizontal line     <b>T</b>  trend line     <b>R</b>  rectangle     <b>G</b>  Fibonacci     <b>X</b>  text note\n"
            + "<b>Delete</b>  remove the selected drawing\n<b>Mouse wheel</b>  zoom the chart";
    }
}
