using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Bottom-right notifications. Identical messages merge ("×3") instead of stacking up.</summary>
    public sealed class ToastLayer : MonoBehaviour
    {
        private sealed class Toast
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Text Text;
            public string Message;
            public int Count = 1;
            public float Expire;
            public bool Dying;
        }

        private const int Max = 4;
        private RectTransform _stack;
        private readonly List<Toast> _toasts = new List<Toast>();

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _stack = UIFactory.Rect(rt, "Stack");
            _stack.anchorMin = _stack.anchorMax = _stack.pivot = new Vector2(1, 0);
            _stack.anchoredPosition = new Vector2(-20, 128);
            _stack.sizeDelta = new Vector2(420, 10);
            var v = UIFactory.VLayout(_stack, 8);
            v.childAlignment = TextAnchor.LowerRight;
            UIFactory.Fit(_stack, false, true);
            var g = UIFactory.Group(_stack);
            g.blocksRaycasts = false; g.interactable = false;
        }

        public void Show(string message, ToastKind kind)
        {
            if (string.IsNullOrEmpty(message)) return;
            foreach (var t in _toasts)
            {
                if (t.Dying || t.Message != message) continue;
                t.Count++;
                t.Text.text = message + UIStyle.Colorize("  ×" + t.Count, UIStyle.TextFaint);
                t.Expire = Time.unscaledTime + Duration(kind);
                UITween.Scale(t.Rt, Vector3.one * 1.04f, Vector3.one, 0.2f, UITween.OutCubic);
                return;
            }
            Color accent = kind == ToastKind.Success ? UIStyle.Good : kind == ToastKind.Warning ? UIStyle.Warn : kind == ToastKind.Error ? UIStyle.Bad : UIStyle.Accent;
            string icon = kind == ToastKind.Success ? "Check" : kind == ToastKind.Warning || kind == ToastKind.Error ? "Warning" : "Info";
            var card = UIFactory.Panel(_stack, "Toast", UIStyle.PanelSolid, 12);
            UIFactory.HLayout(card, 12, 14, 18, 10, 10);
            UIFactory.Layout(card, 420, -1);
            var stripe = UIFactory.Image(card, "Stripe", accent, UIStyle.Rounded(2));
            var srt = stripe.rectTransform;
            srt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(0, 1); srt.pivot = new Vector2(0, 0.5f);
            srt.offsetMin = new Vector2(0, 8); srt.offsetMax = new Vector2(4, -8);
            var ic = UIFactory.Image(card, "Icon", accent, UIStyle.Icon(icon));
            ic.preserveAspect = true;
            UIFactory.Layout(ic, 22, 22);
            var text = UIFactory.Label(card, message, UIStyle.FontSmall, UIStyle.Text);
            UIFactory.Layout(text, 350, -1, 1);
            var toast = new Toast { Rt = card, Group = UIFactory.Group(card), Text = text, Message = message, Expire = Time.unscaledTime + Duration(kind) };
            toast.Group.alpha = 0f;
            UITween.Fade(toast.Group, 1f, 0.2f);
            UITween.Scale(card, new Vector3(0.92f, 0.92f, 1f), Vector3.one, 0.25f, UITween.OutBack);
            _toasts.Add(toast);
            while (_toasts.Count > Max) Remove(_toasts[0], true);
            if (kind == ToastKind.Error) GameServices.Audio?.PlayUI("error", 0.35f);
        }

        private static float Duration(ToastKind k) => k == ToastKind.Error || k == ToastKind.Warning ? 5.5f : 3.6f;

        private void Remove(Toast t, bool immediate)
        {
            _toasts.Remove(t);
            if (t.Rt == null) return;
            if (immediate) { Destroy(t.Rt.gameObject); return; }
            t.Dying = true;
            var go = t.Rt.gameObject;
            UITween.Fade(t.Group, 0f, 0.4f, () => { if (go != null) Destroy(go); });
        }

        private void Update()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (Time.unscaledTime > _toasts[i].Expire) Remove(_toasts[i], false);
        }
    }
}
