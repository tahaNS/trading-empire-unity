using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// Plays the game's sounds. Every sound is synthesised from the recipes in Core (no audio files): the clips are built the first time
    /// they are needed. A soft city hum (filtered brown noise) grows with the city and follows the market mood.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public const float MasterGain = 0.4f;
        const string MuteKey = "te.muted";

        public static AudioManager Instance { get; private set; }

        AudioSource _sfx, _hum;
        AudioLowPassFilter _filter;
        readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();
        float _humLevel, _humBright = 0.5f;
        float _lastCoin = -10f;
        readonly Dictionary<object, int> _noticeSeen = new Dictionary<object, int>();
        int _cityTx, _transfers;

        public bool Muted { get; private set; }
        /// <summary>Counts every sound requested while not muted (also useful for tests, where there may be no audio device).</summary>
        public int Played { get; private set; }
        public SoundId? LastPlayed { get; private set; }

        public static AudioManager Create(GameWorld world)
        {
            var go = new GameObject("Audio");
            var a = go.AddComponent<AudioManager>();
            a.Muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;
            a._sfx = go.AddComponent<AudioSource>();
            a._sfx.playOnAwake = false;
            a._sfx.volume = MasterGain;
            a._hum = go.AddComponent<AudioSource>();
            a._hum.playOnAwake = false;
            a._hum.loop = true;
            a._hum.volume = 0f;
            a._filter = go.AddComponent<AudioLowPassFilter>();
            a.Bind(world);
            Instance = a;
            return a;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------------------------------------------------------- playback

        AudioClip ClipFor(SoundId id)
        {
            if (!_clips.TryGetValue(id, out var clip))
            {
                var data = SoundSynth.Render(Sounds.Recipes[id]);
                clip = AudioClip.Create("sfx-" + id, data.Length, 1, SoundSynth.SampleRate, false);
                clip.SetData(data, 0);
                _clips[id] = clip;
            }
            return clip;
        }

        public void Play(SoundId id)
        {
            if (Muted) return;
            Played++;
            LastPlayed = id;
            if (_sfx != null) _sfx.PlayOneShot(ClipFor(id));
        }

        /// <summary>The UI's click sound (safe to call when there is no audio manager, e.g. in tests).</summary>
        public static void Click() { if (Instance != null) Instance.Play(SoundId.Click); }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyHum();
        }

        /// <summary>A soft city hum: filtered noise that grows with the size of the city. Both values are 0..1 (bright is the mood).</summary>
        public void SetAmbient(float level, float bright)
        {
            _humLevel = Mathf.Clamp01(level);
            _humBright = Mathf.Clamp01(bright);
            ApplyHum();
        }

        void ApplyHum()
        {
            if (_hum == null) return;
            if (_hum.clip == null)
            {
                var noise = SoundSynth.BrownNoise();
                var clip = AudioClip.Create("hum", noise.Length, 1, SoundSynth.SampleRate, false);
                clip.SetData(noise, 0);
                _hum.clip = clip;
            }
            if (!_hum.isPlaying && !Muted && _humLevel > 0) _hum.Play();
            _hum.volume = Muted ? 0f : _humLevel * 0.16f * MasterGain / 0.4f;
            _filter.cutoffFrequency = 250f + _humBright * 700f;
        }

        // ---------------------------------------------------------------- game events

        void Bind(GameWorld world)
        {
            _noticeSeen[world.City] = world.City.State.Notice != null ? world.City.State.Notice.Seq : 0;
            _cityTx = world.City.State.Transactions.Count;
            _transfers = world.Transfers.History.Count;
            world.City.Changed += () =>
            {
                var s = world.City.State;
                if (s.Notice != null && s.Notice.Seq != _noticeSeen[world.City]) { _noticeSeen[world.City] = s.Notice.Seq; PlayForNotice(s.Notice); }
                if (s.Transactions.Count != _cityTx)
                {
                    bool income = s.Transactions.Skip(_cityTx).Any(t => t.Kind == TransactionKind.Income);
                    _cityTx = s.Transactions.Count;
                    if (income && Time.unscaledTime - _lastCoin > 0.4f) { _lastCoin = Time.unscaledTime; Play(SoundId.Coin); }
                }
                UpdateHum(world, false);
            };
            world.Transfers.Changed += () =>
            {
                int n = world.Transfers.History.Count;
                if (n > _transfers) Play(SoundId.Coin);
                _transfers = n;
            };
            Watch(world.Trading);
        }

        /// <summary>Play sounds for the notices of a trading session (the game's own, or a challenge run).</summary>
        public void Watch(TradingSession session)
        {
            _noticeSeen[session] = session.State.Notice != null ? session.State.Notice.Seq : 0;
            session.Changed += () =>
            {
                var n = session.State.Notice;
                if (n != null && n.Seq != _noticeSeen[session]) { _noticeSeen[session] = n.Seq; PlayForNotice(n); }
            };
        }

        void PlayForNotice(Notice n)
        {
            var id = Sounds.ForNotice(n.Kind, n.Text);
            if (id.HasValue) Play(id.Value);
        }

        /// <summary>The hum follows the city's size and mood; it is silent while the terminal is open.</summary>
        public void UpdateHum(GameWorld world, bool terminalOpen)
        {
            var s = world.City.State;
            SetAmbient(terminalOpen ? 0f : Mathf.Min(s.Buildings.Count / 30f, 1f) * 0.8f + 0.1f, s.Mood == Mood.Booming ? 0.9f : s.Mood == Mood.Panic ? 0.05f : 0.5f);
        }
    }
}
