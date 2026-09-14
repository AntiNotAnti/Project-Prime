using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Trait("LifecycleFast", "true")]
public sealed class AdmissionKeyIpcTests
{
    [Fact]
    public void AdmissionKeyCommandAndAcknowledgementRoundTripWithoutLeakingSecret()
    {
        Guid admissionId = Guid.NewGuid();
        string key = Convert.ToBase64String(new byte[AdmissionKeyRules.ByteLength]);
        var command = new InstallAdmissionKey(admissionId, Guid.NewGuid(), Guid.NewGuid(),
            new(Guid.NewGuid()), Guid.NewGuid(), new(Guid.NewGuid()), new(7),
            new(Guid.NewGuid()), Guid.NewGuid(), 3, 19, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60, key,
            HandoffGeneration.Initial);

        WorkerMessage decoded = WorkerIpcCodec.Decode(WorkerIpcCodec.Encode(command));

        var roundTrip = Assert.IsType<InstallAdmissionKey>(decoded);
        Assert.Equal(command, roundTrip);
        Assert.DoesNotContain(key, command.ToString(), StringComparison.Ordinal);

        var acknowledgement = new AdmissionKeyInstalled(command.AdmissionId, command.TicketId,
            command.NodeSessionId, command.NodeId, command.NodeIncarnation, command.MatchId,
            command.WireMatchId, command.WorkerId, command.WorkerIncarnation, command.SeatId,
            command.JoinNonce, command.ExpiresAt, HandoffGeneration.Initial);
        Assert.Equal(acknowledgement, Assert.IsType<AdmissionKeyInstalled>(
            WorkerIpcCodec.Decode(WorkerIpcCodec.Encode(acknowledgement))));

        var legacy = new MatchAdminResult(command.MatchId, AdminAction.Resume, true, null, null);
        Assert.Equal((byte)19, WorkerIpcCodec.Encode(legacy)[4]);
        Assert.Equal((byte)20, WorkerIpcCodec.Encode(command)[4]);
    }

    [Fact]
    public void AdmissionKeyRejectsNonCanonicalOrWrongLengthMaterial()
    {
        Assert.Throws<ArgumentException>(() => AdmissionKeyRules.Validate(Convert.ToBase64String(new byte[31])));
        Assert.Throws<ArgumentException>(() => AdmissionKeyRules.Validate(Convert.ToBase64String(new byte[33])));
        Assert.Throws<ArgumentException>(() => AdmissionKeyRules.Validate(new string('A', 44)));
    }

    [Fact]
    public void HandoffCarriesAnExplicitAuthenticationModeAndFailsClosedOnMismatch()
    {
        Guid match = Guid.NewGuid();
        var legacy = new NodeMatchHandoff(match, 7, "127.0.0.1", 27000,
            "ticket", 9, false, MphRead.Hunter.Samus);
        Assert.False(legacy.UdpAuthenticationEnabled);
        legacy.Validate();

        string key = Convert.ToBase64String(new byte[AdmissionKeyRules.ByteLength]);
        var authenticated = new NodeMatchHandoff(match, 7, "127.0.0.1", 27000,
            "ticket", 9, false, MphRead.Hunter.Samus, Guid.NewGuid(), key, true);
        authenticated.Validate();
        NodeMatchHandoff missingKey = authenticated with { AdmissionKey = "" };
        NodeMatchHandoff unexpectedKey = legacy with { AdmissionId = Guid.NewGuid(), AdmissionKey = key };
        Assert.Throws<ArgumentException>(missingKey.Validate);
        Assert.Throws<ArgumentException>(unexpectedKey.Validate);
    }
}
