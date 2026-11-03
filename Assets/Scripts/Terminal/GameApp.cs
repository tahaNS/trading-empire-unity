using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Builds the whole game at runtime, so pressing Play in ANY scene shows it (no scene setup needed): the world, the
    /// 3D city with its HUD, and the trading terminal. The city starts with an empty treasury: trade, then transfer.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public GameWorld World { get; private set; }
        public CityView CityView { get; private set; }
        public CityHud CityHud { get; private set; }
        public TerminalView Terminal { get; private set; }
        public bool TerminalOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (FindFirstObjectByType<GameApp>() != null) return;
            Create(null);
        }

        /// <summary>Create the game. Pass a camera to render the UI through it (used for screenshots); null = screen overlay.</summary>
        public static GameApp Create(Camera uiCamera)
        {
            var cam = uiCamera != null ? uiCamera : Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }

            var go = new GameObject("Game");
            var app = go.AddComponent<GameApp>();
            app.World = GameWorld.Create();
            app.World.StartNewGame();
            app.World.City.EventSeed = (uint)Random.Range(1, int.MaxValue);

            app.CityView = CityView.Create(app.World.City, cam);

            var canvasGo = new GameObject("Game Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            if (uiCamera == null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            else { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1f; }
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            app.CityHud = CityHud.Build(canvasGo.transform, app.World, () => app.ShowTerminal(true));
            app.Terminal = TerminalView.Build(canvasGo.transform, app.World.Trading, () => app.ShowTerminal(false));
            app.ShowTerminal(false);
            return app;
        }

        public void ShowTerminal(bool open)
        {
            TerminalOpen = open;
            Terminal.gameObject.SetActive(open);
            CityHud.gameObject.SetActive(!open);
            CityView.Interactive = !open;
            if (open) Terminal.RefreshNow(); else CityHud.RefreshNow();
        }

        void Update()
        {
            // the city clock always runs; the market replay only while the terminal is open
            World.Advance(Time.unscaledDeltaTime, TerminalOpen);
        }
    }
}
