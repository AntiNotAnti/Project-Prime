namespace MphRead.Backend;

/// <summary>Names and limits shared by the explicit Backend route policies.</summary>
public static class BackendRoutePolicy
{
    public const string Auth = "auth";
    public const string GuestAuth = "guest-auth";
    public const string Api = "api";
    public const string Presence = "presence";
    public const string MachinePreAuth = "machine";

    public const int AuthPermitsPerMinute = 20;
    public const int GuestAuthPermitsPerMinute = 20;
    public const int ApiPermitsPerMinute = 120;
    public const int PresencePermitsPerMinute = 60;
    public const int MachinePreAuthPermitsPerMinute = 60;
    public const int AuthenticatedNodePermitsPerMinute = 60;
}
