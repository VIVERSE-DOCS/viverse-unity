namespace NeonSumo
{
    /// <summary>
    /// ActionSync event name constants. Use these instead of magic strings.
    /// </summary>
    internal static class NeonSumoNetEvents
    {
        // Playing — elimination, arena, ramps
        public const string Eliminate = "eliminate";
        public const string RingDrop = "ring_drop";
        public const string RampsRetract = "ramps_retract";
        public const string UnlockControls = "unlock_controls";
        public const string RoundWin = "round_win";
        /// <summary>Host targets a human client for camera shake after player–player contact.</summary>
        public const string CameraShake = "camera_shake";

        // Spawn & presentation
        /// <summary>Host broadcasts spawn state for a player; clients apply locally.</summary>
        public const string PlayerSpawnSync = "player_spawn_sync";
        public const string PlayerColor = "player_color";
        /// <summary>Broadcast human-readable display label for a peer UI (Lobby seed / VIVERSE).</summary>
        public const string PlayerDisplayName = "player_display_name";
        /// <summary>Sent by a client after it has completed local spawn setup.</summary>
        public const string SpawnAck = "spawn_ack";

        // Ready phase (lobby countdown)
        public const string StartReadyPhase = "start_ready_phase";
        public const string UpdateReadyPhase = "update_ready_phase";
        public const string CancelReadyPhase = "cancel_ready_phase";
        /// <summary>Client announces they clicked the lobby Ready button. Host-authoritative vote; not Game.Ready().</summary>
        public const string PlayerManualReady = "player_manual_ready";

        // Intro (3-2-1-Go)
        public const string StartIntroPhase = "start_intro_phase";
    }
}
