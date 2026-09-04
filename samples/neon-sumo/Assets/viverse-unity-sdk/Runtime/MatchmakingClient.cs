using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using Viverse.NativeWebSocket;
#endif

namespace ViverseSDK
{
    [Serializable]
    public class Room
    {
        public string id;
        public string mode;
        public string name;
        public List<Actor> actors;
        public int max_players;
        public int min_players;
        public bool is_closed;
        public bool is_game_started;
        public string master_client_id;
        public string game_session;
    }

    [Serializable]
    public class Actor
    {
        public string session_id;
        public string name;
        public bool is_master_client;
    }

    [Serializable]
    public class MatchResponse
    {
        public int request_id;
        public bool success;
        public string message;
        public string request_type;
        public string data;
    }

    [Serializable]
    public class EventPayload
    {
        public string @event;
        public string data;
    }

    public enum ConnectionState
    {
        Uninitialized = 0,
        Connecting = 1,
        Connected = 2,
        JoiningLobby = 3,
        JoinedLobby = 4,
        JoiningRoom = 5,
        JoinedRoom = 6,
        LeavingRoom = 7,
        Disconnected = 8,
    }

    public class MatchmakingClient : MonoBehaviour
    {
        public static MatchmakingClient Instance { get; private set; }

        public event Action OnConnect;
        public event Action OnDisconnect;
        public event Action<string> OnError;
        public event Action<string> OnStateChange;
        public event Action<string> OnRoomListUpdate;
        public event Action<string> OnJoinRoom;
        public event Action OnJoinedLobby;
        public event Action<string> OnRoomActorChange;
        public event Action OnRoomClosed;
        public event Action OnGameStartNotify;
        public event Action OnMatchingTimeout;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViversePlay_Matchmaking_Initialize(string appId, bool debugMode, string gameObjectName, string callbackMethod);

        [DllImport("__Internal")]
        private static extern void ViversePlay_Matchmaking_Disconnect();

        [DllImport("__Internal")]
        private static extern void ViversePlay_Matchmaking_SendRequest(string requestJson);

        [DllImport("__Internal")]
        private static extern string ViversePlay_Matchmaking_GetCurrentActor();

        [DllImport("__Internal")]
        private static extern string ViversePlay_Matchmaking_GetCurrentRoom();

        [DllImport("__Internal")]
        private static extern bool ViversePlay_Matchmaking_IsInLobby();

        [DllImport("__Internal")]
        private static extern bool ViversePlay_Matchmaking_IsJoinedToRoom();
#endif

        private int _requestCounter = 0;
        private Dictionary<int, Action<string>> _pendingRequests = new Dictionary<int, Action<string>>();
        private string _appId;
        private string _sessionId;
        private bool _isConnected;

#if !UNITY_WEBGL || UNITY_EDITOR
        private WebSocket _ws;
        private bool _debugMode;
        private Room _currentRoom;
        private Actor _currentActor;
        private static readonly byte[] _xorKey = { 0x12, 0x34, 0x56, 0x78 };
        private const string DEFAULT_MATCHMAKING_URL = "wss://broadcasting-gateway-gaming.vrprod.viveport.com";
        private Coroutine _heartbeatCoroutine;
        private const float HEARTBEAT_INTERVAL = 30f;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void Update()
        {
            _ws?.DispatchMessageQueue();
        }

        private async void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopHeartbeat();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                await _ws.Close();
            }
        }
#endif

        public void Initialize(string appId, bool debugMode = false)
        {
            _appId = appId;
            _sessionId = System.Guid.NewGuid().ToString();
#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Matchmaking_Initialize(appId, debugMode, gameObject.name, nameof(OnJSMessage));
#else
            _debugMode = debugMode;

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288;

            var url = $"{DEFAULT_MATCHMAKING_URL}/api/matchmaking-service/v1/match?app_id={appId}";
            if (debugMode) url += "&debug=true";

            Debug.Log($"[Matchmaking] Connecting to: {url}");
            _ws = new WebSocket(url);

            _ws.OnOpen += () =>
            {
                _isConnected = true;
                Debug.Log("[Matchmaking] Connected");
                OnConnect?.Invoke();
                OnStateChange?.Invoke("Connected");
                StartHeartbeat();
            };

            _ws.OnClose += (closeCode) =>
            {
                _isConnected = false;
                _currentRoom = null;
                _currentActor = null;
                StopHeartbeat();

                foreach (var kvp in _pendingRequests)
                    kvp.Value?.Invoke("{\"success\":false,\"message\":\"Connection closed\"}");
                _pendingRequests.Clear();

                OnDisconnect?.Invoke();
                OnStateChange?.Invoke("Disconnected");
            };

            _ws.OnError += (error) =>
            {
                OnError?.Invoke(error);
            };

            _ws.OnMessage += (bytes) =>
            {
                HandleEditorMessage(bytes);
            };

            _ = _ws.Connect();
#endif
        }

        public void Disconnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Matchmaking_Disconnect();
