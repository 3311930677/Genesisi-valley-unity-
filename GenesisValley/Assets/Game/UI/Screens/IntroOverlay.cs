using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>Cinematic letterbox during the opening flight: world name, the one-line goal and a skip hint.</summary>
    public sealed class IntroOverlay : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _top, _bottom;
        private Text _world, _line, _skip;
        private CanvasGroup _worldGroup, _lineGroup;
        private float _t;
        private bool _shown;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _group = UIFactory.Group(rt);
            _group.blocksRaycasts = false;
            _top = UIFactory.Image(rt, "BarTop", Color.black).rectTransform;
            _top.anchorMin = new Vector2(0, 1); _top.anchorMax = new Vector2(1, 1); _top.pivot = new Vector2(0.5f, 1); _top.sizeDelta = new Vector2(0, 110);
            _bottom = UIFactory.Image(rt, "BarBottom", Color.black).rectTransform;
            _bottom.anchorMin = new Vector2(0, 0); _bottom.anchorMax = new Vector2(1, 0); _bottom.pivot = new Vector2(0.5f, 0); _bottom.sizeDelta = new Vector2(0, 110);

            _world = UIFactory.Label(rt, "", 64, UIStyle.Text, TextAnchor.MiddleCenter, true, UIStyle.TitleFont);
            UIFactory.Place(_world.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200, 100), new Vector2(0, 60));
            _worldGroup = UIFactory.Group(_world);
            _line = UIFactory.Label(rt, "让水来到这片土地。", 30, new Color(1f, 0.95f, 0.86f, 0.95f), TextAnchor.MiddleCenter, true, UIStyle.TitleFont);
            UIFactory.Place(_line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200, 60), new Vector2(0, -10));
            _lineGroup = UIFactory.Group(_line);
            _skip = UIFactory.Label(rt, "按任意键跳过", UIStyle.FontSmall, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleRight);
            UIFactory.Place(_skip.rectTransform, new Vector2(1, 0), new Vector2(400, 40), new Vector2(-40, 34));
            gameObject.SetActive(false);
        }

        public void SetShown(bool show)
        {
            if (show == _shown) return;
            _shown = show;
            if (show)
            {
                gameObject.SetActive(true);
                _t = 0f;
                var w = GameServices.World;
                _world.text = w != null ? w.WorldName : "创世纪谷";
                _group.alpha = 1f;
                _worldGroup.alpha = 0f; _lineGroup.alpha = 0f;
                _top.sizeDelta = _bottom.sizeDelta = new Vector2(0, 0);
            }
            else
            {
                UITween.Run(this, 0.6f, k =>
                {
                    float h = Mathf.Lerp(110f, 0f, k);
                    _top.sizeDelta = _bottom.sizeDelta = new Vector2(0, h);
                    _worldGroup.alpha = _lineGroup.alpha = Mathf.Min(_worldGroup.alpha, 1f - k);
                    _skip.color = new Color(1, 1, 1, 0.55f * (1f - k));
                }, UITween.InOutCubic, () => { if (!_shown) gameObject.SetActive(false); });
            }
        }

        private void Update()
        {
            if (!_shown) return;
            _t += Time.unscaledDeltaTime;
            float bars = Mathf.SmoothStep(0f, 110f, Mathf.Clamp01(_t / 0.9f));
            _top.sizeDelta = _bottom.sizeDelta = new Vector2(0, bars);
            _worldGroup.alpha = Mathf.Clamp01((_t - 0.8f) / 1.2f) * (1f - Mathf.Clamp01((_t - 4.6f) / 1.2f));
            _lineGroup.alpha = Mathf.Clamp01((_t - 2.0f) / 1.2f);
            _world.rectTransform.anchoredPosition = new Vector2(0, 60 + 6f * Mathf.Clamp01(_t / 6f));
            _skip.color = new Color(1, 1, 1, 0.5f * Mathf.Clamp01((_t - 1f) / 1f) * (0.75f + 0.25f * Mathf.Sin(_t * 2.5f)));
        }
    }
}
