using System;
using System.Collections.Generic;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The welcome dialog (first run) and the guided first trade: a small card with the current step. Steps finish when the game's own
    /// state moves on (see <see cref="Tutorial"/>), so they cannot be faked.
    /// </summary>
    public sealed class TutorialCard : MonoBehaviour
    {
        const string WelcomedKey = "te.welcomed", StatusKey = "te.tutorial", StepKey = "te.tutorial.step";

        GameApp _app;
        bool _persist;
        readonly Tutorial _tutorial = new Tutorial();
        RectTransform _card, _welcome, _offer;
        Text _title, _body, _count;
        Button _skip;
        float _nextCheck;

        public Tutorial Tutorial { get { return _tutorial; } }
        public bool WelcomeOpen { get { return _welcome.gameObject.activeSelf; } }

        public static TutorialCard Build(Transform canvas, GameApp app, bool persist)
        {
            var root = Ui.Rect(canvas, "Tutorial");
            Ui.Stretch(root);
            var t = root.gameObject.AddComponent<TutorialCard>();
            t._app = app; t._persist = persist;
            t.BuildUi(root);
            if (persist) t.Restore();
            return t;
        }

        void BuildUi(RectTransform root)
        {
            var c = Ui.Card(root, "Card", 14f, Color.white);
            _card = c.rectTransform;
            Ui.Place(_card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-290, 236), new Vector2(290, 344));
            _count = Ui.Label(_card, "", 11, Theme.Primary, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_count.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -28), new Vector2(-170, -6));
            _title = Ui.Label(_card, "", 15, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -52), new Vector2(-16, -28));
            _body = Ui.Label(_card, "", 12, Theme.Muted, TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 8), new Vector2(-16, -54));
            _skip = Ui.Pill(_card, "Skip", () => { _tutorial.Skip(Facts()); Save(); Refresh(); }, false, 11);
            Ui.Place((RectTransform)_skip.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-124, -30), new Vector2(-70, -8));
            var close = Ui.Pill(_card, "Hide guide", () => { _tutorial.Dismiss(); Save(); Refresh(); }, false, 11);
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-66, -30), new Vector2(-8, -8));
            // the close button is wider than 58px: re-place both from the right edge
            Ui.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-92, -30), new Vector2(-8, -8));
            Ui.Place((RectTransform)_skip.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-148, -30), new Vector2(-98, -8));
            _card.gameObject.SetActive(false);

            var dim = Ui.Box(root, "Welcome", new Color(0.05f, 0.1f, 0.15f, 0.5f));
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _welcome = dim.rectTransform;
            var win = Ui.Card(_welcome, "Window", 16f, Color.white);
            Ui.Place(win.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330, -270), new Vector2(330, 270));
            var h = Ui.Label(win.transform, "Welcome to Trading Empire", 20, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(h.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -56), new Vector2(-24, -16));
            var text = Ui.Label(win.transform,
                "<b>1  Trade.</b> Open the Terminal and trade historical forex and gold prices by hand: step through candles, place a buy or sell with a stop-loss and take-profit, and close it for a profit.\n\n"
                + "<b>2  Transfer.</b> Move realized profits from your trading account to the city treasury (Finances). Unrealized profit and margin in use can't be moved.\n\n"
                + "<b>3  Build.</b> Spend the treasury on buildings. They take a few days to finish, then pay rent and cost upkeep as time passes.\n\n"
                + "<b>4  Grow.</b> Complete missions to unlock bigger buildings and more land, until your city's income matters more than your trading.\n\n"
                + "<color=#5c6b75>Careful: if your trading account and the city treasury both run empty (and no building earns income), it's game over.\nYour game saves automatically. Game > Export makes a backup file.</color>",
                13, Theme.Ink, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(24, 76), new Vector2(-24, -64));
            var guide = Ui.Pill(win.transform, "Guide me through a first trade", () => { CloseWelcome(); StartTutorial(); }, true, 13);
            Ui.Place((RectTransform)guide.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 22), new Vector2(314, 62));
            var go = Ui.Pill(win.transform, "Start playing", () => CloseWelcome(), false, 13);
            Ui.Place((RectTransform)go.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(326, 22), new Vector2(476, 62));
            _welcome.gameObject.SetActive(false);

            // the "New here?" offer shown the first time the terminal opens
            var oc = Ui.Card(root, "Offer", 12f, Color.white);
            _offer = oc.rectTransform;
            Ui.Place(_offer, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 16), new Vector2(386, 150));
            var ot = Ui.Label(_offer, "New here?", 15, Theme.Ink, TextAnchor.MiddleLeft, false, true);
            Ui.Place(ot.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -38), new Vector2(-16, -10));
            var ob = Ui.Label(_offer, "Take a short guided first trade: plan it, place it, see it through and review it.", 12, Theme.Muted, TextAnchor.UpperLeft);
            ob.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Place(ob.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 46), new Vector2(-16, -40));
            var yes = Ui.Pill(_offer, "Start the tutorial", () => { CloseOffer(); StartTutorial(); }, true, 12);
            Ui.Place((RectTransform)yes.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 12), new Vector2(176, 42));
            var no = Ui.Pill(_offer, "No thanks", () => CloseOffer(), false, 12);
            Ui.Place((RectTransform)no.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(184, 12), new Vector2(284, 42));
            _offer.gameObject.SetActive(false);
        }

        void CloseOffer()
        {
            _offer.gameObject.SetActive(false);
            if (_persist) PlayerPrefs.SetInt("te.offerSeen", 1);
        }

        /// <summary>The first time the terminal opens (and no guide was started), offer the guided first trade.</summary>
        public void OfferInTerminal()
        {
            if (!_persist || PlayerPrefs.GetInt("te.offerSeen", 0) == 1 || _tutorial.Status != TutorialStatus.New || _welcome.gameObject.activeSelf) return;
            _offer.gameObject.SetActive(true);
        }

        public void HideOffer() { if (_offer != null) _offer.gameObject.SetActive(false); }

        void Restore()
        {
            if (PlayerPrefs.GetInt(WelcomedKey, 0) == 0) { _welcome.gameObject.SetActive(true); return; }
            if (PlayerPrefs.GetString(StatusKey, "") == "active") _tutorial.Resume(PlayerPrefs.GetInt(StepKey, 0), Facts());
            Refresh();
        }

        void Save()
        {
            if (!_persist) return;
            PlayerPrefs.SetString(StatusKey, _tutorial.Status == TutorialStatus.Active ? "active" : _tutorial.Status.ToString().ToLowerInvariant());
            PlayerPrefs.SetInt(StepKey, _tutorial.Step);
        }

        public void ShowWelcome() { _welcome.gameObject.SetActive(true); }

        void CloseWelcome()
        {
            _welcome.gameObject.SetActive(false);
            if (_persist) PlayerPrefs.SetInt(WelcomedKey, 1);
        }

        public void StartTutorial() { _tutorial.Start(Facts()); Save(); Refresh(); }

        TutorialFacts Facts()
        {
            var w = _app.World;
            var st = w.Trading.State;
            return new TutorialFacts
            {
                Ready = st.Status == SessionStatus.Ready,
                Revealed = st.RevealedCount,
                Orders = (st.Pending != null ? st.Pending.Count : 0) + (st.Positions != null ? st.Positions.Count : 0) + (st.Closed != null ? st.Closed.Count : 0),
                Closed = st.Closed != null ? st.Closed.Count : 0,
                JournalEntries = w.Trading.Journal.Entries.Count,
                Drawings = w.Trading.Drawings.Items.Count,
                Indicators = w.Trading.Indicators.Items.Count,
                Transfers = w.Transfers.History.Count,
            };
        }

        // ---- the yellow frame around the control the current step asks you to use

        readonly List<Outline> _lit = new List<Outline>();
        static readonly Color Gold = new Color(0.95f, 0.76f, 0.24f);

        /// <summary>The controls the current step points at (only those on screen).</summary>
        List<Graphic> Targets()
        {
            var list = new List<Graphic>();
            var step = _tutorial.Status == TutorialStatus.Active ? _tutorial.Current : null;
            if (step == null || _app.Photo) return list;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return list;
            Func<string, Graphic> button = prefix =>
            {
                foreach (var b in canvas.GetComponentsInChildren<Button>(false))
                {
                    var t = b.transform.Find("Label") != null ? b.transform.Find("Label").GetComponent<Text>() : null;
                    if (t != null && t.text != null && t.text.StartsWith(prefix, StringComparison.Ordinal) && b.targetGraphic != null && b.isActiveAndEnabled) return b.targetGraphic;
                }
                return null;
            };
            Action<Graphic> add = g => { if (g != null && !list.Contains(g)) list.Add(g); };
            Func<string, Graphic> panel = name =>
            {
                foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
                    if (g.name == "Bg" && g.transform.parent != null && g.transform.parent.name == name) return g;
                return null;
            };
            bool terminal = _app.TerminalOpen;
            if (!terminal && step.Id != "transfer") { add(button("Open Terminal")); return list; }
            switch (step.Id)
            {
                case "move": add(button("Play")); add(button("Pause")); add(button("Next candle")); break;
                case "place": add(button("LONG")); add(button("SHORT")); add(button("Long")); add(button("Short")); add(button("Place order")); break;
                case "close": add(button("Play")); add(button("Pause")); break;
                case "journal": add(button("Journal")); break;
                case "tools": add(panel("Tools")); break;
                case "indicators": add(button("Indicators")); break;
                case "transfer": if (terminal) add(panel("TransferCard")); else add(button("Finances")); break;
            }
            return list;
        }

        float _nextTargets;
        List<Graphic> _wanted = new List<Graphic>();

        public void UpdateHighlightNow() { _nextTargets = 0; UpdateHighlight(); }

        void UpdateHighlight()
        {
            if (Time.unscaledTime >= _nextTargets) { _nextTargets = Time.unscaledTime + 0.3f; _wanted = Targets(); }
            var wanted = _wanted;
            for (int i = _lit.Count - 1; i >= 0; i--)
            {
                var o = _lit[i];
                if (o == null) { _lit.RemoveAt(i); continue; }
                if (!wanted.Contains(o.GetComponent<Graphic>())) { Ui.Kill(o); _lit.RemoveAt(i); }
            }
            foreach (var g in wanted)
            {
                var o = g.GetComponent<Outline>();
                if (o == null || !_lit.Contains(o)) { if (o == null) o = g.gameObject.AddComponent<Outline>(); _lit.Add(o); }
                o.effectDistance = new Vector2(2.5f, 2.5f);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
                o.effectColor = new Color(Gold.r, Gold.g, Gold.b, pulse);
            }
        }

        void Update()
        {
            UpdateHighlight();
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;
            if (_tutorial.Advance(Facts())) Save();
            Refresh();
        }

        public void Refresh()
        {
            var cur = _tutorial.Current;
            bool show = _tutorial.Status == TutorialStatus.Active && cur != null && !_app.Photo;
            _card.gameObject.SetActive(show);
            if (!show) return;
            _count.text = "FIRST TRADE  -  STEP " + (_tutorial.Step + 1) + " OF " + Tutorial.Steps.Length + (cur.Optional ? "  (optional)" : "");
            _title.text = cur.Title;
            _body.text = cur.Body;
            _skip.gameObject.SetActive(cur.Optional);
        }
    }
}
