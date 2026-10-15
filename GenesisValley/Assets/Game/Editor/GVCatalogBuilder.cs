using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenesisValley.Presentation;
using UnityEditor;
using UnityEngine;

namespace GenesisValley.EditorTools
{
    /// <summary>Fills <see cref="ArtCatalog"/> (models, materials, textures, icons, sounds, terrain, sky, fonts, music).</summary>
    public static class GVCatalogBuilder
    {
        private static readonly float[] LayerTiles = { 9f, 12f, 8f, 5f, 16f, 5f, 4f };
        private static readonly float[] LayerSmooth = { 0.05f, 0.05f, 0.04f, 0.08f, 0.12f, 0.22f, 0.03f };

        // key, volume, sources ("k:" Kenney file, "g:" generated file under Art/Audio)
        private static readonly (string key, float volume, string[] files)[] SoundTable =
        {
            ("ui_click", 0.55f, new[] { "k:click_002", "k:click_003" }),
            ("ui_hover", 0.22f, new[] { "k:tick_001", "k:tick_002" }),
            ("select", 0.6f, new[] { "k:select_002", "k:select_003" }),
            ("tool_select", 0.55f, new[] { "k:toggle_002", "k:toggle_003" }),
            ("error", 0.55f, new[] { "k:error_004", "k:error_006" }),
            ("save", 0.7f, new[] { "k:confirmation_001" }),
            ("plan", 0.7f, new[] { "k:drop_002", "k:drop_003" }),
            ("undo", 0.7f, new[] { "k:back_002", "k:back_003" }),
            ("door_open", 0.8f, new[] { "k:doorOpen_1", "k:doorOpen_2" }),
            ("door_close", 0.8f, new[] { "k:doorClose_1", "k:doorClose_2", "k:doorClose_3", "k:doorClose_4" }),
            ("build_knock", 0.75f, Seq("k:impactWood_light_00", 5)),
            ("build_tile", 0.55f, Seq("k:impactPlate_light_00", 5)),
            ("build_complete", 0.9f, new[] { "k:impactWood_heavy_000", "k:impactWood_heavy_002", "k:impactWood_heavy_003" }),
            ("hammer", 0.75f, Seq("k:impactPlank_medium_00", 5)),
            ("chop", 0.9f, new[] { "k:chop" }.Concat(Seq("k:impactWood_medium_00", 5)).ToArray()),
            ("tree_creak", 0.8f, new[] { "k:creak1", "k:creak2", "k:creak3" }),
            ("tree_fall", 1f, new[] { "g:Sfx/tree_fall" }),
            ("harvest", 0.7f, new[] { "k:cloth1", "k:cloth2", "k:cloth3", "k:cloth4", "k:handleSmallLeather" }),
            ("harvest_cut", 0.6f, new[] { "k:knifeSlice", "k:knifeSlice2" }),
            ("step_grass", 0.45f, Seq("k:footstep_grass_00", 5)),
            ("step_dirt", 0.45f, Seq("k:footstep0", 5)),
            ("step_wood", 0.5f, Seq("k:footstep_wood_00", 5)),
            ("milestone", 1f, new[] { "g:Sfx/milestone" }),
            ("complete_chime", 1f, new[] { "g:Sfx/complete_chime" }),
            ("summon", 1f, new[] { "g:Sfx/summon" }),
            ("poof", 0.9f, new[] { "g:Sfx/poof" }),
            ("river_create", 1f, new[] { "g:Sfx/river_create" }),
            ("water_rush", 0.9f, new[] { "g:Sfx/water_rush" }),
            ("forest_plant", 1f, new[] { "g:Sfx/forest_plant" }),
            ("splash", 0.9f, new[] { "g:Sfx/splash" }),
            ("splash_small", 0.8f, new[] { "g:Sfx/splash_small" }),
            ("water_scoop", 0.8f, new[] { "g:Sfx/water_scoop" }),
            ("warning", 0.8f, new[] { "g:Sfx/warning" }),
            ("hint", 0.7f, new[] { "g:Sfx/hint" }),
            ("amb_day", 1f, new[] { "g:Ambience/amb_day" }),
            ("amb_night", 1f, new[] { "g:Ambience/amb_night" }),
            ("amb_wind", 0.85f, new[] { "g:Ambience/amb_wind" }),
            ("amb_river", 1f, new[] { "g:Ambience/amb_river" }),
            ("amb_fire", 1f, new[] { "g:Ambience/amb_fire" }),
            ("terrain_rumble", 0.9f, new[] { "g:Ambience/terrain_rumble" }),
        };

        private static string[] Seq(string prefix, int n) => Enumerable.Range(0, n).Select(i => prefix + i).ToArray();

        public static ArtCatalog LoadOrCreate()
        {
            GVPaths.EnsureFolder(GVPaths.Resources);
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(GVPaths.Catalog);
            if (cat == null) { cat = ScriptableObject.CreateInstance<ArtCatalog>(); AssetDatabase.CreateAsset(cat, GVPaths.Catalog); }
            return cat;
        }

