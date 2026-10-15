using System.Text;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>F3 performance / simulation readout for testing builds.</summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        private Text _text;
        private RectTransform _panel;
        private bool _on;
        private float _fps = 60f, _worst, _refresh;
        private float _worstWindow;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            var rt = (RectTransform)transform;
            UIFactory.Group(rt).blocksRaycasts = false;
            _panel = UIFactory.Panel(rt, "Panel", new Color(0, 0, 0, 0.72f), 8, false, false);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1, 1);
            _panel.anchoredPosition = new Vector2(-18, -96);
            _panel.sizeDelta = new Vector2(330, 300);
            _text = UIFactory.Label(_panel, "", 15, new Color(0.85f, 1f, 0.85f), TextAnchor.UpperLeft);
            UIFactory.Stretch(_text.rectTransform, 12, 12, 10, 10);
            _panel.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            _on = !_on;
            _panel.gameObject.SetActive(_on);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);
            _worstWindow = Mathf.Max(_worstWindow, dt * 1000f);
            if (!_on) return;
            _refresh -= dt;
            if (_refresh > 0f) return;
            _refresh = 0.25f;
            _worst = _worstWindow; _worstWindow = 0f;
            var g = GameServices.Game;
            var w = GameServices.World;
            _sb.Length = 0;
            _sb.Append("FPS ").Append(_fps.ToString("0")).Append("   最差帧 ").Append(_worst.ToString("0.0")).Append(" ms\n");
            _sb.Append("分辨率 ").Append(Screen.width).Append('×').Append(Screen.height).Append("  画质 ").Append(QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "-").Append('\n');
            if (g != null)
            {
                _sb.Append("阶段 ").Append(g.Phase).Append("  速度 ").Append(g.SpeedMultiplier).Append("×").Append(g.IsPaused ? " (暂停)" : "").Append('\n');
                _sb.Append("模拟 ").Append(g.LastStepMs.ToString("0.00")).Append(" ms/步  ").Append(g.StepsLastFrame).Append(" 步/帧\n");
            }
            if (w != null)
            {
                _sb.Append("Tick ").Append(w.Tick).Append("  第 ").Append(w.Day).Append(" 天 ").Append(w.ClockText).Append('\n');
                _sb.Append("村民 ").Append(w.Villagers.Count).Append("  树 ").Append(w.Trees.Count).Append("  建筑 ").Append(w.Buildings.Count).Append('\n');
                _sb.Append("物资堆 ").Append(w.Piles.Count).Append("  任务 ").Append(w.Tasks.Tasks.Count).Append("  河流 ").Append(w.Rivers.Count).Append('\n');
                _sb.Append("寻路预算 ").Append(w.PathBudget).Append("  路径失败 ").Append(w.Stats.PathFailures).Append('\n');
                _sb.Append("种子 ").Append(w.Seed).Append('\n');
            }
            _sb.Append("托管内存 ").Append((System.GC.GetTotalMemory(false) / (1024f * 1024f)).ToString("0.0")).Append(" MB\n");
            var rig = GameServices.CameraRig;
            if (rig != null) _sb.Append("镜头 距离 ").Append(rig.Distance.ToString("0")).Append("  偏航 ").Append(rig.Yaw.ToString("0")).Append('\n');
            _text.text = _sb.ToString();
        }
    }
}
