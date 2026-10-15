using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    public enum FxKind { Dust, Chips, Leaves, Sparkle, Splash, Harvest, Poof, Rumble, Water }

    /// <summary>
    /// Code-built particle effects (no prefab dependencies). One-shot effects share one world-space system per kind
    /// that is moved and Emit()ed; persistent effects (chimney smoke, campfire) are cloned per owner.
    /// </summary>
    public sealed class FxLibrary : MonoBehaviour
    {
        private readonly Dictionary<FxKind, ParticleSystem> _oneShots = new Dictionary<FxKind, ParticleSystem>();
        private ParticleSystem _smokeTemplate, _fireTemplate, _emberTemplate, _beamTemplate;
        private Texture _dot, _smoke, _spark, _leaf, _chip, _flame;
        private bool _built;

        private void Awake()
        {
            GameServices.Fx = this;
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            try { Build(); }
            catch (System.Exception e)
            {
                Debug.LogError("[GenesisValley] 粒子特效初始化失败，特效将被跳过：" + e.Message);
                Debug.LogException(e);
            }
        }

        private void Build()
        {
            var art = GameServices.Art;
            _dot = art.Texture("FX_SoftDot") ?? MakeDot();
            _smoke = art.Texture("FX_Smoke") ?? _dot;
            _spark = art.Texture("FX_Spark") ?? _dot;
            _leaf = art.Texture("FX_Leaf") ?? _dot;
            _chip = art.Texture("FX_Chip") ?? _dot;
            _flame = art.Texture("FX_Flame") ?? _dot;

            _oneShots[FxKind.Dust] = Make("Dust", _smoke, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); m.startColor = new Color(0.72f, 0.63f, 0.5f, 0.55f); m.gravityModifier = -0.02f;
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.6f;
                Size(ps, 0.6f, 1.8f); Fade(ps, 0.1f, 1f);
                var v = ps.limitVelocityOverLifetime; v.enabled = true; v.dampen = 0.25f; v.limit = 0.4f;
            });
            _oneShots[FxKind.Poof] = Make("Poof", _smoke, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.startColor = new Color(0.95f, 0.93f, 0.88f, 0.7f);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
                Size(ps, 0.7f, 1.6f); Fade(ps, 0.05f, 1f);
                var v = ps.limitVelocityOverLifetime; v.enabled = true; v.dampen = 0.4f; v.limit = 0.3f;
            });
            _oneShots[FxKind.Chips] = Make("Chips", _chip, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f); m.gravityModifier = 1.4f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.7f, 0.48f), new Color(0.62f, 0.47f, 0.3f));
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = 0.05f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            });
            _oneShots[FxKind.Leaves] = Make("Leaves", _leaf, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f); m.gravityModifier = 0.12f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.56f, 0.24f), new Color(0.75f, 0.68f, 0.28f));
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.2f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 0.6f;
                var v = ps.limitVelocityOverLifetime; v.enabled = true; v.dampen = 0.15f; v.limit = 0.8f;
                Fade(ps, 0.02f, 0.75f);
            });
            _oneShots[FxKind.Harvest] = Make("Harvest", _leaf, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f); m.gravityModifier = 0.25f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.8f, 0.42f), new Color(0.8f, 0.62f, 0.28f));
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, 0.3f, 4f);
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
                Fade(ps, 0.05f, 0.7f);
            });
            _oneShots[FxKind.Sparkle] = Make("Sparkle", _spark, true, 0f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); m.gravityModifier = -0.15f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.86f, 0.45f, 1f), new Color(1f, 0.97f, 0.8f, 1f));
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.5f;
                Size(ps, 1f, 0.2f); Fade(ps, 0.1f, 0.6f);
                var n = ps.noise; n.enabled = true; n.strength = 0.4f; n.frequency = 1.2f;
            });
            _oneShots[FxKind.Splash] = Make("Splash", _dot, false, 0.8f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); m.gravityModifier = 1.2f;
                m.startColor = new Color(0.88f, 0.95f, 1f, 0.85f);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25; sh.radius = 0.15f;
                sh.rotation = new Vector3(-90, 0, 0);
            });
            _oneShots[FxKind.Water] = Make("WaterMist", _smoke, false, 0.8f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
                m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); m.startColor = new Color(0.85f, 0.93f, 1f, 0.35f);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.2f;
                Size(ps, 0.6f, 1.6f); Fade(ps, 0.15f, 1f);
            });
            _oneShots[FxKind.Rumble] = Make("Rumble", _smoke, false, 1f, ps =>
            {
                var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f); m.startColor = new Color(0.62f, 0.55f, 0.44f, 0.35f);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.rotation = new Vector3(-90, 0, 0);
                Size(ps, 0.5f, 1.5f); Fade(ps, 0.2f, 1f);
            });

            _smokeTemplate = Make("ChimneySmoke", _smoke, false, 1f, ps =>
            {
                var m = ps.main; m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); m.startColor = new Color(0.78f, 0.77f, 0.76f, 0.32f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); m.maxParticles = 60;
                var e = ps.emission; e.rateOverTime = 2.6f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8; sh.radius = 0.15f; sh.rotation = new Vector3(-90, 0, 0);
                Size(ps, 0.6f, 4.2f); Fade(ps, 0.12f, 1f);
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                var n = ps.noise; n.enabled = true; n.strength = 0.25f; n.frequency = 0.25f;
            }, true);
            _fireTemplate = Make("Fire", _flame, true, 0f, ps =>
            {
                var m = ps.main; m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.85f); m.maxParticles = 80;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.22f, 0.9f), new Color(1f, 0.82f, 0.4f, 0.9f));
                var e = ps.emission; e.rateOverTime = 34f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6; sh.radius = 0.32f; sh.rotation = new Vector3(-90, 0, 0);
                Size(ps, 1f, 0.15f);
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.5f), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
                col.color = g;
            }, true);
            _emberTemplate = Make("Embers", _dot, true, 0f, ps =>
            {
                var m = ps.main; m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f); m.maxParticles = 40; m.startColor = new Color(1f, 0.6f, 0.25f, 1f);
                var e = ps.emission; e.rateOverTime = 7f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15; sh.radius = 0.3f; sh.rotation = new Vector3(-90, 0, 0);
                var n = ps.noise; n.enabled = true; n.strength = 0.8f; n.frequency = 1.5f;
                Fade(ps, 0.05f, 0.7f);
            }, true);
            _beamTemplate = Make("Beam", _dot, true, 0f, ps =>
            {
                var m = ps.main; m.loop = false; m.duration = 2.5f; m.startLifetime = 1.8f; m.startSpeed = 0f;
                m.startSize3D = true; m.startSizeX = 3.2f; m.startSizeY = 26f; m.startSizeZ = 1f; m.startColor = new Color(1f, 0.9f, 0.6f, 0.55f);
                var e = ps.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.3f, 1) });
                var sh = ps.shape; sh.enabled = false;
                var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                Fade(ps, 0.2f, 0.5f);
            }, true);
        }

        // ------------------------------------------------------------------ API

        public void Play(FxKind kind, Vector3 position, int count = -1, float scale = 1f)
        {
            EnsureBuilt();
            if (!_oneShots.TryGetValue(kind, out var ps) || ps == null) return;
            if (GameServices.Settings != null && GameServices.Settings.ReducedEffects) count = count < 0 ? 4 : Mathf.Max(1, count / 2);
            ps.transform.position = position;
            ps.transform.localScale = Vector3.one * scale;
            int n = count >= 0 ? count : DefaultCount(kind);
            ps.Emit(n);
        }

        /// <summary>A 3D sound-less "god light" pillar for summoning, plus sparkles.</summary>
        public void SummonBeam(Vector3 position)
        {
            EnsureBuilt();
            if (_beamTemplate == null) return;
            var beam = Instantiate(_beamTemplate, position + Vector3.up * 11f, Quaternion.identity, transform);
            beam.gameObject.SetActive(true);
            beam.Play();
            Destroy(beam.gameObject, 4f);
            Play(FxKind.Sparkle, position + Vector3.up, 40, 1.2f);
        }

        public ParticleSystem CreateChimneySmoke(Transform parent, Vector3 localPos) => Clone(_smokeTemplate, parent, localPos);
        public ParticleSystem CreateFire(Transform parent, Vector3 localPos) => Clone(_fireTemplate, parent, localPos);
        public ParticleSystem CreateEmbers(Transform parent, Vector3 localPos) => Clone(_emberTemplate, parent, localPos);

        private ParticleSystem Clone(ParticleSystem template, Transform parent, Vector3 localPos)
        {
            EnsureBuilt();
            if (template == null || parent == null) return null;
            var ps = Instantiate(template, parent, false);
            ps.transform.localPosition = localPos;
            ps.transform.localRotation = Quaternion.identity;
            ps.gameObject.SetActive(true);
            ps.Play();
            return ps;
        }

        private static int DefaultCount(FxKind k)
        {
            switch (k)
            {
                case FxKind.Dust: return 8;
                case FxKind.Poof: return 10;
                case FxKind.Chips: return 7;
                case FxKind.Leaves: return 26;
                case FxKind.Harvest: return 30;
                case FxKind.Sparkle: return 28;
                case FxKind.Splash: return 14;
                case FxKind.Water: return 10;
                case FxKind.Rumble: return 5;
                default: return 8;
            }
        }

        // ------------------------------------------------------------------ construction helpers

        private ParticleSystem Make(string name, Texture tex, bool additive, float lit, System.Action<ParticleSystem> setup, bool template = false)
        {
            var go = new GameObject("FX_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.playOnAwake = false;
            m.loop = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            m.maxParticles = 400;
            var e = ps.emission; e.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.Particle(tex, additive, lit);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = additive ? -2 : 0;
            setup(ps);
            if (template) go.SetActive(false);
            return ps;
        }

        private static void Size(ParticleSystem ps, float from, float to)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, from, 1, to));
        }

        private static void Fade(ParticleSystem ps, float fadeIn, float fadeOutStart)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, Mathf.Max(0.01f, fadeIn)), new GradientAlphaKey(1, fadeOutStart), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        private static Texture MakeDot()
        {
            const int s = 32;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = "GV_Dot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); a *= a;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            return t;
        }
    }
}
