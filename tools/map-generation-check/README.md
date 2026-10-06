# Canonical map generation ownership

`dotnet run --project tools/map-generation-check -c Release`

This asset-free fixture creates its own texture and two real immutable packages,
compiles their runtime bytes through the canonical map compiler, and starts a
separate game process through `SceneSetup.SetUpRoom` that admits a real
`RoomEntity` scene and decodes its model, collision, entities and nodes. Its spawn
placeholder uses the fixture's own generated model. It also covers an actual
preparation lease.

While that process holds the room, direct scheduler installation, both map packer
overloads, the default `-mapbuild` command and `GenerateMissing` must reject/defer
the newer same-name version. Every output and manifest byte must remain identical.
After normal release, the retry must match the exact source fingerprint and every
byte of a privately compiled expected output set. Further cases cover actual
process termination, runtime directory aliases, mixed destination rejection,
independent private builds and the generate-before-reader prewarm handoff.

A writer holding the room while collision bytes are deliberately poisoned must
defer scene setup before any decoder runs. Ten thousand repeated same-source
scene registrations compete with ten thousand independent writer attempts;
every writer must remain excluded. Failed admission to another runtime root or
host namespace must preserve the scene's previous reader lease.

The real scene uses the normal headless game policy; this gate does not claim GPU
rendering or native-window coverage. No retail assets, socket or UI is required.
