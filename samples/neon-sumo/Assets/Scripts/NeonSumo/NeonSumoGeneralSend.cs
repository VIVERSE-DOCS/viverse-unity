using System;
using System.Collections.Generic;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Sends General module messages as a MiniJson envelope.
    /// Caller serializes the data object first; do not wrap that JSON in extra quotes.
    /// </summary>
    internal static class NeonSumoGeneralSend
    {
        private const string ModuleType = "general";

        public static void SendJson(MultiplayerClient client, string jsonData)
        {
            if (client == null)
                return;

            object dataObj = string.IsNullOrEmpty(jsonData)
                ? new Dictionary<string, object>()
                : MiniJson.Deserialize(jsonData) ?? jsonData;

            var payload = new Dictionary<string, object>
            {
                { "source", "bot" },
                { "messageType", "bot" },
                { "type", ModuleType },
                { "event", "general" },
                { "user_id", NeonSumoUserId.Normalize(client.PeerId) },
                { "data", dataObj }
            };

            client.Send(MiniJson.Serialize(payload));
        }
    }
}
