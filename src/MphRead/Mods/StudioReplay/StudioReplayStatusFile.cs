#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.StudioReplay;

/// <summary>Publishes and reads closed status snapshots without modifying an opened version.</summary>
public static class StudioReplayStatusFile
{
    private const int MaximumBytes = 65536;

    public static void Write(string path, StudioReplayExportStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(status);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Export status exceeds its size limit.");
        string staging = path + ".status." + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                output.Write(bytes);
            // MoveFileEx replacement can reject an open Windows destination
            // even when it grants delete sharing. ReplaceFile opens the old
            // version for read/delete, compatible with our immutable reader.
            if (OperatingSystem.IsWindows() && File.Exists(path))
                File.Replace(staging, path, destinationBackupFileName: null);
            else
                File.Move(staging, path, overwrite: !OperatingSystem.IsWindows());
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    public static StudioReplayExportStatus? Read(string path)
    {
        using var snapshot = OpenSnapshot(path);
        long length = snapshot.Length;
        if (length > MaximumBytes) throw new InvalidDataException("Export status exceeds its size limit.");
        byte[] bytes = new byte[checked((int)length)];
        snapshot.ReadExactly(bytes);
        if (snapshot.ReadByte() != -1) throw new InvalidDataException("Export status changed during its snapshot read.");
        return JsonSerializer.Deserialize<StudioReplayExportStatus>(bytes);
    }

    // Windows requires delete sharing for replacement by rename. Write sharing
    // remains denied, so the version represented by this open handle is immutable.
    internal static FileStream OpenSnapshot(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
}
#endif
