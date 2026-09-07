namespace MphRead.Mods.Network;
public sealed record BotParticipant(byte Slot, ulong Identity, Hunter Hunter, byte TeamIndex, string Name)
{
    public bool RetirementRequested { get; internal set; }
}
