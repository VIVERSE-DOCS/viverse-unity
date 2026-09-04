using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

namespace NeonSumo
{
    /// <summary>
    /// Drives main-menu Ready / Restart from a single place (cold join, post-match, after host restart).
    /// </summary>
    public enum LobbyActionsPhase
    {
        Connecting,
        ColdLobby,
        /// <summary>Round over: host must restart (Play Again or Restart) before anyone can Ready.</summary>
        PostMatchAwaitingHostRestart,
        /// <summary>World reset applied; players can Ready. Restart stays off until next post-match phase.</summary>
        PostRestartAwaitingReady,
    }

    /// <summary>
    /// Contract: GameManager calls UIManager only. UIManager delegates to controllers. Views contain no game logic.
    /// </summary>
    public class NeonSumoUIManager : MonoBehaviour, ICoroutineRunner
    {
        [Header("Main UI")]
        [SerializeField] private MainMenuPanelView mainMenuPanelView;
        [SerializeField] private CountdownPanelView countdownPanelView;
        [SerializeField] private GameUIPanelView gameUIPanelView;
        [SerializeField] private WinnerPanelView winnerPanelView;
        [Tooltip("Required. Assign in Inspector or via SceneBootstrap.")]
        [SerializeField] private NeonSumoGameManager gameManager;
        [Tooltip("Legacy uGUI panel; used only when GameUIPanelView is not assigned.")]
        [SerializeField] private GameObject gameUIPanel;

        [Header("Text Elements (legacy; used when GameUIPanelView is not assigned)")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text errorText;

        [Header("Countdown Audio")]
        [SerializeField] private AudioSource countdownAudioSource;
        [SerializeField] private AudioClip getReadyClip;
        [SerializeField] private AudioClip threeClip;
        [SerializeField] private AudioClip twoClip;
        [SerializeField] private AudioClip oneClip;
        [SerializeField] private AudioClip goClip;

        [Header("UI Animation")]
        [SerializeField] private float countdownPunchScale = 1.08f;
        [SerializeField] private float countdownPunchDuration = 0.12f;
        [SerializeField] private float countdownFadeDuration = 0.15f;
        [SerializeField] private float winnerFadeDuration = 0.2f;
        [SerializeField] private float winnerSlideDistance = 20f;
        [SerializeField] private float winnerSlideDuration = 0.2f;

        [Header("HUD Timer")]
        [SerializeField] private Color hudTimerNormalColor = new Color(1f, 0.604f, 0f);
        [SerializeField] private Color hudTimerLowTimeColor = Color.red;
        [SerializeField] private int hudTimerLowTimeThresholdSeconds = 10;

        private bool _hasBeenInitialized;

        LobbyActionsPhase _lobbyActionsPhase = LobbyActionsPhase.Connecting;

        public enum GameUIState { Playing, RoundOver }

        public enum MainMenuMode
        {
            ColdStart,
            /// <summary>Lobby waiting for host to restart (e.g. after round or player dropped below min).</summary>
            AwaitingHostRestart,
            /// <summary>After <see cref="NeonSumoGameManager.HandleGameRestart"/>: Ready on, Restart off.</summary>
            AfterGameRestartSync,
        }

        private GameUIState _uiState = GameUIState.Playing;

        private UIFlowController _flow;
        private HudPresenter _hud;
        private PlayerListPresenter _playerList;
        private CountdownController _countdown;
        private WinnerPresenter _winner;

        void Awake()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            LogInputModuleDiagnostics();
            BuildControllers();
        }

        void Start()
        {
            BindViewEvents();
            InitializeViewState();
            BindGameManager();
        }

        bool ValidateReferences()
        {
            bool missing = false;
            if (mainMenuPanelView == null) { Debug.LogError("[NeonSumo] NeonSumoUIManager: MainMenuPanelView is not assigned."); missing = true; }
            if (countdownPanelView == null) { Debug.LogError("[NeonSumo] NeonSumoUIManager: CountdownPanelView is not assigned."); missing = true; }
            if (gameUIPanelView == null) { Debug.LogError("[NeonSumo] NeonSumoUIManager: GameUIPanelView is not assigned."); missing = true; }
            if (gameManager == null) { Debug.LogError("[NeonSumo] NeonSumoUIManager: NeonSumoGameManager is not assigned."); missing = true; }
            if (winnerPanelView == null) { Debug.LogError("[NeonSumo] NeonSumoUIManager: WinnerPanelView is not assigned."); missing = true; }
            return !missing;
        }

