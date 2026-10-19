using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Launcher;

public static partial class AdventureSave
{
    private static readonly object ManagementGate = new();
    private static bool SaveHash(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool SameHash(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string ByteFingerprint(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input));
    }
    /// <summary>Expected-byte copy for admitted menus. No serializer or gameplay state is consulted.</summary>
    public static bool TryCopy(byte source, byte destination, string expectedSource, string expectedDestination, out string status)
    {
        status = "COPY REFUSED // REFRESH SAVES";
        if (source is < 1 or > SlotCount || destination is < 1 or > SlotCount || source == destination
            || !SaveHash(expectedSource) || !(SaveHash(expectedDestination) || expectedDestination == "EMPTY")) return false;
        lock (ManagementGate)
        {
            string from = SlotPath(source), to = SlotPath(destination);
            string staging = to + ".copy-" + Guid.NewGuid().ToString("N");
            string? backup = null; bool published = false, movedDestination = false;
            try
            {
                if (!SameHash(ContentFingerprint(source), expectedSource) || !SameHash(ContentFingerprint(destination), expectedDestination)) return false;
                File.Copy(from, staging, overwrite: false);
                if (!SameHash(ByteFingerprint(staging), expectedSource)
                    || !SameHash(ContentFingerprint(source), expectedSource) || !SameHash(ContentFingerprint(destination), expectedDestination)) return false;
                if (expectedDestination != "EMPTY")
                {
                    backup = NextBackup(to);
                    // Atomic no-overwrite name reservation preserves the actual moved bytes
                    // even if an external writer raced the preceding fingerprint read.
                    File.Move(to, backup, overwrite: false);
                    movedDestination = true;
                    if (!SameHash(ByteFingerprint(backup), expectedDestination)) throw new IOException("Save changed before copy admission.");
                }
                if (!SameHash(ContentFingerprint(source), expectedSource) || ContentFingerprint(destination) != "EMPTY")
                    throw new IOException("Save changed before copy publication.");
                File.Move(staging, to, overwrite: false);
                published = true;
                if (!SameHash(ContentFingerprint(source), expectedSource) || !SameHash(ContentFingerprint(destination), expectedSource))
                { status = "COPY OUTCOME UNVERIFIED // REFRESH SAVES; RETAINED BACKUPS PRESERVED"; return false; }
                status = "COPY VERIFIED // EXACT SOURCE BYTES COPIED; PREVIOUS DESTINATION PRESERVED";
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (!published && movedDestination && backup != null)
                {
                    try { if (ContentFingerprint(destination) == "EMPTY") File.Move(backup, to, overwrite: false); }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { }
                }
                status = "COPY REFUSED OR OUTCOME UNVERIFIED // REFRESH SAVES; RETAINED BACKUPS PRESERVED";
                return false;
            }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Archive exact captured bytes with no permanent deletion and no backup replacement.</summary>
    public static bool TryArchive(byte slot, string expected, out string status)
    {
        status = "ARCHIVE REFUSED // REFRESH SAVES";
        if (slot is < 1 or > SlotCount || !SaveHash(expected)) return false;
        lock (ManagementGate)
        {
            try
            {
                if (!SameHash(ContentFingerprint(slot), expected)) return false;
                string path = SlotPath(slot), backup = NextBackup(path);
                File.Move(path, backup, overwrite: false);
                if (ContentFingerprint(slot) != "EMPTY" || !SameHash(ByteFingerprint(backup), expected))
                { status = "ARCHIVE OUTCOME UNVERIFIED // REFRESH SAVES; MOVED BYTES RETAINED IN BACKUP"; return false; }
                status = "ARCHIVE VERIFIED // SLOT EMPTY; EXACT BYTES RETAINED IN BACKUP";
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status = "ARCHIVE REFUSED OR OUTCOME UNVERIFIED // REFRESH SAVES; RETAINED BACKUPS PRESERVED"; return false; }
        }
    }
}
