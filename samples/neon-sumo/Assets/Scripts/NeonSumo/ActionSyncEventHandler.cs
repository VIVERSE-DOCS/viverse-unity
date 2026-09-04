using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Handles parsed ActionSync events. Receives a context from NeonSumoGameManager
    /// to avoid coupling and keep game logic in GameManager.
    /// </summary>
    public class ActionSyncEventHandler
    {
        private readonly ActionSyncEventContext _ctx;
        private readonly Dictionary<string, Action<ParsedActionSyncEvent>> _handlers;

        public ActionSyncEventHandler(ActionSyncEventContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));

            _handlers = new Dictionary<string, Action<ParsedActionSyncEvent>>
            {
                [NeonSumoNetEvents.Eliminate] = HandleEliminate,
                [NeonSumoNetEvents.RingDrop] = evt => HandleRingDrop(evt.ActionMsg),
                [NeonSumoNetEvents.PlayerSpawnSync] = HandlePlayerSpawnSync,
                [NeonSumoNetEvents.PlayerColor] = evt => HandlePlayerColor(evt.ActionMsg),
                [NeonSumoNetEvents.PlayerDisplayName] = evt => HandlePlayerDisplayName(evt),
                [NeonSumoNetEvents.SpawnAck] = evt => HandleSpawnAck(evt.ActionMsg, evt.UserId),
                [NeonSumoNetEvents.RampsRetract] = evt => HandleRampsRetract(evt.ActionMsg),
                [NeonSumoNetEvents.UnlockControls] = evt => HandleUnlockControls(evt.ActionMsg),
                [NeonSumoNetEvents.RoundWin] = evt => HandleRoundWin(evt.ActionMsg),
                [NeonSumoNetEvents.CameraShake] = evt => HandleCameraShake(evt.ActionMsg),
                [NeonSumoNetEvents.StartReadyPhase] = evt => HandleStartReadyPhase(evt.ActionMsg),
                [NeonSumoNetEvents.UpdateReadyPhase] = evt => HandleUpdateReadyPhase(evt.ActionMsg),
                [NeonSumoNetEvents.CancelReadyPhase] = _ =>
                {
                    _ctx.HideStartingIn?.Invoke();
                    _ctx.ResetManualReady?.Invoke();
                },
                [NeonSumoNetEvents.PlayerManualReady] = evt => _ctx.OnPlayerManualReady?.Invoke(evt.UserId),
                [NeonSumoNetEvents.StartIntroPhase] = _ =>
                {
                    DebugLogger.Log("[NeonSumo] start_intro_phase received — starting 3-2-1-Go");
                    _ctx.StartIntroPhaseIfNeeded?.Invoke();
                }
            };
        }

        public void Handle(ParsedActionSyncEvent evt)
        {
            if (evt.ActionName == null) return;

            try
            {
                DebugLogger.Log($"[NeonSumo] HandleParsedActionSyncEvent: {evt.ActionName} userId={evt.UserId}");

                if (string.IsNullOrEmpty(evt.ActionName))
                    return;

                if (_handlers.TryGetValue(evt.ActionName, out var handler))
                {
                    handler.Invoke(evt);
                }
                else
                {
                    DebugLogger.LogWarning($"[NeonSumo] Unhandled action sync event: {evt.ActionName}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"[NeonSumo] Error handling action sync event '{evt.ActionName}': {ex}");
            }
        }

        private void HandleEliminate(ParsedActionSyncEvent evt)
        {
            string eliminatedUserId = NeonSumoUserId.Normalize(evt.ActionMsg ?? evt.UserId);
            if (!string.IsNullOrEmpty(eliminatedUserId) && _ctx.ContainsPlayer?.Invoke(eliminatedUserId) == true)
            {
                _ctx.EliminatePlayer?.Invoke(eliminatedUserId);
            }
        }

        private void HandleRingDrop(string actionMsg)
        {
            bool parsed = int.TryParse(actionMsg, out int ringIndex);
            DebugLogger.Log($"[NeonSumo] ring_drop received. actionMsg='{actionMsg}' parsed={parsed} ringIndex={ringIndex} arena={(_ctx.Arena != null ? "SET" : "NULL")}");

            if (_ctx.Arena != null && parsed)
            {
                _ctx.Arena.ApplyRemoteRingDrop(ringIndex);
                return;
            }

            DebugLogger.LogWarning($"[NeonSumo] ring_drop ignored. arena={(_ctx.Arena != null ? "SET" : "NULL")} parsed={parsed} actionMsg='{actionMsg}'");
        }

        private void HandlePlayerSpawnSync(ParsedActionSyncEvent evt)
        {
            if (!TryDeserialize(evt.ActionMsg, out PlayerSpawnSyncMessage msg) ||
                msg == null ||
                string.IsNullOrEmpty(msg.user_id))
            {
                return;
            }

            string targetUserId = NeonSumoUserId.Normalize(msg.user_id);

            if (string.IsNullOrEmpty(targetUserId))
                return;

            if (targetUserId == _ctx.LocalPlayerId)
            {
                _ctx.ResetSpawnAckSent?.Invoke();
            }

            int spawnIndex = msg.spawn_index;
            Vector3 position = new Vector3(msg.x, msg.y, msg.z);
            Quaternion rotation = Quaternion.Euler(0f, msg.rotY, 0f);

            if (_ctx.ContainsPlayer?.Invoke(targetUserId) != true)
            {
                _ctx.StorePendingRampSpawn?.Invoke(targetUserId, position, rotation, spawnIndex);
                _ctx.EnsurePlayerPresent?.Invoke(targetUserId);
            }

            if (_ctx.ContainsPlayer?.Invoke(targetUserId) != true)
                return;

            _ctx.ApplyRampSpawnFromNetwork?.Invoke(targetUserId, position, rotation, spawnIndex);

            if (_ctx.HasAppliedColorFromSpawn?.Invoke(targetUserId) != true)
            {
                _ctx.ApplyPlayerColor?.Invoke(targetUserId, msg.color_index);
                _ctx.AddAppliedColorFromSpawn?.Invoke(targetUserId);
            }
        }

        private void HandlePlayerColor(string actionMsg)
        {
            if (!TryParseObject(actionMsg, out var payload))
                return;

            string targetUserId = NeonSumoUserId.Normalize(payload["user_id"]?.ToString());
            if (string.IsNullOrEmpty(targetUserId))
                return;

            int colorIndex = payload["color_index"]?.Value<int>() ?? 0;
            _ctx.ApplyPlayerColor?.Invoke(targetUserId, colorIndex);
        }

        private void HandlePlayerDisplayName(ParsedActionSyncEvent evt)
        {
            if (string.IsNullOrEmpty(evt.UserId))
                return;

            if (!TryParseObject(evt.ActionMsg, out var payload))
                return;

            string incoming = payload["display_name"]?.ToString();
            _ctx.RegisterPeerDisplayName?.Invoke(NeonSumoUserId.Normalize(evt.UserId), incoming);
        }

        private void HandleSpawnAck(string actionMsg, string fallbackUserId)
        {
            string ackUserId = NeonSumoUserId.Normalize(fallbackUserId);

            if (TryDeserialize(actionMsg, out SpawnAckMessage msg) && !string.IsNullOrEmpty(msg?.user_id))
            {
                ackUserId = NeonSumoUserId.Normalize(msg.user_id);
            }

            if (!string.IsNullOrEmpty(ackUserId))
            {
                _ctx.RegisterSpawnAck?.Invoke(ackUserId);
            }
        }

        private void HandleRampsRetract(string actionMsg)
        {
            long recvUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double? sendRealtime = null;
            long? sendUnixMs = null;

            if (TryParseObject(actionMsg, out var payload))
            {
                sendRealtime = payload["send_realtime"]?.Value<double>();
                sendUnixMs = payload["send_unix_ms"]?.Value<long>();
            }

            string driftInfo = sendUnixMs.HasValue
                ? $" send_to_recv_ms={recvUnixMs - sendUnixMs.Value}"
                : "";

            Debug.Log($"[RampTiming][ClientRecv] realtime={Time.realtimeSinceStartup:F3}s wall={DateTime.UtcNow:HH:mm:ss.fff} action=ramps_retract send_realtime={(sendRealtime.HasValue ? sendRealtime.Value.ToString("F3") : "n/a")} send_unix_ms={(sendUnixMs.HasValue ? sendUnixMs.Value.ToString() : "n/a")} recv_unix_ms={recvUnixMs}{driftInfo}");

            _ctx.TriggerRampRetract?.Invoke(false);
        }

        private void HandleUnlockControls(string actionMsg)
        {
            if (TryParseObject(actionMsg, out var payload))
            {
                string targetUserId = NeonSumoUserId.Normalize(payload["user_id"]?.ToString());
                bool unlockAll = payload["all"]?.Value<bool>() ?? false;

                if (!string.IsNullOrEmpty(targetUserId))
                {
                    var player = _ctx.GetPlayer?.Invoke(targetUserId);
                    if (player != null)
                    {
                        player.EnableControls();
                        return;
                    }
                }

                if (unlockAll)
                {
                    _ctx.UnlockControlsForAll?.Invoke(false);
                    return;
                }
            }

            _ctx.UnlockControlsForAll?.Invoke(false);
        }

        private void HandleRoundWin(string actionMsg)
        {
            if (_ctx.IsRoundEndingOrResults?.Invoke() == true)
                return;

            string winnerUserId = null;

            if (TryParseObject(actionMsg, out var payload))
            {
                winnerUserId = NeonSumoUserId.Normalize(payload["winner_user_id"]?.ToString());
            }

            _ctx.ApplyRoundWin?.Invoke(winnerUserId);
        }

        private void HandleCameraShake(string actionMsg)
        {
            if (!TryParseObject(actionMsg, out var payload))
                return;

            string targetUserId = NeonSumoUserId.Normalize(payload["target_user_id"]?.ToString());
            if (string.IsNullOrEmpty(targetUserId) || targetUserId != _ctx.LocalPlayerId)
                return;

            float strength = payload["strength"]?.Value<float>() ?? 0f;
            if (strength <= 0f)
                return;

            _ctx.ApplyCameraShake?.Invoke(strength);
        }

        private void HandleStartReadyPhase(string actionMsg)
        {
            if (TryParseObject(actionMsg, out var payload))
            {
                int seconds = payload["seconds"]?.Value<int>() ?? 15;
                _ctx.ShowStartingIn?.Invoke(seconds);
                return;
            }

            _ctx.ShowStartingIn?.Invoke(15);
        }

        private void HandleUpdateReadyPhase(string actionMsg)
        {
            if (!TryParseObject(actionMsg, out var payload))
                return;

            int seconds = payload["seconds"]?.Value<int>() ?? 0;
            _ctx.UpdateStartingIn?.Invoke(seconds);
        }

        private static bool TryDeserialize<T>(string json, out T result)
        {
            result = default;

            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                result = JsonConvert.DeserializeObject<T>(json);
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseObject(string json, out JObject obj)
        {
            obj = null;

            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                obj = JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Context passed to ActionSyncEventHandler. GameManager populates this with delegates.
    /// </summary>
    public sealed class ActionSyncEventContext
    {
        public string LocalPlayerId { get; set; }
        public NeonSumoArena Arena { get; set; }

        public Func<string, bool> ContainsPlayer { get; set; }
        public Action<string> EnsurePlayerPresent { get; set; }
        public Action<string> EliminatePlayer { get; set; }
        public Action ResetSpawnAckSent { get; set; }
        public Action<string, Vector3, Quaternion, int> StorePendingRampSpawn { get; set; }
        public Action<string, Vector3, Quaternion, int> ApplyRampSpawnFromNetwork { get; set; }
        public Func<string, bool> HasAppliedColorFromSpawn { get; set; }
        public Action<string, int> ApplyPlayerColor { get; set; }
        public Action<string> AddAppliedColorFromSpawn { get; set; }
        public Action<string> RegisterSpawnAck { get; set; }
        public Action<bool> TriggerRampRetract { get; set; }
        public Func<string, NeonSumoPlayer> GetPlayer { get; set; }
        public Action<bool> UnlockControlsForAll { get; set; }
        public Action<string> ApplyRoundWin { get; set; }
        public Func<bool> IsRoundEndingOrResults { get; set; }
        public Action StartIntroPhaseIfNeeded { get; set; }
        public Action HideStartingIn { get; set; }
        public Action<int> ShowStartingIn { get; set; }
        public Action<int> UpdateStartingIn { get; set; }
        /// <summary>Roster reset / cancel_ready_phase: unlock the local Ready button.</summary>
        public Action ResetManualReady { get; set; }
        /// <summary>Host-authoritative: sender clicked the lobby Ready button.</summary>
        public Action<string> OnPlayerManualReady { get; set; }
        /// <summary>Applies arena camera shake when <c>target_user_id</c> in the payload matches the local player.</summary>
        public Action<float> ApplyCameraShake { get; set; }
        /// <summary>Parses <c>display_name</c> from ActionSync (<see cref="NeonSumoNetEvents.PlayerDisplayName"/>).</summary>
        public Action<string, string> RegisterPeerDisplayName { get; set; }
    }
}
