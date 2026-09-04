using UnityEngine;
using ViverseSDK;
using System;
using System.Collections;
using System.Threading.Tasks;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NeonSumo
{
    /// <summary>
    /// Handles the flow from matchmaking to multiplayer game.
    /// Explicitly injects MultiplayerClient to GameManager (no polling).
    /// </summary>
    public class NeonSumoMatchmakingFlow : MonoBehaviour
    {
        private const string LogPrefix = "[NeonSumoFlow]";
        private const float ConnectTimeoutSeconds = 10f;
        private const float InitTimeoutSeconds = 10f;
        private const float EditorBotSetupTimeoutSeconds = 15f;

        private MultiplayerClient _multiplayerClient;
        private FlowState _currentState = FlowState.Idle;

        [Tooltip("Required. Assign in Inspector or via SceneBootstrap.")]
        [SerializeField] private NeonSumoGameManager gameManager;

        [Tooltip("Required. Assign NeonSumoConfig asset in Inspector.")]
        [SerializeField] private NeonSumoConfig config;

        private enum FlowState
        {
            Idle,
            LoadingHandoff,
            Connecting,
            InitializingModules,
            InitializingGame,
            Ready,
            Failed
        }

        private IEnumerator Start()
        {
            if (!ValidateDependencies())
                yield break;

            EnsureKeyboardInputWhenUnfocused();

            config.EnsureRuntimeValidity();

            if (!TryGetHandoff(out var handoff))
                yield break;

            yield return StartCoroutine(RunStartupFlow(handoff));
        }

        /// <summary>Multi-client / unfocused Game views often see <c>Keyboard.current == null</c> unless background input is allowed.</summary>
        private static void EnsureKeyboardInputWhenUnfocused()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                var settings = InputSystem.settings;
                if (settings != null)
                    settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            }
            catch
            {
                // Older Input System builds: ignore
            }
#endif
        }

        private bool ValidateDependencies()
        {
            if (gameManager == null)
            {
                DebugLogger.LogError($"{LogPrefix} NeonSumoGameManager is not assigned. Assign in Inspector or via SceneBootstrap.");
                enabled = false;
                return false;
            }

            if (config == null)
            {
                DebugLogger.LogError($"{LogPrefix} NeonSumoConfig is required but not assigned. Assign in Inspector.");
                enabled = false;
                return false;
            }

            return true;
        }

        private bool TryGetHandoff(out MatchmakingHandoffStore.HandoffData handoff)
        {
            if (MatchmakingHandoffStore.TryLoad(out handoff))
                return true;

            DebugLogger.LogError($"{LogPrefix} Missing matchmaking handoff. Launch from your lobby scene (e.g., NeonSumoLobbyScene) that sets handoff and loads gameplay.");
            return false;
        }

        private IEnumerator RunStartupFlow(MatchmakingHandoffStore.HandoffData handoff)
        {
            _currentState = FlowState.LoadingHandoff;
            DebugLogger.Log($"{LogPrefix} Setting up from matchmaking flow");
            DebugLogger.Log($"{LogPrefix} Handoff loaded");

            _multiplayerClient = AcquireMultiplayerClient();
            DebugLogger.Log($"{LogPrefix} Multiplayer client type={_multiplayerClient.GetType().FullName} go='{_multiplayerClient.gameObject.name}'");
            _currentState = FlowState.Connecting;

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL requires Init before OnConnected can fire. Retest against the skill after the skill is updated.
            yield return StartCoroutine(ConnectAndInitWebGl(handoff));
            if (_currentState == FlowState.Failed)
                yield break;
#else
            bool connected = false;
            yield return StartCoroutine(ConnectClient(handoff.RoomId, handoff.AppId, handoff.UserSessionId, ok => connected = ok));
            if (!connected)
            {
                _currentState = FlowState.Failed;
                DebugLogger.LogError($"{LogPrefix} Flow failed at state: {_currentState} (multiplayer OnConnected did not fire)");
                yield break;
            }

            _currentState = FlowState.InitializingModules;
            bool modulesOk = false;
            yield return StartCoroutine(InitializeModules(success => modulesOk = success));

            if (!modulesOk)
            {
                _currentState = FlowState.Failed;
                DebugLogger.LogError($"{LogPrefix} Flow failed at state: {_currentState}");
                yield break;
            }
#endif

            _currentState = FlowState.InitializingGame;
            InitializeGameManager();
            yield return StartCoroutine(EnsureEditorGameBots());
            MatchmakingHandoffStore.Clear();
            _currentState = FlowState.Ready;
            NeonSumoLoadingCoordinator.Instance?.NotifyOnlineGameplayFlowReady();
        }

        private MultiplayerClient AcquireMultiplayerClient()
        {
            PlaySdkRuntimeRoot.Instance.DestroyMultiplayerClient();
            return PlaySdkRuntimeRoot.Instance.GetOrCreateMultiplayerClient();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator ConnectAndInitWebGl(MatchmakingHandoffStore.HandoffData handoff)
        {
            bool connected = false;
            void OnConnected() => connected = true;

            _multiplayerClient.OnConnected += OnConnected;
            try
            {
                DebugLogger.Log($"{LogPrefix} WebGL: Initialize then Init, then wait for OnConnected");
                DebugLogger.Log($"{LogPrefix} Initialize roomId={handoff.RoomId}");
                Task initializeTask = _multiplayerClient.Initialize(handoff.RoomId, handoff.AppId, handoff.UserSessionId);
                yield return WaitForTask(initializeTask, ConnectTimeoutSeconds);

                if (!initializeTask.IsCompleted)
                {
                    _currentState = FlowState.Failed;
                    DebugLogger.LogError($"{LogPrefix} Initialize timed out after {ConnectTimeoutSeconds}s.");
                    yield break;
                }

                if (initializeTask.IsFaulted)
                {
                    _currentState = FlowState.Failed;
                    DebugLogger.LogError($"{LogPrefix} Initialize failed: {initializeTask.Exception?.GetBaseException()?.Message}");
                    yield break;
                }

                _currentState = FlowState.InitializingModules;
                bool modulesOk = false;
                yield return StartCoroutine(InitializeModules(success => modulesOk = success));
                if (!modulesOk)
                {
                    _currentState = FlowState.Failed;
                    DebugLogger.LogError($"{LogPrefix} Flow failed at state: {_currentState}");
                    yield break;
                }

                if (!connected)
                {
                    DebugLogger.Log($"{LogPrefix} Waiting for OnConnected after Init...");
                    yield return WaitUntil(() => connected, ConnectTimeoutSeconds);
                }

                if (!connected)
                {
                    _currentState = FlowState.Failed;
                    DebugLogger.LogError($"{LogPrefix} MultiplayerClient OnConnected timed out after Init().");
                    yield break;
                }

                DebugLogger.Log($"{LogPrefix} MultiplayerClient connected successfully after Init");
            }
            finally
            {
                _multiplayerClient.OnConnected -= OnConnected;
            }
        }
#endif

        private IEnumerator ConnectClient(string roomId, string appId, string userSessionId, Action<bool> onComplete)
        {
            bool connected = false;
            void OnConnected() => connected = true;

            _multiplayerClient.OnConnected += OnConnected;
            Task initializeTask = null;
            try
            {
                DebugLogger.Log($"{LogPrefix} Initialize roomId={roomId}");
                initializeTask = _multiplayerClient.Initialize(roomId, appId, userSessionId);
                yield return WaitUntil(() => connected, ConnectTimeoutSeconds);
                if (initializeTask != null && !initializeTask.IsCompleted)
                    yield return WaitForTask(initializeTask, ConnectTimeoutSeconds);
            }
            finally
            {
                _multiplayerClient.OnConnected -= OnConnected;
            }

            if (initializeTask != null && initializeTask.IsFaulted)
            {
                DebugLogger.LogError($"{LogPrefix} Initialize failed: {initializeTask.Exception?.GetBaseException()?.Message}");
                onComplete?.Invoke(false);
                yield break;
            }

            if (!connected)
            {
                DebugLogger.LogError($"{LogPrefix} MultiplayerClient OnConnected timed out after {ConnectTimeoutSeconds}s. Not calling Init().");
                onComplete?.Invoke(false);
                yield break;
            }

            DebugLogger.Log($"{LogPrefix} MultiplayerClient connected successfully");
            onComplete?.Invoke(true);
        }

        private IEnumerator InitializeModules(Action<bool> onComplete)
        {
            if (!TryGetValidatedPlayerCounts(out var minPlayers, out var maxPlayers))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            var options = BuildInitOptions(minPlayers, maxPlayers);
            DebugLogger.Log($"{LogPrefix} Calling Init and waiting for completion...");
            var initTask = _multiplayerClient.Init(options);

            yield return WaitForTask(initTask, InitTimeoutSeconds);

            if (!initTask.IsCompleted)
            {
                if (IsSemanticallyReady())
                {
                    DebugLogger.LogWarning($"{LogPrefix} Init task did not complete in time, but semantic readiness confirmed. Proceeding.");
                }
                else
                {
                    DebugLogger.LogError($"{LogPrefix} Init timeout. PeerId={_multiplayerClient?.PeerId}, RoomId={_multiplayerClient?.RoomId}");
                    onComplete?.Invoke(false);
                    yield break;
                }
            }
            else if (initTask.IsFaulted)
            {
                DebugLogger.LogError($"{LogPrefix} Init failed: {initTask.Exception?.GetBaseException()?.Message}");
                onComplete?.Invoke(false);
                yield break;
            }
            else
            {
                DebugLogger.Log($"{LogPrefix} Init completed successfully result={initTask.Result}");
            }

            onComplete?.Invoke(true);
        }

        private bool TryGetValidatedPlayerCounts(out int minPlayers, out int maxPlayers)
        {
            maxPlayers = Mathf.Max(1, config.MaxPlayers);
            minPlayers = Mathf.Max(1, config.MinPlayers);

            if (maxPlayers <= 0)
            {
                DebugLogger.LogError($"{LogPrefix} Invalid maxPlayers. Ensure NeonSumoConfig is assigned.");
                return false;
            }

            if (minPlayers <= 0 || minPlayers > maxPlayers)
            {
                DebugLogger.LogError($"{LogPrefix} Invalid minPlayers. Must be 1..maxPlayers.");
                return false;
            }

            return true;
        }

        private MultiplayerInitOptions BuildInitOptions(int minPlayers, int maxPlayers)
        {
            return new MultiplayerInitOptions
            {
                modules = new ModulesConfig
                {
                    game = new ModuleOption
                    {
                        enabled = true,
                        desc = "Neon Sumo Game Module",
                        ready_time = config.ReadyTime,
                        start_delay_time = config.StartDelayTime,
                        play_time = config.PlayTime,
                        change_second = config.RingShrinkAccelerationSecond,
                        total_player = maxPlayers,
                        min_total_player = minPlayers,
                        max_total_player = maxPlayers,
                        wait_player_timeout = config.WaitPlayerTimeout
                    },
                    networkSync = new ModuleOption
                    {
                        enabled = true,
                        desc = "Neon Sumo Network Sync"
                    },
                    actionSync = new ModuleOption
                    {
                        enabled = true,
                        desc = "Neon Sumo Action Sync (boost & elimination)"
                    },
                    leaderboard = new ModuleOption
                    {
                        enabled = true,
                        desc = "Neon Sumo Leaderboard"
                    }
                }
            };
        }

        private bool IsSemanticallyReady()
        {
            return _multiplayerClient != null &&
                   !string.IsNullOrEmpty(_multiplayerClient.PeerId) &&
                   !string.IsNullOrEmpty(_multiplayerClient.RoomId) &&
                   !string.IsNullOrEmpty(_multiplayerClient.AppId);
        }

        private void InitializeGameManager()
        {
            if (!TryGetValidatedPlayerCounts(out _, out var maxPlayers))
                return;

            if (gameManager == null)
            {
                DebugLogger.LogError($"{LogPrefix} GameManager is null, cannot initialize");
                return;
            }

            gameManager.Initialize(_multiplayerClient, NeonSumoUserId.Normalize(_multiplayerClient.PeerId), maxPlayers);
            DebugLogger.Log($"{LogPrefix} Game initialized successfully");
            LogClientDetails(maxPlayers);
        }

        private IEnumerator EnsureEditorGameBots()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            yield break;
#else
            if (_multiplayerClient == null)
                yield break;
            if (!TryGetValidatedPlayerCounts(out var minPlayers, out var maxPlayers))
                yield break;

            var options = BuildInitOptions(minPlayers, maxPlayers);
            DebugLogger.Log($"{LogPrefix} Editor Init does not start bots; requesting game/room bots via REST.");
            var botTask = NeonSumoEditorGameBots.EnsureGameAndRoomAsync(_multiplayerClient, options);
            yield return WaitForTask(botTask, EditorBotSetupTimeoutSeconds);

            if (botTask.IsFaulted)
            {
                DebugLogger.LogError($"{LogPrefix} Editor game-bot setup failed: {botTask.Exception?.GetBaseException()?.Message}");
                yield break;
            }

            if (!botTask.IsCompleted)
            {
                DebugLogger.LogWarning($"{LogPrefix} Editor game-bot setup timed out after {EditorBotSetupTimeoutSeconds}s.");
                yield break;
            }

            DebugLogger.Log($"{LogPrefix} Editor game-bot setup complete. Waiting for MasterNotify (IsMasterUser still {_multiplayerClient.IsMasterUser()} until then).");
#endif
        }

        private void LogClientDetails(int maxPlayers)
        {
            DebugLogger.Log($"{LogPrefix} PeerId: {_multiplayerClient.PeerId}");
            DebugLogger.Log($"{LogPrefix} RoomId: {_multiplayerClient.RoomId}");
            DebugLogger.Log($"{LogPrefix} IsMasterUser: {_multiplayerClient.IsMasterUser()}");
            DebugLogger.Log($"{LogPrefix} MaxPlayers: {maxPlayers}");
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!predicate() && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static IEnumerator WaitForTask(Task task, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!task.IsCompleted && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private void OnDestroy()
        {
            _multiplayerClient?.Disconnect();
        }
    }
}
