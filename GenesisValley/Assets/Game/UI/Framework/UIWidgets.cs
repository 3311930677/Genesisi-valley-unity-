using System;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Button with hover/press/selected states, a soft hover scale, click sounds and an optional tooltip.</summary>
    public sealed class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Image Background;
        public Image Icon;
        public Text Label;
        public Image Outline;
        public Action OnClick;
        public string Tooltip;
        public Func<string> TooltipProvider;
        public string ClickSound = "ui_click";
        public bool Toggleable;
        public float HoverScale = 1.035f;

        public Color Normal = UIStyle.Button, Hover = UIStyle.ButtonHover, Pressed = UIStyle.ButtonPressed, SelectedColor = UIStyle.ButtonSelected;

        private bool _hover, _down, _selected, _interactable = true;
        private float _hoverStart;

        public bool Selected { get => _selected; set { _selected = value; Refresh(); } }
        public bool Interactable { get => _interactable; set { _interactable = value; Refresh(); } }
        public bool IsHovered => _hover;

        public void Refresh()
        {
            if (Background != null)
            {
                var c = !_interactable ? new Color(Normal.r, Normal.g, Normal.b, Normal.a * 0.55f) : _down ? Pressed : _selected ? SelectedColor : _hover ? Hover : Normal;
                Background.color = c;
            }
            if (Outline != null) Outline.color = _selected ? UIStyle.Accent : _hover && _interactable ? new Color(1, 0.92f, 0.75f, 0.35f) : new Color(1, 0.92f, 0.75f, 0.1f);
            float a = _interactable ? 1f : 0.4f;
            if (Label != null) { var lc = Label.color; lc.a = a; Label.color = lc; }
            if (Icon != null) { var ic = Icon.color; ic.a = a; Icon.color = ic; }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hover = true;
            _hoverStart = Time.unscaledTime;
            Refresh();
            if (_interactable)
            {
                UITween.Scale(transform, transform.localScale, Vector3.one * HoverScale, 0.12f, UITween.OutCubic);
                GameServices.Audio?.PlayUI("ui_hover", 0.25f, UnityEngine.Random.Range(0.97f, 1.04f));
            }
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hover = false; _down = false;
            Refresh();
            UITween.Scale(transform, transform.localScale, Vector3.one, 0.15f, UITween.OutCubic);
            TooltipLayer.Hide(this);
        }

        public void OnPointerDown(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left) return; _down = true; Refresh(); UITween.Scale(transform, transform.localScale, Vector3.one * 0.97f, 0.06f, UITween.OutCubic); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); UITween.Scale(transform, transform.localScale, Vector3.one * (_hover ? HoverScale : 1f), 0.12f, UITween.OutBack); }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !_interactable) return;
            if (!string.IsNullOrEmpty(ClickSound)) GameServices.Audio?.PlayUI(ClickSound, 0.6f);
            TooltipLayer.Hide(this);
            OnClick?.Invoke();
        }

        private void Update()
        {
            if (_hover && Time.unscaledTime - _hoverStart > 0.45f)
            {
                string tip = TooltipProvider != null ? TooltipProvider() : Tooltip;
                if (!string.IsNullOrEmpty(tip)) TooltipLayer.Show(this, tip, (RectTransform)transform);
            }
        }

        private void OnDisable() { _hover = false; _down = false; transform.localScale = Vector3.one; TooltipLayer.Hide(this); }
    }

    /// <summary>Shows a tooltip for any graphic (resource counters, bars, icons).</summary>
    public sealed class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Text;
        public Func<string> Provider;
        private bool _hover;
        private float _start;
        public void OnPointerEnter(PointerEventData e) { _hover = true; _start = Time.unscaledTime; }
        public void OnPointerExit(PointerEventData e) { _hover = false; TooltipLayer.Hide(this); }
        private void Update()
        {
            if (!_hover || Time.unscaledTime - _start < 0.35f) return;
            var t = Provider != null ? Provider() : Text;
            if (!string.IsNullOrEmpty(t)) TooltipLayer.Show(this, t, (RectTransform)transform);
        }
        private void OnDisable() { _hover = false; TooltipLayer.Hide(this); }
    }

    /// <summary>Single tooltip bubble on the top layer, placed next to the hovered element and kept on screen.</summary>
    public sealed class TooltipLayer : MonoBehaviour
    {
        private static TooltipLayer _instance;
        private RectTransform _rt;
        private Text _text;
        private CanvasGroup _group;
        private object _owner;
        private RectTransform _target;
        private Canvas _canvas;

        public static void Create(RectTransform root)
        {
            var go = UIFactory.Panel(root, "Tooltip", UIStyle.PanelSolid, 8);
            _instance = go.gameObject.AddComponent<TooltipLayer>();
            _instance._rt = go;
            _instance._canvas = root.GetComponentInParent<Canvas>();
            go.pivot = new Vector2(0, 1);
            go.anchorMin = go.anchorMax = new Vector2(0, 1);
            var fitter = go.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var lg = go.gameObject.AddComponent<VerticalLayoutGroup>();
            lg.padding = new RectOffset(14, 14, 10, 10);
            lg.childControlHeight = lg.childControlWidth = true;
            _instance._text = UIFactory.Label(go, "", UIStyle.FontSmall, UIStyle.Text, TextAnchor.UpperLeft);
            var le = _instance._text.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = -1;
            _instance._text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _instance._group = go.gameObject.AddComponent<CanvasGroup>();
            _instance._group.blocksRaycasts = false;
            _instance._group.interactable = false;
            _instance._group.alpha = 0f;
            go.gameObject.SetActive(false);
        }

        public static void Show(object owner, string text, RectTransform target)
        {
            if (_instance == null) return;
            bool changed = _instance._owner != owner || _instance._text.text != text;
            _instance._owner = owner;
            _instance._target = target;
            if (!changed && _instance.gameObject.activeSelf) return;
            _instance._text.text = text;
            var le = _instance._text.GetComponent<LayoutElement>();
            le.preferredWidth = Mathf.Min(420f, _instance._text.preferredWidth + 2f);
            if (!_instance.gameObject.activeSelf)
            {
                _instance.gameObject.SetActive(true);
                _instance._group.alpha = 0f;
                UITween.Fade(_instance._group, 1f, 0.15f);
            }
            _instance._rt.SetAsLastSibling();
        }

        public static void Hide(object owner)
        {
            if (_instance == null || _instance._owner != owner) return;
            _instance._owner = null;
            _instance.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy) { _owner = null; gameObject.SetActive(false); return; }
            var parent = (RectTransform)_rt.parent;
            var corners = new Vector3[4];
            _target.GetWorldCorners(corners);
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            // Prefer below the element; above if there is no room (toolbar at the bottom).
            Vector2 screen = new Vector2(bottomLeft.x, bottomLeft.y - 8f);
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            float h = _rt.rect.height * scale, w = _rt.rect.width * scale;
            if (screen.y - h < 4f) screen.y = topRight.y + h + 8f;
            screen.x = Mathf.Clamp(screen.x, 6f, Screen.width - w - 6f);
            screen.y = Mathf.Clamp(screen.y, h + 6f, Screen.height - 6f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local);
            _rt.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }

    /// <summary>Labelled slider row with live value text.</summary>
    public sealed class UISliderRow
    {
        public RectTransform Root;
        public Slider Slider;
        public Text Value;
        public Func<float, string> Format = v => v.ToString("0.00");
        public Action<float> Changed;
        internal bool Suppress;

        public float Get() => Slider.value;
        public void Set(float v, bool notify = false)
        {
            Suppress = !notify;
            Slider.value = v;
            Suppress = false;
            Value.text = Format(Slider.value);
        }
    }

    /// <summary>"◀ option ▶" selector – compact, gamepad-friendly replacement for dropdowns.</summary>
    public sealed class UIChoiceRow
    {
        public RectTransform Root;
        public Text Value;
        public string[] Options = new string[0];
        public int Index;
        public Action<int> Changed;

        public void Set(int index, bool notify = false)
        {
            if (Options.Length == 0) { Value.text = "—"; return; }
            Index = (index % Options.Length + Options.Length) % Options.Length;
            Value.text = Options[Index];
            if (notify) Changed?.Invoke(Index);
        }

        public void Step(int d) { Set(Index + d, true); }
    }

    /// <summary>Two-state switch row.</summary>
    public sealed class UIToggleRow
    {
        public RectTransform Root;
        public UIButton Button;
        public Image Knob;
        public Image Track;
        public bool Value;
        public Action<bool> Changed;

        public void Set(bool v, bool notify = false)
        {
            Value = v;
            Track.color = v ? new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.95f) : new Color(1, 1, 1, 0.14f);
            var krt = Knob.rectTransform;
            UITween.Move(krt, new Vector2(v ? 13f : -13f, 0f), 0.14f);
            if (notify) Changed?.Invoke(v);
        }
    }

    /// <summary>Horizontal bar (needs, progress) with optional label.</summary>
    public sealed class UIBar
    {
        public RectTransform Root;
        public Image Fill;
        public Text Label;
        private float _shown = -1f;
        private float _lastTime;

        public void Set(float value01, Color color, string label = null, bool animate = true)
        {
            value01 = Mathf.Clamp01(value01);
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - _lastTime, 0f, 0.5f);
            _lastTime = now;
            if (_shown < 0f || !animate) _shown = value01;
            else _shown = Mathf.MoveTowards(_shown, value01, Mathf.Max(dt, Time.unscaledDeltaTime) * 1.5f);
            var rt = Fill.rectTransform;
            rt.anchorMax = new Vector2(_shown, 1f);
            Fill.color = color;
            if (Label != null && label != null) Label.text = label;
        }
    }
}
