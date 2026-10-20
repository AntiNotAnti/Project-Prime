using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Actual initialized owned AMHE1 Scene, native keyboard/mouse producer and native
// projectile collision/damage. Mode4 bot admission uses real MatchStart actors.
// Damage fixture alone adds an inert Samus after zero-bot admission and places
// it with public native Spawn on an actual clear room floor. That is synthetic
// target setup, not two-human product admission. No projectile, timeline, UA,
// custom source, aim or impact/mark accounting is injected. No GL/media output.
internal static class PirateBlasterSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Action<KeyboardState,Keys,bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private readonly record struct Input(bool Shoot=false,bool Custom=false,bool Native=false,bool Next=false,bool Prev=false,bool Morph=false);
    private static void Check(bool value,string label,bool verbose=true)
    { if(!value)throw new InvalidOperationException(label);_checks++;if(verbose)Console.WriteLine("PIRATE BLASTER SCENE PASS "+label); }
    private static bool Contains(Scene scene,EntityBase entity)
    { foreach(var other in scene.Entities)if(ReferenceEquals(other,entity))return true;return false; }
    private sealed class Fixture : IDisposable
    {
        internal Scene Scene { get; }
        internal PlayerEntity Player=>Scene.Players.Main;
        internal TrooperPirateBlasterRuntime Runtime=>Scene.PirateBlaster??throw new InvalidOperationException("No actual Blaster owner.");
        internal PlayerEntity? Target { get; }
        private KeyboardState Keyboard { get; }
        internal Fixture(string room,int bots=0,bool target=false,bool arsenal=true)
        {
            Rng.SetRng1(8128);Rng.SetRng2(0x8128a5a5);Keyboard=SyntheticInput.CreateKeyboard();Scene? candidate=null;
            try
            {
                candidate=new Scene(new Vector2i(256,192),Keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{},lifetimeOwned:value=>candidate=value);Scene=candidate;
                var plan=new LaunchPlan { Kind=LaunchKind.Offline,Hunter=Hunter.Weavel,RoomKey=room,Mode=GameMode.Battle,
                    Bots=bots,BotLevel=0,IsPlaytest=true,OfflineCharacter=arsenal?OfflineCharacterMode.TrooperArsenalPractice:OfflineCharacterMode.Native,
                    MatchRules=new MatchDefinition { RoomKey=room,Mode=GameMode.Battle,TimeLimitSeconds=0,PointGoal=10000 } };
                plan.MatchRules.ApplyInitialConfiguration(Scene.GameState);MatchStart.AddLocalPlayers(Scene,plan,teamPlay:true);
                Check(OfflineCharacterGameplay.TryBind(Scene,plan,out var reason),"real mode4/native plan admission "+reason);
                Check(Player.ModCharacterGameplay==(arsenal?CharacterGameplayRules.TrooperArsenalV1:CharacterGameplayRules.Native),"exact admitted local gameplay rule");
                if(target){Scene.AddPlayer(Hunter.Samus);Target=Scene.Players.Items[1];Target.IsBot=false;}
                Scene.AddRoom(room,GameMode.Battle,playerCount:target?2:bots+1);Scene.OnLoad();
                var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(Scene,Enum.Parse(mode.FieldType,"All"));
                Scene.GameState.MatchTime=-1;Scene.GameState.PointGoal=10000;
                foreach(var actor in Scene.Players.Items)
                {
                    if(!ReferenceEquals(actor,Player)&&!ReferenceEquals(actor,Target))continue;
                    foreach(var control in actor.Controls.All){control.Type=ButtonType.Key;control.Key=Keys.Unknown;}
                    actor.IgnoreItemPickups=true;
                }
                Player.Controls.Shoot.Key=Keys.Enter;Player.Controls.CustomWeapon.Key=Keys.C;Player.Controls.PowerBeam.Key=Keys.D1;
                Player.Controls.NextWeapon.Key=Keys.N;Player.Controls.PrevWeapon.Key=Keys.P;Player.Controls.Morph.Key=Keys.B;
                Player.Controls.ScrollAllWeapons=true;Player.Controls.MouseAim=false;Player.Controls.KeyboardAim=true;
                SetKey(Keyboard,Keys.Enter,true);
                for(int n=0;n<600&&!Player.LoadFlags.TestFlag(LoadFlags.Spawned);n++)Scene.OnSimulationFrame();
                SetKey(Keyboard,Keys.Enter,false);Check(Player.LoadFlags.TestFlag(LoadFlags.Spawned),"native shoot-to-spawn completed");
                Scene.CameraSequences.Current?.End();for(int n=0;n<(bots==0?240:1);n++)Scene.OnSimulationFrame();
                Check(Player.ModIsInPlay&&Player.Health>0&&!Player.IsBot&&!Player.IsAltForm,"actual initialized living local biped");
                if(arsenal)Check(Runtime.Available(Player)&&Runtime.Selected(Player)&&Player.ModPirateUa==60
                    &&Scene.OfflineWeapons==null&&Scene.CustomCombat==null&&Scene.PrivateCharacterGameplayUnsupported,
                    "native life automatically owns selected private Blaster and one60-UA grant");
                else Check(Scene.PirateBlaster==null&&!Scene.PrivateCharacterGameplayUnsupported,"Native selection allocates no private Blaster");
            }
            catch{if(candidate!=null)Release(candidate);throw;}
        }
        internal void Step(Input input=default,bool alive=true)
        {
            SetKey(Keyboard,Keys.Enter,input.Shoot);SetKey(Keyboard,Keys.C,input.Custom);SetKey(Keyboard,Keys.D1,input.Native);
            SetKey(Keyboard,Keys.N,input.Next);SetKey(Keyboard,Keys.P,input.Prev);SetKey(Keyboard,Keys.B,input.Morph);
            Scene.OnSimulationFrame();
            Check(!NetSession.Active&&float.IsFinite(Player.Position.Y),"finite actual offline step",false);
            if(alive)Check(Player.Health>0&&Player.ModIsInPlay,"step keeps native living owner",false);
        }
        public void Dispose()=>Release(Scene);
    }
    private static void Release(Scene scene)
    {
        var commit=PostMatchCommitContext.Capture(scene.GameState,0,SaveWhen.Never,0);
        try{scene.DoCleanup();}finally{try{scene.UnloadGl();}finally{commit.Commit();commit.Release();ForegroundSceneBinding.TryRelease(commit);}}
    }
    internal static int Run(string assets,string output,string room="MP3 PROVING GROUND")
    {
        try
        {
            _checks=0;Check(!NetSession.Active&&!DemoPlayback.IsActive,"fresh offline non-replay process");
            Paths.SetPath(Ver.AMHE1,Path.GetFullPath(assets));Paths.MphKey=Ver.AMHE1;Check(ServerSim.Available(out var why),"owned AMHE1 available "+why);Headless.Enter();
            var cadence=CadenceAndNativePool(room);var budget=NativeUaBudget(room);var aim=LiveAim(room);
            var selection=SelectionAndNativeFire(room);var cancellation=MorphAndRetirement(room);
            var population=Population(room);var damage=DamageAndMark(room);var staleMark=DamageAndMark(room,staleLife:true);var head=DamageAndMark(room,head:true);
            var funding=NativeUaPickupFunding(room);var modifiers=PerBoltNativeModifiers(room);var cutoff=FarNativeHeadCutoff(room);var totals=NativeGenericKillTotals(room);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output,JsonSerializer.Serialize(new {checks=_checks,room,cadence,budget,aim,selection,cancellation,population,damage,
                staleMark,head,funding,modifiers,cutoff,totals,scope="owned initialized offline mode4 Scene/native controls/projectiles/UA; synthetic inert target placement explicitly separate",
                unqualified=new[]{"physical controls/graphics/audio presentation","imported Trooper presentation","exact15-unit equality headshot boundary","native bot damage/kill","network/replay/career"}},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PIRATE BLASTER SCENE PASS {_checks} assertions; initialized owned offline CPU only.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("PIRATE BLASTER SCENE FAIL "+ex);return 1;}
    }
    private static object CadenceAndNativePool(string room)
    {
        using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;var ordinary=p.EquipInfo.Beams;
        Check(r.Shots==0&&ordinary.All(b=>b.Lifespan<=0),"spawn setup consumed no private shot and ordinary pool is settled");
        var privatePool=r.Beams.ToArray();Check(privatePool.Length==40&&ordinary.All(b=>!privatePool.Contains(b)),"actual private40 pool has no ordinary pool alias");
        ulong before=r.Shots;var emitted=new List<int>();int initial=p.ModPirateUa;
        for(int offset=0;offset<=40;offset++)
        {
            ulong shots=r.Shots;f.Step(new Input(Shoot:offset==0||offset>=40));
            bool due=offset is 0 or 6 or 12 or 40;
            Check(r.Shots-shots==(due?1UL:0UL),"literal0/6/12/40 native emission step"+offset,false);
            if(due)emitted.Add(offset);
            Check(p.ModPirateUa==initial-(offset<40?5:10),"actual shared UA pays5 on first only",false);
            Check(ordinary.All(b=>b.Lifespan<=0&&!ReferenceEquals(b.Owner,p)),"selected Blaster suppresses ordinary native shot allocation",false);
        }
        Check(emitted.SequenceEqual(new[]{0,6,12,40})&&r.Shots-before==4,"released trigger retains paid tails and next burst begins at40");
        return new{emittedOffsets=emitted,uaBefore=initial,uaAfter=p.ModPirateUa,privatePool=40,nativeShotSuppression=true};
    }
    private static object NativeUaBudget(string room)
    {
        using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;var offsets=new List<int>();
        for(int frame=0;frame<480;frame++)
        {
            ulong before=r.Shots;f.Step(new Input(Shoot:true));if(r.Shots!=before)offsets.Add(frame);
        }
        var expected=new List<int>();foreach(int start in new[]{0,40,80,120,160,200,240,280,320,360,400,440})expected.AddRange(new[]{start,start+6,start+12});
        Check(offsets.SequenceEqual(expected)&&r.Shots==36&&p.ModPirateUa==0,"native60-UA bank admits exactly12 bursts/36 native registered bolts");
        ulong shots=r.Shots;f.Step();Check(!r.Selected(p)&&p.CurrentWeapon==BeamType.PowerBeam&&r.Shots==shots,"exhausted bank falls back to native Power Beam");
        return new{initialUa=60,bursts=12,bolts=r.Shots,remainingUa=p.ModPirateUa};
    }
    private static object LiveAim(string room)
    {
        using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;f.Step(new Input(Shoot:true));
        for(int n=1;n<6;n++)f.Step();
        Check(p.ModTryPirateAim(out _,out var oldAim),"actual native muzzle aim before tail");
        PointerInput.StylusMode=false;PointerInput.Reset();PointerDevice.Reset();p.Controls.MouseAim=true;p.Controls.KeyboardAim=false;
        var mouse=(MouseState)typeof(Scene).GetField("_mouseState",Hidden)!.GetValue(f.Scene)!;
        var set=typeof(MouseState).GetProperty("Position",Hidden|BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();
        set(mouse,mouse.Position+new Vector2(16,0));f.Step();
        Check(p.ModTryPirateAim(out _,out var now)&&(now-oldAim).Length>.0001f,"scheduled tail step consumes real changed mouse aim");
        var tail=r.Beams.ToArray().SingleOrDefault(b=>b.ModCustomShot?.Sequence==2&&ReferenceEquals(b.Owner,p));
        Check(tail!=null&&tail.Lifespan>0&&!tail.Flags.TestFlag(BeamFlags.Collided)&&(tail.Direction-now).Length<.0001f,
            "actual second bolt uses CURRENT native muzzle aim, not burst-first aim");
        Check(p.ModPirateUa==55&&r.Shots==2,"aim-changing paid tail emits once without another UA debit");
        Check(tail!.ModCustomShot!.Owner==r.Owner&&tail.ModCustomShot.Identity.Weapon==PirateBlasterDefinition.Definition.Identity,
            "actual native flight retains exact private content and current life source");
        return new{rawMousePixels=16,tailSequence=2,ua=p.ModPirateUa,currentAimQualified=true};
    }
    private static object SelectionAndNativeFire(string room)
    {
        {
            using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;
            f.Step(new Input(Native:true));Check(!r.Selected(p)&&p.CurrentWeapon==BeamType.PowerBeam,"real native Power Beam intent leaves custom selection");
            f.Step(new Input(Shoot:true));Check(p.EquipInfo.Beams.Any(b=>b.Lifespan>0&&ReferenceEquals(b.Owner,p)&&b.ModCustomShot==null),"real native Shoot emits ordinary untagged Power Beam after selection");
            Check(r.Shots==0&&p.ModPirateUa==60,"native shot does not spend Blaster shared UA or author private shot");
            f.Step(new Input(Custom:true));Check(r.Selected(p),"real custom key reselects private Blaster");f.Step();
            f.Step(new Input(Next:true));Check(!r.Selected(p)&&p.CurrentWeapon==BeamType.PowerBeam,"native Next cycle wraps custom to Power Beam");f.Step();
            f.Step(new Input(Prev:true));Check(r.Selected(p),"native Prev cycle returns from Power Beam to private slot");
            }
        using var native=new Fixture(room,arsenal:false);native.Step(new Input(Shoot:true));
        Check(native.Scene.PirateBlaster==null&&native.Player.EquipInfo.Beams.Any(b=>b.Lifespan>0&&ReferenceEquals(b.Owner,native.Player)),"Native Weavel remains ordinary native fire owner");
        return new{nativeFire=true,cycleBothDirections=true,nativeModeUnaffected=true};
    }
    private static object MorphAndRetirement(string room)
    {
        using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;f.Step(new Input(Shoot:true));ulong shots=r.Shots;
        f.Step(new Input(Morph:true));Check(p.Controls.Morph.IsPressed&&!r.Snapshot.Tails&&r.Snapshot.Cooldown==39,"actual Morph cancels paid tails while retaining remaining39 of native40 wait");
        for(int n=0;n<20;n++)f.Step();Check(r.Shots==shots&&p.ModPirateUa==55,"native alt transition emits no deferred Blaster tail/refund");
        var flights=r.Beams.ToArray();f.Scene.GameState.EnhancedHunters=true;
        Check(p.ModCharacterGameplay==CharacterGameplayRules.Native&&!r.Available(p)
            &&flights.All(b=>b.Owner==null&&b.Lifespan<=0&&!Contains(f.Scene,b)),"actual owner invalidation expires/unregisters all private flights immediately");
        Check(f.Scene.PrivateCharacterGameplayUnsupported,"owner retirement keeps private replay exclusion latched");
        f.Scene.GameState.EnhancedHunters=false;f.Step();Check(r.Shots==shots&&!r.Available(p),"restored settings cannot revive retired private runtime");
        return new{morphCancelsTails=true,waitPreserved=true,ownerRetirement=true,replayExclusionLatched=true};
    }
    private static object Population(string room)
    {
        var rows=new List<object>();for(int bots=0;bots<=7;bots++)
        {
            using var f=new Fixture(room,bots:bots);var s=f.Scene;var seen=new bool[bots+1];
            for(int step=0;step<600;step++)
            {
                for(int slot=0;slot<=bots;slot++)seen[slot]|=s.Players.Items[slot].LoadFlags.TestFlag(LoadFlags.Spawned);
                if(seen.All(v=>v))break;s.OnSimulationFrame();
            }
            Check(s.Players.PlayerCount==bots+1&&s.Players.PlayersCreated==bots+1&&!s.GameState.Teams,"actual mode4 configured native population/FFA bots"+bots);
            for(int slot=0;slot<s.Players.Items.Count;slot++)
            {
                var p=s.Players.Items[slot];Check(p.LoadFlags.TestFlag(LoadFlags.Active)==(slot<=bots),"actual active prefix/inactive remainder",false);
                if(slot>bots)continue;Check(seen[slot]&&p.TeamIndex==slot,"actual native spawn with unique FFA slot",false);
                if(slot>0)Check(p.IsBot&&p.ModCharacterGameplay==CharacterGameplayRules.Native&&!f.Runtime.Available(p),"real native bot never receives local private capability",false);
            }
            rows.Add(new{bots,spawned=seen,realMatchStart=true});
        }
        return rows;
    }
    private static void PlaceTargetAndAim(Fixture f,bool head=false,float distance=5)
    {
        var p=f.Player;var target=f.Target!;Check(p.ModTryPirateAim(out var origin,out var aim),"actual live muzzle supplies synthetic placement direction");
        Vector3 horizontal=new(aim.X,0,aim.Z);horizontal.Normalize();Vector3 at=p.Position+horizontal*distance;
        var floor=new CollisionResult();Check(CollisionDetection.CheckBetweenPoints(at.AddY(5),at.AddY(-20),TestFlags.Players,f.Scene,ref floor)&&floor.Plane.Y>.5f,"actual clear-floor target placement query");
        at.Y=floor.Position.Y-target.ModBipedBottom+.05f;
        var obstruction=new CollisionResult();Check(!CollisionDetection.CheckBetweenPoints(origin,at.AddY(.1f),TestFlags.Beams,f.Scene,ref obstruction),"native room LOS reaches actual body fixture");
        target.Spawn(at,-horizontal,Vector3.UnitY,p.NodeRef,respawn:false);target.IsBot=false;target.TeamIndex=p.TeamIndex+1;
        Check(target.ModIsInPlay&&target.Health==99&&!target.ModMatchSpawnProtectionActive,"public native Spawn places inert opposing target without protection or HP accounting writes");
        PointerInput.StylusMode=false;PointerInput.Reset();PointerDevice.Reset();p.Controls.MouseAim=true;p.Controls.KeyboardAim=false;
        var mouse=(MouseState)typeof(Scene).GetField("_mouseState",Hidden)!.GetValue(f.Scene)!;
        var set=typeof(MouseState).GetProperty("Position",Hidden|BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();
        static float Pitch(Vector3 d)=>MathF.Atan2(d.Y,MathF.Sqrt(d.X*d.X+d.Z*d.Z));
        float before=Pitch(aim);set(mouse,mouse.Position+new Vector2(0,4));f.Step();
        Check(p.ModTryPirateAim(out origin,out aim),"native mouse calibration produced live aim");float radiansPerPixel=(Pitch(aim)-before)/4;
        Check(MathF.Abs(radiansPerPixel)>.00001f,"real mouse pitch has usable response");bool aligned=false;int steps=0;
        for(;steps<24;steps++)
        {
            Check(p.ModTryPirateAim(out origin,out aim),"read native current aim for real control setup",false);
            Vector3 desired=(target.Position.AddY(head?target.ModBipedTop-.1f:.1f)-origin).Normalized();float error=Pitch(desired)-Pitch(aim);
            if(MathF.Abs(error)<.005f){aligned=true;break;}
            float pixels=Math.Clamp(MathF.Round(error/radiansPerPixel),-16,16);if(pixels==0)pixels=MathF.Sign(error/radiansPerPixel);
            set(mouse,mouse.Position+new Vector2(0,pixels));f.Step();
        }
        Console.WriteLine("PIRATE BLASTER TARGET SETUP "+JsonSerializer.Serialize(new{aligned,steps,targetPosition=new[]{target.Position.X,target.Position.Y,target.Position.Z},aim=new[]{aim.X,aim.Y,aim.Z},radiansPerPixel,syntheticPlacement=true,realMouseAim=true}));
        Check(aligned,"bounded real mouse pitch aims at actual requested native body/head band");
    }
    private static object? ImpactView(CustomImpactFact? fact) => fact is {} f ? new {
        weapon=f.Weapon.ToString(), projectile=f.Source.Sequence, owner=f.Source.Owner,
        f.VictimSlot, f.Tick, f.Damage, f.TurretDamage, f.HitTurret, f.Headshot, f.Lethal,
        point=new[]{f.Point.X,f.Point.Y,f.Point.Z}
    } : null;
    private static object DamageAndMark(string room,bool staleLife=false,bool head=false)
    {
        using var f=new Fixture(room,target:true);var p=f.Player;var target=f.Target!;var r=f.Runtime;PlaceTargetAndAim(f,head);
        int before=target.Health;ulong shots=r.Shots,hits=r.Hits;f.Step(new Input(Shoot:true));
        for(int n=0;n<60&&r.Hits-hits<3;n++)f.Step();
        Console.WriteLine("PIRATE BLASTER DAMAGE TRACE "+JsonSerializer.Serialize(new{healthBefore=before,healthAfter=target.Health,shots=r.Shots-shots,hits=r.Hits-hits,impact=ImpactView(r.LastImpact),marked=r.IsMarked(p,target),frame=f.Scene.FrameCount}));
        Check(r.Shots-shots==3&&r.Hits-hits==3&&before-target.Health==(head?48:36),"three actual distinct native bolts pay literal body12/head16 each");
        Check(r.LastImpact is {} impact&&impact.Damage==(head?16:12)&&impact.Headshot==head&&impact.Source.Weapon==PirateBlasterDefinition.Definition.Identity,
            "actual collision fact identifies private content and native requested body/head damage");
        Check(r.IsMarked(p,target)&&r.Snapshot.Marks==1,"three paid distinct native bolts to same life create exactly one local mark");
        if(staleLife)
        {
            Check(f.Scene.MatchEvents.TryReadOfflineLife(f.Scene,target,out var oldMatch,out var oldLife),"actual marked victim life is readable");
            target.Spawn(target.Position,target.FacingVector,Vector3.UnitY,target.NodeRef,respawn:true);target.IsBot=false;
            Check(f.Scene.MatchEvents.TryReadOfflineLife(f.Scene,target,out var newMatch,out var newLife)
                &&oldMatch==newMatch&&oldLife!=newLife&&!r.IsMarked(p,target),
                "synthetic public native respawn changes victim life and immediately invalidates stale mark");
            return new{staleLifeInvalidated=true,actualNativeRespawn=true};
        }
        ulong markedAt=r.LastImpact!.Value.Tick;
        for(int n=0;n<240&&f.Scene.FrameCount<markedAt+239;n++){f.Step();Check(r.IsMarked(p,target),"native mark remains through239 source steps",false);}
        Check(f.Scene.FrameCount==markedAt+239,"bounded mark fixture reaches actual source age239");
        f.Step();Check(!r.IsMarked(p,target)&&r.Snapshot.Marks==0,"native mark expires at exact240 source-step boundary");
        return new{damage=head?48:36,headshot=head,distinctHits=3,markSteps=240,syntheticTarget=true,realMouseAim=true,targetNotPinned=true};
    }
    private static object NativeUaPickupFunding(string room)
    {
        using var f=new Fixture(room);var p=f.Player;var r=f.Runtime;
        f.Step(new Input(Shoot:true));for(int n=1;n<=40;n++)f.Step();
        Check(p.ModPirateUa==55&&r.Shots==3,"pickup control spent actual native UA5 on one complete burst");
        var item=ItemSpawnEntity.SpawnItem(ItemType.UASmall,p.Position,p.NodeRef,6000,f.Scene)
            ??throw new InvalidOperationException("Native UA-small factory refused fixture item.");
        p.IgnoreItemPickups=false;try{for(int n=0;n<8&&item.DespawnTimer!=0;n++)f.Step();}finally{p.IgnoreItemPickups=true;}
        int funded=Math.Min(105,(int)p.Values.MpAmmoCap);
        Check(item.DespawnTimer==0&&p.ModPirateUa==funded&&r.Snapshot.Ua==funded,
            "actual native UASmall contact adds literal50 to the SAME Blaster UA bank under native cap");
        f.Step(new Input(Shoot:true));Check(p.ModPirateUa==funded-5&&r.Shots==4,"next actual first bolt spends pickup-funded native UA5");
        return new{syntheticNativeItemPlacement=true,nativeContact=true,before=55,pickup=50,funded,afterFirst=p.ModPirateUa};
    }
    private static object PerBoltNativeModifiers(string room)
    {
        using var f=new Fixture(room,target:true);var p=f.Player;var target=f.Target!;var r=f.Runtime;PlaceTargetAndAim(f);
        int hp=target.Health;var damages=new Dictionary<uint,int>();var flightDamage=new Dictionary<uint,float>();
        ItemInstanceEntity? doubleItem=null;
        try
        {
            for(int offset=0;offset<60&&r.Hits<3;offset++)
            {
                // Explicit synthetic native match modifier only. No private
                // runtime field, damage amount or source provenance is written.
                if(offset==6)f.Scene.GameState.PrimeHunter=p.SlotIndex;
                if(offset==7)
                {
                    doubleItem=ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage,p.Position,p.NodeRef,6000,f.Scene)
                        ??throw new InvalidOperationException("Native Double pickup factory refused fixture item.");
                    p.IgnoreItemPickups=false;
                }
                if(offset==12)Check(p.DoubleDamage&&doubleItem?.DespawnTimer==0,
                    "actual native Double pickup contact completes before paid third-bolt source sample");
                f.Step(new Input(Shoot:offset==0));
                if(doubleItem?.DespawnTimer==0)p.IgnoreItemPickups=true;
                foreach(var beam in r.Beams)if(beam.ModCustomShot is {} source&&ReferenceEquals(beam.Owner,p))
                    flightDamage.TryAdd(source.Sequence,beam.Damage);
                if(r.LastImpact is {} fact)damages.TryAdd(fact.Source.Sequence,fact.Damage);
            }
            Console.WriteLine("PIRATE BLASTER PER-BOLT MODIFIER TRACE "+JsonSerializer.Serialize(new{damages,flightDamage,hpBefore=hp,hpAfter=target.Health,p.DoubleDamage,prime=p.IsPrimeHunter,nativeDoublePickup=doubleItem?.DespawnTimer==0}));
            Check(damages.Count==3&&damages.GetValueOrDefault(1u)==12&&damages.GetValueOrDefault(2u)==18&&damages.GetValueOrDefault(3u)==24,
                "actual consecutive contact damage samples none12 / native Prime18 / native Double24 per bolt");
            Check(hp-target.Health==54&&r.Hits==3&&r.Shots==3&&p.ModPirateUa==55,
                "native per-bolt modifier contacts pay12+18+24 once with one shared UA5 cost");
            return new{actualDamage=new[]{12,18,24},nativeDoublePickup=true,syntheticNativePrimeFlag=true,privateStateWritten=false};
        }
        finally{p.IgnoreItemPickups=true;f.Scene.GameState.PrimeHunter=-1;}
    }
    private static object FarNativeHeadCutoff(string room)
    {
        using var f=new Fixture(room,target:true);var p=f.Player;var target=f.Target!;var r=f.Runtime;
        Check(p.ModTryPirateAim(out var origin,out var aim),"far head fixture observes actual native muzzle");
        Vector3 horizontal=new(aim.X,0,aim.Z);horizontal.Normalize();Vector3 candidate=p.Position+horizontal*18;
        var floor=new CollisionResult();
        if(!CollisionDetection.CheckBetweenPoints(candidate.AddY(5),candidate.AddY(-20),TestFlags.Players,f.Scene,ref floor)||floor.Plane.Y<=.5f)
            return new{qualified=false,reason="actual18-unit target candidate has no native standing floor"};
        candidate.Y=floor.Position.Y-target.ModBipedBottom+.05f;var wall=new CollisionResult();
        if(CollisionDetection.CheckBetweenPoints(origin,candidate.AddY(target.ModBipedTop-.1f),TestFlags.Beams,f.Scene,ref wall))
            return new{qualified=false,reason="actual18-unit head candidate is blocked by owned-room geometry"};
        PlaceTargetAndAim(f,head:true,distance:18);int hp=target.Health;f.Step(new Input(Shoot:true));
        var actual=r.Beams.ToArray().FirstOrDefault(b=>b.ModCustomShot?.Sequence==1&&ReferenceEquals(b.Owner,p));
        Check(actual!=null,"far head control captures actual first native flight");Vector3 spawned=actual!.SpawnPosition;
        f.Step(new Input(Native:true)); // real selection cancels paid tails; existing native flight remains authoritative
        for(int n=0;n<40&&r.Hits==0;n++)f.Step();
        var impact=r.LastImpact;Console.WriteLine("PIRATE BLASTER FAR HEAD TRACE "+JsonSerializer.Serialize(new{impact=ImpactView(impact),hpBefore=hp,hpAfter=target.Health,spawned=new[]{spawned.X,spawned.Y,spawned.Z}}));
        Check(impact is {} hit&&hit.Point.Y-target.Position.Y>=target.ModBipedTop-.3f
            &&(hit.Point-spawned).Length>15&&!hit.Headshot&&hit.Damage==12&&hp-target.Health==12,
            "real high-band contact beyond native15-unit range pays body12 rather than head16");
        return new{qualified=true,syntheticNativeTargetDistance=18,headCutoff=15,actualBodyDamage=12};
    }
    private static object NativeGenericKillTotals(string room)
    {
        using var f=new Fixture(room,target:true);var p=f.Player;var target=f.Target!;var r=f.Runtime;PlaceTargetAndAim(f);
        var state=f.Scene.GameState;int hp=target.Health;
        int fired=state.ShotsFired[0],hits=state.ShotsHit[0],dealt=state.MatchDamageDealt[0],taken=state.MatchDamageTaken[target.SlotIndex];
        int kills=state.Kills[0],deaths=state.Deaths[target.SlotIndex],points=state.Points[0];
        var aliases=Enumerable.Range(0,9).Select(i=>state.BeamKills[0,i]).ToArray();
        for(int step=0;step<180&&target.Health>0;step++)f.Step(new Input(Shoot:true));
        Console.WriteLine("PIRATE BLASTER GENERIC TOTALS TRACE "+JsonSerializer.Serialize(new{hpStart=hp,hpEnd=target.Health,r.Shots,r.Hits,fired=state.ShotsFired[0]-fired,hits=state.ShotsHit[0]-hits,dealt=state.MatchDamageDealt[0]-dealt,taken=state.MatchDamageTaken[target.SlotIndex]-taken,kills=state.Kills[0]-kills,deaths=state.Deaths[target.SlotIndex]-deaths,points=state.Points[0]-points,nativeBeamKills=Enumerable.Range(0,9).Select(i=>state.BeamKills[0,i]-aliases[i]).ToArray()}));
        Check(target.Health==0&&hp==99&&r.Shots==9&&r.Hits==9,"nine actual body bolts resolve native99-HP victim death with lethal final clamp");
        Check(state.ShotsFired[0]-fired==9&&state.ShotsHit[0]-hits==9&&state.MatchDamageDealt[0]-dealt==99
            &&state.MatchDamageTaken[target.SlotIndex]-taken==99,"actual generic shot/hit/damage totals count nine unique contacts and99 paid damage exactly once");
        Check(state.Kills[0]-kills==1&&state.Deaths[target.SlotIndex]-deaths==1&&state.Points[0]-points==1,
            "actual generic native Battle kill/death/point totals resolve one private Blaster kill");
        Check(Enumerable.Range(0,9).All(i=>state.BeamKills[0,i]==aliases[i]),"private kill never aliases any native BeamKills bucket");
        return new{actualShots=9,actualHits=9,paidDamage=99,kills=1,deaths=1,points=1,nativeBeamAlias=false};
    }

}
