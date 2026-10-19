using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Actual NetSession lifecycle methods and actual transport identity; playback-only
// transports allocate no socket and no game world. No live admission is claimed.
internal static class PartyJoinOwnershipTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool okay,string label){if(!okay)throw new InvalidOperationException(label);++checks;Console.WriteLine("PARTY OWNERSHIP PASS "+label);}
        try
        {
            NetSession.StartPlayback();var first=new NetClientJoinOwnership();first.Capture();
            var original=NetSession.EvidenceTransport;
            Check(first.IsCurrent,"actual client captures exact transport incarnation");
            NetSession.StartPlayback();var replacement=NetSession.EvidenceTransport;
            Check(!first.IsCurrent&&!first.TryStopCurrent()&&NetSession.Active&&ReferenceEquals(replacement,NetSession.EvidenceTransport),"old candidate cannot stop actual replacement playback transport");
            // Exercise ABA explicitly: even restoring the old reference cannot
            // revive a retired lifecycle incarnation. Restore the real owner
            // before disposing anything; never pump the disposed old transport.
            var transport=typeof(NetSession).GetField("_transport",BindingFlags.NonPublic|BindingFlags.Static)!;
            transport.SetValue(null,original);
            try{Check(!first.IsCurrent&&!first.TryStopCurrent(),"same-reference ABA cannot revive an old lifecycle incarnation");}
            finally{transport.SetValue(null,replacement);}
            var second=new NetClientJoinOwnership();second.Capture();
            NetSession.StartServerAuthority(_=>{},()=>{});
            Check(!second.IsCurrent&&!second.TryStopCurrent()&&NetSession.IsServer&&NetSession.EvidenceTransport==null,"client cleanup preserves replacement dedicated authority role");
            for(int rotation=0;rotation<8;++rotation)
            {
                Task.WaitAll(Task.Run(()=>NetSession.StartPlayback()),Task.Run(()=>NetSession.StartServerAuthority(_=>{},()=>{})));
                Check(NetSession.IsClient&&NetSession.EvidenceTransport!=null || NetSession.IsServer&&NetSession.EvidenceTransport==null,"competing lifecycle publications remain atomic "+rotation);
            }
            NetSession.StartPlayback();var owned=new NetClientJoinOwnership();owned.Capture();
            Check(owned.TryStopCurrent()&&!NetSession.Active&&NetSession.EvidenceTransport==null,"current exact candidate retires normally");
            Check(!owned.TryStopCurrent(),"retired candidate cannot stop twice");
            CheckLiveCandidatePlanIdentity(Check);
            Console.WriteLine($"PARTY OWNERSHIP PASS {checks} checks; actual content-free lifecycle, no UDP/backend.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("PARTY OWNERSHIP FAIL "+error);return 1;}
        finally{NetSession.Stop();}
    }
    private static void CheckLiveCandidatePlanIdentity(Action<bool,string> check)
    {
        // Exercise the saved service's real discard method without constructing
        // its unrelated renderer/map-preview dependencies or running HTTP.
        Type servicesType = typeof(NetSession).Assembly.GetType("MphRead.Mods.Launcher.RmlUi.Render.RmlUiLiveMenuServices", true)!;
        object services = RuntimeHelpers.GetUninitializedObject(servicesType);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        servicesType.GetField("_partyJoinGate",instance)!.SetValue(services,new object());
        Type candidateType=servicesType.GetNestedType("PartyJoinCandidate",BindingFlags.NonPublic)!;
        var target=new NativePartyJoinTarget("PP-AAAA-BBBB-CCCC-DDDD-0001","00000001-1111-4111-8111-111111111111",7,
            "00000002-1111-4111-8111-111111111111",3,"00000003-1111-4111-8111-111111111111",
            "203.0.113.7",27015,57,100,"SYNTHETIC ROOM",DateTimeOffset.UtcNow.AddSeconds(60));
        object candidate=Activator.CreateInstance(candidateType,instance,null,new object[]{target,CancellationToken.None},null)!;
        var plan=new LaunchPlan{Kind=LaunchKind.Online,PlayerName="Fixture",Port=27015,Lobby=new("Fixture","203.0.113.7:27015")};
        var stalePlan=new LaunchPlan{Kind=LaunchKind.Online,PlayerName="Fixture",Port=27015,Lobby=new("Fixture","203.0.113.7:27015")};
        check(plan.Equals(stalePlan)&&!ReferenceEquals(plan.Lobby,stalePlan.Lobby),"old/new same-valued plans have different actual attempt witnesses");
        candidateType.GetField("Plan",instance)!.SetValue(candidate,plan);
        candidateType.GetField("Ready",instance)!.SetValue(candidate,true);
        NetSession.StartPlayback();
        var ownership=(NetClientJoinOwnership)candidateType.GetField("Ownership",instance)!.GetValue(candidate)!;
        ownership.Capture();
        FieldInfo field=servicesType.GetField("_partyJoin",instance)!;field.SetValue(services,candidate);
        MethodInfo discard=servicesType.GetMethod("DiscardPartyJoin")!;
        discard.Invoke(services,new object[]{target,stalePlan});
        check(ReferenceEquals(field.GetValue(services),candidate)&&ownership.IsCurrent,
            "actual live service preserves replacement attempt when old DTO reuses exact target and equal-valued plan");
        discard.Invoke(services,new object[]{target,plan});
        check(field.GetValue(services)==null&&!NetSession.Active,
            "actual live service discards only the matching attempt and its captured transport");

        Type auth=typeof(NetSession).Assembly.GetType("MphRead.Mods.Launcher.HunterLicenseClient",true)!;
        FieldInfo authField=auth.GetField("_currentSession",BindingFlags.Static|BindingFlags.NonPublic)!;
        Type sessionType=auth.GetNestedType("AuthSession",BindingFlags.NonPublic)!;
        object? saved=authField.GetValue(null);
        string prefsDirectory=LauncherPrefs.Directory;
        string temporary=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"prime-party-handoff-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temporary);
        var prefs=(LauncherPrefs.PlayerName,LauncherPrefs.ServerAddress,LauncherPrefs.ServerPort,LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.LastKind);
        try
        {
            LauncherPrefs.Directory=temporary;
            object lease=Activator.CreateInstance(sessionType,true)!;
            sessionType.GetProperty("ExpiresAt")!.SetValue(lease,DateTimeOffset.UtcNow.ToUnixTimeSeconds()+3600);
            authField.SetValue(null,lease);
            var acceptedTarget=target with{AccountWitness=lease};
            object accepted=Activator.CreateInstance(candidateType,instance,null,new object[]{acceptedTarget,CancellationToken.None},null)!;
            candidateType.GetField("Plan",instance)!.SetValue(accepted,plan);
            candidateType.GetField("Ready",instance)!.SetValue(accepted,true);
            NetSession.StartPlayback();
            var acceptedOwnership=(NetClientJoinOwnership)candidateType.GetField("Ownership",instance)!.GetValue(accepted)!;
            acceptedOwnership.Capture();field.SetValue(services,accepted);
            authField.SetValue(null,null); // auth retired between AcceptJoin and Note
            servicesType.GetMethod("NotePartyJoined")!.Invoke(services,new object[]{acceptedTarget});
            check(field.GetValue(services)==null&&acceptedOwnership.IsCurrent,
                "accepted handoff detaches even when auth retires at Note boundary");
            servicesType.GetMethod("CancelPartyJoin")!.Invoke(services,null);
            check(acceptedOwnership.IsCurrent,"later native cancellation preserves already accepted transport after auth retirement");
            check(prefs==(LauncherPrefs.PlayerName,LauncherPrefs.ServerAddress,LauncherPrefs.ServerPort,LauncherPrefs.LastHunter,LauncherPrefs.LastColor,LauncherPrefs.LastKind)
                &&!System.IO.File.Exists(System.IO.Path.Combine(temporary,"launcher.txt")),
                "retired auth at handoff writes no endpoint preferences; invalid lease cannot start backend acknowledgement");
        }
        finally
        {
            authField.SetValue(null,saved);LauncherPrefs.Directory=prefsDirectory;
            System.IO.Directory.Delete(temporary,true);
        }

    }

}