        public static TerrainLayer[] BuildTerrainLayers(List<string> log)
        {
            GVPaths.EnsureFolder(GVPaths.TerrainLayers);
            var names = System.Enum.GetNames(typeof(GroundLayer));
            var layers = new TerrainLayer[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GVPaths.PolyHaven}/T_Ground_{n}_D.jpg");
                var nm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GVPaths.PolyHaven}/T_Ground_{n}_N.jpg");
                if (d == null) { log.Add($"⚠ 地表贴图缺失：T_Ground_{n}_D"); continue; }
                string path = $"{GVPaths.TerrainLayers}/TL_{n}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                bool create = l == null;
                if (create) l = new TerrainLayer();
                l.name = "TL_" + n;
                l.diffuseTexture = d;
                l.normalMapTexture = nm;
                l.normalScale = 1f;
                l.tileSize = new Vector2(LayerTiles[i], LayerTiles[i]);
                l.tileOffset = Vector2.zero;
                l.smoothness = LayerSmooth[i];
                l.metallic = 0f;
                if (create) AssetDatabase.CreateAsset(l, path); else EditorUtility.SetDirty(l);
                layers[i] = l;
            }
            return layers;
        }

        private static AudioClip Audio(string spec)
        {
            string path = spec.StartsWith("k:") ? $"{GVPaths.Kenney}/{spec.Substring(2)}.ogg" : $"{GVPaths.ArtAudio}/{spec.Substring(2)}.ogg";
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        public static void Fill(ArtCatalog cat, Dictionary<string, Material> mats, TerrainLayer[] layers, Material terrain, Material sky, Material water,
            GameObject male, GameObject female, RuntimeAnimatorController controller, List<string> log)
        {
            // models: every kit FBX by file name
            cat.models.Clear();
            foreach (var folder in new[] { GVPaths.Village + "/Models", GVPaths.Nature + "/Models" })
                foreach (var f in GVPaths.FilesIn(folder, "*.fbx"))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(f);
                    if (go != null) cat.models.Add(new ModelEntry { key = Path.GetFileNameWithoutExtension(f), prefab = go });
                }

            // materials: runtime bases, primitive looks, and every shared material by name
            cat.materials.Clear();
            void M(string key, string mat) { if (mats.TryGetValue(mat, out var m) && m != null) cat.materials.Add(new MaterialEntry { key = key, material = m }); }
            M("Lit", "GV_Lit");
            M("LitEmissive", "GV_LitEmissive");
            M("Bark", "Bark_NormalTree");
            M("Stone", "GV_Stone");
            M("Soil", "GV_Soil");
            foreach (var kv in mats.OrderBy(k => k.Key)) if (!kv.Key.StartsWith("GV_")) cat.materials.Add(new MaterialEntry { key = kv.Key, material = kv.Value });

            // textures: key = file name without the "T_" prefix
            cat.textures.Clear();
            foreach (var f in GVPaths.FilesIn(GVPaths.ArtTextures, "*.png").Concat(GVPaths.FilesIn(GVPaths.PolyHaven, "*.jpg")).Concat(GVPaths.FilesIn(GVPaths.PolyHaven, "*.hdr")))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture>(f);
                if (t == null) continue;
                string k = Path.GetFileNameWithoutExtension(f);
                if (k.StartsWith("T_")) k = k.Substring(2);
                cat.textures.Add(new TextureEntry { key = k, texture = t });
                if (k == "Sky_PartlyCloudy") cat.textures.Add(new TextureEntry { key = "Sky_Day", texture = t });
            }

            cat.icons.Clear();
            foreach (var f in GVPaths.FilesIn(GVPaths.ArtIcons, "*.png"))
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(f);
                if (s != null) cat.icons.Add(new SpriteEntry { key = Path.GetFileNameWithoutExtension(f), sprite = s });
                else log.Add($"⚠ 图标未按 Sprite 导入：{f}");
            }

            cat.sounds.Clear();
            int missingClips = 0;
            foreach (var (key, volume, files) in SoundTable)
            {
                var clips = new List<AudioClip>();
                foreach (var spec in files) { var c = Audio(spec); if (c != null) clips.Add(c); else missingClips++; }
                if (clips.Count > 0) cat.sounds.Add(new SoundEntry { key = key, clips = clips.ToArray(), volume = volume });
                else log.Add($"⚠ 音效 {key} 没有可用的音频文件");
            }
            if (missingClips > 0) log.Add($"⚠ 有 {missingClips} 个音频文件缺失");
            cat.music = GVPaths.FilesIn(GVPaths.ArtAudio + "/Music", "*.ogg").Select(f => AssetDatabase.LoadAssetAtPath<AudioClip>(f)).Where(c => c != null).ToArray();

            cat.maleVillager = male;
            cat.femaleVillager = female;
            cat.villagerAnimator = controller;
            cat.terrainLayers = layers;
            cat.terrainMaterial = terrain;
            cat.skyMaterial = sky;
            cat.waterMaterial = water;

            cat.litShader = Shader.Find("Universal Render Pipeline/Lit");
            cat.foliageShader = Shader.Find("GenesisValley/Foliage");
            cat.overlayShader = Shader.Find("GenesisValley/Overlay");
            cat.particleShader = Shader.Find("GenesisValley/Particle");
            cat.waterShader = Shader.Find("GenesisValley/Water");
            cat.skyShader = Shader.Find("GenesisValley/Sky");
            cat.vertexLitShader = Shader.Find("GenesisValley/VertexLit");

            cat.uiFont = AssetDatabase.LoadAssetAtPath<Font>(GVPaths.Fonts + "/NotoSansSC-Medium-Subset.otf");
            cat.titleFont = AssetDatabase.LoadAssetAtPath<Font>(GVPaths.Fonts + "/NotoSerifSC-Bold-Subset.otf");
            if (cat.uiFont == null) log.Add("⚠ 界面字体缺失（将使用系统字体）");

            cat.Invalidate();
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            log.Add($"✔ 美术目录：模型 {cat.models.Count}，材质 {cat.materials.Count}，贴图 {cat.textures.Count}，图标 {cat.icons.Count}，音效 {cat.sounds.Count}，音乐 {cat.music.Length}");
        }
    }
}
