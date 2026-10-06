using System.Text;

namespace ProjectPrime.Studio.Replay;

/// <summary>Managed worker output never depends on a pipe owned by the editing process.</summary>
internal sealed class ReplayExportWorkerLog(string path) : TextWriter
{
    private const int TailCharacters = 16384;
    private readonly object _gate = new();
    private readonly StringBuilder _tail = new();
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => Append(value.ToString());
    public override void Write(string? value) { if (value != null) Append(value); }
    public override void WriteLine(string? value) => Append((value ?? "") + Environment.NewLine);
    private void Append(string value)
    {
        lock (_gate)
        {
            _tail.Append(value.AsSpan(Math.Max(0, value.Length - TailCharacters)));
            if (_tail.Length > TailCharacters) _tail.Remove(0, _tail.Length - TailCharacters);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string staging = path + ".staging";
                File.WriteAllText(staging, _tail.ToString(), Encoding.UTF8);
                File.Move(staging, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* A diagnostic sink cannot interrupt rendering or native teardown. */ }
        }
    }
}
