using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ViverseSDK;

namespace NeonSumo
{
    public class NeonSumoLobbyManager : MonoBehaviour
    {
        private const string DefaultRoomCodePrefix = "neon_sumo";

        [Header("Scene Flow")]
        public string gameplaySceneName = "NeonSumoScene";
        public string mainMenuSceneName = "NeonSumoMainMenuScene";

        [Header("UI (UI Toolkit)")]
        public NeonSumoLobbyPanelView lobbyPanelView;
        [Tooltip("Optional AppId override for editor testing.")]
        public string appIdOverride;

        [Header("Room Settings")]
        public NeonSumoConfig config;
        public string roomMode = "Room";

        [Header("Auto Flow")]
        public bool autoConnectOnStart = true;

        private const float RoomListPollIntervalSeconds = 2f;

        private NeonSumoLobbyService _lobbyService;
        private bool _isTransitioningToGameplay;
        private bool _isTransitioningToMainMenu;
        private Coroutine _roomListPollRoutine;

        private void Start()
        {
            NeonSumoLoadingCoordinator.Instance?.NotifyLobbySceneLoaded();

            if (!ValidateConfiguration())
                return;

            InitializeConfig();
            NeonSumoAuthSession.EnsureInitialized(config, appIdOverride, this);
            NeonSumoAuthSession.RequestLoginForMultiplayer();
            NeonSumoPlatformProfiles.LocalDisplayProfileChanged += HandleLocalDisplayProfileChanged;
            CreateAndWireService();
            InitializeView();
            RefreshViewState();

            if (autoConnectOnStart)
                _lobbyService.Connect();
        }

        private void OnDestroy()
        {
            NeonSumoPlatformProfiles.LocalDisplayProfileChanged -= HandleLocalDisplayProfileChanged;
            StopRoomListPolling();
            UnwireLobbyView();

            if (_lobbyService != null)
            {
                _lobbyService.OnStatusChanged -= SetStatus;
                _lobbyService.OnConnected -= HandleServiceConnected;
                _lobbyService.OnRoomJoined -= HandleRoomJoined;
                _lobbyService.OnRoomClosed -= HandleRoomClosed;
                _lobbyService.OnLeftRoom -= HandleLeftRoom;
                _lobbyService.OnError -= HandleServiceError;
                _lobbyService.OnGameStartNotify -= HandoffToGameplayScene;
                _lobbyService.OnRoomListChanged -= HandleRoomListChanged;
                if (_isTransitioningToGameplay)
                    _lobbyService.ReleaseForGameplayHandoff();
                else
                    _lobbyService.Cleanup();
            }
        }

        private bool ValidateConfiguration()
        {
            if (config != null) return true;
            DebugLogger.LogError("[NeonSumo] NeonSumoLobbyManager: NeonSumoConfig is required.");
            enabled = false;
            return false;
        }

        private void InitializeConfig()
        {
            config.EnsureRuntimeValidity();
        }

        private void CreateAndWireService()
        {
            _lobbyService = new NeonSumoLobbyService(config, appIdOverride, roomMode);

            _lobbyService.OnStatusChanged += SetStatus;
            _lobbyService.OnConnected += HandleServiceConnected;
            _lobbyService.OnRoomJoined += HandleRoomJoined;
            _lobbyService.OnRoomClosed += HandleRoomClosed;
            _lobbyService.OnLeftRoom += HandleLeftRoom;
            _lobbyService.OnError += HandleServiceError;
            _lobbyService.OnGameStartNotify += HandoffToGameplayScene;
            _lobbyService.OnRoomListChanged += HandleRoomListChanged;
        }

        private void HandleServiceConnected()
        {
            FireAndForget(OnConnectedAsync());
        }

        private async System.Threading.Tasks.Task OnConnectedAsync()
        {
            await SetActor();
            lobbyPanelView?.SetRoomListLoading(true);
            await _lobbyService.RefreshRoomsAsync();
            lobbyPanelView?.SetRoomListLoading(false);
            StartRoomListPolling();
        }

        private void StartRoomListPolling()
        {
            if (_roomListPollRoutine != null)
                return;
            _roomListPollRoutine = StartCoroutine(PollRoomListWhileIdle());
        }

        private void StopRoomListPolling()
        {
            if (_roomListPollRoutine == null)
                return;
            StopCoroutine(_roomListPollRoutine);
            _roomListPollRoutine = null;
        }

        private IEnumerator PollRoomListWhileIdle()
        {
            var wait = new WaitForSeconds(RoomListPollIntervalSeconds);
            while (_lobbyService != null)
            {
                yield return wait;
                if (_isTransitioningToGameplay || _isTransitioningToMainMenu)
                    yield break;
                if (_lobbyService.CurrentRoom != null || _lobbyService.IsBusy)
                    continue;
                FireAndForget(_lobbyService.RefreshRoomsAsync());
            }
        }

