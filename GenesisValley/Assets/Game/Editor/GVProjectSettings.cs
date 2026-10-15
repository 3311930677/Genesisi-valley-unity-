using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// Player, quality and URP settings tuned for the shipped look: depth texture for water/soft particles,
    /// HDR + soft shadows, cascades per quality tier, SSAO strength, product naming and app icon.
    /// URP assets are edited through SerializedObject so this works across URP 12–14 field layouts.
    /// </summary>
    public static class GVProjectSettings
    {
        public const string ProductName = "创世谷 Genesis Valley";
        public const string CompanyName = "Genesis Valley Studio";
        public const string Version = "0.9.0";
        public const string IconPath = GVPaths.Art + "/Branding/GV_AppIcon.png";

        private struct Tier
        {
            public string File; public int Msaa, ShadowRes, Cascades; public float ShadowDistance; public bool Hdr, Soft;
            public float AoIntensity, AoRadius;
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { File = "Assets/Settings/URP-Performant.asset",   Msaa = 1, ShadowRes = 1024, Cascades = 2, ShadowDistance = 70f,  Hdr = true, Soft = false, AoIntensity = 0f,   AoRadius = 0.3f },
            new Tier { File = "Assets/Settings/URP-Balanced.asset",     Msaa = 2, ShadowRes = 2048, Cascades = 3, ShadowDistance = 110f, Hdr = true, Soft = true,  AoIntensity = 0.6f, AoRadius = 0.35f },
            new Tier { File = "Assets/Settings/URP-HighFidelity.asset", Msaa = 4, ShadowRes = 4096, Cascades = 4, ShadowDistance = 150f, Hdr = true, Soft = true,  AoIntensity = 0.8f, AoRadius = 0.4f },
        };

        public static void Apply(List<string> log)
        {
            ApplyPlayer(log);
            int tuned = 0;
            foreach (var t in Tiers) if (ApplyTier(t, log)) tuned++;
            log.Add("✔ URP 画质档位已调整 " + tuned + "/" + Tiers.Length + "（深度纹理、HDR、阴影级联、SSAO）");
            AssetDatabase.SaveAssets();
        }

        private static void ApplyPlayer(List<string> log)
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.genesisvalley.game");

            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null)
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
                log.Add("✔ 应用图标已设置");
            }
            else log.Add("⚠ 未找到应用图标 " + IconPath);

            // Default quality for desktop = High Fidelity (index 2 in the template project).
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
                if (names[i].Replace(" ", "").ToLowerInvariant().Contains("highfidelity")) { QualitySettings.SetQualityLevel(i, true); break; }

            log.Add("✔ 玩家设置：" + ProductName + " v" + Version + "，1920×1080 全屏窗口，线性色彩空间");
        }

        private static bool ApplyTier(Tier t, List<string> log)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(t.File);
            if (asset == null) { log.Add("⚠ 未找到 URP 资源 " + t.File); return false; }
            var so = new SerializedObject(asset);
            SetInt(so, "m_RequireDepthTexture", 1);
            SetInt(so, "m_RequireOpaqueTexture", 0);
            SetInt(so, "m_SupportsHDR", t.Hdr ? 1 : 0);
            SetInt(so, "m_MSAA", t.Msaa);
            SetInt(so, "m_MainLightRenderingMode", 1);
            SetInt(so, "m_MainLightShadowsSupported", 1);
            SetInt(so, "m_MainLightShadowmapResolution", t.ShadowRes);
            SetFloat(so, "m_ShadowDistance", t.ShadowDistance);
            SetInt(so, "m_ShadowCascadeCount", t.Cascades);
            SetInt(so, "m_SoftShadowsSupported", t.Soft ? 1 : 0);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetFloat(so, "m_ShadowDepthBias", 0.6f);
            SetFloat(so, "m_ShadowNormalBias", 0.8f);
            SetInt(so, "m_ColorGradingMode", t.Hdr ? 1 : 0);   // HDR grading for the dusk/night palette
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            // Renderer data: SSAO feature lives as a sub-asset of the renderer.
            var rendererPath = t.File.Replace(".asset", "-Renderer.asset");
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(rendererPath))
            {
                if (sub == null || sub.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var sso = new SerializedObject(sub);
                var active = sso.FindProperty("m_Active");
                if (active != null) active.boolValue = t.AoIntensity > 0f;
                SetFloat(sso, "m_Settings.Intensity", t.AoIntensity);
                SetFloat(sso, "m_Settings.Radius", t.AoRadius);
                SetFloat(sso, "m_Settings.DirectLightingStrength", 0.3f);
                sso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(sub);
            }
            return true;
        }

        private static void SetInt(SerializedObject so, string path, int v)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: p.boolValue = v != 0; break;
                case SerializedPropertyType.Enum: p.intValue = v; break;   // raw enum value (MSAA 1/2/4, shadow res 1024…)
                case SerializedPropertyType.Float: p.floatValue = v; break;
                default: p.intValue = v; break;
            }
        }

        private static void SetFloat(SerializedObject so, string path, float v)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
            else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(v);
        }
    }
}
