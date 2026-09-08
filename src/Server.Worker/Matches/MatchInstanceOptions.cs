using System;
using FruityPrime.Server.Shared;
using MphRead.Admin;
using MphRead.Identity;

namespace MphRead.Mods.Network;

/// <summary>One immutable launch spec plus host-provided integration settings.</summary>
public sealed record MatchInstanceOptions(MatchSpec Spec, uint WireMatchId)
{
    internal bool LegacyDynamicAdmission { get; init; }
    internal Func<bool>? LegacyReplayMayOpen { get; init; }
    public uint InitialTick { get; init; }
    public bool LagCompEnabled { get; init; } = true;
    public bool ProjectileCatchUpEnabled { get; init; } = true;
    public BotFillPolicy? BotFill { get; init; }
    public ObserverOptions Observers { get; init; } = new();
    public string ServerName { get; init; } = "Prime Hunters";
    public IServerTicketAuthority? Tickets { get; init; }
    public string? ReplayDirectory { get; init; }
    public bool RequireReplay { get; init; }
    public bool CollectTelemetry { get; init; }
    public Guid? ReportingServerId { get; init; }
    public bool EnableAdmin { get; init; }
    // A host may resolve and receive next-round selections; the match never applies them.
    public Func<string?, string?, MatchRules>? ResolveAdminSelection { get; init; }
    public Action<MatchRules>? AdminSelectionRequested { get; init; }
}
