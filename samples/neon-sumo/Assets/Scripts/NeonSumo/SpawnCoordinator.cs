using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using ViverseSDK;

namespace NeonSumo
{
    /// <summary>
    /// Coordinates ramp spawn assignments, pending spawns, and spawn-ack gate.
    /// Idempotent: StorePendingRampSpawn overwrites; receiving spawn sync before/after player exists is handled.
    /// </summary>
    public class SpawnCoordinator
    {
        private readonly SpawnCoordinatorContext _ctx;
        private readonly Dictionary<string, int> _spawnIndexByUserId = new Dictionary<string, int>();
        private readonly Dictionary<string, PendingRampSpawn> _pendingSpawnByUserId = new Dictionary<string, PendingRampSpawn>();
        private readonly SpawnAckGate _spawnAckGate;

        private readonly struct PendingRampSpawn
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly int SpawnIndex;

            public PendingRampSpawn(Vector3 position, Quaternion rotation, int spawnIndex)
            {
                Position = position;
                Rotation = rotation;
                SpawnIndex = spawnIndex;
            }
        }

        public SpawnCoordinator(SpawnCoordinatorContext ctx)
        {
            _ctx = ctx;
            var coroutineRunner = new DelegatingCoroutineRunner(
                r => _ctx.StartCoroutine?.Invoke(r),
                c => _ctx.StopCoroutine?.Invoke(c));
            _spawnAckGate = new SpawnAckGate(coroutineRunner, () => _ctx.OnAllSpawnAcksReceived?.Invoke());
        }

        public int GetOrAssignRampSpawnIndex(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return 0;
            if (_spawnIndexByUserId.TryGetValue(userId, out var existing)) return existing;
            int nextIndex = _spawnIndexByUserId.Count;
            _spawnIndexByUserId[userId] = nextIndex;
            return nextIndex;
        }

