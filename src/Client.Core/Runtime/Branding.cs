namespace MphRead.Mods
{
    /// <summary>
    /// What this program is called, in one place.
    ///
    /// The product identity is Project Prime. The root namespace remains MphRead
    /// because it is the upstream/core namespace.
    /// </summary>
    public static class Branding
    {
        public static System.Version EngineVersion { get; } = new System.Version(0, 35, 1, 0);
        /// <summary>The product, as a person would write it.</summary>
        public const string Name = "Project Prime";

        /// <summary>The desktop binary/archive identifier.</summary>
        public const string FileName = "ProjectPrime";

        /// <summary>What upstream is, and what this is a fork of.</summary>
        public const string Upstream = "MphRead";

        /// <summary>
        /// Source repository for Project Prime.
        /// </summary>
        public const string Repository = "AntiNotAnti/Project-Prime";

        /// <summary>
        /// Public binary-only repository queried for signed client updates.
        /// No source credentials are embedded in the player.
        /// </summary>
        public const string UpdateRepository = "AntiNotAnti/Project-Prime-Releases";

        /// <summary>
        /// The name of the running executable, without its extension. Read
        /// rather than assumed, so that a renamed copy still prints commands
        /// somebody can actually type.
        /// </summary>
        public static string Executable
        {
            get
            {
                string? path = System.Environment.ProcessPath;
                if (path == null)
                {
                    return FileName;
                }
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                return name.Length > 0 ? name : FileName;
            }
        }

        /// <summary>
        /// "Project Prime v1.2.0", or "Project Prime (a local build)" when this
        /// was not made by the release workflow -- which is worth saying out
        /// loud, because it is also the case where the updater stands down.
        /// </summary>
        public static string NameAndVersion => Update.BuildVersion.IsRelease
            ? $"{Name} {Update.BuildVersion.Display}"
            : $"{Name} ({Update.BuildVersion.Display})";
    }
}
