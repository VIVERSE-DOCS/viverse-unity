using System;
using System.Collections.Generic;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Sends ActionSync competition envelopes without SDK string interpolation.
    /// Nested action_msg JSON is escaped by MiniJson.
    /// </summary>
    internal static class NeonSumoActionSyncSend
    {
        private const string ModuleType = "action_sync";
        private const string CompetitionEvent = "competition";

        public static void Competition(MultiplayerClient client, string actionName, string actionMsg, string actionId = null)
        {
            if (client == null)
                return;

            if (string.IsNullOrEmpty(actionId))
                actionId = Guid.NewGuid().ToString();

            var payload = new Dictionary<string, object>
            {
                { "source", "client" },
                { "messageType", "bot" },
                { "type", ModuleType },
                { "event", CompetitionEvent },
                { "user_id", NeonSumoUserId.Normalize(client.PeerId) },
                { "action_name", actionName ?? string.Empty },
                { "action_msg", actionMsg ?? string.Empty },
                { "action_id", actionId },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds() }
            };

            client.Send(MiniJson.Serialize(payload));
        }
    }
}
