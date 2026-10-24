#if MPHREAD_SERVER
using MphRead.Mods.MapGen;
using MphRead.Mods.Render.Materials;

namespace MphRead;

/// <summary>
/// Dedicated-authority counterpart of the custom-client presentation-only video hooks.
/// Headless servers neither decode nor upload videos. Keep shared renderer call sites
/// linkable without changing the client's actual video implementation.
/// </summary>
public partial class Scene
{
    private int _photographicVideoUniform = -1;
    private void DetachMapVideoTexture(int binding) { }
    private void StartMapVideoTexture(int binding, MaterialAssetKey? authored,
        MapDefinition? suppliedDefinition = null, MapVideoTexture? suppliedVideo = null) { }
    private int ResolveMapVideoBinding(int binding, bool sampled = false) => binding;
    private void UpdateMapVideoTextures() { }
    private void DisposeMapVideoTextures() { }
}
#endif
