using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using Viverse.NativeWebSocket;
#endif

namespace ViverseSDK
{
    public class MultiplayerClient : MonoBehaviour
    {
        public static MultiplayerClient Instance { get; private set; }

        // DllImport declarations for WebGL
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_Initialize(int requestId, string roomId, string appId, string userSessionId, string gameObjectName, string callbackMethodName);

        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_Init(int requestId, string jsonOptions);

        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_Send(string data);

        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_Disconnect();

        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_SetMaster(bool isMaster);

        [DllImport("__Internal")]
        private static extern bool ViversePlay_Multiplayer_IsMasterUser();

        [DllImport("__Internal")]
        private static extern void ViversePlay_Multiplayer_GetRoomInfo(int requestId);
#endif

        // Modules
        public GameModule Game { get; private set; }
        public NetworkSyncModule NetworkSync { get; private set; }
        public ActionSyncModule ActionSync { get; private set; }
        public LeaderboardModule Leaderboard { get; private set; }
        public LambdaModule Lambda { get; private set; }
        public GeneralModule General { get; private set; }

        // Events
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action<string> OnMessage;

        // Properties
        public string RoomId { get; private set; }
        public string AppId { get; private set; }
        public string PeerId { get; private set; }

        private bool _isMaster;
        private bool _initialized;
        private int _requestCounter;
        private Dictionary<int, TaskCompletionSource<string>> _pendingRequests = new Dictionary<int, TaskCompletionSource<string>>();

#if !UNITY_WEBGL || UNITY_EDITOR
        private WebSocket _ws;
        private const string DEFAULT_PROXY_URL = "wss://broadcasting-gateway-gaming.vrprod.viveport.com";
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
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                await _ws.Close();
            }
        }
#else
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
#endif

        /// <summary>
        /// Connect to the multiplayer proxy/service. Must have a roomId from MatchmakingClient.
        /// </summary>
        public async Task Initialize(string roomId, string appId, string userSessionId = null)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("roomId is required");
            if (string.IsNullOrEmpty(appId)) throw new ArgumentException("appId is required");

            RoomId = roomId;
            AppId = appId;
            PeerId = string.IsNullOrEmpty(userSessionId) ? Guid.NewGuid().ToString() : userSessionId;

#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            int requestId = ++_requestCounter;
            _pendingRequests[requestId] = tcs;
            ViversePlay_Multiplayer_Initialize(requestId, roomId, appId, PeerId, gameObject.name, nameof(ReceiveMessageFromJS));
            await tcs.Task;
#else
            var url = $"{DEFAULT_PROXY_URL}/api/webrtcproxy-service/v1/ws?roomId={roomId}&peerId={PeerId}&pod=0";
            Debug.Log($"[Multiplayer] Connecting to proxy: {url}");

            _ws = new WebSocket(url);

            _ws.OnOpen += () =>
            {
                Debug.Log("[Multiplayer] Proxy connected");
                OnConnected?.Invoke();
            };

            _ws.OnClose += (code) =>
            {
                Debug.Log($"[Multiplayer] Proxy disconnected: {code}");
                _initialized = false;
                OnDisconnected?.Invoke();
            };

            _ws.OnError += (error) =>
            {
                Debug.LogError($"[Multiplayer] Proxy error: {error}");
            };

            _ws.OnMessage += (bytes) =>
            {
                var message = System.Text.Encoding.UTF8.GetString(bytes);
                HandleProxyMessage(message);
            };

#pragma warning disable CS4014
            _ws.Connect();
#pragma warning restore CS4014
            await Task.Yield();
