using System;

namespace MphRead;

public partial class Scene
{
    private int _renderResourcesReleased;

    /// <summary>Terminal scene teardown. Pass true only on the graphics owner
    /// thread before destroying a healthy/current context. After device/context
    /// loss pass false: native handles are forgotten and all managed leases end.</summary>
    public void ReleaseRenderResources(bool canReleaseNativeResources)
        => Mods.Render.RenderResourceLifetime.Release(ref _renderResourcesReleased,
            canReleaseNativeResources && !Mods.Headless.Active,
            ReleaseNativeRenderResources, ForgetRenderResources);

    /// <summary>Ends scene logic and renderer ownership together. Nested replay
    /// scenes inherit loss-safe cleanup when the parent context is unavailable.</summary>
    public void CleanupAndReleaseRenderResources(bool canReleaseNativeResources)
        => Mods.Render.RenderResourceLifetime.WithNativeReleaseEligibility(canReleaseNativeResources, () =>
        {
            try { DoCleanup(); }
            finally { ReleaseRenderResources(canReleaseNativeResources); }
        });

    private void ReleaseModelLeases(bool canReleaseNativeResources)
        => Mods.Render.SharedModelResources.ReleaseAll(_modelLeases, canReleaseNativeResources);

    private void ForgetRenderResources()
    {
        // TextureAssetManager owns logical bindings and invokes ReleaseTexture.
        // Native textures have already been deleted, or belong to a lost context.
        // Drop this registry first so manager disposal cannot call the facade.
        _ownedTextures?.Clear();
        ReleaseModelLeases(canReleaseNativeResources: false);
        Mods.Render.Characters.CharacterModelRuntime.Release(this, canReleaseNativeResources: false);
        _cosmeticTextureAssets?.Dispose();
        _cosmeticTextureAssets = null;
        _texPalMap?.Clear();
        _textureSources?.Clear();
        _streamingTextureVersions?.Clear();
        _streamingTextureQueue?.Clear();
        _streamingTextureDecodes?.Clear();
        _mipmappedTextures?.Clear();
        _appliedTextureSampling?.Clear();
        _modernTextureSampling?.Clear();
        _worldMaterialTextureBytes?.Clear();
        _worldMaterialResidentBytes = 0;
        _flatColors?.Clear();
        _cosmeticTextures?.Clear();
        _materialMaps?.Clear();
        _replicaModels?.Clear();
        if (_previewItems != null) ReleasePreviewItems();
        _freeRenderItems?.Clear();
        _frameTransientTextures?.Clear();
#if MPHREAD_SHELL
        _editorTextures?.Clear();
        _editorMeshes?.Clear(static _ => { });
        _editorGrid = null;
#endif
        _replayOutputFramebuffer = _replayOutputTexture = 0;
        _replayOutputSize = default;
        _celFrameBuffer = _celFrameBufferColor = _frameBuffer = _renderBuffer = 0;
        _celSourceFrameBuffer = _celSourceFrameBufferColor = _celSourceFrameBufferDepth = 0;
        _screenTexture = _celTexture = _depthTexture = 0;
        _shaderProgramId = _rttShaderProgramId = _shiftShaderProgramId = _celShaderProgramId = 0;
        _playerOutlineFramebuffer = _playerOutlineTexture = _playerOutlineProgram = 0;
        _playerOutlineSize = default;
        _playerOutlineDepth = -1;
        _graphicsOutputFramebuffer = _graphicsHdrFramebuffer = 0;
        _graphicsOutputTexture = _graphicsHdrTexture = _graphicsHistoryTexture = 0;
        _graphicsProgram = _graphicsToneMapProgram = 0;
        _graphicsOutputReady = _graphicsHistoryValid = false;
        _graphicsOutputSize = _graphicsHistorySize = default;
        _pbrFramebuffer = _pbrOwnedDepthTexture = 0;
        _pbrDepthTexture = _pbrAlbedoTexture = _pbrNormalTexture = _pbrMaterialTexture = _pbrProgram = 0;
        _pbrReady = _pbrIndependentDepth = _pbrResolvedToScene = false;
        _pbrSize = default;
        _shadowFramebuffer = _shadowDepthTexture = _shadowColorTexture = _shadowTargetSize = 0;
        _shadowReady = false;
        if (Services?.IsReplica != true) Read.ClearCache();
        Mods.MapGen.MapRuntimeUsage.Release(this);
        try { Mods.Render.Materials.MaterialInventory.SaveObserved(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Mods.DebugLog.Line("render", "Material inventory save failed: " + ex.Message); }
    }
}
