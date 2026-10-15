using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Small label that follows the mouse: the active tool's live preview ("河道 84 米 · 可行" / red reason),
    /// or – with the observe tool – the name of whatever is under the cursor.
    /// </summary>
    public sealed class CursorTip : MonoBehaviour
    {
        private RectTransform _tip;
        private Text _text;
        private Image _bg;
        private CanvasGroup _group;
        private string _last;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            UIFactory.Group(rt).blocksRaycasts = false;
            _tip = UIFactory.Panel(rt, "Tip", UIStyle.PanelSolid, 8, false);
            _tip.anchorMin = _tip.anchorMax = new Vector2(0, 0);
            _tip.pivot = new Vector2(0, 1);
            _bg = UIFactory.PanelBg(_tip);
            UIFactory.HLayout(_tip, 0, 12, 12, 7, 7);
            UIFactory.Fit(_tip, true, true);
            _text = UIFactory.Label(_tip, "", UIStyle.FontSmall, UIStyle.Text);
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _group = UIFactory.Group(_tip);
            _group.alpha = 0f;
        }

        private string Compose(out bool bad)
        {
            bad = false;
            var g = GameServices.Game;
            var tools = GameServices.Tools;
            var w = GameServices.World;
            if (g == null || w == null || g.Phase != GamePhase.Playing || PointerBlockers.IsPointerOverUI()) return null;
            if (tools != null && tools.Current != ToolKind.Observe)
            {
                if (!tools.HasCursor) return null;
                bad = !tools.PreviewValid;
                string t = tools.PreviewText, r = tools.PreviewReason;
                if (!string.IsNullOrEmpty(r) && bad) return string.IsNullOrEmpty(t) ? r : t + "\n" + r;
                return string.IsNullOrEmpty(t) ? null : t;
            }
            var sel = GameServices.Selection;
            if (sel == null || sel.Hovered.IsNone || sel.Hovered.Equals(sel.Selected)) return null;
            var h = sel.Hovered;
            switch (h.Kind)
            {
                case SelectionKind.Villager:
                    var v = w.FindVillager(h.Id);
                    return v != null ? "<b>" + v.Name + "</b>  " + UIStyle.Colorize(HudUtil.Doing(w, v), UIStyle.TextDim) : null;
                case SelectionKind.Building:
                    var b = w.FindBuilding(h.Id);
                    if (b == null) return null;
                    return "<b>" + WorldState.KindName(b.Kind) + "</b>" + (b.IsComplete ? "" : UIStyle.Colorize("  工地 " + Mathf.RoundToInt(b.Progress * 100) + "%", UIStyle.TextDim));
                case SelectionKind.Tree:
                    var t2 = w.FindTree(h.Id);
                    return t2 == null ? null : t2.IsStump ? "树桩" : t2.IsSapling ? "树苗 " + Mathf.RoundToInt(t2.Growth * 100) + "%" : HudUtil.SpeciesName(t2.Species);
                case SelectionKind.Camp: return "<b>营地</b>  " + UIStyle.Colorize("点击查看储备", UIStyle.TextDim);
                case SelectionKind.Pile: return "物资堆";
            }
            return null;
        }

        private void LateUpdate()
        {
            var text = Compose(out bool bad);
            bool show = !string.IsNullOrEmpty(text);
            _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * (show ? 10f : 6f));
            if (!show) return;
            if (text != _last) { _text.text = text; _last = text; }
            _bg.color = bad ? new Color(0.35f, 0.1f, 0.08f, 0.94f) : UIStyle.PanelSolid;
            var ui = GameUI.Instance;
            if (ui == null || !ui.ScreenToCanvas(Input.mousePosition, out var local)) return;
            var root = ui.Root.rect;
            // local is relative to root centre; convert to bottom-left anchored coords
            var p = new Vector2(local.x - root.xMin + 22f, local.y - root.yMin - 18f);
            float w = _tip.rect.width, h = _tip.rect.height;
            if (p.x + w > root.width - 8f) p.x = p.x - w - 40f;
            if (p.y - h < 8f) p.y = h + 8f;
            _tip.anchoredPosition = p;
        }
    }
}
