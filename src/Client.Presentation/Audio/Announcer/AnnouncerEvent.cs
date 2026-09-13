using MphRead.Mods.Network;

namespace MphRead.Mods.Audio;

public enum AnnouncerEvent : byte
{
    Three = 1,
    Two,
    One,
    Go,
    Overtime,
    MatchPoint,
    Victory,
    Defeat,
    FirstHunt,
    DoubleKill,
    TripleKill,
    Interceptor,
    Defender,
    PrimeSlayer,
    Capture,
    Assist
}

public static class AnnouncerEventMapping
{
    public static bool TryFromAward(in MatchAward award, out AnnouncerEvent value)
    {
        value = award.Kind switch
        {
            MatchAwardKind.FirstHunt => AnnouncerEvent.FirstHunt,
            MatchAwardKind.DoubleKill => AnnouncerEvent.DoubleKill,
            MatchAwardKind.TripleKill => AnnouncerEvent.TripleKill,
            MatchAwardKind.Interceptor => AnnouncerEvent.Interceptor,
            MatchAwardKind.Defender => AnnouncerEvent.Defender,
            MatchAwardKind.PrimeSlayer => AnnouncerEvent.PrimeSlayer,
            MatchAwardKind.Capture => AnnouncerEvent.Capture,
            MatchAwardKind.Assist => AnnouncerEvent.Assist,
            _ => default
        };
        return award.IsValid;
    }

    public static bool TryFromMatchEvent(in MatchEvent value, out AnnouncerEvent result)
    {
        result = value.Kind switch
        {
            MatchEventKind.CountdownStarted => AnnouncerEvent.Three,
            MatchEventKind.MatchStarted => AnnouncerEvent.Go,
            MatchEventKind.OvertimeStarted => AnnouncerEvent.Overtime,
            MatchEventKind.MatchPointReached => AnnouncerEvent.MatchPoint,
            _ => default
        };
        return value.IsValid && value.Kind is MatchEventKind.CountdownStarted or MatchEventKind.MatchStarted
            or MatchEventKind.OvertimeStarted or MatchEventKind.MatchPointReached;
    }

    /// <summary>Maps a terminal fact only when the server supplied a winner
    /// and the consumer supplied its local team. A bare MatchEnded fact is not
    /// enough to announce Victory.</summary>
    public static bool TryFromMatchEvent(in MatchEvent value, byte localTeam, out AnnouncerEvent result)
    {
        if (value.Kind != MatchEventKind.MatchEnded || !value.IsValid
            || value.Team >= 8 || localTeam >= 8)
        {
            result = default;
            return false;
        }
        result = value.Team == localTeam ? AnnouncerEvent.Victory : AnnouncerEvent.Defeat;
        return true;
    }
}
