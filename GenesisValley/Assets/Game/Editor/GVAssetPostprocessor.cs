using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// Import rules for every shipped asset so a fresh clone imports correctly without hand-tuning:
    /// models (static kit pieces, humanoid characters, the animation library), textures (normal/mask/foliage/UI/HDR),
    /// audio (music streams, ambience in memory, short effects decompressed) and fonts (dynamic CJK with OS fallbacks).
    /// Bumping <see cref="Version"/> re-imports everything that these rules touch.
    /// </summary>
    public sealed class GVAssetPostprocessor : AssetPostprocessor
    {
        private const uint Version = 4;
        public override uint GetVersion() => Version;

        private static bool IsOurs(string p) => GVPaths.Under(p, GVPaths.ThirdParty) || GVPaths.Under(p, GVPaths.Art);
        private static string FileName(string p) => Path.GetFileNameWithoutExtension(p);

        public static bool IsCharacterModel(string p) => GVPaths.Under(p, GVPaths.Characters);
        public static bool IsBaseCharacter(string p) => GVPaths.Under(p, GVPaths.Characters + "/Base");
        public static bool IsAnimationLibrary(string p) => GVPaths.Under(p, GVPaths.Animations);
        public static bool IsSmallPlant(string name) =>
            name.StartsWith("Grass_") || name.StartsWith("Clover_") || name.StartsWith("Flower_") || name.StartsWith("Plant_") || name.StartsWith("Fern_") || name.StartsWith("Petal_") || name.StartsWith("Mushroom_");

        /// <summary>UAL clips that are played as repeating work loops even though their names lack "_Loop".</summary>
        public static readonly string[] ExtraLoopingClips = { "Sword_Attack", "Fixing_Kneeling", "Interact", "PickUp_Table", "Spell_Simple_Shoot", "Punch_Jab", "Sword_Idle" };

        // ------------------------------------------------------------------ models

        private void OnPreprocessModel()
        {
            if (!GVPaths.Under(assetPath, GVPaths.Quaternius)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.generateSecondaryUV = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.useFileScale = true;
            mi.globalScale = 1f;

            if (IsAnimationLibrary(assetPath))
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.Optimal;
                mi.resampleCurves = true;
                mi.isReadable = false;
            }
            else if (IsCharacterModel(assetPath))
            {
                // Base bodies drive the humanoid avatar; hair and outfits are re-bound onto that skeleton by the
                // character builder, so their own rig only needs to keep the skinning data.
                mi.animationType = IsBaseCharacter(assetPath) ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.optimizeGameObjects = false;
                mi.isReadable = true; // the builder cuts the head out of the full body mesh
                mi.skinWeights = ModelImporterSkinWeights.Standard;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                // Terrain details and crop batches use these meshes directly.
                mi.isReadable = IsSmallPlant(FileName(assetPath));
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsAnimationLibrary(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                bool loop = n.EndsWith("_Loop", StringComparison.Ordinal) || Array.IndexOf(ExtraLoopingClips, n) >= 0;
                c.loopTime = loop;
                c.loopPose = n.EndsWith("_Loop", StringComparison.Ordinal);
                // In-place clips: bake root motion into the pose so villagers stay where the simulation puts them.
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                c.heightFromFeet = false;
                c.mirror = false;
            }
            mi.clipAnimations = clips;
        }

        // ------------------------------------------------------------------ textures

        private void OnPreprocessTexture()
        {
            if (!IsOurs(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string f = FileName(assetPath);
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();

            if (GVPaths.Under(assetPath, GVPaths.ArtIcons))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 256;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.spritePixelsPerUnit = 100;
                return;
            }

            ti.textureType = TextureImporterType.Default;
            ti.anisoLevel = 4;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.Compressed;

            if (ext == ".hdr" || ext == ".exr")
            {
                ti.sRGBTexture = false;
                ti.mipmapEnabled = false; // equirect sky sampled per pixel; avoids the mip seam at the wrap
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }

            bool normal = f.EndsWith("_Normal", StringComparison.Ordinal) || f.EndsWith("_N", StringComparison.Ordinal) || f.EndsWith("_Normal_N", StringComparison.Ordinal);
            if (normal)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = false;
            }
            else if (f.EndsWith("_MaskMap", StringComparison.Ordinal))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }

            bool foliage = f.StartsWith("Leaves") || f.StartsWith("Leaf_") || f == "Flowers" || f == "Grass" || f == "T_VineLeaf";
            if (foliage)
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipMapsPreserveCoverage = true;
                ti.alphaTestReferenceValue = 0.45f;
                ti.wrapMode = TextureWrapMode.Clamp;
            }

            if (f.StartsWith("T_FX_") || f.StartsWith("T_UI_"))
            {
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.wrapMode = f == "T_UI_Dash" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }

            if (f.StartsWith("T_Ground_")) { ti.anisoLevel = 8; ti.maxTextureSize = 2048; }
            if (GVPaths.Under(assetPath, GVPaths.Characters)) ti.anisoLevel = 2;
        }

        // ------------------------------------------------------------------ audio

        private void OnPreprocessAudio()
        {
            if (!IsOurs(assetPath)) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            if (assetPath.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.6f;
                ai.loadInBackground = true;
            }
            else if (assetPath.Contains("/Ambience/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.6f;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.75f;
            }
            ai.defaultSampleSettings = s;
            ai.forceToMono = GVPaths.Under(assetPath, GVPaths.Kenney);
        }

        // ------------------------------------------------------------------ fonts

        private void OnPreprocessAsset()
        {
            if (!(assetImporter is TrueTypeFontImporter fi) || !GVPaths.Under(assetPath, GVPaths.Fonts)) return;
            fi.fontTextureCase = FontTextureCase.Dynamic;
            fi.includeFontData = true;
            fi.fontRenderingMode = FontRenderingMode.Smooth;
            // Rare characters typed by the player (world names) fall back to the system's CJK fonts.
            fi.fontNames = new[] { FileName(assetPath).Contains("Serif") ? "Noto Serif SC" : "Noto Sans SC", "Microsoft YaHei", "PingFang SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS" };
        }
    }
}