        /// <summary>Store pending spawn for a user not yet in the game. Idempotent: overwrites existing.</summary>
        public void StorePendingRampSpawn(string userId, Vector3 position, Quaternion rotation, int spawnIndex)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;
            _pendingSpawnByUserId[userId] = new PendingRampSpawn(position, rotation, spawnIndex);
        }

        /// <summary>Apply pending spawn if one exists. Returns true if applied.</summary>
        public bool TryApplyPendingRampSpawn(string userId)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return false;
            if (!_pendingSpawnByUserId.TryGetValue(userId, out var pending)) return false;
            _pendingSpawnByUserId.Remove(userId);
            _ctx.ApplyRampSpawnFromNetwork?.Invoke(userId, pending.Position, pending.Rotation, pending.SpawnIndex);
            return true;
        }

        public void RegisterSpawnAck(string userId)
        {
            _spawnAckGate.Register(NeonSumoUserId.Normalize(userId));
        }

        /// <summary>Reset spawn ack state. Call when starting new round or on round win.</summary>
        public void ResetSpawnAckGate()
        {
            _spawnAckGate.Reset();
        }

        /// <summary>Reset only spawn ack sent flag. Used when local player receives PlayerSpawnSync.</summary>
        public void ResetSpawnAckSent()
        {
            _spawnAckGate.ResetLocalSpawnAckSent();
        }

        public void BeginSpawnAckWait(int playerCount)
        {
            _spawnAckGate.Begin(playerCount);
        }

        public void TrySendLocalSpawnAck(string userId)
        {
            if (_ctx.MultiplayerClient == null || !NeonSumoUserId.TryNormalize(userId, out userId)) return;
            if (_ctx.IsLocalPlayer?.Invoke(userId) != true) return;
            if (_spawnAckGate.LocalSpawnAckSentForCurrentRound) return;

            var msg = new SpawnAckMessage { user_id = userId };
            string actionMsg = JsonConvert.SerializeObject(msg);
            NeonSumoActionSyncSend.Competition(_ctx.MultiplayerClient, NeonSumoNetEvents.SpawnAck, actionMsg, Guid.NewGuid().ToString());
            _spawnAckGate.MarkLocalSpawnAckSent();

            if (_ctx.IsMasterClient?.Invoke() == true)
            {
                RegisterSpawnAck(userId);
            }
        }

        /// <summary>
        /// Ensures every user has a ramp index without reshuffling existing assignments.
        /// Previous implementation cleared and reassigned by sorted user id, which swapped ramps whenever
        /// alphabetical order differed from join order (e.g. host first on idx 0, second player id sorts first).
        /// </summary>
        public void AssignRampSpawnIndices(IEnumerable<string> userIds)
        {
            foreach (var userId in userIds)
            {
                if (string.IsNullOrEmpty(userId)) continue;
                GetOrAssignRampSpawnIndex(userId);
            }
        }

        public bool TryGetSpawnIndex(string userId, out int spawnIndex)
        {
            spawnIndex = 0;
            return NeonSumoUserId.TryNormalize(userId, out userId) && _spawnIndexByUserId.TryGetValue(userId, out spawnIndex);
        }

        public void RecordSpawnAssignment(string userId, int spawnIndex)
        {
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return;
            _spawnIndexByUserId[userId] = spawnIndex;
        }

        public bool HasAssignmentsForPlayerCount(int count)
        {
            return _spawnIndexByUserId.Count >= count;
        }

        public bool HasAnyAssignments => _spawnIndexByUserId.Count > 0;

        /// <summary>Builds spawn sync message without sending. Caller can broadcast and optionally send local ACK.</summary>
        public PlayerSpawnSyncMessage BuildSpawnSyncMessage(string userId, int spawnIndex)
        {
            if (_ctx.Arena == null) return null;
            if (!NeonSumoUserId.TryNormalize(userId, out userId)) return null;

            Vector3 spawnPos = _ctx.Arena.GetRampSpawnPosition(spawnIndex);
            Quaternion spawnRot = _ctx.Arena.GetRampSpawnRotation(spawnIndex);
            int colorIndex = _ctx.GetOrAssignPlayerColorIndex?.Invoke(userId) ?? 0;

            return new PlayerSpawnSyncMessage
            {
                user_id = userId,
                spawn_index = spawnIndex,
                x = spawnPos.x,
                y = spawnPos.y,
                z = spawnPos.z,
                rotY = spawnRot.eulerAngles.y,
                color_index = colorIndex
            };
        }

        /// <summary>Sends PlayerSpawnSync to network. Does not send local ACK.</summary>
        public void BroadcastRampSpawn(PlayerSpawnSyncMessage message)
        {
            if (_ctx.MultiplayerClient == null || message == null) return;

            string actionMsg = JsonConvert.SerializeObject(message);
            NeonSumoActionSyncSend.Competition(_ctx.MultiplayerClient, NeonSumoNetEvents.PlayerSpawnSync, actionMsg, Guid.NewGuid().ToString());
        }

        /// <summary>Broadcasts spawn for user and sends local ACK if this is the local player.</summary>
        public void BroadcastRampSpawnForUser(string userId, int spawnIndex)
        {
            var message = BuildSpawnSyncMessage(userId, spawnIndex);
            if (message == null) return;

            BroadcastRampSpawn(message);
            TrySendLocalSpawnAck(userId);
        }

        public void BroadcastRampSpawns(IEnumerable<KeyValuePair<string, NeonSumoPlayer>> players, int playerCount)
        {
            if (_ctx.MultiplayerClient == null || _ctx.Arena == null) return;
            if (_spawnIndexByUserId.Count < playerCount)
            {
                DebugLogger.LogWarning($"[NeonSumo] BroadcastRampSpawns skipped: assignments={_spawnIndexByUserId.Count} players={playerCount}");
                return;
            }

            foreach (var kvp in players)
            {
                string userId = kvp.Key;
                if (TryGetSpawnIndex(userId, out int spawnIndex))
                {
                    BroadcastRampSpawnForUser(userId, spawnIndex);
                }
            }
        }
    }

    /// <summary>Context for SpawnCoordinator. GameManager populates this.</summary>
    public class SpawnCoordinatorContext
    {
        public NeonSumoArena Arena;
        public MultiplayerClient MultiplayerClient;
        public Func<string, int> GetOrAssignPlayerColorIndex;
        public Action<string, Vector3, Quaternion, int> ApplyRampSpawnFromNetwork;
        public Func<string, bool> IsLocalPlayer;
        public Func<bool> IsMasterClient;
        public Func<IEnumerator, Coroutine> StartCoroutine;
        public Action<Coroutine> StopCoroutine;
        public Action OnAllSpawnAcksReceived;
    }
}
