using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
namespace MphRead.Mods.Launcher;

public enum NativePartyTravelAction { Publish, Follow, Decline, JoinLeader, Clear, Reserve, CancelReservation }
public sealed record NativePartyTravel(string TravelId, int Revision, string LeaderPrimeId,
    string LobbyId, string RoomKey, string ServerName, ulong AuthorityEpoch,
    DateTimeOffset ExpiresAt, bool IsLeader, string SelfStatus);
public sealed record NativePartyLocalLobby(ushort MatchId, ulong AuthorityEpoch,
    string RoomKey, string ServerName);
public sealed record NativePartyJoinTarget(string SelfPrimeId, string PartyId,
    long PartyRevision, string TravelId, int TravelRevision, string LobbyId,
    string Host, int Port, byte Protocol, ulong AuthorityEpoch, string RoomKey,
    DateTimeOffset ExpiresAt)
{
    [JsonIgnore] internal object? AccountWitness { get; init; }
    public NativePartyReservation? Reservation { get; init; }
}
public sealed record NativePartyTravelCommand(NativePartyTravelAction Action,
    string ExpectedSelfPrimeId, string PartyId, long ExpectedRevision,
    string TravelId, int TravelRevision, NativePartyLocalLobby? LocalLobby = null)
{
    [JsonIgnore] internal object? AccountWitness { get; init; }
    public NativePartyReservation? Reservation { get; init; }
}
public sealed record NativePartyTravelResult(NativePartySnapshot Snapshot, NativePartyJoinTarget? JoinTarget);

public static class NativePartyTravelValidation
{
    public static bool ValidTarget(NativePartyJoinTarget? target, DateTimeOffset now)
        => target != null && NativeSocialFriendsController.ValidPrimeId(target.SelfPrimeId)
        && Guid.TryParse(target.PartyId, out _) && target.PartyRevision is >= 0 and <= 9007199254740991L
        && Guid.TryParse(target.TravelId, out _) && target.TravelRevision is >= 1 and <= 1000000
        && Guid.TryParse(target.LobbyId, out _) && IPAddress.TryParse(target.Host, out var address)
        && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && target.Port is >= 1 and <= 65535 && target.Protocol > 0 && target.AuthorityEpoch != 0
        && target.RoomKey is { Length: > 0 and <= 128 } && target.ExpiresAt > now
        && target.ExpiresAt <= now.AddMinutes(3);
}
public interface INativePartyTravelAuthority : INativePartyAuthority
{
    Task<NativePartyTravelResult> TravelAsync(NativePartyTravelCommand command, CancellationToken cancellation);
}
