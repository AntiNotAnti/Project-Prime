namespace MphRead.Mods.MatchEvents;
internal enum MatchDeathKind : byte { Weapon, Alt, Bomb, Burn, Deathalt, Suicide, Environment }
internal readonly record struct MatchDeathDetails(MatchDeathKind Kind, int KillerTeam, int VictimTeam)
{
    internal int Encode() => 0x1000000 | (int)Kind | ((KillerTeam + 1) << 8) | ((VictimTeam + 1) << 16);
    internal static bool TryDecode(int value, out MatchDeathDetails details)
    {
        details = new((MatchDeathKind)(value & 255), ((value >> 8) & 255) - 1, ((value >> 16) & 255) - 1);
        return (value & unchecked((int)0xFF000000)) == 0x1000000 && (value & 255) <= 6
            && details.KillerTeam is >= -1 and < 8 && details.VictimTeam is >= 0 and < 8;
    }
    internal static MatchDeathKind Classify(bool environment, bool suicide, bool deathalt, bool burn, bool weapon, bool bomb)
        => environment ? MatchDeathKind.Environment : suicide ? MatchDeathKind.Suicide
        : deathalt ? MatchDeathKind.Deathalt : burn ? MatchDeathKind.Burn
        : weapon ? MatchDeathKind.Weapon : bomb ? MatchDeathKind.Bomb : MatchDeathKind.Alt;
}
