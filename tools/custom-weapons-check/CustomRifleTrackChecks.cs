using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure cosmetic clocks and rigid transform contracts. Launch/result values are
// explicit typed fixtures, not authenticated network or actual emitted shots.
internal static class CustomRifleTrackChecks
{
    private static readonly WeaponDefinition Definition=RiftlineM7.Create();
    private static readonly WeaponDigest Manifest=new CustomWeaponManifest(new[]{Definition}).Digest;
    private static readonly WeaponOwnerContext Owner=new(3,0x0102030405060708,0,7,9);
    private static readonly WorldBootstrapIdentity Bootstrap=new(new(Owner.MatchId,Owner.AuthorityEpoch,2),4,Owner.Generation,100,3,5);
    private static readonly CustomRifleContext Online=new(Owner,Bootstrap,Manifest,Definition.Identity);
    private static readonly CustomRifleContext Offline=new(Owner,default,default,Definition.Identity,true);
    private static WeaponStateSnapshot State(CustomRifleContext context,uint shot=0,uint tick=100,CustomWeaponPhase phase=CustomWeaponPhase.Ready,uint reloadEnd=0)
        =>new(context.Owner,context.Weapon,tick,phase,4,16,0,shot==0?0:unchecked(tick+75),reloadEnd,shot,shot==0?0:tick);
    private static CustomReplicaLaunch Launch(uint sequence,WorldBootstrapIdentity? bootstrap=null,WeaponDigest? manifest=null,
        byte slot=0,ushort life=9,ushort weapon=10,CustomWeaponActionKind kind=CustomWeaponActionKind.Fire)
    {
        bool fire=kind==CustomWeaponActionKind.Fire;
        var action=new CustomWeaponAction(bootstrap??Bootstrap,manifest??Manifest,slot,life,new(weapon),sequence,101,
            fire?100u:0,0,kind,0,fire?Vector3.UnitY:Vector3.Zero,fire?Vector3.UnitZ:Vector3.Zero);
        return new(new(action,101,fire?100u:101,0,Vector3.Zero,Vector3.UnitZ,true,false,false),102,Vector3.UnitY,Vector3.UnitZ);
    }
    private static CustomPresentationResult Result(uint sequence,bool accepted,CustomRifleContext? context=null)
    {var c=context??Online;return new(c.Bootstrap,c.Manifest,c.Owner,sequence,CustomWeaponActionKind.Fire,
        accepted?WeaponActionResult.Accepted:WeaponActionResult.Cooldown,accepted);}
    private static bool Finite(CustomRiflePose p)=>float.IsFinite(p.Kick)&&float.IsFinite(p.Bolt)&&float.IsFinite(p.Magazine)
        &&float.IsFinite(p.Raise)&&float.IsFinite(p.Flash)&&p.Kick is >=0 and <=1&&p.Bolt is >=0 and <=1
        &&p.Magazine is >=0 and <=1&&p.Raise is >=0 and <=1&&p.Flash is >=0 and <=1;
    private static bool Near(float a,float b)=>MathF.Abs(a-b)<.00001f;
    private static bool Near(Vector3 a,Vector3 b)=>(a-b).LengthSquared<.0000001f;
    private static CustomRifleTrack New(CustomRifleContext? context=null,uint shot=0,bool selected=true)
    {var c=context??Online;var t=new CustomRifleTrack();t.Synchronize(c,State(c,shot),selected,138,c.Offline);return t;}
    internal static void Run(Action<bool,string> Check)
    {
        int count=0;void Verify(bool pass,string message){Check(pass,"M7 cosmetic track/mount: "+message);count++;}
        Identity(Verify);PredictionAndFacts(Verify);TimingAndReload(Verify);ScenePhaseFence(Verify);Mounts(Verify);Allocation(Verify);
        Console.WriteLine($"M7 track/mount pure checks passed: {count}; cosmetic fixtures only, no actual rig/render/audio acceptance claim.");
    }
    private static void Identity(Action<bool,string> check)
    {
        check(Online.IsValid&&Offline.IsValid,"full online identity and explicit offline identity accepted");
        foreach(var invalid in new[]{default(CustomRifleContext),Online with{Owner=default},Online with{Bootstrap=default},
            Online with{Manifest=default},Online with{Bootstrap=Bootstrap with{Start=Bootstrap.Start with{MatchId=4}}},
            Online with{Bootstrap=Bootstrap with{Start=Bootstrap.Start with{AuthorityEpoch=2}}},
            Online with{Weapon=WeaponIdentity.Native(BeamType.Imperialist)},
            Online with{Weapon=WeaponIdentity.Custom(Guid.Parse("10000000-0000-0000-0000-000000000001"),Definition.Identity.DefinitionHash!)},
            Offline with{Bootstrap=Bootstrap},Offline with{Manifest=Manifest},Online with{Offline=true}})
        {
            var track=New();track.Submitted(Online,1,true);
            check(!invalid.IsValid&&track.Synchronize(invalid,State(invalid),true,138,invalid.Offline)==0
                &&track.Context==default&&!track.Selected&&track.Sample(0).Kick==0,"invalid context clears prior cosmetic identity and prediction");
        }
        var wrongOwner=new WeaponOwnerContext(Owner.MatchId,Owner.AuthorityEpoch,1,Owner.Generation,Owner.Life);
        var t=New();t.Synchronize(Online,State(Online) with{Owner=wrongOwner},true,138,false);
        check(t.Context==default&&!t.Selected,"state cannot cross owner even when context valid");
        t=New();t.Synchronize(Online,State(Online) with{Weapon=WeaponIdentity.Native(BeamType.Imperialist)},true,138,false);
        check(t.Context==default,"state weapon cannot substitute native fallback identity");
        t=New();t.Synchronize(Online,State(Online),true,138,true);
        check(t.Context==default,"online context cannot be interpreted as offline");
        t=New(Offline);check(!t.AcceptedLaunch(Launch(1)),"offline track refuses online launch events");
        var remote=Online with{Bootstrap=Bootstrap with{SlotGeneration=88}};
        t=New(remote);check(remote.IsValid&&t.AcceptedLaunch(Launch(1)),"recipient generation may differ from original source owner generation");
        foreach(var next in new[]{Online with{Bootstrap=Bootstrap with{Revision=5}},
            Online with{Manifest=WeaponDigest.Read(Enumerable.Repeat((byte)17,32).ToArray())},
            Online with{Weapon=WeaponIdentity.Custom(RiftlineM7.ContentId,new string('a',64))},
            Online with{Owner=new(Owner.MatchId,Owner.AuthorityEpoch,Owner.Slot,Owner.Generation,10)}})
        {
            t=New();t.Submitted(Online,1,true);var cues=t.Synchronize(next,State(next,1),true,138,false);
            check(cues==0&&t.Context==next&&t.Sample(0).Kick==0&&!t.Submitted(Online,2,true),
                "bootstrap/manifest/new-life transition silently resets old prediction and old producer context");
        }
    }
    private static void PredictionAndFacts(Action<bool,string> check)
    {
        var t=New(shot:5);check(t.Sample(0).Kick==0&&t.Sample(0).Flash==0
            &&!t.AcceptedLaunch(Launch(5))&&!t.AcceptedLaunch(Launch(4)),"baseline past shots restore silently and reject old/reordered launch facts");
        int shotCues=0;t=New();if(t.Submitted(Online,1,true))shotCues++;
        check(shotCues==1&&!t.Submitted(Online,1,true)&&!t.Submitted(Online,2,false)&&t.Sample(0).Kick==1,"prediction starts one cue only after permitted new submission");
        check(!t.Apply(Result(1,true)),"acceptance confirms prediction without requesting voice cancellation");if(t.AcceptedLaunch(Launch(1)))shotCues++;
        check(shotCues==1&&!t.AcceptedLaunch(Launch(1)),"accepted result and matching reliable launch cannot replay predicted shot cue");
        check(t.AcceptedLaunch(Launch(3))&&!t.AcceptedLaunch(Launch(2))&&!t.AcceptedLaunch(Launch(3)),"new accepted launch cues once; duplicates and reordered older sequences drop");
        var before=t.Sample(0);
        foreach(var foreign in new[]{Launch(4,manifest:WeaponDigest.Read(Enumerable.Repeat((byte)23,32).ToArray())),
            Launch(4,slot:1),Launch(4,life:10),Launch(4,weapon:11),Launch(4,kind:CustomWeaponActionKind.Reload),
            Launch(4,bootstrap:Bootstrap with{Start=Bootstrap.Start with{StartGeneration=3}}),
            Launch(4,bootstrap:Bootstrap with{SlotGeneration=8})})
            check(!t.AcceptedLaunch(foreign)&&t.Sample(0)==before,"foreign owner/manifest/runtime/kind/source stream cannot restart current pulse");
        var invalid=Launch(4);invalid=invalid with{Source=invalid.Source with{Action=invalid.Source.Action with{Life=0}}};
        check(!t.AcceptedLaunch(default)&&!t.AcceptedLaunch(invalid)&&t.Sample(0)==before,"default/malformed typed launch refuses without throwing or changing pulse");
        var sourceReplacement=Launch(4,bootstrap:Bootstrap with{Revision=Bootstrap.Revision+1});
        check(!t.AcceptedLaunch(sourceReplacement),"bound original source bootstrap cannot be replaced inside same track");
        t=New();t.Submitted(Online,1,true);t.Step();t.Submitted(Online,2,true);before=t.Sample(0);
        check(!t.Apply(Result(1,false))&&t.Sample(0)==before,"older rejected prediction cannot cancel a newer prediction or voice");
        check(!t.Apply(Result(2,false,Online with{Owner=new(Owner.MatchId,Owner.AuthorityEpoch,0,Owner.Generation,10)}))
            &&t.Sample(0)==before,"stale-life rejection cannot cancel current-life prediction or voice");
        check(t.Apply(Result(2,false))&&t.Sample(0).Kick==0&&t.Sample(0).Flash==0,"matching rejected prediction cancels its pulse and voice exactly once");
        check(!t.Apply(Result(2,false)),"duplicate rejection cannot cancel a voice again");
        t=New();t.Submitted(Online,1,true);check(t.AcceptedLaunch(Launch(2)),"new accepted fact can supersede older predicted shot");
        before=t.Sample(0);
        check(!t.Apply(Result(1,false))&&t.Sample(0)==before&&before.Kick==1,"late denial of superseded prediction cannot cancel newer accepted launch or voice");
        t=New();t.Submitted(Online,1,true);t.Synchronize(Online,State(Online),false,138,false);
        check(!t.Selected&&t.Sample(0).Kick==0&&!t.Submitted(Online,2,true)&&t.Step()==0,"selection loss stops active shot/bolt and refuses new prediction");
        check(t.Synchronize(Online,State(Online,1),true,138,false)==0&&!t.AcceptedLaunch(Launch(1)),"reselection cannot replay old accepted sequence");
        t.Reset();check(t.Context==default&&!t.Selected&&t.Step()==0&&t.Sample(1)==new CustomRiflePose(0,0,0,0,0),"reset clears all presentation clocks and identity");
    }
    private static void TimingAndReload(Action<bool,string> check)
    {
        var t=New();t.Submitted(Online,1,true);
        check(Near(t.Sample(.5f).Kick,1-.5f/6)&&Near(t.Sample(.5f).Flash,1-.5f/3),"fractional draw sampling interpolates kick/flash without simulation advance");
        check(t.Sample(-1)==t.Sample(0)&&t.Sample(2)==t.Sample(1)&&t.Sample(float.NaN)==t.Sample(0)
            &&t.Sample(float.PositiveInfinity)==t.Sample(0),"draw alpha clamps finite range and neutralizes nonfinite input");
        var fixedBefore=t.Sample(0);for(int i=0;i<1000;i++)t.Sample((i%5)/4f);
        check(t.Sample(0)==fixedBefore,"repeated rendering does not consume fixed-step clock");
        int bolts=0;
        for(int age=1;age<=76;age++)
        {
            var cue=t.Step();if((cue&CustomRifleCueMask.Bolt)!=0)bolts++;
            check(cue==(age==16?CustomRifleCueMask.Bolt:CustomRifleCueMask.None)&&Finite(t.Sample(.5f)),"only fixed bolt step16 cues; bounded pose age "+age);
        }
        check(bolts==1&&t.Sample(0).Kick==0&&t.Sample(0).Bolt==0&&t.Sample(0).Flash==0,"shot animation expires after fixed75 steps");
        t=New();t.Submitted(Online,1,true);for(int i=0;i<8;i++)t.Step();
        check(Near(t.Sample(.5f).Bolt,.5f/8),"bolt motion interpolates between fixed positions");
        var reload=State(Online,tick:120,phase:CustomWeaponPhase.Reloading,reloadEnd:258);
        t=New();check(t.Synchronize(Online,reload,true,138,false)==CustomRifleCueMask.Reload
            &&t.Synchronize(Online,reload,true,138,false)==0,"actual reload phase transition cues exactly once");
        check(t.Sample(0).Magazine==0,"new reload begins at start pose");
        for(int i=0;i<28;i++)check(t.Step()==0,"reload step never invents another reload/bolt cue "+i);
        check(t.Sample(0).Magazine==1,"magazine reaches withdrawn plateau through fixed reload steps");
        var baseline=new CustomRifleTrack();var half=reload with{Tick=189};
        check(baseline.Synchronize(Online,half,true,138,false)==0&&baseline.Sample(0).Magazine==1,
            "late reload baseline projects existing progress silently");
        check(t.Synchronize(Online,reload with{Phase=CustomWeaponPhase.Ready,ReloadCompleteTick=0},true,138,false)==0
            &&t.Sample(0).Magazine==0,"reload completion removes magazine overlay without cue");
        check(t.Synchronize(Online,reload with{ReloadCompleteTick=259},true,138,false)==CustomRifleCueMask.Reload,
            "distinct subsequent reload deadline gets one new transition");
        foreach(int duration in new[]{int.MinValue,0,1,138,3600,int.MaxValue})
        {
            int bounded=Math.Clamp(duration,1,3600);var r=State(Online,tick:uint.MaxValue-4,phase:CustomWeaponPhase.Reloading,reloadEnd:unchecked(uint.MaxValue-4+(uint)bounded));
            var fresh=new CustomRifleTrack();check(fresh.Synchronize(Online,r,true,duration,false)==0&&Finite(fresh.Sample(.5f)),"reload duration clamp and uint-wrap baseline remain finite "+duration);
        }
        var offline=New(Offline,shot:7);
        check(offline.Synchronize(Offline,State(Offline,8),true,138,true)==CustomRifleCueMask.Shot
            &&offline.Synchronize(Offline,State(Offline,8),true,138,true)==0,"offline canonical shot high-water creates exactly one new cue");
    }
    private static void ScenePhaseFence(Action<bool,string> check)
    {
        // Declared CPU scene/actor/HUD seam: exercise the actual scene dispatch
        // methods without assets, GL, an audio object or an audio device. This
        // proves cue-clock ordering/cancellation, not actual morph simulation.
        check(!NetSession.Active,"scene phase fixture begins without a live session");
        if(NetSession.Active)return;
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var sync=typeof(Scene).GetMethod("SynchronizeCustomRifleTrack",hidden)!;
        var feedback=typeof(Scene).GetMethod("StepCustomWeaponFeedback",hidden)!;
        var step=typeof(Scene).GetMethod("StepCustomRifleTracks",hidden)!;
        var tracks=typeof(Scene).GetField("_customRifleTracks",hidden)!;
        var audio=typeof(Scene).GetField("_customRifleAudio",hidden)!;
        var flags=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!;
        var headless=typeof(Headless).GetProperty(nameof(Headless.Active))!;
        bool oldHeadless=Headless.Active;var oldGame=GameState.Current;
        var oldPlayers=PlayerEntity.LegacyRegistry;var oldRandom=Rng.Current;
        Scene? scene=null;
        try
        {
            headless.SetValue(null,false);
            scene=new Scene(new Vector2i(256,192),SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{},initializeRuntime:false);
            var player=scene.Players.Main;player.Health=100;
            void Synchronize(CustomRifleContext context,bool selected)
            {
                var hud=new CustomWeaponHudState(selected,true,State(context),0,4);
                sync.Invoke(scene,new object?[]{player,hud,null,false});
            }
            CustomRifleTrack Seed()
            {
                flags.SetValue(player,(PlayerFlags1)0);player.Health=100;
                ((CustomRifleTrack?[]?)tracks.GetValue(scene))?[0]?.Reset();
                Synchronize(Offline,true);
                var track=((CustomRifleTrack?[])tracks.GetValue(scene)!)[0]!;
                if(!track.Submitted(Offline,1,true))throw new InvalidOperationException("Scene cosmetic prediction fixture refused.");
                for(int age=0;age<15;age++)track.Step();
                return track;
            }
            foreach(var form in new[]{PlayerFlags1.Morphing,PlayerFlags1.AltForm,PlayerFlags1.Unmorphing})
            {
                var track=Seed();var before=track.Sample(0);flags.SetValue(player,form);
                feedback.Invoke(scene,null);
                check(track.Sample(0)==before,"feedback phase does not advance dueBolt before current form is synchronized "+form);
                Synchronize(Offline,true);step.Invoke(scene,null);
                check(track.Context==default&&!track.Selected&&track.Sample(0)==default
                    &&audio.GetValue(scene)==null,"current form resets dueBolt before scene cue step without audio object "+form);
            }
            var dying=Seed();player.Health=0;feedback.Invoke(scene,null);Synchronize(Offline,true);step.Invoke(scene,null);
            check(dying.Context==default&&!dying.Selected&&dying.Sample(0)==default,"current death resets pending scene cue before stepping");
            var deselected=Seed();Synchronize(Offline,false);step.Invoke(scene,null);
            check(!deselected.Selected&&deselected.Sample(0).Bolt==0,"current selection loss suppresses dueBolt");
            var previous=Seed();var next=Offline with{Owner=new(Owner.MatchId,Owner.AuthorityEpoch,Owner.Slot,Owner.Generation,10)};
            Synchronize(next,true);step.Invoke(scene,null);
            check(previous.Context==next&&previous.Selected&&previous.Sample(0).Bolt==0&&!previous.Submitted(Offline,2,true),
                "new-life scene baseline cancels old bolt but keeps new selection eligible");
            check(previous.Submitted(next,1,true),"first valid new-life prediction remains allowed after baseline synchronization");
            check(audio.GetValue(scene)==null,"CPU scene transition fixture never creates an audio owner or device");
        }
        finally
        {
            if(scene!=null)typeof(Scene).GetMethod("ReleaseCustomRifleModel",hidden)!.Invoke(scene,null);
            headless.SetValue(null,oldHeadless);GameState.Current=oldGame;
            PlayerEntity.LegacyRegistry=oldPlayers;Rng.Current=oldRandom;
        }
        check(Headless.Active==oldHeadless&&ReferenceEquals(GameState.Current,oldGame)
            &&ReferenceEquals(PlayerEntity.LegacyRegistry,oldPlayers)&&ReferenceEquals(Rng.Current,oldRandom),
            "scene transition fixture restores foreground role/game/player/RNG facades");
    }
    private static void Mounts(Action<bool,string> check)
    {
        using var stream=typeof(CustomRifleModel).Assembly.GetManifestResourceStream(CustomRifleModel.Resource)!;
        using var bytes=new MemoryStream();stream.CopyTo(bytes);var model=CustomRifleModel.DecodeVerified(bytes.ToArray());
        var socket=model.Muzzle;float[] scales={.45f,.45f,.48f,.43f,.48f,.50f,.46f};
        Vector3[] tips={new(.63f,-.02f,0),new(.6f,0,0),new(1.2f,0,-.2f),new(.765f,-.05f,-.16f),new(1.11f,.15f,0),new(.71f,0,0),new(.89f,.15f,-.172f)};
        var anchor=Matrix4.CreateRotationZ(.37f)*Matrix4.CreateRotationY(-.24f)*Matrix4.CreateTranslation(2,3,4);var original=anchor;
        var pose=new CustomRiflePose(.5f,.25f,.75f,.4f,.1f);
        for(int hunter=0;hunter<7;hunter++)foreach(bool first in new[]{false,true})
        {
            check(CustomRifleMounts.TryTransform((Hunter)hunter,first,.7f,Matrix4.Identity,socket,pose,.6f,out var local,out var muzzle),"seven-Hunter first/third-person rigid attachment exists "+hunter+"/"+first);
            var expected=first?new Vector3(0,.1f-.4f*.2f-.6f*.2f,.7f+.18f-.5f*.06f):tips[hunter]+new Vector3(.18f,.18f,0);
            check(Near(muzzle,expected)&&Near(Matrix.Vec3MultMtx4(socket,local),expected),"actual model socket transformed exactly onto declared native tip "+hunter+"/"+first);
            var axis=Matrix.Vec3MultMtx4(socket+Vector3.UnitZ,local)-muzzle;
            check(Near(axis,(first?Vector3.UnitZ:Vector3.UnitX)*(scales[hunter]*(first?.5f:1))),"authored +Z has correct forward orientation and positive scale "+hunter+"/"+first);
            check(CustomRifleMounts.TryTransform((Hunter)hunter,first,.7f,anchor,socket,pose,.6f,out var world,out var worldMuzzle)
                &&Near(worldMuzzle,Matrix.Vec3MultMtx4(expected,anchor))&&Near(Matrix.Vec3MultMtx4(socket,world),worldMuzzle)
                &&anchor==original,"parent rigid rotation/translation is respected and never rewritten "+hunter+"/"+first);
        }
        // Independently calculated short-native-tip muzzle Z values from the
        // authored -0.61 rear extent and socket1.42, with half-size FP scales.
        float[] shortTips={.65675f,.65675f,.6872f,.63645f,.6872f,.7075f,.6669f};
        for(int hunter=0;hunter<7;hunter++)
        {
            check(CustomRifleMounts.TryTransform((Hunter)hunter,true,0,Matrix4.Identity,socket,default,0,out _,out var shortMuzzle)
                &&Near(shortMuzzle,new(0,.1f,shortTips[hunter])),"short native tip uses bounded receiver-clearance origin "+hunter);
            foreach(float nativeTip in new[]{-1f,0f,.7f,99f})for(int lod=0;lod<2;lod++)
            {
                bool transformed=CustomRifleMounts.TryTransform((Hunter)hunter,true,nativeTip,Matrix4.Identity,socket,default,0,out var mount,out _);
                float nearest=float.PositiveInfinity;
                foreach(var box in model.Lod(lod))for(int corner=0;corner<8;corner++)
                {
                    var vertex=box.Center+new Vector3((corner&1)==0?-box.Half.X:box.Half.X,
                        (corner&2)==0?-box.Half.Y:box.Half.Y,(corner&4)==0?-box.Half.Z:box.Half.Z);
                    nearest=MathF.Min(nearest,Matrix.Vec3MultMtx4(vertex,mount).Z);
                }
                check(transformed&&float.IsFinite(nearest)&&nearest>=.20f-.00001f,
                    "every actual FP LOD vertex stays at least.20 forward without kick/raise "+hunter+"/"+nativeTip+"/"+lod);
            }
        }
        foreach(var badPose in new[]{pose with{Kick=float.NaN},pose with{Bolt=float.PositiveInfinity},pose with{Magazine=float.NaN},pose with{Raise=float.NegativeInfinity},pose with{Flash=float.NaN}})
            check(!CustomRifleMounts.TryTransform(Hunter.Samus,true,.7f,anchor,socket,badPose,0,out var absent,out var absentMuzzle)
                &&absent==Matrix4.Identity&&absentMuzzle==default,"all nonfinite cosmetic components refuse with neutral outputs");
        var nanAnchor=anchor;nanAnchor.M14=float.NaN;
        foreach(var hunter in new[]{Hunter.Guardian,Hunter.Random,(Hunter)255})
            check(!CustomRifleMounts.TryTransform(hunter,false,0,anchor,socket,pose,0,out _,out _),"unsupported Hunter attachment refuses "+hunter);
        check(!CustomRifleMounts.TryTransform(Hunter.Samus,false,0,nanAnchor,socket,pose,0,out _,out _)
            &&!CustomRifleMounts.TryTransform(Hunter.Samus,false,float.NaN,anchor,socket,pose,0,out _,out _)
            &&!CustomRifleMounts.TryTransform(Hunter.Samus,true,0,anchor,new(float.NaN,0,0),pose,0,out _,out _)
            &&!CustomRifleMounts.TryTransform(Hunter.Samus,true,0,anchor,socket,pose,float.NaN,out _,out _),"nonfinite anchor/socket/tip/scope refuses");
        var extremes=new CustomRiflePose(99,0,0,-99,0);
        check(CustomRifleMounts.TryTransform(Hunter.Samus,true,99,Matrix4.Identity,socket,extremes,99,out _,out var clamped)
            &&Near(clamped,new(0,-.1f,2.12f)),"finite tip/kick/raise/scope inputs clamp to bounded first-person offsets");
    }
    private static void Allocation(Action<bool,string> check)
    {
        var t=New();t.Submitted(Online,1,true);var socket=new Vector3(0,.04f,1.42f);var anchor=Matrix4.Identity;
        for(int i=0;i<1000;i++){var pose=t.Sample(.5f);CustomRifleMounts.TryTransform(Hunter.Samus,true,.7f,anchor,socket,pose,.5f,out _,out _);}
        var beforePose=t.Sample(0);long before=GC.GetAllocatedBytesForCurrentThread();bool okay=true;
        for(int i=0;i<100000;i++)
        {
            var pose=t.Sample((i%5)/4f);okay&=Finite(pose)
                &&CustomRifleMounts.TryTransform((Hunter)(i%7),(i&1)==0,.7f,anchor,socket,pose,.5f,out _,out _);
        }
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(okay&&allocated==0&&t.Sample(0)==beforePose,"100k warmed draw samples/mounts allocate zero and advance no clocks");
        for(int i=1;i<=16;i++)check(t.Step()==(i==16?CustomRifleCueMask.Bolt:CustomRifleCueMask.None),"heavy draw sampling does not move bolt cue from fixed step16 "+i);
    }
}
