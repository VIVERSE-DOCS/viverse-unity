using System;
using System.Collections.Generic;

namespace ViverseSDK
{
    /// <summary>
    /// Action/competition synchronization module.
    /// </summary>
    public class ActionSyncModule
    {
        private readonly MultiplayerClient _sdk;
        private const string ModuleType = "action_sync";

        public event Action<string> OnCompetition;

        public ActionSyncModule(MultiplayerClient sdk)
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
            if (eventName == "competition")
            {
                OnCompetition?.Invoke(MiniJson.Serialize(data));
            }
        }
#endif

        public void Competition(string actionName, string actionMsg, string actionId)
        {
            long timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"competition\",\"user_id\":\"{_sdk.PeerId}\",\"action_name\":\"{actionName}\",\"action_msg\":\"{actionMsg}\",\"action_id\":\"{actionId}\",\"timestamp\":{timestamp}}}";
            _sdk.Send(payload);
        }

        internal void TriggerOnCompetition(string data) => OnCompetition?.Invoke(data);
    }
}
