using System;
using System.Collections.Generic;
using System.IO;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Builds the whole game at runtime, so pressing Play in ANY scene shows it (no scene setup needed): the world, the 3D city
    /// with its HUD, the trading terminal, the menu, saving and challenges. The city starts with an empty treasury: trade, then
    /// transfer. If a save exists it is loaded; the game autosaves.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const float AutosaveSeconds = 30f;

        public GameWorld World { get; private set; }
        public CityView CityView { get; private set; }
        public CityHud CityHud { get; private set; }
        public MenuHud MenuHud { get; private set; }
        public TerminalView Terminal { get; private set; }
        public ChallengeController Challenges { get; private set; }
        public SaveManager Saves { get; private set; }
        public AudioManager Audio { get; private set; }
        public bool TerminalOpen { get; private set; }
        public string LastError { get; private set; }

        bool _persist;
        Camera _uiCamera;
        float _nextAutosave;
        readonly List<GameObject> _owned = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (FindFirstObjectByType<GameApp>() != null) return;
            Create(null, true, false);
        }

        /// <summary>
        /// Create the game. Pass a camera to render the UI through it (used for screenshots); null = screen overlay.
        /// <paramref name="persist"/> turns saving and loading on; <paramref name="forceNew"/> ignores an existing save.
        /// </summary>
        public static GameApp Create(Camera uiCamera, bool persist = false, bool forceNew = true)
        {
            GameSettings.Load();
            var cam = uiCamera != null ? uiCamera : Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }

            var go = new GameObject("Game");
            var app = go.AddComponent<GameApp>();
            app._persist = persist;
            app._uiCamera = uiCamera;
            app._owned.Add(go);
            app.World = GameWorld.Create();
            string startupNotice = null;
            if (persist)
            {
                app.Saves = new SaveManager(app.World, new FileSaveStorage(Path.Combine(Application.persistentDataPath, "saves")));
            }
            else
            {
                app.Saves = new SaveManager(app.World, new MemorySaveStorage());
            }
            bool loaded = false;
            if (persist && !forceNew && app.Saves.HasSave)
            {
                var outcome = app.Saves.Load();
                loaded = outcome.Ok;
                startupNotice = outcome.Ok ? outcome.Notice : "Could not load your save: " + outcome.Error + " A new game was started.";
            }
            if (!loaded)
            {
                app.World.StartNewGame();
                app.World.City.EventSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            }

            app.CityView = CityView.Create(app.World.City, cam);
            app._owned.Add(app.CityView.gameObject);

            var canvasGo = new GameObject("Game Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            app._owned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            if (uiCamera == null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            else { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1f; }
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameSettings.Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            app.Audio = AudioManager.Create(app.World);
            app._owned.Add(app.Audio.gameObject);
            app.Challenges = new ChallengeController(app.World, s => { app.Terminal.SetSession(s); if (s != app.World.Trading) app.Audio.Watch(s); });
            app.CityHud = CityHud.Build(canvasGo.transform, app.World, () => app.ShowTerminal(true), () => app.MenuHud.Show(true));
            app.Terminal = TerminalView.Build(canvasGo.transform, app.World.Trading, () => app.ShowTerminal(false), app.Challenges);
            app.MenuHud = MenuHud.Build(canvasGo.transform, app);
            app.ShowTerminal(false);
            if (startupNotice != null) app.World.City.Announce(string.IsNullOrEmpty(startupNotice) ? NoticeKind.Info : NoticeKind.Error, startupNotice);
            app._nextAutosave = Time.unscaledTime + AutosaveSeconds;
            Debug.Log("Trading Empire started (" + (loaded ? "loaded a save" : "new game") + ", " + app.World.City.State.Buildings.Count + " buildings, " + app.World.Trading.State.Total + " candles)");
            return app;
        }

        public void ShowTerminal(bool open)
        {
            TerminalOpen = open;
            Terminal.gameObject.SetActive(open);
            CityHud.gameObject.SetActive(!open);
            CityView.Interactive = !open && !MenuHud.Open;
            MenuHud.RefreshNow();
            Audio.UpdateHum(World, open);
            if (open) Terminal.RefreshNow(); else CityHud.RefreshNow();
        }

        /// <summary>Re-apply settings (UI size, candle colours) to the running game.</summary>
        public void ApplySettings()
        {
            var scaler = FindFirstObjectByType<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = GameSettings.Reference;
            Terminal.RefreshNow();
        }

        // ---------------------------------------------------------------- saving

        public bool Save()
        {
            var err = new[] { "" };
            bool ok = Saves.Save(false, err);
            LastError = ok ? null : err[0];
            return ok;
        }

        public string LoadSaved()
        {
            var o = Saves.Load();
            if (o.Ok) { Challenges.Leave(); World.City.Announce(NoticeKind.Success, "Game loaded."); return o.Notice ?? "Game loaded."; }
            return o.Error;
        }

        public string RestoreBackup(string day)
        {
            var o = Saves.RestoreBackup(day);
            if (o.Ok) { Challenges.Leave(); return "Restored the backup from " + day + "."; }
            return o.Error;
        }

        /// <summary>Write a self-contained save file (web-compatible) into the exports folder and return where it went.</summary>
        public string Export()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "exports");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "trading-empire-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(file, Saves.ExportText(), new System.Text.UTF8Encoding(false));
                return "Exported to " + file;
            }
            catch (Exception e) { return "Could not export: " + e.Message; }
        }

        public string Import(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path == "") return "Type the path to a save file first.";
            if (!File.Exists(path)) return "File not found: " + path;
            try
            {
                var o = Saves.Import(File.ReadAllText(path));
                if (o.Ok) { Challenges.Leave(); return "Imported. The game is paused: press a speed button to continue."; }
                return o.Error;
            }
            catch (Exception e) { return "Could not read the file: " + e.Message; }
        }

        /// <summary>Start a fresh game. The old save stays on disk as a backup until the new game is saved over it.</summary>
        public void StartOver()
        {
            if (_persist) Save(); // keep what the player had, as the backup of the next save
            foreach (var g in _owned) if (g != null) Ui.Kill(g);
            _owned.Clear();
            // build the new game on the next frame, after the old objects are gone
            var starter = new GameObject("Restarting").AddComponent<Restarter>();
            starter.Begin(_uiCamera, _persist);
        }

        sealed class Restarter : MonoBehaviour
        {
            Camera _cam; bool _persist;
            public void Begin(Camera cam, bool persist) { _cam = cam; _persist = persist; }
            void Start()
            {
                var app = Create(_cam, _persist, true);
                if (app.MenuHud != null) app.MenuHud.Show(false);
                Ui.Kill(gameObject);
            }
        }

        void Update()
        {
            // the city clock always runs; the market only while the terminal is open (a challenge has its own market)
            World.Clock.Advance(Time.unscaledDeltaTime);
            if (TerminalOpen)
            {
                var session = Challenges.Active ? Challenges.Run.Session : World.Trading;
                if (session.State.Status == SessionStatus.Ready) session.Advance(Time.unscaledDeltaTime);
            }
            CityView.Interactive = !TerminalOpen && !MenuHud.Open;
            if (_persist && Time.unscaledTime >= _nextAutosave)
            {
                _nextAutosave = Time.unscaledTime + AutosaveSeconds;
                Saves.Save(true);
            }
        }

        void OnApplicationQuit() { if (_persist && World != null) Saves.Save(true); }
        void OnApplicationPause(bool paused) { if (paused && _persist && World != null) Saves.Save(true); }
    }
}
