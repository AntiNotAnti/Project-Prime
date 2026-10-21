# CW-12f14 — actual two-client contacts and causal replay direction

Initial development base: `e4034bd42e47c78a3e0fe6db63817d0d0c78a2d9`.
Integrated v5 local qualification on main base `242be3d57` is recorded in the
[evidence receipt](evidence/CW-12f14.md), with historical attempts preserved.

A contact's derived direction may use its retained construction flight or a
launch accepted in the same or an earlier complete recording frame. A launch
accepted in a later recording frame cannot retroactively orient that contact.
This keeps first linear consumption and cold reconstruction consistent without
changing CWRS, the replay fact schema, native packet shapes or accepted damage.
Recording-frame values already survive detached and durable checkpoints.

Custom contact facts retain authority outcomes and retire recorded flights.
They never subtract HP or run hit resolution during replay. Accepted native
snapshots remain the sole HP/death source; head/impact reactions are derived
presentation, fenced by victim generation and life.

World restoration also rebinds the replay service's derived presentation lifecycle
cache from the current decoder and the restored generation fence. An unprojected
construction still has generation0 and stays unknown. A completed world with a
matching recorded generation already projected that exact tuple. Missing players
remain unknown: their decoder entry is removed only on roster generation change,
so a future occupant requires the same reset. This prevents a fresh owner from
resetting restored body/camera/gun history on its first suffix step. The explicit
graph contract remains unchanged.

File inspection validates semantic marker254 through the established marker
codec and treats it as metadata. It does not inject the marker into the gameplay
decoder or increment ignored-packet counters. The test bundle encodes actual
accepted Marker values using that same codec; non-Event payloads remain exact
callback bytes, with their original acceptance order/frame/tick preserved.
Private replay single-particle submission uses a separate 200-entry prewarmed
picture pool. Accepted contact/death sparks use that bounded scratch while the
original serialized pool and count remain unchanged. Single-particle models
absent from the saved world use a private Scene-owned draw-model cache, keyed
by asset name/FirstHunt, rather than enlarging canonical `ReplicaModels` during
a picture. The twelve declared `SingleType` values bound this eligible sprite
asset set. Existing canonical models are reused when present. The ordinary
gameplay pool and all checkpoint fields stay intact.

The current main integration uses protocol84, retaining historical protocol83
archives and checkpoint layouts. Its Parkour-pose compatibility uses current
native replica16/custom replica17 while preserving native14/custom15 historical
shapes. Those compatibility changes come from merged main, not a new f14 custom
schema. CWRD/CWRS/contact fact encoding and accepted damage are unchanged by
this causal-direction, lifecycle and draw-resource correction. Format7 requires
its declared protocol and embedded replica construction to agree.

The new acceptance fixture uses two separate ordinary client processes and a
separate actual dedicated server. The clients use normal connection, package
preparation, Ready, world admission, pickup, selection and keyboard fire/
respawn paths. Declared owner-reported pose/aim setup occurs before the client's
simulation/input phase. No server snapshot callback or post-simulation setup may
repair hit geometry, and no synthetic damage, packet, grant or contact is used.
This four-shot fixture uses a six-round magazine and does not exercise reload;
the existing ordinary-client GL regression covers actual reload separately.
IPC and captured fact counts/bytes are bounded; each helper owns its processes,
GL/audio lifetime, profile and package resources.

Observed validation and limitations belong in the corresponding evidence file.
Ordinary custom recording/theatre/killcam/export admission stays closed until
its remaining acceptance gates pass. Android remains explicitly deferred.