using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPrime.Server.Shared;

namespace MphRead.NetTest;

/// <summary>Roles in the two-human-client fidelity run.</summary>
internal enum TwoClientRole
{
    Shooter,
    Target
}

internal static class TwoClientRoleParser
{
    internal static TwoClientRole Parse(string value) => value switch
    {
        "shooter" => TwoClientRole.Shooter,
        "target" => TwoClientRole.Target,
        _ => throw new ArgumentException("Two-client role must be shooter or target.")
    };

    internal static string Format(TwoClientRole value) => value switch
    {
        TwoClientRole.Shooter => "shooter",
        TwoClientRole.Target => "target",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

/// <summary>
/// Shared binding for both authenticated clients. It contains no wall-clock
/// shot identity; command attribution is carried by TwoClientShotIdentity.
/// </summary>
internal sealed record TwoClientRunBinding(
    Guid RunId, Guid NodeId, Guid NodeIncarnation, Guid WorkerId,
    Guid WorkerIncarnation, Guid MatchId, uint WireMatchId,
    string ContentVersion, string ContentHash, string BuildVersion,
    byte ProtocolVersion, string Scenario, string Room, string NodeControlUri,
    ushort NodePort, string WorkerHost, ushort WorkerPort,
    string EvidenceClass, bool ValidationFixture, bool RenderedWanProof)
{
    internal void Validate()
    {
        if (RunId == Guid.Empty || NodeId == Guid.Empty || NodeIncarnation == Guid.Empty
            || WorkerId == Guid.Empty || WorkerIncarnation == Guid.Empty
            || MatchId == Guid.Empty || WireMatchId == 0
            || ContentVersion is not { Length: >= 1 and <= 128 }
            || ContentVersion.Any(char.IsControl)
            || ContentHash is not { Length: 64 }
            || ContentHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || BuildVersion is not { Length: >= 1 and <= 128 }
            || BuildVersion.Any(char.IsControl)
            || Scenario is not { Length: >= 1 and <= 32 }
            || Scenario.Any(char.IsControl) || Scenario != "headshot"
            || Room is not { Length: >= 1 and <= 128 } || Room.Any(char.IsControl)
            || NodeControlUri is not { Length: >= 12 and <= 512 }
            || NodeControlUri.Any(char.IsControl) || NodePort == 0
            || WorkerHost is not { Length: >= 1 and <= 128 } || WorkerHost.Any(char.IsControl)
            || WorkerPort == 0 || EvidenceClass is not { Length: >= 1 and <= 96 }
            || EvidenceClass.Any(char.IsControl) || ValidationFixture || RenderedWanProof)
            throw new ArgumentException("Two-client run binding is invalid or crosses the fixture/proof boundary.");
        if (!Uri.TryCreate(NodeControlUri, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != Uri.UriSchemeWss || endpoint.HostNameType == UriHostNameType.Unknown
            || endpoint.AbsolutePath != "/v1/control" || endpoint.Port != NodePort
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Two-client Node endpoint is not an exact wss control URI.");
    }
}

/// <summary>Clock-independent attribution identity for one shooter root shot.</summary>
internal readonly record struct TwoClientShotIdentity(
    byte ActorSlot, ulong ActorConnectionId, uint ActorLife, uint CommandSequence)
{
    internal bool IsValid => ActorSlot < 8 && ActorConnectionId != 0 && ActorLife != 0;
}

internal sealed record TwoClientTargetMetrics(
    bool OrdinaryProductionInput, bool CorrectTarget, bool VerticalObserved, bool StrafeObserved,
    bool CloseObserved, bool LongObserved, bool MinimumMovement,
    bool MinimumDuration, int FramesObserved, int FramesOnTarget,
    int AirborneFrames, int MovedFrames, int VerticalFrames, int StrafeFrames,
    int CloseFrames, int LongFrames, int StationaryBodyFrames,
    int StationaryHeadFrames)
{
    internal bool Valid => OrdinaryProductionInput && CorrectTarget
        && VerticalObserved && StrafeObserved
        && CloseObserved && LongObserved && MinimumMovement && MinimumDuration;

    internal void Validate()
    {
        if (FramesObserved < 0 || FramesOnTarget < 0 || AirborneFrames < 0
            || MovedFrames < 0 || VerticalFrames < 0 || StrafeFrames < 0
            || CloseFrames < 0 || LongFrames < 0 || StationaryBodyFrames < 0
            || StationaryHeadFrames < 0 || FramesOnTarget > FramesObserved
            || AirborneFrames > FramesObserved || MovedFrames > FramesObserved
            || VerticalFrames > FramesObserved || StrafeFrames > FramesObserved
            || CloseFrames > FramesObserved || LongFrames > FramesObserved
            || StationaryBodyFrames > FramesObserved || StationaryHeadFrames > FramesObserved)
            throw new ArgumentException("Two-client target metrics contain negative counters.");
    }
}

/// <summary>N2 gate/counter projection emitted by the shooter client.</summary>
internal sealed record TwoClientN2Metrics(
    bool OrdinaryProductionInput, bool CorrectWeapon, bool AuthoritativeWeapon,
    bool Zoomed, bool AmmoAvailable, bool LegalCadence, bool HeadAimObserved,
    bool MinimumDuration, bool ChoreographyValid, bool ShotCorrelationValid,
    bool CombatCoverageValid, bool HeadshotEvidenceValid, int TriggerAttempts,
    int LocalRootShots,
    int AuthoritativeRootShots, int CorrelatedRootShots, int PredictedContacts,
    int PredictedHeadshots, int AuthoritativeHits, int AuthoritativeHeadshots,
    int ConfirmedHeadshots, int DowngradedHeadshots, int PromotedHeadshots,
    int DeniedHeadshots, double? HeadshotAgreementRate)
{
    internal bool ShooterInputValid => OrdinaryProductionInput && CorrectWeapon
        && AuthoritativeWeapon && Zoomed && AmmoAvailable && LegalCadence
        && HeadAimObserved && MinimumDuration;

    internal bool Valid => ShooterInputValid && ChoreographyValid && ShotCorrelationValid
        && CombatCoverageValid && HeadshotEvidenceValid;

    internal void Validate()
    {
        if (TriggerAttempts < 0 || LocalRootShots < 0 || AuthoritativeRootShots < 0
            || CorrelatedRootShots < 0 || PredictedContacts < 0
            || PredictedHeadshots < 0 || AuthoritativeHits < 0
            || AuthoritativeHeadshots < 0 || ConfirmedHeadshots < 0
            || DowngradedHeadshots < 0 || PromotedHeadshots < 0 || DeniedHeadshots < 0
            || (HeadshotAgreementRate is { } rate && (!double.IsFinite(rate) || rate < 0)))
            throw new ArgumentException("Two-client N2 metrics contain invalid counters.");
        if (CorrelatedRootShots > LocalRootShots || CorrelatedRootShots > AuthoritativeRootShots
            || PredictedHeadshots > PredictedContacts || AuthoritativeHeadshots > AuthoritativeHits
            || ConfirmedHeadshots > PredictedHeadshots || ConfirmedHeadshots > AuthoritativeHeadshots)
            throw new ArgumentException("Two-client N2 counters are internally inconsistent.");
        if (HeadshotAgreementRate is { } agreement && agreement > 1)
            throw new ArgumentException("Two-client headshot agreement must be in the inclusive 0..1 range.");
        if (HeadshotAgreementRate is not null && !HeadshotEvidenceValid)
            throw new ArgumentException("Headshot agreement cannot be meaningful when evidence is invalid.");
    }
}

internal sealed record TwoClientClientMetrics(
    long SubmittedFrames, long AcknowledgedFrames, long SnapshotsReceived,
    long CombatEvents, long DamageEvents, long PacketsSent, long PacketsReceived,
    long PacketsDropped, double MeasuredRttMs, double MeasuredJitterMs,
    double PresentedPoseError, double SpeculativeHitPresentedPoseDistance,
    uint RequestedRewindTicks, uint ValidatedRewindTicks, double ClampPositionError,
    double ClampVerticalError)
{
    internal void Validate()
    {
        if (SubmittedFrames < 0 || AcknowledgedFrames < 0 || SnapshotsReceived < 0
            || CombatEvents < 0 || DamageEvents < 0 || PacketsSent < 0
            || PacketsReceived < 0 || PacketsDropped < 0
            || AcknowledgedFrames > SubmittedFrames
            || !double.IsFinite(MeasuredRttMs) || MeasuredRttMs < 0
            || !double.IsFinite(MeasuredJitterMs) || MeasuredJitterMs < 0
            || !double.IsFinite(PresentedPoseError) || PresentedPoseError < 0
            || !double.IsFinite(SpeculativeHitPresentedPoseDistance)
            || SpeculativeHitPresentedPoseDistance < 0
            || !double.IsFinite(ClampPositionError) || ClampPositionError < 0
            || !double.IsFinite(ClampVerticalError) || ClampVerticalError < 0)
            throw new ArgumentException("Two-client client metrics are invalid.");
    }
}

internal sealed record TwoClientWorkerMetrics(
    bool Completed, bool WorkerTargetChoreography, long TickCount,
    long SnapshotCount, long ShotEvents, long DamageEvents,
    uint MaximumRequestedRewindTicks, uint MaximumValidatedRewindTicks)
{
    internal void Validate()
    {
        if (TickCount < 0 || SnapshotCount < 0 || ShotEvents < 0 || DamageEvents < 0
            || MaximumValidatedRewindTicks > MaximumRequestedRewindTicks
            || WorkerTargetChoreography)
            throw new ArgumentException("Worker metrics are incomplete or contain forbidden target choreography.");
    }
}

internal sealed record TwoClientParticipantReport(
    string Schema, Guid RunId, TwoClientRole Role, TwoClientRunBinding Binding,
    Guid SubjectId, byte SeatId, ulong ConnectionId, DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc, bool ClientCompleted, bool RuntimePassed,
    TwoClientClientMetrics ClientMetrics, TwoClientN2Metrics N2,
    TwoClientTargetMetrics Target, TwoClientShotIdentity[] ShotIdentities,
    string[] FailureReasons)
{
    internal const string CurrentSchema = "project-prime.rendered-wan-two-client.v1";

    internal void Validate(DateTimeOffset now)
    {
        if (Schema != CurrentSchema || RunId == Guid.Empty || SubjectId == Guid.Empty
            || !Enum.IsDefined(Role) || ConnectionId == 0 || StartedUtc == default
            || EndedUtc < StartedUtc || EndedUtc - StartedUtc > TimeSpan.FromMinutes(15)
            || Binding.RunId != RunId || Binding.RenderedWanProof
            || Binding.ValidationFixture || FailureReasons is null
            || ShotIdentities is null
            || FailureReasons.Any(reason => reason is null || reason.Length > 256
                || reason.Any(char.IsControl)))
            throw new ArgumentException("Two-client participant report is invalid.");
        Binding.Validate();
        ClientMetrics.Validate();
        N2.Validate();
        Target.Validate();
        if (Binding.EvidenceClass == "real-wan-proof")
            throw new ArgumentException("Two-client tooling cannot claim WAN proof.");
        if (now > EndedUtc.AddMinutes(15))
            throw new ArgumentException("Two-client participant report is stale.");
        var identities = new HashSet<TwoClientShotIdentity>();
        foreach (TwoClientShotIdentity identity in ShotIdentities ?? [])
        {
            if (!identity.IsValid || identity.ActorSlot != SeatId || !identities.Add(identity))
                throw new ArgumentException("Two-client shot identities are not unique and life-fenced.");
        }
    }
}

internal sealed record TwoClientExpectedParticipant(
    Guid SubjectId, TwoClientRole Role, byte SeatId, ulong ConnectionId,
    bool IsBot)
{
    internal void Validate()
    {
        if (SubjectId == Guid.Empty || !Enum.IsDefined(Role) || ConnectionId == 0 || IsBot)
            throw new ArgumentException("Two-client expected participant is invalid.");
    }
}

internal sealed record TwoClientServerReport(
    string Schema, Guid RunId, TwoClientRunBinding Binding,
    TwoClientExpectedParticipant[] Participants, DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc, bool Completed, TwoClientWorkerMetrics Worker,
    string[] FailureReasons)
{
    internal const string CurrentSchema = "project-prime.rendered-wan-two-client-server.v1";

    internal void Validate(DateTimeOffset now)
    {
        if (Schema != CurrentSchema || RunId == Guid.Empty || Binding.RunId != RunId
            || StartedUtc == default || EndedUtc < StartedUtc
            || EndedUtc - StartedUtc > TimeSpan.FromMinutes(15)
            || Participants is null || Participants.Length != 2
            || Participants.Any(value => value is null)
            || FailureReasons is null || FailureReasons.Any(reason => reason is null
                || reason.Length > 256 || reason.Any(char.IsControl)))
            throw new ArgumentException("Two-client server report is invalid.");
        Binding.Validate();
        Worker.Validate();
        foreach (TwoClientExpectedParticipant participant in Participants) participant.Validate();
        if (Participants.Select(value => value.SubjectId).Distinct().Count() != 2
            || Participants.Select(value => value.SeatId).Distinct().Count() != 2
            || Participants.Select(value => value.ConnectionId).Distinct().Count() != 2
            || Participants.Any(value => value.IsBot))
            throw new ArgumentException("Two-client server report contains an unexpected participant.");
        if (now > EndedUtc.AddMinutes(15))
            throw new ArgumentException("Two-client server report is stale.");
    }
}

internal sealed record TwoClientMergeReport(
    string Schema, Guid RunId, TwoClientRunBinding Binding,
    DateTimeOffset StartedUtc, DateTimeOffset EndedUtc,
    bool ChoreographyValid, bool ShotCorrelationValid, bool CombatCoverageValid,
    bool HeadshotEvidenceValid, bool ScenarioValid, int TriggerAttempts,
    int LocalRootShots, int AuthoritativeRootShots, int CorrelatedRootShots,
    int PredictedContacts, int PredictedHeadshots, int AuthoritativeHits,
    int AuthoritativeHeadshots, int ConfirmedHeadshots, int DowngradedHeadshots,
    int PromotedHeadshots, int DeniedHeadshots, double? HeadshotAgreementRate,
    TwoClientClientMetrics ShooterClient, TwoClientClientMetrics TargetClient,
    TwoClientWorkerMetrics Worker, string EvidenceClass, bool RenderedWanProof,
    bool RequiresHumanVisualReview, string[] InvalidReasons)
{
    internal const string CurrentSchema = "project-prime.rendered-wan-two-client-merge.v1";
}

internal static class TwoClientWanMerge
{
    internal static TwoClientMergeReport Merge(TwoClientServerReport server,
        TwoClientParticipantReport shooter, TwoClientParticipantReport target,
        DateTimeOffset now)
    {
        server.Validate(now);
        shooter.Validate(now);
        target.Validate(now);
        if (shooter.Role != TwoClientRole.Shooter || target.Role != TwoClientRole.Target)
            throw new ArgumentException("Two-client merge requires exactly one shooter and one target report.");
        ValidateSameBinding(server.Binding, shooter.Binding);
        ValidateSameBinding(server.Binding, target.Binding);
        if (shooter.SubjectId == target.SubjectId || shooter.SeatId == target.SeatId
            || shooter.ConnectionId == target.ConnectionId)
            throw new ArgumentException("Two-client reports do not represent independent authenticated clients.");
        TwoClientExpectedParticipant expectedShooter = server.Participants.Single(value
            => value.Role == TwoClientRole.Shooter);
        TwoClientExpectedParticipant expectedTarget = server.Participants.Single(value
            => value.Role == TwoClientRole.Target);
        if (expectedShooter.SubjectId != shooter.SubjectId
            || expectedShooter.SeatId != shooter.SeatId
            || expectedShooter.ConnectionId != shooter.ConnectionId
            || expectedTarget.SubjectId != target.SubjectId
            || expectedTarget.SeatId != target.SeatId
            || expectedTarget.ConnectionId != target.ConnectionId)
            throw new ArgumentException("Two-client reports do not match the authenticated Worker roster.");

        var identities = new HashSet<TwoClientShotIdentity>();
        foreach (TwoClientShotIdentity identity in shooter.ShotIdentities)
            if (!identities.Add(identity)) throw new ArgumentException("Duplicate clock-independent shot identity.");
        if (shooter.N2.CorrelatedRootShots > identities.Count)
            throw new ArgumentException("Correlated roots exceed the supplied life-fenced shot identities.");

        bool choreography = shooter.N2.ChoreographyValid && shooter.N2.ShooterInputValid
            && target.Target.Valid
            && server.Completed && server.Worker.Completed && shooter.ClientCompleted
            && shooter.RuntimePassed && target.ClientCompleted && target.RuntimePassed;
        bool correlation = shooter.N2.ShotCorrelationValid
            && shooter.N2.CorrelatedRootShots > 0
            && shooter.N2.CorrelatedRootShots <= identities.Count;
        bool coverage = shooter.N2.CombatCoverageValid;
        bool evidence = shooter.N2.HeadshotEvidenceValid
            && shooter.N2.HeadshotAgreementRate is { } rate and >= 0 and <= 1;
        bool scenario = choreography && correlation && coverage && evidence;
        var failures = new List<string>();
        if (!choreography) failures.Add("choreography-invalid");
        if (!correlation) failures.Add("shot-correlation-invalid");
        if (!coverage) failures.Add("combat-coverage-invalid");
        if (!evidence) failures.Add("headshot-evidence-invalid");
        double? agreement = scenario ? shooter.N2.HeadshotAgreementRate : null;

        DateTimeOffset started = new[] { server.StartedUtc, shooter.StartedUtc, target.StartedUtc }.Min();
        DateTimeOffset ended = new[] { server.EndedUtc, shooter.EndedUtc, target.EndedUtc }.Max();
        return new(TwoClientMergeReport.CurrentSchema, server.RunId, server.Binding,
            started, ended,
            choreography, correlation, coverage, evidence, scenario,
            shooter.N2.TriggerAttempts, shooter.N2.LocalRootShots,
            shooter.N2.AuthoritativeRootShots, shooter.N2.CorrelatedRootShots,
            shooter.N2.PredictedContacts, shooter.N2.PredictedHeadshots,
            shooter.N2.AuthoritativeHits, shooter.N2.AuthoritativeHeadshots,
            shooter.N2.ConfirmedHeadshots, shooter.N2.DowngradedHeadshots,
            shooter.N2.PromotedHeadshots, shooter.N2.DeniedHeadshots, agreement,
            shooter.ClientMetrics, target.ClientMetrics, server.Worker,
            server.Binding.EvidenceClass, RenderedWanProof: false,
            RequiresHumanVisualReview: true, failures.ToArray());
    }

    private static void ValidateSameBinding(TwoClientRunBinding expected,
        TwoClientRunBinding actual)
    {
        if (expected != actual)
            throw new ArgumentException("Two-client reports are not bound to the same run, match, Worker, or content.");
    }
}

internal sealed record TwoClientDescriptor(
    string Schema, Guid RunId, TwoClientRole Role, Guid SubjectId,
    TwoClientRunBinding Binding, string NodeControlUri, string CertificateSha256,
    string SpkiSha256, DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc, int TimeoutSeconds)
{
    internal const string CurrentSchema = "project-prime.rendered-wan-two-client-descriptor.v1";

    internal void Validate(DateTimeOffset now)
    {
        if (Schema != CurrentSchema || RunId == Guid.Empty || SubjectId == Guid.Empty
            || Binding.RunId != RunId || !Enum.IsDefined(Role)
            || NodeControlUri is not { Length: >= 12 and <= 512 }
            || NodeControlUri.Any(char.IsControl) || IssuedUtc == default
            || CertificateSha256 is not { Length: 64 }
            || CertificateSha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || SpkiSha256 is not { Length: 64 }
            || SpkiSha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || ExpiresUtc <= IssuedUtc || ExpiresUtc - IssuedUtc > TimeSpan.FromMinutes(10)
            || TimeoutSeconds is < 30 or > 600 || now > ExpiresUtc)
            throw new ArgumentException("Two-client descriptor is expired, unbounded, or invalid.");
        if (!Uri.TryCreate(NodeControlUri, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != Uri.UriSchemeWss || endpoint.HostNameType == UriHostNameType.Unknown
            || endpoint.AbsolutePath != "/v1/control" || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || endpoint.Port is <= 0 or > ushort.MaxValue)
            throw new ArgumentException("Two-client descriptor Node endpoint is not an exact wss control URI.");
        Binding.Validate();
        if (Binding.NodeControlUri != NodeControlUri || Binding.NodePort != endpoint.Port)
            throw new ArgumentException("Two-client descriptor Node endpoint does not match its run binding.");
    }
}

internal sealed record TwoClientSecret(
    string Schema, Guid RunId, Guid SubjectId, DateTimeOffset ExpiresUtc, string Ticket)
{
    internal const string CurrentSchema = "project-prime.rendered-wan-two-client-secret.v1";

    internal void Validate(TwoClientDescriptor descriptor, DateTimeOffset now)
    {
        if (Schema != CurrentSchema || RunId != descriptor.RunId
            || SubjectId != descriptor.SubjectId || ExpiresUtc != descriptor.ExpiresUtc
            || Ticket is not { Length: > 0 and <= 4096 }
            || Ticket.Any(value => value is < (char)33 or > (char)126) || now > ExpiresUtc)
            throw new ArgumentException("Two-client secret is stale or does not match its descriptor.");
    }
}

/// <summary>
/// Strict report codec shared by the offline merge consumer and the local
/// self-tests. Duplicate JSON properties are rejected before deserialization;
/// reports are never accepted through a permissive fallback parser.
/// </summary>
internal static class RenderedWanReportCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    internal static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > 4 * 1024 * 1024)
            throw new JsonException("Two-client report is missing or oversized.");
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray(),
            new JsonDocumentOptions { MaxDepth = 32 });
        NodeControlCodec.RejectDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new JsonException("Two-client report is missing.");
    }
}

internal static partial class RenderedWanValidationCheck
{
    /// <summary>
    /// Strict offline consumer for the two real-client reports. It only
    /// merges reports that independently bind the same authenticated run,
    /// match, Worker, and content. A pre-existing output file is rejected.
    /// </summary>
    internal static int TwoClientMerge(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(
                "Usage: nettest --rendered-wan-two-client-merge SERVER_REPORT SHOOTER_REPORT TARGET_REPORT OUTPUT_JSON");
            return 2;
        }
        string serverPath = Path.GetFullPath(args[1]);
        string shooterPath = Path.GetFullPath(args[2]);
        string targetPath = Path.GetFullPath(args[3]);
        string outputPath = Path.GetFullPath(args[4]);
        if (new[] { serverPath, shooterPath, targetPath }.Contains(outputPath,
                StringComparer.Ordinal))
        {
            Console.Error.WriteLine("The merge output must not overwrite an input report.");
            return 2;
        }
        try
        {
            TwoClientServerReport server = ReadTwoClientReport<TwoClientServerReport>(serverPath);
            TwoClientParticipantReport shooter = ReadTwoClientReport<TwoClientParticipantReport>(shooterPath);
            TwoClientParticipantReport target = ReadTwoClientReport<TwoClientParticipantReport>(targetPath);
            TwoClientMergeReport merged = TwoClientWanMerge.Merge(
                server, shooter, target, DateTimeOffset.UtcNow);
            WriteTwoClientReport(outputPath, merged);
            Console.WriteLine($"RENDERED_WAN_TWO_CLIENT_MERGE result=MERGED run={merged.RunId:D} scenarioValid={merged.ScenarioValid} renderedWanProof=false humanReview=true output={outputPath}");
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException
            or IOException or JsonException)
        {
            Console.Error.WriteLine($"RENDERED_WAN_TWO_CLIENT_MERGE result=FAIL reason={error.Message}");
            return 1;
        }
    }

