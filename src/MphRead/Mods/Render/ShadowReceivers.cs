using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private WorldShaderProgram? _shadowReceiverProgram, _shadowReceiverPrevious;
    private bool _shadowReceiverScope, _shadowReceiverColorPass, _shadowReceiverBindings;
    private bool _shadowReceiverRefused;
    private Mods.ShadowQuality _shadowReceiverAttemptQuality;
    private Vector2i _shadowReceiverAttemptSize;
    private int _shadowReceiverActiveUnit, _shadowReceiverBinding;

    // The late scene contains viewmodels/effects/HUD with other depth/projection
    // ownership. Failure of the optional inline shader falls back to unshadowed
    // native rendering, never to multiplying that unrelated final color.
    private static bool InlineShadowReceiverPolicy => true;

    private void InitShadowReceiverUniforms()
    {
        _shaderLocations.ReceiveWorldShadow = GL.GetUniformLocation(_shaderProgramId, "receive_world_shadow");
        _shaderLocations.WorldShadowSampler = GL.GetUniformLocation(_shaderProgramId, "world_shadow_tex");
        _shaderLocations.WorldShadowMatrix = GL.GetUniformLocation(_shaderProgramId, "world_shadow_matrix");
        _shaderLocations.WorldShadowTexel = GL.GetUniformLocation(_shaderProgramId, "world_shadow_texel");
        _shaderLocations.WorldShadowLight = GL.GetUniformLocation(_shaderProgramId, "world_shadow_light");
    }

    private void BeginShadowReceivers()
    {
        _shadowReceiverScope = _shadowReceiverColorPass = false;
        if (Mods.RenderOptions.Shadows == Mods.ShadowQuality.Off)
        {
            _shadowReceiverRefused = false;
            return;
        }
        if (!ShadowMapReady) return;
        if (_shadowReceiverRefused && (_shadowReceiverAttemptQuality != Mods.RenderOptions.Shadows
            || _shadowReceiverAttemptSize != _targetSize)) _shadowReceiverRefused = false;
        if (_shadowReceiverRefused) return;
        _shadowReceiverAttemptQuality = Mods.RenderOptions.Shadows;
        _shadowReceiverAttemptSize = _targetSize;
        // Reuse the scene-owned bundle instead of allocating a snapshot record
        // every shadowed picture. These are the only ordinary world programs.
        _fullWorldProgram ??= SaveWorldProgram();
        _shadowReceiverPrevious = _nativeWorldProgram?.Program == _shaderProgramId ? _nativeWorldProgram
            : _plainWorldProgram?.Program == _shaderProgramId ? _plainWorldProgram
            : _unlitWorldProgram?.Program == _shaderProgramId ? _unlitWorldProgram : _fullWorldProgram;
        try
        {
            if (GL.GetInteger(GetPName.MaxTextureImageUnits) < 5)
                throw new NotSupportedException("five fragment texture units are required");
            if (_shadowReceiverProgram == null)
            {
                _shadowReceiverProgram = PrewarmWorldShaderVariant(Shaders.ShadowFragmentShader);
                if (_shadowReceiverProgram == null) throw new InvalidOperationException("receiver program did not link");
            }
            UseWorldProgram(_shadowReceiverProgram);
            if (_shaderLocations.ReceiveWorldShadow < 0 || _shaderLocations.WorldShadowMatrix < 0
                || _shaderLocations.WorldShadowSampler < 0 || _shaderLocations.WorldShadowTexel < 0
                || _shaderLocations.WorldShadowLight < 0)
                throw new InvalidOperationException("receiver uniform bundle incomplete");
            ResetCosmeticUniforms();
            GL.UniformMatrix4(_shaderLocations.ViewMatrix, false, ref _viewMatrix);
            SetMaterialCamera(ref _viewMatrix);
            GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, false, ref _perspectiveMatrix);
            UploadWorldFrameUniforms();
            // WorldSetup may already have selected the native pause-map camera.
            // A program switch must preserve its projection and material eye.
            SetPauseMenuUniforms();
            Matrix4 light = _shadowView * _shadowProjection;
            GL.UniformMatrix4(_shaderLocations.WorldShadowMatrix, false, ref light);
            GL.Uniform2(_shaderLocations.WorldShadowTexel, 1f / _shadowTargetSize, 1f / _shadowTargetSize);
            Vector3 direction = _light1Vector;
            if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)
                || !float.IsFinite(direction.Z) || direction.LengthSquared < .0001f)
                direction = new Vector3(-.45f,-.82f,-.35f);
            GL.Uniform3(_shaderLocations.WorldShadowLight, direction.Normalized());
            GL.Uniform1(_shaderLocations.WorldShadowSampler, 4);
            GL.Uniform1(_shaderLocations.ReceiveWorldShadow, 0);
            _shadowReceiverActiveUnit = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture4);
            _shadowReceiverBinding = GL.GetInteger(GetPName.TextureBinding2D);
            _shadowReceiverBindings = true;
            GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTexture);
            GL.ActiveTexture((TextureUnit)_shadowReceiverActiveUnit);
            _shadowReceiverScope = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _shadowReceiverRefused = true;
            Console.WriteLine("[render] effective directional shadows unavailable; native unshadowed fallback: " + ex.Message);
            EndShadowReceivers();
            if (_shadowReceiverProgram != null)
            {
                int program = _shadowReceiverProgram.Program;
                DeleteProgram(ref program);
                _shadowReceiverProgram = null;
            }
        }
    }

    private void SetShadowReceiver(RenderItem item)
    {
        if (!_shadowReceiverScope) return;
        bool receive = _shadowReceiverColorPass && !GameState.MenuPause && !_drawingPlayerOutlineMask
            && Mods.Render.ShadowReceiverPolicy.Eligible(item);
        GL.Uniform1(_shaderLocations.ReceiveWorldShadow, receive ? 1 : 0);
        if (receive)
        {
            // A custom source-GX program can use this unit between ordinary
            // world packets. Re-establish our reserved binding before receiving.
            GL.ActiveTexture(TextureUnit.Texture4);
            GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
        }
    }

    private void EndShadowReceivers()
    {
        _shadowReceiverScope = _shadowReceiverColorPass = false;
        if (_shadowReceiverBindings)
        {
            GL.ActiveTexture(TextureUnit.Texture4);
            GL.BindTexture(TextureTarget.Texture2D, _shadowReceiverBinding);
            GL.ActiveTexture((TextureUnit)_shadowReceiverActiveUnit);
            _shadowReceiverBindings = false;
        }
        if (_shadowReceiverPrevious != null)
        {
            UseWorldProgram(_shadowReceiverPrevious);
            _shadowReceiverPrevious = null;
            ResetCosmeticUniforms();
        }
    }

    private void ReleaseShadowReceivers()
    {
        EndShadowReceivers();
        if (_shadowReceiverProgram != null)
        {
            int program = _shadowReceiverProgram.Program;
            DeleteProgram(ref program);
            _shadowReceiverProgram = null;
        }
        _shadowReceiverRefused = false;
    }
}
