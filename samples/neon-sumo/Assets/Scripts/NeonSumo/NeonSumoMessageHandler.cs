using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeonSumo
{
    /// <summary>
    /// Parses raw OnMessage payloads and dispatches to handlers.
    /// Resilient to unknown message types (ignores + logs once).
    /// </summary>
    public sealed class NeonSumoMessageHandler
    {
        private readonly NeonSumoMessageContext _ctx;
        private readonly HashSet<string> _loggedUnknownActions = new HashSet<string>();
        private readonly Dictionary<string, Action<IReadOnlyDictionary<string, object>>> _handlers;

        public NeonSumoMessageHandler(NeonSumoMessageContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));

            _handlers = new Dictionary<string, Action<IReadOnlyDictionary<string, object>>>
            {
                [NeonSumoMessageActions.PlayerInput] = HandlePlayerInput,
            };
        }

        public void Handle(object data)
        {
            try
            {
                if (!TryParseToDict(data, out var dict))
                    return;

                if (!TryGetString(dict, "action", out var action))
                    return;

                if (_handlers.TryGetValue(action, out var handler))
                {
                    handler(dict);
                    return;
                }

                LogUnknownActionOnce(action);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"[NeonSumo] Message handling error: {ex.Message}");
            }
        }

        private void HandlePlayerInput(IReadOnlyDictionary<string, object> dict)
        {
            if (_ctx.IsMasterClient?.Invoke() != true)
                return;

            if (!TryGetString(dict, "userId", out var userId))
                return;

            userId = NeonSumoUserId.Normalize(userId);
            if (string.IsNullOrEmpty(userId))
                return;

            float moveX = GetFloat(dict, "moveX");
            float moveZ = GetFloat(dict, "moveZ");
            bool boost = GetBool(dict, "boost");
            double timestamp = GetDouble(dict, "timestamp");

            _ctx.ReceivePlayerInput?.Invoke(userId, moveX, moveZ, boost, timestamp);
        }

        private void LogUnknownActionOnce(string action)
        {
            if (_loggedUnknownActions.Add(action))
            {
                DebugLogger.Log($"[NeonSumo] Unknown message action (ignoring): {action}");
            }
        }

        private static bool TryParseToDict(object data, out Dictionary<string, object> dict)
        {
            dict = null;

            switch (data)
            {
                case Dictionary<string, object> d:
                    dict = d;
                    return true;

                case string s:
                    dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(s);
                    return dict != null;

                case JObject j:
                    dict = j.ToObject<Dictionary<string, object>>();
                    return dict != null;

                default:
                    if (data == null)
                        return false;
                    try
                    {
                        var json = JsonConvert.SerializeObject(data);
                        dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        return dict != null;
                    }
                    catch
                    {
                        return false;
                    }
            }
        }

        private static bool TryGetString(IReadOnlyDictionary<string, object> dict, string key, out string value)
        {
            value = null;

            if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
                return false;

            value = raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static float GetFloat(IReadOnlyDictionary<string, object> dict, string key, float fallback = 0f)
        {
            if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
                return fallback;

            try { return Convert.ToSingle(raw); }
            catch { return fallback; }
        }

        private static double GetDouble(IReadOnlyDictionary<string, object> dict, string key, double fallback = 0d)
        {
            if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
                return fallback;

            try { return Convert.ToDouble(raw); }
            catch { return fallback; }
        }

        private static bool GetBool(IReadOnlyDictionary<string, object> dict, string key, bool fallback = false)
        {
            if (dict == null || !dict.TryGetValue(key, out var raw) || raw == null)
                return fallback;

            try { return Convert.ToBoolean(raw); }
            catch { return fallback; }
        }
    }

    /// <summary>Context for NeonSumoMessageHandler. GameManager populates this.</summary>
    public sealed class NeonSumoMessageContext
    {
        public Action<string, float, float, bool, double> ReceivePlayerInput { get; }
        public Func<bool> IsMasterClient { get; }

        public NeonSumoMessageContext(
            Action<string, float, float, bool, double> receivePlayerInput,
            Func<bool> isMasterClient)
        {
            ReceivePlayerInput = receivePlayerInput;
            IsMasterClient = isMasterClient;
        }
    }
}
