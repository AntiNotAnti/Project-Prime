# Project Prime AMHE1 unresolved findings

| Finding | Area | Missing evidence | Next experiment | Status |
|---|---|---|---|---|
| FID-EVIDENCE-001 | Provenance | Private acquisition record; only USA rev-1 anchor/tree digest is frozen | Preserve operator record beside reference | BlockedByEvidence |
| FID-EVIDENCE-002 | Dynamic trials | Emulator/hardware/capture configuration and repeatability | Freeze and repeat bounded idle trial | BlockedByEvidence |
| FID-SIM-001 | Tick mapping | AMHE1 frame/timer to 60 Hz normalization | Correlate static timer with repeated boundary trial | BlockedByEvidence |
| FID-SCOPE-001 | Room/mode matrix | Dynamic AMHE1 room/mode restrictions; the Project scope is frozen as IDs 93-118 and 12 modes | Capture AMHE1 selection behavior if parity restrictions are later required | BlockedByEvidence |
| FID-HUNTER-001 | Guardian/Psycho Bit | Project Prime now exposes Guardian as an official extension; exact retail enemy/player semantics remain unavailable | Keep extension behavior bounded and documented; do not claim AMHE1 retail parity | Intentional Project Prime extension |
| FID-GATE-001 | Physical/WAN/soak | Entry gates were waived | Run original gates against F9 candidate | BlockedByEvidence |
| FID-GATE-002 | PostgreSQL | Four integration cases skipped | Run isolated PostgreSQL suite | BlockedByEvidence |
| FID-GATE-003 | Reference mount | Local tree remains writable | Use read-only mount for evidence runs | BlockedByEvidence |

G6 remains rolled back. Local source, headless, imaging, and package evidence never
substitutes for deployed, WAN, rendered, or physical-device proof. The 2026-09-09
waiver approves retaining these terminal limitations and prevents them from blocking
administrative closure; it does not change any finding to accepted or fixed.
