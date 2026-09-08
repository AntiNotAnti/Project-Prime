# Match feature ownership (MI7 / A3)

`Scene.Features` is an immutable `MatchFeatureSet`, supplied at construction.
Nested bugfix and cheat records contain only immutable scalar options. Defaults
preserve the former code values. No server scene reads client settings or ambient
state. A client scene explicitly captures `ClientMatchFeatures.Capture()` once;
subsequent settings changes affect only future scenes.

Reader classification (source: all Features/Bugfixes/Cheats references in Game):

| Options | Actual readers / reason for scene ownership |
| --- | --- |
| AllowInvalidTeams, HalfSecondAlarm | MatchFlow: team validity/end condition and countdown scheduling |
| FullBoostCharge, UnlimitedJumps | PlayerInput: charge and movement rules |
| BoostOpensDoors, WalkThroughWalls | PlayerCollision: doors and collision policy |
| QuadrupleDamage | BeamProjectileEntity.Spawn: damage calculation |
| UnlockAllDoors | DoorEntity: initial lock state |
| FreeWeaponSelect | PlayerEntity: developer weapon selection |
| NoDoubleEnemyDeath | ForceFieldLockEntity: death processing guard |
| MaxRoomDetail | SceneSetup: loaded room node detail |
| MaxPlayerDetail | PlayerEntity/PlayerProcess: effect selection |
| NoIdleSway, DelayedIdleSway, FixedWeapon, FixedCrosshair | PlayerEntity/PlayerProcess/PlayerInput: shared weapon, aim, reticle state; captured even when cosmetic |
| SmoothCamSeqHandoff, BetterCamSeqNodeRef | CamSeqEntity/CameraSequence: sequence transition/node behavior |
| NoStrayRespawnText, CorrectBountySfx | PlayerProcess/OctolithFlagEntity: shared text/audio event decisions |

The old mutable preference types now live in Client/Settings/Features.cs, outside
the Game assembly. HUD opacity, ProHud, HUD/target sway, map centering and spatial
audio are client presentation preferences. The remaining campaign switches
(NoRepeatEncounters, AlternateHunters1P, NoRandomEncounters,
ContinueFromCurrentRoom, SkipPlanetIntros, StartWithAllUpgrades,
StartWithAllOctoliths, AlwaysFightGorea2, NoSlenchRollTimerUnderflow) have no Game
readers: they remain legacy client menu/settings compatibility fields and are not
copied into a match. FixedWeapon/FixedCrosshair capture the effective ProHud value.

FeatureIsolationTests compares strict scene A with unchanged default scene B and
an independent control through MatchFlow, and verifies that changing client cheat
preferences affects an explicit new snapshot only, never a default server scene.
This is focused in-process evidence, not a full concurrent-server isolation gate.
