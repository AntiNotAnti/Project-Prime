using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.IPC;

/// <summary>Studio facade over the shared current-user capability store.</summary>
public static class StudioEndpointStore
{
    public static string GetDirectory(string installationDirectory, string userDataDirectory)
        => LocalIpcEndpointStore.GetDirectory(installationDirectory, userDataDirectory);
    public static string GetDescriptorPath(string installationDirectory, string userDataDirectory)
        => LocalIpcEndpointStore.GetDescriptorPath(installationDirectory, userDataDirectory);
    internal static void EnsurePrivateDirectory(string directory) => LocalIpcEndpointStore.EnsurePrivateDirectory(directory);
    internal static FileStream OpenLock(string directory) => LocalIpcEndpointStore.OpenLock(directory);
    internal static void Write(string directory, StudioEndpointDescriptor descriptor) => LocalIpcEndpointStore.Write(directory, descriptor);
    public static StudioEndpointDescriptor Read(string installationDirectory, string userDataDirectory)
        => LocalIpcEndpointStore.Read(installationDirectory, userDataDirectory);
    public static StudioEndpointDescriptor ReadPath(string path) => LocalIpcEndpointStore.ReadPath(path);
}
