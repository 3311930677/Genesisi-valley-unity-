using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Base class for every pop-up window (pause menu, settings, saves…). Windows are built once in code,
    /// stay inactive while closed and register themselves with <see cref="GameUI"/> so Esc always closes the
    /// top-most one and the game pauses / mutes / swallows hotkeys consistently.
    /// </summary>
    public abstract class UIWindow : MonoBehaviour
    {
        public RectTransform Rt { get; private set; }
        public CanvasGroup Group { get; private set; }
        public bool IsOpen { get; private set; }
        /// <summary>Stops the simulation while open (in-game only).</summary>
        public virtual bool PausesGame => false;
        /// <summary>Muffles world audio while open.</summary>
        public virtual bool Muffles => false;
        /// <summary>Swallows game hotkeys / camera keys while open.</summary>
        public virtual bool BlocksHotkeys => true;

        protected RectTransform Card;
        protected Text TitleText;

        public void Init(RectTransform rt)
        {
            Rt = rt;
            Group = UIFactory.Group(rt);
            Group.alpha = 0f; Group.blocksRaycasts = false; Group.interactable = false;
            Build();
            rt.gameObject.SetActive(false);
        }

        protected abstract void Build();
        protected virtual void OnOpen() { }
        protected virtual void OnClose() { }
        /// <summary>Called when Open() is invoked on an already open window.</summary>
        protected virtual void OnReopen() { OnOpen(); }

        public void Open()
        {
            if (IsOpen) { transform.SetAsLastSibling(); OnReopen(); return; }
            IsOpen = true;
            transform.SetAsLastSibling();
            UIFactory.SetVisible(Group, true, 0.16f);
            if (Card != null) UITween.Scale(Card, Vector3.one * 0.965f, Vector3.one, 0.22f, UITween.OutBack);
            OnOpen();
            GameUI.Instance?.WindowOpened(this);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            UIFactory.SetVisible(Group, false, 0.12f);
            OnClose();
            GameUI.Instance?.WindowClosed(this);
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        /// <summary>Esc while this is the top window. Return false to let the next handler run.</summary>
        public virtual bool HandleEscape() { Close(); return true; }

        /// <summary>Standard centred card with dimmed backdrop, serif title, accent divider and close button.</summary>
        protected RectTransform BuildFrame(string title, float width, float height, bool dim = true, bool closeButton = true, float dimAlpha = 0.6f)
        {
            UIFactory.Stretch(Rt);
            if (dim)
            {
                var d = UIFactory.Image(Rt, "Dim", new Color(0.03f, 0.022f, 0.016f, dimAlpha), null, true);
                UIFactory.Stretch(d.rectTransform);
                var vign = UIFactory.Image(Rt, "DimGlow", new Color(0, 0, 0, 0.35f), UIStyle.Gradient(false, 1.2f));
                UIFactory.Stretch(vign.rectTransform);
            }
            Card = UIFactory.Panel(Rt, "Card", UIStyle.PanelSolid, 18);
            UIFactory.Place(Card, new Vector2(0.5f, 0.5f), new Vector2(width, height), Vector2.zero);
            var sheen = UIFactory.Image(Card, "Sheen", new Color(1f, 0.9f, 0.7f, 0.05f), UIStyle.Gradient(false, 2.2f));
            UIFactory.Stretch(sheen.rectTransform, 2, 2, 2, height * 0.5f);
            TitleText = UIFactory.Label(Card, title, UIStyle.FontTitle, UIStyle.Text, TextAnchor.MiddleLeft, true, UIStyle.TitleFont);
            var trt = TitleText.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(30, -70); trt.offsetMax = new Vector2(-70, -16);
            var div = UIFactory.Image(Card, "TitleRule", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.35f), UIStyle.Divider());
            var drt = div.rectTransform;
            drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0.5f, 1);
            drt.offsetMin = new Vector2(26, -82); drt.offsetMax = new Vector2(-26, -72);
            if (closeButton)
            {
                var close = UIFactory.IconButton(Card, "Close", "✕", HandleCloseButton, 40, "关闭（Esc）");
                UIFactory.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(40, 40), new Vector2(-20, -20));
                close.Normal = new Color(1, 1, 1, 0.04f);
                close.Refresh();
            }
            var body = UIFactory.Rect(Card, "Body");
            UIFactory.Stretch(body, 30, 30, 96, 26);
            return body;
        }

        protected virtual void HandleCloseButton() => Close();
    }

    /// <summary>
    /// Root of the code-built interface. Owns the overlay canvas and its layers, routes game phases to the
    /// right screens, and implements the global Esc chain (cancel tool → clear selection → pause menu) plus
    /// F1 (hide UI), P (photo mode), Tab (moisture overlay) and F3 (debug).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        public Canvas Canvas { get; private set; }
        public CanvasScaler Scaler { get; private set; }
        public RectTransform Root { get; private set; }
        public RectTransform WorldLayer, HudLayer, ScreenLayer, WindowLayer, ModalLayer, CurtainLayer, ToastLayerRt, TooltipLayerRt;

        public HudRoot Hud { get; private set; }
        public TitleScreen Title { get; private set; }
        public LoadingCurtain Loading { get; private set; }
        public IntroOverlay Intro { get; private set; }
        public ToastLayer Toasts { get; private set; }
        public WorldLabels Labels { get; private set; }
        public PauseMenu Pause { get; private set; }
        public SettingsWindow Settings { get; private set; }
        public SaveLoadWindow SaveLoad { get; private set; }
        public ConfirmDialog Confirm { get; private set; }
        public ChronicleWindow Chronicle { get; private set; }
        public CreditsWindow Credits { get; private set; }
        public NewWorldDialog NewWorld { get; private set; }
        public HelpWindow Help { get; private set; }
        public PhotoModePanel Photo { get; private set; }
        public DebugOverlay Debug { get; private set; }

        public bool HudHidden { get; private set; }
        public bool KeyCaptureActive { get; set; }
        public IReadOnlyList<UIWindow> OpenWindows => _open;
        public bool AnyWindowOpen => _open.Count > 0;

        private readonly List<UIWindow> _open = new List<UIWindow>();
        private GameBootstrap _boundGame;
        private float _appliedScale = -1f;
        private CanvasGroup _hudGroup, _worldGroup;

        public static GameBootstrap Game => GameServices.Game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null || FindObjectOfType<GameUI>() != null) return;
            if (FindObjectOfType<GameBootstrap>() == null) return;
            new GameObject("GameUI").AddComponent<GameUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
            EnsureEventSystem();
            BuildLayers();
            BuildScreens();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputBindings.Suppressed = false;
            Unbind();
        }

        // ================================================================== construction

        private void BuildCanvas()
        {
            gameObject.layer = 5;
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            Canvas.pixelPerfect = false;
            Canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            Scaler = gameObject.AddComponent<CanvasScaler>();
            Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Scaler.referenceResolution = new Vector2(1920, 1080);
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            Scaler.matchWidthOrHeight = 0.6f;
            gameObject.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)transform;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private RectTransform Layer(string name)
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(Root, name));
            return rt;
        }

        private void BuildLayers()
        {
            WorldLayer = Layer("L0_World");
            HudLayer = Layer("L1_Hud");
            ScreenLayer = Layer("L2_Screens");
            WindowLayer = Layer("L3_Windows");
            ModalLayer = Layer("L4_Modal");
            CurtainLayer = Layer("L5_Curtain");
            ToastLayerRt = Layer("L6_Toasts");
            TooltipLayerRt = Layer("L7_Tooltip");
            _worldGroup = UIFactory.Group(WorldLayer);
            _worldGroup.blocksRaycasts = false;
            _hudGroup = UIFactory.Group(HudLayer);
            TooltipLayer.Create(TooltipLayerRt);
        }

        public T CreateWindow<T>(RectTransform layer, string name) where T : UIWindow
        {
            var rt = UIFactory.Rect(layer, name);
            var w = rt.gameObject.AddComponent<T>();
            w.Init(rt);
            return w;
        }

        private T CreatePart<T>(RectTransform layer, string name) where T : Component
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(layer, name));
            return rt.gameObject.AddComponent<T>();
        }

        private void BuildScreens()
        {
            Labels = CreatePart<WorldLabels>(WorldLayer, "WorldLabels");
            Hud = CreatePart<HudRoot>(HudLayer, "Hud");
            Title = CreatePart<TitleScreen>(ScreenLayer, "Title");
            Intro = CreatePart<IntroOverlay>(ScreenLayer, "Intro");
            Photo = CreatePart<PhotoModePanel>(ScreenLayer, "PhotoMode");
            Debug = CreatePart<DebugOverlay>(ScreenLayer, "Debug");

            Pause = CreateWindow<PauseMenu>(WindowLayer, "PauseMenu");
            SaveLoad = CreateWindow<SaveLoadWindow>(WindowLayer, "SaveLoad");
            Settings = CreateWindow<SettingsWindow>(WindowLayer, "Settings");
            Chronicle = CreateWindow<ChronicleWindow>(WindowLayer, "Chronicle");
            Credits = CreateWindow<CreditsWindow>(WindowLayer, "Credits");
            Help = CreateWindow<HelpWindow>(WindowLayer, "Help");
            NewWorld = CreateWindow<NewWorldDialog>(WindowLayer, "NewWorld");
            Confirm = CreateWindow<ConfirmDialog>(ModalLayer, "Confirm");

            Loading = CreatePart<LoadingCurtain>(CurtainLayer, "Loading");
            Toasts = CreatePart<ToastLayer>(ToastLayerRt, "Toasts");
        }

        // ================================================================== game binding

        private void Bind(GameBootstrap g)
        {
            Unbind();
            _boundGame = g;
            g.PhaseChanged += OnPhase;
            g.Toast += OnToast;
            g.WorldInstalled += OnWorld;
            OnPhase(g.Phase);
            if (g.World != null) OnWorld(g.World);
        }

        private void Unbind()
        {
            if (_boundGame == null) return;
            _boundGame.PhaseChanged -= OnPhase;
            _boundGame.Toast -= OnToast;
            _boundGame.WorldInstalled -= OnWorld;
            _boundGame = null;
        }

        private void OnToast(string text, ToastKind kind) { if (Toasts != null) Toasts.Show(text, kind); }

        private void OnWorld(Simulation.WorldState w)
        {
            Hud.OnWorldInstalled(w);
            Labels.OnWorldInstalled(w);
        }

        private void OnPhase(GamePhase p)
        {
            bool playing = p == GamePhase.Playing;
            if (p == GamePhase.Title || p == GamePhase.Loading)
            {
                // Leaving a running game: close in-game windows and photo mode.
                for (int i = _open.Count - 1; i >= 0; i--) if (!(_open[i] is ConfirmDialog)) _open[i].Close();
                if (Photo.IsActive) Photo.Exit();
                if (HudHidden) SetHudHidden(false);
            }
            Hud.SetShown(playing && !HudHidden);
            Labels.SetShown(playing && !HudHidden);
            Title.SetShown(p == GamePhase.Title);
            Intro.SetShown(p == GamePhase.Intro);
            Loading.SetPhase(p);
            if (playing) Hud.OnEnterPlay();
            RecomputeBlocking();
        }

        // ================================================================== windows

        internal void WindowOpened(UIWindow w)
        {
            _open.Remove(w);
            _open.Add(w);
            RecomputeBlocking();
        }

        internal void WindowClosed(UIWindow w)
        {
            _open.Remove(w);
            RecomputeBlocking();
        }

        public void RecomputeBlocking()
        {
            var g = Game;
            bool pause = false, muffle = false, block = KeyCaptureActive;
            foreach (var w in _open)
            {
                pause |= w.PausesGame;
                muffle |= w.Muffles;
                block |= w.BlocksHotkeys;
            }
            InputBindings.Suppressed = block;
            if (g != null)
            {
                g.MenuPaused = pause && g.Phase == GamePhase.Playing;
                if (g.CameraRig != null) g.CameraRig.InputEnabled = g.Phase == GamePhase.Playing && !block;
            }
            if (GameServices.Audio != null) GameServices.Audio.Muffled = muffle && g != null && g.Phase == GamePhase.Playing;
        }

        public void SetHudHidden(bool hidden)
        {
            HudHidden = hidden;
            bool playing = Game != null && Game.Phase == GamePhase.Playing;
            Hud.SetShown(playing && !hidden);
            Labels.SetShown(playing && !hidden);
            if (hidden && Game != null) Notify("界面已隐藏 · 按 " + InputBindings.KeyName(InputBindings.Get(GameAction.ToggleUI)) + " 或 Esc 恢复");
        }

        public void Notify(string text, ToastKind kind = ToastKind.Info) { if (Toasts != null) Toasts.Show(text, kind); }

        /// <summary>Asks before doing something destructive. "No" simply closes.</summary>
        public void Ask(string title, string message, string yes, System.Action onYes, string no = "取消", bool danger = false)
        {
            Confirm.Show(title, message, yes, onYes, no, danger);
        }

        // ================================================================== frame

        private void Update()
        {
            var g = Game;
            if (g != _boundGame && g != null) Bind(g);
            if (g == null) return;

            ApplyScale();

            if (Input.GetKeyDown(KeyCode.Escape) && !KeyCaptureActive) HandleEscape();
            if (KeyCaptureActive || InputBindings.TextInputActive && !InputBindings.Suppressed) return;

            bool playing = g.Phase == GamePhase.Playing;
            if (Input.GetKeyDown(KeyCode.F3)) Debug.Toggle();
            if (!playing || AnyWindowOpen) return;

            if (InputBindings.DownRaw(GameAction.ToggleUI) && !Photo.IsActive) SetHudHidden(!HudHidden);
            if (InputBindings.DownRaw(GameAction.PhotoMode)) { if (Photo.IsActive) Photo.Exit(); else Photo.Enter(); }
            if (InputBindings.DownRaw(GameAction.Overlay) && !Photo.IsActive) Hud.ToggleOverlay();
            if (Input.GetKeyDown(KeyCode.L) && !Photo.IsActive) Chronicle.Open();
            if (Input.GetKeyDown(KeyCode.F2) && !Photo.IsActive) Help.Open();
        }

        private void HandleEscape()
        {
            if (_open.Count > 0)
            {
                for (int i = _open.Count - 1; i >= 0; i--)
                    if (_open[i].HandleEscape()) return;
                return;
            }
            var g = Game;
            switch (g.Phase)
            {
                case GamePhase.Intro: g.SkipIntro(); return;
                case GamePhase.Title: return;
                case GamePhase.Playing:
                    if (Photo.IsActive) { Photo.Exit(); return; }
                    if (HudHidden) { SetHudHidden(false); return; }
                    if (Hud.HandleEscape()) return;
                    if (g.Tools != null && g.Tools.CancelOrExit()) return;
                    if (g.Selection != null && g.Selection.CancelOrClear()) return;
                    Pause.Open();
                    return;
            }
        }

        private void ApplyScale()
        {
            var s = GameServices.Settings;
            float k = s != null ? Mathf.Clamp(s.UiScale, 0.75f, 1.5f) : 1f;
            if (Mathf.Approximately(k, _appliedScale)) return;
            _appliedScale = k;
            Scaler.referenceResolution = new Vector2(1920f / k, 1080f / k);
        }

        /// <summary>Screen point → canvas local point on the root (for world labels / cursor tips).</summary>
        public bool ScreenToCanvas(Vector2 screen, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, screen, null, out local);

        public float ScaleFactor => Canvas != null ? Canvas.scaleFactor : 1f;
    }
}
