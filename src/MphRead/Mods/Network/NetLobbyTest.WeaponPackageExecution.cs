using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    // Exact creator package, real dedicated engine/world contacts and ordinary
    // endpoint-owned native inputs. Placement/200HP lane setup is declared by
    // ActionTransportFixture; no grants or resource-state injection occur.
    public static int RunWeaponPackageExecution(string data)
    {
        string cwd=Directory.GetCurrentDirectory();string root=Path.Combine(Path.GetTempPath(),"prime-package-engine-"+Guid.NewGuid().ToString("N"));
        int before=_checks;Directory.CreateDirectory(root);
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            var reference=WeaponPackage.Parse(WeaponPackageReference.BuildRiftlineM7());
            var resources=new System.Collections.Generic.Dictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(string path in reference.ResourcePaths)if(path!="weapon.json")resources.Add(path,reference.CopyResource(path));
            var rules=JsonNode.Parse(resources[reference.Project.MechanicsPath])!;rules["damage"]!["body"]=37;rules["damage"]!["head"]=83;rules["magazineCapacity"]=6;rules["initialReserve"]=23;rules["maximumReserve"]=40;rules["shotCooldownTicks"]=24;rules["reloadTicks"]=42;rules["opticsMilli"]=new JsonArray(1500,2500,4000,6000);
            resources[reference.Project.MechanicsPath]=Encoding.UTF8.GetBytes(rules.ToJsonString());
            var model=JsonNode.Parse(resources[reference.Project.ModelPath])!;model["palette"]![0]=new JsonArray(.95f,.08f,.55f);resources[reference.Project.ModelPath]=Encoding.UTF8.GetBytes(model.ToJsonString());
            foreach(string path in reference.Project.AudioPaths){var wave=resources[path];for(int at=44;at<wave.Length;at+=2){short sample=System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(at));System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(at),(short)(sample/2));}}
            var project=JsonNode.Parse(reference.Project.ToCanonicalBytes())!;project["contentId"]="12c012c0-1122-3344-5566-778899aabbcc";project["name"]="Network creator four-optic six-round";
            var package=WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));
            Check(package.Project.ContentId!=reference.Project.ContentId&&package.Definition.Mechanics.OpticMagnificationsMilli.Count==4,"network test uses new creator lineage and four validated package optics");
            string serverPath=Path.Combine(root,"server"),firstPath=Path.Combine(root,"first"),secondPath=Path.Combine(root,"second");
            var serverStore=new WeaponPackageStore(serverPath);var firstStore=new WeaponPackageStore(firstPath);var secondStore=new WeaponPackageStore(secondPath);
            serverStore.Install(package.CopyArchive());firstStore.Install(package.CopyArchive());secondStore.Install(package.CopyArchive());
            using(var f=new ActionTransportFixture(maxPlayers:3,package:package,serverStore:serverPath,firstStore:firstPath,secondStore:secondPath,acquireFirst:false))
            {
                Check(f.FirstPackage!.Binding!.Package(0).PackageHash==package.PackageHash&&f.SecondPackage!.Binding!.Package(0).PackageHash==package.PackageHash
                    &&f.FirstReady.Ready!.Value.Manifest==new CustomWeaponManifest(new[]{package.Definition}).Digest,"both clients prepare exact retained package and post-load CWM1 uses selected creator definition");
                var preparedResources=f.FirstPackage.Binding.Resources(0);
                Check(preparedResources.ArchiveHash==package.PackageHash&&preparedResources.Model.Color(0)==new OpenTK.Mathematics.Vector4(.95f,.08f,.55f,1),"client pre-load preparation owns exact selected creator model palette");
                for(int cue=0;cue<4;cue++)Check(preparedResources.Wave(cue).SequenceEqual(WeaponPackage.DecodeWave(package.CopyResource(package.Project.AudioPaths[cue]))),"client pre-load preparation owns exact selected creator PCM cue "+cue);
                Check(f.A.State is {HasWeapon:false}&&f.B.State is {HasWeapon:false}&&!f.Latest.First.HasState&&!f.Latest.Second.HasState,"package preparation and native load start both real lives empty");
                f.AcquirePublic(f.First);
                Check(f.A.State is {HasWeapon:true,State.Magazine:6,State.Reserve:23}&&f.B.State is {HasWeapon:false},"ordinary authored world contact funds only approaching creator life with six/twentythree");
                f.Equip(f.First,freshSources:true);f.Refresh(f.First);var optic=f.Settle(f.First,f.Action(f.First,3,CustomWeaponActionKind.Optic,3));f.A.AckReceipt(3);
                Check(optic.Receipt.Result==WeaponActionResult.Accepted&&optic.Receipt.After is {Optic:3},"actual authority and receipt codec accept selected package optic3");
                f.Refresh(f.Second);var fire=f.Press(f.First,4,optic:3);f.Send(f.First,fire);
                WaitForActionInputs(f,()=>f.A.Receipts.ContainsKey(4)&&f.A.Contacts.ContainsKey(4)&&f.Latest.Payments==1,"real selected package body projectile resolves canonical payment");
                var paid=f.A.Receipts[4];
                Check(paid.Receipt.Result==WeaponActionResult.Accepted&&paid.Receipt.After is {Magazine:5,Reserve:23,Optic:3}
                    &&paid.Receipt.After.Value.FireReadyTick-paid.Receipt.Action.SourceFrame==24&&f.Latest.Second.Health==163&&!f.Latest.Second.NativeHistory,"selected creator shot spends one round uses24tick cadence and pays exactly37HP outside native damage history");
                f.A.AckReceipt(4);f.A.AckContact(4);f.Refresh(f.First);var reload=f.Settle(f.First,f.Action(f.First,5,CustomWeaponActionKind.Reload));f.A.AckReceipt(5);
                Check(reload.Receipt.Result==WeaponActionResult.Accepted&&reload.Receipt.After is {Phase:CustomWeaponPhase.Reloading,Magazine:5,Reserve:23,Optic:0}
                    &&reload.Receipt.After.Value.ReloadCompleteTick-reload.Receipt.Action.SourceFrame==42,"source-admitted selected package reload clears optic and binds exact42tick completion");
                uint deadline=reload.Receipt.After!.Value.ReloadCompleteTick;
                WaitForActionInputs(f,()=>f.Latest.First.State.Tick>=deadline&&f.Latest.First.State.Phase==CustomWeaponPhase.Ready,"genuine native source samples advance package reload completion");
                Check(f.Latest.First.State is {Magazine:6,Reserve:22,Optic:0}&&f.Latest.Second.Health==163,"package reload transfers exactly one reserve without duplicate damage");
                f.Refresh(f.First);var fourth=f.Settle(f.First,f.Action(f.First,6,CustomWeaponActionKind.Optic,4));f.A.AckReceipt(6);
                Check(fourth.Receipt.Result==WeaponActionResult.Accepted&&fourth.Receipt.After is {Optic:4},"fourth package optic survives authority and private result codecs");
                f.Refresh(f.Second);var head=f.Press(f.First,7,head:true,optic:4);f.Send(f.First,head);
                WaitForActionInputs(f,()=>f.A.Receipts.ContainsKey(7)&&f.A.Contacts.ContainsKey(7)&&f.Latest.Payments==2,"actual creator head ray resolves through native collision and canonical payment");
                Console.WriteLine($"PACKAGE HEAD EVIDENCE receipt={f.A.Receipts[7].Receipt.After}; HP={f.Latest.Second.Health}; nativeHistory={f.Latest.Second.NativeHistory}; contacts={f.Latest.Contacts}; failures={f.Latest.StepFailures}; contact={f.A.Contacts[7].Outcome}");
                Check(f.A.Receipts[7].Receipt.After is {Magazine:5,Reserve:22,Optic:4}&&f.Latest.Second.Health==80&&!f.Latest.Second.NativeHistory
                    &&f.Latest.Contacts==2&&f.Latest.StepFailures==0,"creator headshot pays83HP exactly once with healthy engine and unchanged native history");f.A.AckReceipt(7);f.A.AckContact(7);
                // A late spectator has same mechanics but a different archive revision;
                // neither metadata similarity nor public CWM1 may bypass CWPK.
                project["name"]="Wrong archive metadata revision";var wrong=WeaponPackage.Parse(WeaponPackageBuilder.Build(WeaponProject.Parse(Encoding.UTF8.GetBytes(project.ToJsonString())),resources));
                string latePath=Path.Combine(root,"late");var lateStore=new WeaponPackageStore(latePath);lateStore.Install(wrong.CopyArchive());
                var late=f.Rig.Add(12920,initialSpectator:true);var missing=new PackagePreparationProbe(late,lateStore);var missingReady=new CustomReadinessProbe(late,package.Definition);
                try
                {
                    f.Rig.Wait(()=>missing.Key.HasValue&&missing.Rejected>0,"late actual client sees required exact archive absent despite matching creator mechanics");late.Loaded();MaintainActionInputs(f,150,"premature late native-load ACK crosses unprepared package endpoint");
                    Check(missing.Binding==null&&!f.Rig.Server.IsWeaponPackagePrepared(late.Slot)&&missingReady.Pages==0,"mismatched late archive cannot enter native world/custom readiness");
                }
                finally{missingReady.Dispose();missing.Dispose();}
                int oldSlot=late.Slot;lateStore.Install(package.CopyArchive());late.Send(PacketType.Bye,Array.Empty<byte>());f.Rig.Wait(()=>!f.Rig.Server.IsWeaponPackagePrepared(oldSlot),"mismatched late peer disconnect retires package preparation");f.Rig.Clients.Remove(late);late.Dispose();
                var replacement=f.Rig.Add(12921,initialSpectator:true);using var prepared=new PackagePreparationProbe(replacement,lateStore);using var ready=new CustomReadinessProbe(replacement,package.Definition);
                f.Rig.Wait(()=>prepared.Binding!=null,"reconnected late client explicitly prepares correctly installed archive");prepared.Send();f.Rig.Wait(()=>f.Rig.Server.IsWeaponPackagePrepared(replacement.Slot),"new late transport incarnation independently confirms exact package");replacement.Loaded();
                f.Rig.Wait(()=>ready.Ready.HasValue,"prepared late spectator now receives actual post-load creator manifest",10000);ready.Send();f.Rig.Wait(()=>ready.Confirmations>0&&ready.Replica is {IsReady:true},"late creator spectator assembles actual current recipient baseline",10000);
                Check(replacement.Slot==oldSlot&&f.Latest.First.State is {Magazine:5,Reserve:22}&&f.Latest.Second.Health==80,"late rejoin preserves exact ongoing creator resources and paid HP");
                Check(serverStore.Uninstall(package.PackageHash)&&serverStore.Prune()==0&&firstStore.Uninstall(package.PackageHash)&&firstStore.Prune()==0,"active authority scene and prepared client retain exact archive leases after uninstall");
            }
            Check(serverStore.Prune()==1&&firstStore.Prune()==1,"authority and client scene/fixture teardown release exact retained archives");
            Console.WriteLine($"PASS {_checks-before} real UDP/engine package execution checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("PACKAGE EXECUTION UDP FAIL "+error);return 1;}
        finally{NetSession.Stop();Directory.SetCurrentDirectory(cwd);try{Directory.Delete(root,true);}catch(Exception error){Console.Error.WriteLine("Package engine cleanup: "+error.Message);}}
    }
}
