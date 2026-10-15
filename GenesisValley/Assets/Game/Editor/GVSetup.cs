using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GenesisValley.Presentation;
using GenesisValley.Presentation.Blueprints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// "Genesis Valley" editor menu: one-click setup (import rules → materials → terrain/sky/water → villagers →
    /// animator → art catalog → URP/player settings → main scene), Windows build, and quick play.
    /// Everything is idempotent; run it again after pulling new assets.
    /// </summary>
    [InitializeOnLoad]
    public static class GVSetup
    {
        public const string Menu = "Genesis Valley/";
        private const string Title = "创世谷 · 一键配置";
        public static string MainScenePath => GVPaths.Scenes + "/Main" + GVPaths.SceneExtension;
        public const string BuildFolder = "Builds/Windows";
        public const string ExeName = "GenesisValley.exe";

        static GVSetup()
        {
            EditorApplication.delayCall += PromptFirstRun;
        }

        private static void PromptFirstRun()
        {
            if (Application.isBatchMode || SessionState.GetBool("GV_SetupPrompted", false)) return;
            SessionState.SetBool("GV_SetupPrompted", true);
            bool configured = AssetDatabase.LoadAssetAtPath<ArtCatalog>(GVPaths.Catalog) != null && File.Exists(MainScenePath);
            if (configured) return;
            if (!AssetDatabase.IsValidFolder(GVPaths.Quaternius)) return; // assets not unpacked yet – setup would only report errors
            if (EditorUtility.DisplayDialog("创世谷 Genesis Valley", "检测到项目还没有完成配置（美术目录 / 主场景）。\n\n现在运行「一键配置」吗？大约需要 2–5 分钟，期间会重新导入模型。", "立即配置", "稍后"))
                RunSetup();
        }

        // ------------------------------------------------------------------ menu

        [MenuItem(Menu + "一键配置 (Setup Everything)", priority = 1)]
        public static void RunSetupMenu() => RunSetup();

        [MenuItem(Menu + "打开主场景", priority = 20)]
        public static void OpenMain()
        {
            if (!File.Exists(MainScenePath)) { EditorUtility.DisplayDialog("创世谷", "主场景还不存在，请先运行「一键配置」。", "好"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MainScenePath);
        }

        [MenuItem(Menu + "运行游戏 ▶", priority = 21)]
        public static void Play()
        {
            if (!File.Exists(MainScenePath)) RunSetup();
            if (!File.Exists(MainScenePath)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MainScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem(Menu + "构建 Windows 版本", priority = 40)]
        public static void BuildWindowsMenu() => BuildWindows(true);

        [MenuItem(Menu + "打开存档文件夹", priority = 60)]
        public static void OpenSaves()
        {
            Directory.CreateDirectory(GameBootstrap.SaveDirectory);
            EditorUtility.RevealInFinder(GameBootstrap.SaveDirectory);
        }

        [MenuItem(Menu + "查看配置报告", priority = 61)]
        public static void OpenReport()
        {
            var p = GVPaths.Generated + "/SetupReport.md";
            var a = AssetDatabase.LoadAssetAtPath<TextAsset>(p);
            if (a != null) { AssetDatabase.OpenAsset(a); return; }
            EditorUtility.DisplayDialog("创世谷", "还没有配置报告，请先运行「一键配置」。", "好");
        }

        /// <summary>Batch entry: Tuanjie.exe -batchmode -quit -projectPath … -executeMethod GenesisValley.EditorTools.GVSetup.SetupBatch</summary>
        public static void SetupBatch()
        {
            bool ok = RunSetup(false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Batch entry: … -executeMethod GenesisValley.EditorTools.GVSetup.BuildBatch</summary>
        public static void BuildBatch()
        {
            bool ok = RunSetup(false) && BuildWindows(false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        // ------------------------------------------------------------------ setup

        public static bool RunSetup(bool interactive = true)
        {
            var log = new List<string>();
            var t0 = DateTime.Now;
            bool ok = true;
            try
            {
                if (!AssetDatabase.IsValidFolder(GVPaths.Quaternius))
                {
                    log.Add("✖ 没有找到 Assets/ThirdParty/Quaternius。请先把美术资源包解压到项目的 Assets 目录（见 docs/SETUP.md）。");
                    ok = false;
                }
                else
                {
                    Step(0.02f, "刷新资源");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                    Step(0.08f, "创建共享材质");
                    var mats = GVMaterialBuilder.BuildAll(log);

                    Step(0.15f, "关联模型材质（会重新导入模型）");
                    int remapped = 0;
                    remapped += GVMaterialBuilder.RemapModels(GVPaths.Village, mats, log);
                    remapped += GVMaterialBuilder.RemapModels(GVPaths.Nature, mats, log);
                    remapped += GVMaterialBuilder.RemapModels(GVPaths.Characters, mats, log);
                    log.Add($"✔ 材质 {mats.Count} 个，重新关联模型 {remapped} 个");

                    Step(0.55f, "地形图层 / 天空 / 水体");
                    var layers = GVCatalogBuilder.BuildTerrainLayers(log);
                    var terrain = GVMaterialBuilder.BuildTerrainMaterial(log);
                    var sky = GVMaterialBuilder.BuildSky(log);
                    var water = GVMaterialBuilder.BuildWater(log);

                    Step(0.62f, "动画控制器");
                    var controller = GVCharacterBuilder.BuildController(log);

                    Step(0.68f, "村民角色");
                    var male = GVCharacterBuilder.BuildVillager(false, controller, mats, log);
                    var female = GVCharacterBuilder.BuildVillager(true, controller, mats, log);

                    Step(0.8f, "美术目录");
                    var cat = GVCatalogBuilder.LoadOrCreate();
                    GVCatalogBuilder.Fill(cat, mats, layers, terrain, sky, water, male, female, controller, log);
                    Validate(cat, log);

                    Step(0.88f, "渲染管线与玩家设置");
                    GVProjectSettings.Apply(log);

                    Step(0.94f, "主场景");
                    BuildScene(cat, sky, log);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception e)
            {
                ok = false;
                log.Add("✖ 配置中断：" + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (log.Any(l => l.StartsWith("✖"))) ok = false;
            var secs = (DateTime.Now - t0).TotalSeconds;
            WriteReport(log, ok, secs);
            var summary = string.Join("\n", log.Where(l => l.StartsWith("✖")).Concat(log.Where(l => l.StartsWith("✔"))).Take(18));
            int warnings = log.Count(l => l.StartsWith("⚠"));
            foreach (var l in log)
            {
                if (l.StartsWith("✖")) Debug.LogError("[Genesis Valley] " + l);
                else if (l.StartsWith("⚠")) Debug.LogWarning("[Genesis Valley] " + l);
                else Debug.Log("[Genesis Valley] " + l);
            }
            if (interactive)
            {
                string head = ok ? $"配置完成（{secs:0} 秒）。按 ▶ 或菜单「Genesis Valley/运行游戏」开始。" : "配置未完全成功，请查看下面的信息和控制台。";
                if (warnings > 0) head += $"\n共有 {warnings} 条提示，详见「Genesis Valley/查看配置报告」。";
                EditorUtility.DisplayDialog(Title, head + "\n\n" + summary, "好");
                if (ok && File.Exists(MainScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MainScenePath);
            }
            return ok;
        }

        private static void Step(float p, string what)
        {
            EditorUtility.DisplayProgressBar(Title, what, p);
        }

        private static void WriteReport(List<string> log, bool ok, double secs)
        {
            GVPaths.EnsureFolder(GVPaths.Generated);
            var sb = new StringBuilder();
            sb.AppendLine("# 创世谷 · 一键配置报告");
            sb.AppendLine();
            sb.AppendLine($"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- 编辑器：{Application.unityVersion}");
            sb.AppendLine($"- 结果：{(ok ? "成功" : "有错误")}（{secs:0.0} 秒）");
            sb.AppendLine();
            foreach (var l in log) sb.AppendLine("- " + l);
            File.WriteAllText(GVPaths.Generated + "/SetupReport.md", sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(GVPaths.Generated + "/SetupReport.md");
        }

        /// <summary>Checks that every model the blueprints and presenters ask for exists in the catalog.</summary>
        private static void Validate(ArtCatalog cat, List<string> log)
        {
            var wanted = new HashSet<string>();
            void Collect(Blueprint bp) { foreach (var p in bp.Pieces) if (!string.IsNullOrEmpty(p.Model)) wanted.Add(p.Model); }
            try
            {
                for (int v = 0; v < 3; v++) { Collect(BuildingBlueprints.House(v)); Collect(BuildingBlueprints.Storehouse(v)); Collect(BuildingBlueprints.FishingHut(v, 6f)); }
                Collect(BuildingBlueprints.Bridge(12f, 1f));
            }
            catch (Exception e) { log.Add("⚠ 蓝图检查失败：" + e.Message); }
            foreach (var s in new[] { "CommonTree_", "Pine_", "TwistedTree_" }) for (int i = 1; i <= 5; i++) wanted.Add(s + i);
            foreach (var k in new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3", "Bush_Common", "Bush_Common_Flowers", "Grass_Common_Short", "Grass_Wispy_Short", "Grass_Wispy_Tall", "Clover_1", "Flower_3_Group", "Flower_4_Group", "Plant_1", "Plant_7", "Prop_Wagon", "Prop_Crate" }) wanted.Add(k);
            cat.Invalidate();
            var missing = wanted.Where(k => !cat.HasModel(k)).OrderBy(k => k).ToList();
            if (missing.Count == 0) log.Add($"✔ 资源校验：蓝图与场景需要的 {wanted.Count} 个模型全部就绪");
            else log.Add($"⚠ 缺少 {missing.Count} 个模型（将使用程序化替代）：{string.Join(", ", missing.Take(20))}");
            if (cat.maleVillager == null || cat.femaleVillager == null) log.Add("⚠ 村民预制体不完整，将使用程序化小人");
            if (cat.villagerAnimator == null) log.Add("⚠ 没有动画控制器");
        }

        private static void BuildScene(ArtCatalog cat, Material sky, List<string> log)
        {
            GVPaths.EnsureFolder(GVPaths.Scenes);
            string path = MainScenePath;
            Scene scene;
            if (File.Exists(path)) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var boot = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (boot == null) boot = new GameObject("Genesis Valley").AddComponent<GameBootstrap>();
            boot.art = cat;
            EditorUtility.SetDirty(boot);

            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            var lsPath = GVPaths.Generated + "/GV_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (ls == null) { ls = new LightingSettings { name = "GV_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            var autoProp = typeof(LightingSettings).GetProperty("autoGenerate");
            if (autoProp != null && autoProp.CanWrite) autoProp.SetValue(ls, false);
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
            log.Add("✔ 主场景 " + path + "（已设为唯一构建场景）");
        }

        // ------------------------------------------------------------------ build

        public static bool BuildWindows(bool interactive)
        {
            if (!File.Exists(MainScenePath))
            {
                if (interactive) EditorUtility.DisplayDialog("创世谷", "主场景还不存在，请先运行「一键配置」。", "好");
                return false;
            }
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BuildFolder));
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            string msg = ok
                ? $"构建成功：{options.locationPathName}\n大小 {report.summary.totalSize / (1024f * 1024f):0.0} MB，用时 {report.summary.totalTime.TotalSeconds:0} 秒。"
                : $"构建失败（{report.summary.result}），错误 {report.summary.totalErrors} 个，请查看控制台。";
            Debug.Log("[Genesis Valley] " + msg);
            if (interactive)
            {
                EditorUtility.DisplayDialog("创世谷 · 构建", msg, "好");
                if (ok) EditorUtility.RevealInFinder(options.locationPathName);
            }
            return ok;
        }
    }
}
