using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Testing;
using Xunit;

namespace MphRead.Tests;

public sealed class SemanticControlProtocolTests
{
    [Fact]
    public void RequestSchemaIsClosedAndRejectsUnsafeValues()
    {
        SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SubmitMovement", "{\"x\":1,\"y\":0}")));
        Assert.Equal(SemanticControlCommand.SubmitMovement, request.Command);
        Assert.Equal("session-a", request.SessionId);

        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SubmitMovement", "{\"z\":1}"))));
        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SubmitMovement", "{\"x\":1,\"x\":2}"))));
        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SubmitMovement", "{\"x\":1e999}"))));
        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("GetShellState", "{\"x\":1}"))));
        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("CaptureFrame", "{\"label\":\"../outside\"}"))));
    }

    [Fact]
    public void PhaseGuardRejectsDelayedCommandsAfterTransition()
    {
        var guard = new SemanticControlPhaseGuard(
            new SemanticControlIdentity("session-a", "match-a", "phase-a"));
        SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("OpenPlay", "{}")));

        Assert.True(guard.Validate(request).Accepted);
        guard.Advance(new SemanticControlIdentity("session-a", "match-a", "rematch-b"));

        SemanticControlResponse stale = guard.Validate(request);
        Assert.False(stale.Accepted);
        Assert.Equal("stale-phase", stale.Error);
    }

    [Fact]
    public void PhaseGuardAllowsOnlyBootstrapShellReadAcrossPhaseValues()
    {
        var guard = new SemanticControlPhaseGuard(
            new SemanticControlIdentity("session-a", "match-a", "phase-a"));
        SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("GetShellState", "{}", "cmd-bootstrap",
                phaseId: "unknown-until-read")));

        SemanticControlResponse response = guard.Validate(request);

        Assert.True(response.Accepted);
        Assert.Equal("cmd-bootstrap", response.CommandId);
        Assert.False(guard.Validate(request with { Command = SemanticControlCommand.OpenPlay }).Accepted);
    }

    [Fact]
    public async Task DispatcherKeepsUiAndGameplayOwnersSeparate()
    {
        var calls = new List<string>();
        var dispatcher = new SemanticControlDispatcher(
            (request, _) =>
            {
                calls.Add("ui:" + request.Command);
                return ValueTask.FromResult(SemanticControlResponse.Success(request.CommandId));
            },
            (request, _) =>
            {
                calls.Add("gameplay:" + request.Command);
                return ValueTask.FromResult(SemanticControlResponse.Success(request.CommandId));
            });

        SemanticControlRequest shell = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("OpenPlay", "{}", "cmd-ui")));
        SemanticControlRequest movement = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SubmitMovement", "{\"x\":0,\"y\":1}", "cmd-game")));
        SemanticControlRequest selectHunter = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("SelectHunter", "{\"hunter\":\"Samus\"}", "cmd-select")));
        SemanticControlRequest startMatch = SemanticControlProtocol.ParseRequest(
            Encoding.UTF8.GetBytes(RequestJson("StartMatch", "{}", "cmd-start")));

        Assert.True((await dispatcher.DispatchAsync(shell, CancellationToken.None)).Accepted);
        Assert.True((await dispatcher.DispatchAsync(selectHunter, CancellationToken.None)).Accepted);
        Assert.True((await dispatcher.DispatchAsync(startMatch, CancellationToken.None)).Accepted);
        Assert.True((await dispatcher.DispatchAsync(movement, CancellationToken.None)).Accepted);
        Assert.Equal(new[]
        {
            "ui:OpenPlay", "ui:SelectHunter", "ui:StartMatch", "gameplay:SubmitMovement"
        }, calls);
    }

    [Fact]
    public void ResponsesRejectAmbiguousAcceptedAndErrorState()
    {
        const string response = "{\"schemaVersion\":1,\"commandId\":\"cmd-1\",\"accepted\":true,\"error\":\"bad\",\"payload\":null}";
        Assert.Throws<InvalidDataException>(() => SemanticControlProtocol.ParseResponse(
            Encoding.UTF8.GetBytes(response)));

        SemanticControlResponse valid = SemanticControlResponse.Rejected("cmd-1", "queue-full");
        string serialized = SemanticControlProtocol.SerializeResponse(valid);
        Assert.Equal(valid, SemanticControlProtocol.ParseResponse(Encoding.UTF8.GetBytes(serialized.TrimEnd())));
    }

    [Fact]
    public async Task UnixTransportAuthenticatesAndDispatchesWithoutLiveE2eClaim()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine("/tmp", "pctl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string tokenPath = Path.Combine(root, "token");
        string endpoint = Path.Combine(root, "control.sock");
        const string token = "test-token";
        File.WriteAllText(tokenPath, token + "\n");
        File.SetUnixFileMode(tokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var options = new SemanticControlServerOptions(true, endpoint, tokenPath,
            QueueCapacity: 2, RequestTimeoutMilliseconds: 1000);
        var guard = new SemanticControlPhaseGuard(
            new SemanticControlIdentity("session-a", "match-a", "phase-a"));
        var server = new SemanticControlServer(options, guard,
            (request, _) => ValueTask.FromResult(
                SemanticControlResponse.Success(request.CommandId)));
        try
        {
            server.Start();
            Stopwatch timer = Stopwatch.StartNew();
            while (!File.Exists(endpoint) && timer.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(10);

            SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
                Encoding.UTF8.GetBytes(RequestJson("GetShellState", "{}", "cmd-transport")));
            SemanticControlResponse response = await SemanticControlClient.SendAsync(
                options, token, request);
            Assert.True(response.Accepted);
            Assert.Equal("cmd-transport", response.CommandId);
        }
        finally
        {
            await server.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Assert.False(File.Exists(endpoint));
    }

    [Fact]
    public async Task RequestTimeoutCancelsInFlightDispatchBeforePhaseCanAdvance()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine("/tmp", "pctl-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string tokenPath = Path.Combine(root, "token");
        string endpoint = Path.Combine(root, "control.sock");
        const string token = "timeout-token";
        File.WriteAllText(tokenPath, token + "\n");
        File.SetUnixFileMode(tokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var options = new SemanticControlServerOptions(true, endpoint, tokenPath,
            QueueCapacity: 2, RequestTimeoutMilliseconds: 100);
        var guard = new SemanticControlPhaseGuard(
            new SemanticControlIdentity("session-a", "match-a", "phase-a"));
        var dispatchStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchCancelled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int dispatchCount = 0;
        var server = new SemanticControlServer(options, guard,
            async (request, cancellationToken) =>
            {
                Interlocked.Increment(ref dispatchCount);
                dispatchStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    dispatchCancelled.TrySetResult(true);
                    throw;
                }
                return SemanticControlResponse.Success(request.CommandId);
            });
        try
        {
            server.Start();
            SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
                Encoding.UTF8.GetBytes(RequestJson("GetShellState", "{}", "cmd-timeout")));
            Task<SemanticControlResponse> send = SemanticControlClient.SendAsync(
                options, token, request);
            await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            guard.Advance(new SemanticControlIdentity("session-a", "match-a", "rematch-b"));

            SemanticControlResponse response = await send.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(response.Accepted);
            Assert.Equal("dispatch-timeout", response.Error);
            await dispatchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, dispatchCount);
        }
        finally
        {
            await server.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PhaseAdvancingUiOwnerCommandRemainsAccepted()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine("/tmp", "pctl-transition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string tokenPath = Path.Combine(root, "token");
        string endpoint = Path.Combine(root, "control.sock");
        const string token = "transition-token";
        File.WriteAllText(tokenPath, token + "\n");
        File.SetUnixFileMode(tokenPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var options = new SemanticControlServerOptions(true, endpoint, tokenPath,
            QueueCapacity: 2, RequestTimeoutMilliseconds: 1000);
        var guard = new SemanticControlPhaseGuard(
            new SemanticControlIdentity("session-a", "match-a", "phase-a"));
        var server = new SemanticControlServer(options, guard,
            (request, _) =>
            {
                if (request.Command == SemanticControlCommand.StartMatch)
                {
                    guard.Advance(new SemanticControlIdentity(
                        request.SessionId, request.MatchId, "match-b"));
                }
                return ValueTask.FromResult(SemanticControlResponse.Success(request.CommandId));
            });
        try
        {
            server.Start();
            Stopwatch timer = Stopwatch.StartNew();
            while (!File.Exists(endpoint) && timer.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(10);

            SemanticControlRequest request = SemanticControlProtocol.ParseRequest(
                Encoding.UTF8.GetBytes(RequestJson("StartMatch", "{}", "cmd-transition")));
            SemanticControlResponse response = await SemanticControlClient.SendAsync(
                options, token, request);

            Assert.True(response.Accepted);
            Assert.Equal("cmd-transition", response.CommandId);
            Assert.Equal("match-b", guard.Current.PhaseId);
        }
        finally
        {
            await server.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string RequestJson(string command, string arguments, string commandId = "cmd-1",
        string phaseId = "phase-a")
        => $"{{\"schemaVersion\":1,\"commandId\":\"{commandId}\",\"sessionId\":\"session-a\",\"matchId\":\"match-a\",\"phaseId\":\"{phaseId}\",\"command\":\"{command}\",\"arguments\":{arguments}}}";
}
