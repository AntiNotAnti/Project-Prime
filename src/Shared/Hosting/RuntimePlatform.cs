using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Update
{
    /// <summary>Runtime package identifier shared by the executable hosts.</summary>
    internal static class RuntimePlatform
    {
        public static string Rid()
        {
            // Android first, and not as an "os-arch" pair: the APK is one file
            // for every ABI (release.yml publishes FruityPrime-<tag>-android.apk
            // and nothing per-architecture), so matching on the architecture
            // here would find nothing on every phone.
            if (OperatingSystem.IsAndroid())
            {
                return "android";
            }
            string os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            };
            return $"{os}-{arch}";
        }

    }
}
