using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;

namespace GenesisValley.UI
{
    /// <summary>
    /// Visual language of the interface: warm dark "lacquered wood" panels with cream text and a honey-gold accent,
    /// so the HUD stays readable over bright meadows and night scenes alike. All sprites are generated at runtime
    /// (anti-aliased rounded rects, soft shadows, gradients) – no texture assets required.
    /// </summary>
    public static class UIStyle
    {
        public static readonly Color Panel = new Color(0.115f, 0.098f, 0.085f, 0.86f);
        public static readonly Color PanelSolid = new Color(0.135f, 0.115f, 0.1f, 0.97f);
        public static readonly Color PanelLight = new Color(0.2f, 0.172f, 0.148f, 0.9f);
        public static readonly Color PanelBorder = new Color(1f, 0.93f, 0.8f, 0.13f);
        public static readonly Color Paper = new Color(0.955f, 0.92f, 0.85f, 0.97f);
        public static readonly Color Ink = new Color(0.22f, 0.17f, 0.13f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.92f, 0.85f, 1f);
        public static readonly Color TextDim = new Color(0.78f, 0.72f, 0.64f, 1f);
        public static readonly Color TextFaint = new Color(0.6f, 0.55f, 0.49f, 1f);
        public static readonly Color Accent = new Color(0.93f, 0.71f, 0.36f, 1f);
        public static readonly Color AccentDark = new Color(0.62f, 0.43f, 0.18f, 1f);
        public static readonly Color Good = new Color(0.55f, 0.85f, 0.45f, 1f);
        public static readonly Color Warn = new Color(1f, 0.74f, 0.3f, 1f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f, 1f);
        public static readonly Color Water = new Color(0.5f, 0.82f, 1f, 1f);
        public static readonly Color Button = new Color(0.2f, 0.17f, 0.145f, 0.92f);
        public static readonly Color ButtonHover = new Color(0.29f, 0.245f, 0.2f, 0.95f);
        public static readonly Color ButtonPressed = new Color(0.16f, 0.135f, 0.115f, 0.95f);
        public static readonly Color ButtonSelected = new Color(0.45f, 0.33f, 0.17f, 0.95f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);

        public const int FontSmall = 17, FontBody = 20, FontLarge = 24, FontTitle = 30, FontHuge = 46;

        private static Font _font, _title;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = GameServices.Art.uiFont;
                if (_font == null)
                {
                    try { _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS" }, 32); }
                    catch { _font = null; }
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Font TitleFont
        {
            get
            {
                if (_title != null) return _title;
                _title = GameServices.Art.titleFont;
                if (_title == null)
                {
                    try { _title = Font.CreateDynamicFontFromOSFont(new[] { "Noto Serif SC", "Noto Serif CJK SC", "Source Han Serif SC", "STSong", "SimSun", "Songti SC" }, 48); }
                    catch { _title = null; }
                }
                if (_title == null) _title = Font;
                return _title;
            }
        }

        public static void ResetCache() { _font = null; _title = null; }

        // ------------------------------------------------------------------ procedural sprites

        /// <summary>9-sliced rounded rectangle. <paramref name="border"/> &gt; 0 draws only the outline.</summary>
        public static Sprite Rounded(int radius, float border = 0f)
        {
            string key = "r" + radius + "_" + border;
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "GV_UI_" + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Signed distance to a rounded box whose straight part is the 4 centre pixels.
                float px0 = Mathf.Abs(x + 0.5f - half) - 2f, py0 = Mathf.Abs(y + 0.5f - half) - 2f;
                float qx = Mathf.Max(px0, 0f), qy = Mathf.Max(py0, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(px0, py0), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d);
                if (border > 0f) a *= Mathf.Clamp01(d + border + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = radius + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = key;
            Sprites[key] = s;
            return s;
        }

        /// <summary>Soft drop shadow (9-sliced, wide blur).</summary>
        public static Sprite SoftShadow(int radius = 14, int blur = 18)
        {
            string key = "sh" + radius + "_" + blur;
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            int size = (radius + blur) * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "GV_UI_" + key, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px0 = Mathf.Abs(x + 0.5f - half) - 2f, py0 = Mathf.Abs(y + 0.5f - half) - 2f;
                float qx = Mathf.Max(px0, 0f), qy = Mathf.Max(py0, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(px0, py0), 0f) - radius;
                float a = 1f - Mathf.SmoothStep(-blur * 0.35f, blur, d);
                px[y * size + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = radius + blur + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            Sprites[key] = s;
            return s;
        }

        public static Sprite Circle(int radius = 32, float border = 0f) => Rounded(radius, border);

        /// <summary>Horizontal (left→right) or vertical (bottom→top) alpha gradient, 1 → 0.</summary>
        public static Sprite Gradient(bool horizontal, float power = 1.6f)
        {
            string key = "g" + horizontal + power;
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            int n = 256;
            var tex = new Texture2D(horizontal ? n : 4, horizontal ? 4 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                byte a = (byte)(Mathf.Pow(1f - i / (n - 1f), power) * 255);
                for (int j = 0; j < 4; j++)
                {
                    if (horizontal) tex.SetPixel(i, j, new Color32(255, 255, 255, a));
                    else tex.SetPixel(j, i, new Color32(255, 255, 255, a));
                }
            }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            Sprites[key] = s;
            return s;
        }

        /// <summary>Radial glow (centre opaque → transparent edge).</summary>
        public static Sprite Glow()
        {
            const string key = "glow";
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            Sprites[key] = s;
            return s;
        }

        /// <summary>Thin ornamental divider: a line that fades at both ends with a small diamond in the middle.</summary>
        public static Sprite Divider()
        {
            const string key = "divider";
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            int w = 256, h = 12;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2 - 1;
                float line = Mathf.Clamp01(1.2f - Mathf.Abs(y + 0.5f - h * 0.5f)) * (1f - u * u);
                float diamond = Mathf.Clamp01(5f - (Mathf.Abs(x + 0.5f - w * 0.5f) + Mathf.Abs(y + 0.5f - h * 0.5f)));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(line, diamond)));
            }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            Sprites[key] = s;
            return s;
        }

        public static Sprite Icon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var s = GameServices.Art.Icon(key);
            if (s == null && !key.StartsWith("Icon_")) s = GameServices.Art.Icon("Icon_" + key);
            return s;
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        public static string Colorize(string text, Color c) => "<color=#" + Hex(c) + ">" + text + "</color>";
    }
}
