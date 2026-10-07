using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

internal static partial class Program
{
    private static async Task<int> RunInstanceProbeAsync(string[] args)
    {
        if (args.Length != 4) return 2;
        await using StudioInstanceGuard guard = await StudioInstanceGuard.TryAcquireAsync(args[1], args[2],
            new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home), async (request, token) =>
            {
                await File.AppendAllTextAsync(args[3], JsonSerializer.Serialize(request, StudioProtocol.JsonOptions) + "\n", token);
                return StudioRequestResult.Success;
            });
        if (!guard.IsPrimary) { Console.WriteLine("SECONDARY " + guard.ForwardResult.Accepted); return guard.ForwardResult.Accepted ? 0 : 2; }
        Console.WriteLine("READY");
        Console.Out.Flush();
        while (await Console.In.ReadLineAsync() is { } line)
            if (line == "exit") break;
        return 0;
    }

    private static async Task CheckProcessIsolationAsync(string directory)
    {
        string installation = Path.Combine(directory, "installation-a");
        string peerInstallation = Path.Combine(directory, "installation-b");
        string data = Path.Combine(directory, "process-data");
        string inbox = Path.Combine(directory, "forwarded.jsonl");
        string peerInbox = Path.Combine(directory, "peer-forwarded.jsonl");
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(peerInstallation);
        Process? primary = null, peer = null;
        try
        {
            primary = await StartProbeAsync(installation, data, inbox);
            peer = await StartProbeAsync(peerInstallation, data, peerInbox);
            Check(primary.Id != peer.Id && !primary.HasExited && !peer.HasExited,
                "independent installation scopes can own separate host processes");
            string descriptorPath = StudioEndpointStore.GetDescriptorPath(installation, data);
            var descriptor = StudioEndpointStore.Read(installation, data);
            Check(descriptor.ProcessId == primary.Id, "endpoint descriptor identifies actual owning process");
            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode publicBits = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                Check((File.GetUnixFileMode(descriptorPath) & publicBits) == 0,
                    "IPC capability descriptor is private to OS user");
            }
            string source = Path.Combine(directory, "exact replay ü.ppdemo");
            var request = new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Replay, source, Recover: true);
            await using (StudioInstanceGuard secondary = await StudioInstanceGuard.TryAcquireAsync(installation, data, request,
                (_, _) => throw new InvalidOperationException("Secondary must not receive requests.")))
            {
                Check(!secondary.IsPrimary && secondary.ForwardResult.Accepted, "second bootstrap authenticates and forwards instead of owning another instance");
            }
            StudioOpenRequest? received = JsonSerializer.Deserialize<StudioOpenRequest>(File.ReadLines(inbox).Single(), StudioProtocol.JsonOptions);
            Check(received == request && !primary.HasExited && File.Exists(descriptorPath),
                "exact request reaches owner and secondary disposal leaves owner endpoint alive");

            peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(!primary.HasExited, "peer process crash leaves Studio host probe alive");
            peer.Dispose();
            peer = await StartProbeAsync(peerInstallation, data, peerInbox);
            await StopProbeAsync(primary);
            Check(!peer.HasExited && !File.Exists(descriptorPath), "graceful Studio host shutdown removes descriptor and leaves independent peer alive");
            primary.Dispose();
            primary = await StartProbeAsync(installation, data, inbox);
            var beforeCrash = StudioEndpointStore.Read(installation, data);
            primary.Kill(entireProcessTree: true);
            await primary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(!peer.HasExited && File.Exists(descriptorPath), "Studio host crash is isolated and leaves recoverable stale descriptor");
            primary.Dispose();
            primary = await StartProbeAsync(installation, data, inbox);
            var afterCrash = StudioEndpointStore.Read(installation, data);
            Check(afterCrash.ProcessId == primary.Id && afterCrash.PipeName != beforeCrash.PipeName && afterCrash.Secret != beforeCrash.Secret,
                "relaunch replaces stale endpoint with fresh authenticated owner");
            await using (StudioInstanceGuard secondary = await StudioInstanceGuard.TryAcquireAsync(installation, data,
                new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home), (_, _) => Task.FromResult(StudioRequestResult.Success)))
            {
                Check(!secondary.IsPrimary && secondary.ForwardResult.Accepted, "forwarding reconnects after owner crash");
            }
            await StopProbeAsync(primary);
            await StopProbeAsync(peer);
            Check(!File.Exists(descriptorPath) && !File.Exists(StudioEndpointStore.GetDescriptorPath(peerInstallation, data)),
                "all graceful host exits remove active endpoint descriptors");
        }
        finally
        {
            foreach (Process? process in new[] { primary, peer })
            {
                if (process is null) continue;
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
                finally { process.Dispose(); }
            }
        }
    }

    private static async Task<Process> StartProbeAsync(string installation, string data, string inbox)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate process runtime.");
        ProcessStartInfo start = new(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--instance-probe");
        start.ArgumentList.Add(installation);
        start.ArgumentList.Add(data);
        start.ArgumentList.Add(inbox);
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start IPC host probe.");
        try
        {
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (line != "READY") throw new InvalidOperationException("IPC host probe failed to become primary: " + line + " " + await process.StandardError.ReadToEndAsync());
            return process;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    private static async Task StopProbeAsync(Process process)
    {
        await process.StandardInput.WriteLineAsync("exit");
        await process.StandardInput.FlushAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Check(process.ExitCode == 0, "host probe releases owned IPC resources on normal exit");
    }
}
