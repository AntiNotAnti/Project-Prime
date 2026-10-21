using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using MphRead.Sound;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

internal static class CustomReplayIndexedNativeEvidenceChecks
{
    internal static int Run(string input,string output)
    {
        string cwd=Directory.GetCurrentDirectory();NativeWindow? window=null;DesktopGraphicsSession? graphics=null;
        try
        {
            input=Path.GetFullPath(input);output=Path.GetFullPath(output);Directory.CreateDirectory(output);string loaded=typeof(Scene).Assembly.Location;Console.WriteLine("Loaded ProjectPrime: "+loaded+" SHA256="+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(loaded))));
            string data=File.ReadAllText(Path.Combine(input,"replay-fixture-data.txt"));Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();MapAudit.ForceEveryone=true;
            string archive=Path.Combine(input,"actual-full-custom"+DemoFile.Extension);string packagePath=Path.Combine(input,"variant.ppweapon");if(new FileInfo(packagePath).Length>32*1024*1024)throw new InvalidDataException("Package bound.");var store=new WeaponPackageStore(Path.Combine(output,"store"));store.Install(File.ReadAllBytes(packagePath));using var prepared=Task.Run(()=>PreparedCustomReplayArchive.Prepare(archive,store)).GetAwaiter().GetResult();using(var baseline=prepared.CopyWorld())if(!baseline.IndexedFirePresentation)throw new InvalidDataException("Actual indexed artifact required.");
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(640,480);settings.StartVisible=true;settings.Title="Actual indexed native presentation evidence";window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);
            using var source=PassiveReplayScene.OpenCustomArchiveInspection(prepared,new Vector2i(640,480));prepared.Dispose();var activeKeys=new HashSet<ShotKey>();var beamRows=new List<object>();var factRows=new List<object>();var diagnostics=new List<object>();var bodyJoins=new HashSet<ShotKey>();int steps=0;
            void Observe()
            {
                uint frame=source.Session.RecordingFrame;var poses=source.Scene.ReplayPoses??throw new InvalidDataException("Admitted indexed cursor missing.");if(!poses.Prepare()||!poses.IndexedFirePresentation)throw new InvalidDataException("Indexed cursor failed: "+poses.LastError);
                foreach(var beam in source.Scene.GetBeamProjectileEntities())if(beam.ModCustomShot==null&&beam.Beam==BeamType.PowerBeam&&beam.Lifespan>0&&beam.ModShotId!=0){if(activeKeys.Count>=8192)throw new InvalidDataException("Native evidence key budget.");if(activeKeys.Add(beam.ModLaunchKey))beamRows.Add(new{frame,key=beam.ModLaunchKey,beam.ModShotId,beam.ModLaunchFrame,beam.Lifespan,position=new[]{beam.Position.X,beam.Position.Y,beam.Position.Z}});}
                foreach(var fact in poses.ResolvedShotFactsAt(frame)){if(factRows.Count>=8192)throw new InvalidDataException("Native resolved evidence budget.");var key=new ShotKey(fact.AuthorityEpoch,fact.MatchId,fact.ShooterSlot,fact.ShooterGeneration,fact.ShooterLifeId,fact.ShotId);bool hasAuthored=poses.TryAuthoredFire(fact,out var fire);factRows.Add(new{frame,key,fact.DamageEventId,fact.Weapon,fact.Headshot,fact.Damage,fact.HealthAfter,hasAuthored,hasPose=hasAuthored&&fire.HasPose});}
                foreach(var diagnostic in poses.CombatDiagnosticsAt(frame)){if(diagnostics.Count>=8192)throw new InvalidDataException("Native diagnostic evidence budget.");var fact=diagnostic.Fact;var key=new ShotKey(fact.AuthorityEpoch,fact.MatchId,fact.ShooterSlot,fact.ShooterGeneration,fact.ShooterLifeId,fact.ShotId);bool actualMatch=source.State.Match is{} match&&match.MatchId==fact.MatchId&&match.AuthorityEpoch==fact.AuthorityEpoch;bool hasAuthored=poses.TryAuthoredFire(fact,out var authored);bool joined=actualMatch&&fact.Weapon==(byte)BeamType.PowerBeam&&!fact.Headshot&&fact.Damage>0&&diagnostic.HasFire&&diagnostic.HasPose&&hasAuthored&&authored.ShotId==fact.ShotId&&authored.HasPose;if(joined)bodyJoins.Add(key);diagnostics.Add(new{frame,key,fact.DamageEventId,diagnostic.FireRecordingFrame,diagnostic.HasFire,diagnostic.HasPose,diagnostic.HasAckTarget,ackServerFrame=double.IsFinite(diagnostic.AckServerFrame)?(double?)diagnostic.AckServerFrame:null,ackImpactDistance=float.IsFinite(diagnostic.AckImpactDistance)?(float?)diagnostic.AckImpactDistance:null,joined});}
            }
            Observe();while(!source.Session.AtEnd){if(++steps>PreparedCustomReplayArchive.MaximumReconstructionFrames)throw new InvalidDataException("Native evidence frame budget.");if(!source.StepCustomArchiveInspection())break;Observe();}
            var matched=bodyJoins.Where(activeKeys.Contains).ToArray();var options=new JsonSerializerOptions{WriteIndented=true};File.WriteAllText(Path.Combine(output,"native-indexed-evidence.json"),JsonSerializer.Serialize(new{steps,activeNativeKeys=beamRows,resolvedNativeFacts=factRows,diagnostics,matchedBodyKeys=matched,scope="Fresh cold-open actual indexed archive. Observed active native PowerBeam entities, genuine resolved body ShotFacts and full-identity authored FireEvent/pose joins. No fabricated packet, health write, render/first-person or audible playback claim."},options));if(activeKeys.Count==0||matched.Length==0)throw new InvalidDataException("No actual body ShotId joined active native projectile and indexed authored-fire pose diagnostics.");Console.WriteLine($"PASS actual native indexed evidence: activeKeys={activeKeys.Count}, resolved={factRows.Count}, diagnostics={diagnostics.Count}, bodyJoins={matched.Length}");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("INDEXED NATIVE EVIDENCE FAIL "+error);return 1;}
        finally{AudioLifetime.Shutdown();graphics?.Dispose();window?.Dispose();Directory.SetCurrentDirectory(cwd);}
    }
}
