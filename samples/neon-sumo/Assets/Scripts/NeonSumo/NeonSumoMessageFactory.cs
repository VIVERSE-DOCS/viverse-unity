using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Builds message payloads for NeonSumo network protocol. Centralizes schema for easier evolution.
    /// Uses <see cref="JObject"/> to avoid boxing primitives into <c>Dictionary&lt;string, object&gt;</c> on hot paths.
    /// </summary>
    internal static class NeonSumoMessageFactory
    {
        public static JObject CreatePlayerInput(
            string userId,
            float moveX,
            float moveZ,
            bool boost,
            long timestamp)
        {
            return new JObject
            {
                ["action"] = NeonSumoMessageActions.PlayerInput,
                ["userId"] = userId,
                ["moveX"] = moveX,
                ["moveZ"] = moveZ,
                ["boost"] = boost,
                ["timestamp"] = timestamp
            };
        }

        /// <summary>
        /// Transform payload for NetworkSync UpdateMyPosition / UpdateEntityPosition.
        /// Must be a JSON object (not a quoted string).
        /// </summary>
        public static string CreateNetworkSyncTransformJson(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity)
        {
            var data = new Dictionary<string, object>
            {
                { "x", position.x },
                { "y", position.y },
                { "z", position.z },
                { "rotY", rotation.eulerAngles.y },
                { "qx", rotation.x },
                { "qy", rotation.y },
                { "qz", rotation.z },
                { "qw", rotation.w },
                { "vx", velocity.x },
                { "vy", velocity.y },
                { "vz", velocity.z }
            };
            return MiniJson.Serialize(data);
        }
    }
}
