using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GenesisValley.Core;
using GenesisValley.Persistence;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    public enum GamePhase { Boot, Title, Loading, Intro, Playing }
    public enum ToastKind { Info, Success, Warning, Error }

    /// <summary>A save file as listed by the load menu (header only – the body is decoded on load).</summary>
    public sealed class SaveSlotInfo
    {
        public string Slot;
        public string Path;
        public SaveHeader Header;
        public DateTime SavedLocal;
        public bool Unreadable;
        public string ThumbnailPath;
        public bool IsAuto => Slot == GameBootstrap.AutoSlot || Slot == GameBootstrap.QuickSlot;
    }

    /// <summary>
    /// Single-player host (GDD §10 stage A): owns the authoritative WorldState + SimulationRunner, advances it at a fixed
    /// 10 Hz independent of frame rate, stamps player commands, and drives the game flow
    /// title (live valley behind the menu) → loading → intro flight → playing. Also save slots, autosave and thumbnails.
    /// Every other system is created here so an empty scene with this one component is a playable game.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const string AutoSlot = "autosave";
        public const string QuickSlot = "quicksave";
        public const int ManualSlots = 6;

        [Tooltip("Art catalog filled by Genesis Valley ▸ 一键配置. Falls back to Resources/GV_ArtCatalog.")]
        public ArtCatalog art;
        [Tooltip("Seed of the valley shown behind the title menu.")]
        public long titleSeed = 20261015;
        [Tooltip("Editor convenience: skip the title screen and start a new world immediately.")]
        public bool skipTitle;
        [Tooltip("Game hours simulated before the title valley is shown so villagers are already busy.")]
        public float titleWarmupSeconds = 40f;
        public float autosaveMinutes = 5f;

        public SimulationRunner Runner { get; private set; }
        public WorldState World => Runner != null ? Runner.World : null;
        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public float LoadingProgress { get; private set; }
        public string LoadingText { get; private set; } = "";

        /// <summary>Space-bar pause.</summary>
        public bool UserPaused { get; private set; }
        /// <summary>Set by menus (pause menu, settings, load dialog) – stops the clock without changing UserPaused.</summary>
        public bool MenuPaused { get; set; }
        public bool IsPaused => UserPaused || MenuPaused || Phase == GamePhase.Loading || Phase == GamePhase.Boot;
        public float SpeedMultiplier { get; private set; } = 1f;
        /// <summary>0..1 position between the last two ticks (smooth villager motion).</summary>
        public float InterpolationAlpha { get; private set; } = 1f;
        public float LastStepMs { get; private set; }
        public int StepsLastFrame { get; private set; }
        public bool IsTitleWorld { get; private set; }

        public event Action<GamePhase> PhaseChanged;
        public event Action<WorldState> WorldInstalled;
        public event Action<string, ToastKind> Toast;
        public event Action SavesChanged;

        public WorldPresenter Presenter { get; private set; }
        public GodCameraController CameraRig { get; private set; }
        public ToolController Tools { get; private set; }
        public SelectionController Selection { get; private set; }

        public static string SaveDirectory => System.IO.Path.Combine(Application.persistentDataPath, "saves");

        private double _accumulator;
        private int _nextCommandId = 1;
        private int _nextTransaction = 1;
        private float _nextAutosave;
        private bool _busy;
        private float _sessionSeconds;

        // ================================================================== boot

        private void Awake()
        {
            GameServices.Reset();
            if (art == null) art = Resources.Load<ArtCatalog>("GV_ArtCatalog");
            GameServices.Art = art;
            GameServices.Settings = GameSettings.Load();
            GameServices.Settings.Apply();
            GameServices.Game = this;
            SaveGame.GameVersion = Application.version;
            Application.runInBackground = true;

            // Order matters: services first, then views that read them in Awake.
            Make<FxLibrary>("Fx");
            Make<AudioDirector>("Audio");
            Make<DayNightCycle>("Sky");
            Make<PostFxController>("PostFx");
            Presenter = Make<WorldPresenter>("World");
            EnsureCamera();
            Tools = Make<ToolController>("Tools");
            Selection = Make<SelectionController>("Selection");
            Presenter.EventRaised += OnSimEvent;
        }

        private T Make<T>(string name) where T : Component
        {
            var existing = FindObjectOfType<T>();
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        private void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.Skybox;
            CameraRig = cam.GetComponent<GodCameraController>();
            if (CameraRig == null) CameraRig = cam.gameObject.AddComponent<GodCameraController>();
        }

        private void Start()
        {
            if (skipTitle) NewGame("创世纪谷", (ulong)DateTime.Now.Ticks, playIntro: false);
            else ShowTitle();
        }

        private void OnDestroy()
        {
            if (GameServices.Game == this) GameServices.Reset();
        }

        private void SetPhase(GamePhase p)
        {
            if (Phase == p) return;
            Phase = p;
            if (CameraRig != null) CameraRig.InputEnabled = p == GamePhase.Playing;
            var audio = GameServices.Audio;
            if (audio != null) audio.TitleMode = p == GamePhase.Title;
            PhaseChanged?.Invoke(p);
        }

        // ================================================================== flow

        /// <summary>Title screen: a live valley simulates behind the menu while the camera slowly orbits.</summary>
        public void ShowTitle()
        {
            if (_busy) return;
            StartCoroutine(TitleRoutine());
        }

        private IEnumerator TitleRoutine()
        {
            _busy = true;
            SetPhase(GamePhase.Loading);
            LoadingText = "山谷正在苏醒…";
            LoadingProgress = 0f;
            yield return null;
            var w = WorldGenerator.Generate(SimConfig.CreateDefault(), (ulong)titleSeed);
            w.WorldName = "创世纪谷";
            LoadingProgress = 0.3f;
            yield return null;
            Install(w, true);
            // Warm up so the title valley already has people at work (spread over frames).
            int ticks = w.Config.SecondsToTicks(titleWarmupSeconds);
            for (int done = 0; done < ticks; )
            {
                int n = Mathf.Min(40, ticks - done);
                for (int i = 0; i < n; i++) StepOnce();
                done += n;
                LoadingProgress = 0.3f + 0.7f * done / Mathf.Max(1, ticks);
                yield return null;
            }
            Presenter.World.Events.Pending.Clear();
            var sky = GameServices.Sky;
            if (sky != null) sky.HourOverride = 16.6f;
            var focus = CampPoint(w);
            CameraRig.SetTitleOrbit(focus + new Vector3(0, 0, 0), 135f, 2.2f);
            LoadingProgress = 1f;
            _busy = false;
            SetPhase(GamePhase.Title);
            GameServices.Audio?.RestartMusic(0.8f);
        }

        /// <summary>新建世界: fresh generation, then the 6–8 s skippable intro flight (GDD §3).</summary>
        public void NewGame(string worldName, ulong seed, bool playIntro = true)
        {
            if (_busy) return;
            StartCoroutine(NewGameRoutine(worldName, seed, playIntro));
        }

        private IEnumerator NewGameRoutine(string worldName, ulong seed, bool playIntro)
        {
            _busy = true;
            SetPhase(GamePhase.Loading);
            LoadingText = "正在塑造山谷…";
            LoadingProgress = 0.05f;
            yield return null;
            yield return null;
            WorldState w;
            try { w = WorldGenerator.Generate(SimConfig.CreateDefault(), seed); }
            catch (Exception e)
            {
                Debug.LogException(e);
                _busy = false;
                Notify("世界生成失败：" + e.Message, ToastKind.Error);
                ShowTitle();
                yield break;
            }
            w.WorldName = string.IsNullOrWhiteSpace(worldName) ? "创世纪谷" : worldName.Trim();
            LoadingProgress = 0.5f;
            LoadingText = "村民们正在赶来…";
            yield return null;
            Install(w, false);
            LoadingProgress = 1f;
            yield return null;
            _busy = false;
            BeginPlay(playIntro);
        }

        private void BeginPlay(bool intro)
        {
            var sky = GameServices.Sky;
            if (sky != null) sky.HourOverride = -1f;
            UserPaused = false;
            MenuPaused = false;
            SpeedMultiplier = 1f;
            _nextAutosave = Time.unscaledTime + autosaveMinutes * 60f;
            GameServices.Audio?.RestartMusic(2.5f);
            var focus = CampPoint(World);
            CameraRig.StopOrbit();
            if (intro)
            {
                SetPhase(GamePhase.Intro);
                CameraRig.PlayIntro(focus, () => SetPhase(GamePhase.Playing));
            }
            else
            {
                CameraRig.Configure((x, z) => World.Height.SampleHeight(x, z), World.Config.WorldSize, focus);
                SetPhase(GamePhase.Playing);
            }
        }

        /// <summary>Skips the intro flight (any key / click also skips inside the camera).</summary>
        public void SkipIntro()
        {
            if (Phase != GamePhase.Intro) return;
            CameraRig.Configure((x, z) => World.Height.SampleHeight(x, z), World.Config.WorldSize, CampPoint(World));
            SetPhase(GamePhase.Playing);
        }

        public void ReturnToTitle()
        {
            if (Phase == GamePhase.Playing && GameServices.Settings.AutoSave && !IsTitleWorld) Save(AutoSlot, quiet: true);
            ShowTitle();
        }

        public void Quit()
        {
            if (Phase == GamePhase.Playing && GameServices.Settings.AutoSave && !IsTitleWorld) Save(AutoSlot, quiet: true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Install(WorldState w, bool titleWorld)
        {
            IsTitleWorld = titleWorld;
            Runner = new SimulationRunner(w);
            _accumulator = 0;
            InterpolationAlpha = 1f;
            // Continue id sequences after a load so transaction ids never collide with saved undo records.
            _nextCommandId = 1;
            _nextTransaction = (int)(w.LastAuthoritativeSequence % 1000000) + 1000;
            Selection?.Clear();
            Tools?.ResetState();
            Presenter.Bind(w);
            var focus = CampPoint(w);
            CameraRig.Configure((x, z) => w.Height.SampleHeight(x, z), w.Config.WorldSize, focus);
            WorldInstalled?.Invoke(w);
        }

        public Vector3 CampPoint(WorldState w)
        {
            if (w == null) return Vector3.zero;
            var camp = w.Camp;
            float x = camp != null ? camp.X : w.Config.WorldSize * 0.5f, z = camp != null ? camp.Z : w.Config.WorldSize * 0.5f;
            return new Vector3(x, w.Height.SampleHeight(x, z), z);
        }

        // ================================================================== simulation

        private void Update()
        {
            if (Runner == null) return;
            _sessionSeconds += Time.unscaledDeltaTime;
            HandleHotkeys();

            StepsLastFrame = 0;
            if (!IsPaused)
            {
                double tick = World.Config.TickSeconds;
                _accumulator += Math.Min(Time.unscaledDeltaTime, 0.25) * SpeedMultiplier;
                int maxSteps = 6 * Mathf.CeilToInt(SpeedMultiplier);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (_accumulator >= tick && StepsLastFrame < maxSteps)
                {
                    StepOnce();
                    _accumulator -= tick;
                    StepsLastFrame++;
                }
                if (StepsLastFrame > 0) LastStepMs = (float)(sw.Elapsed.TotalMilliseconds / StepsLastFrame);
                if (StepsLastFrame >= maxSteps) _accumulator = 0; // drop the backlog after a hitch instead of spiralling
                InterpolationAlpha = Mathf.Clamp01((float)(_accumulator / tick));
            }
            Presenter.DispatchEvents();

            if (Phase == GamePhase.Playing && !IsTitleWorld && GameServices.Settings.AutoSave && Time.unscaledTime >= _nextAutosave)
            {
                _nextAutosave = Time.unscaledTime + autosaveMinutes * 60f;
                Save(AutoSlot, quiet: true);
            }
        }

        private void StepOnce()
        {
            Presenter.Villagers.BeforeStep();
            Runner.Step();
        }

        private void HandleHotkeys()
        {
            if (Phase != GamePhase.Playing) return;
            if (InputBindings.Down(GameAction.Pause)) TogglePause();
            if (InputBindings.Down(GameAction.Speed1)) SetSpeed(1);
            if (InputBindings.Down(GameAction.Speed2)) SetSpeed(2);
            if (InputBindings.Down(GameAction.Speed3)) SetSpeed(4);
            if (InputBindings.Down(GameAction.QuickSave)) Save(QuickSlot);
            if (InputBindings.Down(GameAction.QuickLoad)) Load(QuickSlot);
            if (InputBindings.Down(GameAction.FocusCamp)) CameraRig.FlyTo(CampPoint(World), 80f, 1.1f);
        }

        /// <summary>Advances the simulation by <paramref name="ticks"/> fixed steps right now (diagnostics / warm-up).</summary>
        public void StepTicks(int ticks)
        {
            if (Runner == null) return;
            for (int i = 0; i < ticks; i++) StepOnce();
        }

        public void TogglePause() { UserPaused = !UserPaused; Notify(UserPaused ? "已暂停" : "继续", ToastKind.Info); }
        public void SetPaused(bool paused) => UserPaused = paused;

        public void SetSpeed(float multiplier)
        {
            SpeedMultiplier = Mathf.Clamp(multiplier, 1f, 4f);
            UserPaused = false;
        }

        // ================================================================== commands

        public int NextTransaction() => _nextTransaction++;

        /// <summary>Stamps and applies a player command right away (single-player host).</summary>
        public CommandResult Submit(GameCommand cmd)
        {
            if (Runner == null) return CommandResult.Reject("世界尚未就绪");
            if (Phase != GamePhase.Playing) return CommandResult.Reject("现在还不能这样做");
            cmd.PlayerId = 1;
            cmd.CommandId = _nextCommandId++;
            cmd.ClientSequence = cmd.CommandId;
            return Runner.ApplyImmediate(cmd);
        }

        /// <summary>Read-only access to the authoritative validators for previews (rivers, bridges).</summary>
        public CommandProcessor Validator => Runner != null ? Runner.Commands : null;

        // ================================================================== saves

        public static string SlotPath(string slot) => System.IO.Path.Combine(SaveDirectory, slot + ".gvsave");

        public List<SaveSlotInfo> ListSaves()
        {
            var list = new List<SaveSlotInfo>();
            if (!Directory.Exists(SaveDirectory)) return list;
            foreach (var path in Directory.GetFiles(SaveDirectory, "*.gvsave"))
            {
                var info = new SaveSlotInfo { Slot = System.IO.Path.GetFileNameWithoutExtension(path), Path = path };
                try
                {
                    info.Header = SaveGame.PeekHeader(path);
                    info.SavedLocal = info.Header.SavedUtcTicks > 0 ? new DateTime(info.Header.SavedUtcTicks, DateTimeKind.Utc).ToLocalTime() : File.GetLastWriteTime(path);
                }
                catch (Exception)
                {
                    info.Unreadable = true;
                    info.SavedLocal = File.GetLastWriteTime(path);
                }
                var thumb = System.IO.Path.ChangeExtension(path, ".jpg");
                if (File.Exists(thumb)) info.ThumbnailPath = thumb;
                list.Add(info);
            }
            list.Sort((a, b) => b.SavedLocal.CompareTo(a.SavedLocal));
            return list;
        }

        public bool HasAnySave()
        {
            if (!Directory.Exists(SaveDirectory)) return false;
            return Directory.GetFiles(SaveDirectory, "*.gvsave").Length > 0;
        }

        /// <summary>Most recent readable save (继续).</summary>
        public SaveSlotInfo LatestSave()
        {
            foreach (var s in ListSaves()) if (!s.Unreadable) return s;
            return null;
        }

        public bool Save(string slot, bool quiet = false)
        {
            var w = World;
            if (w == null || IsTitleWorld) return false;
            try
            {
                Directory.CreateDirectory(SaveDirectory);
                var path = SlotPath(slot);
                SaveGame.WriteFile(path, w);
                CaptureThumbnail(System.IO.Path.ChangeExtension(path, ".jpg"));
                if (!quiet) Notify("已保存：" + w.WorldName + " · 第 " + w.Day + " 天", ToastKind.Success);
                else Notify("已自动保存", ToastKind.Info);
                GameServices.Audio?.PlayUI("save", 0.6f);
                SavesChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notify("保存失败：" + e.Message, ToastKind.Error);
                return false;
            }
        }

        public void Load(string slot)
        {
            if (_busy) return;
            var path = SlotPath(slot);
            if (!File.Exists(path) && !File.Exists(path + ".bak")) { Notify("还没有这个存档", ToastKind.Warning); return; }
            StartCoroutine(LoadRoutine(path));
        }

        private IEnumerator LoadRoutine(string path)
        {
            _busy = true;
            var previous = Phase;
            SetPhase(GamePhase.Loading);
            LoadingText = "正在回到山谷…";
            LoadingProgress = 0.1f;
            yield return null;
            yield return null;
            WorldState w = null;
            bool usedBackup = false;
            string error = null;
            try
            {
                w = SaveGame.ReadFile(path, SimConfig.CreateDefault(), out var header, out usedBackup);
                if (header.ConfigHash != w.Config.ComputeHash()) Debug.LogWarning("存档使用了不同的数值配置，已按当前配置载入");
            }
            catch (Exception e) { Debug.LogException(e); error = e.Message; }
            if (w == null)
            {
                _busy = false;
                Notify("读取失败：" + error + "（当前世界未改动）", ToastKind.Error);
                SetPhase(previous == GamePhase.Loading ? GamePhase.Title : previous);
                yield break;
            }
            LoadingProgress = 0.6f;
            yield return null;
            Install(w, false);
            LoadingProgress = 1f;
            yield return null;
            _busy = false;
            BeginPlay(false);
            Notify(usedBackup ? "主存档损坏，已从备份恢复" : "欢迎回来：" + w.WorldName + " · 第 " + w.Day + " 天", usedBackup ? ToastKind.Warning : ToastKind.Success);
        }

        public bool DeleteSave(string slot)
        {
            try
            {
                var path = SlotPath(slot);
                foreach (var p in new[] { path, path + ".bak", System.IO.Path.ChangeExtension(path, ".jpg") })
                    if (File.Exists(p)) File.Delete(p);
                SavesChanged?.Invoke();
                return true;
            }
            catch (Exception e) { Notify("删除失败：" + e.Message, ToastKind.Error); return false; }
        }

        /// <summary>Renders a small UI-free picture of the current view for the load menu.</summary>
        private void CaptureThumbnail(string path)
        {
            var cam = CameraRig != null ? CameraRig.Cam : Camera.main;
            if (cam == null) return;
            RenderTexture rt = null;
            Texture2D tex = null;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(384, 216, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(384, 216, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 384, 216), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToJPG(82));
            }
            catch (Exception e) { Debug.LogWarning("存档缩略图生成失败：" + e.Message); }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tex != null) Destroy(tex);
            }
        }

        // ================================================================== misc

        public void Notify(string text, ToastKind kind = ToastKind.Info) => Toast?.Invoke(text, kind);

        private void OnSimEvent(SimEvent e)
        {
            if (e.Type == SimEventType.UndoApplied) Notify(string.IsNullOrEmpty(e.Message) ? "已撤销" : e.Message, ToastKind.Info);
        }

        private void OnApplicationFocus(bool focus)
        {
            var s = GameServices.Settings;
            if (!focus && s != null && s.PauseOnFocusLoss && Phase == GamePhase.Playing) UserPaused = true;
        }

        private void OnApplicationQuit()
        {
            if (Phase == GamePhase.Playing && !IsTitleWorld && GameServices.Settings != null && GameServices.Settings.AutoSave)
            {
                try { SaveGame.WriteFile(SlotPath(AutoSlot), World); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        public float SessionSeconds => _sessionSeconds;
    }
}
