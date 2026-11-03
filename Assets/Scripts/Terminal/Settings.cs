using UnityEngine;

namespace TradingEmpire.Terminal
{
    /// <summary>Player settings kept on this computer (not part of a game save).</summary>
    public static class GameSettings
    {
        const string KeyScale = "te.uiScale", KeyCb = "te.colorBlind";
        public static readonly float[] Scales = { 0.85f, 1f, 1.15f, 1.3f };
        public static readonly string[] ScaleNames = { "Small", "Normal", "Large", "Extra large" };

        public static int ScaleIndex { get; private set; } = 1;

        public static void Load()
        {
            ScaleIndex = Mathf.Clamp(PlayerPrefs.GetInt(KeyScale, 1), 0, Scales.Length - 1);
            Theme.ColorBlind = PlayerPrefs.GetInt(KeyCb, 0) == 1;
        }

        public static void SetScale(int index) { ScaleIndex = Mathf.Clamp(index, 0, Scales.Length - 1); PlayerPrefs.SetInt(KeyScale, ScaleIndex); PlayerPrefs.Save(); }
        public static void SetColorBlind(bool on) { Theme.ColorBlind = on; PlayerPrefs.SetInt(KeyCb, on ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>The canvas reference height: a larger UI scale means a smaller reference, so everything is drawn bigger.</summary>
        public static Vector2 Reference { get { float k = Scales[ScaleIndex]; return new Vector2(1600f / k, 900f / k); } }
    }
}
