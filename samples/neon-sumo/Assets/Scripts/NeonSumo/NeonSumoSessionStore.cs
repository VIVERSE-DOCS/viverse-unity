namespace NeonSumo
{
    /// <summary>
    /// Runtime session choice for this app run (not persisted). Consumed by <see cref="NeonSumoSceneBootstrap"/>.
    /// </summary>
    public enum NeonSumoGameMode
    {
        None = 0,
        OfflineSinglePlayer = 1,
        OnlineMultiplayer = 2,
    }

    /// <summary>
    /// Per-process session store. Reset to <see cref="NeonSumoGameMode.None"/> after gameplay bootstrap consumes a mode (one-shot).
    /// </summary>
    public static class NeonSumoSessionStore
    {
        public static NeonSumoGameMode Mode { get; set; } = NeonSumoGameMode.None;

        public static void Clear()
        {
            Mode = NeonSumoGameMode.None;
        }
    }
}
