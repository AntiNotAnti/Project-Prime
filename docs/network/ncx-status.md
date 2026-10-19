# Competitive netcode implementation ledger

The user selected the attached [Competitive Netcode Overhaul plan](ncx-implementation-plan.md)
on 2026-10-19 after clarifying a conflicting Custom Weapons title. That selection governs this
work. Custom Weapons CW-00–03 already on main are an independent implementation train.

Implement NCX-00 through NCX-13 in order, one independently reviewable PR per slice. Commit
each completed slice and merge after its local acceptance, without waiting for review or CI,
as explicitly requested. Queued, running, cancelled and unexecuted checks never count as PASS.
Refresh main and list overlapping PRs before every slice. Preserve failed evidence.

| Slice | Deliverable | Status |
| --- | --- | --- |
| NCX-00 | Exact-source baseline, ownership and capability audit | Complete; 23 reference routes pass, characterized fidelity failure retained |
| NCX-01 | Matched measurement, shot timelines, independent hit and rendered visual rig | Next; comparator binary/visual/A/A gates unrun |
| NCX-02 | Canonical causal payment identity and definitive terminal results | Pending NCX-01 |
| NCX-03 | Bounded shadow fast-decision layer | Pending NCX-02 |
| NCX-04 | Certified narrow-family fast settlement | Pending NCX-03 evidence |
| NCX-05 | Independently certified family extensions and explicit fallbacks | Pending NCX-04 |
| NCX-06 | Prediction retirement and confirmation presentation | Pending NCX-05 |
| NCX-07 | Same-shot victim and observer confirmed-impact fidelity | Pending NCX-06 |
| NCX-08 | Continuous fire, multi-entity and death boundaries | Pending NCX-07 |
| NCX-09 | Demonstrated transport/recovery faults and measured improvements | Pending NCX-08 |
| NCX-10 | Historical collision accuracy and rewind fairness | Pending NCX-09 |
| NCX-11 | Qualified combat CPU/memory/packet optimization | Pending NCX-10 |
| NCX-12 | Matched Fruity and cross-platform release matrix | Pending NCX-11 |
| NCX-13 | Controlled rollout and tested rollback | Pending NCX-12 |

Owner-reported movement, fixed 60 Hz simulation and current Native/Momentum rules remain
unchanged. The dedicated server owns combat and match consequences. No new flag is enabled
by default. A claim, source admission, native emission, committed payment, confirmed death,
cosmetic packet, draw submission and visible pixel are different facts.

Local raw artifacts live under `artifacts/ncx/NCX-NN/`; committed manifests live under
`tools/nettest/baselines/`. Never commit game assets, player/account identifiers, credentials
or private packet/video captures. Release and superiority claims require the full evidence
defined in the plan, including rendered pixels, paired statistics and physical devices.