#endif

            // Initialize modules
            General = new GeneralModule(this);
            Game = new GameModule(this);
            NetworkSync = new NetworkSyncModule(this);
            ActionSync = new ActionSyncModule(this);
            Leaderboard = new LeaderboardModule(this);
            Lambda = new LambdaModule(this);
        }

        /// <summary>
        /// Initialize modules and create game room (call after Initialize + OnConnected).
        /// </summary>
        public async Task<string> Init(MultiplayerInitOptions options = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            int requestId = ++_requestCounter;
            _pendingRequests[requestId] = tcs;
            string jsonOptions = options != null ? JsonUtility.ToJson(options) : "";
            ViversePlay_Multiplayer_Init(requestId, jsonOptions);

            if (options?.modules?.lambda?.enabled == true)
                Lambda?.SetEnabled(true);

            _initialized = true;
            return await tcs.Task;
#else
            // Editor path: game room creation via REST would go here
            // For now, mark initialized (proxy handles bot setup)
            if (options?.modules?.lambda?.enabled == true)
                Lambda?.SetEnabled(true);

            _initialized = true;
            await Task.CompletedTask;
            return $"{{\"session_id\":\"{PeerId}\",\"room_id\":\"{RoomId}\",\"app_id\":\"{AppId}\"}}";
#endif
        }

        /// <summary>
        /// Send raw data through the data channel / proxy.
        /// </summary>
        public void Send(string data)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Multiplayer_Send(data);
#else
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                _ws.SendText(data);
            }
#endif
        }

        public void Disconnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Multiplayer_Disconnect();
            _initialized = false;
#else
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
#pragma warning disable CS4014
                _ws.Close();
#pragma warning restore CS4014
            }
            _initialized = false;
#endif
        }

        public void SetMaster(bool isMaster)
        {
            _isMaster = isMaster;
#if UNITY_WEBGL && !UNITY_EDITOR
            ViversePlay_Multiplayer_SetMaster(isMaster);
#endif
        }

        public bool IsMasterUser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ViversePlay_Multiplayer_IsMasterUser();
#else
            return _isMaster;
#endif
        }

        public async Task<string> GetRoomInfo()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var tcs = new TaskCompletionSource<string>();
            int requestId = ++_requestCounter;
            _pendingRequests[requestId] = tcs;
            ViversePlay_Multiplayer_GetRoomInfo(requestId);
            return await tcs.Task;
#else
            // Editor path: send via module protocol
            if (Game != null)
                return await Game.GetRoomInfoInternal();
            throw new InvalidOperationException("Game module not initialized");