        void LogInputModuleDiagnostics()
        {
            var modules = FindObjectsByType<BaseInputModule>(FindObjectsSortMode.None);
            DebugLogger.Log($"[NeonSumo] InputModules found: {modules.Length}");
            foreach (var m in modules)
                DebugLogger.Log($" - {m.GetType().Name}");

            if (modules.Length > 1)
            {
                Debug.LogError($"[NeonSumo] CRITICAL: Found {modules.Length} Input Modules! WebGL requires exactly ONE.");
                foreach (var m in modules)
                    Debug.LogError($"  - {m.GetType().Name} on {m.gameObject.name}");
            }
        }

        void BuildControllers()
        {
            _flow = new UIFlowController(mainMenuPanelView, gameUIPanelView, countdownPanelView, winnerPanelView, gameUIPanel);
            IHudView hudView = CreateHudView();
            var hudTimerStyle = new HudTimerStyle(hudTimerNormalColor, hudTimerLowTimeColor, hudTimerLowTimeThresholdSeconds);
            _hud = new HudPresenter(this, hudView, hudTimerStyle);
            _playerList = new PlayerListPresenter(mainMenuPanelView,
                () => gameManager != null ? gameManager.GetEffectiveRoomHostUserId() : null,
                userId => gameManager.ResolvePlayerListNicknameSegmentForUi(userId));
            var countdownConfig = new CountdownControllerConfig
            {
                GetReadyClip = getReadyClip,
                ThreeClip = threeClip,
                TwoClip = twoClip,
                OneClip = oneClip,
                GoClip = goClip,
                PunchScale = countdownPunchScale,
                PunchDuration = countdownPunchDuration,
                FadeDuration = countdownFadeDuration,
                WinnerFadeDuration = winnerFadeDuration,
                WinnerSlideDistance = winnerSlideDistance,
                WinnerSlideDuration = winnerSlideDuration
            };
            _countdown = new CountdownController(
                this, countdownPanelView, winnerPanelView, countdownAudioSource, countdownConfig);
            _winner = new WinnerPresenter(winnerPanelView, winnerSlideDistance);
        }

        IHudView CreateHudView()
        {
            if (gameUIPanelView != null)
                return new GameUiHudViewAdapter(gameUIPanelView);
            if (timerText != null || errorText != null)
                return new LegacyTmpHudViewAdapter(timerText, errorText);
            return new NoOpHudViewAdapter();
        }

        void BindViewEvents()
        {
            mainMenuPanelView.ReadyClicked += OnReadyClicked;
            mainMenuPanelView.RestartClicked += OnRestartClicked;
            mainMenuPanelView.QuitClicked += OnQuitClicked;
            winnerPanelView.RestartClicked += OnRestartClicked;
            winnerPanelView.QuitClicked += OnQuitClicked;
        }

        void UnbindViewEvents()
        {
            if (mainMenuPanelView != null)
            {
                mainMenuPanelView.ReadyClicked -= OnReadyClicked;
                mainMenuPanelView.RestartClicked -= OnRestartClicked;
                mainMenuPanelView.QuitClicked -= OnQuitClicked;
            }
            if (winnerPanelView != null)
            {
                winnerPanelView.RestartClicked -= OnRestartClicked;
                winnerPanelView.QuitClicked -= OnQuitClicked;
            }
        }

        void InitializeViewState()
        {
            mainMenuPanelView.SetWinsCounter(0);
            _flow.Initialize();
            ShowMainMenu(MainMenuMode.ColdStart);
            ReapplyLobbyPhaseButtonPolicy();

            if (gameUIPanelView != null)
            {
                gameUIPanelView.SetVisible(true);
                gameUIPanelView.SetTimerVisible(false);
            }
            else if (timerText != null)
                timerText.gameObject.SetActive(false);
        }

        void BindGameManager()
        {
            if (gameManager == null) return;
            gameManager.OnInitialized += HandleGameManagerInitialized;
            DebugLogger.Log("[NeonSumo] Subscribed to GameManager.OnInitialized event");
            if (gameManager.IsInitialized)
                HandleGameManagerInitialized();
        }

