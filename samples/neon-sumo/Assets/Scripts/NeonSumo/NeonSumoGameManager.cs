using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ViverseSDK;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace NeonSumo
{
    [DefaultExecutionOrder(-100)]
    public class NeonSumoGameManager : MonoBehaviour, IRampUnlockHandler
    {
        [Header("References")]
        public NeonSumoUIManager uiManager;
        public NeonSumoArena arena;
        public NeonSumoRamps ramps;
        public GameObject playerPrefab;
        [Tooltip("Body materials per color_index: Blue, Green, Red, Yellow.")]
        [SerializeField] Material[] playerBodyMaterials;
        [Tooltip("Optional: applied when a player is eliminated. If unset, a gray tint is used.")]
        [SerializeField] Material playerEliminatedBodyMaterial;
        public PlayerBarFusionModernView playerBar;
        public PlayerIndicatorManager indicatorManager;
        [Header("VFX")]
        [Tooltip("Template particle system used for player collision sparks (e.g. scene VFX_Sparks).")]
        [SerializeField] private ParticleSystem collisionSparksTemplate;
        [Tooltip("Minimum time between spawned collision spark effects.")]
        [SerializeField] private float collisionSparksCooldown = 0.06f;
        [Tooltip("Prewarmed pooled spark instances (Instantiate at load; avoids per-collision Instantiate/Destroy).")]
        [SerializeField] private int collisionSparksPoolSize = 8;

        [Header("Game Settings")]
        public NeonSumoConfig config;
        public bool enableArenaShrink = false;
        [Tooltip("Seconds shown in the start countdown UI.")]
        public int startCountdownSeconds = 3;

        [Header("Ramp Spawn")]
        public bool useRampSpawns = true;
        public bool useRampSpawnsOnRestart = true;
        public bool useRampUnlockTrigger = true;

        [Header("Ramp Retract")]
        [Tooltip("Seconds after game start to retract ramps.")]
        public float rampRetractDelaySeconds = 1.5f;

        [Header("Ready Countdown")]
        [Tooltip("Seconds to wait before starting when min players ready (resets if new player joins).")]
        [SerializeField] private float readyCountdownDuration = 15f;

        private MultiplayerClient _multiplayerClient;
        private readonly NeonSumoNetworkEventRouter _networkEventRouter = new NeonSumoNetworkEventRouter();
        private ActionSyncEventHandler _actionSyncHandler;
        private HostInputSync _hostInputSync;
        private NeonSumoCpuSumoBrain _cpuBrain;
        private SpawnCoordinator _spawnCoordinator;
        private RingShrinkHandler _ringShrinkHandler;
        private PlayerColorManager _playerColorManager;
        private Dictionary<string, NeonSumoPlayer> _players = new Dictionary<string, NeonSumoPlayer>();
        private string _localPlayerId;
        /// <summary>From room info (<c>master_client_id</c> / create-room); original room creator for P1 ordering.</summary>
        private string _roomHostUserId;
        private bool _isGameActive = false;
        private bool _isInitialized = false;
        private bool _didBackfill = false;
        private bool _backfillInProgress = false;
        private int _localWins = 0;
        private readonly Dictionary<string, int> _peerScores = new Dictionary<string, int>();
        private int _totalPlayerCount = 4; // Will be set from GameModule config
        private PlayerBarCoordinator _playerBarCoordinator;
        private bool _rampSpawnPrepared = false;
        private bool _rampsRetracted = false;
        private bool _isMasterResolved = false;
        private bool _isMasterUser = false;
        private readonly HashSet<string> _appliedColorFromSpawn = new HashSet<string>();
        private Coroutine _startCountdownRoutine;
        private Coroutine _rampRetractRoutine;
        private bool _roundEndTriggered;
        // SDK readiness and manual lobby readiness are intentionally separate.
        // Game.Ready()/OnPlayerAllReady represent VIVERSE session presence.
        // _manualReadyPlayerIds represents players who clicked the game's Ready UI.
        private bool _localManualReadyClicked;
        private bool _sdkAllPlayersReady;
        private readonly HashSet<string> _manualReadyPlayerIds = new HashSet<string>(StringComparer.Ordinal);
        private ReadyCountdownCoordinator _readyCountdownCoordinator;
        private NeonSumoMessageHandler _messageHandler;
        private Action<string> _onMessageHandler;
        private Action<string> _onMasterNotifyHandler;
        private MasterSyncHandler _masterSyncHandler;
        private static readonly Vector3 HoldingSpawnPosition = new Vector3(0f, -1000f, 0f);
        private NeonSumoArenaCamera _cachedArenaCamera;
        private float _lastCollisionSparksTime = -999f;
        private bool _loggedMissingCollisionSparksTemplate;
        private ParticleSystem _collisionSparksPrototype;
        private readonly Stack<ParticleSystem> _collisionSparksPool = new Stack<ParticleSystem>();
        private Transform _collisionSparksPoolRoot;

        private readonly NeonSumoDisplayNameRegistry _displayNames = new NeonSumoDisplayNameRegistry();
        /// <summary>Preserves local sanitized display across <see cref="PerformRestart"/> when prefs seed was one-shot consumed.</summary>
        private string _sessionLocalSanitizedDisplayName;

        private readonly NeonSumoRoundFlow _flow = new NeonSumoRoundFlow();

        // Event fired when GameManager is initialized (similar to MultiplayerClient.OnConnected pattern)
        public event Action OnInitialized;

        /// <summary>Fired after world/players/UI reset from a game restart (local <see cref="RequestRestart"/> or <c>OnGameRestart</c>). Offline bootstrap may re-arm match timers.</summary>
        public event Action OnWorldResetAfterRestart;

        void Awake()
        {
            bool missing = false;
            if (config == null) { DebugLogger.LogError("[NeonSumo] NeonSumoGameManager: NeonSumoConfig is required but not assigned. Assign in Inspector."); missing = true; }
            else { config.EnsureRuntimeValidity(); }
            if (uiManager == null) { DebugLogger.LogError("[NeonSumo] NeonSumoGameManager: NeonSumoUIManager is not assigned. Assign in Inspector or via Bootstrap."); missing = true; }
            if (indicatorManager == null) { DebugLogger.LogError("[NeonSumo] NeonSumoGameManager: PlayerIndicatorManager is not assigned. Assign in Inspector or via Bootstrap."); missing = true; }
            if (missing) { enabled = false; }
            else
            {
                EnsureCollisionSparksTemplateResolvedAndPool();
            }
        }

        void OnEnable()
        {
            NeonSumoPlatformProfiles.LocalDisplayProfileChanged += OnLocalViVerseDisplayProfileMaybeChanged;
        }

        void OnDisable()
        {
            NeonSumoPlatformProfiles.LocalDisplayProfileChanged -= OnLocalViVerseDisplayProfileMaybeChanged;
        }

        /// <summary>
        /// VIVERSE avatar-list scrape or richer auth JSON can arrive after matchmaking handshake; rebroadcast sanitized name via ActionSync.
        /// </summary>
        void OnLocalViVerseDisplayProfileMaybeChanged()
        {
            string skipReason = string.IsNullOrEmpty(_localPlayerId) ? "local_peer_id_missing" : null;

            // #region agent log
            if (skipReason != null)
                NeonSumoDebugNdjson.Log(
                    hypothesisId: "H4",
                    location: "NeonSumoGameManager.OnLocalViVerseDisplayProfileMaybeChanged",
                    message: "late_profile_blocked",
                    data: new
                    {
                        skip = skipReason,
                        localPeerIdChars = _localPlayerId?.Length ?? 0,
                        offlineSafePath = true,
                    });
            // #endregion

            if (string.IsNullOrEmpty(_localPlayerId))
                return;

            if (!NeonSumoPlatformProfiles.TryGetProfileDisplayLabel(out string raw) || string.IsNullOrEmpty(raw))
                return;

            if (!_displayNames.TrySetRaw(_localPlayerId, raw, out string sanitized))
                return;

            _sessionLocalSanitizedDisplayName = sanitized;
            if (_multiplayerClient?.ActionSync != null)
                BroadcastLocalDisplayNameAnnouncement();
            RefreshDisplayNameDependentUi();
            DebugLogger.Log($"[NeonSumo][DisplayNames] Late VIVERSE profile — updated local registry: {sanitized}");
            // #region agent log
            NeonSumoDebugNdjson.Log(
                hypothesisId: "H4",
                location: "NeonSumoGameManager.OnLocalViVerseDisplayProfileMaybeChanged",
                message: "late_profile_registry_updated",
                data: new
                {
                    labelChars = sanitized?.Length ?? 0,
                    hadActionSyncAnnouncement = _multiplayerClient?.ActionSync != null,
                });
            // #endregion
        }

        /// <summary>
        /// Resolves optional scene template once and prewarms the spark pool so the first collision does not pay FindObjectsByType or per-hit Instantiate.
        /// </summary>
        void EnsureCollisionSparksTemplateResolvedAndPool()
        {
            if (collisionSparksTemplate == null)
            {
                collisionSparksTemplate = FindCollisionSparksTemplateInScene();
            }

            if (collisionSparksTemplate == null)
            {
                return;
            }

            _collisionSparksPrototype = collisionSparksTemplate;
            if (_collisionSparksPoolRoot == null)
            {
                var poolGo = new GameObject("CollisionSparksPool");
                poolGo.transform.SetParent(transform, false);
                poolGo.SetActive(true);
                _collisionSparksPoolRoot = poolGo.transform;
            }

            int target = Mathf.Max(0, collisionSparksPoolSize);
            while (_collisionSparksPool.Count < target)
            {
                var ps = Instantiate(_collisionSparksPrototype, _collisionSparksPoolRoot);
                ps.gameObject.SetActive(false);
                _collisionSparksPool.Push(ps);
            }
        }

        // FIX #4: Explicit initialization, no polling
        public void Initialize(MultiplayerClient client, string localPlayerId, int totalPlayerCount = 8)
        {
            _multiplayerClient = client;
            _localPlayerId = NeonSumoUserId.Normalize(localPlayerId);
            _totalPlayerCount = totalPlayerCount > 0 ? totalPlayerCount : GetConfiguredMaxPlayers();
            arena?.SetMaxPlayers(_totalPlayerCount);
            if (arena != null && _multiplayerClient != null)
            {
                arena.ConfigureNetwork(_multiplayerClient, IsMasterClient());
            }
            EnsurePlayerColorManager();
            EnsurePlayerBarCoordinator();
            EnsureReadyCountdownCoordinator();
            EnsureSpawnCoordinator();
            EnsureRingShrinkHandler();
            SubscribeSdkEvents();
            EnsureHostInputSync();
            _isInitialized = true;
            ResetSessionFlags();
            ResetRoundStateFlags();
            EnsureRampsController();
            
            DebugLogger.Log($"[NeonSumo] GameManager.Initialize() complete. uiManager={(uiManager != null ? "NOT NULL" : "NULL")}");

            // Local player will not trigger OnClientConnected; spawn explicitly.
            if (!string.IsNullOrEmpty(_localPlayerId) && !_players.ContainsKey(_localPlayerId))
            {
                SpawnPlayer(_localPlayerId, true);
            }

            ApplyLocalMultiplayerDisplayNameHandshake();
            NeonSumoPlatformProfiles.TryScheduleAvatarFallbackFromStoredPrefs(this);
            
            // Defer backfill until game bot events confirm readiness.
            
            // Fire initialization event (similar to MultiplayerClient.OnConnected)
            OnInitialized?.Invoke();
            
            // Enable ready button now that we're initialized (backward compatibility)
            if (uiManager != null)
            {
                DebugLogger.Log("[NeonSumo] Calling uiManager.OnGameManagerInitialized()");
                uiManager.OnGameManagerInitialized();
            }
            else
            {
                DebugLogger.LogError("[NeonSumo] uiManager is NULL in Initialize()! OnGameManagerInitialized() will never be called!");
            }
        }

        public void InitializeLocal(int localPlayerCount = 1)
        {
            if (!TryBeginLocalInitialization(localPlayerCount))
                return;

            ConfigureArenaForLocal();
            EnsureEarlyLocalSystems();
            ClearLocalPlayersList();
            for (int i = 0; i < _totalPlayerCount; i++)
            {
                string userId = i == 0 ? _localPlayerId : $"BOT_{i}";
                SpawnPlayer(userId, i == 0);
            }
            FinalizeLocalCoordinatorSystems();
            SeedLocalDisplayNameFromPrefs(announceViaActionSync: false);
            MarkLocalInitializedAndNotify();
            NotifyLocalUIManagerInitialized();
        }

        /// <summary>
        /// Offline-only: same end state as <see cref="InitializeLocal"/>, but yields between phases to spread cost across frames.
        /// </summary>
        /// <param name="traceStartupPhases">When true, logs phase order (see NeonSumoOfflineBootstrap logStartupPhases).</param>
        public IEnumerator InitializeLocalStaged(int localPlayerCount = 1, bool traceStartupPhases = false)
        {
            void Trace(string phase)
            {
                if (traceStartupPhases)
                    DebugLogger.Log($"[NeonSumo][Startup] {phase}");
            }

            bool beganLocal;
            using (NeonSumoStartupProfilerMarkers.InitializeManagers.Auto())
            {
                Trace("InitializeManagers");
                beganLocal = TryBeginLocalInitialization(localPlayerCount);
            }

            if (!beganLocal)
                yield break;

            Trace("after TryBeginLocal");
            yield return null;

            using (NeonSumoStartupProfilerMarkers.InitializeArena.Auto())
            {
                Trace("InitializeArena");
                ConfigureArenaForLocal();
                EnsureEarlyLocalSystems();
                ClearLocalPlayersList();
            }

            yield return null;

            using (NeonSumoStartupProfilerMarkers.SpawnLocalPlayer.Auto())
            {
                Trace("SpawnLocalPlayer");
                SpawnPlayer(_localPlayerId, true);
            }

            yield return null;

            // One Begin/End per frame: ProfilerMarker cannot bridge yield (non-matching EndSample otherwise).
            for (int i = 1; i < _totalPlayerCount; i++)
            {
                NeonSumoStartupProfilerMarkers.SpawnBot.Begin();
                try
                {
                    Trace($"SpawnBot BOT_{i}");
                    SpawnPlayer($"BOT_{i}", false);
                }
                finally
                {
                    NeonSumoStartupProfilerMarkers.SpawnBot.End();
                }

                yield return null;
            }

            NeonSumoStartupProfilerMarkers.FinalizeSpawnSystems.Begin();
            try
            {
                Trace("FinalizeLocalCoordinatorSystems");
                FinalizeLocalCoordinatorSystems();
            }
            finally
            {
                NeonSumoStartupProfilerMarkers.FinalizeSpawnSystems.End();
            }

            yield return null;

            using (NeonSumoStartupProfilerMarkers.UIInit.Auto())
            {
                Trace("MarkLocalInitializedAndNotify");
                SeedLocalDisplayNameFromPrefs(announceViaActionSync: false);
                MarkLocalInitializedAndNotify();
                Trace("NotifyLocalUIManagerInitialized");
                NotifyLocalUIManagerInitialized();
            }
        }

        /// <summary>Caches <see cref="NeonSumoArenaCamera"/> before intro / countdown. Call from startup after optional camera deferral.</summary>
        public void CacheArenaCameraForStartup()
        {
            GetOrFindArenaCamera();
        }

        bool TryBeginLocalInitialization(int localPlayerCount)
        {
            _multiplayerClient = null;
            _roomHostUserId = null;
            if (config == null)
            {
                DebugLogger.LogError("[NeonSumo] InitializeLocal failed: NeonSumoConfig is not assigned.");
                return false;
            }

            _totalPlayerCount = Mathf.Clamp(localPlayerCount, 1, GetConfiguredMaxPlayers());
            if (arena == null)
            {
                DebugLogger.LogError("[NeonSumo] InitializeLocal failed: NeonSumoArena is not assigned in the inspector.");
                return false;
            }

            return true;
        }

        void ConfigureArenaForLocal()
        {
            arena.SetMaxPlayers(_totalPlayerCount);
            arena.ConfigureNetwork(null, true);
            ResetRoundStateFlags();
            EnsureRampsController();
        }

        void EnsureEarlyLocalSystems()
        {
            EnsurePlayerColorManager();
            EnsurePlayerBarCoordinator();
            EnsureSpawnCoordinator();
        }

        void ClearLocalPlayersList()
        {
            _players.Clear();
            _localPlayerId = NeonSumoUserId.Normalize("LOCAL_0");
        }

        void FinalizeLocalCoordinatorSystems()
        {
            EnsureReadyCountdownCoordinator();
            EnsureSpawnCoordinator();
            EnsureRingShrinkHandler();
            EnsureHostInputSync();
        }

        void MarkLocalInitializedAndNotify()
        {
            _isInitialized = true;
            // #region agent log
            NeonSumoDebugNdjson.Log(
                hypothesisId: "H4",
                location: "NeonSumoGameManager.MarkLocalInitializedAndNotify",
                message: "local_init_marked_ready",
                data: new
                {
                    localPlayerId = _localPlayerId ?? "(null)",
                    multiplayerClientAttached = _multiplayerClient != null,
                    displayHandshakeWillRunOnlyWithMultiplayerClient = true,
                });
            // #endregion
            OnInitialized?.Invoke();
        }

        void NotifyLocalUIManagerInitialized()
        {
            if (uiManager != null)
            {
                DebugLogger.Log("[NeonSumo] Local game initialized, enabling UI");
                uiManager.OnGameManagerInitialized();
            }
            else
            {
                DebugLogger.LogWarning("[NeonSumo] uiManager is NULL in InitializeLocal()");
            }
        }

        public void StartLocalGame()
        {
            if (!_isInitialized)
            {
                DebugLogger.LogWarning("[NeonSumo] StartLocalGame called before InitializeLocal; initializing with 1 player");
                InitializeLocal(1);
            }

            DebugLogger.Log("[NeonSumo] Local game: starting intro (Get Ready + 3-2-1) before StartGame, same as multiplayer");
            StartIntroPhaseIfNeeded();
        }

        void SubscribeSdkEvents()
        {
            if (_multiplayerClient == null) return;

            // Game Module Events
            _multiplayerClient.Game.OnCountdownToStart += HandleCountdownToStart;
            _multiplayerClient.Game.OnCountdownToEnd += HandleCountdownToEnd;
            _multiplayerClient.Game.OnGameEnd += HandleGameEnd;
            _multiplayerClient.Game.OnGameRestart += HandleGameRestart;
            _multiplayerClient.Game.OnGameTimeUp += HandleGameTimeUp;
            _multiplayerClient.Game.OnPlayerAllReady += HandleAllPlayersReady;
            _multiplayerClient.Game.OnWaitForPlayer += HandleWaitForPlayer;
            _multiplayerClient.Game.OnErrorNotify += HandleGameError;
            EnsureMasterSyncHandler();
            _onMasterNotifyHandler = data => _masterSyncHandler?.Handle(data);
            _multiplayerClient.Game.OnMasterNotify += _onMasterNotifyHandler;

            // Network Sync Events (position only)
            _multiplayerClient.NetworkSync.OnNotifyPositionUpdate += HandleRemotePositionUpdate;
            _multiplayerClient.NetworkSync.OnNotifyRemove += HandleRemoteRemove;
            _multiplayerClient.Leaderboard.OnLeaderboardUpdate += HandleLeaderboardUpdate;

            // Action Sync Events (via NetworkEventRouter)
            var actionCtx = new ActionSyncEventContext
            {
                LocalPlayerId = _localPlayerId,
                Arena = arena,
                ContainsPlayer = userId => _players.ContainsKey(NeonSumoUserId.Normalize(userId)),
                EnsurePlayerPresent = EnsurePlayerPresent,
                EliminatePlayer = userId => EliminatePlayer(userId, false),
                ResetSpawnAckSent = () => _spawnCoordinator?.ResetSpawnAckSent(),
                StorePendingRampSpawn = (userId, pos, rot, idx) => _spawnCoordinator?.StorePendingRampSpawn(userId, pos, rot, idx),
                ApplyRampSpawnFromNetwork = ApplyRampSpawnFromNetwork,
                ApplyPlayerColor = (userId, colorIndex) =>
                {
                    if (_playerColorManager == null) return;
                    _playerColorManager.SetAssignment(userId, colorIndex);
                    _playerColorManager.ApplyAssignedColor(userId);
                    _playerColorManager.RefreshIndicators();
                    _playerBarCoordinator?.RebuildRoster();
                },
                HasAppliedColorFromSpawn = userId => _appliedColorFromSpawn.Contains(userId),
                AddAppliedColorFromSpawn = userId => _appliedColorFromSpawn.Add(userId),
                RegisterSpawnAck = userId => _spawnCoordinator?.RegisterSpawnAck(userId),
                TriggerRampRetract = sendNetwork => TriggerRampRetract(sendNetwork),
                GetPlayer = GetPlayer,
                UnlockControlsForAll = sendNetwork => UnlockControlsForAll(sendNetwork),
                ApplyRoundWin = ApplyRoundWin,
                IsRoundEndingOrResults = () => _flow.CurrentState == NeonSumoGameState.RoundEnding || _flow.CurrentState == NeonSumoGameState.RoundResults,
                StartIntroPhaseIfNeeded = StartIntroPhaseIfNeeded,
                HideStartingIn = () => uiManager?.HideStartingIn(),
                ShowStartingIn = sec => uiManager?.ShowStartingIn(sec),
                UpdateStartingIn = sec => uiManager?.UpdateStartingIn(sec),
                ResetManualReady = ResetLocalManualReady,
                OnPlayerManualReady = HandlePlayerManualReady,
                ApplyCameraShake = strength => GetOrFindArenaCamera()?.Shake(strength),
                RegisterPeerDisplayName = OnRemotePeerDisplayNameReceived,
            };
            _actionSyncHandler = new ActionSyncEventHandler(actionCtx);
            _networkEventRouter.Setup(_multiplayerClient, _localPlayerId, evt => _actionSyncHandler.Handle(evt));

            // General / peer events
            _multiplayerClient.OnClientConnected += HandlePlayerJoined;
            _multiplayerClient.OnClientDisconnected += HandlePlayerLeft;
            EnsureMessageHandler();
            _onMessageHandler = data => _messageHandler?.Handle(data);
            if (_multiplayerClient.General != null)
                _multiplayerClient.General.OnMessage += _onMessageHandler;
            else
                _multiplayerClient.OnMessage += _onMessageHandler;

            DebugLogger.Log("[NeonSumo] SDK callbacks setup complete");
        }

        void UnsubscribeSdkEvents()
        {
            if (_multiplayerClient == null) return;

            _multiplayerClient.Game.OnCountdownToStart -= HandleCountdownToStart;
            _multiplayerClient.Game.OnCountdownToEnd -= HandleCountdownToEnd;
            _multiplayerClient.Game.OnGameEnd -= HandleGameEnd;
            _multiplayerClient.Game.OnGameRestart -= HandleGameRestart;
            _multiplayerClient.Game.OnGameTimeUp -= HandleGameTimeUp;
            _multiplayerClient.Game.OnPlayerAllReady -= HandleAllPlayersReady;
            _multiplayerClient.Game.OnWaitForPlayer -= HandleWaitForPlayer;
            _multiplayerClient.Game.OnErrorNotify -= HandleGameError;
            if (_onMasterNotifyHandler != null)
                _multiplayerClient.Game.OnMasterNotify -= _onMasterNotifyHandler;
            _multiplayerClient.NetworkSync.OnNotifyPositionUpdate -= HandleRemotePositionUpdate;
            _multiplayerClient.NetworkSync.OnNotifyRemove -= HandleRemoteRemove;
            _multiplayerClient.Leaderboard.OnLeaderboardUpdate -= HandleLeaderboardUpdate;
            _networkEventRouter.Teardown();
            _multiplayerClient.OnClientConnected -= HandlePlayerJoined;
            _multiplayerClient.OnClientDisconnected -= HandlePlayerLeft;
            if (_onMessageHandler != null)
            {
                if (_multiplayerClient.General != null)
                    _multiplayerClient.General.OnMessage -= _onMessageHandler;
                _multiplayerClient.OnMessage -= _onMessageHandler;
            }
        }

        #region Player Management

        void TryBackfillExistingPlayers()
        {
            if (_didBackfill || _backfillInProgress) return;
            if (_multiplayerClient == null || _multiplayerClient.Game == null) return;

            _backfillInProgress = true;
            _ = BackfillExistingPlayersSafeAsync();
        }

        async Task BackfillExistingPlayersSafeAsync()
        {
            _backfillInProgress = true;
            try
            {
                await BackfillExistingPlayersAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.LogWarning($"[NeonSumo] Backfill failed: {ex.Message}");
            }
            finally
            {
                _backfillInProgress = false;
            }
        }

        async Task BackfillExistingPlayersAsync()
        {
            if (_multiplayerClient == null || _multiplayerClient.Game == null)
                return;

            var roomInfoJson = await _multiplayerClient.GetRoomInfo();
            DebugLogger.Log($"[NeonSumo] GetRoomInfo raw={roomInfoJson}");
            var roomInfo = ParseJsonObjectDictionary(roomInfoJson);
            if (roomInfo == null)
            {
                DebugLogger.LogWarning($"[NeonSumo] Backfill skipped: room info was null. Raw={roomInfoJson}");
                return;
            }

            TryApplyRoomHostFromRoomInfo(roomInfo);

            var userIds = ExtractUserIds(roomInfo);
            if (userIds.Count == 0)
            {
                DebugLogger.LogWarning($"[NeonSumo] Backfill found no users in room info. Raw={roomInfoJson}");
                return;
            }

            _didBackfill = true;

            DebugLogger.Log($"[NeonSumo] Backfilling room members: {userIds.Count} users found");
            foreach (var rawId in userIds)
            {
                if (!NeonSumoUserId.TryNormalize(rawId, out var userId)) continue;
                if (_players.ContainsKey(userId)) continue;

                DebugLogger.Log($"[NeonSumo] Spawning missing remote player: {userId}");
                SpawnPlayer(userId, userId == _localPlayerId);
                _spawnCoordinator?.TryApplyPendingRampSpawn(userId);
            }

            RefreshHostFirstPresentationAfterBackfill();
            uiManager?.RefreshLobbyHostDependentControls();
        }

        static JObject ParseJsonObject(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        static Dictionary<string, object> ParseJsonObjectDictionary(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch
            {
                return null;
            }
        }

        List<string> ExtractUserIds(Dictionary<string, object> roomInfo)
        {
            var ids = new HashSet<string>();
            if (roomInfo == null)
                return new List<string>();

            JObject root;
            try
            {
                root = JObject.FromObject(roomInfo);
            }
            catch
            {
                return new List<string>();
            }

            CollectUserIdsFromContainer(root, ids);
            foreach (var wrapper in new[] { "room_data", "room", "data", "payload" })
            {
                if (root[wrapper] is JObject nested)
                    CollectUserIdsFromContainer(nested, ids);
                else if (root[wrapper] != null)
                    AddIdsFromToken(root[wrapper], ids);
            }

            return new List<string>(ids);
        }

        void CollectUserIdsFromContainer(JObject obj, HashSet<string> ids)
        {
            if (obj == null)
                return;
            string[] keys =
            {
                "user_ids", "user_list", "users", "members", "connected_clients",
                "actors", "players", "peers"
            };
            foreach (var key in keys)
            {
                if (obj[key] != null)
                    AddIdsFromToken(obj[key], ids);
            }
        }

        void TryApplyRoomHostFromRoomInfo(Dictionary<string, object> roomInfo)
        {
            if (roomInfo == null) return;
            try
            {
                var jo = JObject.FromObject(roomInfo);
                string id = ReadMasterClientIdFromRoomJo(jo);
                if (!string.IsNullOrEmpty(id))
                    SetRoomHostUserIdPreferringPlayerKey(id);
            }
            catch (Exception ex)
            {
                DebugLogger.LogWarning($"[NeonSumo] Could not parse room host from room info: {ex.Message}");
            }
        }

        static string ReadMasterClientIdFromRoomJo(JObject jo)
        {
            if (jo == null) return null;
            string id =
                jo.Value<string>("master_client_id")
                ?? jo.Value<string>("masterClientId")
                ?? jo.Value<string>("host_id")
                ?? jo.Value<string>("host_user_id");
            if (!string.IsNullOrEmpty(id)) return id;
            foreach (var wrapper in new[] { "room_data", "room", "data" })
            {
                if (jo[wrapper] is JObject nested)
                {
                    id =
                        nested.Value<string>("master_client_id")
                        ?? nested.Value<string>("masterClientId")
                        ?? nested.Value<string>("host_id")
                        ?? nested.Value<string>("host_user_id");
                    if (!string.IsNullOrEmpty(id))
                        return id;
                    if (nested["room"] is JObject innerRoom)
                    {
                        id =
                            innerRoom.Value<string>("master_client_id")
                            ?? innerRoom.Value<string>("masterClientId")
                            ?? innerRoom.Value<string>("host_id")
                            ?? innerRoom.Value<string>("host_user_id");
                        if (!string.IsNullOrEmpty(id))
                            return id;
                    }
                }
            }
            return id;
        }

        /// <summary>
        /// Aligns cached host id with an actual <see cref="_players"/> key when casing differs so host-first sort applies.
        /// </summary>
        void SetRoomHostUserIdPreferringPlayerKey(string candidate)
        {
            candidate = NeonSumoUserId.Normalize(candidate);
            if (string.IsNullOrEmpty(candidate)) return;

            if (_players.Count > 0)
            {
                foreach (var k in _players.Keys)
                {
                    if (string.Equals(k, candidate, StringComparison.Ordinal))
                    {
                        _roomHostUserId = k;
                        return;
                    }
                }
                foreach (var k in _players.Keys)
                {
                    if (string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        _roomHostUserId = k;
                        return;
                    }
                }
            }

            _roomHostUserId = candidate;
        }

        void OnCanonicalRoomHostFromNetwork(string masterUserFromNotify)
        {
            masterUserFromNotify = NeonSumoUserId.Normalize(masterUserFromNotify);
            SetRoomHostUserIdPreferringPlayerKey(masterUserFromNotify);
            DebugLogger.Log($"[NeonSumo] Canonical room host set: {_roomHostUserId} (from MasterNotify / game sync)");
            EnsurePlayerPresent(masterUserFromNotify);

            RefreshHostFirstPresentationAfterBackfill();
            uiManager?.RefreshLobbyHostDependentControls();
        }

        void EnsurePlayerPresent(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;
            if (!_players.ContainsKey(userId))
            {
                DebugLogger.Log($"[NeonSumo] Ensuring player exists: {userId} local={userId == _localPlayerId}");
                SpawnPlayer(userId, userId == _localPlayerId);
            }
            _spawnCoordinator?.TryApplyPendingRampSpawn(userId);
        }

        /// <summary>
        /// Same host id on every client: set from MasterNotify <c>master_user</c> and/or room info. Offline local play uses <see cref="_localPlayerId"/>.
        /// </summary>
        public string GetEffectiveRoomHostUserId()
        {
            if (!string.IsNullOrEmpty(_roomHostUserId))
                return _roomHostUserId;
            if (_multiplayerClient == null)
                return _localPlayerId;
            // Room JSON sometimes omits host fields; after MasterNotify we still know the authoritative peer.
            if (_isMasterResolved && _isMasterUser && !string.IsNullOrEmpty(_localPlayerId))
                return _localPlayerId;
            return null;
        }

        /// <summary>
        /// Who may press Lobby Restart / Play Again: network master and/or canonical room host (room JSON or resolved master).
        /// Prefer this over <see cref="IsHostClient"/> for UI, which can lag until <c>master_notify</c> or WebGL <c>SetMaster</c> runs.
        /// </summary>
        public bool IsLocalLobbyRestartAuthority()
        {
            if (_multiplayerClient == null)
                return true;
            if (IsMasterClient())
                return true;
            var hostId = GetEffectiveRoomHostUserId();
            return !string.IsNullOrEmpty(hostId) && NeonSumoPlayerOrder.UserIdMatchesHost(_localPlayerId, hostId);
        }

        void RefreshHostFirstPresentationAfterBackfill()
        {
            if (_players.Count == 0) return;

            uiManager?.RefreshPlayerListOrdering();
            _playerBarCoordinator?.RebuildRoster();
            if (IsMasterClient())
                RemapMasterColorsByHostFirstOrder();
            SetupPlayerIndicators();
        }

        void RemapMasterColorsByHostFirstOrder()
        {
            if (_playerColorManager == null || !IsMasterClient()) return;

            var ordered = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());
            for (int i = 0; i < ordered.Count; i++)
            {
                string uid = ordered[i];
                _playerColorManager.SetAssignment(uid, i % PlayerColorManager.PaletteSize);
                _playerColorManager.ApplyAssignedColor(uid);
            }
            _playerColorManager.BroadcastAllAssignments();
            _playerColorManager.RefreshIndicators();
        }

        int GetHostFirstColorIndexForSpawnSync(string userId)
        {
            if (_playerColorManager == null || string.IsNullOrEmpty(userId)) return 0;
            if (!IsMasterClient())
            {
                return _playerColorManager.TryGetColorIndex(userId, out int idx) ? idx : 0;
            }

            var ordered = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());
            int slot = ordered.IndexOf(userId);
            if (slot < 0) slot = ordered.Count;
            int colorIndex = slot % PlayerColorManager.PaletteSize;
            _playerColorManager.SetAssignment(userId, colorIndex);
            return colorIndex;
        }

        void AddIdsFromToken(JToken token, HashSet<string> ids)
        {
            if (token == null) return;

            if (token.Type == JTokenType.String)
            {
                if (NeonSumoUserId.TryNormalize(token.Value<string>() ?? token.ToString(), out var id))
                    ids.Add(id);
                return;
            }

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var id = obj.Value<string>("user_id")
                         ?? obj.Value<string>("id")
                         ?? obj.Value<string>("session_id")
                         ?? obj.Value<string>("peer_id");
                if (NeonSumoUserId.TryNormalize(id, out id))
                    ids.Add(id);
                
                // Also handle dictionary-style payloads: { "0": "id1", "1": "id2" }
                foreach (var property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.String)
                    {
                        if (NeonSumoUserId.TryNormalize(property.Value.Value<string>() ?? property.Value.ToString(), out var valueId))
                            ids.Add(valueId);
                    }
                }
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                {
                    AddIdsFromToken(item, ids);
                }
            }
        }

        void HandlePlayerJoined(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;

            DebugLogger.Log($"[NeonSumo] Player joined: {userId} localPlayerId={_localPlayerId} peerId={_multiplayerClient?.PeerId}");

            bool isNew = !_players.ContainsKey(userId);
            if (isNew)
            {
                SpawnPlayer(userId, userId == _localPlayerId);
            }
            _spawnCoordinator?.TryApplyPendingRampSpawn(userId);

            if (IsMasterClient())
            {
                _playerColorManager?.BroadcastAllAssignments();
            }

            if (IsMasterClient() && useRampSpawns && arena != null && arena.HasRampSpawns() && _spawnCoordinator != null)
            {
                if (!_spawnCoordinator.HasAnyAssignments)
                {
                    var userIds = new List<string>(_players.Keys);
                    NeonSumoPlayerOrder.SortUserIdsHostFirst(userIds, GetEffectiveRoomHostUserId());
                    _spawnCoordinator.AssignRampSpawnIndices(userIds);
                }
                else
                {
                    _spawnCoordinator.GetOrAssignRampSpawnIndex(userId);
                }

                if (_multiplayerClient != null && _spawnCoordinator.HasAssignmentsForPlayerCount(_players.Count))
                {
                    _spawnCoordinator.BroadcastRampSpawns(_players, _players.Count);
                }
            }

            if (IsMasterClient() && _rampsRetracted && _multiplayerClient != null)
            {
                NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.RampsRetract, string.Empty, Guid.NewGuid().ToString());
            }

            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                SetupPlayerIndicators();
            }

            if (isNew && IsPreGameLobbyState() && IsMasterClient())
            {
                DebugLogger.Log("[NeonSumo] New player joined — resetting pre-game ready (roster changed)");
                ResetPreGameRosterReadyState();
            }

            BroadcastLocalDisplayNameAnnouncement();
        }

        void HandlePlayerLeft(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;

            DebugLogger.Log($"[NeonSumo] Player left: {userId}");

            _displayNames.RemoveUser(userId);

            // Reset spawn ack gate so we don't hang if a player disconnects mid-ack-wait
            _spawnCoordinator?.ResetSpawnAckGate();

            if (_players.ContainsKey(userId))
            {
                EliminatePlayer(userId, false);
            }

            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                SetupPlayerIndicators();
            }

            // Broadcast ramp spawns so remaining clients stay in sync (lobby indicators).
            if (IsMasterClient() && useRampSpawns && arena != null && arena.HasRampSpawns() && _multiplayerClient != null && _spawnCoordinator != null && _spawnCoordinator.HasAssignmentsForPlayerCount(_players.Count))
            {
                _spawnCoordinator.BroadcastRampSpawns(_players, _players.Count);
            }

            if (IsPreGameLobbyState() && IsMasterClient())
            {
                bool wasCountdown = _flow.CurrentState == NeonSumoGameState.Countdown;
                DebugLogger.Log("[NeonSumo] Player left — resetting pre-game ready (roster changed)");
                ResetPreGameRosterReadyState();
                if (wasCountdown && _players.Count < GetConfiguredMinPlayers())
                {
                    uiManager?.ShowMainMenu(NeonSumoUIManager.MainMenuMode.AwaitingHostRestart);
                }
            }
        }

        void SpawnPlayer(string userId, bool isLocal)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;
            if (_players.ContainsKey(userId))
                return;

            isLocal = userId == _localPlayerId;

            if (arena == null)
            {
                DebugLogger.LogError("[NeonSumo] SpawnPlayer failed: arena reference is null.");
                return;
            }

            if (playerPrefab == null)
            {
                DebugLogger.LogError("[NeonSumo] SpawnPlayer failed: playerPrefab is not assigned.");
                return;
            }

            Vector3 spawnPos;
            Quaternion spawnRot = Quaternion.identity;
            int rampSpawnIndex = 0;
            bool hasRampSpawns = useRampSpawns && arena.HasRampSpawns();
            bool isMaster = IsMasterClient();

            if (hasRampSpawns)
            {
                if (isMaster)
                {
                    rampSpawnIndex = _spawnCoordinator.GetOrAssignRampSpawnIndex(userId);
                    spawnPos = arena.GetRampSpawnPosition(rampSpawnIndex);
                    spawnRot = arena.GetRampSpawnRotation(rampSpawnIndex);
                    DebugLogger.Log($"[NeonSumo] SpawnPlayer ramp spawn user={userId} idx={rampSpawnIndex} pos={spawnPos} rotY={spawnRot.eulerAngles.y} local={isLocal}");
                }
                else
                {
                    spawnPos = HoldingSpawnPosition;
                    spawnRot = Quaternion.identity;
                }
            }
            else
            {
                var circleOrder = new List<string>(_players.Keys);
                circleOrder.Add(userId);
                NeonSumoPlayerOrder.SortUserIdsHostFirst(circleOrder, GetEffectiveRoomHostUserId());
                int spawnSlot = circleOrder.IndexOf(userId);
                if (spawnSlot < 0) spawnSlot = circleOrder.Count - 1;
                spawnPos = arena.GetSpawnPosition(spawnSlot, _totalPlayerCount);
                DebugLogger.Log($"[NeonSumo] SpawnPlayer circle spawn user={userId} idx={spawnSlot} pos={spawnPos} local={isLocal}");
            }

            GameObject playerObj = Instantiate(playerPrefab, spawnPos, spawnRot);

            playerObj.name = $"Player_{userId}";
            NeonSumoPlayer player = GetOrAddNeonSumoPlayer(playerObj);

            player.SetBodyMaterials(playerBodyMaterials, playerEliminatedBodyMaterial);
            player.Initialize(userId, isLocal, this, _multiplayerClient, arena, ResolveControlKind(isLocal));
            _players[userId] = player;
            if (!string.IsNullOrEmpty(_roomHostUserId))
                SetRoomHostUserIdPreferringPlayerKey(_roomHostUserId);
            player.ApplyAuthority(IsMasterClient());

            ApplyColorAssignmentForPlayer(userId);

            if (hasRampSpawns && !isMaster)
            {
                if (!_spawnCoordinator.TryApplyPendingRampSpawn(userId))
                {
                    player.SetVisible(false);
                    player.FreezeForCountdown();
                    player.DisableControls();
                }
            }

            if (_flow.CurrentState != NeonSumoGameState.Playing)
            {
                player.FreezeForCountdown();
                player.DisableControls();
            }

            if (uiManager != null)
            {
                Color nameColor = player != null ? player.CurrentBodyColor : Color.white;
                uiManager.AddPlayer(userId, isLocal, nameColor);
            }
            else
            {
                DebugLogger.LogWarning("[NeonSumo] uiManager is NULL in SpawnPlayer()");
            }

            if (hasRampSpawns && isMaster && _multiplayerClient != null)
            {
                _spawnCoordinator.BroadcastRampSpawnForUser(userId, rampSpawnIndex);
            }

            _playerBarCoordinator?.AppendLatestPlayerToBar(userId);
            RegisterPlayerIndicatorForSpawnedUser(userId);
        }

        static NeonSumoPlayer GetOrAddNeonSumoPlayer(GameObject playerObj)
        {
            if (!playerObj.TryGetComponent<NeonSumoPlayer>(out var player))
                player = playerObj.AddComponent<NeonSumoPlayer>();
            return player;
        }

        static bool IsHumanPlayer(NeonSumoPlayer p) =>
            p != null && (p.ControlKind == NeonSumoControlKind.HumanLocal ||
                          p.ControlKind == NeonSumoControlKind.HumanRemote);

        /// <summary>
        /// Host-only: decides which clients get camera shake for a player–player collision.
        /// Strength is centralized here (not on <see cref="NeonSumoPlayer"/>).
        /// ActionSync does not echo competition events to the sender (<see cref="ActionSyncModule"/>), so the local human is shaken here; remotes receive <see cref="NeonSumoNetEvents.CameraShake"/>.
        /// </summary>
        public void NotifyPlayerPlayerCollisionCameraShake(NeonSumoPlayer a, NeonSumoPlayer b, float impulse)
        {
            if (!IsHumanPlayer(a) && !IsHumanPlayer(b))
                return;

            float strength = Mathf.Min(impulse * 0.02f, 0.5f);

            if (_multiplayerClient == null)
            {
                if (IsHumanPlayer(a) && a.IsLocal)
                    GetOrFindArenaCamera()?.Shake(strength);
                else if (IsHumanPlayer(b) && b.IsLocal)
                    GetOrFindArenaCamera()?.Shake(strength);
                return;
            }

            if (!IsMasterClient())
                return;

            void ConsiderHuman(NeonSumoPlayer p)
            {
                if (!IsHumanPlayer(p))
                    return;
                if (p.UserId == _localPlayerId)
                    GetOrFindArenaCamera()?.Shake(strength);
                else
                {
                    var payload = JsonConvert.SerializeObject(new JObject
                    {
                        ["target_user_id"] = p.UserId,
                        ["strength"] = strength
                    });
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.CameraShake, payload, Guid.NewGuid().ToString());
                }
            }

            ConsiderHuman(a);
            ConsiderHuman(b);
        }

        /// <summary>
        /// Spawns a local-only spark effect at the collision point.
        /// Intentionally not network synchronized.
        /// </summary>
        public void TrySpawnCollisionSparks(Vector3 position, Vector3 collisionNormal)
        {
            if (_collisionSparksPrototype == null)
            {
                if (collisionSparksTemplate == null)
                {
                    collisionSparksTemplate = FindCollisionSparksTemplateInScene();
                }

                _collisionSparksPrototype = collisionSparksTemplate;
            }

            if (_collisionSparksPrototype == null)
            {
                if (!_loggedMissingCollisionSparksTemplate)
                {
                    _loggedMissingCollisionSparksTemplate = true;
                    DebugLogger.LogWarning("[NeonSumo] Collision sparks not spawned: assign VFX_Sparks on NeonSumoGameManager or keep a scene ParticleSystem named 'VFX_Sparks'.");
                }
                return;
            }

            if (Time.time - _lastCollisionSparksTime < Mathf.Max(0.01f, collisionSparksCooldown))
            {
                return;
            }

            _lastCollisionSparksTime = Time.time;

            var forward = collisionNormal.sqrMagnitude > 0.0001f ? collisionNormal.normalized : Vector3.up;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);

            ParticleSystem spawned = BorrowCollisionSpark();
            spawned.transform.SetPositionAndRotation(position, rotation);
            spawned.gameObject.SetActive(true);
            spawned.Clear();
            spawned.Play(true);

            float lifetime = spawned.main.duration + spawned.main.startLifetime.constantMax + 0.25f;
            float delay = Mathf.Max(0.5f, lifetime);
            StartCoroutine(ReturnCollisionSparkToPoolAfter(spawned, delay));
        }

        ParticleSystem BorrowCollisionSpark()
        {
            if (_collisionSparksPool.Count > 0)
            {
                return _collisionSparksPool.Pop();
            }

            return Instantiate(_collisionSparksPrototype, _collisionSparksPoolRoot != null ? _collisionSparksPoolRoot : transform);
        }

        IEnumerator ReturnCollisionSparkToPoolAfter(ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ps == null)
            {
                yield break;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            if (_collisionSparksPoolRoot != null)
            {
                ps.transform.SetParent(_collisionSparksPoolRoot, false);
            }

            _collisionSparksPool.Push(ps);
        }

        private static ParticleSystem FindCollisionSparksTemplateInScene()
        {
            var allParticles = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allParticles.Length; i++)
            {
                var ps = allParticles[i];
                if (ps != null && string.Equals(ps.name, "VFX_Sparks", StringComparison.Ordinal))
                {
                    return ps;
                }
            }

            return null;
        }

        void ApplyColorAssignmentForPlayer(string userId)
        {
            if (string.IsNullOrEmpty(userId) || _playerColorManager == null) return;

            if (IsMasterClient())
            {
                var ordered = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());
                int slot = ordered.IndexOf(userId);
                if (slot < 0) slot = ordered.Count > 0 ? ordered.Count : 0;
                _playerColorManager.SetAssignment(userId, slot % PlayerColorManager.PaletteSize);
                _playerColorManager.ApplyAssignedColor(userId);
                _playerColorManager.BroadcastColorAssignment(userId);
            }
            else
            {
                if (_playerColorManager.HasAssignment(userId))
                {
                    _playerColorManager.ApplyAssignedColor(userId);
                }
            }
        }

        // FIX #1: Use ActionSync for elimination events, not NetworkSync
        public void EliminatePlayer(string userId, bool sendSync = true)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;
            if (!_players.ContainsKey(userId)) return;

            NeonSumoPlayer player = _players[userId];
            if (player != null && !player.IsEliminated)
            {
                player.SetEliminated();
                _playerBarCoordinator?.ApplyElimination(userId);

                if (sendSync)
                {
                    // Use ActionSync for elimination event
                    string eliminationId = Guid.NewGuid().ToString();
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.Eliminate, userId, eliminationId);
                }

                uiManager.PlayerEliminated(userId);

                CheckForWinner();
            }
        }

        // Master-only winner check to avoid double end-game triggers
        void CheckForWinner()
        {
            if (_flow.CurrentState != NeonSumoGameState.Playing) return;
            
            // Only master client determines winner to avoid race conditions
            if (_multiplayerClient != null && !IsMasterClient()) return;

            List<string> activePlayers = new List<string>();
            foreach (var kvp in _players)
            {
                if (!kvp.Value.IsEliminated)
                {
                    activePlayers.Add(kvp.Key);
                }
            }

            if (activePlayers.Count <= 1)
            {
                string winnerId = activePlayers.Count == 1 ? activePlayers[0] : null;
                HandleRoundWin(winnerId);
            }
        }

        #endregion

        #region Game Flow

        public void PlayerReady()
        {
            if (_flow.CurrentState != NeonSumoGameState.ReadyPhase && _flow.CurrentState != NeonSumoGameState.WaitingForPlayers)
            {
                DebugLogger.LogWarning($"[NeonSumo] Cannot mark ready: Current state is {_flow.CurrentState.ToString()}, expected ReadyPhase or WaitingForPlayers");
                return;
            }

            if (_localManualReadyClicked)
                return;

            _localManualReadyClicked = true;
            uiManager.SetReadyState(true);

            if (_multiplayerClient == null)
            {
                DebugLogger.Log("[NeonSumo] Offline: ready — advancing to intro / next match");
                AdvanceLocalMatchFromReady();
                return;
            }

            NeonSumoActionSyncSend.Competition(
                _multiplayerClient, NeonSumoNetEvents.PlayerManualReady, string.Empty, Guid.NewGuid().ToString());
            DebugLogger.Log($"[NeonSumo] Local player marked as ready (state: {_flow.CurrentState.ToString()})");

            if (NeonSumoUserId.TryNormalize(_localPlayerId, out var localId))
                _manualReadyPlayerIds.Add(localId);

            EvaluatePreGameReadyState();
        }

        /// <summary>Offline only: after Play Again reset, Ready must drive the same intro pipeline as multiplayer all-ready (no SDK).</summary>
        void AdvanceLocalMatchFromReady()
        {
            if (_multiplayerClient != null) return;

            _spawnCoordinator?.ResetSpawnAckGate();
            if (!_flow.TryTransition(NeonSumoGameState.Countdown))
            {
                DebugLogger.LogWarning($"[NeonSumo] AdvanceLocalMatchFromReady: TryTransition(Countdown) failed from {_flow.CurrentState.ToString()}");
                _flow.ForceTransition(NeonSumoGameState.Countdown);
            }

            TryBackfillExistingPlayers();
            StartIntroPhaseIfNeeded();
        }

        void HandleWaitForPlayer(string data)
        {
            _sdkAllPlayersReady = false;
            DebugLogger.Log($"[NeonSumo] WaitForPlayer event received at {Time.realtimeSinceStartup:F3}s - transitioning to ReadyPhase");
            _flow.TryTransition(NeonSumoGameState.ReadyPhase);
            // Don't show game UI here - keep main menu visible until game starts
            // Game UI will be shown in StartGame() when the game actually begins
            TryBackfillExistingPlayers();

            if (IsMasterClient() && useRampSpawns && arena != null && arena.HasRampSpawns() && _multiplayerClient != null && _spawnCoordinator != null && _spawnCoordinator.HasAssignmentsForPlayerCount(_players.Count))
            {
                DebugLogger.Log("[NeonSumo] Broadcasting ramp spawns for ReadyPhase");
                _spawnCoordinator.BroadcastRampSpawns(_players, _players.Count);
            }
        }

        void HandleAllPlayersReady(string data)
        {
            _sdkAllPlayersReady = true;
            DebugLogger.Log($"[NeonSumo] AllPlayersReady received (SDK presence gate) | state={_flow.CurrentState.ToString()}");
            TryBackfillExistingPlayers();
            EvaluatePreGameReadyState();
        }

        void HandlePlayerManualReady(string userId)
        {
            if (!IsMasterClient())
                return;
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;
            if (!_players.ContainsKey(userId))
            {
                DebugLogger.Log($"[NeonSumo] Ignoring player_manual_ready from {userId} — not on current host roster");
                return;
            }

            _manualReadyPlayerIds.Add(userId);
            DebugLogger.Log($"[NeonSumo] Manual ready vote from {userId} ({_manualReadyPlayerIds.Count}/{_players.Count})");
            EvaluatePreGameReadyState();
        }

        void EvaluatePreGameReadyState()
        {
            if (_multiplayerClient == null || !IsMasterClient())
                return;
            if (!_sdkAllPlayersReady)
                return;
            if (!IsPreGameLobbyState())
                return;
            if (_readyCountdownCoordinator != null && _readyCountdownCoordinator.IsRunning)
                return;
            if (_startCountdownRoutine != null)
                return;

            TryBackfillExistingPlayers();

            int minPlayers = GetConfiguredMinPlayers();
            if (_players.Count < minPlayers)
                return;

            foreach (var id in _players.Keys)
            {
                if (!_manualReadyPlayerIds.Contains(id))
                    return;
            }

            _spawnCoordinator?.ResetSpawnAckGate();
            _flow.TryTransition(NeonSumoGameState.Countdown);

            if (_players.Count >= _totalPlayerCount)
            {
                DebugLogger.Log("[NeonSumo] Full lobby and all players manually ready — starting immediately");
                BeginGameStartSequence();
                return;
            }

            DebugLogger.Log($"[NeonSumo] All connected players manually ready — starting {readyCountdownDuration}s countdown");
            _readyCountdownCoordinator?.BeginCountdown();
        }

        bool IsPreGameLobbyState()
        {
            var state = _flow.CurrentState;
            return state == NeonSumoGameState.WaitingForPlayers
                || state == NeonSumoGameState.ReadyPhase
                || state == NeonSumoGameState.Countdown;
        }

        void ResetLocalManualReady()
        {
            _localManualReadyClicked = false;
            uiManager?.ResetManualReadyUi();
        }

        void ResetPreGameRosterReadyState()
        {
            bool wasRunning = _readyCountdownCoordinator != null && _readyCountdownCoordinator.IsRunning;
            _readyCountdownCoordinator?.CancelCountdown();
            if (!wasRunning && _multiplayerClient != null && IsMasterClient())
            {
                NeonSumoActionSyncSend.Competition(
                    _multiplayerClient, NeonSumoNetEvents.CancelReadyPhase, string.Empty, Guid.NewGuid().ToString());
            }

            if (_startCountdownRoutine != null)
            {
                StopCoroutine(_startCountdownRoutine);
                _startCountdownRoutine = null;
            }

            _sdkAllPlayersReady = false;
            _manualReadyPlayerIds.Clear();
            ResetLocalManualReady();
            _flow.TryTransition(NeonSumoGameState.ReadyPhase);
        }

        /// <summary>Unified start pipeline: ramp spawns (if enabled) then BroadcastIntroPhase, or direct trigger.</summary>
        void BeginGameStartSequence()
        {
            if (_multiplayerClient == null || !IsMasterClient()) return;

            if (useRampSpawns && arena != null && arena.HasRampSpawns())
            {
                _spawnCoordinator.BroadcastRampSpawns(_players, _players.Count);
                _spawnCoordinator.BeginSpawnAckWait(_players.Count);
            }
            else
            {
                BroadcastIntroPhase();
            }
        }

        void HandleCountdownToStart(string data)
        {
            // Decouple backfill from manual ready: ensure _players matches room before countdown/game.
            TryBackfillExistingPlayers();

            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                return;
            }

            DebugLogger.Log($"[NeonSumo] CountdownToStart received at {Time.realtimeSinceStartup:F3}s");
            EnsureCountdownPhase();
            if (startCountdownSeconds <= 0)
            {
                StartGame();
                return;
            }

            if (_startCountdownRoutine == null)
            {
                _startCountdownRoutine = StartCoroutine(RunStartCountdown());
            }
        }

        IEnumerator RunStartCountdown()
        {
            // Reset camera to default arena view before Get Ready phase
            GetOrFindArenaCamera()?.ResetToInitialView();

            // Peel TMP / visible intro UI off the same frame as StartCoroutine (sync prefix)
            yield return null;

            // Phase 1: Get Ready (before 3, 2, 1, Go)
            uiManager.ShowGetReady();
            uiManager.PlayGetReadyAudio();
            yield return new WaitForSeconds(uiManager.GetReadyClipLength);

            // Phase 2: 3, 2, 1, Go
            int remaining = Mathf.Max(0, startCountdownSeconds);
            while (remaining >= 0)
            {
                uiManager.ShowCountdown(remaining);
                uiManager.PlayCountdownAudio(remaining);

                if (remaining == 0)
                {
                    yield return new WaitForSeconds(1f);  // Let "GO!" be visible
                    StartGame();
                    break;
                }

                yield return new WaitForSeconds(1f);
                remaining--;
            }

            _startCountdownRoutine = null;
        }

        void StartGame()
        {
            if (!_flow.TryTransition(NeonSumoGameState.Playing))
            {
                DebugLogger.LogWarning($"[NeonSumo] StartGame: TryTransition(Playing) failed from {_flow.CurrentState.ToString()}; forcing");
                _flow.ForceTransition(NeonSumoGameState.Playing);
            }
            _isGameActive = true;
            _hostInputSync?.ResetTimers();
            _cpuBrain?.ClearBoostIntentHistory();
            _playerBarCoordinator?.ResetForRound();

            uiManager.ShowGameUI();
            uiManager.ShowEndCountdown(60);  // Set initial timer to 1:00
            uiManager.UpdateWinsCounter(_localWins);
            SetupPlayerIndicators();
            DebugLogger.Log($"[NeonSumo] Game started at {Time.realtimeSinceStartup:F3}s");

            if (useRampSpawns && arena != null && arena.HasRampSpawns())
            {
                if (!_rampSpawnPrepared)
                {
                    BeginRampSpawnPhase();
                }
                BeginRampRollForAll();
                ScheduleRampRetract();
            }
            else
            {
                _rampSpawnPrepared = false;
                BeginRampRollForAll();
                ScheduleRampRetract();
                SetGameplayInputEnabled(true);
            }

            if (enableArenaShrink && arena != null)
            {
                _ringShrinkHandler?.Reset();
                // Time-based ring events driven by HandleCountdownToEnd (no BeginShrinkWithWarnings)
            }
        }

        void BeginRampSpawnPhase()
        {
            SetGameplayInputEnabled(false);
            bool isMaster = IsMasterClient();
            if (isMaster)
            {
                var userIds = new List<string>(_players.Keys);
                NeonSumoPlayerOrder.SortUserIdsHostFirst(userIds, GetEffectiveRoomHostUserId());
                _spawnCoordinator.AssignRampSpawnIndices(userIds);
                ApplyRampSpawnsLocally();

                if (_multiplayerClient != null)
                {
                    _spawnCoordinator.BroadcastRampSpawns(_players, _players.Count);
                }

                _rampSpawnPrepared = true;
            }
            else
            {
                _rampSpawnPrepared = false;
            }
        }

        void ApplyRampSpawnsLocally()
        {
            foreach (var kvp in _players)
            {
                if (kvp.Value == null) continue;
                if (!_spawnCoordinator.TryGetSpawnIndex(kvp.Key, out int spawnIndex))
                {
                    DebugLogger.LogWarning($"[NeonSumo] ApplyRampSpawnsLocally: no spawn index for user {kvp.Key}");
                    continue;
                }
                ApplyRampSpawnToPlayer(kvp.Key, kvp.Value, spawnIndex, setControlsLocked: true);
            }
        }

        void ApplyRampSpawnToPlayer(string userId, NeonSumoPlayer player, int spawnIndex, bool setControlsLocked)
        {
            if (arena == null || player == null) return;

            Vector3 spawnPos = arena.GetRampSpawnPosition(spawnIndex);
            Quaternion spawnRot = arena.GetRampSpawnRotation(spawnIndex);
            player.MoveToSpawn(spawnPos, spawnRot);
            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                player.BeginRampRoll();
            }
            else
            {
                player.FreezeForCountdown();
            }

            if (setControlsLocked)
            {
                player.DisableControls();
            }
        }

        void ApplyRampSpawnFromNetwork(string userId, Vector3 position, Quaternion rotation, int spawnIndex)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;
            if (!_players.TryGetValue(userId, out var player) || player == null)
            {
                return;
            }

            player.MoveToSpawn(position, rotation);
            player.SetVisible(true);
            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                player.BeginRampRoll();
            }
            else
            {
                player.FreezeForCountdown();
                player.DisableControls();
            }
            _spawnCoordinator.RecordSpawnAssignment(userId, spawnIndex);
            _rampSpawnPrepared = true;
            DebugLogger.Log($"[NeonSumo] ApplyRampSpawnFromNetwork user={userId} idx={spawnIndex} pos={position} rotY={rotation.eulerAngles.y} local={IsLocalPlayer(userId)}");
            _spawnCoordinator.TrySendLocalSpawnAck(userId);
        }

        /// <summary>Broadcasts start_intro_phase to all clients and starts 3-2-1-Go locally. Host only. Sends countdown_to_start to backend now so backend warm-up overlaps with intro.</summary>
        void BroadcastIntroPhase()
        {
            if (_multiplayerClient == null || !IsMasterClient()) return;

            NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.StartIntroPhase, string.Empty, Guid.NewGuid().ToString());
            _multiplayerClient.Game.TriggerGameStart();
            DebugLogger.Log("[NeonSumo] Broadcast start_intro_phase — starting 3-2-1-Go locally, backend timer started");
            StartIntroPhaseIfNeeded();
        }

        /// <summary>Starts the 3-2-1-Go countdown if not already running. Called by host (after broadcast) or by clients (when receiving start_intro_phase).</summary>
        void StartIntroPhaseIfNeeded()
        {
            TryBackfillExistingPlayers();
            if (_flow.CurrentState == NeonSumoGameState.Playing) return;
            EnsureCountdownPhase();
            if (startCountdownSeconds <= 0)
            {
                StartGame();
                return;
            }
            if (_startCountdownRoutine == null)
            {
                _startCountdownRoutine = StartCoroutine(RunStartCountdown());
            }
        }

        /// <summary>
        /// Host enters Countdown from EvaluatePreGameReadyState. Clients stay in ReadyPhase until
        /// start_intro_phase / OnCountdownToStart. Playing is only valid from Countdown.
        /// </summary>
        void EnsureCountdownPhase()
        {
            if (_flow.CurrentState == NeonSumoGameState.Countdown || _flow.CurrentState == NeonSumoGameState.Playing)
                return;
            if (!_flow.TryTransition(NeonSumoGameState.Countdown))
            {
                DebugLogger.LogWarning($"[NeonSumo] EnsureCountdownPhase: TryTransition(Countdown) failed from {_flow.CurrentState.ToString()}; forcing");
                _flow.ForceTransition(NeonSumoGameState.Countdown);
            }
        }

        public void OnRampUnlocked(NeonSumoPlayer player)
        {
            if (!useRampUnlockTrigger)
            {
                return;
            }

            if (!_rampSpawnPrepared && _flow.CurrentState != NeonSumoGameState.Playing)
            {
                return;
            }

            if (player != null)
            {
                player.OnRampReached();
                if (IsMasterClient())
                {
                    TriggerRampRetract(sendNetwork: true);
                }
            }
        }

        void UnlockControlsForAll(bool sendNetwork)
        {
            SetGameplayInputEnabled(true);
            _rampSpawnPrepared = false;

            foreach (var player in _players.Values)
            {
                if (player == null) continue;
                player.EnableControls();
            }

            if (sendNetwork && _multiplayerClient != null && IsMasterClient())
            {
                var payload = new JObject { ["all"] = true };
                string actionMsg = JsonConvert.SerializeObject(payload);
                NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.UnlockControls, actionMsg, Guid.NewGuid().ToString());
            }
        }

        bool IsMasterClient()
        {
            if (_multiplayerClient == null) return true;
            if (_isMasterResolved) return _isMasterUser;
            return _multiplayerClient.IsMasterUser();
        }

        void BeginRampRollForAll()
        {
            foreach (var player in _players.Values)
            {
                if (player == null) continue;
                player.BeginRampRoll();
                DebugLogger.Log($"[Ramp] Countdown end -> physics enabled for {player.UserId}");
            }
        }

        void HandleCountdownToEnd(string data)
        {
            var s = _flow.CurrentState;
            if (s == NeonSumoGameState.GameEnd || s == NeonSumoGameState.RoundResults || s == NeonSumoGameState.RoundEnding ||
                s == NeonSumoGameState.Restarting || !_isGameActive || _roundEndTriggered)
            {
                return;
            }

            var json = ParseJsonObject(data);
            int seconds = json?["second"]?.Value<int>() ?? json?["seconds"]?.Value<int>() ?? 5;
            int displaySeconds = Mathf.Min(seconds, 60);  // Clamp to prevent 1:01 blip on first tick
            DebugLogger.Log($"[NeonSumo] OnCountdownToEnd: {seconds}");
            uiManager.ShowEndCountdown(displaySeconds);

            if (enableArenaShrink && arena != null)
            {
                _ringShrinkHandler?.Tick(seconds);
            }
        }

        // FIX #5: Remove local timer, rely on GameModule.OnGameTimeUp only
        void HandleGameTimeUp()
        {
            _isGameActive = false;
            DebugLogger.Log("[NeonSumo] Game time up (from GameModule)");
            
            // Determine winner by last standing
            CheckForWinner();
        }

        void HandleRoundWin(string winnerId)
        {
            if (_roundEndTriggered) return;
            _isGameActive = false;

            _spawnCoordinator?.ResetSpawnAckGate();

            if (_multiplayerClient != null && IsMasterClient())
            {
                var payload = new JObject
                {
                    ["winner_user_id"] = winnerId,
                    ["round"] = _localWins + 1,
                    ["send_unix_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                string actionMsg = JsonConvert.SerializeObject(payload);
                NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.RoundWin, actionMsg, Guid.NewGuid().ToString());
            }

            ApplyRoundWin(winnerId);
        }

        void ApplyRoundWin(string winnerId)
        {
            if (_roundEndTriggered || _flow.CurrentState == NeonSumoGameState.RoundResults)
            {
                return;
            }

            _roundEndTriggered = true;
            _isGameActive = false;

            if (!_flow.TryTransition(NeonSumoGameState.RoundEnding))
            {
                DebugLogger.LogWarning($"[NeonSumo] ApplyRoundWin: TryTransition(RoundEnding) failed from {_flow.CurrentState.ToString()}; forcing.");
                _flow.ForceTransition(NeonSumoGameState.RoundEnding);
            }

            SetGameplayInputEnabled(false);
            arena?.StopShrink();
            _ringShrinkHandler?.Reset();
            uiManager?.ShowEndCountdown(0);

            foreach (var player in _players.Values)
            {
                if (player == null) continue;
                player.DisableControls();
                player.FreezeForCountdown();
            }

            ApplyWinnerAndShowUI(winnerId);
            DebugLogger.Log($"[NeonSumo] Round win applied. Winner: {GetWinnerDisplayName(winnerId)}");
            _playerBarCoordinator?.ApplyWinnerPlacement(winnerId);

            if (!_flow.TryTransition(NeonSumoGameState.RoundResults))
            {
                DebugLogger.LogWarning($"[NeonSumo] ApplyRoundWin: TryTransition(RoundResults) failed from {_flow.CurrentState.ToString()}; forcing.");
                _flow.ForceTransition(NeonSumoGameState.RoundResults);
            }
        }

        void EndGame(string winnerId)
        {
            _isGameActive = false;
            _flow.TryTransition(NeonSumoGameState.GameEnd);

            SetGameplayInputEnabled(false);
            _rampSpawnPrepared = false;
            arena?.StopShrink();
            uiManager?.ShowEndCountdown(0);

            ApplyWinnerAndShowUI(winnerId);
            DebugLogger.Log($"[NeonSumo] Game ended. Winner: {GetWinnerDisplayName(winnerId)}");
        }

        /// <summary>Shared winner logic: update local wins, leaderboard, and show winner UI.</summary>
        void ApplyWinnerAndShowUI(string winnerId)
        {
            winnerId = NeonSumoUserId.Normalize(winnerId);
            if (winnerId == _localPlayerId)
            {
                _localWins++;
                _multiplayerClient?.Leaderboard.LeaderboardUpdate(_localWins);
            }

            string winnerName = GetWinnerDisplayName(winnerId);
            uiManager?.ShowWinner(winnerName, _localWins, winnerId);
        }

        string GetWinnerDisplayName(string winnerId)
        {
            if (winnerId == null || !_players.ContainsKey(winnerId))
                return "No Winner";

            return _displayNames.ResolveWinnerHeadline(winnerId);
        }

        void HandleGameEnd()
        {
            EndGame(null);
        }

        public void RequestRestart()
        {
            if (!IsLocalLobbyRestartAuthority())
                return;

            if (_multiplayerClient != null)
            {
                if (!_flow.TryTransition(NeonSumoGameState.Restarting))
                {
                    DebugLogger.LogWarning($"[NeonSumo] RequestRestart: TryTransition(Restarting) failed from {_flow.CurrentState.ToString()}; still sending restart to backend");
                }
                _multiplayerClient.Game.TriggerGameRestart();
                return;
            }

            if (!_flow.TryTransition(NeonSumoGameState.Restarting))
            {
                DebugLogger.LogWarning($"[NeonSumo] RequestRestart: TryTransition(Restarting) failed from {_flow.CurrentState.ToString()}");
                return;
            }

            DebugLogger.Log("[NeonSumo] Offline: Play Again — applying world reset locally");
            PerformRestart();
        }

        void HandleGameRestart()
        {
            DebugLogger.Log("[NeonSumo] Game restarting");
            DebugLogger.Log($"[NeonSumo] Restart identity: localPlayerId={_localPlayerId} peerId={_multiplayerClient?.PeerId} isMaster={IsMasterClient()}");
            PerformRestart();
        }

        void PerformRestart()
        {
            CancelScheduledRampRetract();

            _hostInputSync?.ResetTimers();
            _cpuBrain?.ClearBoostIntentHistory();

            _spawnCoordinator?.ResetSpawnAckGate();
            indicatorManager?.ClearAll();

            _displayNames.Clear();
            RestoreSessionLocalDisplayNameAfterRestart();

            if (useRampSpawnsOnRestart && arena != null && arena.HasRampSpawns())
            {
                foreach (var kvp in _players)
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.ResetForNewRound();
                    }
                }
                BeginRampSpawnPhase();
            }
            else
            {
                _rampSpawnPrepared = false;
                int index = 0;
                foreach (var kvp in _players)
                {
                    if (kvp.Value != null)
                    {
                        Vector3 newSpawn = arena.GetSpawnPosition(index, _totalPlayerCount);
                        kvp.Value.Respawn(newSpawn);
                        index++;
                    }
                }
            }

            _flow.TryTransition(NeonSumoGameState.ReadyPhase);
            ResetRoundStateFlags();
            uiManager.ResetUI();
            uiManager.ShowMainMenu(NeonSumoUIManager.MainMenuMode.AfterGameRestartSync);
            uiManager.SetReadyState(false);
            RebuildPlayerListUI();
            uiManager.UpdateWinsCounter(_localWins);
            _playerBarCoordinator?.ResetForRound();

            BroadcastLocalDisplayNameAnnouncement();

            if (arena != null)
            {
                arena.ResetArena();
                _ringShrinkHandler?.Reset();
            }

            if (ramps != null)
            {
                ramps.ResetRamps();
            }

            ResetRampUnlockTriggersForNewRound();

            DebugLogger.Log("[NeonSumo] Game reset, waiting for ready");
            OnWorldResetAfterRestart?.Invoke();
        }

        void HandleGameError(string data)
        {
            var json = ParseJsonObject(data);
            string errorType = json?["error_type"]?.ToString() ?? "Unknown";
            string errorMsg = json?["error_msg"]?.ToString() ?? "Unknown error";
            
            DebugLogger.LogError($"[NeonSumo] Game Error [{errorType}]: {errorMsg}");
            uiManager.ShowError($"{errorType}: {errorMsg}");
        }

        void EnsureMasterSyncHandler()
        {
            if (_masterSyncHandler != null) return;
            var ctx = new MasterSyncContext
            {
                MultiplayerClient = _multiplayerClient,
                LocalPlayerId = _localPlayerId,
                Arena = arena,
                OnCanonicalRoomHostUserId = OnCanonicalRoomHostFromNetwork,
                OnMasterResolved = isMaster =>
                {
                    _isMasterUser = isMaster;
                    _isMasterResolved = true;
                    uiManager?.RefreshLobbyHostDependentControls();
                    if (isMaster)
                        EvaluatePreGameReadyState();
                },
                TryBackfill = TryBackfillExistingPlayers,
                ApplyAuthorityToAll = isMaster =>
                {
                    foreach (var player in _players.Values)
                    {
                        if (player == null) continue;
                        player.ApplyAuthority(isMaster);
                    }
                },
                SyncColorsAndBroadcast = () =>
                {
                    RemapMasterColorsByHostFirstOrder();
                    _playerBarCoordinator?.RebuildRoster();
                },
                BeginRampSpawnPhaseIfNeeded = () =>
                {
                    if (useRampSpawns && arena != null && arena.HasRampSpawns() && _flow.CurrentState != NeonSumoGameState.Playing)
                        BeginRampSpawnPhase();
                },
            };
            _masterSyncHandler = new MasterSyncHandler(ctx);
        }

        void EnsureRampsController()
        {
            if (ramps != null) return;
            DebugLogger.LogWarning("[NeonSumo] Ramps controller not assigned. Please wire it in the inspector.");
        }

        void EnsurePlayerColorManager()
        {
            if (_playerColorManager != null) return;
            var ctx = new PlayerColorContext(
                GetPlayer,
                (userId, color) => uiManager?.SetPlayerColor(userId, color),
                SetupPlayerIndicators,
                (actionName, payload) =>
                {
                    if (_multiplayerClient == null) return;
                    string actionMsg = JsonConvert.SerializeObject(payload);
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, actionName, actionMsg, Guid.NewGuid().ToString());
                });
            _playerColorManager = new PlayerColorManager(ctx);
        }

        void EnsureSpawnCoordinator()
        {
            if (_spawnCoordinator != null) return;
            var ctx = new SpawnCoordinatorContext
            {
                Arena = arena,
                MultiplayerClient = _multiplayerClient,
                GetOrAssignPlayerColorIndex = GetHostFirstColorIndexForSpawnSync,
                ApplyRampSpawnFromNetwork = ApplyRampSpawnFromNetwork,
                IsLocalPlayer = userId => IsLocalPlayer(userId),
                IsMasterClient = IsMasterClient,
                StartCoroutine = r => StartCoroutine(r),
                StopCoroutine = c => StopCoroutine(c),
                OnAllSpawnAcksReceived = () =>
                {
                    if (_multiplayerClient != null && IsMasterClient())
                    {
                        DebugLogger.Log("[NeonSumo] Master triggering intro phase (spawn ACK gate)");
                        BroadcastIntroPhase();
                    }
                },
            };
            _spawnCoordinator = new SpawnCoordinator(ctx);
        }

        void EnsureRingShrinkHandler()
        {
            if (_ringShrinkHandler != null) return;
            var ctx = new RingShrinkContext
            {
                PlayWarningLine = (idx, dur, cw) => arena?.PlayWarningLine(idx, dur, cw),
                PlayWarningAlarm = () => arena?.PlayWarningAlarm(),
                TriggerRingDrop = idx => arena?.TriggerRingDrop(idx),
                OnWarningLineStarted = idx => GetOrFindArenaCamera()?.NotifyWarningRingStarted(idx),
                IsMasterClient = IsMasterClient,
            };
            _ringShrinkHandler = new RingShrinkHandler(ctx);
        }

        void EnsureHostInputSync()
        {
            EnsureCpuBrain();
            if (_hostInputSync != null) return;
            var ctx = new HostInputSyncContext(
                _localPlayerId,
                _multiplayerClient,
                () => _players,
                () => GetPlayer(_localPlayerId))
            {
                GetCpuInput = ResolveCpuInputForHost,
            };
            _hostInputSync = new HostInputSync(ctx);
        }

        void EnsureCpuBrain()
        {
            if (_cpuBrain != null) return;
            _cpuBrain = new NeonSumoCpuSumoBrain(EnumeratePlayersForCpuBrain);
        }

        IEnumerable<NeonSumoPlayer> EnumeratePlayersForCpuBrain()
        {
            foreach (var p in _players.Values)
            {
                if (p != null)
                {
                    yield return p;
                }
            }
        }

        (Vector2 move, bool boost) ResolveCpuInputForHost(NeonSumoPlayer player)
        {
            if (player == null || player.ControlKind != NeonSumoControlKind.Cpu || _cpuBrain == null)
            {
                return (Vector2.zero, false);
            }

            return _cpuBrain.Compute(player);
        }

        NeonSumoControlKind ResolveControlKind(bool isLocal)
        {
            if (_multiplayerClient == null)
            {
                return isLocal ? NeonSumoControlKind.HumanLocal : NeonSumoControlKind.Cpu;
            }

            return isLocal ? NeonSumoControlKind.HumanLocal : NeonSumoControlKind.HumanRemote;
        }

        void TriggerRampRetract(bool sendNetwork)
        {
            if (_rampsRetracted)
            {
                return;
            }

            EnsureRampsController();
            if (ramps == null)
            {
                return;
            }

            Vector3 arenaCenter = arena != null ? arena.transform.position : Vector3.zero;
            ramps.RetractRamps(arenaCenter);
            _rampsRetracted = true;

            if (sendNetwork && _multiplayerClient != null && IsMasterClient())
            {
                var payload = new JObject
                {
                    ["send_realtime"] = Time.realtimeSinceStartup,
                    ["send_unix_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                string actionMsg = JsonConvert.SerializeObject(payload);
                Debug.Log($"[RampTiming][HostSend] realtime={Time.realtimeSinceStartup:F3}s wall={DateTime.UtcNow:HH:mm:ss.fff} action=ramps_retract payload={actionMsg}");
                NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.RampsRetract, actionMsg, Guid.NewGuid().ToString());
            }
        }

        void ScheduleRampRetract()
        {
            if (ramps == null)
            {
                return;
            }

            CancelScheduledRampRetract();
            _rampRetractRoutine = StartCoroutine(RampRetractAfterDelay());
        }

        void CancelScheduledRampRetract()
        {
            if (_rampRetractRoutine == null)
            {
                return;
            }

            StopCoroutine(_rampRetractRoutine);
            _rampRetractRoutine = null;
        }

        void ResetRampUnlockTriggersForNewRound()
        {
            if (!useRampUnlockTrigger)
            {
                return;
            }

            var triggers = FindObjectsByType<NeonSumoRampUnlockTrigger>(FindObjectsSortMode.None);
            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] != null)
                {
                    triggers[i].ResetForNewRound();
                }
            }
        }

        IEnumerator RampRetractAfterDelay()
        {
            float delay = Mathf.Max(0f, rampRetractDelaySeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (_flow.CurrentState != NeonSumoGameState.Playing)
            {
                _rampRetractRoutine = null;
                yield break;
            }

            if (_multiplayerClient == null || IsMasterClient())
            {
                TriggerRampRetract(sendNetwork: true);
            }

            _rampRetractRoutine = null;
        }

        #endregion

        #region Network Sync

        void HandleRemotePositionUpdate(string data)
        {
            try
            {
                var dataDict = ParseJsonObjectDictionary(data);
                if (dataDict == null) return;

                string entityId = dataDict.ContainsKey("entity_id") ? dataDict["entity_id"]?.ToString() : null;
                string userId = dataDict.ContainsKey("user_id") ? dataDict["user_id"]?.ToString() : null;
                string playerId = !string.IsNullOrEmpty(entityId) ? entityId : userId;
                if (!NeonSumoUserId.TryNormalize(playerId, out playerId)) return;

                if (_players.TryGetValue(playerId, out var player) && player != null)
                {
                    var posData = dataDict.ContainsKey("data") ? dataDict["data"] : null;
                    if (posData != null)
                        player.UpdateRemotePosition(posData);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"[NeonSumo] Error handling remote position: {ex.Message}");
            }
        }

        void HandleLeaderboardUpdate(string data)
        {
            var dataDict = ParseJsonObjectDictionary(data);
            if (dataDict == null) return;

            string userId = dataDict.ContainsKey("user_id") ? dataDict["user_id"]?.ToString() : null;
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;

            int score = 0;
            if (dataDict.TryGetValue("score", out var scoreObj) && scoreObj != null)
            {
                try { score = Convert.ToInt32(scoreObj); }
                catch { return; }
            }

            _peerScores[userId] = score;
            if (userId != _localPlayerId)
                return;

            _localWins = score;
            uiManager?.UpdateWinsCounter(_localWins);
        }

        void HandleRemoteRemove(string data)
        {
            // This is for entity removal, not player elimination
            // Elimination is handled via ActionSync now
            DebugLogger.Log("[NeonSumo] Remote remove event (currently unused for players)");
        }

        void RebuildPlayerListUI()
        {
            if (uiManager == null || _players.Count == 0) return;
            var userIds = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());
            foreach (var userId in userIds)
            {
                if (!_players.TryGetValue(userId, out var player) || player == null) continue;
                bool isLocal = userId == _localPlayerId;
                Color nameColor = player.CurrentBodyColor;
                uiManager.AddPlayer(userId, isLocal, nameColor);
            }
        }

        /// <summary>
        /// Adds a single world-space indicator for <paramref name="userId"/> without clearing existing entries.
        /// Used from <see cref="SpawnPlayer"/> so each spawn does not rebuild all indicators.
        /// </summary>
        void RegisterPlayerIndicatorForSpawnedUser(string userId)
        {
            EnsureIndicatorManager();
            if (indicatorManager == null || string.IsNullOrEmpty(userId))
                return;

            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                indicatorManager.ClearAll();
                return;
            }

            if (!_players.TryGetValue(userId, out var player) || player == null)
                return;

            var userIds = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());
            int orderIndex = userIds.IndexOf(userId);
            if (orderIndex < 0)
                return;

            int displayIndex = orderIndex + 1;
            if (displayIndex > 4)
                return;

            bool isLocal = userId == _localPlayerId;
            indicatorManager.RegisterPlayer(player.transform, displayIndex, player.CurrentBodyColor, isLocal);
        }

        void SetupPlayerIndicators()
        {
            EnsureIndicatorManager();
            if (indicatorManager == null)
            {
                return;
            }

            if (_flow.CurrentState == NeonSumoGameState.Playing)
            {
                indicatorManager.ClearAll();
                return;
            }

            indicatorManager.ClearAll();

            var userIds = NeonSumoPlayerOrder.OrderedUserIds(_players.Keys, GetEffectiveRoomHostUserId());

            int index = 1;
            foreach (var userId in userIds)
            {
                if (!_players.TryGetValue(userId, out var player) || player == null)
                {
                    continue;
                }

                bool isLocal = userId == _localPlayerId;
                indicatorManager.RegisterPlayer(player.transform, index, player.CurrentBodyColor, isLocal);
                index++;
                if (index > 4)
                {
                    break;
                }
            }
        }

        void EnsureIndicatorManager()
        {
            if (indicatorManager == null)
                DebugLogger.LogError("[NeonSumo] NeonSumoGameManager: PlayerIndicatorManager is not assigned. Assign in Inspector or via Bootstrap.");
        }

        void EnsurePlayerBarCoordinator()
        {
            if (_playerBarCoordinator != null) return;
            var ctx = new PlayerBarCoordinatorContext(
                playerBar,
                () => _players,
                _localPlayerId,
                userId =>
                {
                    if (_playerColorManager == null) return (false, Color.white);
                    return _playerColorManager.TryGetColor(userId, out var c) ? (true, c) : (false, Color.white);
                },
                GetEffectiveRoomHostUserId,
                userId => _displayNames.ResolvePlayerBarLabel(userId, _localPlayerId));
            _playerBarCoordinator = new PlayerBarCoordinator(ctx);
        }

        void ApplyLocalMultiplayerDisplayNameHandshake()
        {
            if (_multiplayerClient == null)
                return;

            SeedLocalDisplayNameFromPrefs(announceViaActionSync: true);
        }

        /// <summary>Loads lobby/VIVERSE-backed labels into <see cref="_displayNames"/> — works offline (<paramref name="announceViaActionSync"/> false) too.</summary>
        void SeedLocalDisplayNameFromPrefs(bool announceViaActionSync)
        {
            if (string.IsNullOrEmpty(_localPlayerId))
                return;

            MatchmakingHandoffStore.TryConsumeLocalDisplayNameSeed(out string prefSeed);
            bool consumedLobbySeed = !string.IsNullOrEmpty(prefSeed);

            bool hasPlatformDisplay = NeonSumoPlatformProfiles.TryGetProfileDisplayLabel(out string platformDisplay) &&
                                       !string.IsNullOrEmpty(platformDisplay);

            string chosenRaw = hasPlatformDisplay ? platformDisplay : prefSeed;
            if (!hasPlatformDisplay && LooksLikeGeneratedLobbyActorName(prefSeed))
                chosenRaw = null;

            int chosenLenBeforeSanitize = string.IsNullOrEmpty(chosenRaw) ? 0 : chosenRaw.Trim().Length;

            // #region agent log
            NeonSumoDebugNdjson.Log(
                hypothesisId: "H4-fix",
                location: "NeonSumoGameManager.SeedLocalDisplayNameFromPrefs",
                message: announceViaActionSync ? "seed_online_path" : "seed_offline_path",
                data: new
                {
                    announceViaActionSync,
                    multiplayerAttached = _multiplayerClient != null,
                    consumedLobbySeed,
                    prefersPlatformProfile = hasPlatformDisplay,
                    chosenRawCharsApprox = chosenLenBeforeSanitize,
                });
            // #endregion

            if (hasPlatformDisplay)
            {
                DebugLogger.Log($"[NeonSumo][DisplayNames] Local seed source=platform{(consumedLobbySeed ? "; lobby prefs consumed" : string.Empty)}");
            }
            else if (!string.IsNullOrEmpty(chosenRaw))
            {
                DebugLogger.Log("[NeonSumo][DisplayNames] Local seed source=lobby prefs handoff");
            }

            if (string.IsNullOrEmpty(chosenRaw))
            {
                DebugLogger.Log($"[NeonSumo][DisplayNames] No local seed; UI will use PeerId prefixes until remote announcements");
            }
            else if (_displayNames.TrySetRaw(_localPlayerId, chosenRaw, out string sanitizedLocal))
            {
                _sessionLocalSanitizedDisplayName = sanitizedLocal;
                DebugLogger.Log($"[NeonSumo][DisplayNames] Registered local display: {sanitizedLocal}");
            }

            if (announceViaActionSync && _multiplayerClient?.ActionSync != null)
                BroadcastLocalDisplayNameAnnouncement();

            RefreshDisplayNameDependentUi();
        }

        void RestoreSessionLocalDisplayNameAfterRestart()
        {
            if (_multiplayerClient == null || string.IsNullOrEmpty(_sessionLocalSanitizedDisplayName))
                return;

            if (_displayNames.TrySetRaw(_localPlayerId, _sessionLocalSanitizedDisplayName, out _))
                DebugLogger.Log("[NeonSumo][DisplayNames] Restored local display after restart");
        }

        void BroadcastLocalDisplayNameAnnouncement()
        {
            if (_multiplayerClient?.ActionSync == null)
                return;

            if (!_displayNames.TryGet(_localPlayerId, out string name))
                return;

            string payload = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["display_name"] = name,
            });

            NeonSumoActionSyncSend.Competition(_multiplayerClient, 
                NeonSumoNetEvents.PlayerDisplayName,
                payload,
                Guid.NewGuid().ToString());
            DebugLogger.Log($"[NeonSumo][DisplayNames] Sent player_display_name ActionSync ({name.Length} chars)");
        }

        void OnRemotePeerDisplayNameReceived(string senderUserId, string rawIncoming)
        {
            if (string.IsNullOrEmpty(senderUserId))
                return;

            if (!_displayNames.TrySetRaw(senderUserId, rawIncoming, out string sanitized))
            {
                DebugLogger.Log($"[NeonSumo][DisplayNames] Ignored invalid remote display from peer starting {TruncateId(senderUserId)}");
                return;
            }

            DebugLogger.Log($"[NeonSumo][DisplayNames] Remote display Peer={TruncateId(senderUserId)} → {sanitized}");
            RefreshDisplayNameDependentUi();
        }

        static string TruncateId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return "∅";

            return userId.Length <= 8 ? userId : userId.Substring(0, 8) + "…";
        }

        static bool LooksLikeGeneratedLobbyActorName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length != 10)
                return false;
            if (!name.StartsWith("Player", System.StringComparison.Ordinal))
                return false;
            for (int i = 6; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                    return false;
            }
            return true;
        }

        void RefreshDisplayNameDependentUi()
        {
            uiManager?.RefreshPlayerDisplayNamesFromRegistry();
            _playerBarCoordinator?.RebuildRoster();
        }

        #endregion

        #region Public Getters

        public bool IsGameActive => _isGameActive;
        public bool IsInitialized => _isInitialized;
        public NeonSumoGameState CurrentState => _flow.CurrentState;
        public NeonSumoPlayer GetPlayer(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return null;
            _players.TryGetValue(userId, out var player);
            return player;
        }
        public IEnumerable<NeonSumoPlayer> GetAlivePlayers()
        {
            foreach (var kvp in _players)
                if (kvp.Value != null && !kvp.Value.IsEliminated)
                    yield return kvp.Value;
        }
        public bool IsLocalPlayer(string userId) =>
            NeonSumoUserId.Normalize(userId) == _localPlayerId;
        public bool IsHostClient => IsMasterClient();

        public string ResolvePlayerListNicknameSegmentForUi(string userId)
        {
            return _displayNames.ResolvePlayerListNicknameSegment(userId, _localPlayerId);
        }

        /// <summary>
        /// True when the match is playing and no alive player is still in ramp roll-in.
        /// Used by <see cref="NeonSumoArenaCamera"/> to avoid framing a spread-out ramp centroid.
        /// </summary>
        public bool IsArenaCameraDynamicFramingReady()
        {
            if (_flow.CurrentState != NeonSumoGameState.Playing)
            {
                return false;
            }

            bool hasAnyAlive = false;
            foreach (var player in GetAlivePlayers())
            {
                if (player == null)
                {
                    continue;
                }

                hasAnyAlive = true;
                if (player.IsRollingIn)
                {
                    return false;
                }
            }

            return hasAnyAlive;
        }

        #endregion

        public void SetGameplayInputEnabled(bool enabled)
        {
            foreach (var player in _players.Values)
            {
                if (player == null) continue;
                if (enabled)
                {
                    player.EnableControls();
                }
                else
                {
                    player.DisableControls();
                }
            }
        }

        NeonSumoArenaCamera GetOrFindArenaCamera()
        {
            if (_cachedArenaCamera == null)
                _cachedArenaCamera = FindFirstObjectByType<NeonSumoArenaCamera>();
            return _cachedArenaCamera;
        }

        int GetConfiguredMaxPlayers()
        {
            return Mathf.Max(1, config.MaxPlayers);
        }

        int GetConfiguredMinPlayers()
        {
            return Mathf.Max(1, config.MinPlayers);
        }

        void ResetRoundStateFlags()
        {
            _isGameActive = false;
            _rampSpawnPrepared = false;
            _rampsRetracted = false;
            _roundEndTriggered = false;
            _localManualReadyClicked = false;
            _sdkAllPlayersReady = false;
            _manualReadyPlayerIds.Clear();
        }

        void ResetSessionFlags()
        {
            _didBackfill = false;
            _backfillInProgress = false;
            _isMasterResolved = false;
            _isMasterUser = false;
            _roomHostUserId = null;
        }

        void EnsureReadyCountdownCoordinator()
        {
            if (_readyCountdownCoordinator != null) return;
            var ctx = new ReadyCountdownContext
            {
                DurationSeconds = readyCountdownDuration,
                ShowStartingIn = sec => uiManager?.ShowStartingIn(sec),
                UpdateStartingIn = sec => uiManager?.UpdateStartingIn(sec),
                HideStartingIn = () => uiManager?.HideStartingIn(),
                BroadcastStart = seconds =>
                {
                    if (_multiplayerClient == null) return;
                    var payload = JsonConvert.SerializeObject(new { seconds });
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.StartReadyPhase, payload, Guid.NewGuid().ToString());
                },
                BroadcastUpdate = seconds =>
                {
                    if (_multiplayerClient == null) return;
                    var payload = JsonConvert.SerializeObject(new { seconds });
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.UpdateReadyPhase, payload, Guid.NewGuid().ToString());
                },
                BroadcastCancel = () =>
                {
                    if (_multiplayerClient == null) return;
                    NeonSumoActionSyncSend.Competition(_multiplayerClient, NeonSumoNetEvents.CancelReadyPhase, string.Empty, Guid.NewGuid().ToString());
                },
                IsMasterClient = IsMasterClient,
                OnComplete = BeginGameStartSequence,
                StartCoroutine = r => StartCoroutine(r),
                StopCoroutine = c => StopCoroutine(c),
            };
            _readyCountdownCoordinator = new ReadyCountdownCoordinator(ctx);
        }

        void OnDestroy()
        {
            OnInitialized = null;
            UnsubscribeSdkEvents();
        }

        void FixedUpdate()
        {
            if (!_isGameActive || _flow.CurrentState != NeonSumoGameState.Playing || _hostInputSync == null) return;
            bool isHost = _multiplayerClient == null || IsMasterClient();

            if (isHost)
            {
                _hostInputSync.TickHost(Time.fixedDeltaTime);
            }
            else if (_multiplayerClient != null)
            {
                _hostInputSync.TickClient(Time.fixedDeltaTime);
            }
        }

        void EnsureMessageHandler()
        {
            if (_messageHandler != null) return;
            var ctx = new NeonSumoMessageContext(
                receivePlayerInput: (userId, moveX, moveZ, boost, timestamp) =>
                    _hostInputSync?.ReceivePlayerInput(userId, moveX, moveZ, boost, timestamp),
                isMasterClient: IsMasterClient);
            _messageHandler = new NeonSumoMessageHandler(ctx);
        }
    }
}

