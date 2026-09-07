using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Update;
using Xunit;

namespace MphRead.Tests;

public sealed class ServerUpdateRuntimeTests
{
    [Fact]
    public void ValidationCanonicalizesContentButPreservesOriginalRestartArguments()
    {
        string root = Path.GetFullPath(Path.GetTempPath());
        string installation = Path.Combine(root, "installed"), launch = Path.Combine(root, "launch");
        string[] original = { "-server", "room with spaces", "-data", "content path", "-rotation", "cycle.json",
            "-mapdir", "my maps", "-mode", "Battle", "-autoupdate", "-update-repository", "owned/fork" };
        string[] copy = (string[])original.Clone();
        string[] validation = ServerUpdateRuntime.ValidationArguments(original, installation, launch);
        Assert.Equal(copy, original);
        Assert.Equal(Path.Combine(installation, "content path"), ServerUpdateRuntime.Value(validation, "data"));
        Assert.Equal(Path.Combine(installation, "cycle.json"), ServerUpdateRuntime.Value(validation, "rotation"));
        Assert.Equal(Path.Combine(launch, "my maps"), ServerUpdateRuntime.Value(validation, "mapdir"));
        Assert.Equal("room with spaces", ServerUpdateRuntime.Value(validation, "server"));
        Assert.False(ServerUpdateRuntime.Has(validation, "autoupdate"));
        Assert.True(ServerUpdateRuntime.Has(validation, "noupdate"));
        Assert.Equal(Path.Combine(installation, "maps"), ServerUpdateRuntime.Value(
            ServerUpdateRuntime.ValidationArguments(Array.Empty<string>(), installation, launch), "mapdir"));
        var start = new ServerRestart("executable", original, launch, false).CreateStartInfo()!;
        Assert.Equal(copy, start.ArgumentList);
        Assert.Equal(launch, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public async Task ActualEarlyDirectoryValidationReturnsMarkerWithoutPortsOrSettings()
    {
        using var stage = Fixture.Managed();
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        string[] arguments = { "-authoritative-server-validate", "-masterserver", "-port", port.ToString(),
            "-autoupdate", "-update-repository", "liveteklol/Fruity-Prime", "-noupdate" };
        Assert.Null(await ServerUpdateRuntime.ValidateStagedAsync(stage.Stage, arguments, stage.Root, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(stage.Root, "paths.txt")));
        Assert.False(File.Exists(Path.Combine(stage.Stage.Directory, "paths.txt")));
        Assert.False(Directory.Exists(Path.Combine(stage.Stage.Directory, ".update")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualEarlyContentValidationReportsMissingData(bool supplied)
    {
        using var stage = Fixture.Managed();
        string[] args = supplied ? new[] { "-authoritative-server-validate", "-data", Path.Combine(stage.Root, "missing") }
            : new[] { "-authoritative-server-validate" };
        string? error = await ServerUpdateRuntime.ValidateStagedAsync(stage.Stage, args, stage.Root, CancellationToken.None);
        Assert.NotNull(error);
        Assert.Contains("Staged validation failed:", error);
        Assert.DoesNotContain("valid success report", error);
        Assert.False(File.Exists(Path.Combine(stage.Stage.Directory, "paths.txt")));
    }

    [Theory]
    [InlineData("family")]
    [InlineData("hash")]
    [InlineData("executable")]
    public async Task InvalidStageCannotExecuteEvenThroughDirectValidationApi(string fault)
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var fixture = Fixture.Script("echo executed > \"$1\"\n");
        ServerUpdateStage stage = fixture.Stage;
        if (fault == "family") { stage = stage with { Manifest = stage.Manifest with { Family = NetWireFamily.LegacyRelay } }; }
        else if (fault == "executable") { stage = stage with { Manifest = stage.Manifest with { Executable = "../unverified" } }; }
        else { File.AppendAllText(Path.Combine(stage.Directory, stage.Manifest.Executable), "# changed\n"); }
        string marker = Path.Combine(fixture.Root, "executed");
        await Assert.ThrowsAnyAsync<Exception>(() => ServerUpdateRuntime.ValidateStagedAsync(stage, new[] { marker }, fixture.Root, CancellationToken.None));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ExitZeroWithoutMarkerCannotApproveStage()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var stage = Fixture.Script("exit 0\n");
        Assert.NotNull(await ServerUpdateRuntime.ValidateStagedAsync(stage.Stage, Array.Empty<string>(), stage.Root, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationKillsAndAwaitsOwnedValidator()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var stage = Fixture.Script("echo $$ > \"$1\"\nwhile :; do sleep 1; done\n");
        string marker = Path.Combine(stage.Root, "pid");
        using var cancel = new CancellationTokenSource();
        Task<string?> pending = ServerUpdateRuntime.ValidateStagedAsync(stage.Stage, new[] { marker }, stage.Root, cancel.Token);
        var watch = Stopwatch.StartNew();
        while (!File.Exists(marker) && watch.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(10); }
        Assert.True(File.Exists(marker));
        int pid = Int32.Parse(File.ReadAllText(marker));
        cancel.Cancel();
        Assert.Contains("cancelled", await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task ExcessiveOutputStopsValidatorPromptly()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var stage = Fixture.Script("while :; do echo 'overflow overflow overflow overflow overflow overflow'; done\n");
        string? error = await ServerUpdateRuntime.ValidateStagedAsync(stage.Stage, Array.Empty<string>(), stage.Root, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(error);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "fruity-validator-" + Guid.NewGuid().ToString("N"));
        public ServerUpdateStage Stage { get; private set; } = null!;
        private Fixture() { Directory.CreateDirectory(Path.Combine(Root, "stage")); }
        private void Seal(string executable)
        {
            string directory = Path.Combine(Root, "stage");
            ServerUpdateFile[] files = Directory.GetFiles(directory).Select(path => new ServerUpdateFile(Path.GetFileName(path),
                new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();
            Stage = new(directory, new(1, NetWireFamily.Authoritative, NetHeader.Version, "2.1", "test", "test.zip", 1,
                new string('0', 64), executable, files));
        }
        public static Fixture Managed()
        {
            var result = new Fixture();
            string source = Path.GetDirectoryName(typeof(ServerUpdate).Assembly.Location)!;
            string destination = Path.Combine(result.Root, "stage");
            foreach (string file in Directory.GetFiles(source, "*.dll")) { File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
            File.WriteAllText(Path.Combine(destination, "FruityPrime.runtimeconfig.json"),
                "{\"runtimeOptions\":{\"tfm\":\"net9.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"9.0.0\"}}}");
            result.Seal("FruityPrime.dll");
            return result;
        }
        public static Fixture Script(string body)
        {
            var result = new Fixture();
            string path = Path.Combine(result.Root, "stage", "validator");
            File.WriteAllText(path, "#!/bin/sh\n" + body);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            result.Seal("validator");
            return result;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