    internal static int TwoClientSelfTest()
    {
        int cases = 0;
        if (TwoClientRoleParser.Parse("shooter") != TwoClientRole.Shooter
            || TwoClientRoleParser.Format(TwoClientRole.Target) != "target")
            throw new InvalidOperationException("Two-client role parser failed.");
        try { TwoClientRoleParser.Parse("bot"); throw new InvalidOperationException("Invalid role accepted."); }
        catch (ArgumentException) { cases++; }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid run = Guid.NewGuid(), node = Guid.NewGuid(), nodeIncarnation = Guid.NewGuid();
        Guid worker = Guid.NewGuid(), workerIncarnation = Guid.NewGuid(), match = Guid.NewGuid();
        TwoClientRunBinding binding = new(run, node, nodeIncarnation, worker,
            workerIncarnation, match, 77, "AMHE1", new string('a', 64), "build",
            1, "headshot", "MP1 SANCTORUS", "wss://198.51.100.10:27010/v1/control", 27010,
            "198.51.100.10", 27020,
            "non-loopback-path-candidate-unverified", false, false);
        var shooterIds = Enumerable.Range(0, 36)
            .Select(sequence => new TwoClientShotIdentity(1, 1001, 4, (uint)sequence))
            .ToArray();
        var targetId = new TwoClientShotIdentity(2, 1002, 8, 0);
        TwoClientClientMetrics clientMetrics = new(600, 590, 300, 20, 10,
            1000, 900, 2, 100, 3, 0.2, 0.4, 2, 1, 0.1, 0.1);
        TwoClientN2Metrics n2 = new(true, true, true, true, true, true, true, true,
            true, true, true, true, 36, 36, 36, 36,
            30, 20, 20, 12, 10, 1, 1, 0, 0.5);
        TwoClientTargetMetrics motion = new(true, true, true, true, true, true, true, true,
            600, 500, 200, 400, 200, 200, 300, 300, 25, 25);
        TwoClientWorkerMetrics workerMetrics = new(true, false, 600, 300, 36, 20, 20, 20);
        TwoClientExpectedParticipant shooterParticipant = new(Guid.NewGuid(),
            TwoClientRole.Shooter, 1, 1001, false);
        TwoClientExpectedParticipant targetParticipant = new(Guid.NewGuid(),
            TwoClientRole.Target, 2, 1002, false);
        TwoClientServerReport server = new(TwoClientServerReport.CurrentSchema, run, binding,
            [shooterParticipant, targetParticipant], now.AddSeconds(-5), now,
            true, workerMetrics, []);
        TwoClientParticipantReport shooter = new(TwoClientParticipantReport.CurrentSchema,
            run, TwoClientRole.Shooter, binding, shooterParticipant.SubjectId, 1, 1001,
            now.AddSeconds(-5), now, true, true, clientMetrics, n2, motion, shooterIds, []);
        TwoClientParticipantReport target = new(TwoClientParticipantReport.CurrentSchema,
            run, TwoClientRole.Target, binding, targetParticipant.SubjectId, 2, 1002,
            now.AddSeconds(-5), now, true, true, clientMetrics, n2, motion, [targetId], []);
        TwoClientMergeReport merged = TwoClientWanMerge.Merge(server, shooter, target, now);
        if (!merged.ScenarioValid || !merged.ChoreographyValid
            || !merged.ShotCorrelationValid || !merged.CombatCoverageValid
            || !merged.HeadshotEvidenceValid || merged.RenderedWanProof
            || merged.HeadshotAgreementRate is null)
            throw new InvalidOperationException("Two-client valid merge failed.");
        cases++;

        byte[] encoded = RenderedWanReportCodec.Serialize(shooter);
        TwoClientParticipantReport decoded = RenderedWanReportCodec
            .Deserialize<TwoClientParticipantReport>(encoded);
        decoded.Validate(now);
        if (decoded.Binding != shooter.Binding || !decoded.ShotIdentities.SequenceEqual(shooter.ShotIdentities))
            throw new InvalidOperationException("Two-client report JSON round-trip changed identity binding.");
        cases++;
        string jsonText = Encoding.UTF8.GetString(encoded).TrimEnd();
        byte[] duplicate = Encoding.UTF8.GetBytes(
            jsonText[..^1] + ",\"schema\":\"duplicate\"}");
        try { _ = RenderedWanReportCodec.Deserialize<TwoClientParticipantReport>(duplicate); throw new InvalidOperationException("Duplicate JSON property accepted."); }
        catch (JsonException) { cases++; }
        byte[] unknown = Encoding.UTF8.GetBytes(
            jsonText[..^1] + ",\"unexpected\":1}");
        try { _ = RenderedWanReportCodec.Deserialize<TwoClientParticipantReport>(unknown); throw new InvalidOperationException("Unknown JSON property accepted."); }
        catch (JsonException) { cases++; }

        TwoClientSecret secret = new(TwoClientSecret.CurrentSchema, run,
            shooter.SubjectId, now.AddMinutes(2), "ticket");
        TwoClientDescriptor descriptor = new(TwoClientDescriptor.CurrentSchema, run,
            TwoClientRole.Shooter, shooter.SubjectId, binding,
            "wss://198.51.100.10:27010/v1/control", new string('b', 64),
            new string('c', 64), now.AddSeconds(-1),
            now.AddMinutes(2), 120);
        descriptor.Validate(now); secret.Validate(descriptor, now); cases++;
        try { secret.Validate(descriptor with { ExpiresUtc = now.AddMinutes(1) }, now); throw new InvalidOperationException("Mismatched secret accepted."); }
        catch (ArgumentException) { cases++; }
        try
        {
            (secret with { Ticket = "ticket with spaces" }).Validate(descriptor, now);
            throw new InvalidOperationException("Whitespace-bearing secret accepted.");
        }
        catch (ArgumentException) { cases++; }

        try
        {
            _ = TwoClientWanMerge.Merge(server with { Binding = binding with { MatchId = Guid.NewGuid() } }, shooter, target, now);
            throw new InvalidOperationException("Mismatched match accepted.");
        }
        catch (ArgumentException) { cases++; }
        try
        {
            _ = TwoClientWanMerge.Merge(server, shooter with { Binding = binding with { ValidationFixture = true } }, target, now);
            throw new InvalidOperationException("Developer fixture crossed N6 boundary.");
        }
        catch (ArgumentException) { cases++; }
        try
        {
            _ = TwoClientWanMerge.Merge(server, shooter with { ConnectionId = target.ConnectionId }, target, now);
            throw new InvalidOperationException("Duplicate authenticated client accepted.");
        }
        catch (ArgumentException) { cases++; }

        TwoClientMergeReport noEvidence = TwoClientWanMerge.Merge(server,
            shooter with { N2 = n2 with { HeadshotEvidenceValid = false, HeadshotAgreementRate = null } },
            target, now);
        if (noEvidence.ScenarioValid || noEvidence.HeadshotAgreementRate != null)
            throw new InvalidOperationException("N6 published agreement without headshot evidence.");
        cases++;
        Console.WriteLine($"RENDERED_WAN_TWO_CLIENT_SELF_TEST result=PASS cases={cases}");
        return 0;
    }

    private static T ReadTwoClientReport<T>(string path)
    {
        FileInfo info = new(path);
        if (!info.Exists || info.Length is < 2 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Two-client report is missing or oversized.");
        return RenderedWanReportCodec.Deserialize<T>(File.ReadAllBytes(path));
    }

    private static void WriteTwoClientReport(string path, object value)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null) throw new ArgumentException("Merge output has no parent directory.");
        Directory.CreateDirectory(parent);
        byte[] bytes = RenderedWanReportCodec.Serialize(value);
        var options = new FileStreamOptions
        {
            Access = FileAccess.Write, Mode = FileMode.CreateNew,
            Share = FileShare.None, Options = FileOptions.WriteThrough
        };
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }
}
