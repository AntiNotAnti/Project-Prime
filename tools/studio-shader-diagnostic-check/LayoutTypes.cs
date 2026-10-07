namespace MphRead.Mods.Render;

// Shape-only dependencies of the actual generated layout table; no engine or GPU implementation.
internal enum ModernProgramKind { World, DeferredPbr, DeferredPbrMrt, PostProcess }
internal readonly record struct ModernUniformLayout(string Name, int Offset, int Components,
    int Count, int Stride, bool Integer);
internal sealed record ModernShaderLayout(int Size, int FlipOffset,
    ModernUniformLayout[] Uniforms, string[] Samplers);
