using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

// Explicitly synthetic presentation adversaries applied to an independent
// loaded Scene. These are never published as accepted network/replay facts.
internal static class CustomReplayDrawFlagChecks
{
    internal static int Run(PassiveReplayScene replay,NativeWindow window,string output)
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new InvalidDataException(label);checks++;}
        Directory.CreateDirectory(output);var actor=replay.Scene.Players.Items[0];
        Check(actor.Health>0&&!actor.IsAltForm,"synthetic remote fixture starts from a loaded live biped");
        replay.Scene.Players.MainPlayerIndex=1;Check(!actor.IsMainPlayer,"synthetic actor uses remote third-person draw branch");
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var flags=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;
        var frozen=typeof(PlayerEntity).GetField("_frozenTimer",hidden)!;var frozenGfx=typeof(PlayerEntity).GetField("_frozenGfxTimer",hidden)!;
        var ice=(Matrix4[])typeof(PlayerEntity).GetField("_bipedIceTransforms",hidden)!.GetValue(actor)!;
        Check(ice.Length==19,"synthetic frozen remote owns all nineteen serialized ice anchors");
        var baseline=actor.Flags2&~(PlayerFlags2.HideModel|PlayerFlags2.Spectating|PlayerFlags2.Lod1|PlayerFlags2.DrawnThirdPerson);
        var charge=replay.Scene.SpawnEffectGetEntry(Metadata.ChargeEffectIds[0],Matrix4.CreateTranslation(1,2,3))??throw new InvalidDataException("Synthetic charge asset unavailable.");
        var muzzle=replay.Scene.SpawnEffectGetEntry(Metadata.MuzzleEffectIds[0],Matrix4.CreateTranslation(4,5,6))??throw new InvalidDataException("Synthetic muzzle asset unavailable.");
        Check(charge is {Elements.Count:>0}&&muzzle is {Elements.Count:>0},"synthetic fixture owns persistent real charge and muzzle effect elements");
        typeof(PlayerEntity).GetField("_chargeEffect",hidden)!.SetValue(actor,charge);typeof(PlayerEntity).GetField("_muzzleEffect",hidden)!.SetValue(actor,muzzle);charge!.SetDrawEnabled(false);muzzle!.SetDrawEnabled(false);
        bool previousDetail=Features.MaxPlayerDetail;Features.MaxPlayerDetail=false;
        try
        {
        var results=new List<object>();
        foreach(string mode in new[]{"frozen-visible","hidden-early-return","spectating-early-return","charge-muzzle-visible"})
        foreach(bool seeded in new[]{false,true})
        {
            var configured=baseline|(seeded?PlayerFlags2.Lod1|PlayerFlags2.DrawnThirdPerson:PlayerFlags2.None)
                |(mode=="hidden-early-return"?PlayerFlags2.HideModel:mode=="spectating-early-return"?PlayerFlags2.Spectating:PlayerFlags2.None);
            flags.SetValue(actor,configured);frozen.SetValue(actor,(ushort)(mode=="frozen-visible"?90:0));frozenGfx.SetValue(actor,(ushort)(mode=="frozen-visible"?90:0));
            for(int i=0;i<ice.Length;i++)ice[i]=Matrix4.CreateTranslation(i+.25f,i+.5f,i+.75f);
            for(int draw=0;draw<2;draw++)
            {
                replay.Scene.Players.Main.CameraInfo.Position=actor.Position+new Vector3(draw==0?1:10,0,0);
                bool nativeLod1=!actor.IsMainPlayer&&!Features.MaxPlayerDetail&&(actor.Position-replay.Scene.Players.Main.CameraInfo.Position).LengthSquared>=3*3;Check(nativeLod1==(draw==1),"exact native camera-distance condition exercises both LOD tiers");
                Vector3 target=actor.Position+new Vector3(0,1,0);replay.Scene.SetReplicaCamera(target+(draw==0?new Vector3(2.7f,1.5f,3.8f):new Vector3(70,30,90)),target,55);replay.Scene.OnResize();
                var expectedIce=(Matrix4[])ice.Clone();var chargeFlags=charge.Elements.Select(element=>element.Flags).ToArray();var chargeTransforms=charge.Elements.Select(element=>element.OwnTransform).ToArray();var muzzleFlags=muzzle.Elements.Select(element=>element.Flags).ToArray();var muzzleTransforms=muzzle.Elements.Select(element=>element.OwnTransform).ToArray();using var before=ReplayWorldCheckpoint.CaptureCustom(replay);
                replay.Scene.OnDrawFrame();Check(replay.Scene.OnRenderFrame()&&GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"synthetic adversarial draw renders "+mode);
                if(mode=="frozen-visible"&&draw==0)Check(ScreenCapture.SaveWindow(replay.Scene,Path.Combine(output,"synthetic-frozen-"+seeded+"-window.png")),"synthetic frozen visible image retained");
                DesktopGraphicsSession.Present(window);replay.Scene.AfterRenderFrame();
                using var after=ReplayWorldCheckpoint.CaptureCustom(replay);var differences=before.CanonicalGraphDifferences(after);File.WriteAllText(Path.Combine(output,mode+"-"+seeded+"-"+draw+"-differences.json"),JsonSerializer.Serialize(differences));
                Check(actor.Flags2==configured&&ice.AsSpan().SequenceEqual(expectedIce),"synthetic draw exactly preserves flags and all frozen ice anchors "+mode);
                Check(charge.Elements.Select(element=>element.Flags).SequenceEqual(chargeFlags)&&charge.Elements.Select(element=>element.OwnTransform).SequenceEqual(chargeTransforms)&&muzzle.Elements.Select(element=>element.Flags).SequenceEqual(muzzleFlags)&&muzzle.Elements.Select(element=>element.OwnTransform).SequenceEqual(muzzleTransforms),"synthetic persistent charge and muzzle draw preserve canonical flags and transforms "+mode);
                Check(differences.Length==0,"synthetic remote draw preserves strict full graph "+mode);
                if(mode=="charge-muzzle-visible")Check(charge.DrawEnabledOverride.HasValue&&muzzle.DrawEnabledOverride.HasValue&&charge.DrawTransformOverride.HasValue&&muzzle.DrawTransformOverride.HasValue&&replay.Scene.PresentationEffectOverrideCount>=2,"visible persistent effects register draw-only enable and transform overrides");
                replay.Scene.ClearPresentationEffectTransforms();
                Check(charge.DrawEnabledOverride==null&&muzzle.DrawEnabledOverride==null&&charge.DrawTransformOverride==null&&muzzle.DrawTransformOverride==null&&!charge.DrawTransformUsesViewModel&&!muzzle.DrawTransformUsesViewModel&&replay.Scene.PresentationEffectOverrideCount==0,"clearing presentation resets both override channels and registrations");
                using var afterClear=ReplayWorldCheckpoint.CaptureCustom(replay);Check(before.CanonicalGraphDifferences(afterClear).Length==0,"presentation override cleanup preserves strict canonical graph");results.Add(new{mode,seeded,draw,nativeLod1});
            }
        }
        File.WriteAllText(Path.Combine(output,"synthetic-draw-results.json"),JsonSerializer.Serialize(new{checks,recordingFrame=replay.Session.RecordingFrame,results,scope="Synthetic frozen remote biped, hidden/spectating early returns and persistent native charge/muzzle presentation fixtures. No genuine accepted freeze or native live draw behavior qualification."},new JsonSerializerOptions{WriteIndented=true}));return checks;
        }
        finally{Features.MaxPlayerDetail=previousDetail;}
    }
}
