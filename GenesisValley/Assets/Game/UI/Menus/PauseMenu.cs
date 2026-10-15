using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Esc menu: 继续 / 保存 / 加载 / 设置 / 操作说明 / 返回标题 / 退出.</summary>
    public sealed class PauseMenu : UIWindow
    {
        private Text _summary;
        public override bool PausesGame => true;
        public override bool Muffles => true;

        protected override void Build()
        {
            var body = BuildFrame("已暂停", 460, 650, true, false, 0.45f);
            UIFactory.VLayout(body, 10, 0, 0, 0, 0);
            _summary = UIFactory.Label(body, "", UIStyle.FontSmall, UIStyle.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.Layout(_summary, -1, 40);
            Item(body, "继续游戏", "Play", Close);
            Item(body, "保存游戏", "Save", () => GameUI.Instance.SaveLoad.OpenSave());
            Item(body, "加载存档", "Load", () => GameUI.Instance.SaveLoad.OpenLoad());
            Item(body, "设置", "Gear", () => GameUI.Instance.Settings.Open());
            Item(body, "操作说明", "Info", () => GameUI.Instance.Help.Open());
            UIFactory.Divider(body, -1, 0.18f);
            Item(body, "返回标题画面", "Menu", ReturnToTitle);
            Item(body, "退出游戏", "Exit", QuitGame);
        }

        private static UIButton Item(Transform parent, string text, string icon, System.Action click)
        {
            var b = UIFactory.Button(parent, text, click, 400, 54, icon, UIStyle.FontBody + 1);
            return b;
        }

        protected override void OnOpen()
        {
            var w = GameServices.World;
            if (w != null) _summary.text = w.WorldName + " · 第 " + w.Day + " 天 " + w.ClockText + " · " + w.Population + " 位村民";
        }

        private static string LossText()
        {
            var s = GameServices.Settings;
            return s != null && s.AutoSave ? "当前进度会自动存入「自动存档」。" : "自上次保存以来的进度将会丢失。";
        }

        private void ReturnToTitle()
        {
            GameUI.Instance.Ask("返回标题画面", "要离开这座山谷吗？\n" + LossText(), "返回标题", () =>
            {
                Close();
                GameServices.Game?.ReturnToTitle();
            });
        }

        private void QuitGame()
        {
            GameUI.Instance.Ask("退出游戏", "确定要退出吗？\n" + LossText(), "退出", () => GameServices.Game?.Quit(), "取消", true);
        }
    }

    /// <summary>Yes/No modal used for destructive actions (overwrite, delete, quit).</summary>
    public sealed class ConfirmDialog : UIWindow
    {
        private Text _message;
        private UIButton _yes, _no;
        private System.Action _onYes;

        public override bool PausesGame => true;

        protected override void Build()
        {
            var body = BuildFrame("", 560, 300, true, false, 0.5f);
            _message = UIFactory.Label(body, "", UIStyle.FontBody, UIStyle.TextDim, TextAnchor.UpperLeft);
            var mrt = _message.rectTransform;
            mrt.anchorMin = new Vector2(0, 0); mrt.anchorMax = new Vector2(1, 1); mrt.offsetMin = new Vector2(0, 70); mrt.offsetMax = Vector2.zero;
            var actions = UIFactory.Rect(body, "Actions");
            actions.anchorMin = new Vector2(0, 0); actions.anchorMax = new Vector2(1, 0); actions.pivot = new Vector2(0.5f, 0);
            actions.sizeDelta = new Vector2(0, 54); actions.anchoredPosition = Vector2.zero;
            UIFactory.HLayout(actions, 12, 0, 0, 0, 0, TextAnchor.MiddleRight);
            _no = UIFactory.Button(actions, "取消", Close, 150, 52);
            _yes = UIFactory.Button(actions, "确定", Accept, 190, 52);
        }

        public void Show(string title, string message, string yes, System.Action onYes, string no, bool danger)
        {
            TitleText.text = title;
            _message.text = message;
            _yes.Label.text = yes;
            _no.Label.text = no;
            _onYes = onYes;
            _yes.Normal = danger ? new Color(0.55f, 0.18f, 0.14f, 0.95f) : UIStyle.AccentDark;
            _yes.Hover = danger ? new Color(0.7f, 0.24f, 0.18f, 0.95f) : new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.95f);
            _yes.Refresh();
            Open();
        }

        private void Accept()
        {
            var a = _onYes;
            _onYes = null;
            Close();
            a?.Invoke();
        }

        private void Update()
        {
            if (IsOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Accept();
        }
    }
}
