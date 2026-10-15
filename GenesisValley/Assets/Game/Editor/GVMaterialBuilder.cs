using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// Creates/updates every shared material (one per look, GDD §9) from the curated textures and remaps the FBX
    /// embedded materials onto them. Materials keep their GUIDs between runs, so re-running setup is safe.
    /// </summary>
    public static class GVMaterialBuilder
    {
        public const string UrpLit = "Universal Render Pipeline/Lit";
        public const string UrpTerrain = "Universal Render Pipeline/Terrain/Lit";
        public const string Foliage = "GenesisValley/Foliage";

        public sealed class Spec
        {
            public string Name, Shader = UrpLit, Base, Normal, Mask;
            public Color Color = Color.white;
            public float Smoothness = 0.2f, Metallic, NormalScale = 1f, Cutoff = 0.45f;
            public bool Transparent, TwoSided;
            // foliage
            public float VertexAO = 0.6f, Translucency = 0.4f, Wind = 1f, WindHeight = 6f, Flutter = 0.4f, ColorVariation = 0.12f;
        }

        private static string V(string f) => GVPaths.Village + "/Textures/" + f;
        private static string N(string f) => GVPaths.Nature + "/Textures/" + f;
        private static string C(string f) => GVPaths.Characters + "/Textures/" + f;

        private static Spec Trim(string name, string tex, string normalTex = null, string maskTex = null, Color? tint = null) => new Spec
        {
            Name = name, Base = V($"T_{tex}_BaseColor.jpg"), Normal = V($"T_{normalTex ?? tex}_Normal.png"), Mask = V($"T_{maskTex ?? normalTex ?? tex}_MaskMap.png"),
            Smoothness = 1f, Color = tint ?? Color.white,
        };

        private static Spec Leaves(string name, string tex, float windHeight, float wind = 1f, float flutter = 0.4f, float ao = 0.6f, Color? tint = null, float translucency = 0.42f) => new Spec
        {
            Name = name, Shader = Foliage, Base = N(tex), Color = tint ?? Color.white, Smoothness = 0.1f, Cutoff = 0.45f,
            WindHeight = windHeight, Wind = wind, Flutter = flutter, VertexAO = ao, Translucency = translucency, TwoSided = true,
        };

        public static List<Spec> Specs()
        {
            var l = new List<Spec>
            {
                // Medieval Village MegaKit (names match the FBX materials)
                Trim("MI_WoodTrim", "WoodTrim"),
                Trim("MI_WoodTrim_Wear", "WoodTrim", tint: new Color(0.86f, 0.83f, 0.78f)),
                Trim("MI_Plaster", "Plaster"),
                Trim("MI_RockTrim", "RockTrim"),
                Trim("MI_Brick", "Brick"),
                Trim("MI_RedBrick", "RedBrick", "Brick", "Brick"),
                Trim("MI_RoundTiles", "RoundTiles"),
                Trim("MI_UnevenBrick", "UnevenBrick"),
                new Spec { Name = "MI_MetalOrnaments", Color = new Color(0.22f, 0.21f, 0.2f), Metallic = 0.8f, Smoothness = 0.42f },
                new Spec { Name = "MI_WindowGlass", Base = V("T_WindowGradient.png"), Color = new Color(0.62f, 0.72f, 0.78f, 0.42f), Smoothness = 0.95f, Transparent = true },
                new Spec { Name = "MI_Vine", Shader = Foliage, Base = V("T_VineLeaf.png"), Color = new Color(0.3f, 0.5f, 0.16f), Cutoff = 0.4f, WindHeight = 3f, Wind = 0.4f, Flutter = 0.6f, VertexAO = 0f, TwoSided = true },

                // Stylized Nature MegaKit
                new Spec { Name = "Bark_NormalTree", Base = N("Bark_NormalTree.jpg"), Normal = N("Bark_NormalTree_Normal.png"), Smoothness = 0.08f },
                new Spec { Name = "Bark_DeadTree", Base = N("Bark_DeadTree.jpg"), Normal = N("Bark_DeadTree_Normal.png"), Smoothness = 0.08f },
                new Spec { Name = "Bark_TwistedTree", Base = N("Bark_TwistedTree.jpg"), Normal = N("Bark_TwistedTree_Normal.png"), Smoothness = 0.08f },
                Leaves("Leaves_NormalTree", "Leaves_NormalTree_C.png", 8f, 1f, 0.5f, 0.65f),
                Leaves("Leaves_Pine", "Leaf_Pine_C.png", 9f, 0.8f, 0.2f, 0.6f, null, 0.3f),
                Leaves("Leaves_TwistedTree", "Leaves_TwistedTree_Olive.png", 7f, 1f, 0.5f, 0.65f),
                Leaves("Leaves_Bush", "Leaves_Bush.png", 1.5f, 0.6f, 0.45f, 0.6f),
                Leaves("Leaves", "Leaves.png", 1f, 0.6f, 0.4f, 0.55f),
                Leaves("Flowers", "Flowers.png", 0.8f, 0.7f, 0.5f, 0.45f),
                Leaves("Grass", "Grass.png", 0.6f, 1f, 0.3f, 0.75f, new Color(0.52f, 0.68f, 0.32f), 0.5f),
                new Spec { Name = "Mushrooms", Base = N("Mushrooms.jpg"), Smoothness = 0.3f },
                new Spec { Name = "PathRocks", Base = N("PathRocks_Diffuse.jpg"), Smoothness = 0.16f },
                new Spec { Name = "Rocks", Base = N("Rocks_Diffuse.jpg"), Smoothness = 0.16f },

                // Characters (MI_ names drive VillagerRig's skin/hair/cloth tinting)
                new Spec { Name = "MI_Superhero_Male", Base = C("T_Superhero_Male_BaseColor.jpg"), Normal = C("T_Superhero_Male_Normal.png"), Mask = C("T_Superhero_Male_MaskMap.png"), Smoothness = 0.75f },
                new Spec { Name = "MI_Superhero_Female", Base = C("T_Superhero_Female_BaseColor.jpg"), Normal = C("T_Superhero_Female_Normal.png"), Mask = C("T_Superhero_Female_MaskMap.png"), Smoothness = 0.75f },
                new Spec { Name = "MI_Regular_Male", Base = C("T_Regular_Male_BaseColor.jpg"), Normal = C("T_Regular_Male_Normal.png"), Mask = C("T_Regular_Male_MaskMap.png"), Smoothness = 0.75f },
                new Spec { Name = "MI_Regular_Female", Base = C("T_Regular_Female_BaseColor.jpg"), Normal = C("T_Regular_Female_Normal.png"), Mask = C("T_Regular_Female_MaskMap.png"), Smoothness = 0.75f },
                new Spec { Name = "MI_Eyes", Base = C("T_Eye_Brown.png"), Smoothness = 0.85f },
                new Spec { Name = "MI_Hair_1", Base = C("T_Hair_1_BaseColor.jpg"), Normal = C("T_Hair_1_Normal.png"), Smoothness = 0.32f, TwoSided = true },
                new Spec { Name = "MI_Hair_2", Base = C("T_Hair_2_BaseColor.jpg"), Normal = C("T_Hair_2_Normal.png"), Smoothness = 0.32f, TwoSided = true },
                new Spec { Name = "MI_Peasant", Base = C("T_Peasant_BaseColor.jpg"), Normal = C("T_Peasant_Normal.png"), Mask = C("T_Peasant_MaskMap.png"), Smoothness = 1f },
                new Spec { Name = "MI_Peasant_2", Base = C("T_Peasant_2_BaseColor.jpg"), Normal = C("T_Peasant_Normal.png"), Mask = C("T_Peasant_MaskMap.png"), Smoothness = 1f },
                new Spec { Name = "MI_Ranger", Base = C("T_Ranger_BaseColor.jpg"), Normal = C("T_Ranger_Normal.png"), Mask = C("T_Ranger_MaskMap.png"), Smoothness = 1f, TwoSided = true },
                new Spec { Name = "MI_Ranger_3", Base = C("T_Ranger_3_BaseColor.jpg"), Normal = C("T_Ranger_Normal.png"), Mask = C("T_Ranger_MaskMap.png"), Smoothness = 1f, TwoSided = true },

                // Runtime bases + blueprint primitive looks (MaterialLibrary.ForKey)
                new Spec { Name = "GV_Lit", Smoothness = 0.15f },
                new Spec { Name = "GV_LitEmissive", Smoothness = 0.3f },
                new Spec { Name = "GV_Stone", Base = N("Rocks_Diffuse.jpg"), Color = new Color(0.92f, 0.92f, 0.9f), Smoothness = 0.18f },
                new Spec { Name = "GV_Soil", Base = GVPaths.PolyHaven + "/T_Ground_Soil_D.jpg", Normal = GVPaths.PolyHaven + "/T_Ground_Soil_N.jpg", Smoothness = 0.06f },
            };
            return l;
        }

        /// <summary>FBX material name → project material name, for the few kit models we recolour.</summary>
        public static string RemapName(string fbxName, string materialName)
        {
            if (fbxName.StartsWith("Bush_Common") && materialName == "Leaves_TwistedTree") return "Leaves_Bush";
            if (materialName == "Material") return "Bark_NormalTree";
            return materialName;
        }

        public static string MaterialPath(string name) => GVPaths.Materials + "/" + name + ".mat";

        public static Dictionary<string, Material> BuildAll(List<string> log)
        {
            GVPaths.EnsureFolder(GVPaths.Materials);
            var result = new Dictionary<string, Material>();
            foreach (var spec in Specs())
            {
                var m = Build(spec, log);
                if (m != null) result[spec.Name] = m;
            }
            var emissive = result.TryGetValue("GV_LitEmissive", out var e) ? e : null;
            if (emissive != null)
            {
                emissive.EnableKeyword("_EMISSION");
                emissive.SetColor("_EmissionColor", Color.black);
                emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(emissive);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        private static Texture2D Tex(string path, List<string> log, string owner)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) log?.Add($"⚠ 材质 {owner} 缺少贴图 {path}");
            return t;
        }

        public static Material Build(Spec s, List<string> log)
        {
            var shader = Shader.Find(s.Shader);
            if (shader == null) { log?.Add($"✖ 找不到着色器 {s.Shader}（材质 {s.Name}）"); return null; }
            string path = MaterialPath(s.Name);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = s.Name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;

            var baseTex = Tex(s.Base, log, s.Name);
            var normal = Tex(s.Normal, log, s.Name);
            var mask = Tex(s.Mask, log, s.Name);

            Set(m, "_BaseMap", baseTex); Set(m, "_MainTex", baseTex);
            SetC(m, "_BaseColor", s.Color); SetC(m, "_Color", s.Color);
            SetF(m, "_Smoothness", s.Smoothness);
            SetF(m, "_Cutoff", s.Cutoff);
            m.enableInstancing = true;

            if (s.Shader == UrpLit)
            {
                SetF(m, "_WorkflowMode", 1f);
                SetF(m, "_Metallic", s.Metallic);
                Set(m, "_BumpMap", normal); SetF(m, "_BumpScale", s.NormalScale);
                Keyword(m, "_NORMALMAP", normal != null);
                Set(m, "_MetallicGlossMap", mask);
                Keyword(m, "_METALLICSPECGLOSSMAP", mask != null);
                SetF(m, "_SmoothnessTextureChannel", 0f);
                Set(m, "_OcclusionMap", mask);
                SetF(m, "_OcclusionStrength", mask != null ? 1f : 0f);
                Keyword(m, "_OCCLUSIONMAP", mask != null);
                SetF(m, "_Cull", s.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
                SetF(m, "_SpecularHighlights", 1f); SetF(m, "_EnvironmentReflections", 1f);
                SetF(m, "_ReceiveShadows", 1f);
                if (s.Transparent) MakeTransparent(m); else MakeOpaque(m);
            }
            else if (s.Shader == Foliage)
            {
                SetF(m, "_VertexAO", s.VertexAO);
                SetF(m, "_Translucency", s.Translucency);
                SetF(m, "_WindStrength", s.Wind);
                SetF(m, "_WindHeight", s.WindHeight);
                SetF(m, "_WindFlutter", s.Flutter);
                SetF(m, "_ColorVariation", s.ColorVariation);
                SetF(m, "_Cull", s.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void MakeOpaque(Material m)
        {
            SetF(m, "_Surface", 0f); SetF(m, "_Blend", 0f); SetF(m, "_AlphaClip", 0f);
            SetF(m, "_SrcBlend", (float)BlendMode.One); SetF(m, "_DstBlend", (float)BlendMode.Zero); SetF(m, "_ZWrite", 1f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
            m.SetShaderPassEnabled("ShadowCaster", true);
        }

        private static void MakeTransparent(Material m)
        {
            SetF(m, "_Surface", 1f); SetF(m, "_Blend", 0f); SetF(m, "_AlphaClip", 0f);
            SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha); SetF(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha); SetF(m, "_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        public static void Set(Material m, string prop, Texture t) { if (m.HasProperty(prop)) m.SetTexture(prop, t); }
        public static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        public static void SetC(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
        public static void Keyword(Material m, string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }

        // ------------------------------------------------------------------ FBX remapping

        /// <summary>Points every embedded FBX material under <paramref name="folder"/> at the shared project materials.</summary>
        public static int RemapModels(string folder, Dictionary<string, Material> mats, List<string> log)
        {
            int changed = 0;
            var files = GVPaths.FilesIn(folder, "*.fbx", true);
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < files.Length; i++)
                {
                    var path = files[i];
                    if (i % 8 == 0) EditorUtility.DisplayProgressBar("创世谷 · 一键配置", "关联模型材质 " + Path.GetFileName(path), i / (float)files.Length);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null) continue;
                    string fbx = Path.GetFileNameWithoutExtension(path);
                    bool dirty = false;
                    var existing = importer.GetExternalObjectMap();
                    foreach (var name in EmbeddedMaterialNames(path))
                    {
                        string target = RemapName(fbx, name);
                        if (!mats.TryGetValue(target, out var mat) || mat == null) { log?.Add($"⚠ {fbx}: 材质 {name} 没有对应的项目材质"); continue; }
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                        if (existing.TryGetValue(id, out var cur) && cur == mat) continue;
                        importer.AddRemap(id, mat);
                        dirty = true;
                    }
                    if (dirty) { importer.SaveAndReimport(); changed++; }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            return changed;
        }

        /// <summary>Names of the materials an FBX defines (from its sub-assets or its current remap table).</summary>
        public static IEnumerable<string> EmbeddedMaterialNames(string path)
        {
            var names = new HashSet<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Material) names.Add(o.name);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null)
                foreach (var kv in importer.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
            // Renderers may reference remapped project materials already; collect their source names too.
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && names.Count == 0)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) names.Add(m.name);
            return names;
        }

        // ------------------------------------------------------------------ environment

        public static Material BuildSky(List<string> log)
        {
            var shader = Shader.Find("GenesisValley/Sky");
            if (shader == null) { log.Add("✖ 找不到 GenesisValley/Sky"); return null; }
            var m = LoadOrCreate("GV_Sky", shader);
            var hdr = AssetDatabase.LoadAssetAtPath<Texture>(GVPaths.PolyHaven + "/T_Sky_PartlyCloudy.hdr");
            if (hdr == null) log.Add("⚠ 天空 HDR 缺失，将使用程序化天空");
            Set(m, "_DayTex", hdr);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material BuildWater(List<string> log)
        {
            var shader = Shader.Find("GenesisValley/Water");
            if (shader == null) { log.Add("✖ 找不到 GenesisValley/Water"); return null; }
            var m = LoadOrCreate("GV_Water", shader);
            Set(m, "_NormalMap", AssetDatabase.LoadAssetAtPath<Texture>(GVPaths.ArtTextures + "/T_Water_Ripple_N.png"));
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material BuildTerrainMaterial(List<string> log)
        {
            var shader = Shader.Find(UrpTerrain);
            if (shader == null) { log.Add("⚠ 找不到 URP Terrain/Lit，地形将使用渲染管线默认材质"); return null; }
            var m = LoadOrCreate("GV_Terrain", shader);
            // Keywords are normally toggled by the terrain inspector; the runtime-built terrain needs them baked in so
            // the variants survive build stripping and the layer normal maps render.
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material LoadOrCreate(string name, Shader shader)
        {
            GVPaths.EnsureFolder(GVPaths.Materials);
            string path = MaterialPath(name);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }
    }
}
