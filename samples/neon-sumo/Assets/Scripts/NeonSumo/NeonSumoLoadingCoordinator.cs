using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace NeonSumo
{
    /// <summary>
    /// DontDestroyOnLoad handoff between main menu / lobby and gameplay: keeps a loading overlay visible across async scene loads
    /// until the target scene reports ready (offline staged startup complete or online matchmaking flow ready).
    /// </summary>
    public class NeonSumoLoadingCoordinator : MonoBehaviour
    {
        public static NeonSumoLoadingCoordinator Instance { get; private set; }

        enum LoadContext
        {
            None,
            LobbyFromMainMenu,
            MainMenuFromLobby,
            SinglePlayerGameplay,
            OnlineGameplayFromLobby
        }

        [Tooltip("Optional. If null, a fullscreen overlay is created at runtime.")]
        [SerializeField] private GameObject loadingOverlayRoot;

        [Tooltip("When true, pressing Single Player from the menu skips bootstrap startupDelayFrames (still runs staged startup).")]
        [SerializeField] private bool skipStartupDelayWhenLoadingFromMainMenu = true;

        LoadContext _context;
        Stopwatch _menuToGameplayReadySw;
        Stopwatch _gameplayStagedStartupSw;

        /// <summary>Only single-player handoff skips <see cref="NeonSumoOfflineBootstrap"/> startup delay; online loads use matchmaking flow.</summary>
        public bool SkipStartupDelayForCurrentGameplayEntry =>
            skipStartupDelayWhenLoadingFromMainMenu && _context == LoadContext.SinglePlayerGameplay;

        /// <summary>True while the main-menu single-player path expects <see cref="NotifySinglePlayerGameplayReady"/>.</summary>
        public bool AwaitingSinglePlayerGameplayReveal => _context == LoadContext.SinglePlayerGameplay;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static NeonSumoLoadingCoordinator EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NeonSumoLoadingCoordinator");
            return go.AddComponent<NeonSumoLoadingCoordinator>();
        }

        /// <summary>Main menu → NeonSumoScene (offline): call before LoadSceneAsync; shows overlay and starts menu→ready timing.</summary>
        public void BeginSinglePlayerTransitionToGameplay()
        {
            _context = LoadContext.SinglePlayerGameplay;
            _menuToGameplayReadySw = Stopwatch.StartNew();
            EnsureOverlayExists();
            if (loadingOverlayRoot != null)
                loadingOverlayRoot.SetActive(true);
        }

        /// <summary>Main menu → lobby scene: show overlay until <see cref="NotifyLobbySceneLoaded"/>.</summary>
        public void BeginLobbySceneLoadFromMainMenu()
        {
            _context = LoadContext.LobbyFromMainMenu;
            EnsureOverlayExists();
            if (loadingOverlayRoot != null)
                loadingOverlayRoot.SetActive(true);
        }

        /// <summary>Lobby → main menu scene: show overlay until <see cref="NotifyMainMenuSceneLoaded"/>.</summary>
        public void BeginMainMenuSceneLoadFromLobby()
        {
            _context = LoadContext.MainMenuFromLobby;
            EnsureOverlayExists();
            if (loadingOverlayRoot != null)
                loadingOverlayRoot.SetActive(true);
        }

        /// <summary>Lobby → NeonSumoScene (online): call before LoadSceneAsync; overlay until matchmaking flow reaches Ready.</summary>
        public void BeginOnlineGameplaySceneLoadFromLobby()
        {
            _context = LoadContext.OnlineGameplayFromLobby;
            _menuToGameplayReadySw = Stopwatch.StartNew();
            EnsureOverlayExists();
            if (loadingOverlayRoot != null)
                loadingOverlayRoot.SetActive(true);
        }

        public void BeginGameplayStagedStartupTiming()
        {
            _gameplayStagedStartupSw = Stopwatch.StartNew();
        }

        public void EndGameplayStagedStartupTimingAndLog()
        {
            if (_gameplayStagedStartupSw == null || !_gameplayStagedStartupSw.IsRunning) return;
            _gameplayStagedStartupSw.Stop();
            UnityEngine.Debug.Log($"[NeonSumo][StartupTiming] gameplay staged startup: {_gameplayStagedStartupSw.ElapsedMilliseconds} ms");
        }

        public void NotifyLobbySceneLoaded()
        {
            if (_context != LoadContext.LobbyFromMainMenu) return;
            _context = LoadContext.None;
            SetOverlayHidden();
        }

        public void NotifyMainMenuSceneLoaded()
        {
            if (_context != LoadContext.MainMenuFromLobby) return;
            _context = LoadContext.None;
            SetOverlayHidden();
        }

        /// <summary>NeonSumoOfflineBootstrap calls after <see cref="NeonSumoGameManager.StartLocalGame"/> (intro started). Wrap with <c>NeonSumo.Startup.NotifyReady</c> at the call site.</summary>
        public void NotifySinglePlayerGameplayReady()
        {
            if (_context != LoadContext.SinglePlayerGameplay) return;
            _context = LoadContext.None;

            if (_menuToGameplayReadySw != null && _menuToGameplayReadySw.IsRunning)
            {
                _menuToGameplayReadySw.Stop();
                UnityEngine.Debug.Log($"[NeonSumo][StartupTiming] menu → single-player gameplay ready: {_menuToGameplayReadySw.ElapsedMilliseconds} ms");
            }
            SetOverlayHidden();
        }

        /// <summary>NeonSumoMatchmakingFlow calls when multiplayer initialization finished and state is Ready.</summary>
        public void NotifyOnlineGameplayFlowReady()
        {
            if (_context != LoadContext.OnlineGameplayFromLobby) return;
            _context = LoadContext.None;

            if (_menuToGameplayReadySw != null && _menuToGameplayReadySw.IsRunning)
            {
                _menuToGameplayReadySw.Stop();
                UnityEngine.Debug.Log($"[NeonSumo][StartupTiming] lobby → online gameplay ready: {_menuToGameplayReadySw.ElapsedMilliseconds} ms");
            }
            SetOverlayHidden();
        }

        void SetOverlayHidden()
        {
            if (loadingOverlayRoot != null)
                loadingOverlayRoot.SetActive(false);
        }

        void EnsureOverlayExists()
        {
            if (loadingOverlayRoot != null) return;

            var canvasGo = new GameObject("NeonSumoLoadingOverlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var image = panel.AddComponent<Image>();
            image.color = new Color(0.02f, 0.02f, 0.05f, 0.92f);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(panel.transform, false);
            var tRect = textGo.AddComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0.5f, 0.5f);
            tRect.anchorMax = new Vector2(0.5f, 0.5f);
            tRect.sizeDelta = new Vector2(800, 120);
            var text = textGo.AddComponent<Text>();
            Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = uiFont;
            text.text = "Loading…";
            text.fontSize = 36;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            loadingOverlayRoot = canvasGo;
            loadingOverlayRoot.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
