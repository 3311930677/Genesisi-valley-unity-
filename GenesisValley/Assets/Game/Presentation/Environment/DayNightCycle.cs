using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Sun/moon, ambient trilight, fog, sky shader and global wind from the sim clock (GDD §7.3: one main
    /// directional light, soft ambient, night still readable). The 15:30 key is the locked "afternoon art test" look.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DayNightCycle : MonoBehaviour
    {
        private struct Key
        {
            public float Hour;
            public Color Sun; public float SunI;
            public Color Sky, Equator, Ground;
            public Color Fog; public float FogStart, FogEnd;
            public Color SkyTint; public float SkyExposure, DayBlend, Stars;
            public Color Glow; public float GlowI;
            public float Temperature, Exposure;
            public float Shadow;
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        private static readonly Key[] Keys =
        {
            new Key { Hour = 0f,   Sun = C(0.55f,0.66f,0.95f), SunI = 0.26f, Sky = C(0.16f,0.21f,0.34f), Equator = C(0.12f,0.15f,0.22f), Ground = C(0.07f,0.07f,0.09f), Fog = C(0.09f,0.12f,0.19f), FogStart = 90, FogEnd = 650, SkyTint = C(0.3f,0.35f,0.5f), SkyExposure = 0.05f, DayBlend = 0f, Stars = 1.4f, Glow = C(0.2f,0.3f,0.6f), GlowI = 0f, Temperature = -18f, Exposure = 0.35f, Shadow = 0.55f },
            new Key { Hour = 4.6f, Sun = C(0.55f,0.66f,0.95f), SunI = 0.22f, Sky = C(0.18f,0.23f,0.36f), Equator = C(0.14f,0.17f,0.25f), Ground = C(0.08f,0.08f,0.1f), Fog = C(0.12f,0.15f,0.22f), FogStart = 90, FogEnd = 650, SkyTint = C(0.35f,0.4f,0.55f), SkyExposure = 0.08f, DayBlend = 0.02f, Stars = 1.1f, Glow = C(0.45f,0.35f,0.55f), GlowI = 0.15f, Temperature = -14f, Exposure = 0.3f, Shadow = 0.5f },
            new Key { Hour = 5.8f, Sun = C(1.0f,0.58f,0.38f), SunI = 0.55f, Sky = C(0.5f,0.48f,0.6f), Equator = C(0.62f,0.5f,0.46f), Ground = C(0.22f,0.19f,0.18f), Fog = C(0.72f,0.6f,0.58f), FogStart = 120, FogEnd = 760, SkyTint = C(1.0f,0.8f,0.75f), SkyExposure = 0.45f, DayBlend = 0.55f, Stars = 0.2f, Glow = C(1.0f,0.5f,0.32f), GlowI = 1.1f, Temperature = 8f, Exposure = 0.15f, Shadow = 0.7f },
            new Key { Hour = 7.2f, Sun = C(1.0f,0.82f,0.62f), SunI = 1.05f, Sky = C(0.6f,0.66f,0.78f), Equator = C(0.66f,0.62f,0.55f), Ground = C(0.32f,0.29f,0.24f), Fog = C(0.8f,0.8f,0.8f), FogStart = 160, FogEnd = 880, SkyTint = C(1.0f,0.95f,0.9f), SkyExposure = 0.85f, DayBlend = 1f, Stars = 0f, Glow = C(1.0f,0.7f,0.45f), GlowI = 0.35f, Temperature = 6f, Exposure = 0.05f, Shadow = 0.8f },
            new Key { Hour = 10f,  Sun = C(1.0f,0.95f,0.87f), SunI = 1.3f, Sky = C(0.66f,0.73f,0.82f), Equator = C(0.63f,0.64f,0.58f), Ground = C(0.36f,0.33f,0.27f), Fog = C(0.78f,0.83f,0.87f), FogStart = 220, FogEnd = 1000, SkyTint = C(1f,1f,1f), SkyExposure = 1f, DayBlend = 1f, Stars = 0f, Glow = C(1f,0.8f,0.6f), GlowI = 0f, Temperature = 0f, Exposure = 0f, Shadow = 0.85f },
            new Key { Hour = 15.5f, Sun = C(1.0f,0.93f,0.82f), SunI = 1.25f, Sky = C(0.66f,0.72f,0.80f), Equator = C(0.62f,0.62f,0.55f), Ground = C(0.36f,0.33f,0.27f), Fog = C(0.78f,0.82f,0.84f), FogStart = 220, FogEnd = 950, SkyTint = C(1f,0.98f,0.95f), SkyExposure = 1f, DayBlend = 1f, Stars = 0f, Glow = C(1f,0.75f,0.5f), GlowI = 0.05f, Temperature = 3f, Exposure = 0f, Shadow = 0.85f },
            new Key { Hour = 17.8f, Sun = C(1.0f,0.78f,0.52f), SunI = 1.1f, Sky = C(0.62f,0.62f,0.72f), Equator = C(0.7f,0.58f,0.48f), Ground = C(0.33f,0.28f,0.22f), Fog = C(0.84f,0.74f,0.64f), FogStart = 180, FogEnd = 880, SkyTint = C(1f,0.88f,0.75f), SkyExposure = 0.9f, DayBlend = 1f, Stars = 0f, Glow = C(1f,0.6f,0.32f), GlowI = 0.6f, Temperature = 14f, Exposure = 0.05f, Shadow = 0.8f },
            new Key { Hour = 19.1f, Sun = C(1.0f,0.5f,0.3f), SunI = 0.55f, Sky = C(0.45f,0.4f,0.55f), Equator = C(0.6f,0.42f,0.38f), Ground = C(0.2f,0.16f,0.15f), Fog = C(0.66f,0.48f,0.45f), FogStart = 140, FogEnd = 760, SkyTint = C(1f,0.7f,0.6f), SkyExposure = 0.5f, DayBlend = 0.6f, Stars = 0.15f, Glow = C(1f,0.42f,0.22f), GlowI = 1.25f, Temperature = 18f, Exposure = 0.15f, Shadow = 0.7f },
            new Key { Hour = 20.3f, Sun = C(0.5f,0.55f,0.85f), SunI = 0.2f, Sky = C(0.22f,0.25f,0.4f), Equator = C(0.2f,0.2f,0.3f), Ground = C(0.09f,0.09f,0.11f), Fog = C(0.16f,0.18f,0.28f), FogStart = 100, FogEnd = 680, SkyTint = C(0.5f,0.5f,0.7f), SkyExposure = 0.12f, DayBlend = 0.08f, Stars = 0.9f, Glow = C(0.6f,0.35f,0.45f), GlowI = 0.3f, Temperature = -10f, Exposure = 0.3f, Shadow = 0.55f },
            new Key { Hour = 21.5f, Sun = C(0.55f,0.66f,0.95f), SunI = 0.26f, Sky = C(0.16f,0.21f,0.34f), Equator = C(0.12f,0.15f,0.22f), Ground = C(0.07f,0.07f,0.09f), Fog = C(0.09f,0.12f,0.19f), FogStart = 90, FogEnd = 650, SkyTint = C(0.3f,0.35f,0.5f), SkyExposure = 0.05f, DayBlend = 0f, Stars = 1.4f, Glow = C(0.2f,0.3f,0.6f), GlowI = 0f, Temperature = -18f, Exposure = 0.35f, Shadow = 0.55f },
            new Key { Hour = 24f,  Sun = C(0.55f,0.66f,0.95f), SunI = 0.26f, Sky = C(0.16f,0.21f,0.34f), Equator = C(0.12f,0.15f,0.22f), Ground = C(0.07f,0.07f,0.09f), Fog = C(0.09f,0.12f,0.19f), FogStart = 90, FogEnd = 650, SkyTint = C(0.3f,0.35f,0.5f), SkyExposure = 0.05f, DayBlend = 0f, Stars = 1.4f, Glow = C(0.2f,0.3f,0.6f), GlowI = 0f, Temperature = -18f, Exposure = 0.35f, Shadow = 0.55f },
        };

        public const float Sunrise = 5.6f, Sunset = 19.6f;
        public float AzimuthOffset = -35f;
        public float MaxSunElevation = 58f;

        /// <summary>When ≥ 0, overrides the sim clock (title screen, photo mode).</summary>
        public float HourOverride = -1f;
        public float ExposureBias;

        public Light Sun { get; private set; }
        public float Hour { get; private set; } = 15.5f;
        public float NightFactor { get; private set; }
        public float WindStrength { get; private set; } = 0.6f;
        public Vector3 WindDirection { get; private set; } = new Vector3(0.8f, 0, 0.6f);
        public Vector3 SunDirection { get; private set; }
        public float WhiteBalanceTemperature { get; private set; }
        public float ExposureOffset { get; private set; }

        private Material _sky;
        private float _skyRotation;
        private float _windAngle = 37f;
        private float _windTime;

        private static readonly int WindDirId = Shader.PropertyToID("_GV_WindDir");
        private static readonly int WindParamsId = Shader.PropertyToID("_GV_WindParams");
        private static readonly int AmbientId = Shader.PropertyToID("_GV_AmbientLight");

        private void Awake()
        {
            GameServices.Sky = this;
            EnsureSun();
            EnsureSky();
            Apply(Hour, 0f);
        }

        private void OnDestroy() { if (GameServices.Sky == this) GameServices.Sky = null; }

        private void EnsureSun()
        {
            if (Sun != null) return;
            foreach (var l in FindObjectsOfType<Light>()) if (l.type == LightType.Directional) { Sun = l; break; }
            if (Sun == null)
            {
                var go = new GameObject("Sun");
                Sun = go.AddComponent<Light>();
                Sun.type = LightType.Directional;
            }
            Sun.shadows = LightShadows.Soft;
            Sun.shadowBias = 0.04f;
            Sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = Sun;
        }

        private void EnsureSky()
        {
            var art = GameServices.Art;
            if (art.skyMaterial != null) _sky = art.skyMaterial;
            else if (art.skyShader != null || Shader.Find("GenesisValley/Sky") != null)
            {
                _sky = new Material(art.skyShader != null ? art.skyShader : Shader.Find("GenesisValley/Sky")) { name = "GV_Sky_Runtime" };
                var tex = art.Texture("Sky_Day");
                if (tex != null) _sky.SetTexture("_DayTex", tex);
            }
            if (_sky != null) RenderSettings.skybox = _sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var w = GameServices.World;
            float hour = HourOverride >= 0f ? HourOverride : (w != null ? w.Hour : Hour);
            Hour = hour;
            Apply(hour, dt);
        }

        private void Apply(float hour, float dt)
        {
            EnsureSun();
            var k = Sample(hour);

            // --- sun / moon direction
            bool day = hour > Sunrise - 0.3f && hour < Sunset + 0.3f;
            float f = Mathf.InverseLerp(Sunrise, Sunset, hour);
            float sunElev = Mathf.Sin(Mathf.Clamp01(f) * Mathf.PI) * MaxSunElevation;
            float sunAz = Mathf.Lerp(-105f, 105f, f) + AzimuthOffset;
            var sunDir = Quaternion.Euler(Mathf.Max(sunElev, -8f), sunAz, 0) * Vector3.back;
            // Moon: opposite arc across the night.
            float nightF = hour >= Sunset ? (hour - Sunset) / (24f - Sunset + Sunrise) : (hour + 24f - Sunset) / (24f - Sunset + Sunrise);
            float moonElev = Mathf.Sin(Mathf.Clamp01(nightF) * Mathf.PI) * 48f;
            float moonAz = Mathf.Lerp(-95f, 95f, nightF) + AzimuthOffset + 180f;
            var moonDir = Quaternion.Euler(Mathf.Max(moonElev, 10f), moonAz, 0) * Vector3.back;
            SunDirection = sunDir;

            // Cross-fade sun → moon around the horizon so shadows never swing violently.
            float sunUp = Mathf.Clamp01((sunElev + 1f) / 7f) * (day ? 1f : 0f);
            float moonUp = 1f - Mathf.Clamp01((sunElev + 3f) / 6f);
            if (!day) moonUp = 1f;
            bool useSun = sunUp >= moonUp * 0.5f && day;
            var lightDir = useSun ? sunDir : moonDir;
            float intensity = useSun ? k.SunI * Mathf.Lerp(0.15f, 1f, sunUp) : k.SunI * Mathf.Clamp01(moonUp);
            Sun.transform.rotation = Quaternion.LookRotation(-lightDir);
            Sun.color = useSun ? k.Sun : Color.Lerp(k.Sun, C(0.6f, 0.7f, 1f), 0.6f);
            Sun.intensity = intensity;
            Sun.shadowStrength = k.Shadow;

            NightFactor = 1f - Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.75f, k.DayBlend));

            // --- ambient & fog
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = k.Sky;
            RenderSettings.ambientEquatorColor = k.Equator;
            RenderSettings.ambientGroundColor = k.Ground;
            RenderSettings.fogColor = k.Fog;
            RenderSettings.fogStartDistance = k.FogStart;
            RenderSettings.fogEndDistance = k.FogEnd;
            Shader.SetGlobalVector(AmbientId, new Vector4(
                Mathf.Max(k.Equator.r, Sun.color.r * intensity * 0.6f),
                Mathf.Max(k.Equator.g, Sun.color.g * intensity * 0.6f),
                Mathf.Max(k.Equator.b, Sun.color.b * intensity * 0.6f), 1f));
            WhiteBalanceTemperature = k.Temperature;
            ExposureOffset = k.Exposure + ExposureBias;

            // --- sky
            if (_sky != null)
            {
                _skyRotation = (_skyRotation + dt * 0.25f) % 360f;
                _sky.SetFloat("_Rotation", _skyRotation);
                _sky.SetFloat("_DayExposure", k.SkyExposure);
                _sky.SetColor("_DayTint", k.SkyTint);
                _sky.SetFloat("_DayBlend", k.DayBlend);
                _sky.SetFloat("_StarIntensity", k.Stars);
                _sky.SetVector("_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0));
                _sky.SetVector("_MoonDir", new Vector4(moonDir.x, moonDir.y, moonDir.z, 0));
                _sky.SetColor("_SunColor", k.Sun * Mathf.Lerp(0.4f, 1.6f, sunUp));
                _sky.SetColor("_GlowColor", k.Glow);
                _sky.SetFloat("_GlowStrength", k.GlowI);
                _sky.SetColor("_GroundColor", Color.Lerp(k.Fog, k.Ground, 0.3f));
            }

            // --- wind: slow direction drift + gust noise
            _windTime += dt;
            _windAngle += dt * (Noise.Value(_windTime * 0.01f, 3.1f) - 0.5f) * 4f;
            WindDirection = Quaternion.Euler(0, _windAngle, 0) * Vector3.forward;
            float gust = Noise.Fbm(_windTime * 0.05f, 7.7f, 2);
            WindStrength = Mathf.Lerp(0.35f, 1.05f, gust) * (NightFactor > 0.5f ? 0.75f : 1f);
            Shader.SetGlobalVector(WindDirId, new Vector4(WindDirection.x, 0, WindDirection.z, 0));
            Shader.SetGlobalVector(WindParamsId, new Vector4(WindStrength, 1f, 0.35f + 0.4f * gust, _windTime));
        }

        private static Key Sample(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int i = 0;
            while (i < Keys.Length - 2 && Keys[i + 1].Hour < hour) i++;
            var a = Keys[i]; var b = Keys[i + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.Hour, b.Hour, hour));
            return new Key
            {
                Hour = hour,
                Sun = Color.Lerp(a.Sun, b.Sun, t), SunI = Mathf.Lerp(a.SunI, b.SunI, t),
                Sky = Color.Lerp(a.Sky, b.Sky, t), Equator = Color.Lerp(a.Equator, b.Equator, t), Ground = Color.Lerp(a.Ground, b.Ground, t),
                Fog = Color.Lerp(a.Fog, b.Fog, t), FogStart = Mathf.Lerp(a.FogStart, b.FogStart, t), FogEnd = Mathf.Lerp(a.FogEnd, b.FogEnd, t),
                SkyTint = Color.Lerp(a.SkyTint, b.SkyTint, t), SkyExposure = Mathf.Lerp(a.SkyExposure, b.SkyExposure, t),
                DayBlend = Mathf.Lerp(a.DayBlend, b.DayBlend, t), Stars = Mathf.Lerp(a.Stars, b.Stars, t),
                Glow = Color.Lerp(a.Glow, b.Glow, t), GlowI = Mathf.Lerp(a.GlowI, b.GlowI, t),
                Temperature = Mathf.Lerp(a.Temperature, b.Temperature, t), Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t),
                Shadow = Mathf.Lerp(a.Shadow, b.Shadow, t),
            };
        }

        /// <summary>Is it "lamp time" in the village?</summary>
        public bool IsDark => NightFactor > 0.5f;
    }
}
