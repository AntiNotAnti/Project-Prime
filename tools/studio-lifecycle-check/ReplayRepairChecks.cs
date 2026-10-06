using MphRead.Mods.StudioReplay;

internal static partial class Program
{
    private static void CheckCameraSourceRetry(StudioReplayPlayer player, string source, byte[] original,
        StudioReplayWorldSnapshot world, string root)
    {
        string sidecar = source + ".camera";
        byte[] durable = File.ReadAllBytes(sidecar);
        uint frame = 17;
        var accepted = new StudioReplayCameraKey(frame, new(11, 12, 13), System.Numerics.Quaternion.Identity);
        string hidden = source + ".retry-missing";
        File.Move(source, hidden);
        try
        {
            Check(player.PutCameraKey(accepted), "camera edit is accepted while its durable source is temporarily missing");
            bool missing = false;
            try { player.FlushCameraEditsAsync().GetAwaiter().GetResult(); }
            catch (IOException) { missing = true; }
            Check(missing && player.CameraEditsPending && player.CameraKeys.Any(key => key == accepted)
                && durable.SequenceEqual(File.ReadAllBytes(sidecar)) && SameReplayWorld(world, player.Snapshot()),
                "missing-source camera save retains accepted edits, prior durable sidecar and exact private world");

            byte[] alternate = Directory.GetFiles(root, "*.ppdemo").Where(path => path != source)
                .Select(File.ReadAllBytes).First(bytes => bytes.Length > 200_000 && !bytes.SequenceEqual(original));
            File.WriteAllBytes(source, alternate);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(2));
            bool replaced = false;
            try { player.FlushCameraEditsAsync().GetAwaiter().GetResult(); }
            catch (InvalidDataException) { replaced = true; }
            Check(replaced && player.CameraEditsPending && durable.SequenceEqual(File.ReadAllBytes(sidecar))
                && SameReplayWorld(world, player.Snapshot()),
                "a different valid recording cannot silently adopt accepted camera edits or replace the durable sidecar");

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); bool canceled = false;
                try { player.FlushCameraEditsAsync(cancellation.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && player.CameraEditsPending && player.CameraKeys.Any(key => key == accepted),
                    "canceling explicit camera retry preserves its accepted immutable edit");
            }

            File.WriteAllBytes(source, original);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(4));
            player.FlushCameraEditsAsync().GetAwaiter().GetResult();
            Check(!player.CameraEditsPending && player.CameraSaveError == null
                && !durable.SequenceEqual(File.ReadAllBytes(sidecar))
                && original.SequenceEqual(File.ReadAllBytes(source)) && SameReplayWorld(world, player.Snapshot()),
                "explicit retry verifies restored identical bytes at a changed timestamp and durably commits accepted camera edits");
            using var reopened = new StudioReplayPlayer(source, Path.Combine(root, "retry-camera-" + Path.GetFileNameWithoutExtension(source)));
            reopened.OnGraphicsInitialize(256, 192); WaitReplayReady(reopened);
            Check(reopened.CameraKeys.Any(key => key == accepted),
                "fresh replay preparation reads the exact accepted camera key from the durable retry sidecar");
        }
        finally
        {
            File.WriteAllBytes(source, original);
            File.Delete(hidden);
        }
    }
}
