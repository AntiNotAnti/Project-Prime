using System;
using System.IO;

namespace MphRead
{
    public static class ContentFiles
    {
        public static event Action<string, MetaDir, bool>? ModelReading;

        internal static void NotifyModelReading(string name, MetaDir directory, bool firstHunt)
            => ModelReading?.Invoke(name, directory, firstHunt);

        private static Action<string, byte[]>? _recordRead;
        private static Action<string>? _recordModel;

        public static byte[] ReadBytes(string path)
        {
            lock (ContentEnvironment.SyncRoot)
            {
                byte[] bytes = ContentEnvironment.ReadResource(path);
                _recordRead?.Invoke(Path.GetFullPath(path), (byte[])bytes.Clone());
                return bytes;
            }
        }

        public static void RecordModel(string path)
        {
            lock (ContentEnvironment.SyncRoot)
                _recordModel?.Invoke(Path.GetFullPath(path));
        }

        public static IDisposable ObserveReads(Action<string, byte[]> record, Action<string>? recordModel = null)
        {
            lock (ContentEnvironment.SyncRoot)
            {
                ArgumentNullException.ThrowIfNull(record);
                if (_recordRead != null)
                { throw new InvalidOperationException("A content trace is already active."); }
                _recordRead = record;
                _recordModel = recordModel;
                return new ReadTrace();

            }
        }

        private sealed class ReadTrace : IDisposable
        {
            public void Dispose()
            {
                lock (ContentEnvironment.SyncRoot)
                {
                    _recordRead = null;
                    _recordModel = null;

                }
            }
        }
    }
}