        void PrepareForMainMenu()
        {
            _countdown.CancelAll();
            _flow.SetBaseState(UIBaseState.MainMenu);
            _flow.SetOverlay(UIOverlayState.None);
            ShowDefaultMatchTimer();
            SetRoundState(GameUIState.Playing);
            mainMenuPanelView?.SetLobbyActionsRowVisible(true);
            ReapplyLobbyPhaseButtonPolicy();
        }

        void ApplyMainMenuMode(MainMenuMode mode)
        {
            DebugLogger.Log($"[NeonSumo] ShowMainMenu mode={mode}");
            if (mainMenuPanelView == null) return;

            switch (mode)
            {
                case MainMenuMode.ColdStart:
                    RefreshLobbyConnectionState();
                    break;
                case MainMenuMode.AwaitingHostRestart:
                    mainMenuPanelView.HideConnectionStatus();
                    EnterLobbyPhase(LobbyActionsPhase.PostMatchAwaitingHostRestart);
                    break;
                case MainMenuMode.AfterGameRestartSync:
                    mainMenuPanelView.HideConnectionStatus();
                    EnterLobbyPhase(LobbyActionsPhase.PostRestartAwaitingReady);
                    break;
            }
        }

        void ShowDefaultMatchTimer()
        {
            _hud.SetTimerColor(hudTimerNormalColor);
            _hud.SetTimerVisible(true);
        }

        public void ShowMainMenu(MainMenuMode mode = MainMenuMode.ColdStart)
        {
            PrepareForMainMenu();
            ApplyMainMenuMode(mode);
        }

        /// <summary>Apply <see cref="LobbyActionsPhase"/> to main-menu Ready/Restart. Single entry point for lobby button policy.</summary>
        void EnterLobbyPhase(LobbyActionsPhase phase)
        {
            if (mainMenuPanelView == null) return;
            _lobbyActionsPhase = phase;

            switch (phase)
            {
                case LobbyActionsPhase.Connecting:
                    mainMenuPanelView.SetReadyButtonState(false, "Connecting...");
                    break;
                case LobbyActionsPhase.ColdLobby:
                    mainMenuPanelView.SetReadyButtonState(true, "Ready");
                    break;
                case LobbyActionsPhase.PostMatchAwaitingHostRestart:
                {
                    bool host = gameManager != null && gameManager.IsLocalLobbyRestartAuthority();
                    mainMenuPanelView.SetReadyButtonState(false,
                        host ? "Press Restart to continue" : "Waiting for host to restart...");
                    break;
                }
                case LobbyActionsPhase.PostRestartAwaitingReady:
                    mainMenuPanelView.SetReadyButtonState(true, "Ready");
                    break;
            }

            ReapplyLobbyPhaseButtonPolicy();
            mainMenuPanelView?.FocusDefaultLobbyActionNextFrame();
        }

        bool ReadyClicksAllowed =>
            _lobbyActionsPhase == LobbyActionsPhase.ColdLobby ||
            _lobbyActionsPhase == LobbyActionsPhase.PostRestartAwaitingReady;

        void ReapplyLobbyPhaseButtonPolicy()
        {
            if (mainMenuPanelView == null) return;

            bool authority = gameManager != null && gameManager.IsLocalLobbyRestartAuthority();
            bool restartOn = authority &&
                             (_lobbyActionsPhase == LobbyActionsPhase.ColdLobby ||
                              _lobbyActionsPhase == LobbyActionsPhase.PostMatchAwaitingHostRestart);
            mainMenuPanelView.SetRestartInteractable(restartOn);
        }

        void RefreshLobbyConnectionState()
        {
            if (mainMenuPanelView == null) return;
            if (gameManager != null && gameManager.IsInitialized)
            {
                mainMenuPanelView.HideConnectionStatus();
                EnterLobbyPhase(LobbyActionsPhase.ColdLobby);
            }
            else
            {
                mainMenuPanelView.ShowConnectionStatus("Connecting to multiplayer services…");
                EnterLobbyPhase(LobbyActionsPhase.Connecting);
            }
        }

