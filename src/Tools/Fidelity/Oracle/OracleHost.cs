using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MphRead;

/// <summary>
/// Runs one explicitly configured external AMHE1 adapter. The adapter is a
/// child process with a private run directory; no shell is involved and a
/// timeout only terminates that child. The adapter must write a normalized
/// <c>artifact.json</c> before exiting successfully. An explicitly supplied
/// private ROM is verified before launch and passed as <c>--rom</c>.
/// </summary>
public static class OracleHost
{
    public const int DefaultTimeoutMilliseconds = 120_000;
    public const int MaximumTimeoutMilliseconds = 15 * 60 * 1000;
    public const int MaximumAdapterOutputCharacters = 256 * 1024;

    public static OracleRecordResult Record(OracleScenario scenario,
        string referenceDirectory, string adapterPath, string artifactRoot,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds, string? runId = null,
        string? romPath = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ValidateTimeout(timeoutMilliseconds);
        VerifyReference(referenceDirectory, scenario.Source);
        OracleRomIdentity? rom = romPath is null ? null : VerifyRom(romPath);
        string adapter = ValidateRegularExecutable(adapterPath);
        string adapterIdentity = "sha256:" + HashFile(adapter);
        string root = PrepareArtifactRoot(artifactRoot);
        string id = ValidateRunId(runId) ?? DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..12];
        string scenarioDirectory = Path.Combine(root, scenario.Id);
        EnsureRegularDirectory(scenarioDirectory, create: true);
        Directory.CreateDirectory(scenarioDirectory);
        string runDirectory = Path.Combine(scenarioDirectory, id);
        if (Directory.Exists(runDirectory) || File.Exists(runDirectory))
            throw new IOException($"Oracle run directory already exists: {runDirectory}");
        Directory.CreateDirectory(runDirectory);
        string scenarioFile = Path.Combine(runDirectory, "scenario.json");
        File.WriteAllText(scenarioFile, OracleJson.SerializeScenario(scenario), new UTF8Encoding(false));

        var start = new ProcessStartInfo
        {
            FileName = adapter,
            WorkingDirectory = runDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--scenario");
        start.ArgumentList.Add(scenarioFile);
        start.ArgumentList.Add("--reference");
        start.ArgumentList.Add(Path.GetFullPath(referenceDirectory));
        if (rom is not null)
        {
            start.ArgumentList.Add("--rom");
            start.ArgumentList.Add(Path.GetFullPath(romPath!));
        }
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(runDirectory);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The oracle adapter did not start.");
            using var outputCancellation = new CancellationTokenSource();
            Task<string> stdout = ReadBoundedAsync(process.StandardOutput,
                MaximumAdapterOutputCharacters, outputCancellation.Token);
            Task<string> stderr = ReadBoundedAsync(process.StandardError,
                MaximumAdapterOutputCharacters, outputCancellation.Token);
            bool exited = process.WaitForExitAsync().Wait(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            bool timedOut = !exited;
            if (timedOut)
            {
                // Deliberately do not request an entire process tree. The
                // configured adapter is the only process this runner owns.
                try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
                catch (InvalidOperationException) { }
                try { process.WaitForExit(5_000); } catch (InvalidOperationException) { }
            }
            // A child that forks and inherits a redirected pipe can keep the
            // pipe open after the adapter exits. Never wait indefinitely for
            // inherited handles: stdout/stderr are bounded diagnostics, not a
            // lifecycle dependency of the oracle run.
            Task outputDrain = Task.WhenAll(stdout, stderr);
            bool outputCompleted;
            try { outputCompleted = outputDrain.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { outputCompleted = true; }
            if (!outputCompleted)
            {
                outputCancellation.Cancel();
                try { outputCompleted = outputDrain.Wait(TimeSpan.FromSeconds(1)); }
                catch (AggregateException) { outputCompleted = true; }
                if (!outputCompleted)
                    throw new InvalidDataException("Oracle adapter left redirected output pipes open.");
            }
            if (timedOut)
                throw new InvalidDataException($"Oracle adapter timed out after {timeoutMilliseconds} ms.");
            if (outputDrain.IsCanceled || outputDrain.IsFaulted)
                throw new InvalidDataException("Oracle adapter output could not be drained safely.",
                    outputDrain.Exception?.GetBaseException());
            string output = stdout.GetAwaiter().GetResult();
            string error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                string detail = String.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidDataException($"Oracle adapter exited with code {process.ExitCode}: {TrimDiagnostic(detail)}");
            }
            string artifactPath = Path.Combine(runDirectory, "artifact.json");
            OracleArtifact artifact = OracleJson.LoadArtifact(artifactPath);
            ValidateRecordedArtifact(artifact, scenario, adapterIdentity, rom?.Identity);
            return new OracleRecordResult(runDirectory, artifact, process.ExitCode, false);
        }
        catch
        {
            // Keep the run-owned directory and adapter diagnostics for review;
            // callers can distinguish an invalid/incomplete run from absence
            // of a run without a shared temporary path.
            throw;
        }
    }

