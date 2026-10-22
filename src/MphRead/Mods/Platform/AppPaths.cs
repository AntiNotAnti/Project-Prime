using System;
using System.IO;

namespace MphRead.Mods.Platform
{
    /// <summary>Installation resources and writable desktop state have different roots.</summary>
    internal static class AppPaths
    {
        public static string ExecutableDirectory => AppContext.BaseDirectory;
        public static string ResourceDirectory
        {
            get
            {
                var executable = new DirectoryInfo(ExecutableDirectory);
                bool bundled = OperatingSystem.IsMacOS() && executable.Name == "MacOS"
                    && executable.Parent?.Name == "Contents"
                    && executable.Parent.Parent?.Extension == ".app";
                return bundled ? Path.Combine(executable.Parent!.FullName, "Resources") : ExecutableDirectory;
            }
        }
        public static string Maps => Path.Combine(ResourceDirectory, "maps");

        // Other desktop platforms keep their existing portable layout. Android
        // sets GameFiles.Root and LauncherPrefs.Directory from its activity.
        public static string UserDataDirectory => Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") is { Length: > 0 } directory
            ? Path.GetFullPath(directory) : OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", Branding.Name)
            : ExecutableDirectory;

        public static string PathsFile => Path.Combine(UserDataDirectory, "paths.txt");

        public static void PrepareUserData()
        {
            // UserDataDirectory may be an isolated path supplied by automation,
            // a launcher, or a developer harness. Treat that override as a
            // directory contract on every desktop platform, not only macOS.
            // CreateDirectory is harmless when the portable Windows/Linux
            // executable directory already exists and still surfaces genuine
            // permission/path failures before we change the process cwd.
            Directory.CreateDirectory(UserDataDirectory);
        }
    }
}
