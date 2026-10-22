#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Render;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
namespace MphRead.Mods.Render.Characters;
internal static partial class CharacterAcceptanceCheck
{
    // Called only by a private acceptance driver after the ordinary Preview
    // warmup, using the same live context/scene. No CLI or launcher activation.
    // Real-source extension is PRIVATE and has NOT run on GL.
    internal static void CheckSourceRigPreview(Scene preview, NativeWindow window,
        string output, CharacterModelPack verifiedPrivatePack, Hunter hunter,
        PrivateCharacterAppearance appearance, int? sourceClipOrdinal = null, string? nativeMaterialAnchor = null)
    {
        Require(OperatingSystem.IsMacOS(), "Source-rig acceptance is macOS only.");
        Require(Scene.LauncherPreview && Scene.PreviewWanted && Scene.LauncherPreviewSlot < 0
            && Scene.LauncherHunter == hunter, "Use the warmed ordinary acceptance preview slot.");
        Require((Hunter)typeof(Scene).GetField("_launcherPreviewClockHunter", Private)!.GetValue(preview)! == hunter
            && (int)typeof(Scene).GetField("_launcherPreviewClockSuit", Private)!.GetValue(preview)! == Scene.LauncherSuit
            && (bool)typeof(Scene).GetField("_sharedPreviewClock", Private)!.GetValue(preview)! == Mods.Launcher.LauncherPresentation.Active,
            "Warm the exact current preview scheduling owner before freezing its existing deadline.");
        Require(!NetSession.Active && !DemoPlayback.IsActive && !preview.Services.IsReplica,
            "Source-rig acceptance requires an offline local preview.");
        Require(verifiedPrivatePack.TryResolvePrivate(hunter, appearance, CharacterModelPart.Biped, 0,
            out var asset) && asset.PoseSource == CharacterPoseSource.SourceRigPreview
            && verifiedPrivatePack.TryCreateSourcePreviewLease(asset, out _), "Missing verified source owner.");
        Require(verifiedPrivatePack.TryCreateSourcePreviewLease(asset, out var lease), "Stale source owner.");
        var source = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease);
        Require(source.SourceRig != null, "Source loader discarded the full hierarchy.");
        string directory = Path.GetFullPath(output); Directory.CreateDirectory(directory);
        var entity = (HunterPreviewEntity?)typeof(Scene).GetField("_preview", Private)!.GetValue(preview);
        Require(entity != null && entity.Shown == hunter && entity.Mode == Mods.Cosmetics.SkinContext.Biped,
            "Warm the existing matching biped preview before source acceptance.");
        double previousStep = (double)typeof(Scene).GetField("_launcherPreviewNextStep", Private)!.GetValue(preview)!;
        float previousYaw = Mods.Cosmetics.CosmeticPreview.Yaw;
        try
        {
            // Freeze the existing preview scheduling owner. Diagnostic poses are
            // explicit draw-only matrices; no independent animation time exists.
            Set(preview, "_launcherPreviewNextStep", double.MaxValue);
            bool realSource=sourceClipOrdinal.HasValue;
            Require(realSource==(nativeMaterialAnchor!=null),"Explicit raw clip ordinal and native material anchor required together.");
            CharacterSourceClip? clip=null;
            if(realSource)
            {
                Require(entity!.TrySetSourceMaterialRigPreview(verifiedPrivatePack,appearance,nativeMaterialAnchor!)
                    &&entity.TrySelectSourceRigClip(sourceClipOrdinal!.Value),"Verified real-source attachment/strict raw-play clip rejected.");
                clip=entity!.SelectedSourceRigClip;Require(clip!=null,"Missing captured raw source clip.");
            }
            var frames = new System.Collections.Generic.List<object>();
            int[]? fixedLists=null;int[]? fixedBindings=null;
            Matrix4? previousOrientation=null;
            double[] phases=realSource?new[]{0d,clip!.Duration*.125,clip.Duration*.25,clip.Duration*.5,clip.Duration*.75,clip.Duration}
                :new[]{0d,.125,.5,1,2,5};
            foreach (double phase in phases)
            {
                if(realSource)Require(entity!.TrySetSourceRigTime(phase),"Caller-time source pose rejected.");
                else
                {
                    var pose=source.SourceRig!.Nodes.Select((node,i)=>phase==0||source.SourceRig.MeshAncestors.Contains(i)?node.Local
                        :Matrix4.CreateRotationZ(MathF.Sin(i*.17f+(float)phase)*.1f)*node.Local).ToArray();
                    Require(entity!.TrySetSourceRigPreview(verifiedPrivatePack,appearance,pose),"Source attachment rejected.");
                }
                Require(entity.TryGetSourceRigPreview(out var weighted) && weighted.IsSourcePreview,
                    "Source preview silently became native.");
                if(realSource)
                {
                    Require(weighted.Segments.Select(s=>s.SourceMaterialName).SequenceEqual(Enumerable.Range(0,7).Select(i=>"retro_material_"+i)),
                        "Seven original source material identities must remain distinct.");
                    var lists=weighted.Segments.Select(s=>s.ListId).ToArray();var bindings=weighted.Segments.Select(s=>s.AlbedoBinding??0).ToArray();
                    Require(bindings.All(b=>b!=0),"Each real source segment must retain its actual first-TXTR albedo.");
                    if(fixedLists!=null)Require(lists.SequenceEqual(fixedLists)&&bindings.SequenceEqual(fixedBindings!),"Caller time recompiled or replaced textures.");
                    fixedLists=lists;fixedBindings=bindings;
                }
                foreach (int angle in new[] { 0, 90, 180, 270 })
                {
                    var rawBeforeDraw = weighted.Segments.Select(s => s.SourceMatrixPalette!.ToArray()).ToArray();
                    Mods.Cosmetics.CosmeticPreview.Yaw = MathHelper.DegreesToRadians(angle);
                    Require(preview.ModDrawPreviewAlone(window.FramebufferSize), "Source preview draw failed.");
                    var expectedWorld=Matrix4.CreateScale(Mods.Cosmetics.CosmeticPreview.Zoom)
                        *Matrix4.CreateRotationY(Mods.Cosmetics.CosmeticPreview.Yaw)
                        *Matrix4.CreateRotationY(MathHelper.DegreesToRadians(180));
                    Require(previousOrientation==null||CharacterSourceClip.MatrixDistance(previousOrientation.Value,expectedWorld)>.1f,
                        "Distinct angle inputs did not produce distinct expected world transforms.");
                    previousOrientation=expectedWorld;
                    var packets = Items(preview, "_previewItems").Where(p => p.Type == RenderItemType.Mesh).ToArray();
                    Require(packets.Length == weighted.Segments.Count, "Missing batch or native fallback mesh.");
                    foreach (var segment in weighted.Segments)
                    {
                        var packet = packets.Single(p => p.ListId == segment.ListId);
                        if(realSource)Require(packet.HasTexture&&packet.TextureBindingId==segment.AlbedoBinding
                            &&packet.TexgenMode==TexgenMode.Texcoord&&packet.XRepeat==segment.WrapS&&packet.YRepeat==segment.WrapT,
                            "Actual source image binding or authored source UV/wrap was replaced.");
                        Require(CharacterSourceClip.MatrixDistance(packet.Transform,expectedWorld)<.000001f,
                            "Source packet omitted or changed the existing preview owner world orientation.");
                        Require(packet.WeightedSkinning && segment.SourceJointPalette is { Count: > 0 and <= 32 }
                            && packet.MatrixStackCount == segment.SourceJointPalette.Count
                            && segment.SourceMatrixPalette is { Length: > 0 and <= 512 }
                            && segment.SourcePresentationPalette is { Length: > 0 and <= 512 }
                            && packet.MatrixStack.Take(packet.MatrixStackCount * 16).SequenceEqual(segment.SourcePresentationPalette),
                            "Submitted batch differs from the source presentation palette snapshot.");
                        int segmentIndex = weighted.Segments.ToList().IndexOf(segment);
                        Require(rawBeforeDraw[segmentIndex].SequenceEqual(segment.SourceMatrixPalette),
                            "Draw submission mutated the original source skin palette.");
                        for (int slot = 0; slot < packet.MatrixStackCount; slot++)
                        {
                            int o = slot * 16;
                            var raw = segment.SourceMatrixPalette!;
                            var skin = new Matrix4(raw[o],raw[o+1],raw[o+2],raw[o+3],
                                raw[o+4],raw[o+5],raw[o+6],raw[o+7],raw[o+8],raw[o+9],raw[o+10],raw[o+11],
                                raw[o+12],raw[o+13],raw[o+14],raw[o+15]);
                            var expected = skin * expectedWorld;
                            for (int row = 0; row < 4; row++)
                                for (int column = 0; column < 4; column++)
                                    Require(MathF.Abs(packet.MatrixStack[o + row * 4 + column] - expected[row,column]) < .000001f,
                                        "Actual GPU palette omitted source skin * owner world composition.");
                        }
                    }
                    lease.RequireCurrent(asset);
                    var capture=CharacterSourceCaptureReceipt.Write(
                        Path.Combine(directory,$"source-phase-{phase}-angle-{angle}.png"),
                        path=>ScreenCapture.SaveWindow(preview,path));
                    Require(capture.Width==window.FramebufferSize.X && capture.Height==window.FramebufferSize.Y,
                        "Capture dimensions differ from the actual framebuffer.");
                    DesktopGraphicsSession.Present(window);
                    frames.Add(new { phase, angle, capture, batches = packets.Length,
                        expectedWorld=new[]{expectedWorld.M11,expectedWorld.M12,expectedWorld.M13,expectedWorld.M14,
                            expectedWorld.M21,expectedWorld.M22,expectedWorld.M23,expectedWorld.M24,
                            expectedWorld.M31,expectedWorld.M32,expectedWorld.M33,expectedWorld.M34,
                            expectedWorld.M41,expectedWorld.M42,expectedWorld.M43,expectedWorld.M44},
                        submittedWorldTransforms=packets.Select(p=>new[]{p.Transform.M11,p.Transform.M12,p.Transform.M13,p.Transform.M14,
                            p.Transform.M21,p.Transform.M22,p.Transform.M23,p.Transform.M24,p.Transform.M31,p.Transform.M32,p.Transform.M33,p.Transform.M34,
                            p.Transform.M41,p.Transform.M42,p.Transform.M43,p.Transform.M44}).ToArray(),
                        gpuPaletteSnapshots=packets.Select(p=>new { count=p.MatrixStackCount,
                            sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                                System.Runtime.InteropServices.MemoryMarshal.AsBytes(p.MatrixStack.AsSpan(0,p.MatrixStackCount*16)))).ToLowerInvariant() }).ToArray(),
                        palettes = weighted.Segments.Select(s => s.SourceJointPalette!.Count).ToArray(),
                        materials=weighted.Segments.Select(s=>new{s.SourceMaterialName,s.SourcePrimitiveOrdinal,s.NativeMaterialIndex,s.AlbedoBinding}).ToArray() });
                }
            }
            lease.Require(asset); // Strong proof once before accepted receipt publication.
            File.WriteAllText(Path.Combine(directory, "source-rig-packet-check.json"),
                JsonSerializer.Serialize(new { scope = realSource?"PRIVATE real Echoes source rig, strict raw ANIM caller seconds, diagnostic first-TXTR albedos, existing macOS GL preview":"synthetic private source rig, fixed diagnostic poses, existing macOS GL preview",
                    rawClip=clip==null?null:new{clip.Ordinal,clip.Name,clip.ResourceId,clip.ContentSha256,clip.Duration},
                    rawCallerTimes=phases, rawPrimitiveDrawPacketsAccepted=realSource, acceptedCaptureCount=frames.Count, expectedAndSubmittedFourWorldOrientationsChecked=true, gpuPaletteWorldCompositionChecked=true,
                    rawSourceSkinPreserved=true,
                    materialShaderAccepted=false, axisUnitPlacementAccepted=false, namedGraphAccepted=false,
                    assetSha256 = lease.ModelSha256, sourceNodes = source.SourceRig!.Nodes.Count,
                    sourceJoints = source.Joints.Count, frames,
                    sourceMediaAccepted = false, sourceClipAccepted = false, gameplayAccepted = false,
                    firstPersonAccepted = false, alternateFormAccepted = false, devicePerformanceAccepted = false }, Json));
        }
        finally
        {
            entity?.ClearSourceRigPreview();
            Set(preview, "_launcherPreviewNextStep", previousStep);
            Mods.Cosmetics.CosmeticPreview.Yaw = previousYaw;
        }
    }
}
#endif
