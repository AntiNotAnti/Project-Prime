# CW-12f9 — historical resources for an isolated passive Scene

This source contract covers `ReplayCustomWeaponResources.cs`,
`SceneReplayWeaponPackage.cs` and the explicit inspection paths in
`ReplayPlaybackSession.cs` and `PassiveReplayScene.cs`. Observed local validation
is recorded in [CW-12f9 evidence](evidence/CW-12f9.md). It extends the
[detached world contract](12f8-custom-world-contract.md) without enabling file
playback or custom world Scene capture/restore.

## Worker preparation and ownership

`PreparedReplayWeaponPackage.Prepare(descriptor, store, cancellationToken)`
first validates CWRD2 metadata, then prepares the exact historical package
manifest through its retained archive bytes. It requires one runtime10 package,
the exact definition/context digest, ordinal display-name equality and identical
canonical mechanics. All bounded CPU resources are prepared before publication;
cancellation is checked before store work and before returning ownership.
Failure disposes acquired bindings. There is no stock lookup, fallback,
download, install, grant, gameplay activation or Scene creation.

`Retain()` atomically obtains an independent binding under the preparation
owner's lock; a disposed owner refuses. Disposing preparation releases only
its own leases. `InspectCheckpoint` requires a complete explicit replica15
checkpoint with byte-identical descriptor and exact prepared context. Metadata
consistency alone does not substitute for historical archive verification.

`Scene.BindReplayWeaponPackage` accepts only a fresh private replay Scene,
before room load or ticks, with the exact complete custom checkpoint and no
other custom gameplay/resource owner. It independently retains prepared
resources, applies the decoded model/audio resources and records ownership
before player/room creation and `OnLoad`. Binding performs no package disk IO
or decoding. Cleanup releases the Scene's independent leases; preparation
disposal or uninstall cannot remove bytes still leased by that Scene.
Binding also latches native replay incompatibility for the Scene's lifetime:
stopping/resetting its decoder cannot make its loaded historical resources
eligible for a native world capsule.

## Explicit loaded inspection

`PassiveReplayScene.CreateCustomInspection(checkpoint, prepared, mapHash, size)`
requires a nonzero exact map hash validated against installed data, without a
download. The checkpoint must contain actual accepted native roster-revision
and snapshot coverage. A genuinely recorded empty actor baseline can satisfy
that coverage; synthetic actor presence is not required by the factory. Every
network placeholder exists before room load, allowing later accepted roster
activation without creating a partially registered actor.

The Scene owns private GameState and RNG through its passive services. Resource
binding does not connect live NetSession, consume live input, create offline
weapon or combat authority runtimes, fund ammunition or pay accepted damage.
It does not rewrite the accepted checkpoint merely to attach presentation
resources. Construction failure disposes session/Scene ownership.

`JoinCustomInspection` requires a private passive host and the prepared exact
checkpoint. `StepCustomInspection`/`AdvanceCustomInspection` are explicit accepted
record APIs: the first step may use the initial frame or its successor, then
frames must be contiguous. Every record must retain its actual recording frame;
World records are refused pending the separate Scene restoration contract.
Construction payload tag253 is refused independently of the record kind, so
relabeling a checkpoint as presentation cannot replace the decoder scope.
Accepted native/custom packets are applied through the private owner. Invalid
stepping closes the session and reports corruption rather than continuing a
partial stream. Generic stepping does not silently enter this custom path.

## Qualification boundary

Ordinary file Join and custom world Scene capture/restore remain closed. Bound
resources and a present decoded model do not prove custom rifle/pickup visual
presentation, actual rendering, audio output, Scene checkpoint fidelity or
archive seeks. Genuine accepted contact capture and bots remain open. Test
results, platform coverage and release limits belong to the evidence receipt;
this source document makes no qualification claim.
