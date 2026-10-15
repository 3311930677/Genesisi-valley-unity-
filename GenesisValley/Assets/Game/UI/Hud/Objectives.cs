using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// "山谷的心愿": gentle, optional goals that follow the milestone flags (GDD §15). Shows the next three,
    /// ticks them off with a little flourish and explains how the player can help without giving orders.
    /// </summary>
    public sealed class Objectives : MonoBehaviour
    {
        private struct Goal
        {
            public Milestones Flag;
            public string Title, Hint;
            public Goal(Milestones f, string t, string h) { Flag = f; Title = t; Hint = h; }
        }

        private static readonly Goal[] Goals =
        {
            new Goal(Milestones.FirstWoodDelivered, "运回第一批木材", "村民会自己去砍树。树木不够时，用「森林」种下一片。"),
            new Goal(Milestones.FirstHouse, "建起第一座房屋", "村民只在平坦的地面建房。用「平整」在营地附近抹出一块平地。"),
            new Goal(Milestones.FirstRiver, "引来一条河流", "选择「河流」，从高处按住拖向低处。水能让土地湿润。"),
            new Goal(Milestones.FirstHarvest, "收获第一季庄稼", "湿润、平坦的土地会被开垦成农田（Tab 查看湿润度）。"),
            new Goal(Milestones.FirstFish, "捕到第一条鱼", "河岸边平坦的空地上，会有人建起渔屋。"),
            new Goal(Milestones.FirstForest, "种下一片森林", "选择「森林」，在空地上点击或拖动。"),
            new Goal(Milestones.FirstStorehouse, "建起仓库", "当营地快装不下时，村民会自己建仓库。"),
            new Goal(Milestones.FirstPopulationGrowth, "迎来新的村民", "食物与床位都充足时，会有人迁入山谷。也可以「召唤」他们。"),
            new Goal(Milestones.FirstBridge, "架起第一座桥", "选择「桥梁意向」，依次点击河的两岸。"),
            new Goal(Milestones.AllHoused, "让每个人都有家", "更多平地 = 更多房屋。"),
            new Goal(Milestones.FirstFoodSurplus, "储备充足的粮食", "多开几块田、多建几座渔屋。"),
        };

        private sealed class Row
        {
            public Goal Goal;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Check, CheckFill;
            public Text Title, Hint;
            public bool Done;
        }

        private RectTransform _card, _list;
        private Text _progress;
        private UIButton _collapse;
        private readonly List<Row> _rows = new List<Row>();
        private bool _collapsed;
        private Milestones _known;
        private float _poll;
        private CanvasGroup _group;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _card = UIFactory.Panel(rt, "Card", UIStyle.Panel, 14);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0, 1);
            _card.anchoredPosition = new Vector2(18, -96);
            _card.sizeDelta = new Vector2(360, 100);
            _group = UIFactory.Group(_card);
            UIFactory.VLayout(_card, 6, 16, 16, 10, 12);
            UIFactory.Fit(_card, false, true);

            var head = UIFactory.Rect(_card, "Head");
            UIFactory.Layout(head, -1, 30);
            UIFactory.HLayout(head, 8);
            var star = UIFactory.Image(head, "Star", UIStyle.Accent, UIStyle.Icon("Star"));
            star.preserveAspect = true;
            UIFactory.Layout(star, 20, 20);
            var title = UIFactory.Label(head, "山谷的心愿", UIStyle.FontBody, UIStyle.Text, TextAnchor.MiddleLeft, true, UIStyle.TitleFont);
            UIFactory.Layout(title, 170, 28, 1);
            _progress = UIFactory.Label(head, "", 14, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Layout(_progress, 60, 28);
            _collapse = UIFactory.Button(head, "–", ToggleCollapsed, 28, 26, null, 16, 6);
            _collapse.Normal = new Color(1, 1, 1, 0.04f); _collapse.Refresh();
            _collapse.Tooltip = "收起 / 展开";

            _list = UIFactory.Rect(_card, "List");
            UIFactory.VLayout(_list, 8);
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            _list.gameObject.SetActive(!_collapsed);
            _collapse.Label.text = _collapsed ? "+" : "–";
        }

        public void ResetFor(WorldState w)
        {
            foreach (var r in _rows) if (r.Rt != null) Destroy(r.Rt.gameObject);
            _rows.Clear();
            _known = w.Stats.Reached;
            Fill(w, false);
        }

        public void OnEvent(SimEvent e)
        {
            if (e.Type == SimEventType.Milestone) _poll = 0f;
        }

        private int ReachedCount(Milestones m)
        {
            int n = 0; foreach (var g in Goals) if ((m & g.Flag) != 0) n++;
            return n;
        }

        private void Fill(WorldState w, bool animate)
        {
            int active = 0;
            foreach (var r in _rows) if (!r.Done) active++;
            foreach (var g in Goals)
            {
                if (active >= 3) break;
                if ((w.Stats.Reached & g.Flag) != 0) continue;
                bool exists = false;
                foreach (var r in _rows) if (r.Goal.Flag == g.Flag) { exists = true; break; }
                if (exists) continue;
                _rows.Add(MakeRow(g, animate));
                active++;
            }
            _progress.text = ReachedCount(w.Stats.Reached) + " / " + Goals.Length;
            _card.gameObject.SetActive(_rows.Count > 0);
        }

        private Row MakeRow(Goal g, bool animate)
        {
            var rt = UIFactory.Rect(_list, "Goal");
            UIFactory.HLayout(rt, 10, 0, 0, 0, 0, TextAnchor.UpperLeft);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.001f);
            hit.raycastTarget = true;
            var check = UIFactory.Image(rt, "Check", new Color(1, 1, 1, 0.35f), UIStyle.Circle(10, 2f));
            UIFactory.Layout(check, 20, 20);
            var fill = UIFactory.Image(check.transform, "Fill", UIStyle.Accent, UIStyle.Icon("Check") ?? UIStyle.Circle(8));
            fill.preserveAspect = true;
            UIFactory.Place(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(16, 16), Vector2.zero);
            fill.transform.localScale = Vector3.zero;
            var col = UIFactory.Rect(rt, "Col");
            UIFactory.VLayout(col, 0);
            UIFactory.Layout(col, 290, -1, 1);
            var t = UIFactory.Label(col, g.Title, UIStyle.FontSmall + 1, UIStyle.Text);
            var h = UIFactory.Label(col, g.Hint, 14, UIStyle.TextFaint);
            var tt = rt.gameObject.AddComponent<TooltipTrigger>();
            tt.Text = g.Hint;
            var row = new Row { Goal = g, Rt = rt, Group = UIFactory.Group(rt), Check = check, CheckFill = fill, Title = t, Hint = h };
            var s = GameServices.Settings;
            h.gameObject.SetActive(s == null || s.ShowHints);
            if (animate)
            {
                row.Group.alpha = 0f;
                UITween.Fade(row.Group, 1f, 0.5f, null, 0.2f);
            }
            return row;
        }

        private void Update()
        {
            var w = GameServices.World;
            var g = GameServices.Game;
            if (w == null || g == null) return;
            var s = GameServices.Settings;
            bool want = s == null || s.ShowHints;
            _group.alpha = Mathf.MoveTowards(_group.alpha, want ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            _group.blocksRaycasts = want;
            _poll -= Time.unscaledDeltaTime;
            if (_poll > 0f) return;
            _poll = 0.5f;
            var reached = w.Stats.Reached;
            if (reached == _known) return;
            _known = reached;
            foreach (var r in _rows)
            {
                if (r.Done || (reached & r.Goal.Flag) == 0) continue;
                r.Done = true;
                Complete(r);
            }
            _progress.text = ReachedCount(reached) + " / " + Goals.Length;
        }

        private void Complete(Row r)
        {
            r.Check.color = UIStyle.Accent;
            UITween.Scale(r.CheckFill.transform, Vector3.zero, Vector3.one, 0.35f, UITween.OutBack);
            r.Title.text = "<color=#" + UIStyle.Hex(UIStyle.Good) + ">" + r.Goal.Title + "</color>";
            r.Hint.text = "已完成";
            var row = r;
            UITween.Fade(r.Group, 0f, 0.6f, () =>
            {
                _rows.Remove(row);
                if (row.Rt != null) Destroy(row.Rt.gameObject);
                var w = GameServices.World;
                if (w != null) Fill(w, true);
            }, 2.6f);
        }
    }

    /// <summary>Centre-top ribbon for milestones ("第一座房屋建成了"), queued and never overlapping.</summary>
    public sealed class MilestoneBanner : MonoBehaviour
    {
        private RectTransform _ribbon;
        private CanvasGroup _group;
        private Text _text, _kicker;
        private Image _glow, _star;
        private readonly Queue<string> _queue = new Queue<string>();
        private float _t = -1f;
        private const float Hold = 4.8f;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _ribbon = UIFactory.Rect(rt, "Ribbon");
            UIFactory.Place(_ribbon, new Vector2(0.5f, 1), new Vector2(760, 130), new Vector2(0, -150));
            _group = UIFactory.Group(_ribbon);
            _group.blocksRaycasts = false;
            _glow = UIFactory.Image(_ribbon, "Glow", new Color(1f, 0.78f, 0.4f, 0.3f), UIStyle.Glow());
            UIFactory.Stretch(_glow.rectTransform, -160, -160, -60, -60);
            var bg = UIFactory.Image(_ribbon, "Bg", new Color(0.12f, 0.09f, 0.06f, 0.88f), UIStyle.Rounded(16));
            UIFactory.Stretch(bg.rectTransform, 40, 40, 14, 14);
            var line1 = UIFactory.Image(_ribbon, "LineTop", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.7f), UIStyle.Divider());
            UIFactory.Place(line1.rectTransform, new Vector2(0.5f, 1), new Vector2(560, 8), new Vector2(0, -14));
            var line2 = UIFactory.Image(_ribbon, "LineBottom", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.7f), UIStyle.Divider());
            UIFactory.Place(line2.rectTransform, new Vector2(0.5f, 0), new Vector2(560, 8), new Vector2(0, 14));
            _star = UIFactory.Image(_ribbon, "Star", UIStyle.Accent, UIStyle.Icon("Star"));
            _star.preserveAspect = true;
            UIFactory.Place(_star.rectTransform, new Vector2(0.5f, 1), new Vector2(34, 34), new Vector2(0, 8));
            _kicker = UIFactory.Label(_ribbon, "里 程 碑", 15, UIStyle.Accent, TextAnchor.MiddleCenter);
            UIFactory.Place(_kicker.rectTransform, new Vector2(0.5f, 1), new Vector2(600, 24), new Vector2(0, -28));
            _text = UIFactory.Label(_ribbon, "", 34, UIStyle.Text, TextAnchor.MiddleCenter, true, UIStyle.TitleFont);
            UIFactory.Place(_text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(660, 60), new Vector2(0, -12));
            _ribbon.gameObject.SetActive(false);
        }

        public void ResetQueue() { _queue.Clear(); _t = -1f; _ribbon.gameObject.SetActive(false); }

        public void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _queue.Enqueue(message);
            if (_t < 0f) Next();
        }

        private void Next()
        {
            if (_queue.Count == 0) { _t = -1f; _ribbon.gameObject.SetActive(false); return; }
            _text.text = _queue.Dequeue();
            _t = 0f;
            _ribbon.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(_t / 0.35f) * (1f - Mathf.Clamp01((_t - Hold) / 0.7f));
            _group.alpha = a;
            float pop = _t < 0.5f ? UITween.OutBack(Mathf.Clamp01(_t / 0.5f)) : 1f;
            _ribbon.localScale = new Vector3(Mathf.Lerp(0.8f, 1f, pop), Mathf.Lerp(0.6f, 1f, pop), 1f);
            _glow.color = new Color(1f, 0.78f, 0.4f, 0.22f + 0.12f * Mathf.Sin(_t * 3f));
            _star.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_t * 2f) * 8f);
            if (_t > Hold + 0.8f) Next();
        }
    }
}
