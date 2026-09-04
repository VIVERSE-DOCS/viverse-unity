using Unity.Profiling;

namespace NeonSumo
{
    /// <summary>Unity ProfilerRecorder scopes for offline Neon Sumo startup; use in NeonSumoOfflineBootstrap and <see cref="NeonSumoGameManager.InitializeLocalStaged"/>.</summary>
    /// <remarks>
    /// Do not use <see cref="ProfilerMarker.Auto"/> (or Begin/End) across <c>yield return</c> or coroutine boundaries — Unity reports non-matching EndSample.
    /// <see cref="Total"/> is kept for optional all-sync profiling only; offline startup is intentionally spread across frames.
    /// </remarks>
    public static class NeonSumoStartupProfilerMarkers
    {
        /// <summary>Optional marker for fully synchronous blocks only — do not wrap coroutine or multi-frame startup.</summary>
        public static readonly ProfilerMarker Total = new ProfilerMarker("NeonSumo.Startup.Total");
        public static readonly ProfilerMarker ShowLoading = new ProfilerMarker("NeonSumo.Startup.ShowLoading");
        public static readonly ProfilerMarker InitializeManagers = new ProfilerMarker("NeonSumo.Startup.InitializeManagers");
        public static readonly ProfilerMarker WarmupUI = new ProfilerMarker("NeonSumo.Startup.WarmupUI");
        public static readonly ProfilerMarker InitializeArena = new ProfilerMarker("NeonSumo.Startup.InitializeArena");
        public static readonly ProfilerMarker SpawnLocalPlayer = new ProfilerMarker("NeonSumo.Startup.SpawnLocalPlayer");
        public static readonly ProfilerMarker SpawnBot = new ProfilerMarker("NeonSumo.Startup.SpawnBot");
        public static readonly ProfilerMarker FinalizeSpawnSystems = new ProfilerMarker("NeonSumo.Startup.FinalizeSpawnSystems");
        public static readonly ProfilerMarker UIInit = new ProfilerMarker("NeonSumo.Startup.UIInit");
        public static readonly ProfilerMarker EnableGameplayCamera = new ProfilerMarker("NeonSumo.Startup.EnableGameplayCamera");
        public static readonly ProfilerMarker StartLocalGame = new ProfilerMarker("NeonSumo.Startup.StartLocalGame");
        public static readonly ProfilerMarker NotifyReady = new ProfilerMarker("NeonSumo.Startup.NotifyReady");
    }
}
