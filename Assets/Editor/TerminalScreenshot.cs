using System;
using System.IO;
using TradingEmpire.Core;
using TradingEmpire.Terminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.EditorTools
{
    /// <summary>
    /// Renders the terminal to a PNG without opening a window, so the UI can be checked from the command line:
    /// Unity -batchmode -projectPath . -executeMethod TradingEmpire.EditorTools.TerminalScreenshot.CaptureCli -shot out.png
    /// Also available from the menu: Trading Empire / Capture terminal screenshot.
    /// </summary>
    public static class TerminalScreenshot
    {
        [MenuItem("Trading Empire/Capture terminal screenshot")]
        public static void CaptureMenu()
        {
            string path = Path.Combine(Application.dataPath, "..", "terminal-screenshot.png");
            Capture(path, true);
            EditorUtility.RevealInFinder(path);
        }

        public static void CaptureCli()
        {
            string path = "terminal.png";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shot") path = args[i + 1];
            Capture(path, false);
            EditorApplication.Exit(0);
        }

        /// <summary>Creates Assets/Scenes/Terminal.unity (a camera and a light; the terminal builds itself on Play).</summary>
        [MenuItem("Trading Empire/Create terminal scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            const string path = "Assets/Scenes/Terminal.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Scene created: " + path);
        }

        public static void CreateSceneCli()
        {
            CreateScene();
            EditorApplication.Exit(0);
        }

        static void Capture(string path, bool restoreScene)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Shot Camera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.Bg;
            cam.orthographic = true;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var view = TerminalBootstrap.Create(cam);
            var scaler = view.GetComponentInParent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // a small scripted session: a long with stop and target, a resting limit, then a few candles
            var s = view.Session;
            s.QuickOrder(Direction.Buy, 10, 20, 40);
            for (int i = 0; i < 6; i++) s.Step();
            var q = s.ChartData();
            long limit = q.Ask.Value - 12 * q.PipPoints;
            s.PlaceOrder(new OrderRequest { Direction = Direction.Buy, Lots = 10, Entry = limit, StopLoss = limit - 15 * q.PipPoints, TakeProfit = limit + 30 * q.PipPoints });
            for (int i = 0; i < 4; i++) s.Step();

            Canvas.ForceUpdateCanvases();
            view.RefreshNow();
            Canvas.ForceUpdateCanvases();
            view.RefreshNow();
            Canvas.ForceUpdateCanvases(); // rebuild the chart mesh before rendering
            var g = view.GetComponentInChildren<CandleGraphic>();
            Debug.Log("CHART bars=" + g.Bars.Count + " count=" + g.Count + " plot=" + g.Plot + " lo=" + g.Lo + " hi=" + g.Hi + " active=" + g.isActiveAndEnabled
                + " rect=" + g.rectTransform.rect + " canvasNull=" + (g.canvas == null) + " cull=" + g.canvasRenderer.cull + " alpha=" + g.canvasRenderer.GetInheritedAlpha()
                + " mat=" + (g.canvasRenderer.materialCount > 0 ? g.canvasRenderer.GetMaterial(0).name : "none"));
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("Terminal screenshot written to " + Path.GetFullPath(path));

            cam.targetTexture = null;
            rt.Release();
            if (restoreScene) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }
    }
}
