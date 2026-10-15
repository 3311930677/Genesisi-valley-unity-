using System;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>Player preferences (PlayerPrefs). Presentation only — never part of the simulation or saves.</summary>
    public sealed class GameSettings
    {
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.55f;
        public float SfxVolume = 0.85f;
        public float AmbienceVolume = 0.7f;
        public int Quality = -1;            // QualitySettings level; -1 = keep project default
        public bool Fullscreen = true;
        public int ResolutionIndex = -1;
        public bool VSync = true;
        public int FrameCap = 120;
        public bool EdgePan = false;
        public float CameraPanSpeed = 1f;
        public float CameraRotateSpeed = 1f;
        public bool InvertZoom = false;
        public float UiScale = 1f;
        public bool ShowVillagerNames = true;
        public bool ShowHints = true;
        public bool ReducedEffects = false;
        public bool TiltShift = true;
        public float ShadowDistance = 160f;
        public float GrassDensity = 1f;
        public bool AutoSave = true;
        public bool PauseOnFocusLoss = false;

        public event Action Changed;

        private const string Prefix = "gv.";

        public static GameSettings Load()
        {
            var s = new GameSettings();
            s.MasterVolume = PlayerPrefs.GetFloat(Prefix + "vol.master", s.MasterVolume);
            s.MusicVolume = PlayerPrefs.GetFloat(Prefix + "vol.music", s.MusicVolume);
            s.SfxVolume = PlayerPrefs.GetFloat(Prefix + "vol.sfx", s.SfxVolume);
            s.AmbienceVolume = PlayerPrefs.GetFloat(Prefix + "vol.amb", s.AmbienceVolume);
            s.Quality = PlayerPrefs.GetInt(Prefix + "quality", s.Quality);
            s.Fullscreen = PlayerPrefs.GetInt(Prefix + "fullscreen", s.Fullscreen ? 1 : 0) == 1;
            s.ResolutionIndex = PlayerPrefs.GetInt(Prefix + "res", s.ResolutionIndex);
            s.VSync = PlayerPrefs.GetInt(Prefix + "vsync", s.VSync ? 1 : 0) == 1;
            s.FrameCap = PlayerPrefs.GetInt(Prefix + "fps", s.FrameCap);
            s.EdgePan = PlayerPrefs.GetInt(Prefix + "edgepan", s.EdgePan ? 1 : 0) == 1;
            s.CameraPanSpeed = PlayerPrefs.GetFloat(Prefix + "cam.pan", s.CameraPanSpeed);
            s.CameraRotateSpeed = PlayerPrefs.GetFloat(Prefix + "cam.rot", s.CameraRotateSpeed);
            s.InvertZoom = PlayerPrefs.GetInt(Prefix + "cam.invzoom", s.InvertZoom ? 1 : 0) == 1;
            s.UiScale = PlayerPrefs.GetFloat(Prefix + "ui.scale", s.UiScale);
            s.ShowVillagerNames = PlayerPrefs.GetInt(Prefix + "ui.names", s.ShowVillagerNames ? 1 : 0) == 1;
            s.ShowHints = PlayerPrefs.GetInt(Prefix + "ui.hints", s.ShowHints ? 1 : 0) == 1;
            s.ReducedEffects = PlayerPrefs.GetInt(Prefix + "fx.reduced", s.ReducedEffects ? 1 : 0) == 1;
            s.TiltShift = PlayerPrefs.GetInt(Prefix + "fx.tilt", s.TiltShift ? 1 : 0) == 1;
            s.ShadowDistance = PlayerPrefs.GetFloat(Prefix + "gfx.shadow", s.ShadowDistance);
            s.GrassDensity = PlayerPrefs.GetFloat(Prefix + "gfx.grass", s.GrassDensity);
            s.AutoSave = PlayerPrefs.GetInt(Prefix + "autosave", s.AutoSave ? 1 : 0) == 1;
            s.PauseOnFocusLoss = PlayerPrefs.GetInt(Prefix + "pausefocus", s.PauseOnFocusLoss ? 1 : 0) == 1;
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "vol.master", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "vol.music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "vol.sfx", SfxVolume);
            PlayerPrefs.SetFloat(Prefix + "vol.amb", AmbienceVolume);
            PlayerPrefs.SetInt(Prefix + "quality", Quality);
            PlayerPrefs.SetInt(Prefix + "fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "res", ResolutionIndex);
            PlayerPrefs.SetInt(Prefix + "vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fps", FrameCap);
            PlayerPrefs.SetInt(Prefix + "edgepan", EdgePan ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "cam.pan", CameraPanSpeed);
            PlayerPrefs.SetFloat(Prefix + "cam.rot", CameraRotateSpeed);
            PlayerPrefs.SetInt(Prefix + "cam.invzoom", InvertZoom ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "ui.scale", UiScale);
            PlayerPrefs.SetInt(Prefix + "ui.names", ShowVillagerNames ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ui.hints", ShowHints ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fx.reduced", ReducedEffects ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fx.tilt", TiltShift ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "gfx.shadow", ShadowDistance);
            PlayerPrefs.SetFloat(Prefix + "gfx.grass", GrassDensity);
            PlayerPrefs.SetInt(Prefix + "autosave", AutoSave ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "pausefocus", PauseOnFocusLoss ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Applies engine-level settings (quality, display, frame rate) and notifies listeners.</summary>
        public void Apply()
        {
            if (Quality >= 0 && Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Quality)
                QualitySettings.SetQualityLevel(Quality, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : FrameCap;
            QualitySettings.shadowDistance = ShadowDistance;
            if (!Application.isEditor)
            {
                var resolutions = Screen.resolutions;
                if (ResolutionIndex >= 0 && ResolutionIndex < resolutions.Length)
                {
                    var r = resolutions[ResolutionIndex];
                    if (Screen.width != r.width || Screen.height != r.height || Screen.fullScreen != Fullscreen)
                        Screen.SetResolution(r.width, r.height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                }
                else if (Screen.fullScreen != Fullscreen)
                {
                    Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                }
            }
            AudioListener.volume = 1f; // master volume is applied per channel by AudioDirector
            Changed?.Invoke();
        }
    }
}
