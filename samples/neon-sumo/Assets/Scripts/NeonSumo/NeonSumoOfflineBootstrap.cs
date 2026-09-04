using System.Collections;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Single-player / offline entry point for Neon Sumo. Starts the game without matchmaking or multiplayer,
    /// for local play or testing.
    /// </summary>
    /// <remarks>
    /// <para><b>What it does:</b></para>
    /// <list type="bullet">
    /// <item><b>Disables arena auto-shrink:</b> In Awake(), turns off arena.AutoStartShrink and arena.EnableShrink
    /// so the bootstrap controls when and how the ring shrinks.</item>
    /// <item><b>Starts a local game:</b> In <c>IEnumerator Start()</c>, waits <c>startupDelayFrames</c> (default 30) frames for engine/render startup to settle,
    /// then either staged init (<c>useStagedStartup</c>: <c>InitializeLocalStaged</c>, <c>WarmupHudStrings</c>, optional camera enable, <c>StartLocalGame</c>)
    /// or legacy (<c>InitializeLocal</c> in one shot), then <c>BeginOfflineMatchDriving</c>. Profiler scopes: <c>NeonSumo.Startup.*</c>.
    /// Tune <c>startupDelayFrames</c> after profiling: try 30, 15, 10, 5, 2 and keep the worst non-frame-1 time (frames 1–100) ideally ≤16.67 ms or ≤20 ms while loading.
    /// Fire TV / WebView often benefit from 10–30 frames of breathing room even when Editor looks fine at 2.</item>
    /// <item><b>Controls the ring shrink:</b> Two modes:
    /// <list type="bullet">
    /// <item><b>Time-based (default):</b> A 60-second countdown drives the shrink. At specific seconds it draws
    /// warning lines, plays warning alarms, and drops rings (5 rings at 35, 29, 23, 17, 11 seconds).</item>
    /// <item><b>Legacy:</b> Waits warningDelaySeconds, then calls arena.BeginShrinkWithWarnings().</item>
    /// </list></item>
    /// <item><b>Match timer / ring UI:</b> After intro (Playing + active), drives ShowEndCountdown so the clock stays 1:00 until the match starts.</item>
    /// <item><b>Play Again:</b> Subscribes to <see cref="NeonSumoGameManager.OnWorldResetAfterRestart"/> to reset one-shot flags and restart the offline driving coroutine.</item>
    /// </list>
    /// <para><b>When to use:</b> Test without multiplayer, run single-player scenes, debug arena shrink/warnings/ring drops,
    /// or play locally without matchmaking. This is the offline counterpart to NeonSumoMatchmakingFlow / NeonSumoLobbyManager.</para>
    /// <para><b>Main menu path:</b> From <see cref="NeonSumoMainMenuController"/>, async scene load + <see cref="NeonSumoLoadingCoordinator"/> keeps a loading overlay until staged startup finishes and intro begins (<see cref="NeonSumoLoadingCoordinator.NotifySinglePlayerGameplayReady"/>).</para>
    /// </remarks>
    public class NeonSumoOfflineBootstrap : MonoBehaviour
    {
        public NeonSumoArena arena;
        public NeonSumoGameManager gameManager;
        public GameObject playerPrefab;
        [Tooltip("Includes one human (LOCAL_0) plus CPU opponents. E.g. 4 = you + 3 bots.")]
        [Min(1)]
        public int localPlayerCount = 4;
        [Tooltip("When true, uses time-based ring shrink (lines, alarms, drops at 60→0 countdown).")]
        public bool enableTimeBasedShrink = true;
        public int countdownSeconds = 60;
        [Tooltip("Legacy: delay before first warning when not using time-based shrink.")]
        public float warningDelaySeconds = 5f;
        [Tooltip("Frames to wait before InitializeLocal. Default 30 (~0.5s @60) separates engine first-touch from game init in the profiler. After tuning SpawnLocalPlayer, sweep 30→15→10→5→2 and compare worst frame (1–100, excl. frame 1) to ~16.67 ms ideal / ~20 ms acceptable. Use higher values on Fire TV/WebView if needed; 0–2 for snappy desktop play.")]
        [Min(0)]
        [SerializeField] private int startupDelayFrames = 30;

        [Tooltip("When true: multi-frame InitializeLocalStaged + optional loading overlay and deferred gameplay cameras. When false: single-frame InitializeLocal (legacy profiler comparison).")]
        [SerializeField] private bool useStagedStartup = true;

        [Tooltip("Optional. Shown at the start of staged startup while world cameras may be disabled.")]
        [SerializeField] private GameObject loadingOverlay;

        [Tooltip("Optional. Disabled until after the first loading frame and local init, then re-enabled. Use when UI is Screen Space - Overlay so the loading screen stays visible; avoid if HUD uses Screen Space - Camera on these.")]
        [SerializeField] private Camera[] deferGameplayCamerasUntilAfterLoading;

        [Tooltip("Logs startup phase order to the console. Disable for release builds after profiling.")]
        [SerializeField] private bool logStartupPhases;

        private int _lastProcessedRingSecond = -1;
        private bool _line1Played, _line2Played, _line3Played, _line4Played, _line5Played;
        private bool _alarm1Played, _alarm2Played, _alarm3Played, _alarm4Played, _alarm5Played;
        private bool _ring1Dropped, _ring2Dropped, _ring3Dropped, _ring4Dropped, _ring5Dropped;
        private Coroutine _offlineDriveRoutine;

        void Awake()
        {
            if (arena != null)
            {
                arena.AutoStartShrink = false;
                arena.EnableShrink = false;
            }
        }

        IEnumerator Start()
        {
            if (gameManager == null)
            {
                DebugLogger.LogError("[NeonSumo] NeonSumoOfflineBootstrap: NeonSumoGameManager is required but not assigned. Assign in Inspector.");
                enabled = false;
                yield break;
            }

            gameManager.OnWorldResetAfterRestart += OnWorldResetAfterRestart;

            // ProfilerMarker scopes must not span yield/Coroutine waits — Begin/End must occur in the same frame.

            int delayFrames = Mathf.Max(0, startupDelayFrames);
            if (NeonSumoLoadingCoordinator.Instance != null && NeonSumoLoadingCoordinator.Instance.SkipStartupDelayForCurrentGameplayEntry)
                delayFrames = 0;

            for (int i = 0; i < delayFrames; i++)
                yield return null;

            if (useStagedStartup)
                yield return StartCoroutine(RunStagedOfflineStartup());
            else
                yield return StartCoroutine(RunLegacyOfflineStartup());

            BeginOfflineMatchDriving();
        }

        void LogStartupPhase(string phase)
        {
            if (!logStartupPhases) return;
            DebugLogger.Log($"[NeonSumo][Startup] {phase}");
        }

        void SetDeferredGameplayCamerasEnabled(bool enabled)
        {
            if (deferGameplayCamerasUntilAfterLoading == null) return;
            for (int i = 0; i < deferGameplayCamerasUntilAfterLoading.Length; i++)
            {
                Camera c = deferGameplayCamerasUntilAfterLoading[i];
                if (c != null)
                    c.enabled = enabled;
            }
        }

        IEnumerator RunStagedOfflineStartup()
        {
            int n = Mathf.Max(1, localPlayerCount);

            bool menuHandoff = NeonSumoLoadingCoordinator.Instance != null &&
                               NeonSumoLoadingCoordinator.Instance.AwaitingSinglePlayerGameplayReveal;
            if (menuHandoff)
                NeonSumoLoadingCoordinator.Instance.BeginGameplayStagedStartupTiming();

            SetDeferredGameplayCamerasEnabled(false);

            using (NeonSumoStartupProfilerMarkers.ShowLoading.Auto())
            {
                LogStartupPhase("ShowLoading");
                if (!menuHandoff && loadingOverlay != null)
                    loadingOverlay.SetActive(true);
                else if (menuHandoff)
                    LogStartupPhase("ShowLoading (menu coordinator overlay already visible)");
            }

            yield return null;
            LogStartupPhase("post-loading-frame");

            IEnumerator staged = gameManager.InitializeLocalStaged(n, logStartupPhases);
            while (staged.MoveNext())
                yield return staged.Current;

            using (NeonSumoStartupProfilerMarkers.WarmupUI.Auto())
            {
                LogStartupPhase("WarmupUI");
                gameManager.uiManager?.WarmupHudStrings();
            }

            using (NeonSumoStartupProfilerMarkers.EnableGameplayCamera.Auto())
            {
                LogStartupPhase("EnableGameplayCamera");
                SetDeferredGameplayCamerasEnabled(true);
                gameManager.CacheArenaCameraForStartup();
            }

            yield return null;

            using (NeonSumoStartupProfilerMarkers.StartLocalGame.Auto())
            {
                LogStartupPhase("StartLocalGame");
                gameManager.StartLocalGame();
            }

            NeonSumoLoadingCoordinator.Instance?.EndGameplayStagedStartupTimingAndLog();

            using (NeonSumoStartupProfilerMarkers.NotifyReady.Auto())
            {
                LogStartupPhase("NotifyReady");
                NeonSumoLoadingCoordinator.Instance?.NotifySinglePlayerGameplayReady();
            }

            if (loadingOverlay != null)
                loadingOverlay.SetActive(false);

            yield return null;
        }

        IEnumerator RunLegacyOfflineStartup()
        {
            int n = Mathf.Max(1, localPlayerCount);

            bool menuHandoff = NeonSumoLoadingCoordinator.Instance != null &&
                               NeonSumoLoadingCoordinator.Instance.AwaitingSinglePlayerGameplayReveal;
            if (menuHandoff)
                NeonSumoLoadingCoordinator.Instance.BeginGameplayStagedStartupTiming();

            LogStartupPhase("Legacy: InitializeLocal (sync)");
            gameManager.InitializeLocal(n);

            yield return null;

            using (NeonSumoStartupProfilerMarkers.WarmupUI.Auto())
            {
                LogStartupPhase("WarmupUI");
                gameManager.uiManager?.WarmupHudStrings();
            }

            yield return null;

            using (NeonSumoStartupProfilerMarkers.StartLocalGame.Auto())
            {
                LogStartupPhase("StartLocalGame");
                gameManager.StartLocalGame();
            }

            NeonSumoLoadingCoordinator.Instance?.EndGameplayStagedStartupTimingAndLog();

            using (NeonSumoStartupProfilerMarkers.NotifyReady.Auto())
            {
                LogStartupPhase("NotifyReady");
                NeonSumoLoadingCoordinator.Instance?.NotifySinglePlayerGameplayReady();
            }

            yield return null;
        }

        void OnDestroy()
        {
            if (gameManager != null)
                gameManager.OnWorldResetAfterRestart -= OnWorldResetAfterRestart;
        }

        void OnWorldResetAfterRestart()
        {
            BeginOfflineMatchDriving();
        }

        void BeginOfflineMatchDriving()
        {
            if (_offlineDriveRoutine != null)
            {
                StopCoroutine(_offlineDriveRoutine);
                _offlineDriveRoutine = null;
            }

            ResetOneShotRingFlags();

            if (enableTimeBasedShrink && arena != null && gameManager?.uiManager != null)
                _offlineDriveRoutine = StartCoroutine(RunCountdownWithRingEvents());
            else if (!enableTimeBasedShrink && arena != null)
                _offlineDriveRoutine = StartCoroutine(PlayFirstWarningAfterDelay());
            else if (gameManager?.uiManager != null)
                _offlineDriveRoutine = StartCoroutine(RunCountdownTimer());
        }

        void ResetOneShotRingFlags()
        {
            _lastProcessedRingSecond = -1;
            _line1Played = _line2Played = _line3Played = _line4Played = _line5Played = false;
            _alarm1Played = _alarm2Played = _alarm3Played = _alarm4Played = _alarm5Played = false;
            _ring1Dropped = _ring2Dropped = _ring3Dropped = _ring4Dropped = _ring5Dropped = false;
        }

        IEnumerator WaitForLocalMatchPlaying()
        {
            yield return new WaitUntil(() =>
                gameManager != null &&
                gameManager.IsGameActive &&
                gameManager.CurrentState == NeonSumoGameState.Playing);
        }

        IEnumerator RunCountdownWithRingEvents()
        {
            yield return StartCoroutine(WaitForLocalMatchPlaying());

            int remaining = Mathf.Max(0, countdownSeconds);
            while (remaining >= 0)
            {
                if (gameManager == null || !gameManager.IsGameActive || gameManager.CurrentState != NeonSumoGameState.Playing)
                {
                    yield break;
                }

                gameManager?.uiManager?.ShowEndCountdown(remaining);
                if (arena != null && remaining != _lastProcessedRingSecond)
                {
                    HandleRingEvents(remaining);
                    _lastProcessedRingSecond = remaining;
                }
                yield return new WaitForSeconds(1f);
                remaining--;
            }
        }

        void HandleRingEvents(int seconds)
        {
            // Line duration = time from line start until ring drop
            if (seconds <= 55 && !_line1Played) { _line1Played = true; arena.PlayWarningLine(0, 55f - 35f, clockwise: true); }
            if (seconds <= 45 && !_line2Played) { _line2Played = true; arena.PlayWarningLine(1, 45f - 29f, clockwise: false); }
            if (seconds <= 35 && !_line3Played) { _line3Played = true; arena.PlayWarningLine(2, 35f - 23f, clockwise: true); }
            if (seconds <= 25 && !_line4Played) { _line4Played = true; arena.PlayWarningLine(3, 25f - 17f, clockwise: false); }
            if (seconds <= 15 && !_line5Played) { _line5Played = true; arena.PlayWarningLine(4, 15f - 11f, clockwise: true); }

            if (seconds <= 38 && !_alarm1Played) { _alarm1Played = true; arena.PlayWarningAlarm(); }
            if (seconds <= 32 && !_alarm2Played) { _alarm2Played = true; arena.PlayWarningAlarm(); }
            if (seconds <= 26 && !_alarm3Played) { _alarm3Played = true; arena.PlayWarningAlarm(); }
            if (seconds <= 20 && !_alarm4Played) { _alarm4Played = true; arena.PlayWarningAlarm(); }
            if (seconds <= 14 && !_alarm5Played) { _alarm5Played = true; arena.PlayWarningAlarm(); }

            if (seconds <= 35 && !_ring1Dropped) { _ring1Dropped = true; arena.TriggerRingDrop(0); }
            if (seconds <= 29 && !_ring2Dropped) { _ring2Dropped = true; arena.TriggerRingDrop(1); }
            if (seconds <= 23 && !_ring3Dropped) { _ring3Dropped = true; arena.TriggerRingDrop(2); }
            if (seconds <= 17 && !_ring4Dropped) { _ring4Dropped = true; arena.TriggerRingDrop(3); }
            if (seconds <= 11 && !_ring5Dropped) { _ring5Dropped = true; arena.TriggerRingDrop(4); }
        }

        IEnumerator RunCountdownTimer()
        {
            yield return StartCoroutine(WaitForLocalMatchPlaying());

            int remaining = Mathf.Max(0, countdownSeconds);
            while (remaining >= 0)
            {
                if (gameManager == null || !gameManager.IsGameActive || gameManager.CurrentState != NeonSumoGameState.Playing)
                {
                    yield break;
                }

                gameManager?.uiManager?.ShowEndCountdown(remaining);
                yield return new WaitForSeconds(1f);
                remaining--;
            }
        }

        IEnumerator PlayFirstWarningAfterDelay()
        {
            yield return StartCoroutine(WaitForLocalMatchPlaying());

            float delay = Mathf.Max(0f, warningDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            arena.BeginShrinkWithWarnings(arena.WarningRingDuration, shouldDropOnComplete: true);
        }
    }
}