#else
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                _ = _ws.Close();
            }
#endif
        }

        public string GetCurrentActor()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ViversePlay_Matchmaking_GetCurrentActor();
#else
            return _currentActor != null ? JsonUtility.ToJson(_currentActor) : null;
#endif
        }

        public string GetCurrentRoom()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ViversePlay_Matchmaking_GetCurrentRoom();
#else
            return _currentRoom != null ? JsonUtility.ToJson(_currentRoom) : null;
#endif
        }

        public bool IsInLobby()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ViversePlay_Matchmaking_IsInLobby();
#else
            return _currentRoom == null;
#endif
        }

        public bool IsJoinedToRoom()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ViversePlay_Matchmaking_IsJoinedToRoom();
#else
            return _currentRoom != null;
#endif
        }

        public bool IsConnected()
        {
            return _isConnected;
        }

        public void SendRequest(string requestType, string jsonPayload = "{}", Action<string> callback = null)
        {
            int requestId = ++_requestCounter;
            if (callback != null) _pendingRequests[requestId] = callback;

            string inner = jsonPayload.Length > 2 ? jsonPayload.Substring(1, jsonPayload.Length - 2) : "";
            string sessionField = $"\"session_id\":\"{_sessionId ?? ""}\"";
            string json;
            if (string.IsNullOrEmpty(inner))
                json = $"{{\"request_id\":{requestId},\"request_type\":\"{requestType}\",\"app_id\":\"{_appId ?? ""}\",{sessionField}}}";
            else
                json = $"{{\"request_id\":{requestId},\"request_type\":\"{requestType}\",\"app_id\":\"{_appId ?? ""}\",{sessionField},{inner}}}";

#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Matchmaking_SendRequest(json);
#else
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                var errorJson = "{\"success\":false,\"message\":\"Not connected\"}";
                if (callback != null)
                {
                    _pendingRequests.Remove(requestId);
                    callback(errorJson);
                }
                return;
            }

            if (_debugMode)
            {
                _ws.SendText(json);
            }
            else
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                byte[] encrypted = XorEncrypt(data);
                _ws.Send(encrypted);
            }
#endif
        }

        public void OnJSMessage(string json)
        {
            ProcessIncomingMessage(json);
        }


#if !UNITY_WEBGL || UNITY_EDITOR
        private void HandleEditorMessage(byte[] bytes)
        {
            if (bytes.Length == 1 && bytes[0] == 0x00) return; // heartbeat

            string json;
            if (_debugMode)
            {
                json = System.Text.Encoding.UTF8.GetString(bytes);
            }
            else
            {
                byte[] decrypted = XorEncrypt(bytes);
                json = System.Text.Encoding.UTF8.GetString(decrypted);
            }

            ProcessIncomingMessage(json);
        }

        private static byte[] XorEncrypt(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ _xorKey[i % _xorKey.Length]);
            return result;
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
        }

        private void StopHeartbeat()
        {
            if (_heartbeatCoroutine != null)
            {
                StopCoroutine(_heartbeatCoroutine);
                _heartbeatCoroutine = null;
            }
        }

        private System.Collections.IEnumerator HeartbeatLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(HEARTBEAT_INTERVAL);
                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    _ws.Send(new byte[] { 0x00 });
                }
            }
        }
#endif

        private void ProcessIncomingMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            if (json.Contains("\"event\":"))
            {
                var payload = JsonUtility.FromJson<EventPayload>(json);
                DispatchEvent(payload.@event, json);
                return;
            }

            var response = JsonUtility.FromJson<MatchResponse>(json);
            if (response.request_id > 0 && _pendingRequests.ContainsKey(response.request_id))
            {
                var cb = _pendingRequests[response.request_id];
                _pendingRequests.Remove(response.request_id);
                cb?.Invoke(json);
            }