        public void ShowGameUI()
        {
            _flow.SetBaseState(UIBaseState.GameHUD);
            _flow.SetOverlay(UIOverlayState.None);
            ShowDefaultMatchTimer();
            SetRoundState(GameUIState.Playing);
            ReapplyLobbyPhaseButtonPolicy();
        }

        public void ShowCountdown(int seconds)
        {
            _flow.SetBaseState(UIBaseState.GameHUD);
            _flow.SetOverlay(UIOverlayState.Countdown);
            _countdown.ShowCountdown(seconds);
            _hud.SetTimerVisible(true);
            _hud.SetTimerSeconds(60);
        }

        public void ShowGetReady()
        {
            _flow.SetBaseState(UIBaseState.GameHUD);
            _flow.SetOverlay(UIOverlayState.Countdown);
            _countdown.ShowGetReady();
            _hud.SetTimerVisible(true);
            _hud.SetTimerSeconds(60);
        }

        /// <summary>
        /// Pre-touches countdown UI and legacy TMP HUD glyphs before intro. Safe to call after <see cref="OnGameManagerInitialized"/>; does not duplicate initialization.
        /// </summary>
        public void WarmupHudStrings()
        {
            if (timerText != null)
            {
                const string warmup = "READY GET READY GO! 0123456789 1:00 PLAYER 1 PLAYER 2 YOU WIN";
                timerText.text = warmup;
                timerText.ForceMeshUpdate();
                timerText.text = string.Empty;
                timerText.ForceMeshUpdate();
            }

            if (errorText != null)
            {
                errorText.text = " ";
                errorText.ForceMeshUpdate();
                errorText.text = string.Empty;
            }

            _countdown.WarmupCountdownGlyphs();

            _hud.SetTimerVisible(false);
            _hud.SetTimerSeconds(60);
            _hud.SetTimerSeconds(0);
            _hud.SetTimerVisible(false);
        }

        public void ShowStartingIn(int seconds)
        {
            _flow.SetBaseState(UIBaseState.GameHUD);
            _flow.SetOverlay(UIOverlayState.Countdown);
            _countdown.ShowStartingIn(seconds);
            _hud.SetTimerVisible(true);
        }

        public void UpdateStartingIn(int seconds)
        {
            _countdown.UpdateStartingIn(seconds);
        }

        public void HideStartingIn()
        {
            _countdown.HideStartingIn();
        }

        public void PlayGetReadyAudio()
        {
            _countdown.PlayGetReadyAudio();
        }

        public float GetReadyClipLength => _countdown.GetReadyClipLength;

        public void PlayCountdownAudio(int seconds)
        {
            _countdown.PlayCountdownAudio(seconds);
        }

        public void SetTimerToSeconds(int seconds)
        {
            _hud.SetTimerSeconds(seconds);
            if (seconds > 0)
                _hud.SetTimerVisible(true);
        }

        public void ShowEndCountdown(int seconds)
        {
            _flow.SetBaseState(UIBaseState.GameHUD);
            _flow.SetOverlay(UIOverlayState.None);
            _hud.SetTimerSeconds(seconds);
            _hud.SetTimerVisible(seconds > 0);
        }

        public void ShowWinner(string winnerName, int localWins, string winnerId = null)
        {
            // Main menu under winner: list + wins only. Play Again / Quit live on the modal (no duplicate row).
            _flow.SetBaseState(UIBaseState.MainMenu);
            mainMenuPanelView.HideConnectionStatus();
            mainMenuPanelView.SetLobbyActionsRowVisible(false);
            SetRoundState(GameUIState.RoundOver);
            EnterLobbyPhase(LobbyActionsPhase.PostMatchAwaitingHostRestart);

            _playerList.UpdateWinsCounter(localWins);

            bool restartAuthority = gameManager != null && gameManager.IsLocalLobbyRestartAuthority();
            string subtitle;
            if (winnerName == "No Winner")
                subtitle = string.Empty;
            else if (restartAuthority)
                subtitle = "Wins!";
            else
                subtitle = "Wins!\nWaiting for host to restart…";

            var presentation = new WinnerPresentation(
                winnerDisplayText: winnerName != "No Winner" ? winnerName : "Draw!",
                subtitleText: subtitle,
                winnerColor: !string.IsNullOrEmpty(winnerId) ? _playerList.GetPlayerColor(winnerId) : Color.white,
                showRestart: restartAuthority);

            _winner.Show(presentation);
            _flow.SetOverlay(UIOverlayState.Winner);
            mainMenuPanelView?.SetRootPointerPassthrough(true);
            _countdown.PlayWinnerAnimation();
        }

