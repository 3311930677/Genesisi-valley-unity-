using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GenesisValley.Core;
using Unity.Profiling;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Automated quality review run (menu「Genesis Valley › 质量采集」or player arg <c>-gvCapture</c>):
    /// starts a fixed-seed world, lets the village develop, then captures the fixed review shots
    /// (全景 / 中景 / 近景 × 清晨 / 正午 / 黄昏 / 夜晚), a HUD shot, a terrain raise/lower before–after pair,
    /// performance counters and every warning/error logged during the run. Output: QualityCaptures/&lt;stamp&gt;/
    /// with one contact sheet (contact_sheet.jpg) and report.md – the two files to send back for review.
    /// </summary>
    public sealed class QualityCapture : MonoBehaviour
    {
        public const string PendingKey = "gv.capture.pending";
        public const ulong Seed = 20261015;
        private const int ShotW = 1600, ShotH = 900, ThumbW = 480, ThumbH = 270, SheetCols = 4;

        private static readonly (string name, float hour)[] Hours =
        {
            ("清晨", 7.0f), ("正午", 12.5f), ("黄昏", 18.2f), ("夜晚", 21.6f),
        };

        public static bool Running { get; private set; }
        public static string LastOutput { get; private set; }
        public static event Action<string> Finished;

        private string _dir;
        private readonly List<(string label, Texture2D thumb)> _thumbs = new List<(string, Texture2D)>();
        private readonly List<string> _logs = new List<string>();
        private int _errors, _warnings, _exceptions;
        private readonly StringBuilder _report = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            bool arg = Array.IndexOf(Environment.GetCommandLineArgs(), "-gvCapture") >= 0;
            bool pending = PlayerPrefs.GetInt(PendingKey, 0) == 1;
            if (!arg && !pending) return;
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
            new GameObject("QualityCapture").AddComponent<QualityCapture>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Running = false;
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type == LogType.Warning) _warnings++;
            else if (type == LogType.Exception) _exceptions++;
            else _errors++;
            if (_logs.Count < 40)
            {
                string first = stack;
                if (!string.IsNullOrEmpty(first))
                {
                    var lines = first.Split('\n');
                    first = string.Join(" ← ", lines, 0, Mathf.Min(3, lines.Length)).Trim();
                }
                _logs.Add($"[{type}] {msg.Trim()}" + (type == LogType.Warning || string.IsNullOrEmpty(first) ? "" : $"\n    at {first}"));
            }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Running = true;
            string root = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QualityCaptures"))
                                               : Path.Combine(Application.persistentDataPath, "QualityCaptures");
            _dir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_dir);
            Debug.Log("[质量采集] 开始，输出目录：" + _dir);

            // --- boot into a fixed world
            float t0 = Time.realtimeSinceStartup;
            while ((GameServices.Game == null || GameServices.Game.Phase != GamePhase.Title) && Time.realtimeSinceStartup - t0 < 180f) yield return null;
            var game = GameServices.Game;
            if (game == null || game.Phase != GamePhase.Title) { Fail("3 分钟内没有进入标题界面（卡在 " + (game != null ? game.Phase + " " + Mathf.RoundToInt(game.LoadingProgress * 100) + "%" : "无 GameBootstrap") + "）"); yield break; }
            float bootSeconds = Time.realtimeSinceStartup - t0;

            game.NewGame("质量采集", Seed, playIntro: false);
            t0 = Time.realtimeSinceStartup;
            while (game.Phase != GamePhase.Playing && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            if (game.Phase != GamePhase.Playing) { Fail("新建世界 2 分钟内没有进入游戏（卡在 " + game.Phase + " " + Mathf.RoundToInt(game.LoadingProgress * 100) + "%）"); yield break; }
            float newGameSeconds = Time.realtimeSinceStartup - t0;

            // --- let the village develop (~ 2 in-game days), in chunks so presenters keep up
            var w = game.World;
            int ticks = w.Config.SecondsToTicks(w.Config.SecondsPerGameDay * 2f);
            for (int done = 0; done < ticks; done += 60)
            {
                game.StepTicks(Mathf.Min(60, ticks - done));
                yield return null;
            }
            game.SetPaused(true);
            for (int i = 0; i < 30; i++) yield return null;

            var cam = game.CameraRig;
            var sky = GameServices.Sky;
            var camp = game.CampPoint(w);
            var centre = new Vector3(w.Config.WorldSize * 0.5f, 0, w.Config.WorldSize * 0.5f);
            var shots = new (string name, Vector3 focus, float dist, float yaw)[]
            {
                ("全景", centre, 330f, 35f),
                ("中景", camp, 95f, 40f),
                ("近景", camp + new Vector3(6f, 0, -4f), 24f, 215f),
            };

            // --- performance (noon panorama + mid shot, real screen)
            if (sky != null) sky.HourOverride = 12.5f;
            var perf = new StringBuilder();
            foreach (var s in new[] { shots[0], shots[1] })
            {
                cam.SnapTo(s.focus, s.dist, s.yaw);
                yield return Measure(s.name, perf);
            }

            // --- review shots without HUD
            var hidden = HideOverlays();
            foreach (var h in Hours)
            {
                if (sky != null) sky.HourOverride = h.hour;
                foreach (var s in shots)
                {
                    cam.SnapTo(s.focus, s.dist, s.yaw);
                    yield return Settle();
                    Capture($"{s.name}_{h.name}", cam.Cam);
                }
            }

            // --- terrain response: raise then dig near camp
            if (sky != null) sky.HourOverride = 16.5f;
            var spot = camp + new Vector3(28f, 0, 18f);
            cam.SnapTo(spot, 45f, 30f);
            yield return Settle();
            Capture("地形_改造前", cam.Cam);
            yield return Brush(game, BrushMode.Raise, spot, 9f, 40);
            yield return Brush(game, BrushMode.Lower, spot + new Vector3(16f, 0, 4f), 7f, 30);
            for (int i = 0; i < 90; i++) yield return null; // let the terrain / grass / FX respond
            Capture("地形_改造后", cam.Cam);

            // --- HUD shot (real screen, includes UI)
            RestoreOverlays(hidden);
            if (sky != null) sky.HourOverride = 15.5f;
            cam.SnapTo(camp, 80f, 40f);
            yield return Settle();
            yield return new WaitForEndOfFrame();
            var ui = ScreenCapture.CaptureScreenshotAsTexture();
            Save("界面_HUD", ui);
            Destroy(ui);
            if (sky != null) sky.HourOverride = -1f;

            // --- report
            WriteSheet();
            _report.AppendLine("# 创世谷 质量采集报告");
            _report.AppendLine();
            _report.AppendLine($"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}　版本：{Application.version}　引擎：{Application.unityVersion}　编辑器：{Application.isEditor}");
            _report.AppendLine($"- 设备：{SystemInfo.graphicsDeviceName}（{SystemInfo.graphicsDeviceType}，{SystemInfo.graphicsMemorySize} MB）/ {SystemInfo.processorType} / 内存 {SystemInfo.systemMemorySize} MB");
            _report.AppendLine($"- 屏幕：{Screen.width}×{Screen.height}　画质档：{QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            _report.AppendLine($"- 启动到标题：{bootSeconds:0.0}s　新建世界到可玩：{newGameSeconds:0.0}s");
            _report.AppendLine($"- 世界（种子 {Seed}，模拟 2 天后）：第 {w.Day} 天，人口 {w.Population}，建筑 {w.Buildings.Count}，树 {w.Trees.Count}，仓库 {w.Storages.Count}，河流 {w.Rivers.Count}");
            _report.AppendLine();
            _report.AppendLine("## 性能");
            _report.Append(perf);
            _report.AppendLine();
            _report.AppendLine($"## 日志：{_exceptions} 个异常，{_errors} 个错误，{_warnings} 个警告");
            foreach (var l in _logs) _report.AppendLine("- " + l);
            File.WriteAllText(Path.Combine(_dir, "report.md"), _report.ToString(), new UTF8Encoding(false));
            Done();
        }

        // ------------------------------------------------------------------ steps

        private static IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForSecondsRealtime(0.35f);
        }

        private static IEnumerator Brush(GameBootstrap game, BrushMode mode, Vector3 at, float radius, int commands)
        {
            int txn = game.NextTransaction();
            for (int i = 0; i < commands; i++)
            {
                game.Submit(new TerrainBrushCommand { TransactionId = txn, Mode = mode, CenterX = at.x, CenterZ = at.z, Radius = radius, Strength = 0.12f });
                if (i % 3 == 0) GameServices.Fx?.Play(FxKind.Rumble, at, 2, Mathf.Max(1f, radius / 6f));
                yield return null;
            }
        }

        private IEnumerator Measure(string label, StringBuilder sb)
        {
            yield return Settle();
            var draw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var mem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            var frames = new List<float>();
            float end = Time.realtimeSinceStartup + 4f;
            long d = 0, b = 0, sp = 0, tr = 0; int samples = 0;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                if (draw.Valid) { d += draw.LastValue; b += batches.LastValue; sp += setpass.LastValue; tr += tris.LastValue; samples++; }
            }
            frames.Sort();
            float avg = 0; foreach (var f in frames) avg += f; avg /= Mathf.Max(1, frames.Count);
            float p99 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))] : 0;
            sb.AppendLine($"- {label}（正午）：平均 {1000f / Mathf.Max(0.01f, avg):0} FPS（{avg:0.0} ms），1% 慢帧 {p99:0.0} ms" +
                          (samples > 0 ? $"，Draw Calls {d / samples}，Batches {b / samples}，SetPass {sp / samples}，三角面 {tr / samples / 1000}k" : "，渲染计数不可用（需编辑器或 Development Build）") +
                          (mem.Valid ? $"，内存 {mem.LastValue / (1024 * 1024)} MB" : ""));
            draw.Dispose(); batches.Dispose(); setpass.Dispose(); tris.Dispose(); mem.Dispose();
        }

        private void Capture(string label, Camera cam)
        {
            Texture2D tex = null;
            var rt = RenderTexture.GetTemporary(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
                tex.Apply();
            }
            catch (Exception e) { Debug.LogWarning("[质量采集] 截图失败 " + label + "：" + e.Message); }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
            if (tex != null) { Save(label, tex); Destroy(tex); }
        }

        private void Save(string label, Texture2D tex)
        {
            File.WriteAllBytes(Path.Combine(_dir, label + ".jpg"), tex.EncodeToJPG(88));
            var thumb = Scale(tex, ThumbW, ThumbH);
            _thumbs.Add((label, thumb));
        }

        private static Texture2D Scale(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        private void WriteSheet()
        {
            if (_thumbs.Count == 0) return;
            int rows = (_thumbs.Count + SheetCols - 1) / SheetCols;
            var sheet = new Texture2D(SheetCols * ThumbW, rows * ThumbH, TextureFormat.RGB24, false);
            var bg = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(24, 20, 18, 255);
            sheet.SetPixels32(bg);
            var index = new StringBuilder("\n## 拼图顺序（从左到右、从上到下）\n");
            for (int i = 0; i < _thumbs.Count; i++)
            {
                int cx = i % SheetCols, cy = rows - 1 - i / SheetCols;
                sheet.SetPixels(cx * ThumbW, cy * ThumbH, ThumbW, ThumbH, _thumbs[i].thumb.GetPixels());
                index.AppendLine($"{i + 1}. {_thumbs[i].label}");
                Destroy(_thumbs[i].thumb);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(_dir, "contact_sheet.jpg"), sheet.EncodeToJPG(85));
            Destroy(sheet);
            _report.Insert(0, index.ToString());
        }

        private static List<Canvas> HideOverlays()
        {
            var list = new List<Canvas>();
            foreach (var c in FindObjectsOfType<Canvas>())
                if (c.isRootCanvas && c.enabled) { c.enabled = false; list.Add(c); }
            return list;
        }

        private static void RestoreOverlays(List<Canvas> list)
        {
            foreach (var c in list) if (c != null) c.enabled = true;
        }

        private void Fail(string why)
        {
            _report.AppendLine("# 创世谷 质量采集报告（失败）");
            _report.AppendLine();
            _report.AppendLine("**失败原因：** " + why);
            _report.AppendLine();
            _report.AppendLine($"## 日志：{_exceptions} 个异常，{_errors} 个错误，{_warnings} 个警告");
            foreach (var l in _logs) _report.AppendLine("- " + l);
            File.WriteAllText(Path.Combine(_dir, "report.md"), _report.ToString(), new UTF8Encoding(false));
            Debug.LogError("[质量采集] 失败：" + why);
            Done();
        }

        private void Done()
        {
            LastOutput = _dir;
            Running = false;
            Debug.Log("[质量采集] 完成：" + _dir);
            Finished?.Invoke(_dir);
            if (!Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), "-gvCapture") >= 0) Application.Quit();
            Destroy(gameObject);
        }
    }
}
