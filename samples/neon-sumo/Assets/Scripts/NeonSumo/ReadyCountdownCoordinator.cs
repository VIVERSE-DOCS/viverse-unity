using System;
using System.Collections;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Owns ready-phase countdown: coroutine, broadcast throttling, and UI updates.
    /// Centralizes cancellation and broadcast tick logic.
    /// </summary>
    public class ReadyCountdownCoordinator
    {
        private readonly ReadyCountdownContext _ctx;

        private Coroutine _routine;
        private bool _isRunning;
        private int _lastBroadcastSecond = -1;
        private int _lastDisplayedSecond = -1;

        public ReadyCountdownCoordinator(ReadyCountdownContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public bool IsRunning => _isRunning;

        /// <summary>Starts the ready countdown. Idempotent — cancels any existing countdown first.</summary>
        public void BeginCountdown()
        {
            CancelCountdown();

            float duration = Mathf.Max(0f, _ctx.DurationSeconds);
            if (duration <= 0f)
            {
                _ctx.HideStartingIn?.Invoke();
                _ctx.OnComplete?.Invoke();
                return;
            }

            _isRunning = true;
            _routine = _ctx.StartCoroutine?.Invoke(RunCountdown());

            if (_routine == null)
            {
                Cleanup();
                DebugLogger.LogError("[NeonSumo] Failed to start ready countdown coroutine.");
            }
        }

        /// <summary>Cancels the countdown if running. Broadcasts cancel if master client.</summary>
        public void CancelCountdown()
        {
            if (_routine != null)
            {
                _ctx.StopCoroutine?.Invoke(_routine);
                _routine = null;
            }

            if (_isRunning && _ctx.IsMasterClient?.Invoke() == true)
            {
                _ctx.BroadcastCancel?.Invoke();
            }

            Cleanup();
        }

        private void Cleanup()
        {
            _isRunning = false;
            _routine = null;
            _lastBroadcastSecond = -1;
            _lastDisplayedSecond = -1;
            _ctx.HideStartingIn?.Invoke();
        }

        private IEnumerator RunCountdown()
        {
            float remaining = Mathf.Max(0f, _ctx.DurationSeconds);
            bool isMaster = _ctx.IsMasterClient?.Invoke() == true;

            int initialSeconds = Mathf.CeilToInt(remaining);
            _lastDisplayedSecond = initialSeconds;

            _ctx.ShowStartingIn?.Invoke(initialSeconds);

            if (isMaster)
            {
                _lastBroadcastSecond = initialSeconds;
                _ctx.BroadcastStart?.Invoke(initialSeconds);
            }

            while (remaining > 0f)
            {
                yield return null;
                remaining -= Time.deltaTime;

                int displaySeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));

                if (displaySeconds != _lastDisplayedSecond)
                {
                    _lastDisplayedSecond = displaySeconds;
                    _ctx.UpdateStartingIn?.Invoke(displaySeconds);
                }

                if (isMaster && displaySeconds != _lastBroadcastSecond)
                {
                    _lastBroadcastSecond = displaySeconds;
                    _ctx.BroadcastUpdate?.Invoke(displaySeconds);
                }
            }

            Cleanup();

            DebugLogger.Log("[NeonSumo] Ready countdown complete — beginning game start sequence");
            _ctx.OnComplete?.Invoke();
        }
    }

    /// <summary>Context for ReadyCountdownCoordinator. GameManager populates this.</summary>
    public class ReadyCountdownContext
    {
        public float DurationSeconds;
        public Action<int> ShowStartingIn;
        public Action<int> UpdateStartingIn;
        public Action HideStartingIn;
        public Action<int> BroadcastStart;
        public Action<int> BroadcastUpdate;
        public Action BroadcastCancel;
        public Func<bool> IsMasterClient;
        public Action OnComplete;
        public Func<IEnumerator, Coroutine> StartCoroutine;
        public Action<Coroutine> StopCoroutine;
    }
}
