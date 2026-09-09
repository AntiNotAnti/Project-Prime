namespace MphRead.Mods
{
    /// <summary>
    /// What this program is called, in one place.
    ///
    /// The official product name is Prime Hunters. Existing binary names,
    /// package identities and repository URLs remain compatible with installs
    /// released before the rename. The root namespace remains MphRead.
    /// </summary>
    public static class Branding
    {
        public static System.Version EngineVersion { get; } = new System.Version(0, 35, 1, 0);
        /// <summary>The product, as a person would write it.</summary>
        public const string Name = "Prime Hunters";

        /// <summary>The existing binary/archive identifier, preserved for update compatibility.</summary>
        public const string FileName = "FruityPrime";

        /// <summary>What upstream is, and what this is a fork of.</summary>
        public const string Upstream = "MphRead";

        /// <summary>
        /// Historical repository identity retained for compatibility links.
        ///
        /// The project was forked as MphRead and the repository has since been
        /// Update discovery uses <see cref="UpdateRepository"/> separately so
        /// an unconfigured release feed cannot advertise unrelated releases.
        /// </summary>
        public const string Repository = "liveteklol/Fruity-Prime";

        /// <summary>
        /// Repository queried for published client updates. Empty until a
        /// release feed and packages are intentionally configured.
        /// </summary>
        public const string UpdateRepository = "";

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
        /// "Prime Hunters v1.2.0", or "Prime Hunters (a local build)" when this
        /// was not made by the release workflow -- which is worth saying out
        /// loud, because it is also the case where the updater stands down.
        /// </summary>
        public static string NameAndVersion => Update.BuildVersion.IsRelease
            ? $"{Name} {Update.BuildVersion.Display}"
            : $"{Name} ({Update.BuildVersion.Display})";
    }
}
