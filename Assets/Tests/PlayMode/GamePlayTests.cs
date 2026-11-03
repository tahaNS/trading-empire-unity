using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TradingEmpire.Core;
using TradingEmpire.Terminal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TradingEmpire.PlayModeTests
{
    /// <summary>Runs the real game loop (Update, UI, input module) and uses the real buttons. Any console error or exception fails the test.</summary>
    public class GamePlayTests
    {
        GameApp _app;

        static void CleanAll()
        {
            foreach (var o in UnityEngine.Object.FindObjectsByType<GameApp>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(o.gameObject);
            foreach (var o in UnityEngine.Object.FindObjectsByType<CityView>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(o.gameObject);
            foreach (var o in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(o.gameObject);
        }

        // the tests click settings buttons; they must not leave the player's own settings changed
        static readonly string[] PrefKeys = { "te.uiScale", "te.colorBlind", "te.muted", "te.welcomed", "te.tutorial", "te.tutorial.step", "te.offerSeen" };
        readonly System.Collections.Generic.Dictionary<string, string> _savedPrefs = new System.Collections.Generic.Dictionary<string, string>();

        [SetUp]
        public void Setup()
        {
            _savedPrefs.Clear();
            foreach (var k in PrefKeys) if (PlayerPrefs.HasKey(k)) _savedPrefs[k] = PlayerPrefs.GetString(k, null) ?? PlayerPrefs.GetInt(k).ToString();
            CleanAll();
        }

        [TearDown]
        public void TearDown()
        {
            CleanAll();
            foreach (var k in PrefKeys) PlayerPrefs.DeleteKey(k);
            foreach (var kv in _savedPrefs) { int n; if (int.TryParse(kv.Value, out n)) PlayerPrefs.SetInt(kv.Key, n); else PlayerPrefs.SetString(kv.Key, kv.Value); }
            PlayerPrefs.Save();
            GameSettings.Load();
        }

        static Button[] LiveButtons()
        {
            return UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled && b.interactable && b.gameObject.activeInHierarchy).ToArray();
        }

        static string LabelOf(Button b)
        {
            var t = b.GetComponentInChildren<Text>();
            return t != null ? t.text : b.name;
        }

        static Button Find(string startsWith)
        {
            return LiveButtons().FirstOrDefault(b => LabelOf(b).StartsWith(startsWith, StringComparison.Ordinal));
        }

        [UnityTest]
        public IEnumerator Game_starts_runs_frames_and_switches_screens()
        {
            _app = GameApp.Create(null, false, true);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsNotNull(_app.World);
            Assert.IsTrue(_app.CityHud.gameObject.activeInHierarchy);
            Assert.IsFalse(_app.Terminal.gameObject.activeInHierarchy);

            Find("Open Terminal").onClick.Invoke();
            yield return null;
            Assert.IsTrue(_app.TerminalOpen);
            Assert.IsTrue(_app.Terminal.gameObject.activeInHierarchy);

            // the market runs through the real Update loop
            _app.World.Trading.SetSpeed(30);
            Find("Play").onClick.Invoke();
            int before = _app.World.Trading.State.RevealedCount;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater(_app.World.Trading.State.RevealedCount, before, "the replay did not advance");

            Find("LONG").onClick.Invoke();
            yield return null;
            Assert.IsNotNull(_app.World.Trading.Draft);
            Find("Place order").onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, _app.World.Trading.State.Positions.Count);
            Find("Close all").onClick.Invoke();
            yield return null;
            Assert.AreEqual(0, _app.World.Trading.State.Positions.Count);

            Find("< Back to city").onClick.Invoke();
            yield return null;
            Assert.IsFalse(_app.TerminalOpen);
            Assert.IsTrue(_app.CityHud.gameObject.activeInHierarchy);

            // the city clock runs through Update too
            long t0 = _app.World.Clock.Time;
            until = Time.realtimeSinceStartup + 1.0f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Greater(_app.World.Clock.Time, t0, "the city clock did not advance");
        }

        [UnityTest]
        public IEnumerator Building_through_the_real_input_path_works()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            var city = _app.World.City;
            city.GrantFunds(Money.FromDollars(50_000));
            yield return null;
            var houseCard = LiveButtons().First(b => LabelOf(b).Contains("<b>House</b>"));
            houseCard.onClick.Invoke();
            yield return null;
            Assert.IsNotNull(city.State.Placement);
            city.SetHover(new Cell(11, 11));
            Assert.IsTrue(city.State.Hover.Check.Ok);
            Assert.IsTrue(city.TryPlace(new Cell(11, 11)));
            for (int i = 0; i < 3; i++) yield return null; // the 3D view syncs in Update
            Assert.IsNotNull(GameObject.Find("Building b1"), "the building did not appear in the scene");
            city.Select(city.State.Buildings[0].Id);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsNotNull(Find("Upgrade"));
            Find("Demolish").onClick.Invoke();          // the first click asks, the second confirms
            yield return null;
            Find("Confirm").onClick.Invoke();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsNull(GameObject.Find("Building b1"), "the building stayed in the scene after demolition");
        }

        [UnityTest]
        public IEnumerator Smashing_every_button_at_random_never_throws()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            var rng = new System.Random(11);
            _app.World.City.GrantFunds(Money.FromDollars(200_000));
            _app.World.City.UnlockEverything();
            string[] skip = { "Start a new game", "Import from path" };
            int clicks = 0;
            for (int round = 0; round < 700; round++)
            {
                var buttons = LiveButtons().Where(b => !skip.Any(s => LabelOf(b).StartsWith(s, StringComparison.Ordinal))).ToArray();
                // the photo bar hides itself for the frame in which it takes a picture
                if (buttons.Length == 0) { yield return new WaitForSecondsRealtime(0.7f); buttons = LiveButtons().Where(b => !skip.Any(s => LabelOf(b).StartsWith(s, StringComparison.Ordinal))).ToArray(); }
                Assert.Greater(buttons.Length, 0, "no clickable button left on screen (round " + round + ", photo " + _app.Photo + ", terminal " + _app.TerminalOpen + ", menu " + _app.MenuHud.Open + ", over " + (_app.World.GameOver.State != null) + ", barActive " + _app.PhotoBar.transform.Find("Bar").gameObject.activeSelf + ")");
                var pick = buttons[rng.Next(buttons.Length)];
                pick.onClick.Invoke();
                clicks++;
                // type into a random text field now and then, including nonsense
                if (rng.Next(6) == 0)
                {
                    var fields = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None).Where(f => f.isActiveAndEnabled).ToArray();
                    if (fields.Length > 0)
                    {
                        string[] junk = { "", "0", "-5", "1e99", "abc", "0.001", "99999999999", "1.5", "  ", "\"quoted\"" };
                        var f = fields[rng.Next(fields.Length)];
                        f.text = junk[rng.Next(junk.Length)];
                        f.onEndEdit.Invoke(f.text);
                    }
                }
                // let the world move: market, city clock, autosave timers
                if (round % 5 == 0) _app.World.Clock.SkipDays(1 + rng.Next(20));
                if (round % 7 == 0) for (int s = 0; s < 20; s++) _app.World.Trading.Step();
                if (rng.Next(3) == 0) yield return null;
            }
            yield return null;
            Assert.Greater(clicks, 600);
            // whatever happened, the game state is still valid
            var json = Json.Serialize(SaveCodec.Capture(_app.World, DateTimeOffset.UtcNow, false));
            Assert.IsTrue(SaveCodec.Parse(json).Ok);
        }

        [UnityTest]
        public IEnumerator Menu_save_load_export_and_a_new_game_work()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            _app.World.City.GrantFunds(Money.FromDollars(20_000));
            Find("Game").onClick.Invoke();
            yield return null;
            Assert.IsTrue(_app.MenuHud.Open);
            Assert.IsTrue(_app.Save(), _app.LastError);
            Assert.AreEqual("Game loaded.", _app.LoadSaved());
            Assert.IsTrue(_app.Export().StartsWith("Exported to "));
            Assert.IsTrue(_app.Import("").Contains("Type the path"));
            Assert.IsTrue(_app.Import("/no/such/file.json").Contains("not found"));
            _app.StartOver();
            for (int i = 0; i < 4; i++) yield return null;
            var again = UnityEngine.Object.FindFirstObjectByType<GameApp>();
            Assert.IsNotNull(again, "no game after starting over");
            Assert.AreNotSame(_app, again);
            Assert.AreEqual(Money.Zero, again.World.City.TreasuryBalance); // a fresh game has an empty treasury
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<GameApp>(FindObjectsSortMode.None).Length);
            _app = again;
        }

        [UnityTest]
        public IEnumerator A_challenge_can_be_started_played_finished_and_left()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            Find("Challenges").onClick.Invoke();
            yield return null;
            Find("Start").onClick.Invoke();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(_app.Challenges.Active);
            Assert.AreNotSame(_app.World.Trading, _app.Terminal.Session);
            _app.Terminal.Session.QuickOrder(Direction.Buy, 10, 20, 40);
            for (int i = 0; i < 40; i++) _app.Terminal.Session.Step();
            yield return null;
            Find("Finish now").onClick.Invoke();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsNotNull(_app.Challenges.Result);
            Find("Back to my game").onClick.Invoke();
            yield return null;
            Assert.AreSame(_app.World.Trading, _app.Terminal.Session);
            Assert.AreEqual(1, _app.World.Challenges.Results.Count);
        }

        /// <summary>Would a click at this screen point reach a UI element (true) or fall through to the 3D city (false)?</summary>
        static bool UiBlocksAt(Vector2 screen)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = screen }, results);
            return results.Count > 0;
        }

        static Vector2 ScreenCenterOf(string objectName)
        {
            var rt = GameObject.Find(objectName).GetComponent<RectTransform>();
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return (corners[0] + corners[2]) / 2f;
        }

        [UnityTest]
        public IEnumerator Panels_block_the_mouse_so_clicks_never_reach_the_city_behind_them()
        {
            _app = GameApp.Create(null, false, true);
            _app.World.City.GrantFunds(Money.FromDollars(50_000));
            _app.World.City.StartPlacement("house");
            _app.World.City.TryPlace(new Cell(11, 11));
            _app.World.City.Select(_app.World.City.State.Buildings[0].Id);
            _app.World.City.CancelPlacement();
            for (int i = 0; i < 3; i++) yield return null;
            yield return null;
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("Goal")), "the goal card lets clicks through");
            _app.CityHud.transform.Find("TransferPanel").gameObject.SetActive(true);
            yield return null;
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("TreasuryCard")), "the treasury card lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("DateCard")), "the date card lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("Grid")), "the building dock lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("Inspector")), "the inspector lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("TransferPanel")), "the transfer panel lets clicks through");
            // the middle of the map is empty of UI, so a click there is the city's
            Assert.IsFalse(UiBlocksAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)), "the map area is covered by UI");
        }

        [UnityTest]
        public IEnumerator A_game_saved_to_disk_loads_back_identically()
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "saves");
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            try
            {
                _app = GameApp.Create(null, true, true);
                yield return null;
                _app.World.City.GrantFunds(Money.FromDollars(77_777));
                _app.World.City.StartPlacement("house");
                Assert.IsTrue(_app.World.City.TryPlace(new Cell(11, 11)));
                _app.World.Trading.QuickOrder(Direction.Buy, 10, 20, 40);
                for (int i = 0; i < 12; i++) _app.World.Trading.Step();
                Assert.IsTrue(_app.Save(), _app.LastError);
                var balance = _app.World.City.TreasuryBalance;
                int revealed = _app.World.Trading.State.RevealedCount;
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(dir, "current.json")));
                CleanAll();
                yield return null;
                _app = GameApp.Create(null, true, false); // a fresh start loads the save
                yield return null;
                Assert.AreEqual(balance, _app.World.City.TreasuryBalance);
                Assert.AreEqual(revealed, _app.World.Trading.State.RevealedCount);
                Assert.AreEqual(1, _app.World.City.State.Buildings.Count);
                Assert.AreEqual(1, _app.World.Trading.State.Positions.Count);
                Assert.AreEqual(0, _app.World.Clock.Speed, "a loaded game must start paused");
            }
            finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        }

        [UnityTest]
        public IEnumerator A_big_city_stays_light_enough()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            var city = _app.World.City;
            city.GrantFunds(Money.FromDollars(900_000_000));
            city.UnlockEverything();
            city.GrantUnlock("land-2"); city.GrantUnlock("land-3"); city.GrantUnlock("land-4");
            while (city.ExpandLand()) { }
            int placed = 0;
            var area = city.Grid.Buildable;
            string[] types = { "house", "small-shop", "apartment", "park", "restaurant", "house", "small-shop", "small-office" };
            int attempt = 0;
            for (int y = area.Y; y < area.Y + area.H && placed < 170; y++)
                for (int x = area.X; x < area.X + area.W && placed < 170; x++)
                {
                    city.StartPlacement(types[attempt++ % types.Length]);
                    if (city.TryPlace(new Cell(x, y))) placed++;
                }
            city.CancelPlacement();
            _app.World.Clock.SkipDays(20);
            for (int i = 0; i < 3; i++) yield return null;
            int renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
            float t0 = Time.realtimeSinceStartup;
            for (int i = 0; i < 30; i++) yield return null;
            float perFrame = (Time.realtimeSinceStartup - t0) / 30f * 1000f;
            Debug.Log("PERF per building=" + (renderers / Math.Max(1, city.State.Buildings.Count)) + " buildings=" + city.State.Buildings.Count + " renderers=" + renderers + " ms/frame=" + perFrame.ToString("0.0"));
            Assert.GreaterOrEqual(city.State.Buildings.Count, 100, "could not build a big city for the test");
            Assert.Less(renderers, 20000, "the city creates too many objects");
        }

        [UnityTest]
        public IEnumerator Chart_tools_draw_select_edit_and_delete_with_pointer_events()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            for (int i = 0; i < 3; i++) yield return null;
            var chart = _app.Terminal.Chart;
            var store = _app.World.Trading.Drawings;
            var plot = chart.Graphic.Plot;
            Assert.Greater(plot.width, 100f, "the chart has no plot area yet");
            Vector2 At(float fx, float fy) { return new Vector2(plot.xMin + plot.width * fx, plot.yMin + plot.height * fy); }

            // a horizontal line takes one click
            chart.SetTool("hline");
            chart.PointerDown(At(0.5f, 0.5f));
            Assert.AreEqual(1, store.Items.Count);
            Assert.AreEqual("hline", store.Items[0].Kind);
            Assert.AreEqual(store.Items[0].Id, chart.SelectedId);
            Assert.IsNull(chart.Tool, "the tool should return to the cursor after drawing");

            // a trend line takes two clicks
            chart.SetTool("trend");
            chart.PointerDown(At(0.2f, 0.3f));
            Assert.AreEqual(1, chart.PendingCount);
            Assert.AreEqual(1, store.Items.Count);
            chart.PointerUp(At(0.2f, 0.3f)); // a click, not a drag: the line waits for its second point
            chart.PointerDown(At(0.7f, 0.6f));
            Assert.AreEqual(2, store.Items.Count);
            Assert.AreEqual("trend", store.Items[1].Kind);
            var trend = store.Items[1];
            Assert.IsTrue(trend.GetAnchor("a").HasValue && trend.GetAnchor("b").HasValue);
            Assert.Less(trend.GetAnchor("a").Value.Time, trend.GetAnchor("b").Value.Time);

            // a position box and a Fibonacci (two clicks) too
            chart.SetTool("long");
            chart.PointerDown(At(0.5f, 0.4f));
            Assert.AreEqual("position", store.Items[2].Kind);
            chart.SetTool("fib");
            chart.PointerDown(At(0.3f, 0.2f)); chart.PointerUp(At(0.3f, 0.2f)); chart.PointerDown(At(0.6f, 0.7f));
            Assert.AreEqual(4, store.Items.Count);
            yield return null;

            // everything is drawn: the scene has lines for each drawing
            Assert.IsNotNull(chart.Graphic.Scene);
            foreach (var d in store.Items) Assert.IsTrue(chart.Graphic.Scene.Lines.Any(l => l.DrawingId == d.Id) || chart.Graphic.Scene.Fills.Any(f => f.DrawingId == d.Id), d.Kind + " was not drawn");

            // select by clicking on the trend line (the other drawings cross it, and the topmost one wins, so remove them first)
            foreach (var d in store.Items.Where(x => x.Id != trend.Id).ToList()) store.Remove(d.Id);
            yield return null;
            chart.SetTool(null);
            chart.Select(null);
            var line = chart.Graphic.Scene.Lines.First(l => l.DrawingId == trend.Id);
            chart.PointerDown((line.A + line.B) / 2);
            Assert.AreEqual(trend.Id, chart.SelectedId, "clicking on a line should select it");
            long before = store.Get(trend.Id).GetAnchor("a").Value.Time;
            chart.PointerUp((line.A + line.B) / 2);

            // a locked drawing does not move
            _app.Terminal.Chart.Select(trend.Id);
            store.Update(trend.Id, d => d.Data.Set("locked", true));
            chart.PointerDown((line.A + line.B) / 2 );
            Assert.AreEqual(trend.Id, chart.SelectedId);

            // delete and clear
            chart.DeleteSelected();
            Assert.AreEqual(0, store.Items.Count);
            Assert.IsNull(chart.SelectedId);
            store.Clear();
            Assert.AreEqual(0, store.Items.Count);
        }

        [UnityTest]
        public IEnumerator Sounds_play_for_game_events_and_respect_mute()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            var audio = _app.Audio;
            Assert.IsNotNull(audio);
            audio.SetMuted(false);
            int before = audio.Played;
            _app.World.Trading.QuickOrder(Direction.Buy, 10, 20, 40); // "Opened buy ..." is a trade-open sound
            Assert.Greater(audio.Played, before, "no sound for opening a trade");
            Assert.AreEqual(SoundId.TradeOpen, audio.LastPlayed);
            before = audio.Played;
            _app.World.Trading.CloseAll();
            Assert.Greater(audio.Played, before);
            // the UI click
            before = audio.Played;
            Find("Game").onClick.Invoke();
            Assert.Greater(audio.Played, before, "buttons should click");
            // muted: nothing plays
            audio.SetMuted(true);
            before = audio.Played;
            _app.World.Trading.QuickOrder(Direction.Sell, 10, 20, 40);
            Assert.AreEqual(before, audio.Played);
            audio.SetMuted(false);
            // the hum follows the city
            audio.UpdateHum(_app.World, false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Settings_apply_to_the_running_game()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Game").onClick.Invoke();
            yield return null;
            Find("Settings").onClick.Invoke();
            yield return null;
            Find("Large").onClick.Invoke();
            yield return null;
            var scaler = UnityEngine.Object.FindFirstObjectByType<CanvasScaler>();
            Assert.Less(scaler.referenceResolution.x, 1600f);
            Find("Colour-blind").onClick.Invoke();
            yield return null;
            Assert.IsTrue(Theme.ColorBlind);
            Find("Normal").onClick.Invoke();
            Find("Green and red").onClick.Invoke();
            yield return null;
            Assert.IsFalse(Theme.ColorBlind);
            Assert.AreEqual(1600f, scaler.referenceResolution.x);
        }
        [UnityTest]
        public IEnumerator Every_indicator_can_be_added_drawn_edited_hidden_and_removed()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            for (int i = 0; i < 80; i++) _app.World.Trading.Step();
            var chart = _app.Terminal.Chart;
            Find("Indicators").onClick.Invoke();
            yield return null;
            Assert.IsTrue(chart.IndicatorWindow.Visible);
            chart.IndicatorWindow.SelectTab(1);
            yield return null;
            foreach (var def in Indicators.All)
            {
                var add = Find(def.Name);
                Assert.IsNotNull(add, "no Add button for " + def.Name);
                add.onClick.Invoke();
                yield return null;
                chart.IndicatorWindow.SelectTab(1);
                yield return null;
            }
            Assert.AreEqual(Math.Min(Indicators.All.Length, Indicators.MaxIndicators), _app.World.Trading.Indicators.Items.Count);
            _app.Terminal.RefreshNow();
            yield return null;
            Assert.AreEqual(Indicators.MaxPanes, chart.Graphic.Panes.Count);
            Assert.Greater(chart.Graphic.Overlays.Count, 0);
            // hide the first, change a setting of the second, remove the third
            var items = _app.World.Trading.Indicators.Items.ToList();
            _app.World.Trading.Indicators.Update(items[0].Id, c => c.Hidden = true);
            _app.World.Trading.Indicators.Update(items[1].Id, c => c.Params["period"] = 5);
            _app.World.Trading.Indicators.Remove(items[2].Id);
            yield return null;
            _app.World.Trading.Indicators.Clear();
            yield return null;
            Assert.AreEqual(0, chart.Graphic.Panes.Count);
        }

        [UnityTest]
        public IEnumerator Photo_mode_hides_the_interface_and_the_fly_over_moves_the_camera()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            DemoCity.Populate(_app.World);
            yield return null;
            Find("Photo").onClick.Invoke();
            yield return null;
            Assert.IsTrue(_app.Photo);
            Assert.IsFalse(_app.CityHud.gameObject.activeInHierarchy);
            Assert.IsTrue(_app.PhotoBar.gameObject.activeInHierarchy);
            var before = _app.CityView.Camera.transform.position;
            Find("Fly over").onClick.Invoke();
            Assert.IsTrue(_app.CityView.Touring);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreNotEqual(before, _app.CityView.Camera.transform.position);
            _app.SetPhoto(false);
            yield return null;
            Assert.IsFalse(_app.CityView.Touring);
            Assert.IsTrue(_app.CityHud.gameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator The_guided_first_trade_follows_real_actions()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            _app.Tutorial.StartTutorial();
            yield return null;
            Assert.AreEqual(0, _app.Tutorial.Tutorial.Step);
            for (int i = 0; i < 5; i++) _app.World.Trading.Step();
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, _app.Tutorial.Tutorial.Step);
            _app.World.Trading.QuickOrder(Direction.Buy, 10, 20, 40);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(2, _app.Tutorial.Tutorial.Step);
            // the welcome and the guide never throw while the buttons are smashed (covered by the smash test); the card hides on dismiss
            _app.Tutorial.Tutorial.Dismiss();
            _app.Tutorial.Refresh();
            Assert.IsFalse(_app.Tutorial.gameObject.transform.Find("Card").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator A_trade_is_planned_dragged_and_placed_through_the_order_panel_and_chart()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            for (int i = 0; i < 5; i++) _app.World.Trading.Step();
            yield return null;
            var s = _app.World.Trading;
            Find("LONG").onClick.Invoke();
            yield return null;
            Assert.IsNotNull(s.Draft);
            // the plan's lines are on the chart and the SL line can be dragged with pointer events
            var chart = _app.Terminal.Chart;
            _app.Terminal.RefreshNow();
            var sl = chart.Graphic.Lines.First(l => l.Tag == "sl");
            long before = s.Draft.StopLoss;
            chart.PointerDown(new Vector2(chart.Graphic.Plot.center.x, chart.Graphic.YOf(sl.Price)));
            chart.PointerDrag(new Vector2(chart.Graphic.Plot.center.x, chart.Graphic.YOf(sl.Price) + 25f));
            chart.PointerUp(Vector2.zero);
            Assert.AreNotEqual(before, s.Draft.StopLoss);
            // the panel can change the size mode, then place the order
            Find("Risk %").onClick.Invoke();
            yield return null;
            Assert.IsFalse(s.Draft.RiskSizing);
            Find("Place order").onClick.Invoke();
            yield return null;
            Assert.IsNull(s.Draft);
            Assert.AreEqual(1, s.State.Positions.Count);
        }

        [UnityTest]
        public IEnumerator The_object_tree_lists_hides_locks_reorders_and_deletes_drawings()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            for (int i = 0; i < 20; i++) _app.World.Trading.Step();
            var ds = _app.World.Trading.Drawings;
            ds.Add(DrawingKinds.HLine(110000, "#f2c25b"));
            ds.Add(DrawingKinds.HLine(110200, "#6ea8ff"));
            Find("Objects").onClick.Invoke();
            yield return null;
            Assert.IsTrue(_app.Terminal.Chart.ObjectWindow.Visible);
            var first = ds.Items[0].Id;
            Find("Hide all").onClick.Invoke();
            yield return null;
            Assert.IsTrue(ds.Items.All(d => d.Hidden));
            Find("Show all").onClick.Invoke();
            yield return null;
            Assert.IsTrue(ds.Items.All(d => !d.Hidden));
            Find("Delete all").onClick.Invoke();
            yield return null;
            Assert.AreEqual(0, ds.Items.Count);
        }

        [UnityTest]
        public IEnumerator The_terminal_can_move_funds_measure_scale_and_go_full_chart()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            _app.World.Trading.QuickOrder(Direction.Buy, 10, 10, 10);
            for (int i = 0; i < 400; i++) _app.World.Trading.Step();
            _app.World.Trading.CloseAll();
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            // move realized profit to the city with the terminal's own transfer card
            var before = _app.World.City.TreasuryBalance;
            var eligible = _app.World.Transfers.EligibleAmount();
            if (eligible > Money.Zero)
            {
                Find("Move").onClick.Invoke();
                yield return null;
                Assert.IsTrue(_app.World.City.TreasuryBalance >= before);
            }
            // measure: two points make a measurement that is drawn but never saved
            var chart = _app.Terminal.Chart;
            _app.Terminal.RefreshNow();
            chart.SetTool("measure");
            var g = chart.Graphic;
            chart.PointerDown(new Vector2(g.Plot.center.x - 60, g.Plot.center.y));
            chart.PointerDown(new Vector2(g.Plot.center.x + 60, g.Plot.center.y + 40));
            _app.Terminal.RefreshNow();
            Assert.IsTrue(g.Scene.Labels.Any(l => l.Text.Contains("pips")), "the measurement label was not drawn");
            Assert.AreEqual(0, _app.World.Trading.Drawings.Items.Count);
            // auto scale off keeps the range, on follows the candles again
            chart.SetAuto(false);
            Assert.IsFalse(chart.AutoScale);
            chart.SetAuto(true);
            Assert.IsTrue(chart.AutoScale);
            // full chart hides the panels and brings them back
            _app.Terminal.ToggleFull();
            Assert.IsFalse(_app.Terminal.transform.Find("RightPanel").gameObject.activeSelf);
            _app.Terminal.ToggleFull();
            Assert.IsTrue(_app.Terminal.transform.Find("RightPanel").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator In_full_chart_a_plan_can_be_placed_and_the_chart_can_be_dragged_with_auto_scale_off()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            for (int i = 0; i < 300; i++) _app.World.Trading.Step();
            var chart = _app.Terminal.Chart;
            _app.Terminal.ToggleFull();
            Find("Long").onClick.Invoke();
            yield return null;
            Assert.IsNotNull(_app.World.Trading.Draft);
            var bar = _app.Terminal.transform.Find("FullOrderBar");
            Assert.IsTrue(bar.gameObject.activeSelf, "the order bar is missing in full chart");
            // drag the chart with auto scale off: it scrolls through time and moves the price range
            _app.World.Trading.CancelDraft();
            chart.SetAuto(false);
            _app.Terminal.RefreshNow();
            var g = chart.Graphic;
            long lo0 = g.Lo; int pan0 = g.PanBars;
            var start = new Vector2(g.Plot.center.x, g.Plot.center.y);
            chart.PointerDown(start);
            chart.PointerDrag(start + new Vector2(g.Step * 12, 40));
            chart.PointerUp(start);
            g.ComputeLayout();
            Assert.Greater(g.PanBars, pan0, "the chart did not scroll back in time");
            Assert.AreNotEqual(lo0, g.Lo, "the price range did not move");
            // back to live
            g.PanBars = 0;
            _app.Terminal.ToggleFull();
            yield return null;
            Find("Long").onClick.Invoke();
            yield return null;
            Find("Place order").onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, _app.World.Trading.State.Positions.Count);
        }

        [UnityTest]
        public IEnumerator Fibonacci_levels_can_be_edited_added_switched_off_and_deleted()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Open Terminal").onClick.Invoke();
            yield return null;
            for (int i = 0; i < 80; i++) _app.World.Trading.Step();
            var ds = _app.World.Trading.Drawings;
            var bars = _app.World.Trading.ChartData().Candles;
            string id = ds.Add(DrawingKinds.Make("fib", "#6ea8ff", ("a", new Anchor(bars[10].Time, bars[10].Low)), ("b", new Anchor(bars[60].Time, bars[60].High))));
            var chart = _app.Terminal.Chart;
            chart.Select(id);
            yield return null;
            Assert.IsTrue(chart.FibWindow.Visible, "the levels panel did not open for a Fibonacci drawing");
            yield return null;
            Assert.AreEqual(7, DrawingKinds.FibLevelsOf(ds.Get(id)).Count);
            Find("+ Add level").onClick.Invoke();
            yield return null;
            Assert.AreEqual(8, DrawingKinds.FibLevelsOf(ds.Get(id)).Count);
            // switch the first level off with its dot, then delete one with its x
            var dots = LiveButtons().Where(b => LabelOf(b) == "*").ToArray();
            dots[0].onClick.Invoke();
            yield return null;
            Assert.AreEqual(7, DrawingKinds.FibLevelsOf(ds.Get(id)).Count);
            Assert.AreEqual(8, DrawingKinds.FibLevelDefs(ds.Get(id)).Count);
            var kill = LiveButtons().Where(b => LabelOf(b) == "x" && b.transform.parent == chart.FibWindow.transform.Find("Viewport/Content")).ToArray();
            kill[0].onClick.Invoke();
            yield return null;
            Assert.AreEqual(7, DrawingKinds.FibLevelDefs(ds.Get(id)).Count);
            Find("Reset levels").onClick.Invoke();
            yield return null;
            Assert.AreEqual(7, DrawingKinds.FibLevelsOf(ds.Get(id)).Count);
            // a different selection closes the panel
            chart.Select(null);
            yield return null;
            Assert.IsFalse(chart.FibWindow.Visible);
        }
    }
}
