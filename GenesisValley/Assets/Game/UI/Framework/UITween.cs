using System;
using System.Collections.Generic;
using UnityEngine;

namespace GenesisValley.UI
{
    /// <summary>Tiny unscaled-time tween runner for UI (fades, pops, slides). Keyed so a new tween replaces the old one.</summary>
    public sealed class UITween : MonoBehaviour
    {
        private sealed class Tween
        {
            public object Key;
            public float T, Duration, Delay;
            public Action<float> Apply;
            public Func<float, float> Ease;
            public Action Done;
        }

        private static UITween _instance;
        private readonly List<Tween> _tweens = new List<Tween>();
        private readonly Dictionary<object, Tween> _byKey = new Dictionary<object, Tween>();

        public static UITween Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("UITween");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<UITween>();
                }
                return _instance;
            }
        }

        public static float OutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        public static float OutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        public static float Linear(float t) => t;

        public static void Run(object key, float duration, Action<float> apply, Func<float, float> ease = null, Action done = null, float delay = 0f)
        {
            var inst = Instance;
            if (key != null && inst._byKey.TryGetValue(key, out var old)) { inst._tweens.Remove(old); inst._byKey.Remove(key); }
            var tw = new Tween { Key = key, Duration = Mathf.Max(0.0001f, duration), Apply = apply, Ease = ease ?? OutCubic, Done = done, Delay = delay };
            inst._tweens.Add(tw);
            if (key != null) inst._byKey[key] = tw;
            if (delay <= 0f) apply(tw.Ease(0f));
        }

        public static void Kill(object key)
        {
            if (_instance == null || key == null) return;
            if (_instance._byKey.TryGetValue(key, out var tw)) { _instance._tweens.Remove(tw); _instance._byKey.Remove(key); }
        }

        public static void Fade(CanvasGroup g, float to, float duration, Action done = null, float delay = 0f)
        {
            if (g == null) return;
            float from = g.alpha;
            Run(g, duration, t => { if (g != null) g.alpha = Mathf.LerpUnclamped(from, to, t); }, OutCubic, done, delay);
        }

        public static void Scale(Transform tr, Vector3 from, Vector3 to, float duration, Func<float, float> ease = null, float delay = 0f)
        {
            if (tr == null) return;
            Run(tr, duration, t => { if (tr != null) tr.localScale = Vector3.LerpUnclamped(from, to, t); }, ease ?? OutBack, null, delay);
        }

        public static void Move(RectTransform rt, Vector2 to, float duration, Func<float, float> ease = null, Action done = null, float delay = 0f)
        {
            if (rt == null) return;
            Vector2 from = rt.anchoredPosition;
            Run(rt, duration, t => { if (rt != null) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, t); }, ease ?? OutCubic, done, delay);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                if (i >= _tweens.Count) continue;
                var tw = _tweens[i];
                if (tw.Delay > 0f) { tw.Delay -= dt; if (tw.Delay > 0f) continue; }
                tw.T = Mathf.Min(1f, tw.T + dt / tw.Duration);
                try { tw.Apply(tw.Ease(tw.T)); }
                catch (Exception e) { Debug.LogException(e); tw.T = 1f; }
                if (tw.T >= 1f)
                {
                    _tweens.Remove(tw);
                    if (tw.Key != null && _byKey.TryGetValue(tw.Key, out var cur) && cur == tw) _byKey.Remove(tw.Key);
                    tw.Done?.Invoke();
                }
            }
        }
    }
}
