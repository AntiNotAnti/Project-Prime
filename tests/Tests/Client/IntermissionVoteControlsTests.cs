using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;
using MphRead.Mods;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class IntermissionVoteControlsTests
{
    [Fact]
    public void NoBallotInvalidCoordinatesAndInputGatesNeverQueueAVote()
    {
        using var live = new LiveSession();
        Assert.False(IntermissionVoteControls.Available);
        Assert.False(IntermissionVoteControls.ConsumePointerVote());

        InstallBallot(live.Client, phaseRevision: 1, revision: 1, 1, 2);
        Assert.True(IntermissionVoteControls.Available);
        Assert.False(IntermissionVoteControls.QueuePointerDown(float.NaN, 60));
        Assert.False(IntermissionVoteControls.QueuePointerDown(16, float.PositiveInfinity));
        Assert.False(IntermissionVoteControls.QueuePointerDown(7, 60));
        Assert.False(IntermissionVoteControls.QueuePointerDown(249, 60));
        Assert.False(IntermissionVoteControls.QueuePointerDown(16, 49));
        Assert.False(IntermissionVoteControls.QueuePointerDown(16, 75));

        Func<bool>? previousPause = ClientInputState.ReadPauseOpen;
        try
        {
            ClientInputState.ReadPauseOpen = () => true;
            Assert.False(IntermissionVoteControls.QueuePointerDown(16, 60));
            Assert.False(IntermissionVoteControls.Submit());

            ClientInputState.ReadPauseOpen = () => false;
            ChatBox.Open(swallowOpeningChar: false);
            Assert.False(IntermissionVoteControls.QueuePointerDown(16, 60));
            Assert.False(IntermissionVoteControls.Submit());
            Assert.False(IntermissionVoteControls.ConsumePointerVote());
        }
        finally
        {
            ChatBox.Clear();
            ClientInputState.ReadPauseOpen = previousPause;
        }
    }

    [Fact]
    public void SelectionClampsAndResetsOnPhaseAndNewSession()
    {
        using (var first = new LiveSession())
        {
            InstallBallot(first.Client, phaseRevision: 1, revision: 1, 1, 2);
            IntermissionVoteControls.Move(1);
            InstallBallot(first.Client, phaseRevision: 1, revision: 2, 1);
            IntermissionVoteRequest clamped = SubmitAndWait(first);
            Assert.Equal((byte)1, clamped.OptionId);

            InstallBallot(first.Client, phaseRevision: 1, revision: 3, 1, 2);
            IntermissionVoteControls.Move(1);
            InstallBallot(first.Client, phaseRevision: 2, revision: 4, 1, 2);
            IntermissionVoteRequest phaseReset = SubmitAndWait(first);
            Assert.Equal((byte)1, phaseReset.OptionId);
        }

        using var second = new LiveSession(matchId: 2);
        InstallBallot(second.Client, phaseRevision: 1, revision: 1, 1, 2);
        IntermissionVoteRequest sessionReset = SubmitAndWait(second);
        Assert.Equal((byte)1, sessionReset.OptionId);
    }

    [Fact]
    public void QueuedTapIsConsumedOnlyForTheCurrentPhase()
    {
        using var live = new LiveSession();
        InstallBallot(live.Client, phaseRevision: 1, revision: 1, 1, 2);
        Assert.True(IntermissionVoteControls.QueuePointerDown(16, 63));
        IntermissionVoteRequest current = ConsumeAndWait(live);
        Assert.Equal((byte)2, current.OptionId);

        InstallBallot(live.Client, phaseRevision: 2, revision: 2, 1, 2);
        Assert.True(IntermissionVoteControls.QueuePointerDown(16, 63));
        InstallBallot(live.Client, phaseRevision: 3, revision: 3, 1, 2);
        Assert.False(IntermissionVoteControls.ConsumePointerVote());
    }

    [Fact]
    public void ExpiredBallotKeepsResultsVisibleButDiscardsQueuedAndNewInput()
    {
        using var live = new LiveSession();
        InstallBallot(live.Client, 1, 1, 1, 2);
        uint beforeWrap = uint.MaxValue - 2;
        var ballot = live.Client.Ballot! with { HasDeadline = true, DeadlineTick = 1 };
        typeof(NetClient).GetProperty(nameof(NetClient.Ballot))!.GetSetMethod(true)!
            .Invoke(live.Client, new object[] { ballot });
        void SetTick(uint tick) => typeof(NetClient).GetProperty(nameof(NetClient.Snapshot))!
            .GetSetMethod(true)!.Invoke(live.Client, new object[] { live.Client.Snapshot with { ServerTick = tick } });
        SetTick(beforeWrap);
        Assert.False(IntermissionVoteControls.IsClosed(ballot, beforeWrap));
        Assert.True(IntermissionVoteControls.QueuePointerDown(16, 63));
        SetTick(1);
        Assert.True(IntermissionVoteControls.IsClosed(ballot, 1));
        Assert.True(IntermissionVoteControls.IsClosed(ballot, 2));
        Assert.False(IntermissionVoteControls.IsClosed(ballot with { HasDeadline = false }, 2));
        Assert.True(IntermissionVoteControls.Available);
        Assert.False(IntermissionVoteControls.ConsumePointerVote());
        Assert.False(IntermissionVoteControls.QueuePointerDown(16, 63));
        Assert.False(IntermissionVoteControls.Submit());
        Assert.Equal(ballot, live.Client.Ballot);
        SetTick(0);
        Assert.False(IntermissionVoteControls.ConsumePointerVote()); // Expired queued tap was discarded.
    }

    private static IntermissionVoteRequest SubmitAndWait(LiveSession live)
    {
        IntermissionVoteRequest? received = null;
        live.Server.IntermissionVoteReceived = (_, request) =>
        {
            received = request;
            return true;
        };
        Assert.True(IntermissionVoteControls.Submit());
        live.Pump(() => received.HasValue);
        return received!.Value;
    }

    private static IntermissionVoteRequest ConsumeAndWait(LiveSession live)
    {
        IntermissionVoteRequest? received = null;
        live.Server.IntermissionVoteReceived = (_, request) =>
        {
            received = request;
            return true;
        };
        Assert.True(IntermissionVoteControls.ConsumePointerVote());
        live.Pump(() => received.HasValue);
        return received!.Value;
    }

    private static void InstallBallot(NetClient client, uint phaseRevision, uint revision, params byte[] optionIds)
    {
        var options = ImmutableArray.CreateBuilder<IntermissionOption>(optionIds.Length);
        for (int i = 0; i < optionIds.Length; i++)
            options.Add(new(optionIds[i], IntermissionChoice.Map, 0, $"Option {optionIds[i]}"));
        var ballot = new IntermissionBallot(client.Accepted.MatchId, phaseRevision, revision, 0,
            MatchPhase.Intermission, false, 0, 1, options.MoveToImmutable());
        PropertyInfo property = typeof(NetClient).GetProperty(nameof(NetClient.Ballot))!;
        property.GetSetMethod(nonPublic: true)!.Invoke(client, new object?[] { ballot });
    }

    private sealed class LiveSession : IDisposable
    {
        private readonly NetTransport _serverTransport = new(0);
        private readonly ServerNetwork _server;
        private readonly AuthoritativePlay _play;
        private uint _tick;

        public ServerNetwork Server => _server;
        public NetClient Client => _play.Client;

        public LiveSession(uint matchId = 1)
        {
            _server = new ServerNetwork(_serverTransport, "MP1 SANCTORUS", GameMode.Battle, matchId);
            _play = new AuthoritativePlay("127.0.0.1", _serverTransport.LocalPort, "VOTE", Hunter.Samus);
            Pump(() => Client.Connection != null);
            Assert.True(Client.Ready(Client.Accepted.MatchId));
            ulong connectionId = Client.Connection!.Id;
            Pump(() => _server.Find(connectionId)?.Connection.State == NetConnectionState.Ready);
        }

        public void Pump(Func<bool> done)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                _server.Poll(_tick++);
                Client.Poll();
                if (done()) return;
                Thread.Sleep(1);
            }
            Assert.Fail("Voting loopback condition timed out.");
        }

        public void Dispose()
        {
            _play.Dispose();
            _serverTransport.Dispose();
        }
    }
}