    /// <summary>
    /// Verifies the frozen extracted AMHE1 directory. This is separate from
    /// <see cref="VerifyRom"/> because extracted content and complete-cartridge
    /// identity are distinct evidence axes.
    /// </summary>
    public static FidelityReferenceManifest VerifyReference(string referenceDirectory,
        OracleSourceIdentity source)
    {
        if (!StringComparer.Ordinal.Equals(source.ProjectRevision, OracleJson.DefaultProjectRevision))
            throw new InvalidDataException("Oracle source Project Prime revision is not the frozen recording boundary.");
        if (!StringComparer.Ordinal.Equals(source.ReferenceRevision, OracleJson.Amhe1Revision))
            throw new InvalidDataException("Oracle source is not AMHE1 Revision 1.");
        if (!StringComparer.Ordinal.Equals(source.ReferenceAnchorPath, OracleJson.Amhe1AnchorPath)
            || !StringComparer.OrdinalIgnoreCase.Equals(source.ReferenceAnchorSha256, OracleJson.Amhe1AnchorSha256)
            || !StringComparer.OrdinalIgnoreCase.Equals(source.ReferenceAggregateSha256, OracleJson.Amhe1AggregateSha256))
            throw new InvalidDataException("Oracle source identity is not the frozen AMHE1 reference.");
        FidelityReferenceManifest manifest = FidelityManifestBuilder.Build(referenceDirectory,
            FidelityReferenceIdentity.Amhe1);
        if (!StringComparer.Ordinal.Equals(manifest.ContentVersion, source.ReferenceRevision)
            || !StringComparer.OrdinalIgnoreCase.Equals(manifest.AggregateSha256, source.ReferenceAggregateSha256))
            throw new InvalidDataException("The supplied AMHE1 reference does not match the scenario source identity.");
        return manifest;
    }

