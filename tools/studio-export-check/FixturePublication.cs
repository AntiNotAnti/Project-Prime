using System.Text;

internal static class FixturePublication
{
    internal static void PublishText(string destination, string value)
    {
        string staging = destination + ".fixture." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(staging, value); File.Move(staging, destination, overwrite: true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    internal static string ReadText(string path)
    {
        using var snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (snapshot.Length > 1024 * 1024) throw new InvalidDataException("Fixture signal exceeds its size limit.");
        using var reader = new StreamReader(snapshot, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
