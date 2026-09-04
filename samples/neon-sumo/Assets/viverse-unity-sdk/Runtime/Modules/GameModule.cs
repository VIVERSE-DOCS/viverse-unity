using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ViverseSDK
{
    /// <summary>
    /// Game lifecycle module — ready, start, end, restart, room info.
    /// </summary>
    public class GameModule
    {
        private readonly MultiplayerClient _sdk;
        private const string ModuleType = "game";
#pragma warning disable CS0414
        private string _masterUser = "";
#pragma warning restore CS0414
        private Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new Dictionary<string, TaskCompletionSource<string>>();
        private int _requestIdCounter;

        public event Action<string> OnMasterNotify;
        public event Action<string> OnWaitForPlayer;
        public event Action<string> OnPlayerAllReady;
        public event Action OnPlayerOverLimit;
        public event Action<string> OnCountdownToStart;
        public event Action<string> OnCountdownToEnd;
        public event Action OnGameTimeUp;
        public event Action OnGameEnd;
        public event Action OnGameRestart;
        public event Action<string> OnErrorNotify;
        public event Action OnBotLeave;

        public GameModule(MultiplayerClient sdk)
        {
            _sdk = sdk;
#if !UNITY_WEBGL || UNITY_EDITOR
            _sdk.OnModuleMessage += HandleModuleMessage;
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void HandleModuleMessage(string moduleType, Dictionary<string, object> data)
        {
            if (moduleType != ModuleType) return;

            // Handle request-response
            if (data.ContainsKey("request_id") && _pendingRequests.TryGetValue(data["request_id"].ToString(), out var tcs))
            {
                tcs.TrySetResult(MiniJson.Serialize(data));
                _pendingRequests.Remove(data["request_id"].ToString());
                return;
            }

            string eventName = data.ContainsKey("event") ? data["event"].ToString() : "";

            switch (eventName)
            {
                case "bot_ready":
                    // Auto-ready if we're in the player list
                    if (data.ContainsKey("user_ids"))
                    {
                        string userIds = MiniJson.Serialize(data["user_ids"]);
                        if (userIds.Contains(_sdk.PeerId))
                            Ready();
                    }
                    break;
                case "bot_leave":
                    OnBotLeave?.Invoke();
                    break;
                case "master_notify":
                    if (data.ContainsKey("master_user"))
                    {
                        _masterUser = data["master_user"].ToString();
                        _sdk.SetMaster(_masterUser == _sdk.PeerId);
                        OnMasterNotify?.Invoke(MiniJson.Serialize(data));
                    }
                    break;
                case "wait_for_player":
                    OnWaitForPlayer?.Invoke(MiniJson.Serialize(data));
                    break;
                case "player_all_ready":
                    OnPlayerAllReady?.Invoke(MiniJson.Serialize(data));
                    break;
                case "player_over_limit":
                    OnPlayerOverLimit?.Invoke();
                    break;
                case "countdown_to_start":
                    OnCountdownToStart?.Invoke(MiniJson.Serialize(data));
                    break;
                case "countdown_to_end":
                    OnCountdownToEnd?.Invoke(MiniJson.Serialize(data));
                    break;
                case "game_time_up":
                    OnGameTimeUp?.Invoke();
                    break;
                case "game_end":
                    OnGameEnd?.Invoke();
                    break;
                case "game_restart":
                    _masterUser = "";
                    OnGameRestart?.Invoke();
                    break;
                case "error_notify":
                    OnErrorNotify?.Invoke(MiniJson.Serialize(data));
                    break;
            }
        }
#endif

        public void Ready()
        {
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"client_ready\",\"user_id\":\"{_sdk.PeerId}\"}}";
            _sdk.Send(payload);
        }

        public void TriggerGameStart()
        {
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"countdown_to_start\",\"user_id\":\"{_sdk.PeerId}\"}}";
            _sdk.Send(payload);
        }

        public void TriggerGameEnd()
        {
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"game_end\",\"user_id\":\"{_sdk.PeerId}\"}}";
            _sdk.Send(payload);
        }

        public void TriggerGameUpdate(int totalPlayer)
        {
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"game_update\",\"user_id\":\"{_sdk.PeerId}\",\"total_player\":{totalPlayer}}}";
            _sdk.Send(payload);
        }

        public void TriggerGameRestart()
        {
            _masterUser = "";
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"game_restart\",\"user_id\":\"{_sdk.PeerId}\"}}";
            _sdk.Send(payload);
        }

        /// <summary>Editor path: get room info via module protocol</summary>
        internal async Task<string> GetRoomInfoInternal()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            string requestId = $"game_req_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}_{++_requestIdCounter}";
            var tcs = new TaskCompletionSource<string>();
            var cts = new CancellationTokenSource(10000);
            cts.Token.Register(() =>
            {
                _pendingRequests.Remove(requestId);
                tcs.TrySetCanceled();
            }, false);

            _pendingRequests[requestId] = tcs;

            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"get_room_info\",\"request_id\":\"{requestId}\",\"user_id\":\"{_sdk.PeerId}\"}}";
            _sdk.Send(payload);

            try
            {
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("GetRoomInfo timed out after 10s");
            }
            finally
            {
                cts.Dispose();
            }
#else
            await Task.CompletedTask;
            throw new InvalidOperationException("Use GetRoomInfo() on WebGL path");
#endif
        }

        /// <summary>Called by MultiplayerClient.DispatchEvent for WebGL path</summary>
        internal void TriggerEvent(string eventName, string data)
        {
            switch (eventName)
            {
                case "onMasterNotify": OnMasterNotify?.Invoke(data); break;
                case "onWaitForPlayer": OnWaitForPlayer?.Invoke(data); break;
                case "onPlayerAllReady": OnPlayerAllReady?.Invoke(data); break;
                case "onPlayerOverLimit": OnPlayerOverLimit?.Invoke(); break;
                case "onCountdownToStart": OnCountdownToStart?.Invoke(data); break;
                case "onCountdownToEnd": OnCountdownToEnd?.Invoke(data); break;
                case "onGameTimeUp": OnGameTimeUp?.Invoke(); break;
                case "onGameEnd": OnGameEnd?.Invoke(); break;
                case "onGameRestart": OnGameRestart?.Invoke(); break;
                case "onErrorNotify": OnErrorNotify?.Invoke(data); break;
                case "onBotLeave": OnBotLeave?.Invoke(); break;
            }
        }
    }
}
