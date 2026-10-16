# PORT-11a: Echoes MREA world render geometry parser

This is a standalone, asset-free reader for the **visual** layer of Echoes
multiplayer MREA files. It adapts the MIT-licensed geometry layout from
Second Hunt, decoding indexed GX triangle lists, strips and fans using the
original material vertex descriptors.

The implementation is bounded by model, material, surface, position and
triangle budgets, with explicit malformed-input rejection. No textures,
meshes, maps or Nintendo assets are committed or uploaded.

The source parser is not yet used by the game. The subsequent integration
will keep collision geometry authoritative, add original world triangles to
the renderer as **visual-only**, and gate the result behind a local opt-in
until real-media and device testing succeeds.

Test: `dotnet run --project tools/external-import-check -c Release`.
