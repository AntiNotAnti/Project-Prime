# HD weapon charge acceptance correction

The former `charge/67: 0/14 replacement sections submitted` failure was reproduced on real Mac OpenGL: the scripted player had **health 0 and a Free camera** after a bot's Insta-Gib shot. The isolated profile inherited Insta-Gib and an unlimited frame cap. No HD geometry was missing from a living first-person player.

The fixture now establishes native Battle rules before resource setup and again after `AddRoom` reapplies saved match rules. It raises a lowered cannon through normal input before requesting a weapon switch. Direct one-picture-per-step scenarios use 60 Hz timing; the explicit 90/120/240/540 scheduling sweep remains. Fractional packets are compared against the existing native sampled animation pose, with exact canonical-node and animation-state restoration assertions afterward. No visibility, transform, material, muzzle, effect, FOV or charge acceptance assertion was weakened. Production combat and HD submission behavior are unchanged.

Actual execution, macOS arm64, `.NET 10.0.401`, OpenGL `2.1 Metal - 91.7`, Samus in `MP1 SANCTORUS`:

- Full original hostile profile: PASS, exit 0, **3,090 submitted frames, 990 high-refresh pictures, 687 effect frames**. Includes charging/missile/zoom/material/FOV/suit stages and the fractional scheduling sweep.
- Pose-only with the same profile: PASS, exit 0; native idle pose and native/source screenshots exported.
- Original profile remained unchanged: Insta-Gib on/unlimited; settings SHA256 `9cc4acbf4806b5470bd74c5a92a3f9b6f2b065bb3641b8dcbdead87f4d0eac55`.
- Windows SSH authentication failed; the existing Parsec session displayed Windows but did not reliably accept focus/keyboard input. The authorized Mac fallback was used without disturbing the Windows session.

Commands (existing private extracted assets and copied replacement pack required):

```sh
PROJECT_PRIME_USER_DATA=/tmp/upstream-test-results/pr06-profile /Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -viewmodelacceptancecheck 'MP1 SANCTORUS' -output /tmp/upstream-test-results/hd-final-full
PROJECT_PRIME_USER_DATA=/tmp/upstream-test-results/pr06-profile /Users/jarrett/.dotnet/dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -viewmodelacceptancecheck 'MP1 SANCTORUS' -poseonly -output /tmp/upstream-test-results/hd-final-pose
```

Receipts are preserved locally in those directories and their `.log` files, with a copy under ignored `artifacts/upstream-improvements/`. Proprietary assets are not committed. Rates describe simulated scheduling on a real graphics context, not physical display measurements or a multi-Hunter/device performance certification. Hosted CI and review are unverified and were not awaited, as requested.

This supersedes the HD fixture failure recorded in [PR-08](pr08.md); it does not retroactively change that historical run's result.
