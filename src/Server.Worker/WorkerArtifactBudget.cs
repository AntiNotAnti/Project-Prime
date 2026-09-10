namespace ProjectPrime.Server.Worker;

/// <summary>Admission fence over worker-owned artifacts. Existing recordings are never silently deleted.</summary>
internal static class WorkerArtifactBudget
{
    public static bool HasHeadroom(WorkerOptions options, int activeMatches)
    {
        long bytes = 0; int files = 0;
        foreach (string root in new[] { options.ArtifactDirectory, options.ReplayDirectory }.OfType<string>().Select(Path.GetFullPath).Distinct())
        {
            if (!Directory.Exists(root)) continue;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            var pending = new Stack<string>(); pending.Push(root);
            int directories = 0;
            while (pending.Count > 0)
            {
                if (++directories > options.MaximumArtifactFiles) return false;
                foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); } catch (FileNotFoundException) { continue; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                    if (++files >= options.MaximumArtifactFiles) return false;
                    try { bytes += new FileInfo(entry).Length; } catch (FileNotFoundException) { continue; }
                    if (bytes >= options.MaximumArtifactBytes) return false;
                }
            }
        }
        return bytes + (activeMatches + 1L) * options.ArtifactReservationBytes <= options.MaximumArtifactBytes;
    }
}
