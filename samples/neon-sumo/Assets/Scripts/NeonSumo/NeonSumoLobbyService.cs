using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Handles matchmaking lifecycle: connect, set actor, create/join room.
    /// Translates ViverseSDK 1.2 SendRequest/JSON callbacks into async methods for UI.
    /// </summary>
    public class NeonSumoLobbyService
    {
        private const string LogPrefix = "[NeonSumoLobby]";
        private const string InvalidExampleAppId = "example-app-id";
        private const int RequestTimeoutMs = 15000;

        private readonly NeonSumoConfig _config;
        private readonly string _appIdOverride;
        private readonly string _roomMode;

        private MatchmakingClient _matchmakingClient;
        private Actor _currentActor;
        private Room _currentRoom;
        private bool _isBusy;
        private List<RoomListItemData> _cachedRoomList = new List<RoomListItemData>();
        private readonly List<Room> _cachedRooms = new List<Room>();
        private readonly List<TaskCompletionSource<string>> _pendingCompletions = new List<TaskCompletionSource<string>>();

        public Actor CurrentActor => _currentActor;
        public Room CurrentRoom => _currentRoom;
        public bool IsBusy => _isBusy;
        public IReadOnlyList<RoomListItemData> CachedRoomList => _cachedRoomList;

        /// <summary>
        /// Room list row id to tint yellow when this client owns the current room
        /// (master_client_id == local session_id).
        /// </summary>
        public string OwnedRoomListHighlightId
        {
            get
            {
                if (_currentRoom == null || !IsCurrentRoomOwnedByLocalActor())
                    return null;
                if (!string.IsNullOrEmpty(_currentRoom.id))
                    return _currentRoom.id;
                return _currentRoom.game_session;
            }
        }

        public event Action<string> OnStatusChanged;
        public event Action OnConnected;
        public event Action<Room> OnRoomJoined;
        public event Action OnRoomClosed;
        public event Action OnLeftRoom;
        public event Action<string> OnError;
        public event Action OnGameStartNotify;
        public event Action<IReadOnlyList<RoomListItemData>> OnRoomListChanged;

        public NeonSumoLobbyService(NeonSumoConfig config, string appIdOverride, string roomMode)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _appIdOverride = appIdOverride ?? string.Empty;
            _roomMode = roomMode ?? "Room";
        }

        public bool IsCurrentRoomOwnedByLocalActor()
        {
            return NeonSumoMatchmakingJson.IsOwnedByLocalActor(_currentRoom, _currentActor);
        }

        public void Connect()
        {
            EnsureMatchmakingClient();

            string appId = ResolveAppId();
            if (!IsValidAppId(appId))
                return;

            DebugLogger.Log($"{LogPrefix} Matchmaking client type={_matchmakingClient.GetType().FullName} go='{_matchmakingClient.gameObject.name}'");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _matchmakingClient.Initialize(appId, debugMode: true);
#else
            _matchmakingClient.Initialize(appId, debugMode: false);
#endif
            RaiseStatus("Initializing SDK...");
        }

        public async Task SetActorAsync(string actorName)
        {
            if (_matchmakingClient == null)
            {
                RaiseStatus("Matchmaking not connected.");
                return;
            }

            // Do not send session_id here. MatchmakingClient.SendRequest already injects
            // the Initialize GUID; a second payload session_id becomes a duplicate JSON key
            // and fights the id the server uses on CreateRoom/JoinRoom.
            _currentActor = new Actor
            {
                name = actorName
            };

            var payload = NeonSumoMatchmakingJson.SerializePayload(new Dictionary<string, object>
            {
                { "properties", new Dictionary<string, object> { { "name", actorName } } }
            });

            try
            {
                string response = await SendMatchmakingRequestAsync("SetActor", payload);
                TryApplyActorFromSdk(response);
                RaiseStatus($"Connected as {_currentActor.name}");
            }
            catch (Exception ex)
            {
                RaiseStatus($"SetActor failed: {ex.Message}");
            }
        }

        public async Task RefreshRoomsAsync()
        {
            if (_matchmakingClient == null || !_matchmakingClient.IsConnected())
            {
                PublishRoomList(new List<RoomListItemData>());
                return;
            }

            try
            {
                string response = await SendMatchmakingRequestAsync("GetAvailableRooms", "{}");
                var fetched = NeonSumoMatchmakingJson.ParseRoomList(response);
                ReplaceCachedRooms(fetched);
                if (_currentRoom != null)
                    UpsertCachedRoom(_currentRoom);
            }
            catch (Exception ex)
            {
                RaiseStatus($"Refresh failed: {ex.Message}");
            }

            PublishRoomList(BuildJoinableRoomListItems(_cachedRooms));
        }

        public async Task CreateRoomAsync(string roomCode, int minPlayers, int maxPlayers)
        {
            if (_isBusy) return;
            if (!EnsureMatchmakingReady()) return;

            _isBusy = true;
            try
            {
                RaiseStatus($"Creating room: {roomCode}");
                var payloadFields = new Dictionary<string, object>
                {
                    { "room_name", roomCode },
                    { "mode", _roomMode },
                    { "max_players", maxPlayers },
                    { "min_players", minPlayers }
                };
                if (!string.IsNullOrEmpty(_currentActor.session_id))
                    payloadFields["master_client_id"] = _currentActor.session_id;

                var payload = NeonSumoMatchmakingJson.SerializePayload(payloadFields);
                string response = await SendMatchmakingRequestAsync("CreateRoom", payload);
                ApplyJoinedRoomFromJson(response, "CreateRoom");
            }
            catch (Exception ex)
            {
                RaiseStatus($"Create failed: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async Task LeaveRoomAsync()
        {
            if (_isBusy) return;
            if (_currentRoom == null)
            {
                DebugLogger.Log($"{LogPrefix} LeaveRoomAsync called with no current room.");
                return;
            }
            if (!EnsureMatchmakingReady()) return;

            _isBusy = true;
            try
            {
                RaiseStatus("Leaving room...");
                var payloadFields = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(_currentRoom.id))
                    payloadFields["room_id"] = _currentRoom.id;

                await SendMatchmakingRequestAsync("LeaveRoom", NeonSumoMatchmakingJson.SerializePayload(payloadFields));
                _currentRoom = null;
                RaiseStatus("Left room.");
                OnLeftRoom?.Invoke();
            }
            catch (Exception ex)
            {
                RaiseStatus($"Leave failed: {ex.Message}");
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async Task JoinRoomAsync(string roomId)
        {
            if (_isBusy) return;
            if (!EnsureMatchmakingReady()) return;
            if (string.IsNullOrEmpty(roomId))
            {
                RaiseStatus("No room selected.");
                return;
            }

            _isBusy = true;
            try
            {
                RaiseStatus("Joining room...");
                var payload = NeonSumoMatchmakingJson.SerializePayload(new Dictionary<string, object>
                {
                    { "room_id", roomId }
                });
                string response = await SendMatchmakingRequestAsync("JoinRoom", payload);
                ApplyJoinedRoomFromJson(response, "JoinRoom");
            }
            catch (Exception ex)
            {
                RaiseStatus($"Join failed: {ex.Message}");
                await RefreshRoomsAsync();
            }
            finally
            {
                _isBusy = false;
            }
        }

        public (string roomId, string appId, string userSessionId)? GetHandoffData()
        {
            if (_currentRoom == null || _currentActor == null)
                return null;

            string appId = ResolveAppId();
            if (string.IsNullOrWhiteSpace(appId))
                return null;

            string roomId = !string.IsNullOrEmpty(_currentRoom.game_session)
                ? _currentRoom.game_session
                : _currentRoom.id;
            if (string.IsNullOrEmpty(roomId))
                return null;

            if (string.IsNullOrEmpty(_currentRoom.game_session))
                DebugLogger.LogWarning($"{LogPrefix} Handoff using room.id because game_session was empty.");

            return (roomId, appId, _currentActor.session_id);
        }

        /// <summary>
        /// Unbinds lobby handlers from the persistent MatchmakingClient without closing the socket.
        /// Lobby → Gameplay must not Disconnect: the waiting room stays in GetAvailableRooms.
        /// </summary>
        public void ReleaseForGameplayHandoff()
        {
            ReleaseLobbyBindings("Lobby gameplay handoff");
        }

        /// <summary>
        /// Unbind and Disconnect. Call only when leaving matchmaking (main menu / quit), not on scene unload.
        /// </summary>
        public void Cleanup()
        {
            MatchmakingClient client = _matchmakingClient;
            ReleaseLobbyBindings("Lobby cleanup");
            if (client != null)
                client.Disconnect();
        }

        void ReleaseLobbyBindings(string pendingFailReason)
        {
            FailPendingMatchmakingRequests(pendingFailReason);

            if (_matchmakingClient != null)
            {
                DetachMatchmakingHandlers(_matchmakingClient);
                _matchmakingClient = null;
            }

            _currentActor = null;
            _currentRoom = null;
            _isBusy = false;
            _cachedRoomList = new List<RoomListItemData>();
            _cachedRooms.Clear();
        }

        private void EnsureMatchmakingClient()
        {
            if (_matchmakingClient != null) return;

            _matchmakingClient = PlaySdkRuntimeRoot.Instance.GetOrCreateMatchmakingClient();
            if (_matchmakingClient == null)
                throw new InvalidOperationException("PlaySdkRuntimeRoot did not return a ViverseSDK.MatchmakingClient.");

            if (_matchmakingClient.GetType() != typeof(MatchmakingClient))
                throw new InvalidOperationException($"Lobby expected {typeof(MatchmakingClient).FullName}, got {_matchmakingClient.GetType().FullName}");

            AttachMatchmakingHandlers(_matchmakingClient);
        }

        private void AttachMatchmakingHandlers(MatchmakingClient client)
        {
            if (client == null) return;

            client.OnConnect += HandleConnected;
            client.OnDisconnect += HandleDisconnect;
            client.OnError += HandleError;
            client.OnJoinRoom += HandleJoinRoomJson;
            client.OnRoomClosed += HandleRoomClosed;
            client.OnGameStartNotify += HandleGameStartNotify;
            client.OnRoomListUpdate += HandleRoomListUpdateJson;
        }

        private void DetachMatchmakingHandlers(MatchmakingClient client)
        {
            if (client == null) return;

            client.OnConnect -= HandleConnected;
            client.OnDisconnect -= HandleDisconnect;
            client.OnError -= HandleError;
            client.OnJoinRoom -= HandleJoinRoomJson;
            client.OnRoomClosed -= HandleRoomClosed;
            client.OnGameStartNotify -= HandleGameStartNotify;
            client.OnRoomListUpdate -= HandleRoomListUpdateJson;
        }

        private async Task<string> SendMatchmakingRequestAsync(string requestType, string jsonPayload)
        {
            if (_matchmakingClient == null)
                throw new InvalidOperationException("Matchmaking client is not initialized.");

            DebugLogger.Log($"{LogPrefix} SendRequest type={requestType} payload={jsonPayload}");

            var tcs = new TaskCompletionSource<string>();
            _pendingCompletions.Add(tcs);
            try
            {
                _matchmakingClient.SendRequest(requestType, jsonPayload, response =>
                {
                    DebugLogger.Log($"{LogPrefix} {requestType} response={response}");
                    if (NeonSumoMatchmakingJson.TryParseSuccess(response, out var success, out var message) && !success)
                    {
                        tcs.TrySetException(new Exception(string.IsNullOrEmpty(message)
                            ? $"{requestType} returned success=false"
                            : $"{requestType} failed: {message}"));
                        return;
                    }

                    tcs.TrySetResult(response ?? string.Empty);
                });

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs));
                if (completed != tcs.Task)
                    throw new TimeoutException($"{requestType} timed out after {RequestTimeoutMs}ms");

                return await tcs.Task;
            }
            finally
            {
                _pendingCompletions.Remove(tcs);
            }
        }

        private void TryApplyActorFromSdk(string setActorResponse)
        {
            string sdkActorJson = _matchmakingClient.GetCurrentActor();
            if (!string.IsNullOrEmpty(sdkActorJson))
                DebugLogger.Log($"{LogPrefix} GetCurrentActor()={sdkActorJson}");
            else
                DebugLogger.Log($"{LogPrefix} GetCurrentActor() was empty after SetActor (Editor does not store _currentActor).");

            var fromGetter = NeonSumoMatchmakingJson.ParseActor(sdkActorJson);
            var fromResponse = NeonSumoMatchmakingJson.ParseActor(setActorResponse);
            var resolved = fromGetter ?? fromResponse;
            if (resolved == null || string.IsNullOrEmpty(resolved.session_id))
            {
                DebugLogger.LogWarning($"{LogPrefix} SetActor did not return a session_id; will sync from CreateRoom/JoinRoom.");
                return;
            }

            if (resolved.session_id != _currentActor.session_id)
            {
                DebugLogger.Log($"{LogPrefix} Local session_id={resolved.session_id} from SetActor response/GetCurrentActor");
                _currentActor.session_id = resolved.session_id;
            }

            if (!string.IsNullOrEmpty(resolved.name))
                _currentActor.name = resolved.name;
        }

        private void SyncLocalActorFromRoom(Room room)
        {
            if (_currentActor == null || room == null)
                return;

            if (TryFindActorInRoom(room, _currentActor.session_id, out var alreadyMatched) && alreadyMatched != null)
            {
                if (!string.IsNullOrEmpty(alreadyMatched.name))
                    _currentActor.name = alreadyMatched.name;
                return;
            }

            Actor serverActor = null;
            if (!string.IsNullOrEmpty(_currentActor.name) && TryFindActorInRoomByName(room, _currentActor.name, out var byName))
                serverActor = byName;
            else if (room.actors != null && room.actors.Count == 1)
                serverActor = room.actors[0];
            else if (!string.IsNullOrEmpty(room.master_client_id) && string.IsNullOrEmpty(_currentActor.session_id))
                serverActor = new Actor { session_id = room.master_client_id };

            if (serverActor == null || string.IsNullOrEmpty(serverActor.session_id))
                return;
            if (serverActor.session_id == _currentActor.session_id)
                return;

            DebugLogger.Log($"{LogPrefix} Syncing local session_id to server actor session_id={serverActor.session_id} (was '{_currentActor.session_id}')");
            _currentActor.session_id = serverActor.session_id;
            if (!string.IsNullOrEmpty(serverActor.name))
                _currentActor.name = serverActor.name;
        }

        private static bool TryFindActorInRoom(Room room, string sessionId, out Actor actor)
        {
            actor = null;
            if (room?.actors == null || string.IsNullOrEmpty(sessionId))
                return false;
            for (int i = 0; i < room.actors.Count; i++)
            {
                if (room.actors[i] != null &&
                    string.Equals(room.actors[i].session_id, sessionId, StringComparison.Ordinal))
                {
                    actor = room.actors[i];
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindActorInRoomByName(Room room, string name, out Actor actor)
        {
            actor = null;
            if (room?.actors == null || string.IsNullOrEmpty(name))
                return false;
            for (int i = 0; i < room.actors.Count; i++)
            {
                if (room.actors[i] != null &&
                    string.Equals(room.actors[i].name, name, StringComparison.Ordinal))
                {
                    actor = room.actors[i];
                    return true;
                }
            }
            return false;
        }

        private void ApplyJoinedRoomFromJson(string json, string source)
        {
            var room = NeonSumoMatchmakingJson.ParseRoomFromJoinOrCreateResponse(json);
            if (room == null)
            {
                DebugLogger.LogWarning($"{LogPrefix} {source} succeeded but room JSON could not be parsed. Raw={json}");
                return;
            }

            HandleJoinRoom(room);
        }

        private void HandleConnected()
        {
            RaiseStatus("Connected. Setting up actor...");
            OnConnected?.Invoke();
        }

        private void HandleDisconnect()
        {
            FailPendingMatchmakingRequests("Matchmaking disconnected");
            bool hadRoom = _currentRoom != null;
            _currentRoom = null;
            _isBusy = false;
            RaiseStatus("Disconnected from matchmaking.");
            OnError?.Invoke("Matchmaking disconnected");
            if (hadRoom)
                OnLeftRoom?.Invoke();
        }

        private void FailPendingMatchmakingRequests(string reason)
        {
            if (_pendingCompletions.Count == 0)
                return;

            var pending = _pendingCompletions.ToArray();
            _pendingCompletions.Clear();
            var ex = new Exception(reason);
            for (int i = 0; i < pending.Length; i++)
                pending[i]?.TrySetException(ex);
        }

        private void HandleRoomListUpdateJson(string json)
        {
            DebugLogger.Log($"{LogPrefix} OnRoomListUpdate raw={json}");
            var rooms = NeonSumoMatchmakingJson.ParseRoomList(json);
            ReplaceCachedRooms(rooms);
            if (_currentRoom != null)
                UpsertCachedRoom(_currentRoom);
            PublishRoomList(BuildJoinableRoomListItems(_cachedRooms));
        }

        private void HandleJoinRoomJson(string json)
        {
            DebugLogger.Log($"{LogPrefix} OnJoinRoom raw={json}");
            ApplyJoinedRoomFromJson(json, "OnJoinRoom");
        }

        private void ReplaceCachedRooms(List<Room> rooms)
        {
            _cachedRooms.Clear();
            if (rooms == null || rooms.Count == 0)
                return;
            _cachedRooms.AddRange(rooms);
            SortCachedRooms();
        }

        private void UpsertCachedRoom(Room room)
        {
            if (room == null)
                return;

            string key = !string.IsNullOrEmpty(room.id) ? room.id : room.game_session;
            if (string.IsNullOrEmpty(key))
                return;

            for (int i = 0; i < _cachedRooms.Count; i++)
            {
                var existing = _cachedRooms[i];
                string existingKey = !string.IsNullOrEmpty(existing?.id) ? existing.id : existing?.game_session;
                if (existingKey == key)
                {
                    _cachedRooms[i] = room;
                    SortCachedRooms();
                    return;
                }
            }

            _cachedRooms.Add(room);
            SortCachedRooms();
        }

        private void SortCachedRooms()
        {
            _cachedRooms.Sort(CompareRooms);
        }

        private static int CompareRooms(Room a, Room b)
        {
            int byName = string.Compare(a?.name, b?.name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
                return byName;
            return string.Compare(RoomKey(a), RoomKey(b), StringComparison.Ordinal);
        }

        private static string RoomKey(Room room)
        {
            if (room == null)
                return string.Empty;
            return !string.IsNullOrEmpty(room.id) ? room.id : (room.game_session ?? string.Empty);
        }

        private static List<RoomListItemData> BuildJoinableRoomListItems(IReadOnlyList<Room> rooms)
        {
            if (rooms == null || rooms.Count == 0)
                return new List<RoomListItemData>();

            var items = new List<RoomListItemData>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                Room r = rooms[i];
                if (IsJoinableRoom(r))
                    items.Add(ToRoomListItemData(r));
            }

            items.Sort(CompareRoomListItems);
            return items;
        }

        private static int CompareRoomListItems(RoomListItemData a, RoomListItemData b)
        {
            int byName = string.Compare(a?.DisplayName, b?.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
                return byName;
            return string.Compare(a?.Id, b?.Id, StringComparison.Ordinal);
        }

        private void PublishRoomList(List<RoomListItemData> items)
        {
            if (items == null)
                items = new List<RoomListItemData>();
            if (RoomListItemsEqual(_cachedRoomList, items))
                return;
            _cachedRoomList = items;
            OnRoomListChanged?.Invoke(items);
        }

        private static bool RoomListItemsEqual(IReadOnlyList<RoomListItemData> a, IReadOnlyList<RoomListItemData> b)
        {
            if (ReferenceEquals(a, b))
                return true;
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
                return false;
            if (countA == 0)
                return true;

            var byId = new Dictionary<string, RoomListItemData>(countA);
            for (int i = 0; i < countA; i++)
            {
                var item = a[i];
                if (item == null || string.IsNullOrEmpty(item.Id))
                    return false;
                byId[item.Id] = item;
            }

            for (int i = 0; i < countB; i++)
            {
                var right = b[i];
                if (right == null || string.IsNullOrEmpty(right.Id) || !byId.TryGetValue(right.Id, out var left))
                    return false;
                if (left.GameSession != right.GameSession ||
                    left.DisplayName != right.DisplayName ||
                    left.PlayerCount != right.PlayerCount ||
                    left.MaxPlayers != right.MaxPlayers ||
                    left.IsJoinable != right.IsJoinable ||
                    left.StatusText != right.StatusText)
                    return false;
            }

            return true;
        }

        private static bool IsJoinableRoom(Room r)
        {
            if (r == null) return false;
            if (r.is_closed || r.is_game_started)
                return false;
            if (r.max_players <= 0)
                return true;
            return (r.actors?.Count ?? 0) < r.max_players;
        }

        private static RoomListItemData ToRoomListItemData(Room r)
        {
            int count = r.actors?.Count ?? 0;
            return new RoomListItemData
            {
                Id = !string.IsNullOrEmpty(r.id) ? r.id : (r.game_session ?? string.Empty),
                GameSession = r.game_session ?? string.Empty,
                DisplayName = r.name ?? "Unknown",
                PlayerCount = count,
                MaxPlayers = r.max_players,
                IsJoinable = true,
                StatusText = $"{count}/{r.max_players}"
            };
        }

        private void HandleJoinRoom(Room room)
        {
            _currentRoom = room;
            SyncLocalActorFromRoom(room);
            UpsertCachedRoom(room);
            PublishRoomList(BuildJoinableRoomListItems(_cachedRooms));
            RaiseStatus($"Joined room: {room.name} ({room.id})");
            if (string.IsNullOrEmpty(room.master_client_id))
                DebugLogger.LogWarning($"{LogPrefix} Joined room JSON had no master_client_id; ownership highlight may be empty.");
            else
                DebugLogger.Log($"{LogPrefix} Ownership: master_client_id={room.master_client_id} local_session_id={_currentActor?.session_id} owned={IsCurrentRoomOwnedByLocalActor()}");
            OnRoomJoined?.Invoke(room);
        }

        private void HandleRoomClosed()
        {
            _currentRoom = null;
            RaiseStatus("Room closed.");
            OnRoomClosed?.Invoke();
        }

        private void HandleError(string message)
        {
            RaiseStatus($"Matchmaking error: {message}");
            OnError?.Invoke(message);
        }

        private void HandleGameStartNotify()
        {
            RaiseStatus("Game start notified. Loading gameplay...");
            OnGameStartNotify?.Invoke();
        }

        private string ResolveAppId()
        {
            if (!string.IsNullOrWhiteSpace(_appIdOverride))
                return _appIdOverride.Trim();

            return _config != null ? _config.AppId : string.Empty;
        }

        private bool IsValidAppId(string appId)
        {
            if (!string.IsNullOrWhiteSpace(appId) && appId != InvalidExampleAppId)
                return true;

            string msg = "Set your App ID in NeonSumoConfig. Using 'example-app-id' will not work for matchmaking.";
            RaiseStatus("ERROR: " + msg);
            DebugLogger.LogError($"{LogPrefix} {msg}");
            return false;
        }

        private bool EnsureMatchmakingReady()
        {
            if (_matchmakingClient == null || !_matchmakingClient.IsConnected())
            {
                RaiseStatus("Connect matchmaking and set actor first.");
                return false;
            }

            if (_currentActor == null)
            {
                RaiseStatus("Set actor before creating/joining a room.");
                return false;
            }

            return true;
        }

        private void RaiseStatus(string message)
        {
            DebugLogger.Log($"{LogPrefix} {message}");
            OnStatusChanged?.Invoke(message);
        }
    }
}
