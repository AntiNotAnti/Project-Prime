using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Camera-local directional shadow map. It replays the already submitted
    /// opaque world geometry into a light-space depth texture. No entity is
    /// processed and no simulation state advances during this pass.
    /// </summary>
    public partial class Scene
    {
        private int _shadowFramebuffer;
        private int _shadowDepthTexture;
        private int _shadowColorTexture;
        private int _shadowTargetSize;
        private bool _shadowRefused;
        private bool _shadowReady;
        private Matrix4 _shadowView = Matrix4.Identity;
        private Matrix4 _shadowProjection = Matrix4.Identity;
        private long _retainedDirectShadowDraws;
        private long _retainedCompatibilityShadowDraws;

        internal long RetainedDirectShadowDraws => _retainedDirectShadowDraws;
        internal long RetainedCompatibilityShadowDraws =>
            _retainedCompatibilityShadowDraws;

        private bool ShadowMapReady => _shadowReady && _shadowDepthTexture != 0;

        private int WantedShadowSize => Mods.RenderOptions.Shadows switch
        {
            Mods.ShadowQuality.Low => 1024,
            Mods.ShadowQuality.High => 2048,
            Mods.ShadowQuality.Ultra => 4096,
            _ => 0
        };

        private void RenderShadowMap()
        {
            _shadowReady = false;
            if (Mods.RenderOptions.Shadows == Mods.ShadowQuality.Off || _shadowRefused
                || _nonDecalItems.Count == 0)
            {
                return;
            }

            try
            {
                EnsureShadowTarget();
                if (_shadowFramebuffer == 0 || _shadowTargetSize <= 0) return;

                var camera = Mods.Render.GraphicsEnvironmentMath.DirectionalShadowCamera(
                    new(_cameraPosition.X,_cameraPosition.Y,_cameraPosition.Z),
                    new(_cameraFacing.X,_cameraFacing.Y,_cameraFacing.Z),
                    new(_light1Vector.X,_light1Vector.Y,_light1Vector.Z),_shadowTargetSize,
                    Mods.RenderOptions.Shadows == Mods.ShadowQuality.Low);
                static Matrix4 RuntimeMatrix(System.Numerics.Matrix4x4 m) => new(
                    m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
                    m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
                _shadowView = RuntimeMatrix(camera.View); _shadowProjection = RuntimeMatrix(camera.Projection);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
                GL.Viewport(0, 0, _shadowTargetSize, _shadowTargetSize);
                GL.ColorMask(false, false, false, false);
                GL.DepthMask(true);
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Less);
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.StencilTest);
                GL.Enable(EnableCap.AlphaTest);
                GL.AlphaFunc(AlphaFunction.Equal, 1f);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                GL.UseProgram(_shaderProgramId);
                GL.UniformMatrix4(_shaderLocations.ViewMatrix, false, ref _shadowView);
                GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, false, ref _shadowProjection);
                GL.Uniform1(_shaderLocations.UseFog, 0);
                GL.Uniform1(_shaderLocations.CelBands, 0);

#if !MPHREAD_SERVER
                bool directShadow = Mods.Render.ModernGraphicsCompat.Active
                    && _wireframeLevel == 0
                    && !Mods.RenderOptions.CelShading;
                if (directShadow)
                {
                    Mods.Render.ModernGraphicsCompat.BeginRetainedPreVisibilityPass();
                    Mods.Render.ModernGraphicsCompat.BeginRetainedWorldFrame();
                }
#else
                const bool directShadow = false;
#endif
                IReadOnlyList<Mods.Render.RetainedDrawPacket> shadowPackets =
                    _retainedRenderWorld.Opaque;
                for (int i = 0; i < shadowPackets.Count; i++)
                {
                    Mods.Render.RetainedDrawPacket packet = shadowPackets[i];
                    RenderItem item = packet.Item;
                    if (item.ViewModel || item.Alpha < .999f
                        || item.RenderMode == RenderMode.Translucent)
                    {
                        continue;
                    }

#if !MPHREAD_SERVER
                    if (directShadow)
                    {
                        Matrix4 viewInverse = item.BillboardMode switch
                        {
                            BillboardMode.Sphere => _viewInvRotMatrix,
                            BillboardMode.Cylinder => _viewInvRotYMatrix,
                            _ => Matrix4.Identity
                        };
                        Mods.Render.RetainedWorldTextureSet textures =
                            RetainedWorldTextures(item);
                        if (Mods.Render.ModernGraphicsCompat.TryDrawRetainedWorld(
                            item, packet.Mesh, textures, _showTextures,
                            useLighting: false, _faceCulling,
                            projectionOverride: null, viewInverse,
                            Mods.Render.WorldRenderPassKind.Opaque))
                        {
                            _retainedDirectShadowDraws++;
                            NoteRetainedTextureSampling(
                                textures, item.XRepeat, item.YRepeat);
                            continue;
                        }
                    }
#endif
                    _retainedCompatibilityShadowDraws++;
                    RenderItem(item);
                }

                _shadowReady = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _shadowRefused = true;
                _shadowReady = false;
                Console.WriteLine($"[render] shadow maps unavailable: {ex.Message}");
                DisposeShadowMap();
            }
            finally
            {
                GL.ColorMask(true, true, true, true);
                GL.DepthMask(true);
                GL.Disable(EnableCap.AlphaTest);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
                GL.Viewport(0, 0, _targetSize.X, _targetSize.Y);
                GL.UseProgram(_shaderProgramId);
                GL.UniformMatrix4(_shaderLocations.ViewMatrix, false, ref _viewMatrix);
                GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, false, ref _perspectiveMatrix);
                UpdateUniforms();
            }
        }

        private void EnsureShadowTarget()
        {
            int desired = Math.Min(WantedShadowSize, MaxRenderTargetSize());
            if (desired <= 0) return;
            if (_shadowFramebuffer == 0)
            {
                _shadowFramebuffer = GL.GenFramebuffer();
                _shadowDepthTexture = GL.GenTexture();
                _shadowColorTexture = GL.GenTexture();
            }
            if (_shadowTargetSize == desired) return;

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);

            GL.BindTexture(TextureTarget.Texture2D, _shadowColorTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                desired, desired, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _shadowColorTexture, 0);

            GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
                desired, desired, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, _shadowDepthTexture, 0);

            ValidateFramebuffer("Shadow map");
            _shadowTargetSize = desired;
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void DisposeShadowMap()
        {
            _shadowReady = false;
            _shadowTargetSize = 0;
            if (_shadowFramebuffer != 0)
            {
                GL.DeleteFramebuffer(_shadowFramebuffer);
                _shadowFramebuffer = 0;
            }
            DeleteTexture(ref _shadowDepthTexture);
            DeleteTexture(ref _shadowColorTexture);
        }
    }
}
