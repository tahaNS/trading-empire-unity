using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// A recording aid: "-te-trailer folder" plays a scripted tour of the real game (building, trading, indicators, drawings, journal,
    /// loading data, photo mode) and writes every frame to the folder at a fixed 30 fps, so the frames can be made into a video.
    /// It drives the game through its normal services; it never touches your save.
    /// </summary>
    public sealed class TrailerDirector : MonoBehaviour
    {
        const int Fps = 30;
        GameApp _app;
        string _dir;
        int _frame;
        bool _recording;
        CanvasGroup _bar, _title, _fade;
        Text _head, _sub, _titleBig, _titleSmall;
        float _barTarget, _titleTarget, _fadeTarget = 1f;

        CityGame City { get { return _app.World.City; } }
        TradingSession Trading { get { return _app.World.Trading; } }

        public static void Begin(GameApp app, string dir)
        {
            Directory.CreateDirectory(dir);
            Time.captureFramerate = Fps;
            var go = new GameObject("TrailerDirector");
            var d = go.AddComponent<TrailerDirector>();
            d._app = app; d._dir = dir;
            d.BuildOverlay();
            d.StartCoroutine(d.CaptureLoop());
            d.StartCoroutine(d.Run());
        }

        // ---------------------------------------------------------------- overlay: captions, title, fades

        void BuildOverlay()
        {
            var cgo = new GameObject("TrailerOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5000;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0f;
            cgo.GetComponent<GraphicRaycaster>().enabled = false;
            var root = (RectTransform)cgo.transform;

            var bar = Ui.Box(root, "Bar", new Color(0.03f, 0.05f, 0.09f, 0.78f));
            Ui.Place(bar.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 118));
            var accent = Ui.Box(bar.transform, "Accent", new Color(0.18f, 0.49f, 0.88f));
            Ui.Place(accent.rectTransform, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(8, 0));
            _head = Ui.Label(bar.transform, "", 42, Color.white, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_head.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(46, 0), new Vector2(-40, -6));
            _sub = Ui.Label(bar.transform, "", 22, new Color(0.8f, 0.86f, 0.93f), TextAnchor.MiddleLeft);
            Ui.Place(_sub.rectTransform, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(48, 8), new Vector2(-40, 0));
            _bar = bar.gameObject.AddComponent<CanvasGroup>(); _bar.alpha = 0;

            var tc = Ui.Box(root, "Title", new Color(0.02f, 0.04f, 0.08f, 0.5f));
            Ui.Stretch(tc.rectTransform);
            _titleBig = Ui.Label(tc.transform, "", 112, Color.white, TextAnchor.MiddleCenter, false, true);
            Ui.Place(_titleBig.rectTransform, new Vector2(0, 0.45f), new Vector2(1, 0.7f), Vector2.zero, Vector2.zero);
            _titleSmall = Ui.Label(tc.transform, "", 34, new Color(0.86f, 0.91f, 0.97f), TextAnchor.MiddleCenter);
            Ui.Place(_titleSmall.rectTransform, new Vector2(0, 0.32f), new Vector2(1, 0.45f), Vector2.zero, Vector2.zero);
            _title = tc.gameObject.AddComponent<CanvasGroup>(); _title.alpha = 0;

            var fade = Ui.Box(root, "Fade", Color.black);
            Ui.Stretch(fade.rectTransform);
            _fade = fade.gameObject.AddComponent<CanvasGroup>(); _fade.alpha = 1;
        }

        void Update()
        {
            float k = 8f * Time.unscaledDeltaTime;
            _bar.alpha = Mathf.MoveTowards(_bar.alpha, _barTarget, k);
            _title.alpha = Mathf.MoveTowards(_title.alpha, _titleTarget, k * 0.6f);
            _fade.alpha = Mathf.MoveTowards(_fade.alpha, _fadeTarget, k * 0.5f);
        }

        void Caption(string head, string sub)
        {
            if (head == null) { _barTarget = 0; return; }
            _head.text = head; _sub.text = sub; _barTarget = 1;
        }

        // ---------------------------------------------------------------- recording

        IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (_recording)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "f" + _frame.ToString("00000") + ".jpg"));
                    _frame++;
                }
            }
        }

        IEnumerator Wait(float seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Type(Action<string> set, string text, float seconds)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            for (int i = 1; i <= frames; i++)
            {
                set(text.Substring(0, Mathf.Clamp(Mathf.RoundToInt(text.Length * i / (float)frames), 0, text.Length)));
                yield return null;
            }
        }

        IEnumerator Camera(Vector3 from, Vector3 to, float zoomFrom, float zoomTo, float seconds)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            for (int i = 0; i <= frames; i++)
            {
                float t = i / (float)frames, e = t * t * (3f - 2f * t);
                _app.CityView.SetCamera(Vector3.Lerp(from, to, e), Mathf.Lerp(zoomFrom, zoomTo, e));
                yield return null;
            }
        }

        // ---------------------------------------------------------------- the city for the show

        static readonly string[] Mix = { "apartment", "house", "small-shop", "apartment", "small-office", "restaurant", "house", "luxury-tower", "park", "hotel", "small-shop", "corporate-office", "plaza", "house" };

        void BuildShowcaseCity()
        {
            var w = _app.World;
            City.GrantFunds(Money.FromDollars(60_000_000));
            City.UnlockEverything();
            while (City.ExpandLand()) { }
            for (int y = 6; y < 27; y++)
                for (int x = 6; x < 27; x++)
                {
                    int h = (x * 73856093) ^ (y * 19349663);
                    string id = Mix[(h & 0x7fffffff) % Mix.Length];
                    City.StartPlacement(id);
                    if (!City.TryPlace(new Cell(x, y))) { City.StartPlacement("house"); City.TryPlace(new Cell(x, y)); }
                    City.CancelPlacement();
                }
            w.Clock.SkipDays(40);
            City.SetPolicy("marketing", true);
            City.SetPolicy("insurance", true);
            City.SetTradingRecord(new TradingRecord { Trades = 14, RecentTrades = 10, RecentWins = 7, RecentNet = Money.FromDollars(420), NetPnl = Money.FromDollars(900) });
            w.Clock.SkipDays(2);
            City.Select(null);
        }

        /// <summary>The first free cell (scanning from the middle outwards) where a building of this kind fits.</summary>
        Cell FreeSpot(string defId)
        {
            for (int r = 0; r < 20; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var c = new Cell(16 + dx, 16 + dy);
                        City.StartPlacement(defId);
                        City.SetHover(c);
                        bool ok = City.State.Hover != null && City.State.Hover.Check.Ok;
                        City.CancelPlacement();
                        if (ok) return c;
                    }
            return new Cell(16, 16);
        }

        /// <summary>The first index (past what is shown) where a long with a 20 pip stop reaches a 40 pip target soon after.</summary>
        int FindGoodEntry(int from)
        {
            var info = Trading.State.Dataset;
            var candles = SyntheticData.Generate(info.Seed ?? 0, Trading.State.Total, info.TimeframeMs > 0 ? info.TimeframeMs : 60000);
            long pip = Trading.Instrument.PipPoints;
            for (int i = from; i < candles.Count - 120; i++)
            {
                long entry = candles[i].Close + 8; // roughly the ask
                bool won = false;
                for (int j = i + 1; j < i + 70 && j < candles.Count; j++)
                {
                    if (candles[j].Low <= entry - 20 * pip) break;
                    if (candles[j].High >= entry + 40 * pip) { won = true; break; }
                }
                if (!won) continue;
                // a pleasant picture: the previous hour dips, so the entry looks like a pullback
                long hi = long.MinValue; for (int k = i - 30; k < i; k++) hi = Math.Max(hi, candles[k].High);
                if (hi - candles[i].Close > 14 * pip) return i;
            }
            return from + 60;
        }

        // ---------------------------------------------------------------- the script

        IEnumerator Run()
        {
            yield return null; yield return null;
            _app.CityHud.gameObject.SetActive(false);
            BuildShowcaseCity();
            _app.MenuHud.Show(false);
            _app.CityHud.gameObject.SetActive(true);
            _app.CityHud.RefreshNow();
            _app.World.Clock.SetSpeed(1);
            _app.CityView.SetCamera(new Vector3(0, 0, 0), 15f);
            _app.CityHud.gameObject.SetActive(false);
            _titleTarget = 1; _titleBig.text = "TRADING EMPIRE"; _titleSmall.text = "Trade the market. Build a city.";
            _recording = true;
            _fadeTarget = 0;
            yield return StartCoroutine(Camera(new Vector3(0, 0, 0), new Vector3(0, 0, 0), 15f, 12f, 3.6f));
            _titleTarget = 0;
            _app.CityHud.gameObject.SetActive(true);
            yield return StartCoroutine(Camera(Vector3.zero, new Vector3(-2f, 0, 1f), 12f, 8.5f, 3.4f));

            // 1. build something: the ghost glides over the map, the site rises, a bounce, rent floats up
            Caption("A living city that pays rent", "Shops, apartments, offices, towers, parks and hotels. Build, upgrade, grow.");
            string def = "luxury-tower";
            var spot = FreeSpot(def);
            City.StartPlacement(def);
            for (int i = 0; i <= 60; i++)
            {
                City.SetHover(new Cell(spot.X + (int)Mathf.Round((1f - i / 60f) * 8), spot.Y + (int)Mathf.Round((1f - i / 60f) * -6)));
                yield return null;
            }
            City.SetHover(spot);
            yield return StartCoroutine(Wait(0.6f));
            City.TryPlace(spot);
            _app.World.Clock.SetSpeed(10);
            yield return StartCoroutine(Camera(new Vector3(-2f, 0, 1f), new Vector3(-0.5f, 0, 0.5f), 8.5f, 6.5f, 5.2f));
            City.Select(City.State.Buildings.Last().Id);
            yield return StartCoroutine(Wait(2.8f));
            City.Select(null);
            _app.World.Clock.SetSpeed(5);

            // 2. the terminal
            Caption(null, null);
            _fadeTarget = 1; yield return StartCoroutine(Wait(0.6f));
            _app.ShowTerminal(true);
            int revealed = Trading.State.RevealedCount;
            int goal = FindGoodEntry(revealed + 120);
            Trading.SetSpeed(30);
            for (int i = 0; i < goal - 90 - revealed; i++) Trading.Step();
            Trading.Pause();
            _fadeTarget = 0; yield return StartCoroutine(Wait(0.4f));
            Caption("A manual trading terminal", "Replay real market history candle by candle. No peeking at the future.");
            Trading.SetSpeed(10); Trading.Play();
            yield return StartCoroutine(Wait(5.2f));
            Trading.Pause();

            // 3. plan a trade
            Caption("Plan every trade on the chart", "Drag entry, stop-loss and take-profit. Size by risk, or by lots. Market, limit or stop.");
            yield return StartCoroutine(Wait(0.8f));
            Trading.StartDraft(Direction.Buy);
            yield return StartCoroutine(Wait(1.2f));
            var d0 = Trading.Draft;
            long pip = Trading.Instrument.PipPoints;
            long sl0 = d0.StopLoss, tp0 = d0.TakeProfit ?? 0;
            for (int i = 0; i <= 36; i++)
            {
                float e = i / 36f; e = e * e * (3f - 2f * e);
                Trading.UpdateDraft(d => { d.StopLoss = sl0 + (long)(6 * pip * e); d.TakeProfit = tp0 + (long)(-8 * pip * e); });
                yield return null;
            }
            yield return StartCoroutine(Wait(1.2f));
            Trading.SubmitDraft();
            Trading.SetSpeed(10); Trading.Play();
            yield return StartCoroutine(Wait(0.6f));

            // 4. indicators while the trade runs
            Caption("16 indicators", "Moving averages, Bollinger Bands, RSI, MACD, Stochastic, ADX and more.");
            _app.Terminal.Chart.ToggleIndicatorPanel();
            string[] inds = { "ema", "bb", "rsi", "macd" };
            foreach (var id in inds) { Trading.Indicators.Add(id); yield return StartCoroutine(Wait(0.9f)); }
            yield return StartCoroutine(Wait(0.8f));
            _app.Terminal.Chart.ToggleIndicatorPanel();
            Trading.SetSpeed(30);
            for (int i = 0; i < 200 && Trading.State.Positions.Count > 0; i++) yield return null;
            Trading.Pause();
            yield return StartCoroutine(Wait(0.8f));
            Trading.Indicators.Clear();

            // 5. drawing tools and the Fibonacci levels
            Caption("21 drawing tools", "Trend lines, channels, pitchforks, Gann fans, position boxes and Fibonacci tools.");
            var bars = Trading.ChartData().Candles;
            int n = bars.Count;
            var recent = bars.Skip(n - 70).ToList();
            long hi = recent.Max(b => b.High), lo = recent.Min(b => b.Low);
            var low = recent.First(b => b.Low == lo); var high = recent.Last(b => b.High == hi);
            var ds = Trading.Drawings;
            ds.Add(DrawingKinds.Trend(new Anchor(bars[n - 62].Time, bars[n - 62].Low), new Anchor(bars[n - 14].Time, bars[n - 14].Low + 6 * pip), "#f2c25b")); yield return StartCoroutine(Wait(0.8f));
            ds.Add(DrawingKinds.Make("rect", "#c792ea", ("a", new Anchor(bars[n - 40].Time, hi - 4 * pip)), ("b", new Anchor(bars[n - 26].Time, hi - 13 * pip)))); yield return StartCoroutine(Wait(0.8f));
            ds.Add(DrawingKinds.Make("pitchfork", "#6ea8ff", ("a", new Anchor(bars[n - 66].Time, lo)), ("b", new Anchor(bars[n - 46].Time, hi)), ("c", new Anchor(bars[n - 30].Time, lo + (hi - lo) / 3)))); yield return StartCoroutine(Wait(0.9f));
            string fib = ds.Add(DrawingKinds.Make("fib", "#2fcfae", ("a", new Anchor(low.Time, lo)), ("b", new Anchor(high.Time, hi))));
            yield return StartCoroutine(Wait(0.9f));
            _app.Terminal.Chart.Select(fib);
            Caption("Edit every Fibonacci level", "Change a number, add a level, delete one, colour it, shade between levels.");
            yield return StartCoroutine(Wait(1.4f));
            ds.Update(fib, x => { var lv = DrawingKinds.FibLevelDefs(x); lv[4].V = 0.705; DrawingKinds.SetFibLevels(x, lv); });
            yield return StartCoroutine(Wait(0.9f));
            ds.Update(fib, x => { var lv = DrawingKinds.FibLevelDefs(x); lv.Add(new DrawingKinds.FibLevel { V = 1.272, Color = "#f2686b" }); DrawingKinds.SetFibLevels(x, lv); });
            yield return StartCoroutine(Wait(1.1f));
            ds.Update(fib, x => { var lv = DrawingKinds.FibLevelDefs(x); lv.RemoveAt(1); DrawingKinds.SetFibLevels(x, lv); });
            yield return StartCoroutine(Wait(1.6f));
            _app.Terminal.Chart.Select(null);
            ds.Clear();

            // 6. the journal
            Caption("Keep a trade journal", "Notes, tags, a star rating, and a small chart of every trade with entry and exit.");
            _app.Terminal.SelectTab(3);
            yield return StartCoroutine(Wait(1.4f));
            var closed = Trading.State.Closed.Where(p => p.Exit != null).ToList();
            if (closed.Count > 0)
            {
                string key = JournalStore.KeyOf(closed.Last());
                yield return StartCoroutine(Type(t => Trading.Journal.Set(key, t), "Waited for the pullback, stop under the swing low, took the target.", 2.2f));
                Trading.Journal.ToggleTag(key, "Good setup"); yield return StartCoroutine(Wait(0.45f));
                Trading.Journal.ToggleTag(key, "Followed plan"); yield return StartCoroutine(Wait(0.45f));
                Trading.Journal.ToggleTag(key, "Patient"); yield return StartCoroutine(Wait(0.5f));
                Trading.Journal.Set(key, null, null, 5);
                yield return StartCoroutine(Wait(1.6f));
            }
            _app.Terminal.SelectTab(4);
            Caption("Know your numbers", "Win rate, profit factor, average R, drawdown and a table of every trade.");
            yield return StartCoroutine(Wait(2.2f));
            _app.Terminal.SelectTab(0);

            // 7. bring your own data
            Caption("Bring your own market data", "Load MetaTrader 4 and 5 history, or a plain CSV, for forex pairs and gold.");
            _app.Terminal.ShowLoadPopover(true);
            yield return StartCoroutine(Wait(0.8f));
            yield return StartCoroutine(Type(t => _app.Terminal.LoadPathInput.text = t, "/Users/you/Downloads/EURUSD_M1_2024.csv", 2.4f));
            yield return StartCoroutine(Wait(1.6f));
            _app.Terminal.ShowLoadPopover(false);
            _app.Terminal.Guide.Show(true);
            _app.Terminal.Guide.SelectTab(2);
            Caption("A guide built in", "How to export from MetaTrader, the drawing tools, and every shortcut.");
            yield return StartCoroutine(Wait(2.8f));
            _app.Terminal.Guide.Show(false);

            // 8. profit builds the city
            Caption(null, null);
            _fadeTarget = 1; yield return StartCoroutine(Wait(0.6f));
            _app.ShowTerminal(false);
            City.GrantFunds(Money.FromDollars(1_200_000));
            var el = _app.World.Transfers.EligibleAmount();
            if (el > Money.Zero) _app.World.Transfers.Transfer(Guid.NewGuid().ToString(), el);
            _app.CityView.SetCamera(new Vector3(-0.5f, 0, 0.5f), 6.5f);
            _fadeTarget = 0;
            _app.CityHud.transform.Find("TransferPanel").gameObject.SetActive(true);
            _app.CityHud.RefreshNow();
            Caption("Profit builds the city", "Move realised profit from your trading account into the city treasury.");
            yield return StartCoroutine(Wait(3.2f));
            _app.CityHud.transform.Find("TransferPanel").gameObject.SetActive(false);
            Caption("Your trading shapes the city", "Hot streaks start a boom, cold ones a panic. Fires, storms and festivals strike.");
            _app.World.Clock.SetSpeed(10);
            yield return StartCoroutine(Camera(new Vector3(-0.5f, 0, 0.5f), new Vector3(2.5f, 0, -1.5f), 6.5f, 10f, 5.4f));

            // 9. photo mode fly-over, then the end card
            Caption("Photo mode and a fly-over", "Hide the interface, save a screenshot, or glide over your city.");
            _app.SetPhoto(true);
            _app.PhotoBar.gameObject.SetActive(false);
            _app.CityView.StartTour();
            yield return StartCoroutine(Wait(7.5f));
            Caption(null, null);
            _fadeTarget = 1; yield return StartCoroutine(Wait(1.2f));
            _titleBig.text = "TRADING EMPIRE"; _titleSmall.text = "Made with Unity   github.com/tahaNS/trading-empire-unity";
            _titleTarget = 1; _fadeTarget = 0.35f;
            yield return StartCoroutine(Wait(4.5f));
            _fadeTarget = 1; yield return StartCoroutine(Wait(1.2f));
            _recording = false;
            Debug.Log("Trailer frames written: " + _frame);
            Application.Quit();
        }
    }
}
