using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Screen-space labels pinned to the world: villager names when zoomed in, construction progress rings
    /// over building sites, and a small warning badge when a site is stuck (no wood / no path).
    /// Pooled and distance-faded so a 50-person village never floods the screen.
    /// </summary>
    public sealed class WorldLabels : MonoBehaviour
    {
        private sealed class Tag
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Text Text;
            public Image Icon, Ring, RingBg;
            public bool Used;
        }

        private const int MaxNames = 36;
        private const int MaxSites = 16;
        private readonly List<Tag> _names = new List<Tag>();
        private readonly List<Tag> _sites = new List<Tag>();
        private CanvasGroup _group;
        private bool _shown;
        private readonly List<(float d, Villager v, Vector3 p)> _candidates = new List<(float, Villager, Vector3)>();
        private Sprite _ringSprite;

        private void Awake()
        {
            _group = UIFactory.Group(this);
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
            _ringSprite = RingSprite();
        }

        public void OnWorldInstalled(WorldState w) { foreach (var t in _names) Hide(t); foreach (var t in _sites) Hide(t); }

        public void SetShown(bool show)
        {
            _shown = show;
        }

        private static Sprite _ring;
        private static Sprite RingSprite()
        {
            if (_ring != null) return _ring;
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x + 0.5f - s * 0.5f, dy = y + 0.5f - s * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 26f) / 4.5f);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _ring = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return _ring;
        }

        private Tag MakeName()
        {
            var rt = UIFactory.Rect(transform, "Name");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(200, 26);
            var t = UIFactory.Label(rt, "", 16, UIStyle.Text, TextAnchor.LowerCenter, false);
            UIFactory.Stretch(t.rectTransform);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.08f, 0.05f, 0.03f, 0.75f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
            var tag = new Tag { Rt = rt, Group = UIFactory.Group(rt), Text = t };
            tag.Group.blocksRaycasts = false;
            rt.gameObject.SetActive(false);
            return tag;
        }

        private Tag MakeSite()
        {
            var rt = UIFactory.Rect(transform, "Site");
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(240, 64);
            var bg = UIFactory.Image(rt, "RingBg", new Color(0, 0, 0, 0.45f), _ringSprite);
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(40, 40), Vector2.zero);
            var ring = UIFactory.Image(rt, "Ring", UIStyle.Accent, _ringSprite);
            UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 1), new Vector2(40, 40), Vector2.zero);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            var icon = UIFactory.Image(rt, "Icon", UIStyle.Text, UIStyle.Icon("Hammer"));
            icon.preserveAspect = true;
            UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 1), new Vector2(20, 20), new Vector2(0, -10));
            var t = UIFactory.Label(rt, "", 14, UIStyle.Text, TextAnchor.LowerCenter);
            UIFactory.Stretch(t.rectTransform, 0, 0, 40, 0);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.08f, 0.05f, 0.03f, 0.8f);
            var tag = new Tag { Rt = rt, Group = UIFactory.Group(rt), Text = t, Icon = icon, Ring = ring, RingBg = bg };
            tag.Group.blocksRaycasts = false;
            rt.gameObject.SetActive(false);
            return tag;
        }

        private static void Hide(Tag t) { if (t.Rt.gameObject.activeSelf) t.Rt.gameObject.SetActive(false); t.Used = false; }

        private void LateUpdate()
        {
            var g = GameServices.Game;
            var w = GameServices.World;
            var rig = GameServices.CameraRig;
            var presenter = GameServices.Presenter;
            var ui = GameUI.Instance;
            bool active = _shown && g != null && w != null && rig != null && rig.Cam != null && presenter != null && g.Phase == GamePhase.Playing && !g.IsTitleWorld;
            _group.alpha = Mathf.MoveTowards(_group.alpha, active ? 1f : 0f, Time.unscaledDeltaTime * 5f);
            if (!active)
            {
                if (_group.alpha <= 0f) { foreach (var t in _names) Hide(t); foreach (var t in _sites) Hide(t); }
                return;
            }
            var cam = rig.Cam;
            var root = ui.Root.rect;
            float scale = ui.ScaleFactor;
            var settings = GameServices.Settings;
            float camDist = rig.Distance;

            // ---- names
            int used = 0;
            bool names = settings == null || settings.ShowVillagerNames;
            var sel = GameServices.Selection;
            if (names && camDist < 95f)
            {
                _candidates.Clear();
                Vector3 camPos = cam.transform.position;
                foreach (var v in w.Villagers)
                {
                    if (!presenter.Villagers.TryGetView(v.Id, out var view) || !view.Visible) continue;
                    var head = view.HeadPosition + Vector3.up * 0.55f;
                    float d = (head - camPos).magnitude;
                    if (d > 85f) continue;
                    _candidates.Add((d, v, head));
                }
                _candidates.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var c in _candidates)
                {
                    if (used >= MaxNames) break;
                    var sp = cam.WorldToScreenPoint(c.p);
                    if (sp.z <= 0f || sp.x < -50 || sp.x > Screen.width + 50 || sp.y < -50 || sp.y > Screen.height + 50) continue;
                    if (used >= _names.Count) _names.Add(MakeName());
                    var tag = _names[used++];
                    if (!tag.Rt.gameObject.activeSelf) tag.Rt.gameObject.SetActive(true);
                    tag.Used = true;
                    bool selected = sel != null && sel.Selected.Kind == SelectionKind.Villager && sel.Selected.Id.Equals(c.v.Id);
                    string mood = HudUtil.Mood(c.v, out var mc);
                    bool warn = mc == UIStyle.Bad || mc == UIStyle.Warn;
                    string txt = c.v.Name;
                    if (warn) txt += " " + UIStyle.Colorize("·" + mood, mc);
                    if (tag.Text.text != txt) tag.Text.text = txt;
                    tag.Text.fontSize = selected ? 19 : 16;
                    tag.Text.color = selected ? UIStyle.Accent : UIStyle.Text;
                    float fade = Mathf.Clamp01((85f - c.d) / 25f) * Mathf.Clamp01((95f - camDist) / 20f);
                    tag.Group.alpha = selected ? 1f : fade * 0.92f;
                    tag.Rt.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
                }
            }
            for (int i = used; i < _names.Count; i++) Hide(_names[i]);

            // ---- construction sites
            int s = 0;
            if (camDist < 220f)
            {
                foreach (var b in w.Buildings)
                {
                    if (b.IsComplete || s >= MaxSites) continue;
                    var anchor = presenter.Buildings.LabelAnchor(b);
                    var sp = cam.WorldToScreenPoint(anchor);
                    if (sp.z <= 0f || sp.x < -60 || sp.x > Screen.width + 60 || sp.y < -60 || sp.y > Screen.height + 60) continue;
                    if (s >= _sites.Count) _sites.Add(MakeSite());
                    var tag = _sites[s++];
                    if (!tag.Rt.gameObject.activeSelf) tag.Rt.gameObject.SetActive(true);
                    bool stuck = !string.IsNullOrEmpty(b.WaitingReason);
                    tag.Ring.fillAmount = Mathf.Lerp(tag.Ring.fillAmount, b.Progress, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
                    tag.Ring.color = stuck ? UIStyle.Warn : UIStyle.Accent;
                    tag.Icon.sprite = UIStyle.Icon(stuck ? "Warning" : HudUtil.KindIcon(b.Kind));
                    tag.Icon.color = stuck ? UIStyle.Warn : UIStyle.Text;
                    string txt = stuck ? b.WaitingReason : WorldState.KindName(b.Kind) + " " + Mathf.RoundToInt(b.Progress * 100) + "%";
                    if (tag.Text.text != txt) tag.Text.text = txt;
                    tag.Text.color = stuck ? UIStyle.Warn : UIStyle.Text;
                    float fade = Mathf.Clamp01((220f - camDist) / 50f);
                    tag.Group.alpha = fade;
                    tag.Rt.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
                    float k = Mathf.Lerp(1.05f, 0.8f, Mathf.InverseLerp(30f, 200f, sp.z));
                    tag.Rt.localScale = new Vector3(k, k, 1f);
                }
            }
            for (int i = s; i < _sites.Count; i++) Hide(_sites[i]);
        }
    }
}
