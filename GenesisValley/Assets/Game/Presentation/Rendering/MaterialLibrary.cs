using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Shared runtime materials (GDD §9: one material per look, never one per object). Shaders come from the
    /// <see cref="ArtCatalog"/> so they are always included in builds; Shader.Find is only a fallback.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Color_ = Shader.PropertyToID("_Color");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private static ArtCatalog Art => GameServices.Art;

        public static void Clear() => Cache.Clear();

        private static Shader Find(Shader preferred, params string[] names)
        {
            if (preferred != null) return preferred;
            foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
            return null;
        }

        public static Shader LitShader => Find(Art.litShader, "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard");
        public static Shader FoliageShader => Find(Art.foliageShader, "GenesisValley/Foliage", "Universal Render Pipeline/Lit", "Standard");
        public static Shader OverlayShader => Find(Art.overlayShader, "GenesisValley/Overlay", "Universal Render Pipeline/Unlit", "Unlit/Color");
        public static Shader ParticleShader => Find(Art.particleShader, "GenesisValley/Particle", "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit");

        public static Shader VertexLitShader => Find(Art.vertexLitShader, "GenesisValley/VertexLit", "Universal Render Pipeline/Particles/Simple Lit", "Universal Render Pipeline/Simple Lit");

        /// <summary>Vertex-coloured lit material (horizon hills, procedural meshes) with an optional world-planar detail map.</summary>
        public static Material VertexLit(Color tint, Texture detail = null, float detailScale = 0.08f, float detailStrength = 0.35f)
        {
            string k = Key("vlit", tint, detailScale, detailStrength, detail);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(VertexLitShader) { name = "GV_VertexLit" };
            SetColor(m, tint);
            if (detail != null && m.HasProperty("_DetailMap")) m.SetTexture("_DetailMap", detail);
            if (m.HasProperty("_DetailScale")) m.SetFloat("_DetailScale", detailScale);
            if (m.HasProperty("_DetailStrength")) m.SetFloat("_DetailStrength", detail != null ? detailStrength : 0f);
            m.enableInstancing = true;
            Cache[k] = m;
            return m;
        }

        private static string Key(string kind, Color c, float a = 0, float b = 0, Object o = null)
            => kind + ColorUtility.ToHtmlStringRGBA(c) + "|" + a.ToString("0.###") + "|" + b.ToString("0.###") + "|" + (o != null ? o.GetInstanceID() : 0);

        public static Material Lit(Color c, float smoothness = 0.15f, float metallic = 0f)
        {
            string k = Key("lit", c, smoothness, metallic);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            var catalogBase = Art.Material("Lit");
            m = catalogBase != null ? new Material(catalogBase) : new Material(LitShader);
            m.name = "GV_Lit_" + ColorUtility.ToHtmlStringRGB(c);
            SetColor(m, c);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smoothness);
            if (m.HasProperty(Metallic)) m.SetFloat(Metallic, metallic);
            m.enableInstancing = true;
            Cache[k] = m;
            return m;
        }

        /// <summary>Lit material with a texture (e.g. bark on procedural logs).</summary>
        public static Material LitTextured(Texture tex, Color tint, float smoothness = 0.1f)
        {
            if (tex == null) return Lit(tint, smoothness);
            string k = Key("littex", tint, smoothness, 0, tex);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(LitShader) { name = "GV_LitTex_" + tex.name };
            if (m.HasProperty(BaseMap)) m.SetTexture(BaseMap, tex);
            if (m.HasProperty(MainTex)) m.SetTexture(MainTex, tex);
            SetColor(m, tint);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smoothness);
            m.enableInstancing = true;
            Cache[k] = m;
            return m;
        }

        public static Material Emissive(Color baseColor, Color emission)
        {
            string k = Key("emis", baseColor, emission.r * 7 + emission.g * 3 + emission.b, emission.maxColorComponent);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            var catalogBase = Art.Material("LitEmissive");
            m = catalogBase != null ? new Material(catalogBase) : new Material(LitShader);
            m.name = "GV_Emissive";
            SetColor(m, baseColor);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Cache[k] = m;
            return m;
        }

        /// <summary>Unlit transparent overlay (tool previews, selection rings, markers).</summary>
        public static Material Overlay(Color tint, Texture tex = null, bool alwaysOnTop = false, float pulse = 0f, float scroll = 0f)
        {
            string k = Key("ovl" + (alwaysOnTop ? "T" : "D"), tint, pulse, scroll, tex);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(OverlayShader) { name = "GV_Overlay" };
            if (m.HasProperty(BaseMap) && tex != null) m.SetTexture(BaseMap, tex);
            if (m.HasProperty(MainTex) && tex != null) m.SetTexture(MainTex, tex);
            SetColor(m, tint);
            if (m.HasProperty("_Pulse")) m.SetFloat("_Pulse", pulse);
            if (m.HasProperty("_ScrollSpeed")) m.SetFloat("_ScrollSpeed", scroll);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)(alwaysOnTop ? CompareFunction.Always : CompareFunction.LessEqual));
            if (m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/Unlit")) MakeUrpTransparent(m);
            m.renderQueue = (int)RenderQueue.Transparent + (alwaysOnTop ? 40 : 20);
            Cache[k] = m;
            return m;
        }

        public static Material Particle(Texture tex, bool additive, float lit = 1f, Color? tint = null)
        {
            var c = tint ?? Color.white;
            string k = Key("ptc" + (additive ? "A" : "B"), c, lit, 0, tex);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(ParticleShader) { name = "GV_Particle" + (tex != null ? "_" + tex.name : "") };
            if (tex != null)
            {
                if (m.HasProperty(BaseMap)) m.SetTexture(BaseMap, tex);
                if (m.HasProperty(MainTex)) m.SetTexture(MainTex, tex);
            }
            SetColor(m, c);
            if (m.HasProperty("_Lit")) m.SetFloat("_Lit", lit);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/Particles")) MakeUrpTransparent(m, additive);
            m.renderQueue = (int)RenderQueue.Transparent + (additive ? 5 : 0);
            Cache[k] = m;
            return m;
        }

        /// <summary>A tinted copy of an existing material (cached per source + tint).</summary>
        public static Material Tinted(Material source, Color tint)
        {
            if (source == null) return Lit(tint);
            string k = Key("tint", tint, 0, 0, source);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(source) { name = source.name + "_Tint" };
            SetColor(m, source.HasProperty(BaseColor) ? source.GetColor(BaseColor) * tint : tint);
            Cache[k] = m;
            return m;
        }

        /// <summary>Keyed copy of a material whose properties the caller sets once (e.g. per-crop dryness).</summary>
        public static Material Variant(Material source, string variantKey)
        {
            if (source == null) return null;
            string k = "var|" + source.GetInstanceID() + "|" + variantKey;
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(source) { name = source.name + "_" + variantKey };
            Cache[k] = m;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c);
            if (m.HasProperty(Color_)) m.SetColor(Color_, c);
        }

        private static void MakeUrpTransparent(Material m, bool additive = false)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
        }

        /// <summary>Material for a blueprint primitive key (Wood, DarkWood, Plank, Rope, Stone, Soil, Straw, Cloth …).</summary>
        public static Material ForKey(string key)
        {
            var m = Art.Material(key);
            if (m != null) return m;
            switch (key)
            {
                case "Wood": return Lit(Palette.Hex(0x8A6A4A), 0.1f);
                case "DarkWood": return Lit(Palette.Hex(0x5A4232), 0.1f);
                case "Plank": return Lit(Palette.Hex(0xA3845E), 0.12f);
                case "Rope": return Lit(Palette.Hex(0xC9B48A), 0.05f);
                case "Stone": return Lit(Palette.Hex(0x8E918B), 0.2f);
                case "Soil": return Lit(Palette.Hex(0x6B5038), 0.05f);
                case "Straw": return Lit(Palette.Hex(0xD9BE72), 0.05f);
                case "Cloth": return Lit(Palette.Hex(0xD8CDB4), 0.05f);
                case "Bark": return Lit(Palette.Bark, 0.05f);
                case "EndGrain": return Lit(Palette.Hex(0xC9A57A), 0.08f);
                case "Water": return Lit(Palette.Water, 0.9f);
                case "Metal": return Lit(Palette.Hex(0x707478), 0.6f, 0.8f);
                default: return Lit(Color.magenta);
            }
        }
    }
}
