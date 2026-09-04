using System;
using System.Collections.Generic;

namespace ViverseSDK
{
    /// <summary>
    /// Network position synchronization module.
    /// </summary>
    public class NetworkSyncModule
    {
        private readonly MultiplayerClient _sdk;
        private const string ModuleType = "network_sync";

        public event Action<string> OnNotifyPositionUpdate;
        public event Action<string> OnNotifyRemove;

        public NetworkSyncModule(MultiplayerClient sdk)
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

            string userId = data.ContainsKey("user_id") ? data["user_id"].ToString() : "";
            if (userId == _sdk.PeerId) return; // Ignore self

            string eventName = data.ContainsKey("event") ? data["event"].ToString() : "";
            switch (eventName)
            {
                case "notify_position":
                    OnNotifyPositionUpdate?.Invoke(MiniJson.Serialize(data));
                    break;
                case "notify_remove":
                    OnNotifyRemove?.Invoke(MiniJson.Serialize(data));
                    break;
            }
        }
#endif

        public void UpdateMyPosition(string dataJson)
        {
            long timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"notify_position\",\"user_id\":\"{_sdk.PeerId}\",\"data\":{dataJson},\"timestamp\":{timestamp}}}";
            _sdk.Send(payload);
        }

        public void UpdateEntityPosition(string entityId, string dataJson)
        {
            long timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"notify_position\",\"user_id\":\"{_sdk.PeerId}\",\"entity_id\":\"{entityId}\",\"data\":{dataJson},\"timestamp\":{timestamp}}}";
            _sdk.Send(payload);
        }

        internal void TriggerOnNotifyPositionUpdate(string data) => OnNotifyPositionUpdate?.Invoke(data);
        internal void TriggerOnNotifyRemove(string data) => OnNotifyRemove?.Invoke(data);
    }
}
