using System;
using System.Collections.Generic;
using System.IO;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Save / load browser with thumbnails. Saving uses six manual slots; loading lists every save, including
    /// the autosave and quicksave, newest first. Overwrite and delete always ask first.
    /// </summary>
    public sealed class SaveLoadWindow : UIWindow
    {
        private bool _saving;
        private RectTransform _grid;
        private ScrollRect _scroll;
        private Text _hint;
        private readonly List<Texture2D> _thumbs = new List<Texture2D>();

        public override bool PausesGame => true;
        public override bool Muffles => true;

        protected override void Build()
        {
            var body = BuildFrame("存档", 1080, 800);
            _hint = UIFactory.Label(body, "", UIStyle.FontSmall, UIStyle.TextFaint);
            var hrt = _hint.rectTransform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(0, -30); hrt.offsetMax = Vector2.zero;
            _grid = UIFactory.ScrollList(body, "Slots", out _scroll, 0);
            UIFactory.Stretch((RectTransform)_scroll.transform, 0, 0, 40, 0);
            UnityEngine.Object.DestroyImmediate(_grid.GetComponent<VerticalLayoutGroup>());
            var grid = _grid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(490, 196);
            grid.spacing = new Vector2(18, 18);
            grid.padding = new RectOffset(2, 2, 4, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
        }

        public void OpenSave()
        {
            var g = GameServices.Game;
            if (g == null || g.World == null || g.IsTitleWorld || g.Phase != GamePhase.Playing) { GameUI.Instance.Notify("现在没有可以保存的山谷", ToastKind.Warning); return; }
            _saving = true;
            if (IsOpen) Populate(); else Open();
        }

        public void OpenLoad()
        {
            _saving = false;
            if (IsOpen) Populate(); else Open();
        }

        protected override void OnOpen()
        {
            var g = GameServices.Game;
            if (g != null) g.SavesChanged += Populate;
            Populate();
        }

        protected override void OnClose()
        {
            var g = GameServices.Game;
            if (g != null) g.SavesChanged -= Populate;
            FreeThumbs();
        }

        private void FreeThumbs()
        {
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
        }

        private void Populate()
        {
            var g = GameServices.Game;
            if (g == null) return;
            TitleText.text = _saving ? "保存游戏" : "加载存档";
            _hint.text = _saving ? "选择一个位置保存当前山谷。已有存档会在确认后被覆盖。" : "选择一个存档继续。自动存档每 " + Mathf.RoundToInt(g.autosaveMinutes) + " 分钟更新一次；F5 快速存档，F9 快速读档。";
            UIFactory.Clear(_grid);
            FreeThumbs();
            var saves = g.ListSaves();
            var bySlot = new Dictionary<string, SaveSlotInfo>();
            foreach (var s in saves) bySlot[s.Slot] = s;
            if (_saving)
            {
                for (int i = 1; i <= GameBootstrap.ManualSlots; i++)
                {
                    string slot = "slot" + i;
                    bySlot.TryGetValue(slot, out var info);
                    SlotCard(slot, "存档 " + i, info);
                }
            }
            else
            {
                if (saves.Count == 0)
                {
                    var empty = UIFactory.Label(_grid, "还没有任何存档。", UIStyle.FontBody, UIStyle.TextFaint, TextAnchor.MiddleCenter);
                    return;
                }
                foreach (var s in saves) SlotCard(s.Slot, SlotTitle(s.Slot), s);
            }
            _scroll.verticalNormalizedPosition = 1f;
        }

        private static string SlotTitle(string slot)
        {
            if (slot == GameBootstrap.AutoSlot) return "自动存档";
            if (slot == GameBootstrap.QuickSlot) return "快速存档";
            if (slot.StartsWith("slot")) return "存档 " + slot.Substring(4);
            return slot;
        }

        private void SlotCard(string slot, string title, SaveSlotInfo info)
        {
            bool exists = info != null;
            var b = UIFactory.Button(_grid, null, () => Pick(slot, info), 490, 196, null, UIStyle.FontBody, 14);
            b.name = "Slot_" + slot;
            b.HoverScale = 1.015f;
            b.Normal = new Color(1, 1, 1, 0.045f);
            b.Hover = new Color(1, 0.9f, 0.75f, 0.1f);
            b.Interactable = _saving || exists && !info.Unreadable;
            var rt = (RectTransform)b.transform;

            // thumbnail
            var frame = UIFactory.Image(rt, "ThumbFrame", new Color(0, 0, 0, 0.45f), UIStyle.Rounded(10));
            UIFactory.Place(frame.rectTransform, new Vector2(0, 0.5f), new Vector2(286, 164), new Vector2(16, 0));
            var raw = new GameObject("Thumb", typeof(RectTransform)).AddComponent<RawImage>();
            raw.rectTransform.SetParent(frame.transform, false);
            UIFactory.Stretch(raw.rectTransform, 3, 3, 3, 3);
            raw.raycastTarget = false;
            var tex = exists ? LoadThumb(info.ThumbnailPath) : null;
            if (tex != null) raw.texture = tex;
            else
            {
                raw.color = new Color(1, 1, 1, 0f);
                var ph = UIFactory.Label(frame.transform, exists ? "无预览" : "空", UIStyle.FontLarge, UIStyle.TextFaint, TextAnchor.MiddleCenter, false, UIStyle.TitleFont);
                UIFactory.Stretch(ph.rectTransform);
            }

            var col = UIFactory.Rect(rt, "Info");
            col.anchorMin = new Vector2(0, 0); col.anchorMax = new Vector2(1, 1);
            col.offsetMin = new Vector2(318, 16); col.offsetMax = new Vector2(-14, -14);
            UIFactory.VLayout(col, 2);
            var t = UIFactory.Label(col, title, 15, UIStyle.Accent);
            UIFactory.Layout(t, -1, 22);
            if (exists && !info.Unreadable && info.Header != null)
            {
                var h = info.Header;
                var name = UIFactory.Label(col, h.WorldName, UIStyle.FontLarge, UIStyle.Text, TextAnchor.MiddleLeft, false, UIStyle.TitleFont);
                UIFactory.Layout(name, -1, 34);
                var d = UIFactory.Label(col, "第 " + h.Day + " 天 · " + h.Population + " 位村民", UIStyle.FontSmall, UIStyle.TextDim);
                UIFactory.Layout(d, -1, 24);
                var when = UIFactory.Label(col, Friendly(info.SavedLocal), 14, UIStyle.TextFaint);
                UIFactory.Layout(when, -1, 22);
            }
            else if (exists)
            {
                var bad = UIFactory.Label(col, "存档无法读取（可能已损坏）", UIStyle.FontSmall, UIStyle.Bad);
                UIFactory.Layout(bad, -1, 50);
            }
            else
            {
                var e = UIFactory.Label(col, "空位置", UIStyle.FontBody, UIStyle.TextFaint);
                UIFactory.Layout(e, -1, 40);
            }
            if (exists)
            {
                var del = UIFactory.TextLink(rt, "删除", () => Delete(slot, title), 14);
                var drt = (RectTransform)del.transform;
                drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1, 0);
                drt.anchoredPosition = new Vector2(-10, 10);
                drt.sizeDelta = new Vector2(60, 28);
                del.Label.color = new Color(1f, 0.6f, 0.55f, 0.85f);
                del.Interactable = true;
            }
            b.Refresh();
        }

        private static string Friendly(DateTime t)
        {
            var now = DateTime.Now;
            if (t.Date == now.Date) return "今天 " + t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(-1)) return "昨天 " + t.ToString("HH:mm");
            return t.ToString("yyyy-MM-dd HH:mm");
        }

        private Texture2D LoadThumb(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) { Destroy(tex); return null; }
                tex.wrapMode = TextureWrapMode.Clamp;
                _thumbs.Add(tex);
                return tex;
            }
            catch (Exception) { return null; }
        }

        private void Pick(string slot, SaveSlotInfo info)
        {
            var g = GameServices.Game;
            var ui = GameUI.Instance;
            if (g == null) return;
            if (_saving)
            {
                if (info != null)
                    ui.Ask("覆盖存档", "「" + SlotTitle(slot) + "」里已经保存了 " + (info.Header != null ? info.Header.WorldName + " · 第 " + info.Header.Day + " 天" : "一个山谷") + "。\n要用当前进度覆盖它吗？", "覆盖", () => DoSave(slot), "取消", true);
                else DoSave(slot);
            }
            else
            {
                if (info == null || info.Unreadable) return;
                if (g.Phase == GamePhase.Playing && !g.IsTitleWorld)
                    ui.Ask("加载存档", "加载「" + (info.Header != null ? info.Header.WorldName : slot) + "」？\n当前未保存的进度将会丢失。", "加载", () => DoLoad(slot));
                else DoLoad(slot);
            }
        }

        private void DoSave(string slot)
        {
            var g = GameServices.Game;
            // Hide the window for the thumbnail? The thumbnail renders the camera only, so UI never appears in it.
            if (g.Save(slot)) Populate();
        }

        private void DoLoad(string slot)
        {
            var g = GameServices.Game;
            Close();
            GameUI.Instance.Pause.Close();
            g.Load(slot);
        }

        private void Delete(string slot, string title)
        {
            GameUI.Instance.Ask("删除存档", "确定要永久删除「" + title + "」吗？此操作无法撤销。", "删除", () =>
            {
                GameServices.Game?.DeleteSave(slot);
                Populate();
            }, "取消", true);
        }
    }
}
