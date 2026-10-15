using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Runtime URP post stack (no asset dependency): ACES tonemapping, restrained bloom for lamps/fire, exposure and
    /// white balance from the day cycle, vignette, and an optional tilt-shift depth of field that keeps the camera
    /// focus in focus (GDD §7.3: no heavy bloom to hide material differences).
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class PostFxController : MonoBehaviour
    {
        private Volume _volume;
        private VolumeProfile _profile;
        private Bloom _bloom;
        private ColorAdjustments _color;
        private Vignette _vignette;
        private WhiteBalance _white;
        private DepthOfField _dof;
        private Tonemapping _tone;

        /// <summary>Extra exposure in stops (photo mode).</summary>
        public float PhotoExposure;
        /// <summary>Photo mode overrides (UI PhotoMode panel). Ignored while <see cref="PhotoMode"/> is false.</summary>
        public bool PhotoMode;
        public float PhotoBlur = 0.5f;        // 0 = sharp .. 1 = strong miniature
        public float PhotoVignette = 0.25f;
        public float PhotoSaturation;         // added to base saturation
        public float PhotoWarmth;             // added to white balance temperature
        public float PhotoContrast;
        public float BaseSaturation = 8f;
        public float BaseContrast = 6f;

        private void Awake()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "GV_RuntimePost";
            _tone = _profile.Add<Tonemapping>(true);
            _tone.mode.Override(TonemappingMode.ACES);
            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.Override(1.05f);
            _bloom.intensity.Override(0.32f);
            _bloom.scatter.Override(0.62f);
            _bloom.tint.Override(new Color(1f, 0.93f, 0.85f));
            _color = _profile.Add<ColorAdjustments>(true);
            _color.postExposure.Override(0.35f);
            _color.contrast.Override(BaseContrast);
            _color.saturation.Override(BaseSaturation);
            _color.colorFilter.Override(Color.white);
            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.2f);
            _vignette.smoothness.Override(0.45f);
            _vignette.color.Override(new Color(0.08f, 0.06f, 0.05f));
            _white = _profile.Add<WhiteBalance>(true);
            _white.temperature.Override(0f);
            _white.tint.Override(0f);
            _dof = _profile.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focusDistance.Override(60f);
            _dof.aperture.Override(9f);
            _dof.focalLength.Override(60f);
            _dof.bladeCount.Override(6);

            var go = new GameObject("PostVolume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.sharedProfile = _profile;
        }

        private void Start() => ApplyCameraSettings();

        public void ApplyCameraSettings()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.requiresDepthTexture = true;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool msaa = asset != null && asset.msaaSampleCount > 1;
            data.antialiasing = msaa ? AntialiasingMode.None : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var s = GameServices.Settings;
            if (asset != null && s != null) asset.shadowDistance = Mathf.Clamp(s.ShadowDistance, 40f, 300f);
        }

        private void LateUpdate()
        {
            var sky = GameServices.Sky;
            var s = GameServices.Settings;
            float exposure = 0.35f + (sky != null ? sky.ExposureOffset : 0f) + PhotoExposure;
            _color.postExposure.value = exposure;
            float night = sky != null ? sky.NightFactor : 0f;
            _color.saturation.value = Mathf.Lerp(BaseSaturation, -12f, night * 0.7f) + (PhotoMode ? PhotoSaturation : 0f);
            _color.contrast.value = BaseContrast + (PhotoMode ? PhotoContrast : 0f);
            _color.colorFilter.value = Color.Lerp(Color.white, new Color(0.86f, 0.9f, 1.05f), night * 0.6f);
            _white.temperature.value = (sky != null ? sky.WhiteBalanceTemperature : 0f) + (PhotoMode ? PhotoWarmth : 0f);
            _bloom.intensity.value = Mathf.Lerp(0.28f, 0.55f, night);
            _vignette.intensity.value = PhotoMode ? PhotoVignette : Mathf.Lerp(0.18f, 0.3f, night);

            bool tilt = PhotoMode ? PhotoBlur > 0.02f : (s == null || s.TiltShift);
            var rig = GameServices.CameraRig;
            _dof.active = tilt && rig != null;
            if (_dof.active)
            {
                float d = rig.FocusDistance;
                _dof.focusDistance.value = Mathf.Max(1f, d);
                // Stronger miniature feel up close, almost none when looking over the whole valley.
                float k = Mathf.InverseLerp(260f, 25f, d);
                if (PhotoMode) k = Mathf.Clamp01(PhotoBlur * 1.25f);
                _dof.aperture.value = Mathf.Lerp(16f, 5.6f, k);
                _dof.focalLength.value = Mathf.Lerp(28f, 85f, k);
            }
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }
    }
}
