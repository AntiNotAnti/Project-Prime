using System;
using System.IO;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Renderer-free coverage of the actual replay lifecycle checkpoint primitive.
// This does not prove packet-decoder baseline parity or authenticated UDP behavior.
internal static class LifecycleCheckpointTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Lifecycle checkpoint: " + label);
        _checks++;
    }

    public static int Run()
    {
        _checks = 0;
        foreach (var presentation in new[] { NetworkPlayerState.Dead,
            NetworkPlayerState.WaitingToSpawn, NetworkPlayerState.Spectating })
        {
            var source = new NetLifecycleTracker();
            source.SetOccupant(12);
            Check(source.Accept(12, ushort.MaxValue, NetworkPlayerState.Alive, out _)
                == LifecycleRejection.None, "initial life accepted");
            source.Accept(12, ushort.MaxValue, NetworkPlayerState.Dead, out _);
            source.Accept(12, ushort.MaxValue, presentation, out _);
            var checkpoint = source.Capture();
            var restored = new NetLifecycleTracker();
            restored.Restore(checkpoint);
            Check(restored.Capture() == checkpoint, "checkpoint retains exact identity and tombstone through " + presentation);
            Check(restored.Accept(12, ushort.MaxValue, NetworkPlayerState.Alive, out bool fresh)
                == LifecycleRejection.InvalidResurrection && !fresh,
                "seek cannot resurrect dead life through " + presentation);
            Check(restored.Capture() == checkpoint, "rejected resurrection leaves checkpoint unchanged");
            Check(restored.Accept(12, 1, NetworkPlayerState.Alive, out fresh)
                == LifecycleRejection.None && fresh, "wrapped new life clears restored tombstone");
            var newLife = restored.Capture();
            Check(restored.Accept(12, ushort.MaxValue, NetworkPlayerState.Dead, out fresh)
                == LifecycleRejection.OldLife && !fresh && restored.Capture() == newLife,
                "late pre-seek death cannot cross wrapped life boundary");
            restored.SetOccupant(13);
            Check(restored.Accept(12, 2, NetworkPlayerState.Dead, out fresh)
                == LifecycleRejection.WrongGeneration && !fresh,
                "reconnected occupant rejects old generation death");
            Check(restored.Accept(13, 1, NetworkPlayerState.Alive, out fresh)
                == LifecycleRejection.None && fresh, "new occupant starts clean life");
        }

        var tracker = new NetLifecycleTracker();
        tracker.SetOccupant(4);
        tracker.Accept(4, 8, NetworkPlayerState.Alive, out _);
        var before = tracker.Capture();
        var invalid = new[] {
            new LifecycleSnapshot(0, 8, NetworkPlayerState.Alive, false),
            new LifecycleSnapshot(4, 8, NetworkPlayerState.Empty, false),
            new LifecycleSnapshot(4, 0, NetworkPlayerState.Alive, false),
            new LifecycleSnapshot(4, 0, NetworkPlayerState.Spectating, true),
            new LifecycleSnapshot(4, 8, NetworkPlayerState.Dead, false),
            new LifecycleSnapshot(4, 8, NetworkPlayerState.Alive, true),
            new LifecycleSnapshot(4, 8, (NetworkPlayerState)255, false)
        };
        foreach (var snapshot in invalid)
        {
            bool refused = false;
            try { tracker.Restore(snapshot); }
            catch (InvalidDataException) { refused = true; }
            Check(refused && tracker.Capture() == before,
                "malformed checkpoint rejected without partially changing active life: " + snapshot);
        }
        foreach (var snapshot in new[] { default(LifecycleSnapshot), before,
            new LifecycleSnapshot(4, 0, NetworkPlayerState.WaitingToSpawn, false),
            new LifecycleSnapshot(4, 0, NetworkPlayerState.Spectating, false) })
        {
            tracker.Restore(snapshot);
            Check(tracker.Capture() == snapshot, "valid empty/waiting/spectator/alive checkpoint round-trips");
        }
        Console.WriteLine($"LIFECYCLE CHECKPOINT PASS {_checks} checks");
        return 0;
    }
}
