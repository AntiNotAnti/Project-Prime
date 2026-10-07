#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.StudioReplay;

/// <summary>Reads one closed status snapshot without preventing the worker's next atomic replacement.</summary>
public static class StudioReplayStatusFile
{
    private const int MaximumBytes = 65536;

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
