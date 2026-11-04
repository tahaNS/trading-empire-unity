using System;
using System.IO;
using System.Linq;
using TradingEmpire.Core;
using TradingEmpire.Terminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.EditorTools
{
    /// <summary>
    /// Renders the game to PNG files without opening a window, so the UI can be checked from the command line:
    /// Unity -batchmode -projectPath . -executeMethod TradingEmpire.EditorTools.TerminalScreenshot.CaptureCli -shot out
    /// writes out-city.png, out-city-panels.png and out-terminal.png. Also in the menu: Trading Empire / Capture screenshots.
    /// </summary>
    public static class TerminalScreenshot
    {
        [MenuItem("Trading Empire/Capture screenshots")]
        public static void CaptureMenu()
        {
            string prefix = Path.Combine(Application.dataPath, "..", "screenshot");
            Capture(prefix, true);
            EditorUtility.RevealInFinder(prefix + "-city.png");
        }

        public static void CaptureCli()
        {
            string prefix = "shot";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shot") prefix = args[i + 1];
            Capture(prefix, false);
            EditorApplication.Exit(0);
        }

        /// <summary>Creates Assets/Scenes/Terminal.unity (a camera and a light; the game builds itself on Play).</summary>
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

        static void Capture(string prefix, bool restoreScene)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Shot Camera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var app = GameApp.Create(cam, false, true);
            var scaler = app.GetComponent<Transform>().root.GetComponentInChildren<CanvasScaler>(true) ?? UnityEngine.Object.FindFirstObjectByType<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            var w = app.World;

            DemoCity.Populate(w);

            Shot(app, cam, rt, prefix + "-city.png");

            // a large interface size: the buttons drop to their own row instead of overlapping the cards
            scaler.scaleFactor = 1.25f;
            w.City.Select(w.City.State.Buildings[0].Id);
            Shot(app, cam, rt, prefix + "-city-large.png");
            scaler.scaleFactor = 1f;

            // the inspector and the transfer panel
            w.City.Select(w.City.State.Buildings[0].Id);
            var hud = app.CityHud;
            hud.transform.Find("TransferPanel").gameObject.SetActive(true);
            Shot(app, cam, rt, prefix + "-city-panels.png");

            hud.transform.Find("TransferPanel").gameObject.SetActive(false);
            hud.transform.Find("ProgressPanel").gameObject.SetActive(true);
            hud.RefreshNow();
            Shot(app, cam, rt, prefix + "-city-progress.png");
            hud.transform.Find("ProgressPanel").gameObject.SetActive(false);
            hud.transform.Find("PolicyPanel").gameObject.SetActive(true);
            Shot(app, cam, rt, prefix + "-city-policies.png");
            hud.transform.Find("PolicyPanel").gameObject.SetActive(false);

            // the menu with the missions
            app.MenuHud.Show(true);
            app.MenuHud.SelectTab(1);
            Shot(app, cam, rt, prefix + "-menu.png");
            app.MenuHud.SelectTab(2);
            Shot(app, cam, rt, prefix + "-menu-help.png");
            UnityEngine.PlayerPrefs.SetInt("te.god", 1);
            app.MenuHud.SelectTab(0);
            Shot(app, cam, rt, prefix + "-menu-god.png");
            UnityEngine.PlayerPrefs.SetInt("te.god", 0);
            app.MenuHud.SelectTab(0);
            Shot(app, cam, rt, prefix + "-menu-game.png");
            app.MenuHud.Show(false);

            // the terminal, with a trade open
            app.ShowTerminal(true);
            var s = app.World.Trading;
            s.QuickOrder(Direction.Buy, 10, 20, 40);
            for (int i = 0; i < 8; i++) s.Step();
            // chart drawings: a trend line, a Fibonacci, a box, a position plan, a note
            {
                var cd = s.ChartData();
                var bars = cd.Candles;
                int n = bars.Count;
                var recent = bars.Skip(n - 60).ToList();
                long hi = recent.Max(b => b.High), lo = recent.Min(b => b.Low);
                var low = recent.First(b => b.Low == lo); var high = recent.Last(b => b.High == hi);
                long tf = cd.TfMs;
                var trendId = s.Drawings.Add(DrawingKinds.Trend(new Anchor(bars[n - 50].Time, bars[n - 50].Low), new Anchor(bars[n - 12].Time, bars[n - 12].High), "#f2c25b"));
                s.Drawings.Add(DrawingKinds.Make("fib", "#6ea8ff", ("a", new Anchor(low.Time, lo)), ("b", new Anchor(high.Time, hi))));
                s.Drawings.Add(DrawingKinds.Make("rect", "#c792ea", ("a", new Anchor(bars[n - 36].Time, hi - 4 * cd.PipPoints)), ("b", new Anchor(bars[n - 24].Time, hi - 12 * cd.PipPoints))));
                s.Drawings.Add(DrawingKinds.HLine(lo + 2 * cd.PipPoints, "#2fcfae"));
                long close = bars[n - 6].Close;
                s.Drawings.Add(DrawingKinds.Position(true, new Anchor(bars[n - 6].Time, close), bars[n - 6].Time + 14 * tf, close + 20 * cd.PipPoints, close - 10 * cd.PipPoints, "#2fcfae"));
                s.Drawings.Add(DrawingKinds.Text(new Anchor(bars[n - 46].Time, hi), "resistance zone", "#e6edf3"));
                s.Drawings.Add(DrawingKinds.Marker(new Anchor(low.Time, lo), true, "#2fcfae"));
                app.Terminal.Chart.Select(trendId);
            }
            Shot(app, cam, rt, prefix + "-terminal.png");
            scaler.scaleFactor = 1.4f;
            app.Terminal.RefreshNow();
            Shot(app, cam, rt, prefix + "-terminal-large.png");
            scaler.scaleFactor = 1f;

            // indicators: a moving average and Bollinger bands over the candles, RSI and MACD in strips, with the window open
            s.Drawings.Clear();
            s.Indicators.Add("ema"); s.Indicators.Add("bb"); s.Indicators.Add("rsi"); s.Indicators.Add("macd");
            app.Terminal.Chart.IndicatorWindow.Show(true);
            app.Terminal.Chart.IndicatorWindow.RefreshNow();
            Shot(app, cam, rt, prefix + "-indicators.png");
            app.Terminal.Chart.IndicatorWindow.SelectTab(1);
            app.Terminal.Chart.IndicatorWindow.RefreshNow();
            Shot(app, cam, rt, prefix + "-indicators-add.png");
            app.Terminal.Chart.IndicatorWindow.Show(false);
            s.Indicators.Clear();

            // the seven drawing kinds that used to show as outlines
            {
                var cd = s.ChartData(); var bars = cd.Candles; int n = bars.Count; long pp = cd.PipPoints;
                s.Drawings.Clear();
                s.Drawings.Add(DrawingKinds.Make("regression", "#6ea8ff", ("a", new Anchor(bars[n - 90].Time, bars[n - 90].Close)), ("b", new Anchor(bars[n - 10].Time, bars[n - 10].Close))));
                s.Drawings.Add(DrawingKinds.Make("pitchfork", "#c792ea", ("a", new Anchor(bars[n - 110].Time, bars[n - 110].Low)), ("b", new Anchor(bars[n - 80].Time, bars[n - 80].High)), ("c", new Anchor(bars[n - 60].Time, bars[n - 60].Low))));
                s.Drawings.Add(DrawingKinds.Make("gannfan", "#f2c25b", ("a", new Anchor(bars[n - 100].Time, bars[n - 100].Low)), ("b", new Anchor(bars[n - 70].Time, bars[n - 100].Low + 15 * pp))));
                s.Drawings.Add(DrawingKinds.Make("fibchannel", "#2fcfae", ("a", new Anchor(bars[n - 70].Time, bars[n - 70].Close)), ("b", new Anchor(bars[n - 30].Time, bars[n - 30].Close)), ("c", new Anchor(bars[n - 50].Time, bars[n - 50].Close + 12 * pp))));
                s.Drawings.Add(DrawingKinds.Make("fibtime", "#f2686b", ("a", new Anchor(bars[n - 120].Time, bars[n - 120].Close)), ("b", new Anchor(bars[n - 118].Time, bars[n - 118].Close))));
                s.Drawings.Add(DrawingKinds.Make("fibfan", "#6ea8ff", ("a", new Anchor(bars[n - 60].Time, bars[n - 60].Low)), ("b", new Anchor(bars[n - 25].Time, bars[n - 25].High))));
                s.Drawings.Add(DrawingKinds.Make("fibarcs", "#c792ea", ("a", new Anchor(bars[n - 40].Time, bars[n - 40].Low)), ("b", new Anchor(bars[n - 15].Time, bars[n - 15].High))));
            }
            Shot(app, cam, rt, prefix + "-drawings.png");
            {
                var fibId = s.Drawings.Items.First(x => x.Kind == "fibchannel").Id;
                app.Terminal.Chart.Select(fibId);
                app.Terminal.Chart.FibWindow.RefreshNow();
                Shot(app, cam, rt, prefix + "-fib-levels.png");
                app.Terminal.Chart.Select(null);
            }
            app.Terminal.Chart.ObjectWindow.Show(true);
            app.Terminal.Chart.ObjectWindow.RefreshNow();
            Shot(app, cam, rt, prefix + "-objects.png");
            app.Terminal.Chart.ObjectWindow.Show(false);
            s.Drawings.Clear();

            // the order plan with its lines, and the guide window
            s.CloseAll();
            s.StartDraft(Direction.Buy);
            Shot(app, cam, rt, prefix + "-plan.png");
            s.CancelDraft();
            app.Terminal.Guide.Show(true);
            Shot(app, cam, rt, prefix + "-guide.png");
            app.Terminal.Guide.Show(false);

            // the guided first trade
            app.Tutorial.StartTutorial();
            app.Tutorial.Refresh();
            app.Tutorial.UpdateHighlightNow();
            Shot(app, cam, rt, prefix + "-tutorial.png");
            app.Tutorial.Tutorial.Dismiss();
            app.Tutorial.Refresh();

            // the journal: close the trade and write about it
            s.CloseAll();
            s.QuickOrder(Direction.Sell, 10, 15, 30);
            for (int i = 0; i < 3; i++) s.Step();
            s.CloseAll();
            var closed = s.State.Closed;
            s.Journal.Set(JournalStore.KeyOf(closed[0]), "Waited for the pullback, stop under the swing low.", new[] { "Good setup", "Patient" }, 4);
            s.Journal.Set(JournalStore.KeyOf(closed[1]), "Chased it after missing the move.", new[] { "FOMO", "Chased price" }, 2);
            app.Terminal.SelectTab(3);
            Shot(app, cam, rt, prefix + "-journal.png");
            scaler.scaleFactor = 1.35f;
            app.Terminal.SelectTab(3);
            Shot(app, cam, rt, prefix + "-journal-large.png");
            app.Terminal.SelectTab(0);
            Shot(app, cam, rt, prefix + "-positions-large.png");
            app.Terminal.SelectTab(4);
            Shot(app, cam, rt, prefix + "-stats-large.png");
            scaler.scaleFactor = 1f;
            app.Terminal.SelectTab(3);

            // the challenge list, and a challenge run
            app.Terminal.SelectTab(0);
            app.Terminal.OpenChallengeList();
            Shot(app, cam, rt, prefix + "-challenges.png");

            cam.targetTexture = null;
            rt.Release();
            if (restoreScene) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        static void Place(CityGame city, string defId, int x, int y)
        {
            city.StartPlacement(defId);
            if (!city.TryPlace(new Cell(x, y))) Debug.LogWarning("Could not place " + defId + " at " + x + "," + y + ": " + (city.State.Notice != null ? city.State.Notice.Text : ""));
        }

        static void Shot(GameApp app, Camera cam, RenderTexture rt, string path)
        {
            app.CityView.SyncNow();
            app.CityHud.RefreshNow();
            app.MenuHud.RefreshNow();
            if (app.TerminalOpen) app.Terminal.RefreshNow();
            Canvas.ForceUpdateCanvases();
            if (app.TerminalOpen) app.Terminal.RefreshNow(); else app.CityHud.RefreshNow();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("Screenshot written to " + Path.GetFullPath(path));
        }
    }
}
