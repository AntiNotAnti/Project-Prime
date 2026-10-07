using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using MphRead.Platform;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args is ["--owner", var childLibrary])
        {
            HostedPackageCache.SetLibraryOwner(childLibrary);
            Console.WriteLine("READY"); Console.Out.Flush();
            _ = Console.ReadLine();
            return;
        }
        if (args is ["--reserve", var cache, var budget, var requested])
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var reservation = new HostedPackageCache(cache, long.Parse(budget, CultureInfo.InvariantCulture))
                    .Reserve(cancellation.Token, long.Parse(requested, CultureInfo.InvariantCulture));
                Console.WriteLine("RESERVED");
            }
            catch (IOException ex) when (ex.Message.StartsWith("Hosted package cache has no unpinned space", StringComparison.Ordinal))
            { Console.WriteLine("BUSY"); }
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "prime-hosted-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? owner = null; int checks = 0;
        try
        {
            byte[] bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string archive = Path.Combine(root, hash + ".ppmap");
            string library = Path.Combine(root, "lobbies", Guid.NewGuid().ToString("N"));
            string copy = Path.Combine(library, Guid.NewGuid().ToString("N") + ".ppmap");
            string marker = Path.Combine(library, ".cache-owner");
            Seed();
            owner = Start("--owner", library);
            Check(await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "READY",
                "independent owner claims its private hosted library before the reaper runs");
            string[] claim = File.ReadAllText(marker).Split('\n');
            Check(claim.Length == 3 && claim[0] == "2" && claim[1] == owner.Id.ToString(CultureInfo.InvariantCulture)
                && ProcessLifetimeIdentity.Matches(owner, claim[2]), "the library owner marker contains its exact canonical process incarnation");
            string token = claim[2];
            await Retained("live exact owner", File.ReadAllText(marker));
            await Retained("unavailable identity", "2\n" + owner.Id + "\n");
            await Retained("malformed identity", "2\n" + owner.Id + "\nmalformed");
            await Retained("oversized owner marker", "2\n" + owner.Id + "\n" + new string('x', 600));
            string legacy = owner.Id + ":" + ((OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
                ? 1L : owner.StartTime.ToUniversalTime().Ticks).ToString(CultureInfo.InvariantCulture);
            await Retained("legacy owner with unverified Unix UTC", legacy);

            string[] reused = token.Split(':');
            reused[^1] = (ulong.Parse(reused[^1], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            WriteOwner("2\n" + owner.Id + "\n" + string.Join(':', reused));
            Check(await Reserve() == "RESERVED", "an exact mismatched incarnation releases a stale library despite an existing reused PID");
            Check(!Directory.Exists(library) && !File.Exists(archive) && !owner.HasExited,
                "confirmed stale ownership removes its library pin and permits original archive eviction without affecting the replacement process");

            Seed(); HostedPackageCache.SetLibraryOwner(library, owner);
            await owner.StandardInput.WriteLineAsync("stop"); await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(owner.ExitCode == 0, "the owned child exits normally before stale library reclamation");
            Check(await Reserve() == "BUSY" && Directory.Exists(library) && File.Exists(archive),
                "a fresh child-owner marker retains the existing startup handoff window after process exit");
            Age();
            Check(await Reserve() == "RESERVED" && !Directory.Exists(library) && !File.Exists(archive),
                "an actually exited owner is reclaimed after handoff and its archive becomes evictable");
            Check(!Directory.EnumerateFiles(root, ".reserve-*").Any() && !Directory.EnumerateFiles(root, ".pin-*").Any(),
                "completed independent reapers leave no reservation or temporary pin leases");
            Console.WriteLine($"Hosted cache: {checks} checks passed.");

            void Seed()
            {
                Directory.CreateDirectory(library);
                File.WriteAllBytes(archive, bytes); File.WriteAllBytes(copy, bytes);
                File.WriteAllText(Path.Combine(library, ".cache-pins"), hash + "\n");
                File.WriteAllText(Path.Combine(library, ".cache-copy-bytes"), bytes.Length.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            void Age() => File.SetLastWriteTimeUtc(marker, DateTime.UtcNow - TimeSpan.FromMinutes(3));
            void WriteOwner(string value) { File.WriteAllText(marker, value); Age(); }
            async Task<string> Reserve()
            {
                using var reaper = Start("--reserve", root, bytes.Length.ToString(CultureInfo.InvariantCulture), "1");
                await reaper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                string output = (await reaper.StandardOutput.ReadToEndAsync()).Trim();
                string error = await reaper.StandardError.ReadToEndAsync();
                Check(reaper.ExitCode == 0 && output is "RESERVED" or "BUSY", "independent cache reaper completes the actual production reservation: " + error);
                return output;
            }
            async Task Retained(string label, string value)
            {
                WriteOwner(value);
                Check(await Reserve() == "BUSY", label + " retains the hash pin under actual cache budget pressure");
                Check(!owner.HasExited && Directory.Exists(library) && File.Exists(archive) && File.Exists(copy)
                    && File.ReadAllBytes(archive).SequenceEqual(bytes) && File.ReadAllBytes(copy).SequenceEqual(bytes)
                    && File.ReadAllText(Path.Combine(library, ".cache-pins")) == hash + "\n",
                    label + " preserves the live private archive and content-addressed original bytes");
            }
            void Check(bool success, string message) { if (!success) throw new Exception(message); checks++; }
        }
        finally
        {
            if (owner != null)
            {
                if (!owner.HasExited) { owner.Kill(entireProcessTree: true); await owner.WaitForExitAsync(); }
                owner.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start the owned cache fixture process.");
    }
}
