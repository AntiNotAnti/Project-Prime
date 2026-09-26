using System;
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

                Vector3 direction = _light1Vector;
                if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)
                    || !float.IsFinite(direction.Z) || direction.LengthSquared < .0001f)
                {
                    direction = new Vector3(-.45f, -.82f, -.35f);
                }
                direction = direction.Normalized();

                Vector3 center = _cameraPosition + _cameraFacing * 24f;
                Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .92f
                    ? Vector3.UnitZ : Vector3.UnitY;
                float span = Mods.RenderOptions.Shadows == Mods.ShadowQuality.Low ? 100f : 130f;

                // Stabilize the orthographic light camera to whole shadow texels.
                // Without this, every sub-pixel camera movement slides the entire
                // shadow map and produces visible crawl at high refresh rates.
                Vector3 lightRight = Vector3.Cross(direction, up).Normalized();
                Vector3 lightUp = Vector3.Cross(lightRight, direction).Normalized();
                float worldPerTexel = span / Math.Max(1, _shadowTargetSize);
                float alongRight = Vector3.Dot(center, lightRight);
                float alongUp = Vector3.Dot(center, lightUp);
                center += lightRight * (MathF.Round(alongRight / worldPerTexel)
                        * worldPerTexel - alongRight)
                    + lightUp * (MathF.Round(alongUp / worldPerTexel)
                        * worldPerTexel - alongUp);

                Vector3 eye = center - direction * 96f;
                _shadowView = Matrix4.LookAt(eye, center, up);
                _shadowProjection = Matrix4.CreateOrthographic(span, span, 1f, 220f);

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

                for (int i = 0; i < _nonDecalItems.Count; i++)
                {
                    RenderItem item = _nonDecalItems[i];
                    if (item.ViewModel || item.Alpha < .999f
                        || item.RenderMode == RenderMode.Translucent)
                    {
                        continue;
                    }
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
