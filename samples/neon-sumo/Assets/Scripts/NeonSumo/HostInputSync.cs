using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Host-authoritative input and transform broadcast. Clients send player_input on General;
    /// the master applies physics and broadcasts pawn transforms on NetworkSync.
    /// </summary>
    public sealed class HostInputSync
    {
        private readonly HostInputSyncContext _ctx;
        private readonly Dictionary<string, PlayerInputSnapshot> _latestInputByUserId = new Dictionary<string, PlayerInputSnapshot>();
        private float _stateBroadcastTimer;
        private float _inputSendTimer;
        private const float InputSendInterval = 0.05f;
        private const float StateBroadcastInterval = 0.05f;
        /// <summary>How long after the last received packet we still apply remote input (jitter / relay delays).</summary>
        private const double MaxRemoteInputAgeMs = 1000.0;

        private struct PlayerInputSnapshot
        {
            public float MoveX;
            public float MoveZ;
            public bool Boost;
            public double ClientTimestamp;
            public float HostReceivedTime;
        }

        public HostInputSync(HostInputSyncContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>Call when host receives player_input message. Stores for next HostTick.</summary>
        public void ReceivePlayerInput(string userId, float moveX, float moveZ, bool boost, double timestamp)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId))
                return;
            _latestInputByUserId[userId] = new PlayerInputSnapshot
            {
                MoveX = moveX,
                MoveZ = moveZ,
                Boost = boost,
                ClientTimestamp = timestamp,
                HostReceivedTime = Time.realtimeSinceStartup
            };
        }

        /// <summary>Host tick: apply input to all players, optionally broadcast state. Call from FixedUpdate when host.</summary>
        public void TickHost(float deltaTime)
        {
            ApplyHostMovement();

            if (_ctx.MultiplayerClient != null)
            {
                _stateBroadcastTimer += deltaTime;
                while (_stateBroadcastTimer >= StateBroadcastInterval)
                {
                    _stateBroadcastTimer -= StateBroadcastInterval;
                    BroadcastPlayerStates();
                }
            }
        }

        /// <summary>Resets broadcast/input timers and drops stale remote snapshots (e.g. after restart).</summary>
        public void ResetTimers()
        {
            _stateBroadcastTimer = 0f;
            _inputSendTimer = 0f;
            _latestInputByUserId.Clear();
        }

        /// <summary>Client tick: send local input. Call from FixedUpdate when not host.</summary>
        public void TickClient(float deltaTime)
        {
            if (_ctx.MultiplayerClient == null) return;

            _inputSendTimer += deltaTime;
            while (_inputSendTimer >= InputSendInterval)
            {
                _inputSendTimer -= InputSendInterval;
                SendPlayerInput();
            }
        }

        private void ApplyHostMovement()
        {
            foreach (var kv in _ctx.GetPlayers())
            {
                var player = kv.Value;
                if (!ShouldProcessPlayer(player))
                    continue;

                var input = ResolveInputForPlayer(player);
                player.ApplyInputFromHost(input.Move, input.Boost);
            }
        }

        private static bool ShouldProcessPlayer(NeonSumoPlayer player)
        {
            return player != null &&
                   !player.IsEliminated &&
                   player.ControlsEnabled;
        }

        private AppliedInput ResolveInputForPlayer(NeonSumoPlayer player)
        {
            switch (player.ControlKind)
            {
                case NeonSumoControlKind.HumanLocal:
                    {
                        var (move, boost) = player.GetInputForHost();
                        return new AppliedInput(move, boost);
                    }
                case NeonSumoControlKind.Cpu:
                    {
                        if (_ctx.GetCpuInput != null)
                        {
                            var (move, boost) = _ctx.GetCpuInput(player);
                            return new AppliedInput(move, boost);
                        }

                        return AppliedInput.Zero;
                    }
                case NeonSumoControlKind.HumanRemote:
                default:
                    return TryGetFreshRemoteInput(player.UserId, out var input)
                        ? input
                        : AppliedInput.Zero;
            }
        }

        private bool TryGetFreshRemoteInput(string userId, out AppliedInput input)
        {
            userId = NeonSumoUserId.Normalize(userId);
            if (_latestInputByUserId.TryGetValue(userId, out var snap) && IsFresh(snap))
            {
                input = new AppliedInput(new Vector2(snap.MoveX, snap.MoveZ), snap.Boost);
                return true;
            }

            input = default;
            return false;
        }

        private static bool IsFresh(PlayerInputSnapshot snap)
        {
            double ageMs = (Time.realtimeSinceStartup - snap.HostReceivedTime) * 1000.0;
            return ageMs < MaxRemoteInputAgeMs;
        }

        private void SendGeneralOutboundPayload(object payload)
        {
            var client = _ctx.MultiplayerClient;
            if (client == null)
                return;
            var general = client.General;
            if (general == null)
                throw new InvalidOperationException("General module is not initialized. Call Initialize() first.");
            string json = payload is JToken token
                ? token.ToString(Formatting.None)
                : JsonConvert.SerializeObject(payload);
            NeonSumoGeneralSend.SendJson(client, json);
        }

        private void SendPlayerInput()
        {
            var player = _ctx.GetLocalPlayer?.Invoke();
            if (player == null) return;
            if (player.IsEliminated || !player.IsLocal || player.ControlKind != NeonSumoControlKind.HumanLocal) return;

            (Vector2 move, bool boost) = player.GetInputForHost();
            var payload = NeonSumoMessageFactory.CreatePlayerInput(
                player.UserId,
                move.x,
                move.y,
                boost,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            SendGeneralOutboundPayload(payload);
        }

        private void BroadcastPlayerStates()
        {
            var client = _ctx.MultiplayerClient;
            var networkSync = client?.NetworkSync;
            if (networkSync == null)
                return;

            string localId = NeonSumoUserId.Normalize(_ctx.LocalPlayerId);

            foreach (var kv in _ctx.GetPlayers())
            {
                var player = kv.Value;
                if (player == null) continue;

                var rb = player.CachedRigidbody;
                if (rb == null) continue;

                if (!NeonSumoUserId.TryNormalize(player.UserId, out var userId))
                    continue;

                string dataJson = NeonSumoMessageFactory.CreateNetworkSyncTransformJson(
                    rb.position,
                    rb.rotation,
                    rb.linearVelocity);

                if (userId == localId)
                    networkSync.UpdateMyPosition(dataJson);
                else
                    networkSync.UpdateEntityPosition(userId, dataJson);
            }
        }

        private readonly struct AppliedInput
        {
            public readonly Vector2 Move;
            public readonly bool Boost;

            public AppliedInput(Vector2 move, bool boost)
            {
                Move = move;
                Boost = boost;
            }

            public static AppliedInput Zero => new AppliedInput(Vector2.zero, false);
        }
    }

    /// <summary>Context for HostInputSync. GameManager populates this.</summary>
    public sealed class HostInputSyncContext
    {
        public string LocalPlayerId { get; }
        public MultiplayerClient MultiplayerClient { get; }
        public Func<IEnumerable<KeyValuePair<string, NeonSumoPlayer>>> GetPlayers { get; }
        public Func<NeonSumoPlayer> GetLocalPlayer { get; }
        /// <summary>When set, used for <see cref="NeonSumoControlKind.Cpu"/> pawns on the host.</summary>
        public Func<NeonSumoPlayer, (Vector2 move, bool boost)> GetCpuInput { get; set; }

        public HostInputSyncContext(
            string localPlayerId,
            MultiplayerClient multiplayerClient,
            Func<IEnumerable<KeyValuePair<string, NeonSumoPlayer>>> getPlayers,
            Func<NeonSumoPlayer> getLocalPlayer)
        {
            LocalPlayerId = localPlayerId;
            MultiplayerClient = multiplayerClient;
            GetPlayers = getPlayers;
            GetLocalPlayer = getLocalPlayer;
        }
    }
}
