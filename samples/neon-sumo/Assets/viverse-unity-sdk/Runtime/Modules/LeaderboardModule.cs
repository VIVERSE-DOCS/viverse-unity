using System;
using System.Collections.Generic;

namespace ViverseSDK
{
    /// <summary>
    /// Leaderboard score synchronization module.
    /// </summary>
    public class LeaderboardModule
    {
        private readonly MultiplayerClient _sdk;
        private const string ModuleType = "leaderboard";

        public event Action<string> OnLeaderboardUpdate;

        public LeaderboardModule(MultiplayerClient sdk)
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

            string eventName = data.ContainsKey("event") ? data["event"].ToString() : "";
            if (eventName == "update_record")
            {
                OnLeaderboardUpdate?.Invoke(MiniJson.Serialize(data));
            }
        }
#endif

        public void LeaderboardUpdate(int score)
        {
            var payload = $"{{\"source\":\"client\",\"messageType\":\"bot\",\"type\":\"{ModuleType}\",\"event\":\"update_record\",\"user_id\":\"{_sdk.PeerId}\",\"score\":{score}}}";
            _sdk.Send(payload);
        }

        internal void TriggerOnLeaderboardUpdate(string data) => OnLeaderboardUpdate?.Invoke(data);
    }
}