        public void ShowError(string errorMessage)
        {
            _hud.ShowTransientError(errorMessage, 3f);
        }

        public void RefreshPlayerListOrdering()
        {
            _playerList?.RefreshHostFirstNumbers();
        }

        /// <summary>After display-name registry changes (multiplayer).</summary>
        public void RefreshPlayerDisplayNamesFromRegistry()
        {
            _playerList?.RefreshNicknameLabelsFromRegistry();
        }

        public void AddPlayer(string userId, bool isLocal, Color nameColor)
        {
            _playerList.AddPlayer(userId, isLocal, nameColor);
        }

        public void SetPlayerColor(string userId, Color nameColor)
        {
            _playerList.SetPlayerColor(userId, nameColor);
        }

        public void PlayerEliminated(string userId)
        {
            _playerList.MarkEliminated(userId);
        }

        public void SetReadyState(bool isReady)
        {
            if (!ReadyClicksAllowed && !isReady)
                return;

            _playerList.SetReadyState(isReady);
        }

        /// <summary>Roster change / cancel_ready_phase: re-enable Ready if the lobby still allows clicks.</summary>
        public void ResetManualReadyUi()
        {
            HideStartingIn();
            if (!ReadyClicksAllowed)
                return;
            SetReadyState(false);
            mainMenuPanelView?.SetReadyButtonState(true, "Ready");
        }

        void HandleGameManagerInitialized()
        {
            if (_hasBeenInitialized) return;

            DebugLogger.Log("[NeonSumo] === HandleGameManagerInitialized() CALLED ===");
            _hasBeenInitialized = true;

            if (mainMenuPanelView != null)
            {
                mainMenuPanelView.SetVisible(true);
                RefreshLobbyConnectionState();
            }

            ReapplyLobbyPhaseButtonPolicy();
        }

        public void OnGameManagerInitialized()
        {
            HandleGameManagerInitialized();
        }

        public void ResetUI()
        {
            _countdown.CancelAll();
            _hud.HideError();
            _playerList.Clear();
            _hud.SetTimerVisible(false);
            SetRoundState(GameUIState.Playing);
            ReapplyLobbyPhaseButtonPolicy();
        }

        public void UpdateWinsCounter(int wins)
        {
            _playerList.UpdateWinsCounter(wins);
        }

        /// <summary>Re-evaluate lobby Restart (and related) when master / room host sync arrives after the panel first opened.</summary>
        public void RefreshLobbyHostDependentControls()
        {
            ReapplyLobbyPhaseButtonPolicy();
        }

        void SetRoundState(GameUIState state)
        {
            _uiState = state;
            bool isRoundOver = _uiState == GameUIState.RoundOver;

            if (!isRoundOver)
            {
                _flow.SetOverlay(UIOverlayState.None);
                mainMenuPanelView?.SetRootPointerPassthrough(false);
            }

            if (gameManager != null && isRoundOver)
                gameManager.SetGameplayInputEnabled(false);
        }

        void OnReadyClicked()
        {
            DebugLogger.Log("[NeonSumo] OnReadyClicked");
            if (!ReadyClicksAllowed)
                return;
            if (gameManager != null && gameManager.IsInitialized)
            {
                gameManager.PlayerReady();
                mainMenuPanelView?.SetReadyButtonState(false, "Waiting for players...");
            }
        }

        void OnRestartClicked()
        {
            gameManager?.RequestRestart();
        }

        void OnQuitClicked()
        {
#if UNITY_EDITOR
            DebugLogger.Log("[NeonSumo] Quit requested - returning to lobby.");
#endif

            PlaySdkRuntimeRoot.Instance.DestroyMultiplayerClient();
            PlaySdkRuntimeRoot.Instance.DestroyMatchmakingClient();
            MatchmakingHandoffStore.Clear();
            SceneManager.LoadScene(0);
        }

        void OnDestroy()
        {
            if (gameManager != null)
                gameManager.OnInitialized -= HandleGameManagerInitialized;
            UnbindViewEvents();
        }
    }
}
