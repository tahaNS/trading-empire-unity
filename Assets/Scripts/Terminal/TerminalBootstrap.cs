using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Builds the whole terminal at runtime, so pressing Play in ANY scene shows it (no scene setup needed).
    /// It starts on labelled synthetic test data; use "Load data..." for a real file.
    /// </summary>
    public static class TerminalBootstrap
    {
        public static TradingSession Session;
        public static TerminalView View;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindFirstObjectByType<TerminalView>() != null) return;
            Create(null);
        }

        /// <summary>Create the terminal. Pass a camera to render the UI through it (used for screenshots); null = screen overlay.</summary>
        public static TerminalView Create(Camera camera)
        {
            if (Camera.main == null && camera == null)
            {
                var cam = new GameObject("Terminal Camera", typeof(Camera)).GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Theme.Bg;
                cam.tag = "MainCamera";
            }
            var canvasGo = new GameObject("Terminal Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            if (camera == null) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            else { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f; }
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            Session = new TradingSession();
            Session.LoadSynthetic();
            View = TerminalView.Build(canvasGo.transform, Session);
            return View;
        }
    }
}
