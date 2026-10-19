using System;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class NcxObserverEvidenceTests
{
    internal static int Run()
    {
        try
        {
            int checks = 0;
            void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException("NCX observer evidence: " + label); checks++; }
            var source0 = new ShotKey(8, 9, 0, 10, 11, uint.MaxValue);
            var source1 = new ShotKey(8, 9, 1, 12, 13, 1);
            var state0 = new NcxObserverState(new(8, 9, 0, 10, 11), true);
            var state1 = new NcxObserverState(new(8, 9, 1, 12, 13), true);
            var states = new[] { state0, state1 };
            var sources = new[] { source0, source1 };
            bool Evidence(NcxObserverState[]? observed = null, ShotKey[]? emitted = null, ShotKey[]? offered = null,
                bool enabled = true, bool role = true, int slot = 2, long frames = 60, ulong epoch = 8,
                ushort match = 9, long overflow = 0, int capacity = 10)
                => NcxObserverEvidence.HasEvidence(enabled, role, slot, frames, epoch, match, overflow, capacity,
                    observed ?? states, emitted ?? sources, offered ?? Array.Empty<ShotKey>());
            Check(Evidence(), "alive exact-life participants with committed emissions and no observer offers pass");
            Check(!Evidence(enabled: false) && !Evidence(role: false) && !Evidence(slot: 0)
                && !Evidence(frames: 59), "explicit campaign observer role and minimum frames required");
            Check(!Evidence(epoch: 0) && !Evidence(match: 0), "current stream identity must be valid");
            Check(!Evidence(observed: Array.Empty<NcxObserverState>())
                && !Evidence(observed: new[] { state0 }) && !Evidence(observed: new[] { state1 }),
                "both participant states required");
            Check(!Evidence(emitted: Array.Empty<ShotKey>()) && !Evidence(emitted: new[] { source0 })
                && !Evidence(emitted: new[] { source1 }), "both participants need actual committed source observations");
            Check(!Evidence(observed: new[] { state0 with { Alive = false }, state1 }), "dead state is not live participant evidence");
            foreach (var badLife in new[] { state0.Life with { Epoch = 7 }, state0.Life with { Match = 8 },
                state0.Life with { Slot = 2 }, state0.Life with { Generation = 9 }, state0.Life with { Life = 10 },
                state0.Life with { Generation = 0 }, state0.Life with { Life = 0 } })
                Check(!Evidence(observed: new[] { state0 with { Life = badLife }, state1 }),
                    "state/emission join includes exact full lifecycle and valid identity");
            foreach (var invalid in new[] { source0 with { AuthorityEpoch = 7 }, source0 with { MatchId = 8 },
                source0 with { Generation = 9 }, source0 with { LifeId = 10 }, source0 with { Generation = 0 },
                source0 with { LifeId = 0 }, source0 with { ShotId = 0 } })
                Check(!Evidence(emitted: new[] { invalid, source1 }), "invalid or mismatched emission cannot witness participant");
            var observer = source0 with { ShooterSlot = 2 };
            Check(!Evidence(offered: new[] { observer }), "any observer offered source rejects no-fire evidence");
            Check(!Evidence(emitted: new[] { source0, source1, observer }),
                "late observer emission rejects even after both participant joins succeeded");
            Check(!Evidence(offered: new[] { observer with { AuthorityEpoch = 1, MatchId = 1, LifeId = 0, ShotId = 0 } })
                && !Evidence(emitted: new[] { source0, source1, observer with { AuthorityEpoch = 1, MatchId = 1 } }),
                "observer source activity is forbidden across retained stream/life boundaries, including invalid offers");
            Check(!Evidence(overflow: 1) && !Evidence(overflow: -1), "nonzero overflow fails closed");
            Check(!Evidence(capacity: 0) && !Evidence(capacity: 1), "invalid bound and over-capacity states fail closed");
            Check(!Evidence(emitted: new[] { source0, source1, source1 }, capacity: 2)
                && !Evidence(offered: new[] { source0, source1, source0 }, capacity: 2),
                "committed and offered inputs independently enforce bound");
            Check(Evidence(observed: new[] { state0, state0, state1 }, emitted: new[] { source0, source0, source1 }),
                "duplicate observations cannot replace missing participants but do not invalidate exact joins");
            Check(!Evidence(observed: new[] { state0, state0 }, emitted: new[] { source0, source0 }),
                "duplicates never certify the absent second participant");
            var oldLife = state0 with { Life = state0.Life with { Life = 20 } };
            Check(Evidence(observed: new[] { oldLife, state0, state1 }), "extra unmatched lives do not replace or erase exact observed life");
            Check(states.SequenceEqual(new[] { state0, state1 }) && sources.SequenceEqual(new[] { source0, source1 }),
                "pure report-time join leaves inputs unchanged");
            Console.WriteLine($"PASS NCX observer evidence ({checks} checks): live exact-life participant emissions and no observer fire; no hit/payment/pixel claim.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