        private void HandleRoomJoined(Room room)
        {
            if (lobbyPanelView != null && !string.IsNullOrEmpty(room.name))
                lobbyPanelView.RoomCode = room.name;
            lobbyPanelView?.SetRoomList(_lobbyService.CachedRoomList, joinEnabled: false, ownedRoomHighlightId: _lobbyService.OwnedRoomListHighlightId);
            RefreshViewState();
            if (_lobbyService.IsCurrentRoomOwnedByLocalActor())
                lobbyPanelView?.FocusEnterGameNextFrame();
        }

        private void HandleRoomClosed()
        {
            bool inRoom = _lobbyService.CurrentRoom != null;
            bool busy = _lobbyService.IsBusy;
            lobbyPanelView?.SetRoomList(_lobbyService.CachedRoomList, joinEnabled: !inRoom && !busy, ownedRoomHighlightId: _lobbyService.OwnedRoomListHighlightId);
            RefreshViewState();
        }

        private void HandleLeftRoom()
        {
            lobbyPanelView?.SetRoomList(_lobbyService.CachedRoomList, joinEnabled: true, ownedRoomHighlightId: _lobbyService.OwnedRoomListHighlightId);
            RefreshViewState();
        }

        private void HandleRoomListChanged(System.Collections.Generic.IReadOnlyList<RoomListItemData> rooms)
        {
            bool inRoom = _lobbyService.CurrentRoom != null;
            bool busy = _lobbyService.IsBusy;
            lobbyPanelView?.SetRoomList(rooms, joinEnabled: !inRoom && !busy, ownedRoomHighlightId: _lobbyService.OwnedRoomListHighlightId);
            RefreshViewState();
        }

        private void HandleServiceError(string message)
        {
            RefreshViewState();
        }

        private void InitializeView()
        {
            if (lobbyPanelView == null) return;

            if (string.IsNullOrEmpty(lobbyPanelView.RoomCode))
                lobbyPanelView.RoomCode = GenerateDefaultRoomCode();

            ApplyViverseDisplayNameToActorField();
            if (string.IsNullOrEmpty(lobbyPanelView.ActorName))
                lobbyPanelView.ActorName = GenerateDefaultActorName();

            lobbyPanelView.SetEnterGameEnabled(false);
            lobbyPanelView.SetLeaveRoomEnabled(false);
            WireLobbyView();
        }

        private void WireLobbyView()
        {
            if (lobbyPanelView == null) return;

            lobbyPanelView.RefreshClicked += RefreshRooms;
            lobbyPanelView.CreateRoomClicked += CreateRoom;
            lobbyPanelView.JoinRoomClicked += JoinRoom;
            lobbyPanelView.EnterGameClicked += EnterGame;
            lobbyPanelView.LeaveRoomClicked += LeaveRoom;
            lobbyPanelView.BackClicked += ReturnToMainMenu;
        }

        private void UnwireLobbyView()
        {
            if (lobbyPanelView == null) return;

            lobbyPanelView.RefreshClicked -= RefreshRooms;
            lobbyPanelView.CreateRoomClicked -= CreateRoom;
            lobbyPanelView.JoinRoomClicked -= JoinRoom;
            lobbyPanelView.EnterGameClicked -= EnterGame;
            lobbyPanelView.LeaveRoomClicked -= LeaveRoom;
            lobbyPanelView.BackClicked -= ReturnToMainMenu;
        }

        private void RefreshViewState()
        {
            if (lobbyPanelView == null) return;

            bool inRoom = _lobbyService.CurrentRoom != null;
            bool busy = _lobbyService.IsBusy;

            lobbyPanelView.SetCreateRoomEnabled(!inRoom && !busy);
            lobbyPanelView.SetRefreshEnabled(!busy);
            lobbyPanelView.SetJoinSelectedEnabled(!inRoom && !busy);
            lobbyPanelView.SetEnterGameEnabled(inRoom && !busy);
            lobbyPanelView.SetLeaveRoomEnabled(inRoom && !busy);
            lobbyPanelView.SetRoomCodeInputEnabled(!inRoom && !busy);
        }

        public void ConnectMatchmaking()
        {
            _lobbyService.Connect();
        }

        private async Task SetActor()
        {
            string actorName = GetActorName();
            await _lobbyService.SetActorAsync(actorName);
        }

        private void RefreshRooms()
        {
            FireAndForget(RefreshRoomsWithLoadingAsync());
        }

        private async Task RefreshRoomsWithLoadingAsync()
        {
            lobbyPanelView?.SetRoomListLoading(true);
            try
            {
                await _lobbyService.RefreshRoomsAsync();
            }
            finally
            {
                lobbyPanelView?.SetRoomListLoading(false);
            }
        }

        private void CreateRoom()
        {
            FireAndForget(RunRoomActionThenRefreshAsync(
                _lobbyService.CreateRoomAsync(
                    GetRoomCode(),
                    GetMinPlayers(),
                    GetMaxPlayers())));
        }

        private void JoinRoom(string roomId)
        {
            FireAndForget(RunRoomActionThenRefreshAsync(_lobbyService.JoinRoomAsync(roomId)));
        }

        private void EnterGame()
        {
            HandoffToGameplayScene();
        }

        private void LeaveRoom()
        {
            FireAndForget(RunRoomActionThenRefreshAsync(_lobbyService.LeaveRoomAsync()));
        }

