using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    [Flags]
    internal enum FrameRenderResource
    {
        None = 0,
        SceneColor = 1,
        SceneDepth = 2,
        SceneStencil = 4,
        ShadowDepth = 8,
        PbrAlbedo = 16,
        PbrNormal = 32,
        PbrMaterial = 64,
        ProcessedScene = 128,
        Output = 256
    }

    internal enum FrameRenderPassKind
    {
        Shadow,
        WorldSetup,
        World,
        Outlines,
        SceneOverlays,
        DeferredPbr,
        PostProcess,
        Composite
    }

    internal readonly record struct FrameRenderGraphPass(
        FrameRenderPassKind Kind,
        string Name,
        FrameRenderResource Reads,
        FrameRenderResource Writes);

    /// <summary>
    /// Top-level presentation graph. It deliberately starts as an ordering and
    /// resource-ownership contract: each existing pass keeps its rendering
    /// implementation while the frame no longer depends on an implicit chain
    /// of calls in Renderer.RenderFrameContent.
    /// </summary>
    internal sealed class FrameRenderGraph
    {
        private static readonly FrameRenderGraphPass[] _passes =
        {
            new(FrameRenderPassKind.Shadow, "frame.shadow",
                FrameRenderResource.None, FrameRenderResource.ShadowDepth),
            new(FrameRenderPassKind.WorldSetup, "frame.world-setup",
                FrameRenderResource.None,
                FrameRenderResource.SceneColor
                    | FrameRenderResource.SceneDepth
                    | FrameRenderResource.SceneStencil),
            new(FrameRenderPassKind.World, "frame.world",
                FrameRenderResource.SceneDepth | FrameRenderResource.SceneStencil,
                FrameRenderResource.SceneColor
                    | FrameRenderResource.SceneDepth
                    | FrameRenderResource.SceneStencil),
            new(FrameRenderPassKind.Outlines, "frame.outlines",
                FrameRenderResource.SceneDepth,
                FrameRenderResource.SceneColor),
            new(FrameRenderPassKind.SceneOverlays, "frame.scene-overlays",
                FrameRenderResource.SceneColor | FrameRenderResource.SceneDepth,
                FrameRenderResource.SceneColor | FrameRenderResource.SceneDepth),
            new(FrameRenderPassKind.DeferredPbr, "frame.pbr",
                FrameRenderResource.SceneDepth,
                FrameRenderResource.PbrAlbedo
                    | FrameRenderResource.PbrNormal
                    | FrameRenderResource.PbrMaterial),
            new(FrameRenderPassKind.PostProcess, "frame.post",
                FrameRenderResource.SceneColor
                    | FrameRenderResource.SceneDepth
                    | FrameRenderResource.ShadowDepth
                    | FrameRenderResource.PbrAlbedo
                    | FrameRenderResource.PbrNormal
                    | FrameRenderResource.PbrMaterial,
                FrameRenderResource.ProcessedScene),
            new(FrameRenderPassKind.Composite, "frame.composite",
                FrameRenderResource.SceneColor | FrameRenderResource.ProcessedScene,
                FrameRenderResource.Output)
        };

        internal IReadOnlyList<FrameRenderGraphPass> Passes => _passes;

        internal static bool Validate(out string error)
        {
            FrameRenderPassKind[] expected =
            {
                FrameRenderPassKind.Shadow,
                FrameRenderPassKind.WorldSetup,
                FrameRenderPassKind.World,
                FrameRenderPassKind.Outlines,
                FrameRenderPassKind.SceneOverlays,
                FrameRenderPassKind.DeferredPbr,
                FrameRenderPassKind.PostProcess,
                FrameRenderPassKind.Composite
            };
            if (_passes.Length != expected.Length)
            {
                error = "frame render graph pass count changed";
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (_passes[i].Kind != expected[i])
                {
                    error = $"frame render graph pass {i} is {_passes[i].Kind}, expected {expected[i]}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(_passes[i].Name))
                {
                    error = $"frame render graph pass {i} has no name";
                    return false;
                }
            }
            if ((_passes[1].Writes & (FrameRenderResource.SceneColor
                | FrameRenderResource.SceneDepth
                | FrameRenderResource.SceneStencil))
                != (FrameRenderResource.SceneColor
                    | FrameRenderResource.SceneDepth
                    | FrameRenderResource.SceneStencil))
            {
                error = "world setup no longer establishes scene attachments";
                return false;
            }
            if ((_passes[^1].Writes & FrameRenderResource.Output) == 0)
            {
                error = "frame graph no longer produces output";
                return false;
            }
            error = "";
            return true;
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private readonly Mods.Render.FrameRenderGraph _frameRenderGraph = new();

        private bool ExecuteCoreFrameRenderGraph(bool drawGameHud)
        {
            foreach (Mods.Render.FrameRenderGraphPass pass in _frameRenderGraph.Passes)
            {
                switch (pass.Kind)
                {
                case Mods.Render.FrameRenderPassKind.Shadow:
                    RenderShadowMap();
                    break;

                case Mods.Render.FrameRenderPassKind.WorldSetup:
                    GL.Clear(ClearBufferMask.ColorBufferBit
                        | ClearBufferMask.DepthBufferBit
                        | ClearBufferMask.StencilBufferBit);
                    GL.ClearStencil(0);
                    UpdateUniforms();
                    SetPauseMenuUniforms();
                    if (_exiting)
                        return false;
                    break;

                case Mods.Render.FrameRenderPassKind.World:
                    ExecuteWorldRenderGraph();
                    break;

                case Mods.Render.FrameRenderPassKind.Outlines:
                    DrawWorldOutlines();
                    break;

                case Mods.Render.FrameRenderPassKind.SceneOverlays:
                    if (!Services.IsReplica)
                        ModDrawPreview();
                    if (drawGameHud
                        && this.Players.Main.LoadFlags.TestFlag(global::MphRead.Entities.LoadFlags.Active)
                        && CameraMode == CameraMode.Player)
                    {
                        SetHudLayerUniforms();
                        this.Players.Main.DrawHudModels();
                        UnsetHudLayerUniforms();
                    }
                    else if (drawGameHud && ScoreboardOverFreeCamera)
                    {
                        SetHudLayerUniforms();
                        this.Players.Main.DrawHudModels();
                        UnsetHudLayerUniforms();
                    }
                    break;

                case Mods.Render.FrameRenderPassKind.DeferredPbr:
                    RenderDeferredPbrGBuffer();
                    break;

                case Mods.Render.FrameRenderPassKind.PostProcess:
                    ApplyGraphicsPostProcess();
                    break;

                case Mods.Render.FrameRenderPassKind.Composite:
                    CheckGlError("EndWorldPass");
                    BeginCompositePass();
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    {
                        GL.Begin(PrimitiveType.TriangleStrip);
                        GL.TexCoord3(1f, 1f, 0f);
                        GL.Vertex3(1f, 1f, 0f);
                        GL.TexCoord3(0f, 1f, 0f);
                        GL.Vertex3(-1f, 1f, 0f);
                        GL.TexCoord3(1f, 0f, 0f);
                        GL.Vertex3(1f, -1f, 0f);
                        GL.TexCoord3(0f, 0f, 0f);
                        GL.Vertex3(-1f, -1f, 0f);
                        GL.End();
                    }
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    if (_graphicsOutputReady)
                        ReleaseFrameTransientFramebufferTexture(
                            ref _graphicsOutputTexture,
                            _graphicsOutputFramebuffer,
                            ReplayOutputFramebuffer());
                    break;
                }
            }
            return true;
        }
    }
}
