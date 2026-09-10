using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;

namespace ProjectPrime.PackageSmoke;

/// <summary>
/// Exercises the public server bundle from a clean extracted directory. This
/// deliberately uses the same WSS control and routed UDP admission contracts
/// as a client; a process that only starts successfully is not a package smoke
/// result.
/// </summary>
public static class Program
{
    private const string MapKey = "MP1 SANCTORUS";
    private const string ContentVersion = "AMHE1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            await RunAsync(options);
            Console.WriteLine("package smoke passed");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"package smoke failed: {error.Message}");
            return 1;
        }
    }

    private static async Task RunAsync(Options options)
    {
        string sourceBundle = Path.GetFullPath(options.Bundle);
        string contentRoot = Path.GetFullPath(options.ContentDirectory);
        if (!Directory.Exists(sourceBundle)) throw new ArgumentException("Bundle directory does not exist.");
        if (!Directory.Exists(contentRoot)) throw new ArgumentException("Content directory does not exist.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "project-prime-package-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Process? node = null;
        bool passed = false;
        try
        {
            string extracted = Path.Combine(tempRoot, "extracted");
            CopyBundle(sourceBundle, extracted);
            Console.WriteLine("phase: fresh extracted bundle");
            string unrelatedCwd = Path.Combine(tempRoot, "unrelated-cwd");
            Directory.CreateDirectory(unrelatedCwd);

            string nodePath = ResolveBundleFile(extracted, "ProjectPrimeServer", OperatingSystem.IsWindows());
            string workerPath = ResolveBundleFile(Path.Combine(extracted, "worker"), "ProjectPrime.Server.Worker", OperatingSystem.IsWindows());
            string example = Path.Combine(extracted, "server.example.json");
            if (!File.Exists(example)) throw new InvalidDataException("Extracted bundle is missing server.example.json.");
            if (File.Exists(Path.Combine(extracted, Path.GetFileName(workerPath))))
                throw new InvalidDataException("Worker executable leaked into the bundle root.");

            HashSet<int> workersBefore = WorkerProcessIds();
            WorkerContentIdentity content = await DescribeContentAsync(workerPath, contentRoot, tempRoot, options.Timeout);
            Console.WriteLine("phase: packaged Worker content identity");
            string replayRoot = Path.Combine(extracted, "smoke-replay");
            string artifactRoot = Path.Combine(extracted, "smoke-artifacts");
            Directory.CreateDirectory(replayRoot);
            Directory.CreateDirectory(artifactRoot);

            Guid nodeId = Guid.NewGuid();
            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string publicKeyPath = Path.Combine(extracted, "smoke-node-public.pem");
            await File.WriteAllTextAsync(publicKeyPath, signingKey.ExportSubjectPublicKeyInfoPem());
            string certificatePath = Path.Combine(extracted, "smoke-tls.pfx");
            const string certificatePassword = "package-smoke-certificate";
            using (RSA tlsKey = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
                await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword));
            }

            int port = FreeTcpPort();
            string https = $"https://127.0.0.1:{port}";
            WriteSmokeConfiguration(extracted, nodeId, publicKeyPath, certificatePath, certificatePassword,
                https, workerPath, content, contentRoot, replayRoot, artifactRoot);

            node = StartNode(nodePath, extracted, unrelatedCwd, https, certificatePath, certificatePassword, tempRoot);
            await WaitForHealthAsync(https, node, options.Timeout);
            Console.WriteLine("phase: packaged Node health");
            using var control = new ClientWebSocket();
            control.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            string token = CreateAdmissionToken(signingKey, nodeId);
            control.Options.SetRequestHeader("Authorization", "Bearer " + token);
            using var controlTimeout = new CancellationTokenSource(options.Timeout);
            await control.ConnectAsync(new Uri(https.Replace("https://", "wss://", StringComparison.Ordinal) + "/v1/control"), controlTimeout.Token);
            using (await WaitForTypeAsync(control, "node.session", controlTimeout.Token)) { }
            Console.WriteLine("phase: authenticated WSS control");

            using JsonDocument created = await SendCommandAsync(control, "lobby.create",
                new { name = "Package smoke", visibility = "Public", playerLimit = 2, observerLimit = 0 }, controlTimeout.Token);
            long revision = created.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();
            using JsonDocument configured = await SendCommandAsync(control, "lobby.configure",
                new { expectedRevision = revision, mapKey = MapKey, mode = "Battle", botCount = 1, timeLimitSeconds = 60 }, controlTimeout.Token);
            revision = configured.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();
            using JsonDocument ready = await SendCommandAsync(control, "lobby.ready.set",
                new { ready = true, expectedRevision = revision }, controlTimeout.Token);
            revision = ready.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();

            Guid startRequest = Guid.NewGuid();
            await SendFrameAsync(control, "lobby.start", startRequest, new { expectedRevision = revision }, controlTimeout.Token);
            NodeMatchHandoff handoff = await WaitForHandoffAsync(control, startRequest, controlTimeout.Token);
            Console.WriteLine($"phase: lobby create/configure/start and Worker handoff ({handoff.Host}:{handoff.Port})");
            UdpAdmission admission = await AdmitUdpAsync(handoff, controlTimeout.Token);
            Console.WriteLine($"phase: UDP admission (connection {admission.ConnectionId})");
            await SendReadyAsync(handoff, admission, controlTimeout.Token);
            using (await WaitForTypeAsync(control, "match.ended", controlTimeout.Token)) { }
            await WaitForArtifactsAsync(replayRoot, artifactRoot, options.Timeout);
            Console.WriteLine("phase: match ended and replay/artifact roots writable");
            await control.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "smoke complete", CancellationToken.None);

            RequestGracefulStop(node);
            await node.WaitForExitAsync().WaitAsync(options.Timeout);
            node = null;
            await WaitForNoNewWorkersAsync(workersBefore, options.Timeout);
            Console.WriteLine("phase: graceful Node drain and no orphan Worker");
            passed = true;
        }
        finally
        {
            if (node is { HasExited: false })
            {
                try { node.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                try { await node.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            if (!passed && Environment.GetEnvironmentVariable("KEEP_PACKAGE_SMOKE_ARTIFACTS") == "1")
                Console.Error.WriteLine("package smoke artifacts retained at " + tempRoot);
            else
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void WriteSmokeConfiguration(string root, Guid nodeId, string publicKeyPath,
        string certificatePath, string certificatePassword, string https, string workerPath,
        WorkerContentIdentity content, string contentRoot, string replayRoot, string artifactRoot)
    {
        string workerFileName = OperatingSystem.IsWindows() ? "worker/ProjectPrime.Server.Worker.exe" : "worker/ProjectPrime.Server.Worker";
        // Keep the executable package-relative to prove the Node resolver. The
        // absolute workerPath is used only to assert the extracted file exists.
        if (!File.Exists(workerPath)) throw new FileNotFoundException("Worker executable is missing from the extracted bundle.", workerPath);
        var config = new
        {
            Node = new
            {
                MaximumLobbies = 16,
                MaximumSessions = 16,
                Authentication = new
                {
                    NodeId = nodeId,
                    Issuer = "https://package-smoke.example",
                    Keys = new[] { new { KeyId = "smoke", PublicKeyPemPath = publicKeyPath } }
                },
                Maps = new[]
                {
                    new { MapKey, ContentHash = content.ContentHash, ContentVersion = content.ContentVersion,
                        BuildVersion = content.BuildVersion, ProtocolVersion = content.ProtocolVersion }
                },
                Workers = new
                {
                    DrainTimeout = "00:00:20",
                    ForceAfterDrainDeadline = true,
                    Processes = new[]
                    {
                        new
                        {
                            FileName = workerFileName,
                            Content = content,
                            ArtifactDirectory = artifactRoot,
                            Arguments = new[] { "--content-dir", contentRoot, "--content-version", ContentVersion,
                                "--lanes", "2", "--max-matches", "4", "--max-matches-per-lane", "2",
                                "--replay-dir", replayRoot },
                            Capacity = new { MatchLimit = 4, PlayerLimit = 32, ActiveMatches = 0, ActivePlayers = 0 },
                            StartupTimeout = "00:00:30",
                            HeartbeatTimeout = "00:00:10",
                            ShutdownTimeout = "00:00:10"
                        }
                    }
                }
            },
            Kestrel = new
            {
                Certificates = new { Default = new { Path = certificatePath, Password = certificatePassword } }
            },
            Urls = https
        };
        File.WriteAllText(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(config, Json));
    }

    private static Process StartNode(string nodePath, string root, string cwd, string https,
        string certificatePath, string certificatePassword, string tempRoot)
    {
        var start = new ProcessStartInfo(nodePath)
        {
            UseShellExecute = false,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(root);
        start.Environment["ASPNETCORE_URLS"] = https;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificatePath;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Packaged Node did not start.");
        _ = DrainAsync(process.StandardOutput, Path.Combine(tempRoot, "node.stdout.log"));
        _ = DrainAsync(process.StandardError, Path.Combine(tempRoot, "node.stderr.log"));
        return process;
    }

    private static async Task DrainAsync(StreamReader input, string path)
    {
        try
        {
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            await input.BaseStream.CopyToAsync(output);
        }
        catch { }
    }

    private static async Task<WorkerContentIdentity> DescribeContentAsync(string workerPath, string contentRoot,
        string tempRoot, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(workerPath)
        {
            UseShellExecute = false, WorkingDirectory = tempRoot,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string value in new[] { "--describe-content", "true", "--content-dir", contentRoot, "--content-version", ContentVersion })
            start.ArgumentList.Add(value);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Packaged Worker did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { Kill(process); throw new TimeoutException("Worker content identity timed out."); }
        string output = await stdout;
        _ = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Packaged Worker could not load the supplied content.");
        string line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? throw new InvalidDataException("Worker content identity was empty.");
        WorkerContentIdentity? content = JsonSerializer.Deserialize<WorkerContentIdentity>(line, Json);
        content?.Validate();
        return content ?? throw new InvalidDataException("Worker content identity was invalid.");
    }

    private static async Task WaitForHealthAsync(string https, Process node, TimeSpan timeout)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        using var deadline = new CancellationTokenSource(timeout);
        Uri uri = new(https + "/health");
        while (!deadline.IsCancellationRequested)
        {
            if (node.HasExited) throw new InvalidOperationException("Packaged Node exited before health became available.");
            try
            {
                using HttpResponseMessage response = await client.GetAsync(uri, deadline.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
            await Task.Delay(100, deadline.Token);
        }
        throw new TimeoutException("Packaged Node health endpoint did not become ready.");
    }

    private static async Task<JsonDocument> SendCommandAsync(ClientWebSocket socket, string type,
        object payload, CancellationToken cancellationToken)
    {
        Guid requestId = Guid.NewGuid();
        await SendFrameAsync(socket, type, requestId, payload, cancellationToken);
        while (true)
        {
            JsonDocument message = await ReadAsync(socket, cancellationToken);
            string? messageType = message.RootElement.GetProperty("type").GetString();
            Guid? responseId = message.RootElement.GetProperty("requestId").ValueKind == JsonValueKind.String
                ? message.RootElement.GetProperty("requestId").GetGuid() : null;
            if (responseId == requestId)
            {
                if (messageType == "error")
                {
                    string reason = message.RootElement.GetProperty("payload").GetProperty("message").GetString() ?? "control command failed";
                    message.Dispose();
                    throw new InvalidOperationException(reason);
                }
                return message;
            }
            message.Dispose();
        }
    }

    private static async Task SendFrameAsync(ClientWebSocket socket, string type, Guid requestId,
        object payload, CancellationToken cancellationToken)
    {
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type, requestId, payload }, Json);
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<NodeMatchHandoff> WaitForHandoffAsync(ClientWebSocket socket, Guid requestId, CancellationToken cancellationToken)
    {
        while (true)
        {
            using JsonDocument message = await ReadAsync(socket, cancellationToken);
            string? type = message.RootElement.GetProperty("type").GetString();
            if (type == "error" && message.RootElement.GetProperty("requestId").ValueKind == JsonValueKind.String
                && message.RootElement.GetProperty("requestId").GetGuid() == requestId)
                throw new InvalidOperationException(message.RootElement.GetProperty("payload").GetProperty("message").GetString());
            if (type != "match.handoff") continue;
            NodeMatchHandoff? handoff = message.RootElement.GetProperty("payload").Deserialize<NodeMatchHandoff>(Json);
            return handoff ?? throw new InvalidDataException("Match handoff payload was empty.");
        }
    }

    private static async Task<JsonDocument> WaitForTypeAsync(ClientWebSocket socket, string type, CancellationToken cancellationToken)
    {
        while (true)
        {
            JsonDocument message = await ReadAsync(socket, cancellationToken);
            string? messageType = message.RootElement.GetProperty("type").GetString();
            if (messageType == type) return message;
            if (messageType == "error")
            {
                string reason = message.RootElement.GetProperty("payload").GetProperty("message").GetString() ?? "control command failed";
                message.Dispose();
                throw new InvalidOperationException(reason);
            }
            message.Dispose();
        }
    }

    private static async Task<JsonDocument> ReadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[NodeControlCodec.MaximumFrameBytes];
        int length = 0;
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(bytes.AsMemory(length), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Node control socket closed during smoke.");
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Node control returned a non-text frame.");
            length += result.Count;
            if (length == bytes.Length && !result.EndOfMessage) throw new InvalidDataException("Node control frame exceeded its limit.");
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(bytes.AsMemory(0, length));
    }

    private static async Task<UdpAdmission> AdmitUdpAsync(NodeMatchHandoff handoff, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var join = new JoinPacket(NetHeader.Version, handoff.Nonce, handoff.Hunter, "Smoke", Ticket: handoff.Ticket,
            Observer: handoff.Observer, WireMatchId: handoff.WireMatchId);
        byte[] datagram = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(datagram);
        join.Write(datagram.AsSpan(NetHeader.Size));
        IPEndPoint endpoint = new(IPAddress.Parse(handoff.Host), handoff.Port);
        await udp.SendAsync(datagram, endpoint, cancellationToken);
        Console.WriteLine($"phase: UDP join sent ({datagram.Length} bytes to {endpoint})");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            UdpReceiveResult received;
            try { received = await udp.ReceiveAsync(deadline.Token); }
            catch (OperationCanceledException) { throw new TimeoutException("Worker UDP admission did not complete."); }
            if (!NetHeader.TryRead(received.Buffer, out NetHeader header)) continue;
            Console.WriteLine($"phase: UDP response {header.Type} ({received.Buffer.Length} bytes from {received.RemoteEndPoint})");
            if (header.Type == NetMessageType.JoinPending) continue;
            if (header.Type == NetMessageType.Refused) throw new InvalidOperationException("Worker refused the routed admission.");
            if (header.Type != NetMessageType.Accepted) continue;
            ReadOnlySpan<byte> body = received.Buffer.AsSpan(NetHeader.Size);
            JoinAcceptedPacket accepted;
            if (!JoinAcceptedPacket.TryRead(body, out accepted))
            {
                if (!ReliableEventPacket.TryRead(body, out _, out ReliableEventType welcomeType, out ReadOnlySpan<byte> welcomeBody)
                    || welcomeType != ReliableEventType.Welcome || !JoinAcceptedPacket.TryRead(welcomeBody, out accepted)) continue;
            }
            if (accepted.ClientNonce == handoff.Nonce && accepted.MatchId == handoff.WireMatchId)
                return new UdpAdmission(accepted, header.ConnectionId);
        }
    }

    private static async Task SendReadyAsync(NodeMatchHandoff handoff, UdpAdmission admission, CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        byte[] eventBody = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(eventBody, admission.Accepted.MatchId);
        byte[] datagram = new byte[NetHeader.Size + ReliableEventPacket.HeaderSize + eventBody.Length];
        new NetHeader(NetMessageType.Event, NetHeaderFlags.None, admission.ConnectionId, 0, 0, 0).Write(datagram);
        ReliableEventPacket.Write(datagram.AsSpan(NetHeader.Size), 0, ReliableEventType.ClientReady, eventBody);
        await udp.SendAsync(datagram, new IPEndPoint(IPAddress.Parse(handoff.Host), handoff.Port), cancellationToken);
    }

    private sealed record UdpAdmission(JoinAcceptedPacket Accepted, ulong ConnectionId);

    private static async Task WaitForArtifactsAsync(string replayRoot, string artifactRoot, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested)
        {
            bool replay = Directory.EnumerateFiles(replayRoot, "*", SearchOption.AllDirectories).Any();
            bool report = Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories).Any();
            if (replay && report) return;
            await Task.Delay(100, deadline.Token);
        }
        throw new TimeoutException("Worker did not persist replay and artifact roots before shutdown.");
    }

    private static string CreateAdmissionToken(ECDsa key, Guid nodeId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "pp-node-admission+jwt", kid = "smoke" }, Json));
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "https://package-smoke.example",
            aud = "urn:project-prime:node:" + nodeId.ToString("D"),
            sub = Guid.NewGuid().ToString("D"), jti = Guid.NewGuid().ToString("D"), name = "Smoke",
            iat = now, nbf = now, exp = now + 120
        }, Json));
        string input = header + "." + payload;
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return input + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void CopyBundle(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bundle contains a directory link.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Bundle contains a file link.");
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(target, File.GetUnixFileMode(file)); } catch (PlatformNotSupportedException) { }
            }
        }
    }

    private static string ResolveBundleFile(string root, string stem, bool windows)
    {
        string path = Path.Combine(root, stem + (windows ? ".exe" : ""));
        if (!File.Exists(path)) throw new FileNotFoundException("Bundle apphost is missing.", path);
        return path;
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HashSet<int> WorkerProcessIds()
    {
        var ids = new HashSet<int>();
        try { foreach (Process process in Process.GetProcessesByName("ProjectPrime.Server.Worker")) { ids.Add(process.Id); process.Dispose(); } }
        catch { }
        return ids;
    }

    private static async Task WaitForNoNewWorkersAsync(HashSet<int> baseline, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (!deadline.IsCancellationRequested)
        {
            if (!WorkerProcessIds().Except(baseline).Any()) return;
            await Task.Delay(100, deadline.Token);
        }
        throw new InvalidOperationException("A packaged Worker process remained after Node shutdown.");
    }

    private static void RequestGracefulStop(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            try { if (process.CloseMainWindow()) return; } catch (InvalidOperationException) { }
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try { if (NativeKill(process.Id, 15) == 0) return; } catch { }
        }
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int NativeKill(int processId, int signal);

    private sealed record Options(string Bundle, string ContentDirectory, TimeSpan Timeout)
    {
        public static Options Parse(string[] args)
        {
            string? bundle = null, content = null;
            TimeSpan timeout = TimeSpan.FromSeconds(90);
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--bundle" when ++i < args.Length: bundle = args[i]; break;
                    case "--content-dir" when ++i < args.Length: content = args[i]; break;
                    case "--timeout" when ++i < args.Length && int.TryParse(args[i], out int seconds): timeout = TimeSpan.FromSeconds(seconds); break;
                    default: throw new ArgumentException("Usage: --bundle DIRECTORY --content-dir DIRECTORY [--timeout SECONDS]");
                }
            }
            if (string.IsNullOrWhiteSpace(bundle) || string.IsNullOrWhiteSpace(content) || timeout <= TimeSpan.Zero)
                throw new ArgumentException("Usage: --bundle DIRECTORY --content-dir DIRECTORY [--timeout SECONDS]");
            return new(bundle, content, timeout);
        }
    }
}
