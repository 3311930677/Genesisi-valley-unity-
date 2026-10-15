using System;
using System.Collections;
using System.IO;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// 拍照模式: hides the HUD, freezes time if wanted and exposes time-of-day / exposure / miniature blur /
    /// grading sliders. Enter (or the shutter button) saves a clean PNG to the photos folder.
    /// </summary>
    public sealed class PhotoModePanel : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _panel;
        private CanvasGroup _panelGroup;
        private Image _flash;
        private Text _hint;
        private UISliderRow _hour, _exposure, _blur, _vignette, _saturation, _warmth, _contrast;
        private UIToggleRow _freeze, _clock;
        private bool _panelHidden;
        private bool _wasPaused;
        private float _savedHour;
        private bool _capturing;
        private PostFxController _post;

        public bool IsActive { get; private set; }
        public static string PhotoDirectory => Path.Combine(Application.persistentDataPath, "photos");

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _group = UIFactory.Group(rt);
            _group.alpha = 0f;

            _flash = UIFactory.Image(rt, "Flash", new Color(1, 1, 1, 0), null, false);
            UIFactory.Stretch(_flash.rectTransform);

            // thin frame corners for a viewfinder feel
            for (int i = 0; i < 4; i++)
            {
                var a = new Vector2(i % 2, i / 2);
                var h = UIFactory.Image(rt, "CornerH", new Color(1, 1, 1, 0.55f));
                UIFactory.Place(h.rectTransform, a, new Vector2(46, 2), new Vector2(a.x == 0 ? 40 : -40, a.y == 0 ? 40 : -40));
                var v = UIFactory.Image(rt, "CornerV", new Color(1, 1, 1, 0.55f));
                UIFactory.Place(v.rectTransform, a, new Vector2(2, 46), new Vector2(a.x == 0 ? 40 : -40, a.y == 0 ? 40 : -40));
            }

            _panel = UIFactory.Panel(rt, "Panel", UIStyle.Panel, 16);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1, 0.5f);
            _panel.anchoredPosition = new Vector2(-30, 0);
            _panel.sizeDelta = new Vector2(440, 760);
            _panelGroup = UIFactory.Group(_panel);
            UIFactory.VLayout(_panel, 4, 22, 22, 18, 18);
            UIFactory.Fit(_panel, false, true);
            var title = UIFactory.Label(_panel, "拍照模式", UIStyle.FontTitle, UIStyle.Text, TextAnchor.MiddleLeft, true, UIStyle.TitleFont);
            UIFactory.Layout(title, -1, 44);
            UIFactory.Divider(_panel, -1, 0.25f);
            _freeze = UIFactory.ToggleRow(_panel, "冻结时间", true, v => SetFrozen(v), "暂停模拟，村民与水流停在这一刻。");
            _clock = UIFactory.ToggleRow(_panel, "自定义时刻", false, v => { _hour.Root.gameObject.SetActive(v); ApplyHour(); });
            _hour = UIFactory.SliderRow(_panel, "时刻", 0f, 24f, 17f, v => { int hh = (int)v; int mm = (int)((v - hh) * 60); return hh.ToString("00") + ":" + mm.ToString("00"); }, v => ApplyHour());
            _hour.Root.gameObject.SetActive(false);
            _exposure = UIFactory.SliderRow(_panel, "曝光", -2f, 2f, 0f, v => (v >= 0 ? "+" : "") + v.ToString("0.0"), v => { if (_post != null) _post.PhotoExposure = v; });
            _blur = UIFactory.SliderRow(_panel, "微缩景深", 0f, 1f, 0.5f, v => Mathf.RoundToInt(v * 100) + "%", v => { if (_post != null) _post.PhotoBlur = v; });
            _vignette = UIFactory.SliderRow(_panel, "暗角", 0f, 0.6f, 0.25f, v => Mathf.RoundToInt(v / 0.6f * 100) + "%", v => { if (_post != null) _post.PhotoVignette = v; });
            _saturation = UIFactory.SliderRow(_panel, "饱和度", -60f, 40f, 0f, v => (v >= 0 ? "+" : "") + v.ToString("0"), v => { if (_post != null) _post.PhotoSaturation = v; });
            _warmth = UIFactory.SliderRow(_panel, "色温", -40f, 40f, 0f, v => v > 0.5f ? "暖 " + v.ToString("0") : v < -0.5f ? "冷 " + (-v).ToString("0") : "0", v => { if (_post != null) _post.PhotoWarmth = v; });
            _contrast = UIFactory.SliderRow(_panel, "对比度", -30f, 30f, 0f, v => (v >= 0 ? "+" : "") + v.ToString("0"), v => { if (_post != null) _post.PhotoContrast = v; });
            UIFactory.Spacer(_panel, 8);
            var actions = UIFactory.Rect(_panel, "Actions");
            UIFactory.Layout(actions, -1, 52);
            UIFactory.HLayout(actions, 10);
            var shoot = UIFactory.Button(actions, "拍照", Capture, 170, 50, "Camera", UIStyle.FontBody);
            shoot.Normal = UIStyle.AccentDark; shoot.Refresh();
            shoot.Tooltip = "保存一张不含界面的照片（Enter）";
            UIFactory.Button(actions, "重置", ResetValues, 100, 50, null, UIStyle.FontSmall);
            UIFactory.Button(actions, "退出", Exit, 100, 50, null, UIStyle.FontSmall);
            var folder = UIFactory.TextLink(_panel, "打开照片文件夹", () =>
            {
                Directory.CreateDirectory(PhotoDirectory);
                Application.OpenURL("file:///" + PhotoDirectory.Replace('\\', '/'));
            });
            UIFactory.Layout(folder, -1, 30);

            _hint = UIFactory.Label(rt, "", UIStyle.FontSmall, new Color(1, 1, 1, 0.7f), TextAnchor.LowerLeft, true);
            UIFactory.Place(_hint.rectTransform, new Vector2(0, 0), new Vector2(900, 40), new Vector2(60, 50));
            gameObject.SetActive(false);
        }

        public void Enter()
        {
            var g = GameServices.Game;
            if (IsActive || g == null || g.Phase != GamePhase.Playing) return;
            IsActive = true;
            _post = FindObjectOfType<PostFxController>();
            gameObject.SetActive(true);
            _group.alpha = 0f;
            UITween.Fade(_group, 1f, 0.3f);
            var ui = GameUI.Instance;
            ui.Hud.SetShown(false);
            ui.Labels.SetShown(false);
            var tools = GameServices.Tools;
            if (tools != null) { tools.ResetState(); tools.enabled = false; }
            var sel = GameServices.Selection;
            if (sel != null) { sel.Clear(); sel.enabled = false; }
            _wasPaused = g.UserPaused;
            var sky = GameServices.Sky;
            _savedHour = sky != null ? sky.HourOverride : -1f;
            if (_post != null) _post.PhotoMode = true;
            ResetValues();
            SetFrozen(_freeze.Value);
            _panelHidden = false;
            _panelGroup.alpha = 1f;
            _panelGroup.blocksRaycasts = true;
            GameServices.Audio?.PlayUI("ui_click", 0.7f);
        }

        public void Exit()
        {
            if (!IsActive) return;
            IsActive = false;
            var g = GameServices.Game;
            if (g != null) g.SetPaused(_wasPaused);
            var sky = GameServices.Sky;
            if (sky != null) sky.HourOverride = _savedHour;
            if (_post != null) { _post.PhotoMode = false; _post.PhotoExposure = 0f; }
            var tools = GameServices.Tools; if (tools != null) tools.enabled = true;
            var sel = GameServices.Selection; if (sel != null) sel.enabled = true;
            var ui = GameUI.Instance;
            bool playing = g != null && g.Phase == GamePhase.Playing;
            ui.Hud.SetShown(playing && !ui.HudHidden);
            ui.Labels.SetShown(playing && !ui.HudHidden);
            UITween.Fade(_group, 0f, 0.2f, () => { if (!IsActive) gameObject.SetActive(false); });
        }

        private void SetFrozen(bool frozen)
        {
            var g = GameServices.Game;
            if (g != null && IsActive) g.SetPaused(frozen || _wasPaused && frozen);
        }

        private void ApplyHour()
        {
            var sky = GameServices.Sky;
            if (sky == null || !IsActive) return;
            sky.HourOverride = _clock.Value ? _hour.Get() : _savedHour;
        }

        private void ResetValues()
        {
            var w = GameServices.World;
            _hour.Set(w != null ? w.Hour : 17f);
            _exposure.Set(0f, true);
            _blur.Set(0.5f, true);
            _vignette.Set(0.25f, true);
            _saturation.Set(0f, true);
            _warmth.Set(0f, true);
            _contrast.Set(0f, true);
            _clock.Set(false, false);
            _hour.Root.gameObject.SetActive(false);
            ApplyHour();
        }

        public void Capture()
        {
            if (_capturing || !IsActive) return;
            StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            _capturing = true;
            var canvas = GameUI.Instance.Canvas;
            canvas.enabled = false;
            yield return new WaitForEndOfFrame();
            string path = null;
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(PhotoDirectory);
                var w = GameServices.World;
                string name = (w != null ? Sanitize(w.WorldName) + "_第" + w.Day + "天_" : "") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                path = Path.Combine(PhotoDirectory, name);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("拍照失败：" + e.Message); path = null; }
            finally { if (tex != null) Destroy(tex); }
            canvas.enabled = true;
            _flash.color = new Color(1, 1, 1, 0.85f);
            UITween.Run(_flash, 0.5f, k => _flash.color = new Color(1, 1, 1, 0.85f * (1f - k)), UITween.OutCubic);
            GameServices.Audio?.PlayUI("save", 0.8f, 1.2f);
            GameUI.Instance.Notify(path != null ? "照片已保存：" + Path.GetFileName(path) : "拍照失败", path != null ? ToastKind.Success : ToastKind.Error);
            _capturing = false;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private void Update()
        {
            if (!IsActive) return;
            var g = GameServices.Game;
            if (g == null || g.Phase != GamePhase.Playing) { Exit(); return; }
            if (InputBindings.TextInputActive && !InputBindings.Suppressed) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.F12)) Capture();
            if (InputBindings.DownRaw(GameAction.ToggleUI))
            {
                _panelHidden = !_panelHidden;
                _panelGroup.blocksRaycasts = !_panelHidden;
                UITween.Fade(_panelGroup, _panelHidden ? 0f : 1f, 0.2f);
            }
            if (_freeze.Value != g.UserPaused) _freeze.Set(g.UserPaused);
            string K(GameAction a) => InputBindings.KeyName(InputBindings.Get(a));
            _hint.text = "Enter 拍照 · " + K(GameAction.ToggleUI) + " 隐藏面板 · " + K(GameAction.Pause) + " 冻结/继续 · 鼠标自由取景 · Esc 退出";
        }
    }
}
