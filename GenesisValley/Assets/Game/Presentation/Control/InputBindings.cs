using System;
using System.Collections.Generic;
using UnityEngine;

namespace GenesisValley.Presentation
{
    public enum GameAction
    {
        PanForward, PanBack, PanLeft, PanRight, RotateLeft, RotateRight, ZoomIn, ZoomOut,
        Pause, Speed1, Speed2, Speed3,
        ToolObserve, ToolRaise, ToolLower, ToolFlatten, ToolRiver, ToolForest, ToolSummon, ToolBridge,
        BrushSmaller, BrushLarger, Undo, QuickSave, QuickLoad, ToggleUI, PhotoMode, FocusCamp, Overlay,
    }

    /// <summary>Rebindable keyboard shortcuts (GDD §4 "快捷键必须可重绑定"), persisted in PlayerPrefs.</summary>
    public static class InputBindings
    {
        private static readonly Dictionary<GameAction, KeyCode> Defaults = new Dictionary<GameAction, KeyCode>
        {
            { GameAction.PanForward, KeyCode.W }, { GameAction.PanBack, KeyCode.S }, { GameAction.PanLeft, KeyCode.A }, { GameAction.PanRight, KeyCode.D },
            { GameAction.RotateLeft, KeyCode.Q }, { GameAction.RotateRight, KeyCode.E }, { GameAction.ZoomIn, KeyCode.R }, { GameAction.ZoomOut, KeyCode.F },
            { GameAction.Pause, KeyCode.Space }, { GameAction.Speed1, KeyCode.Z }, { GameAction.Speed2, KeyCode.X }, { GameAction.Speed3, KeyCode.C },
            { GameAction.ToolObserve, KeyCode.Alpha1 }, { GameAction.ToolRaise, KeyCode.Alpha2 }, { GameAction.ToolLower, KeyCode.Alpha3 }, { GameAction.ToolFlatten, KeyCode.Alpha4 },
            { GameAction.ToolRiver, KeyCode.Alpha5 }, { GameAction.ToolForest, KeyCode.Alpha6 }, { GameAction.ToolSummon, KeyCode.Alpha7 }, { GameAction.ToolBridge, KeyCode.Alpha8 },
            { GameAction.BrushSmaller, KeyCode.LeftBracket }, { GameAction.BrushLarger, KeyCode.RightBracket }, { GameAction.Undo, KeyCode.Backspace },
            { GameAction.QuickSave, KeyCode.F5 }, { GameAction.QuickLoad, KeyCode.F9 }, { GameAction.ToggleUI, KeyCode.F1 }, { GameAction.PhotoMode, KeyCode.P },
            { GameAction.FocusCamp, KeyCode.H }, { GameAction.Overlay, KeyCode.Tab },
        };

        private static readonly Dictionary<GameAction, KeyCode> Current = new Dictionary<GameAction, KeyCode>();
        private static bool _loaded;
        public static event Action Changed;

        public static readonly Dictionary<GameAction, string> Labels = new Dictionary<GameAction, string>
        {
            { GameAction.PanForward, "镜头前移" }, { GameAction.PanBack, "镜头后移" }, { GameAction.PanLeft, "镜头左移" }, { GameAction.PanRight, "镜头右移" },
            { GameAction.RotateLeft, "向左旋转" }, { GameAction.RotateRight, "向右旋转" }, { GameAction.ZoomIn, "拉近" }, { GameAction.ZoomOut, "拉远" },
            { GameAction.Pause, "暂停 / 继续" }, { GameAction.Speed1, "1 倍速" }, { GameAction.Speed2, "2 倍速" }, { GameAction.Speed3, "4 倍速" },
            { GameAction.ToolObserve, "观察" }, { GameAction.ToolRaise, "抬升" }, { GameAction.ToolLower, "降低" }, { GameAction.ToolFlatten, "平整" },
            { GameAction.ToolRiver, "河流" }, { GameAction.ToolForest, "森林" }, { GameAction.ToolSummon, "召唤村民" }, { GameAction.ToolBridge, "桥梁意向" },
            { GameAction.BrushSmaller, "笔刷缩小" }, { GameAction.BrushLarger, "笔刷放大" }, { GameAction.Undo, "撤销（也可 Ctrl+Z）" },
            { GameAction.QuickSave, "快速存档" }, { GameAction.QuickLoad, "快速读档" }, { GameAction.ToggleUI, "隐藏界面" }, { GameAction.PhotoMode, "拍照模式" },
            { GameAction.FocusCamp, "回到营地" }, { GameAction.Overlay, "湿润度图层" },
        };

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var kv in Defaults)
            {
                int v = PlayerPrefs.GetInt("gv.key." + kv.Key, (int)kv.Value);
                Current[kv.Key] = Enum.IsDefined(typeof(KeyCode), v) ? (KeyCode)v : kv.Value;
            }
        }

        public static KeyCode Get(GameAction a) { EnsureLoaded(); return Current.TryGetValue(a, out var k) ? k : KeyCode.None; }

        public static void Set(GameAction a, KeyCode k)
        {
            EnsureLoaded();
            // Swap with any action already using this key so nothing ends up unbound silently.
            foreach (var kv in new List<KeyValuePair<GameAction, KeyCode>>(Current))
                if (kv.Value == k && kv.Key != a) { Current[kv.Key] = Current[a]; PlayerPrefs.SetInt("gv.key." + kv.Key, (int)Current[a]); }
            Current[a] = k;
            PlayerPrefs.SetInt("gv.key." + a, (int)k);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ResetAll()
        {
            foreach (var kv in Defaults) { Current[kv.Key] = kv.Value; PlayerPrefs.DeleteKey("gv.key." + kv.Key); }
            _loaded = true;
            Changed?.Invoke();
        }

        /// <summary>True while the binding is held; text fields capture input so shortcuts are suppressed.</summary>
        public static bool Held(GameAction a) => !TextInputActive && Input.GetKey(Get(a));
        public static bool Down(GameAction a) => !TextInputActive && Input.GetKeyDown(Get(a));

        /// <summary>Set by the UI while a modal window / key capture is open: all game shortcuts go quiet.</summary>
        public static bool Suppressed;

        /// <summary>Raw key-down check that ignores <see cref="Suppressed"/> (used by the UI layer itself).</summary>
        public static bool DownRaw(GameAction a) => Input.GetKeyDown(Get(a));

        public static bool TextInputActive
        {
            get
            {
                if (Suppressed) return true;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es == null || es.currentSelectedGameObject == null) return false;
                return es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            }
        }

        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Alpha0: case KeyCode.Alpha1: case KeyCode.Alpha2: case KeyCode.Alpha3: case KeyCode.Alpha4:
                case KeyCode.Alpha5: case KeyCode.Alpha6: case KeyCode.Alpha7: case KeyCode.Alpha8: case KeyCode.Alpha9:
                    return ((int)k - (int)KeyCode.Alpha0).ToString();
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Space: return "空格";
                case KeyCode.Backspace: return "退格";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Return: return "回车";
                case KeyCode.None: return "—";
                default: return k.ToString();
            }
        }
    }
}
