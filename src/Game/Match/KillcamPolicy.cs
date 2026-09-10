namespace MphRead
{
    /// <summary>Server-authoritative availability and timing for killcam presentation.</summary>
    public enum KillcamPolicy : byte
    {
        Disabled,
        Immediate,
        PostRound
    }
}
