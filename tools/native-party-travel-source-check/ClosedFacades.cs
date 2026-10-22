// Closed facades: no world, socket, preferences, account or HTTP access.
namespace MphRead.Mods.Launcher {
internal static class LauncherPrefs { public static string PlayerName=>"SYNTHETIC"; public static int LastHunter=>0; }
internal static class Hunters { public static int Resolve(int value)=>value; }
internal static partial class HunterLicenseClient {
 private sealed class AuthSession { public string AccessToken{get;set;}="SYNTHETIC"; public string RefreshToken{get;set;}="SYNTHETIC"; public long ExpiresAt{get;set;}=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+3600; }
 private static AuthSession? _currentSession;
 private static readonly SemaphoreSlim Gate=new(1);
 internal static int AuthCalls,FunctionCalls;
 internal static object? Replacement;
 internal static Func<object,object>? Reply;
 internal static bool TryGetNativeCareerTicket(uint id,object? lease,out string ticket){ticket="pp1."+new string('x',30);return IsNativeAccountLeaseCurrent(lease);}
 internal static object NewFixtureLease()=>new AuthSession();
 internal static void SetFixtureLease(object lease){_currentSession=(AuthSession)lease;AuthCalls=FunctionCalls=0;Replacement=null;Reply=null;}
 private static Task<AuthSession> AuthenticateAsync(CancellationToken token) {
  ++AuthCalls;
  if(Replacement!=null)_currentSession=(AuthSession)Replacement;
  return Task.FromResult(_currentSession??throw new Exception("Closed auth owner has no fixture session"));
 }
 private static Task<T> FunctionAsync<T>(string access,string function,object body,CancellationToken token) {
  ++FunctionCalls;return Task.FromResult(Reply==null?default(T)!:(T)Reply(body));
 }
}
internal readonly record struct SocialListing(string Address,int Port);
internal readonly record struct SocialStatus(int Protocol,string RoomKey) { public bool PartyReservationsV1{get;init;}public ushort MatchId{get;init;} }
internal readonly record struct SocialEntry(SocialListing Listing,SocialStatus Status,string Name);
internal readonly record struct SocialLobbyVerification(bool Verified,SocialEntry Entry,string Error);
internal static class ServerBrowserService { public static Task<SocialLobbyVerification> FindListedLobbyAsync(ulong epoch,CancellationToken token)=>throw new Exception("Closed directory facade must not run"); }
}
namespace MphRead.Mods.Network {
 internal enum SessionPhase { Lobby,Starting,InMatch,PostMatch }
 internal sealed class MatchDefinition { public string RoomKey=""; }
 internal struct SessionStatePacket { public SessionPhase Phase;public ushort MatchId;public ulong AuthorityEpoch;public MatchDefinition Match; }
 internal static class NetSession { public static bool Active,IsClient,PersistentLobby,SemanticLegacyPlayback;public static int LocalSlot;public static uint ClientId=1234;public static SessionStatePacket? ServerSession;internal static bool BeginNativePartyReservation(PartyReservationRequestPacket packet,object lease)=>throw new Exception("Closed session facade cannot start leader reservation");internal static bool CancelNativePartyReservation(string id,object? lease)=>false; }
 internal static class NetConnection { internal static ulong NewId()=>1; }
 internal static class NetConfig { public const byte ProtocolVersion=57; public const int MaxPacketSize=1472; }
}
