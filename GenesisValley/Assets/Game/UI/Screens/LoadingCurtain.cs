using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Full-screen curtain shown while a world is generated / loaded, with progress and rotating tips.</summary>
    public sealed class LoadingCurtain : MonoBehaviour
    {
        private static readonly string[] Tips =
        {
            "村民只会在平坦的土地上建房。用「平整」为他们腾出一块空地。",
            "河流必须从高处流向低处——从山坡上开始拖动，效果最好。",
            "树苗大约一天后长成，村民才会去砍伐它们。",
            "食物与床位决定了山谷能留住多少人。",
            "按住鼠标中键拖动大地，按住右键旋转镜头，滚轮缩放。",
            "按 Tab 查看土地的湿润度——靠近水的土地更适合耕种。",
            "Backspace 或 Ctrl+Z 可以撤销刚才的操作。",
            "按 P 进入拍照模式，留住山谷里的每一个黄昏。",
            "村民会自己决定做什么。你只需要塑造他们脚下的土地。",
            "夜里村民会回家睡觉，篝火边常有人闲聊到很晚。",
            "桥只是一个意向：村民会自己运来木材，把它一段段建起来。",
            "双击一位村民，镜头会跟随他走过一天。",
            "Ctrl + 滚轮可以调整笔刷大小。",
        };

        private CanvasGroup _group;
        private UIBar _bar;
        private Text _status, _tip, _percent;
        private RectTransform _spinner;
        private int _tipIndex;
        private float _tipTimer;
        private bool _showing = true;
        private float _shownProgress;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _group = UIFactory.Group(rt);
            _group.alpha = 1f;
            var bg = UIFactory.Image(rt, "Bg", new Color(0.075f, 0.062f, 0.052f, 1f), null, true);
            UIFactory.Stretch(bg.rectTransform);
            var glow = UIFactory.Image(rt, "Glow", new Color(0.93f, 0.66f, 0.32f, 0.10f), UIStyle.Glow());
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(1500, 900), Vector2.zero);
            var shade = UIFactory.Image(rt, "Shade", new Color(0, 0, 0, 0.55f), UIStyle.Gradient(false, 1.4f));
            UIFactory.Stretch(shade.rectTransform);

            var title = UIFactory.Label(rt, "创世纪谷", 76, UIStyle.Text, TextAnchor.MiddleCenter, true, UIStyle.TitleFont);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 110), new Vector2(0, 90));
            var sub = UIFactory.Label(rt, "G E N E S I S   V A L L E Y", 18, new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.8f), TextAnchor.MiddleCenter);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 30), new Vector2(0, 26));

            _bar = UIFactory.Bar(rt, "Progress", 520, 6, UIStyle.Accent);
            UIFactory.Place(_bar.Root, new Vector2(0.5f, 0.5f), new Vector2(520, 6), new Vector2(0, -40));
            _status = UIFactory.Label(rt, "", UIStyle.FontBody, UIStyle.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520, 30), new Vector2(0, -72));
            _percent = UIFactory.Label(rt, "", UIStyle.FontSmall, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Place(_percent.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520, 30), new Vector2(0, -72));

            var sp = UIFactory.Image(rt, "Spinner", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.9f), UIStyle.Icon("Sun") ?? UIStyle.Circle(10, 2f));
            sp.preserveAspect = true;
            _spinner = sp.rectTransform;
            UIFactory.Place(_spinner, new Vector2(1, 0), new Vector2(40, 40), new Vector2(-60, 56));

            var tipIcon = UIFactory.Image(rt, "TipIcon", UIStyle.TextFaint, UIStyle.Icon("Info"));
            tipIcon.preserveAspect = true;
            UIFactory.Place(tipIcon.rectTransform, new Vector2(0.5f, 0), new Vector2(22, 22), new Vector2(-480, 82));
            _tip = UIFactory.Label(rt, "", UIStyle.FontBody, UIStyle.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_tip.rectTransform, new Vector2(0.5f, 0), new Vector2(900, 60), new Vector2(0, 64));
            _tipIndex = Random.Range(0, Tips.Length);
            _tip.text = Tips[_tipIndex];
        }

        public void SetPhase(GamePhase p)
        {
            bool show = p == GamePhase.Loading || p == GamePhase.Boot;
            if (show == _showing) return;
            _showing = show;
            if (show)
            {
                gameObject.SetActive(true);
                _group.blocksRaycasts = true;
                _shownProgress = 0f;
                _tipIndex = (_tipIndex + 1) % Tips.Length;
                _tip.text = Tips[_tipIndex];
                UITween.Fade(_group, 1f, 0.25f);
            }
            else
            {
                _group.blocksRaycasts = false;
                UITween.Fade(_group, 0f, 1.1f, () => { if (!_showing) gameObject.SetActive(false); }, 0.2f);
            }
        }

        private void Update()
        {
            var g = GameServices.Game;
            float target = g != null ? g.LoadingProgress : 0f;
            if (!_showing) target = 1f;
            _shownProgress = Mathf.MoveTowards(_shownProgress, target, Time.unscaledDeltaTime * 1.6f);
            _bar.Set(_shownProgress, UIStyle.Accent, null, false);
            _status.text = g != null && !string.IsNullOrEmpty(g.LoadingText) ? g.LoadingText : "正在准备…";
            _percent.text = Mathf.RoundToInt(_shownProgress * 100f) + "%";
            _spinner.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 40f);
            _tipTimer += Time.unscaledDeltaTime;
            if (_tipTimer > 6f)
            {
                _tipTimer = 0f;
                _tipIndex = (_tipIndex + 1) % Tips.Length;
                _tip.text = Tips[_tipIndex];
            }
        }
    }
}
