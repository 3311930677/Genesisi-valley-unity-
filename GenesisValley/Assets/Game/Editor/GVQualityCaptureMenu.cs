using System.IO;
using GenesisValley.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// 「Genesis Valley › 质量采集」: plays the main scene, runs <see cref="QualityCapture"/>, stops play mode and
    /// reveals QualityCaptures/&lt;stamp&gt;/ (contact_sheet.jpg + report.md are the files to send back).
    /// </summary>
    [InitializeOnLoad]
    public static class GVQualityCaptureMenu
    {
        private const string StopKey = "gv.capture.stopWhenDone";

        static GVQualityCaptureMenu()
        {
            QualityCapture.Finished += OnFinished;
        }

        [MenuItem("Genesis Valley/质量采集（截图 + 性能报告）", priority = 30)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("创世谷", "请先停止播放，再运行质量采集。", "好"); return; }
            if (!File.Exists(GVSetup.MainScenePath)) { EditorUtility.DisplayDialog("创世谷", "主场景还不存在，请先运行「一键配置」。", "好"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GVSetup.MainScenePath);
            PlayerPrefs.SetInt(QualityCapture.PendingKey, 1);
            PlayerPrefs.Save();
            SessionState.SetBool(StopKey, true);
            EditorApplication.isPlaying = true;
            Debug.Log("[质量采集] 已开始：约 1–2 分钟，期间请不要操作游戏窗口。");
        }

        private static void OnFinished(string dir)
        {
            if (!SessionState.GetBool(StopKey, false)) return;
            SessionState.SetBool(StopKey, false);
            EditorApplication.delayCall += () =>
            {
                EditorApplication.isPlaying = false;
                EditorUtility.RevealInFinder(Path.Combine(dir, "report.md"));
                EditorUtility.DisplayDialog("创世谷 · 质量采集完成",
                    "已生成：\n" + dir + "\n\n请把 contact_sheet.jpg 和 report.md 发给我做审核。", "好");
            };
        }
    }
}