#endif
        }

        public bool IsInitialized() => _initialized;

        // ─── Internal: Generic event registration for modules (Editor path) ───

        internal event Action<string, Dictionary<string, object>> OnModuleMessage;

        internal void TriggerModuleMessage(string moduleType, Dictionary<string, object> data)
        {
            OnModuleMessage?.Invoke(moduleType, data);
        }

        // ─── Message handling ───

        /// <summary>Called by jslib SendMessage in WebGL path</summary>
        public void ReceiveMessageFromJS(string json)
        {
            try
            {
                if (json.Contains("\"requestId\""))
                {
                    // Response to a pending request
                    var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
                    if (parsed == null) return;

                    // Lambda bridge response
                    if (parsed.ContainsKey("lambdaBridgeId"))
                    {
                        int bridgeId = Convert.ToInt32(parsed["lambdaBridgeId"]);
                        bool success = parsed.ContainsKey("success") && Convert.ToBoolean(parsed["success"]);
                        if (success)
                        {
                            string jobData = parsed.ContainsKey("jobData") ? parsed["jobData"].ToString() : "{}";
                            LambdaBridge.Resolve(bridgeId, jobData);
                        }
                        else
                        {
                            string errMsg = parsed.ContainsKey("message") ? parsed["message"].ToString() : "Lambda error";
                            LambdaBridge.Reject(bridgeId, errMsg);
                        }
                        return;
                    }

                    int requestId = Convert.ToInt32(parsed["requestId"]);
                    if (_pendingRequests.ContainsKey(requestId))
                    {
                        bool success = parsed.ContainsKey("success") && Convert.ToBoolean(parsed["success"]);
                        if (success)
                        {
                            string data = parsed.ContainsKey("data") ? parsed["data"].ToString() :
                                          parsed.ContainsKey("room_data") ? MiniJson.Serialize(parsed["room_data"]) : "{}";
                            _pendingRequests[requestId].TrySetResult(data);
                        }
                        else
                        {
                            string msg = parsed.ContainsKey("message") ? parsed["message"].ToString() : "Error";
                            _pendingRequests[requestId].TrySetException(new Exception(msg));
                        }
                        _pendingRequests.Remove(requestId);
                    }
                }
                else if (json.Contains("\"eventName\""))
                {
                    // Event from JS SDK
                    var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
                    if (parsed == null) return;

                    string eventName = parsed.ContainsKey("eventName") ? parsed["eventName"].ToString() : "";
                    string dataJson = parsed.ContainsKey("data") && parsed["data"] != null ? MiniJson.Serialize(parsed["data"]) : null;

                    DispatchEvent(eventName, dataJson);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Multiplayer] Error processing JS message: {ex.Message}");
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void HandleProxyMessage(string json)
        {
            try
            {
                var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
                if (parsed == null) return;

                // Module messages: { type, event, source: "bot", ... }
                if (parsed.ContainsKey("type") && parsed.ContainsKey("event") &&
                    parsed.ContainsKey("source") && parsed["source"].ToString() == "bot")
                {
                    string moduleType = parsed["type"].ToString();
                    string eventName = parsed["event"].ToString();

                    // Route to modules
                    OnModuleMessage?.Invoke(moduleType, parsed);

                    // Also dispatch client-level events
                    if (moduleType == "game" && eventName == "client_connected")
                    {
                        string userId = parsed.ContainsKey("user_id") ? parsed["user_id"].ToString() : "";
                        if (userId != PeerId)
                            OnClientConnected?.Invoke(userId);
                    }
                    else if (moduleType == "game" && eventName == "client_disconnected")
                    {
                        string userId = parsed.ContainsKey("user_id") ? parsed["user_id"].ToString() : "";
                        if (userId != PeerId)
                            OnClientDisconnected?.Invoke(userId);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Multiplayer] Error parsing proxy message: {ex.Message}");
            }
        }
#endif

        private void DispatchEvent(string eventName, string dataJson)
        {
            switch (eventName)
            {
                case "onConnected": OnConnected?.Invoke(); break;
                case "onDisconnected": OnDisconnected?.Invoke(); break;
                case "onClientConnected": OnClientConnected?.Invoke(dataJson); break;
                case "onClientDisconnected": OnClientDisconnected?.Invoke(dataJson); break;
                case "onMessage": OnMessage?.Invoke(dataJson); General?.TriggerOnMessage(dataJson); break;

                // NetworkSync
                case "networksync/onNotifyPositionUpdate": NetworkSync?.TriggerOnNotifyPositionUpdate(dataJson); break;
                case "networksync/onNotifyRemove": NetworkSync?.TriggerOnNotifyRemove(dataJson); break;

                // ActionSync
                case "actionsync/onCompetition": ActionSync?.TriggerOnCompetition(dataJson); break;

                // Leaderboard
                case "leaderboard/onLeaderboardUpdate": Leaderboard?.TriggerOnLeaderboardUpdate(dataJson); break;

                // Game
                case "game/onBotLeave": Game?.TriggerEvent("onBotLeave", null); break;
                case "game/onMasterNotify": Game?.TriggerEvent("onMasterNotify", dataJson); break;
                case "game/onWaitForPlayer": Game?.TriggerEvent("onWaitForPlayer", dataJson); break;
                case "game/onPlayerAllReady": Game?.TriggerEvent("onPlayerAllReady", dataJson); break;
                case "game/onPlayerOverLimit": Game?.TriggerEvent("onPlayerOverLimit", null); break;
                case "game/onCountdownToStart": Game?.TriggerEvent("onCountdownToStart", dataJson); break;
                case "game/onCountdownToEnd": Game?.TriggerEvent("onCountdownToEnd", dataJson); break;
                case "game/onGameTimeUp": Game?.TriggerEvent("onGameTimeUp", null); break;
                case "game/onGameEnd": Game?.TriggerEvent("onGameEnd", null); break;
                case "game/onGameRestart": Game?.TriggerEvent("onGameRestart", null); break;
                case "game/onErrorNotify": Game?.TriggerEvent("onErrorNotify", dataJson); break;

                default:
                    Debug.LogWarning($"[Multiplayer] Unhandled event: {eventName}");
                    break;
            }
        }
    }
}
