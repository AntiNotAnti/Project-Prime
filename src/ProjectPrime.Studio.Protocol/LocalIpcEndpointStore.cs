using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.Protocol;

public sealed record StudioEndpointDescriptor(int Version, string PipeName, string Secret, int ProcessId, StudioEndpointRole Role = StudioEndpointRole.Studio);

/// <summary>The descriptor and durable lock are private to the OS user and this installation.</summary>
public static class LocalIpcEndpointStore
{
    public static string GetDirectory(string installationDirectory, string userDataDirectory)
    {
        string installation = Path.GetFullPath(installationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) installation = installation.ToUpperInvariant();
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation)))[..24];
        return Path.Combine(Path.GetFullPath(userDataDirectory), "ipc", identity);
    }

    public static string GetDescriptorPath(string installationDirectory, string userDataDirectory, StudioEndpointRole role = StudioEndpointRole.Studio)
        => Path.Combine(GetDirectory(installationDirectory, userDataDirectory), DescriptorName(role));

    public static void EnsurePrivateDirectory(string directory)
    {
        RefuseLink(directory);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User
                ?? throw new UnauthorizedAccessException("The current Windows user could not be identified.");
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).Create(security);
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static FileStream OpenLock(string directory, StudioEndpointRole role = StudioEndpointRole.Studio)
    {
        string path = Path.Combine(directory, role == StudioEndpointRole.Game ? "game.lock" : "instance.lock");
        RefuseLink(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        // Never unlink this inode: opening a replacement would bypass a live owner's lock.
        return new FileStream(path, options);
    }

    public static void Write(string directory, StudioEndpointDescriptor descriptor)
    {
        string destination = Path.Combine(directory, DescriptorName(descriptor.Role));
        RefuseLink(destination);
        string temporary = Path.Combine(directory, ".endpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, descriptor, StudioProtocol.JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static StudioEndpointDescriptor Read(string installationDirectory, string userDataDirectory, StudioEndpointRole role = StudioEndpointRole.Studio)
        => ReadPath(GetDescriptorPath(installationDirectory, userDataDirectory, role), role);

    public static StudioEndpointDescriptor ReadPath(string path, StudioEndpointRole role = StudioEndpointRole.Studio)
    {
        RefuseLink(path);
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode permissions = File.GetUnixFileMode(path);
            if ((permissions & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new UnauthorizedAccessException("The Studio IPC descriptor is not private to the current user.");
        }
        else
        {
            var user = WindowsIdentity.GetCurrent().User;
            var acl = new FileInfo(path).GetAccessControl();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))
                    throw new UnauthorizedAccessException("The Studio IPC descriptor is not private to the current user.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 4096) throw new StudioProtocolException("The Studio IPC descriptor exceeds the size limit.");
        StudioEndpointDescriptor descriptor;
        try { descriptor = JsonSerializer.Deserialize<StudioEndpointDescriptor>(stream, StudioProtocol.JsonOptions)
            ?? throw new StudioProtocolException("The Studio IPC descriptor is empty."); }
        catch (JsonException) { throw new StudioProtocolException("The Studio IPC descriptor is malformed."); }
        if (descriptor.Version != StudioProtocol.StudioIpcVersion)
            throw new StudioProtocolException("Studio <-> ProjectPrime version mismatch.");
        if (string.IsNullOrEmpty(descriptor.PipeName) || string.IsNullOrEmpty(descriptor.Secret)
            || descriptor.Role != role || !descriptor.PipeName.StartsWith(role == StudioEndpointRole.Game ? "ProjectPrime.Game." : "ProjectPrime.Studio.", StringComparison.Ordinal)
            || descriptor.PipeName.Length > 100 || descriptor.PipeName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')
            || descriptor.Secret.Length > 64 || descriptor.ProcessId <= 0)
            throw new StudioProtocolException("The Studio IPC descriptor is invalid.");
        _ = StudioIpcAuthentication.CreateProof(descriptor.Secret, StudioIpcAuthentication.NewNonce(), "client", descriptor.Version);
        return descriptor;
    }

    private static string DescriptorName(StudioEndpointRole role) => role == StudioEndpointRole.Game ? "game-endpoint.json" : "endpoint.json";

    public static string DefaultUserDataDirectory
    {
        get
        {
            if (Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_IPC_DATA") is { Length: > 0 } custom) return Path.GetFullPath(custom);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string data = OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support")
                : OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathFullyQualified(xdg) ? xdg : Path.Combine(home, ".local", "share");
            return Path.Combine(data, "ProjectPrime", "studio");
        }
    }

    private static void RefuseLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Studio IPC files cannot be symbolic links or reparse points.");
    }
}
