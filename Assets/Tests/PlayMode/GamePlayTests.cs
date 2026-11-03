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

        [SetUp]
        public void Setup() { CleanAll(); }

        [TearDown]
        public void TearDown() { CleanAll(); }

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

            Find("Terminal").onClick.Invoke();
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
            Find("House").onClick.Invoke();
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
            Find("Demolish").onClick.Invoke();
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
                Assert.Greater(buttons.Length, 0, "no clickable button left on screen (round " + round + ")");
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
            Find("Menu").onClick.Invoke();
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
            Find("Terminal").onClick.Invoke();
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
            _app.CityHud.transform.Find("TransferPanel").gameObject.SetActive(true);
            yield return null;
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("TopBar")), "the top bar lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("BuildBar")), "the build bar lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("Inspector")), "the inspector lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("TransferPanel")), "the transfer panel lets clicks through");
            Assert.IsTrue(UiBlocksAt(ScreenCenterOf("Goal")), "the goal card lets clicks through");
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
        public IEnumerator Settings_apply_to_the_running_game()
        {
            _app = GameApp.Create(null, false, true);
            yield return null;
            Find("Menu").onClick.Invoke();
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
    }
}
