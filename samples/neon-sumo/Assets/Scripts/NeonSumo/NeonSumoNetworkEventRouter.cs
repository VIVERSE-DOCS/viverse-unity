using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViverseSDK;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Actions where the sender fully applies the effect locally before broadcasting.
    /// Only include actions here if the sender does NOT rely on receiving its own event.
    /// Otherwise the sender must process its own event.
    /// </summary>
    internal static class IgnoreSelfActions
    {
        private static readonly HashSet<string> Set = new HashSet<string>(StringComparer.Ordinal)
        {
            NeonSumoNetEvents.Eliminate,
            NeonSumoNetEvents.RingDrop,
            NeonSumoNetEvents.RampsRetract,
            NeonSumoNetEvents.UnlockControls,
            NeonSumoNetEvents.RoundWin,
            NeonSumoNetEvents.PlayerColor,
            NeonSumoNetEvents.SpawnAck,
            NeonSumoNetEvents.StartReadyPhase,
            NeonSumoNetEvents.UpdateReadyPhase,
            NeonSumoNetEvents.CancelReadyPhase,
            NeonSumoNetEvents.StartIntroPhase,
        };

        public static bool Contains(string actionName) => Set.Contains(actionName);
    }

    /// <summary>
    /// Parsed ActionSync event. Passed to handlers instead of raw params.
    /// </summary>
    public readonly struct ParsedActionSyncEvent
    {
        public string ActionName { get; }
        public string UserId { get; }
        public string ActionMsg { get; }
        public IReadOnlyDictionary<string, object> CompData { get; }

        public ParsedActionSyncEvent(
            string actionName,
            string userId,
            string actionMsg,
            IReadOnlyDictionary<string, object> compData)
        {
            ActionName = actionName;
            UserId = userId;
            ActionMsg = actionMsg;
            CompData = compData;
        }
    }

    /// <summary>
    /// Centralizes ActionSync subscription, payload parsing, and dispatch.
    /// Use with NeonSumoNetEvents constants.
    /// </summary>
    public sealed class NeonSumoNetworkEventRouter : IDisposable
    {
        private const int MaxSeenActionIds = 256;

        private MultiplayerClient _client;
        private Action<ParsedActionSyncEvent> _handler;
        private string _localPlayerId;
        private readonly HashSet<string> _seenActionIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenActionIdOrder = new Queue<string>();

        /// <summary>Setup router. Call from GameManager.SetupSDKCallbacks.</summary>
        public void Setup(MultiplayerClient client, string localPlayerId, Action<ParsedActionSyncEvent> handler)
        {
            Teardown();
            _client = client;
            _localPlayerId = NeonSumoUserId.Normalize(localPlayerId);
            _handler = handler;
            if (_client?.ActionSync != null)
            {
                _client.ActionSync.OnCompetition += HandleRawEvent;
            }
        }

        /// <summary>Unsubscribe. Call when disconnecting.</summary>
        public void Teardown()
        {
            if (_client?.ActionSync != null)
            {
                _client.ActionSync.OnCompetition -= HandleRawEvent;
            }
            _client = null;
            _handler = null;
            _localPlayerId = null;
            _seenActionIds.Clear();
            _seenActionIdOrder.Clear();
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose() => Teardown();

        /// <summary>Returns true if this event should be ignored (self-event, already applied locally).</summary>
        public bool ShouldIgnoreSelfEvent(string actionName, string userId)
        {
            userId = NeonSumoUserId.Normalize(userId);
            if (string.IsNullOrEmpty(userId) || userId != _localPlayerId) return false;
            return IgnoreSelfActions.Contains(actionName);
        }

        private void HandleRawEvent(string data)
        {
            try
            {
                if (!TryParseEvent(data, out var evt, out var reason))
                {
                    DebugLogger.LogWarning($"[NeonSumo] Ignoring ActionSync payload: {reason}");
                    return;
                }

                if (ShouldIgnoreSelfEvent(evt.ActionName, evt.UserId))
                    return;

                if (TryGetActionId(evt.CompData, out var actionId) && !TryMarkActionId(actionId))
                    return;

                _handler?.Invoke(evt);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"[NeonSumo] NetworkEventRouter error: {ex}");
            }
        }

        private static bool TryParseEvent(object data, out ParsedActionSyncEvent evt, out string failureReason)
        {
            evt = default;
            failureReason = null;

            if (!TryNormalizePayload(data, out var compData))
            {
                failureReason = $"Unsupported payload type: {data?.GetType()}";
                return false;
            }

            if (!TryExtractCompetitionData(compData, out var compDict))
            {
                failureReason = "Missing competition data or action_name/user_id";
                return false;
            }

            string userId = NeonSumoUserId.Normalize(GetString(compDict, "user_id"));
            string successor = NeonSumoUserId.Normalize(GetString(compDict, "successor"));
            string actionName = GetString(compDict, "action_name");
            string actionMsg = GetString(compDict, "action_msg");

            if (string.IsNullOrEmpty(userId)) userId = successor;
            if (string.IsNullOrEmpty(actionName))
            {
                failureReason = "Missing action_name";
                return false;
            }

            evt = new ParsedActionSyncEvent(actionName, userId, actionMsg, compDict);
            return true;
        }

        private bool TryMarkActionId(string actionId)
        {
            if (!_seenActionIds.Add(actionId))
                return false;

            _seenActionIdOrder.Enqueue(actionId);
            while (_seenActionIdOrder.Count > MaxSeenActionIds)
            {
                string oldest = _seenActionIdOrder.Dequeue();
                _seenActionIds.Remove(oldest);
            }

            return true;
        }

        private static bool TryGetActionId(IReadOnlyDictionary<string, object> dict, out string actionId)
        {
            actionId = GetString(dict, "action_id");
            return !string.IsNullOrEmpty(actionId);
        }

        private static bool TryExtractCompetitionData(
            Dictionary<string, object> root,
            out Dictionary<string, object> competitionData)
        {
            competitionData = null;

            if (root.TryGetValue("competition", out var comp))
            {
                if (comp is Dictionary<string, object> d)
                {
                    competitionData = d;
                    return true;
                }
                if (comp is JObject jo)
                {
                    competitionData = jo.ToObject<Dictionary<string, object>>();
                    return competitionData != null;
                }
                if (comp is JToken jt)
                {
                    competitionData = jt.ToObject<Dictionary<string, object>>();
                    return competitionData != null;
                }
            }

            if (root.ContainsKey("action_name") || root.ContainsKey("user_id"))
            {
                competitionData = root;
                return true;
            }

            return false;
        }

        private static string GetString(IReadOnlyDictionary<string, object> dict, string key)
        {
            return dict != null && dict.TryGetValue(key, out var value)
                ? value?.ToString()
                : null;
        }

        private static bool TryNormalizePayload(object data, out Dictionary<string, object> dict)
        {
            dict = null;
            if (data is Dictionary<string, object> d) { dict = d; return true; }
            if (data is string s)
            {
                if (string.IsNullOrEmpty(s))
                    return false;
                try
                {
                    dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(s);
                    return dict != null;
                }
                catch
                {
                    return false;
                }
            }
            if (data is JObject jo) { dict = jo.ToObject<Dictionary<string, object>>(); return dict != null; }
            if (data is JToken jt) { dict = jt.ToObject<Dictionary<string, object>>(); return dict != null; }
            return false;
        }
    }
}
