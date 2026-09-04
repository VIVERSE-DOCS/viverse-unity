namespace NeonSumo
{
    /// <summary>
    /// How a Neon Sumo pawn receives motor input on the host. Used by <see cref="HostInputSync"/> routing.
    /// </summary>
    public enum NeonSumoControlKind
    {
        HumanLocal,
        HumanRemote,
        Cpu,
    }
}
