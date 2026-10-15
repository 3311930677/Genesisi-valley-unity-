using System.Collections.Generic;
using GenesisValley.Core;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// All game audio: pooled 3D one-shots with per-key rate limiting, layered ambience (day birds / night crickets /
    /// wind / river / campfire) and a sparse music playlist with silence between tracks. The listener rides the camera
    /// focus point instead of the lens so a god-camera hears what it looks at.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class AudioDirector : MonoBehaviour
    {
        private sealed class Loop
        {
            public AudioSource Source;
            public float Target;
            public float Fade = 1.5f;
        }

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private int _next;
        private AudioSource _ui;
        private readonly Dictionary<string, Loop> _loops = new Dictionary<string, Loop>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _lastClip = new Dictionary<string, int>();
        private AudioSource _musicA, _musicB;
        private bool _musicOnA = true;
        private float _musicSilenceUntil;
        private int _musicIndex = -1;
        private readonly List<int> _musicOrder = new List<int>();
        private Transform _listener;
        public bool TitleMode;
        public bool Muffled;   // pause menu
        private float _muffle = 1f;

        public Transform Listener => _listener;

        private void Awake()
        {
            GameServices.Audio = this;
            for (int i = 0; i < 28; i++) _pool.Add(MakeSource("Sfx" + i, true));
            _ui = MakeSource("UI", false);
            _musicA = MakeSource("MusicA", false); _musicA.loop = false;
            _musicB = MakeSource("MusicB", false); _musicB.loop = false;
            var lgo = new GameObject("AudioListener");
            lgo.transform.SetParent(transform, false);
            _listener = lgo.transform;
            foreach (var l in FindObjectsOfType<AudioListener>()) l.enabled = false;
            lgo.AddComponent<AudioListener>();
        }

        private void OnDestroy() { if (GameServices.Audio == this) GameServices.Audio = null; }

        private AudioSource MakeSource(string name, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 8f;
            s.maxDistance = 110f;
            s.dopplerLevel = 0f;
            s.spread = 60f;
            return s;
        }

        private static float Master => GameServices.Settings != null ? GameServices.Settings.MasterVolume : 1f;
        private static float Sfx => Master * (GameServices.Settings != null ? GameServices.Settings.SfxVolume : 1f);
        private static float Amb => Master * (GameServices.Settings != null ? GameServices.Settings.AmbienceVolume : 1f);
        private static float Mus => Master * (GameServices.Settings != null ? GameServices.Settings.MusicVolume : 1f);

        private AudioClip Pick(string key, out float volume)
        {
            volume = 1f;
            var e = GameServices.Art.Sound(key);
            if (e == null || e.clips == null || e.clips.Length == 0) return null;
            volume = e.volume;
            int n = e.clips.Length;
            int i = Random.Range(0, n);
            if (n > 1 && _lastClip.TryGetValue(key, out int last) && last == i) i = (i + 1) % n;
            _lastClip[key] = i;
            return e.clips[i];
        }

        /// <summary>Positional one-shot. Silently ignored when the clip is missing or the sound is far away.</summary>
        public void PlayAt(string key, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (_listener == null) return;
            float d2 = (position - _listener.position).sqrMagnitude;
            if (d2 > 125f * 125f) return;
            if (_lastPlayed.TryGetValue(key, out float t) && Time.unscaledTime - t < 0.045f) return;
            var clip = Pick(key, out float baseVol);
            if (clip == null) return;
            _lastPlayed[key] = Time.unscaledTime;
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.transform.position = position;
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume * baseVol * Sfx * _muffle);
            s.clip = clip;
            s.Play();
        }

        public void PlayUI(string key, float volume = 1f, float pitch = 1f)
        {
            var clip = Pick(key, out float baseVol);
            if (clip == null) return;
            _ui.pitch = pitch;
            _ui.PlayOneShot(clip, Mathf.Clamp01(volume * baseVol * Sfx));
        }

        /// <summary>Starts/updates a looping layer; volume 0 fades it out.</summary>
        public void SetLoop(string key, float volume, Vector3? position = null, float fadeSeconds = 1.5f, float pitch = 1f)
        {
            if (!_loops.TryGetValue(key, out var loop))
            {
                var clip = Pick(key, out float baseVol);
                if (clip == null) { _loops[key] = new Loop(); return; }
                var src = MakeSource("Loop_" + key, position.HasValue);
                src.clip = clip;
                src.loop = true;
                src.volume = 0f;
                src.minDistance = 12f;
                src.maxDistance = 140f;
                src.time = Random.Range(0f, clip.length * 0.9f);
                src.Play();
                loop = new Loop { Source = src };
                _loops[key] = loop;
            }
            if (loop.Source == null) return;
            var e = GameServices.Art.Sound(key);
            loop.Target = volume * (e != null ? e.volume : 1f);
            loop.Fade = Mathf.Max(0.05f, fadeSeconds);
            loop.Source.pitch = pitch;
            if (position.HasValue) loop.Source.transform.position = position.Value;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _muffle = Mathf.MoveTowards(_muffle, Muffled ? 0.35f : 1f, dt * 2f);
            FollowCamera();
            UpdateAmbience();
            foreach (var kv in _loops)
            {
                var l = kv.Value;
                if (l.Source == null) continue;
                bool isAmb = kv.Key.StartsWith("amb_");
                float target = l.Target * (isAmb ? Amb : Sfx) * _muffle;
                l.Source.volume = Mathf.MoveTowards(l.Source.volume, target, dt / l.Fade);
            }
            UpdateMusic(dt);
        }

        private void FollowCamera()
        {
            var cam = Camera.main;
            if (cam == null || _listener == null) return;
            var rig = GameServices.CameraRig;
            Vector3 focus = rig != null ? rig.FocusPoint : cam.transform.position + cam.transform.forward * 40f;
            float dist = rig != null ? rig.FocusDistance : 40f;
            // Sit between the focus and the lens: close zoom = intimate, far zoom = distant mix.
            _listener.position = Vector3.Lerp(focus, cam.transform.position, Mathf.InverseLerp(20f, 300f, dist) * 0.55f) + Vector3.up * 2f;
            _listener.rotation = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
        }

        private void UpdateAmbience()
        {
            var sky = GameServices.Sky;
            var w = GameServices.World;
            float night = sky != null ? sky.NightFactor : 0f;
            float wind = sky != null ? sky.WindStrength : 0.5f;
            var rig = GameServices.CameraRig;
            float height = rig != null ? Mathf.InverseLerp(30f, 300f, rig.FocusDistance) : 0.3f;
            SetLoop("amb_day", (1f - night) * Mathf.Lerp(0.8f, 0.45f, height), null, 3f);
            SetLoop("amb_night", night * Mathf.Lerp(0.75f, 0.4f, height), null, 3f);
            SetLoop("amb_wind", Mathf.Lerp(0.18f, 0.6f, height) * Mathf.Lerp(0.6f, 1.2f, wind), null, 2f);
            if (w == null || TitleMode && w == null) return;
            var presenter = GameServices.Presenter;
            if (presenter != null && presenter.Water != null && _listener != null && presenter.Water.NearestWater(_listener.position, out var wp, out float edge))
            {
                var dir = wp - _listener.position;
                // Keep the emitter at the bank nearest to the listener (not mid-channel, which can be far for ponds).
                var p = _listener.position + Vector3.ClampMagnitude(new Vector3(dir.x, 0, dir.z), edge + 0.5f);
                p.y = wp.y;
                SetLoop("amb_river", Mathf.Clamp01(1.2f - edge / 70f) * 0.9f, p, 1.5f);
            }
            else SetLoop("amb_river", 0f, null, 1.5f);
            var camp = presenter != null ? presenter.Camp : null;
            if (camp != null && camp.HasCamp) SetLoop("amb_fire", Mathf.Lerp(0.5f, 1f, night), camp.FirePosition + Vector3.up, 1.5f);
            else SetLoop("amb_fire", 0f, null, 1f);
        }

        // ------------------------------------------------------------------ music

        private void UpdateMusic(float dt)
        {
            var music = GameServices.Art.music;
            var active = _musicOnA ? _musicA : _musicB;
            var other = _musicOnA ? _musicB : _musicA;
            other.volume = Mathf.MoveTowards(other.volume, 0f, dt * 0.3f);
            if (other.volume <= 0f && other.isPlaying) other.Stop();
            if (music == null || music.Length == 0) return;
            float target = Mus * 0.7f * Mathf.Lerp(1f, 0.6f, _muffle < 1f ? 1f - _muffle : 0f);
            if (active.isPlaying)
            {
                float remaining = active.clip != null ? active.clip.length - active.time : 0f;
                float fadeOut = Mathf.Clamp01(remaining / 4f);
                active.volume = Mathf.MoveTowards(active.volume, target * fadeOut, dt * 0.25f);
                return;
            }
            if (Time.unscaledTime < _musicSilenceUntil) return;
            // Next track (shuffled, no immediate repeat); title screen always opens with track 0.
            int idx;
            if (TitleMode) idx = 0;
            else
            {
                if (_musicOrder.Count == 0)
                {
                    for (int i = 0; i < music.Length; i++) if (i != _musicIndex || music.Length == 1) _musicOrder.Add(i);
                    for (int i = _musicOrder.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (_musicOrder[i], _musicOrder[j]) = (_musicOrder[j], _musicOrder[i]); }
                }
                idx = _musicOrder[0]; _musicOrder.RemoveAt(0);
            }
            _musicIndex = idx;
            var clip = music[idx];
            if (clip == null) return;
            _musicOnA = !_musicOnA;
            var src = _musicOnA ? _musicA : _musicB;
            src.clip = clip;
            src.volume = 0f;
            src.Play();
            _musicSilenceUntil = Time.unscaledTime + clip.length + Random.Range(25f, 70f);
        }

        /// <summary>Called when switching between title and game so a fresh track starts promptly.</summary>
        public void RestartMusic(float delay = 1.5f)
        {
            var a = _musicOnA ? _musicA : _musicB;
            _musicOnA = !_musicOnA;
            _musicSilenceUntil = Time.unscaledTime + delay;
            _musicOrder.Clear();
            if (a.isPlaying) { /* faded out by UpdateMusic as the "other" source */ }
        }

        // ------------------------------------------------------------------ sim events

        public void OnSimEvent(in SimEvent e)
        {
            var w = GameServices.World;
            float y = w != null ? w.Height.SampleHeight(e.X, e.Z) : 0f;
            var p = new Vector3(e.X, y + 1f, e.Z);
            switch (e.Type)
            {
                case SimEventType.Milestone: PlayUI("milestone", 0.9f); break;
                case SimEventType.BuildingCompleted: PlayUI("complete_chime", 0.55f); break;
                case SimEventType.VillagersArrived: PlayUI("summon", 0.9f); PlayAt("poof", p, 1f); break;
                case SimEventType.RiverCreated: PlayAt("river_create", p, 1f); PlayUI("water_rush", 0.6f); break;
                case SimEventType.ForestPlanted: PlayAt("forest_plant", p, 1f); break;
                case SimEventType.SitePlanned:
                case SimEventType.BridgePlanned: PlayUI("plan", 0.6f); break;
                case SimEventType.FoodShortage:
                case SimEventType.WaterShortage: PlayUI("warning", 0.7f); break;
                case SimEventType.CommandRejected:
                case SimEventType.UndoRejected: PlayUI("error", 0.7f); break;
                case SimEventType.UndoApplied: PlayUI("undo", 0.7f); break;
                case SimEventType.Saved: PlayUI("save", 0.6f); break;
                case SimEventType.FieldHarvested: PlayAt("harvest", p, 0.8f); break;
                case SimEventType.FishCaught: PlayAt("splash", p, 0.6f); break;
                case SimEventType.VillagerMovedIn: PlayAt("door_open", p, 0.6f); break;
                case SimEventType.Hint: PlayUI("hint", 0.5f); break;
            }
        }
    }
}
