using System.Collections.Generic;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>UI panels register screen rects each frame so world tools never fire through the UI (GDD §4).</summary>
    public static class PointerBlockers
    {
        private static readonly List<Rect> Rects = new List<Rect>();
        private static int _frame = -1;

        /// <summary>Rect in IMGUI coordinates (origin top-left).</summary>
        public static void Register(Rect guiRect)
        {
            if (_frame != Time.frameCount) { Rects.Clear(); _frame = Time.frameCount; }
            Rects.Add(guiRect);
        }

        public static bool IsPointerOverUI()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return true;
            if (_frame < Time.frameCount - 1) return false;
            Vector2 p = Input.mousePosition;
            p.y = Screen.height - p.y;
            foreach (var r in Rects) if (r.Contains(p)) return true;
            return false;
        }
    }
}
