using System.IO;
using UnityEditor;
using UnityEngine;

namespace GenesisValley.EditorTools
{
    /// <summary>Folder layout shared by every editor tool (see docs/ASSET_LEDGER.md).</summary>
    public static class GVPaths
    {
        public const string ThirdParty = "Assets/ThirdParty";
        public const string Quaternius = ThirdParty + "/Quaternius";
        public const string Village = Quaternius + "/MedievalVillage";
        public const string Nature = Quaternius + "/StylizedNature";
        public const string Characters = Quaternius + "/Characters";
        public const string Animations = Quaternius + "/Animations";
        public const string PolyHaven = ThirdParty + "/PolyHaven";
        public const string Kenney = ThirdParty + "/Kenney/Audio";
        public const string Fonts = ThirdParty + "/Fonts";

        public const string Art = "Assets/Game/Art";
        public const string ArtTextures = Art + "/Textures";
        public const string ArtIcons = Art + "/Icons";
        public const string ArtAudio = Art + "/Audio";
        public const string Generated = Art + "/Generated";
        public const string Materials = Generated + "/Materials";
        public const string TerrainLayers = Generated + "/Terrain";
        public const string CharacterOut = Generated + "/Characters";
        public const string Resources = "Assets/Game/Resources";
        public const string Catalog = Resources + "/GV_ArtCatalog.asset";
        public const string Scenes = "Assets/Game/Scenes";

        public static bool Under(string assetPath, string folder) =>
            assetPath != null && assetPath.Replace('\\', '/').StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase);

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Scene extension used by this editor (Tuanjie saves ".scene", Unity ".unity").</summary>
        public static string SceneExtension
        {
            get
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    var ext = Path.GetExtension(p);
                    if (!string.IsNullOrEmpty(ext)) return ext;
                }
                return Application.unityVersion.Contains("t") ? ".scene" : ".unity";
            }
        }

        public static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        public static string[] FilesIn(string folder, string pattern, bool recursive = false)
        {
            if (!Directory.Exists(folder)) return new string[0];
            var files = Directory.GetFiles(folder, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++) files[i] = files[i].Replace('\\', '/');
            System.Array.Sort(files, System.StringComparer.Ordinal);
            return files;
        }
    }
}