#if !UNITY_WEBGL || UNITY_EDITOR
            DispatchByRequestType(response, json);
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void DispatchByRequestType(MatchResponse response, string json)
        {
            if (string.IsNullOrEmpty(response.request_type)) return;

            switch (response.request_type)
            {
                case "createRoom":
                case "joinRoom":
                case "CreateRoom":
                case "JoinRoom":
                    ParseAndSetCurrentRoom(json);
                    OnJoinRoom?.Invoke(json);
                    OnStateChange?.Invoke("JoinedRoom");
                    break;
                case "leaveRoom":
                case "LeaveRoom":
                    _currentRoom = null;
                    OnStateChange?.Invoke("JoinedLobby");
                    break;
                case "broadcastRoomList":
                    OnRoomListUpdate?.Invoke(json);
                    break;
                case "getRoomActors":
                    OnRoomActorChange?.Invoke(json);
                    break;
                case "startGameNotify":
                    OnGameStartNotify?.Invoke();
                    break;
                case "matchingTimeout":
                    OnMatchingTimeout?.Invoke();
                    break;
                case "roomClosed":
                    _currentRoom = null;
                    OnRoomClosed?.Invoke();
                    break;
            }
        }

        private void ParseAndSetCurrentRoom(string json)
        {
            try
            {
                var dict = MiniJson.Deserialize(json) as Dictionary<string, object>;
                if (dict == null) return;

                // Server nests room data under "room", "data", or at top level
                var roomDict = dict;
                if (dict.ContainsKey("room") && dict["room"] is Dictionary<string, object> roomObj)
                    roomDict = roomObj;
                else if (dict.ContainsKey("data"))
                {
                    if (dict["data"] is Dictionary<string, object> dataDict)
                        roomDict = dataDict;
                    else if (dict["data"] is string dataStr && !string.IsNullOrEmpty(dataStr))
                    {
                        var parsed = MiniJson.Deserialize(dataStr) as Dictionary<string, object>;
                        if (parsed != null) roomDict = parsed;
                    }
                }

                _currentRoom = new Room();
                if (roomDict.ContainsKey("id")) _currentRoom.id = roomDict["id"].ToString();
                else if (roomDict.ContainsKey("room_id")) _currentRoom.id = roomDict["room_id"].ToString();
                if (roomDict.ContainsKey("name")) _currentRoom.name = roomDict["name"].ToString();
                else if (roomDict.ContainsKey("room_name")) _currentRoom.name = roomDict["room_name"].ToString();
                if (roomDict.ContainsKey("max_players")) _currentRoom.max_players = Convert.ToInt32(roomDict["max_players"]);
                if (roomDict.ContainsKey("game_session")) _currentRoom.game_session = roomDict["game_session"].ToString();

                Debug.Log($"[Matchmaking] Room set: id={_currentRoom.id}, name={_currentRoom.name}");
            }
            catch (Exception ex)
            {
                // Even if parsing fails, mark as in room so IsJoinedToRoom() works
                if (_currentRoom == null) _currentRoom = new Room();
                Debug.LogWarning($"[Matchmaking] Room parse partial: {ex.Message}");
            }
        }
#endif

        private void DispatchEvent(string eventName, string json)
        {
            switch (eventName)
            {
                case "onConnect": _isConnected = true; OnConnect?.Invoke(); break;
                case "onDisconnect": _isConnected = false; OnDisconnect?.Invoke(); break;
                case "onError": OnError?.Invoke(json); break;
                case "stateChange": OnStateChange?.Invoke(json); break;
                case "onRoomListUpdate": OnRoomListUpdate?.Invoke(json); break;
                case "onJoinRoom":
#if !UNITY_WEBGL || UNITY_EDITOR
                    ParseAndSetCurrentRoom(json);
#endif
                    OnJoinRoom?.Invoke(json);
                    break;
                case "onJoinedLobby": OnJoinedLobby?.Invoke(); break;
                case "onRoomActorChange": OnRoomActorChange?.Invoke(json); break;
                case "onRoomClosed":
#if !UNITY_WEBGL || UNITY_EDITOR
                    _currentRoom = null;
#endif
                    OnRoomClosed?.Invoke();
                    break;
                case "onGameStartNotify": OnGameStartNotify?.Invoke(); break;
                case "onMatchingTimeout": OnMatchingTimeout?.Invoke(); break;
            }
        }
    }
}
