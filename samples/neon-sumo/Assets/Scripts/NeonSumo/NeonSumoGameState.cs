namespace NeonSumo
{
    /// <summary>
    /// Game flow states for NeonSumo round lifecycle.
    /// Extracted from NeonSumoGameManager for centralized transition validation.
    /// </summary>
    public enum NeonSumoGameState
    {
        WaitingForPlayers,
        ReadyPhase,
        Countdown,
        Playing,
        RoundEnding,
        RoundResults,
        GameEnd,
        Restarting
    }
}
