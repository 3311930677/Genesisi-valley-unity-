using System;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Code-built UGUI helpers so every screen shares spacing, typography and states.</summary>
    public static class UIFactory
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors + pivot at the same normalized point, then size and offset.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        /// <summary>Rounded panel with a hairline border and soft drop shadow.</summary>
        public static RectTransform Panel(Transform parent, string name, Color color, int radius = 12, bool shadow = true, bool border = true)
        {
            // Container without its own graphic so the drop shadow can sit *behind* the background.
            var rt = Rect(parent, name);
            if (shadow)
            {
                var sh = Image(rt, "Shadow", new Color(0, 0, 0, 0.5f), UIStyle.SoftShadow(radius, 16));
                Stretch(sh.rectTransform, -16, -16, -12, -22);
                sh.raycastTarget = false;
                sh.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            var bg = Image(rt, "Bg", color, UIStyle.Rounded(radius), true);
            Stretch(bg.rectTransform);
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            if (border)
            {
                var b = Image(rt, "Border", UIStyle.PanelBorder, UIStyle.Rounded(radius, 1.5f));
                Stretch(b.rectTransform);
                b.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            return rt;
        }

        /// <summary>Background image of a panel made by <see cref="Panel"/>.</summary>
        public static Image PanelBg(RectTransform panel)
        {
            var t = panel.Find("Bg");
            return t != null ? t.GetComponent<Image>() : null;
        }

        public static Text Label(Transform parent, string text, int size = UIStyle.FontBody, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, bool shadow = false, Font font = null)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ?? UIStyle.Font;
            t.fontSize = size;
            t.color = color ?? UIStyle.Text;
            t.alignment = align;
            t.text = text;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<Shadow>();
                s.effectColor = new Color(0, 0, 0, 0.6f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        public static LayoutElement Layout(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1, float minH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = prefW; le.preferredHeight = prefH; le.flexibleWidth = flexW; le.flexibleHeight = flexH;
            if (minH >= 0) le.minHeight = minH;
            return le;
        }

        public static VerticalLayoutGroup VLayout(RectTransform rt, float spacing = 8, int padL = 0, int padR = 0, int padT = 0, int padB = 0, bool expandWidth = true)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padL, padR, padT, padB);
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = expandWidth; g.childForceExpandHeight = false;
            g.childAlignment = TextAnchor.UpperLeft;
            return g;
        }

        public static HorizontalLayoutGroup HLayout(RectTransform rt, float spacing = 8, int padL = 0, int padR = 0, int padT = 0, int padB = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padL, padR, padT, padB);
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            g.childAlignment = align;
            return g;
        }

        public static ContentSizeFitter Fit(RectTransform rt, bool horizontal, bool vertical)
        {
            var f = rt.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return f;
        }

        // ------------------------------------------------------------------ buttons

        public static UIButton Button(Transform parent, string text, Action onClick, float width = 220, float height = 50, string icon = null, int fontSize = UIStyle.FontBody, int radius = 10)
        {
            var rt = Rect(parent, "Button_" + text);
            rt.sizeDelta = new Vector2(width, height);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UIStyle.Rounded(radius);
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.raycastTarget = true;
            var outline = Image(rt, "Outline", new Color(1, 1, 1, 0.1f), UIStyle.Rounded(radius, 1.5f));
            Stretch(outline.rectTransform);
            outline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var b = rt.gameObject.AddComponent<UIButton>();
            b.Background = bg;
            b.Outline = outline;
            b.OnClick = onClick;
            float x = 16f;
            if (!string.IsNullOrEmpty(icon))
            {
                var sprite = UIStyle.Icon(icon);
                if (sprite != null)
                {
                    var ic = Image(rt, "Icon", UIStyle.Text, sprite);
                    ic.preserveAspect = true;
                    float s = Mathf.Min(height - 18f, 30f);
                    Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(s, s), new Vector2(14f, 0));
                    b.Icon = ic;
                    x = 14f + s + 10f;
                }
            }
            if (!string.IsNullOrEmpty(text))
            {
                var label = Label(rt, text, fontSize, UIStyle.Text, b.Icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                Stretch(label.rectTransform, b.Icon != null ? x : 10, 10, 0, 0);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                b.Label = label;
            }
            Layout(rt, width, height);
            b.Refresh();
            return b;
        }

        /// <summary>Square icon button (toolbar, speed controls) with optional hotkey badge.</summary>
        public static UIButton IconButton(Transform parent, string icon, string fallbackText, Action onClick, float size = 56, string tooltip = null, string badge = null)
        {
            var b = Button(parent, null, onClick, size, size, null, UIStyle.FontBody, 12);
            b.name = "IconButton_" + (icon ?? fallbackText);
            var sprite = UIStyle.Icon(icon);
            if (sprite != null)
            {
                var ic = Image(b.transform, "Icon", UIStyle.Text, sprite);
                ic.preserveAspect = true;
                Place(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size * 0.58f, size * 0.58f), Vector2.zero);
                b.Icon = ic;
            }
            else
            {
                var t = Label(b.transform, fallbackText, Mathf.RoundToInt(size * 0.32f), UIStyle.Text, TextAnchor.MiddleCenter);
                Stretch(t.rectTransform);
                b.Label = t;
            }
            if (!string.IsNullOrEmpty(badge))
            {
                var bt = Label(b.transform, badge, 13, UIStyle.TextFaint, TextAnchor.LowerRight);
                Stretch(bt.rectTransform, 0, 5, 0, 3);
            }
            b.Tooltip = tooltip;
            return b;
        }

        public static UIButton TextLink(Transform parent, string text, Action onClick, int size = UIStyle.FontSmall)
        {
            var b = Button(parent, text, onClick, 10, 30, null, size, 6);
            b.Normal = new Color(1, 1, 1, 0f);
            b.Hover = new Color(1, 1, 1, 0.08f);
            b.Pressed = new Color(1, 1, 1, 0.04f);
            b.Label.color = UIStyle.Accent;
            var le = b.GetComponent<LayoutElement>();
            le.preferredWidth = b.Label.preferredWidth + 20f;
            b.Refresh();
            return b;
        }

        // ------------------------------------------------------------------ form rows

        private static RectTransform Row(Transform parent, string label, float height, out Text labelText)
        {
            var row = Rect(parent, "Row_" + label);
            Layout(row, -1, height, 1);
            labelText = Label(row, label, UIStyle.FontBody, UIStyle.TextDim);
            labelText.rectTransform.anchorMin = new Vector2(0, 0); labelText.rectTransform.anchorMax = new Vector2(0.42f, 1);
            labelText.rectTransform.offsetMin = new Vector2(4, 0); labelText.rectTransform.offsetMax = Vector2.zero;
            return row;
        }

        public static UISliderRow SliderRow(Transform parent, string label, float min, float max, float value, Func<float, string> format, Action<float> changed, bool whole = false)
        {
            var row = Row(parent, label, 44, out _);
            var r = new UISliderRow { Root = row, Format = format ?? (v => v.ToString("0.00")), Changed = changed };
            var sliderRt = Rect(row, "Slider");
            sliderRt.anchorMin = new Vector2(0.44f, 0.5f); sliderRt.anchorMax = new Vector2(0.86f, 0.5f);
            sliderRt.sizeDelta = new Vector2(0, 24); sliderRt.anchoredPosition = Vector2.zero;
            var track = Image(sliderRt, "Track", new Color(1, 1, 1, 0.12f), UIStyle.Rounded(5), true);
            Stretch(track.rectTransform, 0, 0, 7, 7);
            var fillArea = Rect(sliderRt, "FillArea"); Stretch(fillArea, 0, 0, 7, 7);
            var fill = Image(fillArea, "Fill", UIStyle.Accent, UIStyle.Rounded(5));
            Stretch(fill.rectTransform);
            var handleArea = Rect(sliderRt, "HandleArea"); Stretch(handleArea, 10, 10, 0, 0);
            var handle = Image(handleArea, "Handle", UIStyle.Text, UIStyle.Circle(16), true);
            handle.rectTransform.sizeDelta = new Vector2(22, 22);
            var s = sliderRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.value = value;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            r.Slider = s;
            r.Value = Label(row, r.Format(value), UIStyle.FontSmall, UIStyle.Text, TextAnchor.MiddleRight);
            r.Value.rectTransform.anchorMin = new Vector2(0.86f, 0); r.Value.rectTransform.anchorMax = new Vector2(1, 1);
            r.Value.rectTransform.offsetMin = Vector2.zero; r.Value.rectTransform.offsetMax = new Vector2(-4, 0);
            s.onValueChanged.AddListener(v => { r.Value.text = r.Format(v); if (!r.Suppress) r.Changed?.Invoke(v); });
            return r;
        }

        public static UIChoiceRow ChoiceRow(Transform parent, string label, string[] options, int index, Action<int> changed)
        {
            var row = Row(parent, label, 44, out _);
            var r = new UIChoiceRow { Root = row, Options = options, Changed = changed };
            var box = Rect(row, "Choice");
            box.anchorMin = new Vector2(0.44f, 0.5f); box.anchorMax = new Vector2(1f, 0.5f);
            box.sizeDelta = new Vector2(0, 38); box.anchoredPosition = Vector2.zero;
            var bg = Image(box, "Bg", new Color(1, 1, 1, 0.06f), UIStyle.Rounded(8));
            Stretch(bg.rectTransform);
            var left = Button(box, "◀", () => r.Step(-1), 38, 38, null, UIStyle.FontSmall, 8);
            Place((RectTransform)left.transform, new Vector2(0, 0.5f), new Vector2(38, 38), Vector2.zero);
            var right = Button(box, "▶", () => r.Step(1), 38, 38, null, UIStyle.FontSmall, 8);
            Place((RectTransform)right.transform, new Vector2(1, 0.5f), new Vector2(38, 38), Vector2.zero);
            r.Value = Label(box, "", UIStyle.FontBody, UIStyle.Text, TextAnchor.MiddleCenter);
            Stretch(r.Value.rectTransform, 40, 40, 0, 0);
            r.Set(index);
            return r;
        }

        public static UIToggleRow ToggleRow(Transform parent, string label, bool value, Action<bool> changed, string tooltip = null)
        {
            var row = Row(parent, label, 44, out var lt);
            var r = new UIToggleRow { Root = row, Changed = changed };
            var b = Button(row, null, () => r.Set(!r.Value, true), 52, 28, null, UIStyle.FontSmall, 14);
            b.Normal = b.Hover = b.Pressed = new Color(0, 0, 0, 0);
            b.HoverScale = 1.05f;
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.44f, 0.5f); brt.pivot = new Vector2(0, 0.5f); brt.anchoredPosition = Vector2.zero;
            r.Track = Image(brt, "Track", new Color(1, 1, 1, 0.14f), UIStyle.Rounded(14));
            Stretch(r.Track.rectTransform);
            r.Track.transform.SetAsFirstSibling();
            r.Knob = Image(brt, "Knob", UIStyle.Text, UIStyle.Circle(12));
            Place(r.Knob.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(22, 22), new Vector2(value ? 13 : -13, 0));
            r.Button = b;
            b.Tooltip = tooltip;
            r.Set(value);
            return r;
        }

        public static InputField Input(Transform parent, string placeholder, string value, float width = 320, float height = 44)
        {
            var rt = Rect(parent, "Input");
            rt.sizeDelta = new Vector2(width, height);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UIStyle.Rounded(8); bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = new Color(0, 0, 0, 0.35f);
            var border = Image(rt, "Border", new Color(1, 1, 1, 0.15f), UIStyle.Rounded(8, 1.5f));
            Stretch(border.rectTransform);
            var text = Label(rt, "", UIStyle.FontBody, UIStyle.Text);
            Stretch(text.rectTransform, 14, 14, 0, 0);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Label(rt, placeholder, UIStyle.FontBody, UIStyle.TextFaint);
            Stretch(ph.rectTransform, 14, 14, 0, 0);
            ph.fontStyle = FontStyle.Italic;
            var field = rt.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            field.caretColor = UIStyle.Accent;
            field.selectionColor = new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.35f);
            field.customCaretColor = true;
            field.text = value;
            Layout(rt, width, height);
            return field;
        }

        public static UIBar Bar(Transform parent, string name, float width, float height, Color color, bool withLabel = false)
        {
            var rt = Rect(parent, name);
            rt.sizeDelta = new Vector2(width, height);
            var bg = Image(rt, "Bg", new Color(0, 0, 0, 0.4f), UIStyle.Rounded(Mathf.Max(2, (int)(height * 0.5f))));
            Stretch(bg.rectTransform);
            var fill = Image(rt, "Fill", color, UIStyle.Rounded(Mathf.Max(2, (int)(height * 0.5f))));
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            var bar = new UIBar { Root = rt, Fill = fill };
            if (withLabel)
            {
                bar.Label = Label(rt, "", Mathf.RoundToInt(Mathf.Max(12, height * 0.72f)), UIStyle.Text, TextAnchor.MiddleCenter, true);
                Stretch(bar.Label.rectTransform);
            }
            Layout(rt, width, height);
            return bar;
        }

        public static Image Divider(Transform parent, float width = -1, float alpha = 0.25f)
        {
            var d = Image(parent, "Divider", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, alpha), UIStyle.Divider());
            Layout(d, width, 12);
            return d;
        }

        public static RectTransform Spacer(Transform parent, float height, float width = -1)
        {
            var rt = Rect(parent, "Spacer");
            Layout(rt, width, height);
            return rt;
        }

        /// <summary>Vertical scroll list; returns the content transform (VerticalLayoutGroup + fitter).</summary>
        public static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll, float spacing = 6)
        {
            var rt = Rect(parent, name);
            scroll = rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            scroll.inertia = true;
            var viewport = Rect(rt, "Viewport");
            Stretch(viewport, 0, 10, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.001f);
            vpImg.raycastTarget = true;
            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            VLayout(content, spacing, 0, 4, 2, 6);
            Fit(content, false, true);
            scroll.viewport = viewport;
            scroll.content = content;
            // Slim scrollbar
            var barRt = Rect(rt, "Scrollbar");
            barRt.anchorMin = new Vector2(1, 0); barRt.anchorMax = new Vector2(1, 1); barRt.pivot = new Vector2(1, 0.5f);
            barRt.sizeDelta = new Vector2(6, 0);
            var barBg = barRt.gameObject.AddComponent<Image>();
            barBg.sprite = UIStyle.Rounded(3); barBg.type = UnityEngine.UI.Image.Type.Sliced; barBg.color = new Color(1, 1, 1, 0.06f);
            var slide = Rect(barRt, "Sliding"); Stretch(slide);
            var handle = Image(slide, "Handle", new Color(1, 0.95f, 0.85f, 0.3f), UIStyle.Rounded(3), true);
            Stretch(handle.rectTransform);
            var sb = barRt.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle.rectTransform;
            sb.targetGraphic = handle;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            return content;
        }

        public static void Clear(Transform t)
        {
            // Deactivate first so layout groups ignore the children during the frame before Destroy lands.
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var go = t.GetChild(i).gameObject;
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
        }

        public static CanvasGroup Group(Component c)
        {
            var g = c.GetComponent<CanvasGroup>();
            return g != null ? g : c.gameObject.AddComponent<CanvasGroup>();
        }

        public static void SetVisible(CanvasGroup g, bool visible, float duration = 0.2f)
        {
            if (g == null) return;
            if (visible)
            {
                if (!g.gameObject.activeSelf) { g.gameObject.SetActive(true); g.alpha = 0f; }
                g.blocksRaycasts = true; g.interactable = true;
                UITween.Fade(g, 1f, duration);
            }
            else
            {
                if (!g.gameObject.activeSelf) return;
                g.blocksRaycasts = false; g.interactable = false;
                UITween.Fade(g, 0f, duration, () => { if (g != null && g.alpha <= 0.01f) g.gameObject.SetActive(false); });
            }
        }
    }
}
