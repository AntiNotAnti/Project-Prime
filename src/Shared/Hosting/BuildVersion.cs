using System;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Update
{
    /// <summary>
    /// Which release this binary is, if it is one.
    ///
    /// Not <c>Program.Version</c>: that number is upstream's and tracks the
    /// state of the reverse engineering, not what has been published here. The
    /// release workflow stamps the tag it is building into the assembly, so a
    /// downloaded build knows exactly which release it came from and can be
    /// compared against the one on GitHub without guessing.
    ///
    /// A build that was not made by that workflow has no stamp, and
    /// <see cref="Current"/> is null. That is the case the updater has to
    /// respect above all others: a developer's own build must never be
    /// overwritten by a download because its version happened to compare low.
    /// </summary>
    internal static class BuildVersion
    {
        /// <summary>The release this binary is, or null for a local build.</summary>
        public static Version? Current => BuildIdentity.Current;

        /// <summary>True when this build came out of the release workflow.</summary>
        public static bool IsRelease => BuildIdentity.IsRelease;

        /// <summary>"v1.2.0", or "a local build" when there is no stamp.</summary>
        public static string Display => BuildIdentity.Display;

        /// <summary>
        /// "v1.2.0", "1.2.0", "1.2" -> a Version. Anything else, including the
        /// 1.0.0 the SDK invents when nothing was asked for, is not a release.
        /// </summary>
        public static Version? Parse(string? text) => BuildIdentity.Parse(text);

        /// <summary>Compare on three parts; the fourth is never in a tag.</summary>
        public static Version Normalise(Version version) => BuildIdentity.Normalise(version);
    }
}
