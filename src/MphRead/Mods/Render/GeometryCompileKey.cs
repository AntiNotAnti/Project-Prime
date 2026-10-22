namespace MphRead.Mods.Render;

// The cache is scoped to one GenerateLists call on one model/current context.
// SourceId therefore identifies the immutable instruction sequence, without
// hashing or equating unrelated models that happen to use the same list index.
internal readonly record struct GeometryCompileKey(int SourceId, int TextureWidth,
    int TextureHeight, bool NormalTexgen, bool RoomLayout, int MatrixCount, int Isolation);
