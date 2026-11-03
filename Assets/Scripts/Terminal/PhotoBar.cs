using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>Photo mode: the interface hides, leaving the city, a screenshot button and a fly-over. Esc leaves.</summary>
    public sealed class PhotoBar : MonoBehaviour
    {
        GameApp _app;
        RectTransform _bar;
        Button _tourButton;
        Text _message;
        float _messageUntil, _restoreAt;

        public static PhotoBar Build(Transform canvas, GameApp app)
        {
            var root = Ui.Rect(canvas, "PhotoBar");
            Ui.Stretch(root);
            var bar = root.gameObject.AddComponent<PhotoBar>();
            bar._app = app;
            bar.BuildUi(root);
            root.gameObject.SetActive(false);
            return bar;
        }

        void BuildUi(RectTransform root)
        {
            var card = Ui.Card(root, "Bar", 14f);
            _bar = card.rectTransform;
            Ui.Place(_bar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 16), new Vector2(250, 66));
            var shot = Ui.Pill(_bar, "Save screenshot", () => StartCoroutine(Shoot()), true, 13);
            Ui.Place((RectTransform)shot.transform, Vector2.zero, new Vector2(0, 1), new Vector2(10, 8), new Vector2(166, -8));
            _tourButton = Ui.Pill(_bar, "Fly over the city", () => ToggleTour(), false, 13);
            Ui.Place((RectTransform)_tourButton.transform, Vector2.zero, new Vector2(0, 1), new Vector2(174, 8), new Vector2(344, -8));
            var exit = Ui.Pill(_bar, "Exit (Esc)", () => _app.SetPhoto(false), false, 13);
            Ui.Place((RectTransform)exit.transform, Vector2.zero, new Vector2(0, 1), new Vector2(352, 8), new Vector2(490, -8));
            var msgBg = Ui.Card(root, "Message", 10f, Color.white);
            Ui.Place(msgBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-300, 76), new Vector2(300, 108));
            _message = Ui.Label(msgBg.transform, "", 12, Theme.Ink, TextAnchor.MiddleCenter);
            msgBg.gameObject.SetActive(false);
        }

        /// <summary>A screenshot interrupted by leaving photo mode must not leave the bar hidden for next time.</summary>
        void OnEnable()
        {
            if (_bar != null) _bar.gameObject.SetActive(true);
        }

        void ToggleTour()
        {
            if (_app.CityView.Touring) _app.CityView.StopTour(); else _app.CityView.StartTour();
            RefreshTour();
        }

        void RefreshTour() { Ui.SetButtonLabel(_tourButton, _app.CityView.Touring ? "Stop fly-over" : "Fly over the city"); }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) _app.SetPhoto(false);
            if (!_bar.gameObject.activeSelf && Time.unscaledTime > _restoreAt) _bar.gameObject.SetActive(true);
            RefreshTour();
            if (_message != null && _message.transform.parent.gameObject.activeSelf && Time.unscaledTime > _messageUntil) _message.transform.parent.gameObject.SetActive(false);
        }

        void Say(string text)
        {
            _message.text = text;
            _message.transform.parent.gameObject.SetActive(true);
            _messageUntil = Time.unscaledTime + 5f;
        }

        /// <summary>Hide the bar for one frame and save the picture to Pictures/Trading Empire.</summary>
        IEnumerator Shoot()
        {
            _bar.gameObject.SetActive(false);
            _restoreAt = Time.unscaledTime + 0.5f; // safety net: never leave the bar hidden if the end of the frame is not reached
            _message.transform.parent.gameObject.SetActive(false);
            yield return new WaitForEndOfFrame();
            string path = null, error = null;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var bytes = tex.EncodeToPNG();
                Destroy(tex);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Trading Empire");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "city-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception e) { error = e.Message; }
            _bar.gameObject.SetActive(true);
            Say(error == null ? "Saved " + path : "Could not save the screenshot: " + error);
        }
    }
}
