using System;
using System.Collections.Generic;
using UnityEngine;

namespace GenesisValley.Presentation
{
    [Serializable] public sealed class ModelEntry { public string key; public GameObject prefab; }
    [Serializable] public sealed class MaterialEntry { public string key; public Material material; }
    [Serializable] public sealed class SoundEntry { public string key; public AudioClip[] clips = new AudioClip[0]; [Range(0, 1)] public float volume = 1f; }
    [Serializable] public sealed class SpriteEntry { public string key; public Sprite sprite; }
    [Serializable] public sealed class TextureEntry { public string key; public Texture texture; }

    /// <summary>Terrain layer slots, in the order <see cref="ArtCatalog.terrainLayers"/> must use.</summary>
    public enum GroundLayer { Grass = 0, Meadow = 1, ForestFloor = 2, Path = 3, Cliff = 4, Riverbank = 5, Soil = 6 }

    /// <summary>
    /// Every piece of approved art the presentation layer may use, looked up by stable string keys (GDD §8/§9).
    /// Filled by menu "Genesis Valley/一键配置" from Assets/ThirdParty. Every lookup is optional: a missing slot falls
    /// back to procedural geometry so the game always runs, even straight after cloning the repository.
    /// Prefabs describe looks only — never world state.
    /// </summary>
    [CreateAssetMenu(menuName = "Genesis Valley/Art Catalog", fileName = "ArtCatalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [Header("Models (key = FBX file name, e.g. Wall_Plaster_Straight, CommonTree_3)")]
        public List<ModelEntry> models = new List<ModelEntry>();
        [Header("Materials (blueprint primitives Wood/DarkWood/Plank/Rope/Stone/Soil/Straw/Cloth, fx, foliage …)")]
        public List<MaterialEntry> materials = new List<MaterialEntry>();
        public List<SoundEntry> sounds = new List<SoundEntry>();
        public List<SpriteEntry> icons = new List<SpriteEntry>();
        public List<TextureEntry> textures = new List<TextureEntry>();

        [Header("Villagers")]
        public GameObject maleVillager;
        public GameObject femaleVillager;
        public RuntimeAnimatorController villagerAnimator;

        [Header("Terrain (index = GroundLayer)")]
        public TerrainLayer[] terrainLayers = new TerrainLayer[0];
        public Material terrainMaterial;

        [Header("Environment")]
        public Material skyMaterial;
        public Material waterMaterial;

        [Header("Shaders kept in the build (looked up at runtime)")]
        public Shader litShader;
        public Shader foliageShader;
        public Shader overlayShader;
        public Shader particleShader;
        public Shader waterShader;
        public Shader skyShader;
        public Shader vertexLitShader;

        [Header("UI")]
        public Font uiFont;
        public Font titleFont;

        [Header("Music")]
        public AudioClip[] music = new AudioClip[0];

        [NonSerialized] private Dictionary<string, GameObject> _models;
        [NonSerialized] private Dictionary<string, Material> _materials;
        [NonSerialized] private Dictionary<string, SoundEntry> _sounds;
        [NonSerialized] private Dictionary<string, Sprite> _icons;
        [NonSerialized] private Dictionary<string, Texture> _textures;

        private static ArtCatalog _empty;
        /// <summary>Catalog used when the scene has none configured: every lookup returns null → procedural fallbacks.</summary>
        public static ArtCatalog Empty
        {
            get
            {
                if (_empty == null) { _empty = CreateInstance<ArtCatalog>(); _empty.name = "ArtCatalog (empty)"; }
                return _empty;
            }
        }

        public void Invalidate() { _models = null; _materials = null; _sounds = null; _icons = null; _textures = null; }

        private static Dictionary<string, T> Build<TEntry, T>(List<TEntry> list, Func<TEntry, string> key, Func<TEntry, T> value) where T : class
        {
            var d = new Dictionary<string, T>(StringComparer.Ordinal);
            if (list == null) return d;
            foreach (var e in list)
            {
                if (e == null) continue;
                var k = key(e); var v = value(e);
                if (!string.IsNullOrEmpty(k) && v != null) d[k] = v;
            }
            return d;
        }

        public GameObject Model(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_models == null) _models = Build(models, e => e.key, e => e.prefab);
            return _models.TryGetValue(key, out var m) && m != null ? m : null;
        }

        public bool HasModel(string key) => Model(key) != null;

        public Material Material(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_materials == null) _materials = Build(materials, e => e.key, e => e.material);
            return _materials.TryGetValue(key, out var m) && m != null ? m : null;
        }

        public SoundEntry Sound(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_sounds == null) _sounds = Build(sounds, e => e.key, e => e);
            return _sounds.TryGetValue(key, out var s) && s.clips != null && s.clips.Length > 0 ? s : null;
        }

        public Sprite Icon(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_icons == null) _icons = Build(icons, e => e.key, e => e.sprite);
            return _icons.TryGetValue(key, out var s) && s != null ? s : null;
        }

        public Texture Texture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_textures == null) _textures = Build(textures, e => e.key, e => e.texture);
            return _textures.TryGetValue(key, out var t) && t != null ? t : null;
        }

        public TerrainLayer Layer(GroundLayer l)
        {
            int i = (int)l;
            return terrainLayers != null && i < terrainLayers.Length ? terrainLayers[i] : null;
        }

        /// <summary>Re-reads every list after the editor setup rewrote them.</summary>
        private void OnValidate() => Invalidate();
    }
}
