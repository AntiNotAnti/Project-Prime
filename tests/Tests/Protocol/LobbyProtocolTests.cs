using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class LobbyProtocolTests
{
    private static MatchRules Rules() => MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");

    [Fact]
    public void AcceptedAdmissionCarriesExplicitSessionAndDestination()
    {
        var lobby = new JoinAcceptedPacket(11, 3, 77, JoinDestination.Lobby, 0, 900, 60, Rules());
        byte[] bytes = new byte[JoinAcceptedPacket.Size];
        lobby.Write(bytes);
        Assert.True(JoinAcceptedPacket.TryRead(bytes, out JoinAcceptedPacket decoded));
        Assert.Equal(lobby, decoded);
        Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(9)));
        Assert.Equal((byte)JoinDestination.Lobby, bytes[13]);
        Assert.Equal(0u, decoded.MatchId);
        bytes[14] = 1;
        Assert.False(JoinAcceptedPacket.TryRead(bytes, out _));

        var observer = new JoinAcceptedPacket(12, byte.MaxValue, 77, JoinDestination.Match, 9, 901, 60, Rules());
        observer.Write(bytes);
        Assert.True(JoinAcceptedPacket.TryRead(bytes, out decoded));
        Assert.True(decoded.IsObserver);
        bytes[13] = (byte)JoinDestination.Lobby;
        Assert.False(JoinAcceptedPacket.TryRead(bytes, out _));
    }

    [Fact]
    public void FullLobbySnapshotFitsOneReliableDatagramAndRoundTrips()
    {
        var members = ImmutableArray.CreateBuilder<LobbySnapshotMember>(24);
        for (byte slot = 0; slot < 8; slot++)
        {
            LobbyWireIdentityKind identity = slot == 0 ? LobbyWireIdentityKind.Guest
                : slot == 1 ? LobbyWireIdentityKind.Registered : LobbyWireIdentityKind.Bot;
            LobbyMemberFlags flags = LobbyMemberFlags.Ready;
            if (slot == 0) flags |= LobbyMemberFlags.Host | LobbyMemberFlags.Admin;
            if (slot == 1) flags |= LobbyMemberFlags.Loading;
            members.Add(new(slot, (ulong)slot + 1, $"PLAYER{slot}", (Hunter)slot, slot, identity, flags,
                (ushort)(20 + slot), slot));
        }
        for (int index = 0; index < 16; index++)
        {
            LobbyMemberFlags flags = LobbyMemberFlags.Observer;
            if (index == 0) flags |= LobbyMemberFlags.Loading;
            members.Add(new(byte.MaxValue, (ulong)index + 100, $"OBSERVER{index}", Hunter.Samus,
                byte.MaxValue, LobbyWireIdentityKind.Guest, flags, 50));
        }
        var snapshot = new LobbySnapshotPacket(7, uint.MaxValue, LobbyPhase.Open,
            LobbyPolicyKind.PersistentLobby, ReadyRequired: true, MinimumPlayers: 2,
            HostMayForceStart: true, LobbyPermissions.Admin, BotFillEnabled: true,
            BotMinimumParticipants: 6, BotSkill: 2, 6, Rules(), members.MoveToImmutable());
        byte[] bytes = new byte[LobbySnapshotPacket.MaximumSize];
        Assert.Equal(876, snapshot.Write(bytes));
        Assert.Equal(LobbySnapshotPacket.MaximumSize, bytes.Length);
        Assert.True(bytes.Length <= ReliableChannel.MaxPayloadSize);
        Assert.Equal(NetConfig.MaxPacketSize, NetHeader.Size + ReliableEventPacket.HeaderSize + ReliableChannel.MaxPayloadSize);
        Assert.True(LobbySnapshotPacket.TryRead(bytes, out LobbySnapshotPacket? decoded));
        Assert.Equal(24, decoded!.Members.Length);
        Assert.Equal(8, decoded.Members.Count(member => !member.Observer));
        Assert.Equal(16, decoded.Members.Count(member => member.Observer));
        Assert.Equal("OBSERVER15", decoded.Members[^1].DisplayName);
        Assert.True(decoded.Members[1].Loading);
        Assert.True(decoded.Members[8].Loading);
        Assert.True(decoded.ReadyRequired);
        Assert.Equal(2, decoded.MinimumPlayers);
        Assert.True(decoded.HostMayForceStart);
        Assert.Equal(LobbyPermissions.Admin, decoded.Permissions);
        Assert.True(decoded.BotFillEnabled);
        Assert.Equal(6, decoded.BotMinimumParticipants);
        Assert.Equal(2, decoded.BotSkill);

        for (int size = 0; size < bytes.Length; size++)
            Assert.False(LobbySnapshotPacket.TryRead(bytes.AsSpan(0, size), out _));
        bytes[^1] = 1;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out decoded));
        Assert.Null(decoded);
    }

    [Fact]
    public void SnapshotStatusFlagsUseOnlyTwoRemainingBitsAndRejectImpossibleRoles()
    {
        var snapshot = new LobbySnapshotPacket(1, 1, LobbyPhase.Open, LobbyPolicyKind.PersistentLobby,
            ReadyRequired: true, MinimumPlayers: 1, HostMayForceStart: false, LobbyPermissions.Player,
            BotFillEnabled: false, BotMinimumParticipants: 0, BotSkill: 1, 0, Rules(),
            ImmutableArray.Create(
                new LobbySnapshotMember(0, 10, "PLAYER", Hunter.Samus, 0,
                    LobbyWireIdentityKind.Guest, LobbyMemberFlags.DisconnectedGrace),
                new LobbySnapshotMember(byte.MaxValue, 11, "WATCH", Hunter.Kanden, byte.MaxValue,
                    LobbyWireIdentityKind.Registered, LobbyMemberFlags.Observer | LobbyMemberFlags.Loading),
                new LobbySnapshotMember(1, 12, "BOT", Hunter.Trace, 1,
                    LobbyWireIdentityKind.Bot, LobbyMemberFlags.Ready)));
        byte[] valid = new byte[LobbySnapshotPacket.HeaderSize + 3 * LobbySnapshotPacket.MemberSize];
        snapshot.Write(valid);
        Assert.True(LobbySnapshotPacket.TryRead(valid, out LobbySnapshotPacket? decoded));
        Assert.True(decoded!.Members[0].DisconnectedGrace);
        Assert.True(decoded.Members[1].Ready);
        Assert.True(decoded.Members[2].Loading);

        int first = LobbySnapshotPacket.HeaderSize;
        int observer = first + 2 * LobbySnapshotPacket.MemberSize;
        int bot = first + LobbySnapshotPacket.MemberSize;
        foreach (byte invalidFlags in new byte[]
        {
            0x40,
            0x80,
            (byte)(LobbyMemberFlags.Loading | LobbyMemberFlags.DisconnectedGrace),
            (byte)(LobbyMemberFlags.Ready | LobbyMemberFlags.DisconnectedGrace),
            (byte)(LobbyMemberFlags.Host | LobbyMemberFlags.DisconnectedGrace)
        })
        {
            byte[] bytes = (byte[])valid.Clone();
            bytes[first + 3] = invalidFlags;
            Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        }

        byte[] observerGrace = (byte[])valid.Clone();
        observerGrace[observer + 3] |= (byte)LobbyMemberFlags.DisconnectedGrace;
        Assert.False(LobbySnapshotPacket.TryRead(observerGrace, out _));

        byte[] observerReady = (byte[])valid.Clone();
        observerReady[observer + 3] |= (byte)LobbyMemberFlags.Ready;
        Assert.False(LobbySnapshotPacket.TryRead(observerReady, out _));

        byte[] botLoading = (byte[])valid.Clone();
        botLoading[bot + 3] |= (byte)LobbyMemberFlags.Loading;
        Assert.False(LobbySnapshotPacket.TryRead(botLoading, out _));

        byte[] botNotReady = (byte[])valid.Clone();
        botNotReady[bot + 3] = (byte)LobbyMemberFlags.None;
        Assert.False(LobbySnapshotPacket.TryRead(botNotReady, out _));
    }

    [Fact]
    public void SnapshotRejectsDuplicateIdentityInvalidCountsEnumsAndReservedRules()
    {
        var snapshot = new LobbySnapshotPacket(1, 1, LobbyPhase.Open, LobbyPolicyKind.PersistentLobby,
            ReadyRequired: true, MinimumPlayers: 1, HostMayForceStart: false, LobbyPermissions.Player,
            BotFillEnabled: false, BotMinimumParticipants: 0, BotSkill: 1, 0, Rules(),
            ImmutableArray.Create(
                new LobbySnapshotMember(0, 10, "ONE", Hunter.Samus, 0, LobbyWireIdentityKind.Guest, LobbyMemberFlags.Host),
                new LobbySnapshotMember(1, 11, "TWO", Hunter.Kanden, 1, LobbyWireIdentityKind.Registered, LobbyMemberFlags.None)));
        byte[] valid = new byte[LobbySnapshotPacket.HeaderSize + 2 * LobbySnapshotPacket.MemberSize];
        snapshot.Write(valid);
        byte[] bytes = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(LobbySnapshotPacket.HeaderSize + LobbySnapshotPacket.MemberSize + 8), 10);
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[8] = 99;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[10] = 9;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[16 + 83] = 1;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset] = 4;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 1] = 0;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 3] = 0x80;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 4] = 1;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 4] = 2;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 5] = 1;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 5] = 9;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 6] = 3;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
        bytes = (byte[])valid.Clone(); bytes[LobbySnapshotPacket.PolicyOffset + 7] = 1;
        Assert.False(LobbySnapshotPacket.TryRead(bytes, out _));
    }

    [Fact]
    public void LobbyRequestsAreCanonicalBoundedAndReplayAddressed()
    {
        LobbyRequestPacket[] requests =
        {
            new(1, 2, 3, LobbyRequestType.SetReady, LobbyRuleField.None, 1, ""),
            new(1, 2, 4, LobbyRequestType.SelectHunter, LobbyRuleField.None, (int)Hunter.Random, ""),
            new(1, 2, 5, LobbyRequestType.RequestTeam, LobbyRuleField.None, 7, ""),
            new(1, 2, 6, LobbyRequestType.SetMap, LobbyRuleField.None, 0, "MP3 PROVINGGROUND"),
            new(1, 2, 7, LobbyRequestType.SetMode, LobbyRuleField.None, (int)MatchMode.TeamBattle, ""),
            new(1, 2, 8, LobbyRequestType.SetRule, LobbyRuleField.ScoreGoal, 25, ""),
            new(1, 2, 9, LobbyRequestType.StartMatch, LobbyRuleField.None, 0, ""),
            new(1, 2, 12, LobbyRequestType.StartMatch, LobbyRuleField.None, 1, ""),
            new(1, 2, 10, LobbyRequestType.ReturnToLobby, LobbyRuleField.None, 0, ""),
            new(1, 2, 11, LobbyRequestType.Rematch, LobbyRuleField.None, 0, ""),
            new(1, 2, 13, LobbyRequestType.SetBotFillEnabled, LobbyRuleField.None, 1, ""),
            new(1, 2, 14, LobbyRequestType.SetBotMinimumParticipants, LobbyRuleField.None, 8, ""),
            new(1, 2, 15, LobbyRequestType.SetBotSkill, LobbyRuleField.None, 2, "")
        };
        foreach (LobbyRequestPacket request in requests)
        {
            byte[] bytes = new byte[LobbyRequestPacket.Size];
            request.Write(bytes);
            Assert.True(LobbyRequestPacket.TryRead(bytes, out LobbyRequestPacket decoded));
            Assert.Equal(request, decoded);
            for (int size = 0; size < bytes.Length; size++)
                Assert.False(LobbyRequestPacket.TryRead(bytes.AsSpan(0, size), out _));
        }
        byte[] invalid = new byte[LobbyRequestPacket.Size];
        requests[0].Write(invalid); invalid[14] = 1;
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[1].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 99);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[5].Write(invalid); invalid[13] = 0;
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^3].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 2);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^3].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), -1);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^2].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 9);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^2].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), -1);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^1].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 3);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[^1].Write(invalid); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), -1);
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
        requests[0].Write(invalid); invalid[8] = invalid[9] = invalid[10] = invalid[11] = 0;
        Assert.False(LobbyRequestPacket.TryRead(invalid, out _));
    }

    [Fact]
    public void FeedbackAndScopedChatRequireCanonicalIdsEnumsAndText()
    {
        byte[] feedbackBytes = new byte[LobbyFeedbackPacket.Size];
        var feedback = new LobbyFeedbackPacket(1, 2, 3, LobbyFeedbackCode.NotPermitted, "Host only");
        feedback.Write(feedbackBytes);
        Assert.True(LobbyFeedbackPacket.TryRead(feedbackBytes, out LobbyFeedbackPacket feedbackRead));
        Assert.Equal(feedback, feedbackRead);
        feedbackBytes[13] = 1;
        Assert.False(LobbyFeedbackPacket.TryRead(feedbackBytes, out _));

        byte[] requestBytes = new byte[LobbyChatRequestPacket.Size];
        var request = new LobbyChatRequestPacket(1, 2, 4, ChatScope.Team, "Push now");
        request.Write(requestBytes);
        Assert.True(LobbyChatRequestPacket.TryRead(requestBytes, out LobbyChatRequestPacket requestRead));
        Assert.Equal(request, requestRead);
        requestBytes[12] = 0;
        Assert.False(LobbyChatRequestPacket.TryRead(requestBytes, out _));

        byte[] chatBytes = new byte[LobbyChatPacket.Size];
        var chat = new LobbyChatPacket(1, 2, 4, 99, 777, byte.MaxValue, ChatScope.Lobby,
            LobbyChatSenderFlags.Observer | LobbyChatSenderFlags.Admin, "WATCHER", "hello");
        chat.Write(chatBytes);
        Assert.True(LobbyChatPacket.TryRead(chatBytes, out LobbyChatPacket chatRead));
        Assert.Equal(chat, chatRead);
        Assert.Equal(777ul, chatRead.SenderIdentity);
        Assert.True(chatBytes.Length <= ReliableChannel.MaxPayloadSize);
        for (int size = 0; size < chatBytes.Length; size++)
            Assert.False(LobbyChatPacket.TryRead(chatBytes.AsSpan(0, size), out _));
        chatBytes[31] = 1;
        Assert.False(LobbyChatPacket.TryRead(chatBytes, out _));
        chat.Write(chatBytes);
        chatBytes.AsSpan(20, sizeof(ulong)).Clear();
        Assert.False(LobbyChatPacket.TryRead(chatBytes, out _));
    }

    [Fact]
    public void MatchSummaryIsBoundedCompleteAndStrict()
    {
        var rows = ImmutableArray.CreateBuilder<MatchSummaryRow>(8);
        for (byte slot = 0; slot < 8; slot++)
            rows.Add(new(slot, $"PLAYER{slot}", (Hunter)slot, slot, (byte)(slot + 1), slot == 7,
                slot * 2, slot, slot + 1, slot + 2, slot * 100, slot, slot + 3, slot + 4, slot + 5));
        var summary = new MatchSummaryPacket(1, 8, 44, 600, MatchMode.Battle, MatchEndReason.Forced,
            MatchSummaryFlags.RatingEligible | MatchSummaryFlags.RatingPending, "MP1 SANCTORUS", rows.MoveToImmutable());
        byte[] bytes = new byte[MatchSummaryPacket.MaximumSize];
        Assert.Equal(540, summary.Write(bytes));
        Assert.True(bytes.Length <= ReliableChannel.MaxPayloadSize);
        Assert.True(MatchSummaryPacket.TryRead(bytes, out MatchSummaryPacket? decoded));
        Assert.Equal(summary.SessionId, decoded!.SessionId);
        Assert.Equal(summary.LobbyRevision, decoded.LobbyRevision);
        Assert.Equal(summary.MatchId, decoded.MatchId);
        Assert.Equal(summary.DurationSeconds, decoded.DurationSeconds);
        Assert.Equal(summary.Mode, decoded.Mode);
        Assert.Equal(summary.EndReason, decoded.EndReason);
        Assert.Equal(summary.Flags, decoded.Flags);
        Assert.Equal(summary.Map, decoded.Map);
        Assert.True(summary.Rows.SequenceEqual(decoded.Rows));
        for (int size = 0; size < bytes.Length; size++)
            Assert.False(MatchSummaryPacket.TryRead(bytes.AsSpan(0, size), out _));
        bytes[MatchSummaryPacket.HeaderSize + 5] = 1;
        Assert.False(MatchSummaryPacket.TryRead(bytes, out decoded));
        Assert.Null(decoded);
    }

    [Fact]
    public void ReliablePayloadUsesExactDatagramBudgetAndProtocolRemainsEight()
    {
        Assert.Equal(8, NetHeader.Version);
        Assert.Equal(993, ReliableChannel.MaxPayloadSize);
        var channel = new ReliableChannel();
        Assert.True(channel.TryEnqueue(ReliableEventType.LobbySnapshot,
            new byte[ReliableChannel.MaxPayloadSize], out _));
        var tooLarge = new ReliableChannel();
        Assert.False(tooLarge.TryEnqueue(ReliableEventType.LobbySnapshot,
            new byte[ReliableChannel.MaxPayloadSize + 1], out _));
        Assert.Equal(ReliableAdmissionFailure.OversizedPayload, tooLarge.LastAdmissionFailure);
    }
}
