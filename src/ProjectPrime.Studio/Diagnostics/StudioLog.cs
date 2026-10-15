namespace ProjectPrime.Studio.Diagnostics;

public sealed class StudioLog(string path)
{
    private readonly object _gate = new();
    public void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // A bounded local diagnostic log, never a credential store.
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message.Replace('\n', ' ')}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
