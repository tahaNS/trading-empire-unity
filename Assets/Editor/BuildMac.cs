using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TradingEmpire.EditorTools
{
    /// <summary>
    /// Builds the macOS app. From the command line:
    /// Unity -batchmode -quit -projectPath . -executeMethod TradingEmpire.EditorTools.BuildMac.BuildCli -buildOutput Builds/macOS
    /// or from the menu: Trading Empire / Build macOS app. The app is a universal build (Apple silicon and Intel) and is not signed.
    /// </summary>
    public static class BuildMac
    {
        public const string AppName = "Trading Empire";
        const string Scene = "Assets/Scenes/Terminal.unity";
        const string Icon = "Assets/Art/AppIcon.png";

        [MenuItem("Trading Empire/Build macOS app")]
        public static void BuildMenu()
        {
            var report = Build(Path.Combine("Builds", "macOS"));
            EditorUtility.RevealInFinder(report.summary.outputPath);
        }

        public static void BuildCli()
        {
            string dir = Path.Combine("Builds", "macOS");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-buildOutput") dir = args[i + 1];
            var report = Build(dir);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static void ApplySettings()
        {
            PlayerSettings.companyName = "Trading Empire";
            PlayerSettings.productName = AppName;
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.tradingempire.game");
            PlayerSettings.macOS.buildNumber = "1";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma; // the UI colours are tuned for gamma space
            // universal: runs on Apple silicon and on Intel Macs
            EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "x64arm64");

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Icon);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Standalone, new[] { icon }, IconKind.Application);
        }

        public static BuildReport Build(string outputDir)
        {
            ApplySettings();
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, AppName + ".app");
            if (Directory.Exists(path)) Directory.Delete(path, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = path,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("BUILD " + report.summary.result + " size=" + report.summary.totalSize + " time=" + report.summary.totalTime + " errors=" + report.summary.totalErrors + " path=" + report.summary.outputPath);
            return report;
        }
    }
}