    /// <summary>
    /// Verifies one explicit private AMHE1 cartridge image without retaining,
    /// copying, or exposing its path in an artifact. Header identity is checked
    /// before the complete image digest is accepted.
    /// </summary>
    public static OracleRomIdentity VerifyRom(string romPath)
    {
        string full = Path.GetFullPath(romPath);
        FileInfo info = new(full);
        if (!info.Exists || info.LinkTarget != null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Oracle ROM must be an existing regular non-symbolic file.");
        CartridgeIdentity expected = OracleJson.Amhe1RomIdentity;
        if (info.Length != expected.Size)
            throw new InvalidDataException("Oracle ROM length does not match the frozen AMHE1 cartridge identity.");
        CartridgeValidationResult validation = CartridgeCatalog.Supported.ValidateFile(full);
        if (!validation.IsValid || validation.Identity is null
            || !StringComparer.Ordinal.Equals(validation.Identity.VariantCode, expected.VariantCode))
            throw new InvalidDataException(validation.Error
                ?? "Oracle ROM does not match the frozen AMHE1 cartridge identity.");
        string digest = validation.ActualSha256 ?? expected.Sha256;
        return new OracleRomIdentity("sha256:" + digest.ToLowerInvariant(),
            validation.Header.GameCode, validation.Header.Revision, validation.ActualLength);
    }

    public static void ValidateRecordedArtifact(OracleArtifact artifact, OracleScenario scenario,
        string adapterIdentity, string? expectedRomIdentity = null)
    {
        if (!StringComparer.Ordinal.Equals(artifact.Manifest.ScenarioId, scenario.Id))
            throw new InvalidDataException("Oracle adapter artifact scenario identity does not match the requested scenario.");
        if (artifact.Manifest.Seed != scenario.Seed)
            throw new InvalidDataException("Oracle adapter artifact seed does not match the requested scenario.");
        if (!StringComparer.Ordinal.Equals(artifact.Manifest.Source.ProjectRevision, scenario.Source.ProjectRevision)
            || !StringComparer.Ordinal.Equals(artifact.Manifest.Source.ReferenceRevision,
                scenario.Source.ReferenceRevision)
            || !StringComparer.Ordinal.Equals(artifact.Manifest.Source.ReferenceAnchorPath,
                scenario.Source.ReferenceAnchorPath)
            || !StringComparer.OrdinalIgnoreCase.Equals(artifact.Manifest.Source.ReferenceAnchorSha256,
                scenario.Source.ReferenceAnchorSha256)
            || !StringComparer.OrdinalIgnoreCase.Equals(artifact.Manifest.Source.ReferenceAggregateSha256,
                scenario.Source.ReferenceAggregateSha256))
            throw new InvalidDataException("Oracle adapter artifact source identity does not match the requested scenario.");
        if (!StringComparer.Ordinal.Equals(artifact.Manifest.Result, "normalized"))
            throw new InvalidDataException("Oracle adapter did not produce a normalized artifact.");
        if (!StringComparer.Ordinal.Equals(artifact.Manifest.OracleExecutableIdentity,
            adapterIdentity))
            throw new InvalidDataException("Oracle adapter executable identity does not match the launched file.");
        if (expectedRomIdentity is string verifiedRom)
        {
            if (!StringComparer.Ordinal.Equals(artifact.Manifest.RomIdentity, verifiedRom))
                throw new InvalidDataException("Oracle adapter artifact ROM identity does not match the verified private ROM.");
        }
        else if (!artifact.Manifest.RomIdentity.StartsWith("unverified:", StringComparison.Ordinal))
        {
            throw new InvalidDataException("No ROM was supplied to the adapter; oracle artifacts must mark romIdentity as unverified:<reason>.");
        }
        if (artifact.Manifest.ScenarioHash.Length != 64)
            throw new InvalidDataException("Oracle adapter artifact is missing a scenario hash.");
        string serialized = OracleJson.SerializeScenario(scenario);
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedHash, artifact.Manifest.ScenarioHash))
            throw new InvalidDataException("Oracle adapter artifact scenario hash does not match the requested scenario.");
    }

    private static string PrepareArtifactRoot(string path)
    {
        string root = Path.GetFullPath(path);
        DirectoryInfo info = new(root);
        if (info.Exists && (info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Oracle artifact root must not be a symbolic link.");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void EnsureRegularDirectory(string path, bool create)
    {
        DirectoryInfo info = new(path);
        if (info.Exists && (info.LinkTarget != null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Oracle run directories must not be symbolic links.");
        if (!info.Exists && create) Directory.CreateDirectory(path);
    }

    private static string ValidateRegularExecutable(string path)
    {
        string full = Path.GetFullPath(path);
        FileInfo info = new(full);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Oracle adapter must be an existing regular executable file.");
        if (info.Length <= 0 || info.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Oracle adapter executable exceeds its byte bound.");
        return full;
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string? ValidateRunId(string? value)
    {
        if (value == null) return null;
        if (value.Length is < 1 or > 64 || value.Any(ch => !(Char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new ArgumentException("Oracle run id must contain only letters, digits, '-' or '_'.", nameof(value));
        return value;
    }

    private static void ValidateTimeout(int timeoutMilliseconds)
    {
        if (timeoutMilliseconds is < 1 or > MaximumTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[8192];
        var builder = new StringBuilder(Math.Min(maximum, 8192));
        while (true)
        {
            int remaining = maximum - builder.Length;
            if (remaining == 0)
            {
                // Continue consuming the pipe so the child can exit, but do
                // not retain unbounded adapter output.
                while (await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken) > 0) { }
                return builder.ToString();
            }
            int count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (count == 0) return builder.ToString();
            builder.Append(buffer, 0, count);
        }
    }

    private static string TrimDiagnostic(string value)
    {
        string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }
}
