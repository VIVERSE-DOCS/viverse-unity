using System;
using System.Collections.Generic;

namespace ViverseSDK
{
    /// <summary>
    /// General messaging module — send/receive arbitrary data between peers.
    /// </summary>
    public class GeneralModule
    {
        private readonly MultiplayerClient _sdk;
        private const string ModuleType = "general";

        public event Action<string> OnMessage;
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;

        public GeneralModule(MultiplayerClient sdk)
        {
            _sdk = sdk;
#if !UNITY_WEBGL || UNITY_EDITOR
            _sdk.OnModuleMessage += HandleModuleMessage;
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void HandleModuleMessage(string moduleType, Dictionary<string, object> data)
        {
            if (moduleType != ModuleType && moduleType != "game") return;

            string eventName = data.ContainsKey("event") ? data["event"].ToString() : "";
            string userId = data.ContainsKey("user_id") ? data["user_id"].ToString() : "";

            switch (eventName)
            {
                case "general":
                    if (userId != _sdk.PeerId)
                        OnMessage?.Invoke(data.ContainsKey("data") ? MiniJson.Serialize(data["data"]) : "");
                    break;
                case "client_connected":
                    if (userId != _sdk.PeerId)
                        OnClientConnected?.Invoke(userId);
                    break;
                case "client_disconnected":
                    if (userId != _sdk.PeerId)
                        OnClientDisconnected?.Invoke(userId);
                    break;
            }
        }
#endif

        public void SendMessage(string data)
        {
            var payload = $"{{\"source\":\"bot\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"general\",\"user_id\":\"{_sdk.PeerId}\",\"data\":{data}}}";
            _sdk.Send(payload);
        }

        internal void TriggerOnMessage(string data) => OnMessage?.Invoke(data);
        internal void TriggerOnClientConnected(string userId) => OnClientConnected?.Invoke(userId);
        internal void TriggerOnClientDisconnected(string userId) => OnClientDisconnected?.Invoke(userId);
    }
}
