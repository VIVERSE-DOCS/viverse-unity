using Unity.Profiling;

namespace NeonSumo
{
    /// <summary>Same-frame profiler scopes for main menu scene transitions. Do not span yield/Coroutine boundaries.</summary>
    public static class NeonSumoMenuProfilerMarkers
    {
        public static readonly ProfilerMarker SinglePlayerClicked = new ProfilerMarker("NeonSumo.Menu.SinglePlayerClicked");
        public static readonly ProfilerMarker MultiplayerClicked = new ProfilerMarker("NeonSumo.Menu.MultiplayerClicked");
        public static readonly ProfilerMarker ShowLoadingOverlay = new ProfilerMarker("NeonSumo.Menu.ShowLoadingOverlay");
        public static readonly ProfilerMarker LoadGameplaySceneAsync = new ProfilerMarker("NeonSumo.Menu.LoadGameplaySceneAsync");
    }
}
