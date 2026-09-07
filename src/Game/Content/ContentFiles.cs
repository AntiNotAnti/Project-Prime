using System;
using System.IO;

namespace MphRead
{
    public static class ContentFiles
    {
        public static event Action<string, MetaDir, bool>? ModelReading;

        internal static void NotifyModelReading(string name, MetaDir directory, bool firstHunt)
            => ModelReading?.Invoke(name, directory, firstHunt);

        [ThreadStatic] private static Action<string, byte[]>? _recordRead;
        [ThreadStatic] private static Action<string>? _recordModel;

        public static byte[] ReadBytes(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            _recordRead?.Invoke(Path.GetFullPath(path), bytes);
            return bytes;
        }

        public static void RecordModel(string path) => _recordModel?.Invoke(Path.GetFullPath(path));

        public static IDisposable ObserveReads(Action<string, byte[]> record, Action<string>? recordModel = null)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (_recordRead != null) { throw new InvalidOperationException("A content trace is already active."); }
            _recordRead = record;
            _recordModel = recordModel;
            return new ReadTrace();
        }

        private sealed class ReadTrace : IDisposable
        {
            public void Dispose()
            {
                _recordRead = null;
                _recordModel = null;
            }
        }
    }
}
