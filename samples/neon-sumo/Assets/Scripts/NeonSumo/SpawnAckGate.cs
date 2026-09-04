using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Manages spawn ACK gate state: begin wait, register ACKs, complete when all received or timeout.
    /// Single completion path via CompleteSpawnAckWait.
    /// </summary>
    public sealed class SpawnAckGate
    {
        private readonly HashSet<string> _acknowledgedSpawnUserIds = new HashSet<string>();
        private readonly ICoroutineRunner _coroutineRunner;
        private readonly Action _onAllSpawnAcksReceived;

        private bool _isWaitingForSpawnAcks;
        private bool _localSpawnAckSentForCurrentRound;
        private int _expectedSpawnAckUserCount;
        private Coroutine _spawnAckTimeoutRoutine;

        public SpawnAckGate(ICoroutineRunner coroutineRunner, Action onAllSpawnAcksReceived)
        {
            _coroutineRunner = coroutineRunner;
            _onAllSpawnAcksReceived = onAllSpawnAcksReceived;
        }

        public bool LocalSpawnAckSentForCurrentRound => _localSpawnAckSentForCurrentRound;

        public void Begin(int playerCount)
        {
            if (playerCount <= 0)
            {
                DebugLogger.LogWarning("[NeonSumo] BeginSpawnAckWait called with non-positive playerCount");
                Reset();
                _onAllSpawnAcksReceived?.Invoke();
                return;
            }

            _isWaitingForSpawnAcks = true;
            _localSpawnAckSentForCurrentRound = false;
            _acknowledgedSpawnUserIds.Clear();
            _expectedSpawnAckUserCount = playerCount;

            if (_spawnAckTimeoutRoutine != null)
            {
                _coroutineRunner.StopCoroutine(_spawnAckTimeoutRoutine);
            }

            _spawnAckTimeoutRoutine = _coroutineRunner.StartCoroutine(SpawnAckTimeoutRoutine());
        }

        public void Register(string userId)
        {
            if (!_isWaitingForSpawnAcks || !NeonSumoUserId.TryNormalize(userId, out userId))
                return;

            if (!_acknowledgedSpawnUserIds.Add(userId))
                return;

            DebugLogger.Log($"[NeonSumo] Spawn ACK registered from {userId} ({_acknowledgedSpawnUserIds.Count}/{_expectedSpawnAckUserCount})");

            if (_acknowledgedSpawnUserIds.Count >= _expectedSpawnAckUserCount)
            {
                CompleteSpawnAckWait();
            }
        }

        public void MarkLocalSpawnAckSent()
        {
            _localSpawnAckSentForCurrentRound = true;
        }

        public void ResetLocalSpawnAckSent()
        {
            _localSpawnAckSentForCurrentRound = false;
        }

        public void Reset()
        {
            if (_spawnAckTimeoutRoutine != null)
            {
                _coroutineRunner.StopCoroutine(_spawnAckTimeoutRoutine);
                _spawnAckTimeoutRoutine = null;
            }
            _isWaitingForSpawnAcks = false;
            _acknowledgedSpawnUserIds.Clear();
            _expectedSpawnAckUserCount = 0;
            _localSpawnAckSentForCurrentRound = false;
            DebugLogger.Log("[NeonSumo] Spawn ACK state cleared");
        }

        private void CompleteSpawnAckWait()
        {
            if (!_isWaitingForSpawnAcks)
                return;

            _isWaitingForSpawnAcks = false;

            if (_spawnAckTimeoutRoutine != null)
            {
                _coroutineRunner.StopCoroutine(_spawnAckTimeoutRoutine);
                _spawnAckTimeoutRoutine = null;
            }

            _onAllSpawnAcksReceived?.Invoke();
        }

        /// <summary>Timeout/failsafe coroutine. Completion is driven by Register, not by polling.</summary>
        private IEnumerator SpawnAckTimeoutRoutine()
        {
            const float timeoutSeconds = 30f;
            float elapsed = 0f;

            while (_isWaitingForSpawnAcks && elapsed < timeoutSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            if (_isWaitingForSpawnAcks)
            {
                DebugLogger.LogWarning($"[NeonSumo] Spawn ACK timeout after {timeoutSeconds}s");
                CompleteSpawnAckWait();
            }
        }
    }
}
