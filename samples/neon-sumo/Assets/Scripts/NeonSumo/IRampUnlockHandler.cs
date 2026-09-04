namespace NeonSumo
{
    /// <summary>
    /// Receives notifications when a ramp is unlocked.
    /// Used to decouple ramp unlock reactions from GameManager.
    /// </summary>
    public interface IRampUnlockHandler
    {
        void OnRampUnlocked(NeonSumoPlayer player);
    }
}
