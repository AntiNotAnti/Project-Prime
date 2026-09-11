using System;
using ProjectPrime.Server.Shared;
using MphRead.Admin;
using MphRead.Identity;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

/// <summary>One immutable launch spec plus host-provided integration settings.</summary>
public sealed record MatchInstanceOptions(MatchSpec Spec, uint WireMatchId)
{
    internal bool LegacyDynamicAdmission { get; init; }
    internal Func<bool>? LegacyReplayMayOpen { get; init; }
    public uint InitialTick { get; init; }
    public int SnapshotRateHz { get; init; } = SnapshotCadence.DefaultRateHz;
    public bool AdaptiveTimingEnabled { get; init; }
    public bool AdaptiveTimingV2Enabled { get; init; }
    public bool AdaptiveInputPlayoutEnabled { get; init; }
    public bool ReliableAdaptiveRtoEnabled { get; init; }
    /// <summary>Experimental one-tick ACK coalescing; conservative default is off.</summary>
    public bool AckCoalescingEnabled { get; init; }
    /// <summary>Production matches require the authenticated UDP admission path.</summary>
    public bool UdpAuthenticationEnabled { get; init; } = true;
    public bool LagCompEnabled { get; init; } = true;
    public bool ProjectileCatchUpEnabled { get; init; } = true;
    /// <summary>QZ1 dynamic collision is disabled until WAN validation.</summary>
    public bool HistoricalDynamicCollisionEnabled { get; init; }
    internal DeveloperValidationFixtureId ValidationFixture { get; init; }
    /// <summary>Test-only deterministic target choreography for F4.</summary>
    internal bool HeadshotValidationScenario { get; init; }
    internal int HeadshotScenarioSeconds { get; init; } = 15;
    public BotFillPolicy? BotFill { get; init; }
    public ObserverOptions Observers { get; init; } = new();
    public string ServerName { get; init; } = "Project Prime";
    public IServerTicketAuthority? Tickets { get; init; }
    public string? ReplayDirectory { get; init; }
    public bool RequireReplay { get; init; }
    public bool CollectTelemetry { get; init; }
    public Guid? ReportingServerId { get; init; }
    public MatchContentSnapshot? ContentSnapshot { get; init; }
    public bool EnableAdmin { get; init; }
    // A host may resolve and receive next-round selections; the match never applies them.
    public Func<string?, string?, MatchRules>? ResolveAdminSelection { get; init; }
    public Action<MatchRules>? AdminSelectionRequested { get; init; }
}