        private async Task RunRoomActionThenRefreshAsync(Task action)
        {
            try
            {
                await action;
            }
            finally
            {
                RefreshViewState();
                if (_lobbyService != null &&
                    _lobbyService.CurrentRoom != null &&
                    !_lobbyService.IsBusy &&
                    _lobbyService.IsCurrentRoomOwnedByLocalActor())
                    lobbyPanelView?.FocusEnterGameNextFrame();
            }
        }

        private void ReturnToMainMenu()
        {
            if (_isTransitioningToGameplay || _isTransitioningToMainMenu)
                return;

            _isTransitioningToMainMenu = true;
            StopRoomListPolling();
            FireAndForget(ReturnToMainMenuAsync());
        }

        private async Task ReturnToMainMenuAsync()
        {
            try
            {
                if (_lobbyService?.CurrentRoom != null && !_lobbyService.IsBusy)
                    await _lobbyService.LeaveRoomAsync();

                _lobbyService?.Cleanup();
                NeonSumoSessionStore.Clear();
                MatchmakingHandoffStore.Clear();

                var coord = NeonSumoLoadingCoordinator.EnsureExists();
                coord.BeginMainMenuSceneLoadFromLobby();
                StartCoroutine(LoadMainMenuSceneAsync());
            }
            catch (Exception ex)
            {
                _isTransitioningToMainMenu = false;
                SetStatus($"Failed to return to menu: {ex.Message}");
            }
        }

        IEnumerator LoadMainMenuSceneAsync()
        {
            yield return null;

            AsyncOperation op;
            using (NeonSumoMenuProfilerMarkers.LoadGameplaySceneAsync.Auto())
            {
                op = SceneManager.LoadSceneAsync(mainMenuSceneName);
            }

            while (op != null && !op.isDone)
                yield return null;
        }

        private static async void FireAndForget(Task task)
        {
            try { await task; }
            catch (Exception ex) { DebugLogger.LogError($"[NeonSumoLobby] Async error: {ex.Message}"); }
        }

        private void HandoffToGameplayScene()
        {
            if (_isTransitioningToGameplay)
                return;

            var handoff = _lobbyService.GetHandoffData();
            if (!handoff.HasValue)
            {
                SetStatus("Missing room/actor data for handoff.");
                return;
            }

            _isTransitioningToGameplay = true;
            StopRoomListPolling();

            var (roomId, appId, userSessionId) = handoff.Value;
            NeonSumoSessionStore.Mode = NeonSumoGameMode.OnlineMultiplayer;
            MatchmakingHandoffStore.SaveHandoff(roomId, appId, userSessionId, GetHandoffDisplayNameSeed());
            // Keep MatchmakingClient alive so the room stays joinable for other players
            StartCoroutine(LoadGameplaySceneAsync());
        }

        IEnumerator LoadGameplaySceneAsync()
        {
            var coord = NeonSumoLoadingCoordinator.EnsureExists();
            coord.BeginOnlineGameplaySceneLoadFromLobby();
            yield return null;

            AsyncOperation op;
            using (NeonSumoMenuProfilerMarkers.LoadGameplaySceneAsync.Auto())
            {
                op = SceneManager.LoadSceneAsync(gameplaySceneName);
            }

            while (op != null && !op.isDone)
                yield return null;
        }

        private void SetStatus(string message)
        {
            if (lobbyPanelView != null)
                lobbyPanelView.Status = message;

            if (_lobbyService != null)
                RefreshViewState();
        }

        private int GetMaxPlayers() => Mathf.Max(1, config.MaxPlayers);
        private int GetMinPlayers() => Mathf.Max(1, config.MinPlayers);
        private string GenerateDefaultActorName() => $"Player{UnityEngine.Random.Range(1000, 9999)}";
        private static string GenerateDefaultRoomCode() => $"{DefaultRoomCodePrefix}_{UnityEngine.Random.Range(1000, 9999)}";

        private void HandleLocalDisplayProfileChanged()
        {
            ApplyViverseDisplayNameToActorField();
        }

        private void ApplyViverseDisplayNameToActorField()
        {
            if (lobbyPanelView == null)
                return;
            if (!NeonSumoPlatformProfiles.TryGetProfileDisplayLabel(out string name) || string.IsNullOrEmpty(name))
                return;
            lobbyPanelView.ActorName = name;
        }

        private string GetHandoffDisplayNameSeed()
        {
            if (NeonSumoPlatformProfiles.TryGetProfileDisplayLabel(out string name) && !string.IsNullOrEmpty(name))
                return name;

            return GetActorName();
        }

        private string GetRoomCode()
        {
            string code = lobbyPanelView != null ? lobbyPanelView.RoomCode : string.Empty;
            return string.IsNullOrWhiteSpace(code) ? GenerateDefaultRoomCode() : code.Trim();
        }

        private string GetActorName()
        {
            if (lobbyPanelView != null)
            {
                string name = lobbyPanelView.ActorName;
                if (!string.IsNullOrEmpty(name))
                    return name.Trim();
            }
            return GenerateDefaultActorName();
        }
    }
}
