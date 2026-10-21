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
                unqualified=new[]{"physical controls/graphics/audio presentation","imported Trooper presentation","exact15-unit equality headshot boundary; other protection/effectiveness/handicap edge cases","native bot damage/kill","network/replay/career"}},new JsonSerializerOptions{WriteIndented=true}));
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

    internal static int RunNativeBot(string assets,string output,string room="MP3 PROVING GROUND")
    {
        try
        {
            _checks=0;Check(!NetSession.Active&&!DemoPlayback.IsActive,"fresh native-bot offline process");
            Paths.SetPath(Ver.AMHE1,Path.GetFullPath(assets));Paths.MphKey=Ver.AMHE1;
            Check(ServerSim.Available(out var reason),"owned native-bot AMHE1 available "+reason);Headless.Enter();
            using var f=new Fixture(room,bots:1);var p=f.Player;var bot=f.Scene.Players.Items[1];var r=f.Runtime;var state=f.Scene.GameState;
            Check(bot.IsBot&&bot.ModCharacterGameplay==CharacterGameplayRules.Native&&bot.BotLevel==0
                &&p.ModCharacterGameplay==CharacterGameplayRules.TrooperArsenalV1&&!state.Teams,
                "actual mode4 one-native-bot population owns no private AI capability");
            // Synthetic placement ONCE through native Spawn, not a fabricated
            // actor or frozen target. Spawn initializes the real bot AI itself;
            // all later position, form, pickups and controls belong to that AI.
            Check(p.ModTryPirateAim(out var origin,out var direction),"actual native muzzle supplies bot placement axis");
            Vector3 forward=new(direction.X,0,direction.Z);forward.Normalize();
            Vector3 position=default,bodyCenter=default;bool found=false;int candidates=0,noFloor=0,blockedLos=0,blockedBody=0;
            int chosenDegrees=0;float chosenDistance=0,radius=PlayerEntity.PlayerVolumes[(int)bot.Hunter,0].SphereRadius;
            var contacts=new CollisionResult[8];
            foreach(float distance in new[]{5f,4f,3f})
            {
                foreach(int degrees in new[]{0,45,-45,90,-90,135,-135,180})
                {
                    candidates++;float angle=degrees*MathF.PI/180;
                    Vector3 heading=new(forward.X*MathF.Cos(angle)+forward.Z*MathF.Sin(angle),0,
                        forward.Z*MathF.Cos(angle)-forward.X*MathF.Sin(angle));
                    Vector3 candidate=p.Position+heading*distance;var floor=new CollisionResult();
                    if(!CollisionDetection.CheckBetweenPoints(candidate.AddY(5),candidate.AddY(-20),TestFlags.Players,f.Scene,ref floor)
                        ||floor.Plane.Y<=.5f){noFloor++;continue;}
                    candidate.Y=floor.Position.Y-bot.ModBipedBottom+.05f;
                    Vector3 center=candidate.AddY((bot.ModBipedBottom+bot.ModBipedTop)/2);var wall=new CollisionResult();
                    if(CollisionDetection.CheckBetweenPoints(origin,center,TestFlags.Beams,f.Scene,ref wall)){blockedLos++;continue;}
                    Vector3 low=candidate.AddY(bot.ModBipedBottom+radius),high=candidate.AddY(bot.ModBipedTop-radius);
                    bool occupied=CollisionDetection.CheckSphereBetweenPoints(low,high,radius,contacts.Length,false,TestFlags.Players,f.Scene,contacts)>0;
                    foreach(var sample in new[]{low,center,high})
                        occupied|=CollisionDetection.CheckSpherePenetration(sample,radius,contacts.Length,TestFlags.Players,f.Scene,contacts)>0;
                    if(occupied){blockedBody++;continue;}
                    position=candidate;bodyCenter=center;chosenDegrees=degrees;chosenDistance=distance;found=true;break;
                }
                if(found)break;
            }
            Console.WriteLine("PIRATE BLASTER NATIVE BOT PLACEMENT "+JsonSerializer.Serialize(new{found,candidates,bound=24,noFloor,blockedLos,blockedBody,
                chosenDegrees,chosenDistance,radius,position=new[]{position.X,position.Y,position.Z},bodyCenter=new[]{bodyCenter.X,bodyCenter.Y,bodyCenter.Z},
                syntheticSetup="one native Spawn after real floor, body-center LOS and standing capsule clearance queries"}));
            Check(found,"bounded actual floor/body-center LOS/standing-clearance search finds native bot placement");
            Vector3 facing=p.Position-position;facing.Y=0;facing.Normalize();
            bot.Spawn(position,facing,Vector3.UnitY,p.NodeRef,respawn:false);
            Console.WriteLine("PIRATE BLASTER NATIVE BOT SPAWN FACING "+JsonSerializer.Serialize(new{requested=Components(facing),actual=Components(bot.FacingVector),
                towardHuman=Components((p.Position-bot.Position).Normalized()),dot=Vector3.Dot(bot.FacingVector,(p.Position-bot.Position).Normalized()),
                human=Components(p.Position),bot=Components(bot.Position)}));
            Check((bot.Position-position).Length<.0001f,"native non-respawn setup preserves queried standing coordinates without respawn Y offset");
            Console.WriteLine("PIRATE BLASTER NATIVE BOT POST-SPAWN "+JsonSerializer.Serialize(new{human=NativePlayerView(p),bot=NativePlayerView(bot),ai=BotAiView(bot),los=NativeReciprocalLos(f,bot)}));
            Check(bot.IsBot&&bot.ModIsInPlay&&bot.Health>0&&bot.TeamIndex!=p.TeamIndex&&!bot.ModMatchSpawnProtectionActive,
                "public native Spawn preserves live unmodified opposing bot AI");
            int nativeDealt=state.MatchDamageDealt[1],humanTaken=state.MatchDamageTaken[0],nativeHits=state.ShotsHit[1];
            int privateDealt=state.MatchDamageDealt[0],botTaken=state.MatchDamageTaken[1],humanKills=state.Kills[0],botDeaths=state.Deaths[1];
            ulong privateHits=r.Hits;bool nativeBeamSeen=false;int receiveSteps=0,returnSteps=0;
            var trace=new List<object>();var emissions=new List<object>();var priorLive=new Dictionary<BeamProjectileEntity,float>();
            void ObserveNativeBeam()
            {
                var now=new Dictionary<BeamProjectileEntity,float>();
                foreach(var beam in bot.EquipInfo.Beams)
                {
                    if(!ReferenceEquals(beam.Owner,bot)||beam.ModCustomShot!=null||beam.Lifespan<=0||!Contains(f.Scene,beam))continue;
                    nativeBeamSeen=true;now[beam]=beam.Age;
                    if(emissions.Count<12&&(!priorLive.TryGetValue(beam,out float age)||beam.Age<age))
                    {
                        var sample=NativeEmissionView(f,bot,beam,receiveSteps);emissions.Add(sample);
                        Console.WriteLine("PIRATE BLASTER NATIVE EMISSION "+JsonSerializer.Serialize(sample));
                    }
                }
                priorLive=now;
            }
            bool PaidNativeBeam()=>nativeBeamSeen&&state.ShotsHit[1]>nativeHits
                &&state.MatchDamageDealt[1]>nativeDealt&&state.MatchDamageTaken[0]>humanTaken;
            for(;receiveSteps<900&&!PaidNativeBeam()&&p.Health>0&&bot.Health>0;receiveSteps++)
            {
                f.Step(alive:false);ObserveNativeBeam();
                if(receiveSteps%60==0)
                {
                    var sample=new{phase="receive",step=receiveSteps,frame=f.Scene.FrameCount,human=NativePlayerView(p),bot=NativePlayerView(bot),ai=BotAiView(bot),los=NativeReciprocalLos(f,bot),
                        nativeShots=state.ShotsFired[1],nativeHits=state.ShotsHit[1],nativeDamage=state.MatchDamageDealt[1],humanTaken=state.MatchDamageTaken[0]};
                    trace.Add(sample);Console.WriteLine("PIRATE BLASTER NATIVE RECEIVE "+JsonSerializer.Serialize(sample));
                }
            }
            bool receivedNativeBeamDamage=PaidNativeBeam();
            if(p.Health>0&&bot.Health>0)
            {
                var tracker=new NativeMouseTracker(f);
                for(;returnSteps<600&&p.Health>0&&bot.Health>0&&(p.ModPirateUa>0||r.Snapshot.Tails);returnSteps++)
                {
                    tracker.Track(bot);f.Step(new Input(Shoot:true),alive:false);ObserveNativeBeam();
                    if(returnSteps%60==0)trace.Add(new{step=returnSteps,humanHp=p.Health,botHp=bot.Health,ua=p.ModPirateUa,shots=r.Shots,hits=r.Hits,
                        humanPosition=new[]{p.Position.X,p.Position.Y,p.Position.Z},botPosition=new[]{bot.Position.X,bot.Position.Y,bot.Position.Z},botAlt=bot.IsAltForm});
                }
            }
            bool paidPrivateDamage=r.Hits>privateHits&&r.LastImpact is {} impact&&impact.VictimSlot==bot.SlotIndex
                &&state.MatchDamageDealt[0]>privateDealt&&state.MatchDamageTaken[1]>botTaken;
            bool botKill=state.Kills[0]==humanKills+1&&state.Deaths[1]==botDeaths+1;
            var result=new {checks=_checks,room,scope="separate actual reciprocal native AI combat; real admitted bot; one synthetic native Spawn placement; human keyboard/raw-mouse controls",
                receiveBound=900,returnBound=600,receiveSteps,returnSteps,nativeBeamSeen,receivedNativeBeamDamage,
                nativeDamageBeforeReturn=receivedNativeBeamDamage,nativeDamageAnyPhase=PaidNativeBeam(),paidPrivateDamage,botKill,
                nativeBeamHits=state.ShotsHit[1]-nativeHits,nativeDamage=state.MatchDamageDealt[1]-nativeDealt,humanDamageTaken=state.MatchDamageTaken[0]-humanTaken,
                privateDamage=state.MatchDamageDealt[0]-privateDealt,botDamageTaken=state.MatchDamageTaken[1]-botTaken,
                privateShots=r.Shots,privateHits=r.Hits-privateHits,humanHp=p.Health,botHp=bot.Health,
                kills=state.Kills[0]-humanKills,deaths=state.Deaths[1]-botDeaths,impact=ImpactView(r.LastImpact),trace,emissions,
                botAiOverridden=false,botControlsOverridden=false,healthWritten=false,privateCountersWritten=false,
                syntheticPlacement=true,subsequentPositionPinned=false};
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PIRATE BLASTER NATIVE BOT TRACE "+JsonSerializer.Serialize(result));
            Check(receivedNativeBeamDamage,"actual untagged native bot beam pays attributed human damage within bound");
            Check(paidPrivateDamage,"actual private Blaster projectile pays attributed damage to the unmodified native bot");
            Check(botKill,"bounded real mouse/keyboard return fire earns actual native bot kill/death totals");
            Check(bot.IsBot&&bot.ModCharacterGameplay==CharacterGameplayRules.Native&&!r.Available(bot),"reciprocal combat preserves native bot capability fence");
            File.WriteAllText(output,JsonSerializer.Serialize(new {checks=_checks,completed=true,evidence=result},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PIRATE BLASTER NATIVE BOT PASS {_checks} assertions; reciprocal actual damage and native bot kill.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("PIRATE BLASTER NATIVE BOT FAIL "+error);return 1;}
    }
    private sealed class NativeMouseTracker
    {
        private readonly Fixture _fixture;
        private readonly MouseState _mouse;
        private readonly Action<MouseState,Vector2> _set;
        private readonly FieldInfo _canonical=typeof(PlayerEntity).GetField("_gunVec1",Hidden)!;
        private readonly float _yawPerPixel,_pitchPerPixel;
        private static float Yaw(Vector3 v)=>MathF.Atan2(v.X,v.Z);
        private static float Pitch(Vector3 v)=>MathF.Atan2(v.Y,MathF.Sqrt(v.X*v.X+v.Z*v.Z));
        private static float Wrap(float angle){while(angle>MathF.PI)angle-=MathF.Tau;while(angle<-MathF.PI)angle+=MathF.Tau;return angle;}
        private Vector3 Aim=>(Vector3)_canonical.GetValue(_fixture.Player)!;
        private void Move(float x,float y)=>_set(_mouse,_mouse.Position+new Vector2(x,y));
        internal NativeMouseTracker(Fixture fixture)
        {
            _fixture=fixture;var p=fixture.Player;PointerInput.StylusMode=false;PointerInput.Reset();PointerDevice.Reset();
            p.Controls.MouseAim=true;p.Controls.KeyboardAim=false;
            _mouse=(MouseState)typeof(Scene).GetField("_mouseState",Hidden)!.GetValue(fixture.Scene)!;
            _set=typeof(MouseState).GetProperty("Position",Hidden|BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();
            Vector3 before=Aim;Move(2,0);fixture.Step(alive:false);_yawPerPixel=Wrap(Yaw(Aim)-Yaw(before))/2;
            before=Aim;Move(0,2);fixture.Step(alive:false);_pitchPerPixel=(Pitch(Aim)-Pitch(before))/2;
            Check(float.IsFinite(_yawPerPixel)&&float.IsFinite(_pitchPerPixel)&&MathF.Abs(_yawPerPixel)>.00001f&&MathF.Abs(_pitchPerPixel)>.00001f,
                "tracking calibrates actual two-axis raw mouse response without writing aim");
        }
        internal void Track(PlayerEntity target)
        {
            var body=NetHistoricalTrace.CurrentBody(target);Vector3 point=body.Type==HistoricalBodyType.BipedCylinder
                ?body.Position.AddY((body.Bottom+body.Top)/2):body.Position;
            Check(_fixture.Player.ModTryPirateAim(out var origin,out _),"tracking reads actual native muzzle",false);
            // Bounded aiming heuristic only, not a damage oracle. Native target
            // velocity supplies lead; all hit/death proof comes from real contact.
            point+=target.Speed*Math.Clamp((point-origin).Length/2,0,4);
            Vector3 desired=(point-origin).Normalized(),current=Aim;
            float x=Math.Clamp(MathF.Round(Wrap(Yaw(desired)-Yaw(current))/_yawPerPixel),-16,16);
            float y=Math.Clamp(MathF.Round((Pitch(desired)-Pitch(current))/_pitchPerPixel),-16,16);
            Move(x,y);
        }
    }

    private static float[] Components(Vector3 value)=>new[]{value.X,value.Y,value.Z};
    private static object BotAiView(PlayerEntity bot)
    {
        object? Read(string name)=>bot.AiData.GetType().GetField(name,Hidden)?.GetValue(bot.AiData);
        int? Slot(string name)=>(Read(name)as PlayerEntity)?.SlotIndex;
        string? Scalar(string name)=>Read(name)?.ToString();
        return new{nativeTarget=Slot("_targetPlayer"),tacticalTarget=Slot("_tacticalTarget"),reactionTarget=Slot("_botReactionTarget"),
            flags1=bot.AiData.Flags1,flags2=bot.AiData.Flags2.ToString(),flags3=bot.AiData.Flags3.ToString(),flags4=bot.AiData.Flags4.ToString(),
            forceDisable=Scalar("_forceDisable"),state=Scalar("_state"),mode=Scalar("_mode"),shotDelay=Scalar("_shotDelay"),
            tacticalWeapon=Scalar("_tacticalWeapon"),decisionTimer=Scalar("_tacticalDecisionTimer"),
            reactionFrames=Scalar("_botReactionFrames"),suppressedFire=Scalar("_botSuppressFireThisTick")};
    }
    private static object NativePlayerView(PlayerEntity player)
    {
        ushort Read(string name)=>(ushort)typeof(PlayerEntity).GetField(name,Hidden)!.GetValue(player)!;
        return new{slot=player.SlotIndex,hp=player.Health,position=Components(player.Position),speed=Components(player.Speed),
            facing=Components(player.FacingVector),camera=Components(player.CameraInfo.Position),aim=Components((Vector3)typeof(PlayerEntity).GetField("_gunVec1",Hidden)!.GetValue(player)!),
            weapon=player.CurrentWeapon.ToString(),inPlay=player.ModIsInPlay,alt=player.IsAltForm,morphing=player.IsMorphing,unmorphing=player.IsUnmorphing,
            flags1=player.Flags1.ToString(),flags2=player.Flags2.ToString(),load=player.LoadFlags.ToString(),team=player.TeamIndex,
            player.TimeSinceShot,player.RespawnTimer,spawnInvulnerability=Read("_spawnInvulnTimer"),damageInvulnerability=Read("_damageInvulnTimer"),
            freezeTimer=Read("_frozenTimer"),matchSpawnProtection=player.ModMatchSpawnProtectionActive};
    }
    private static object NativeEmissionView(Fixture f,PlayerEntity bot,BeamProjectileEntity beam,int phaseStep)
    {
        var p=f.Player;var body=NetHistoricalTrace.CurrentBody(p);
        Vector3 center=body.Type==HistoricalBodyType.BipedCylinder?body.Position.AddY((body.Bottom+body.Top)/2):body.Position;
        Vector3 toward=center-beam.SpawnPosition;var world=new CollisionResult();
        bool worldBlocked=CollisionDetection.CheckBetweenPoints(beam.SpawnPosition,center,TestFlags.Beams,f.Scene,ref world);
        Vector3 rayEnd=beam.SpawnPosition+beam.Direction*40;
        bool rayHit=NetHistoricalTrace.Intersect(body,beam.SpawnPosition,rayEnd,.1f,out var ray);
        var rayWall=new CollisionResult();bool rayWorldBlocked=CollisionDetection.CheckBetweenPoints(beam.SpawnPosition,rayEnd,TestFlags.Beams,f.Scene,ref rayWall);
        bool segmentHit=NetHistoricalTrace.Intersect(body,beam.BackPosition,beam.Position,.1f,out var segment);
        int beamIndex=(int)beam.Beam;
        return new{phaseStep,frame=f.Scene.FrameCount,beam=beam.Beam.ToString(),beamKind=beam.BeamKind.ToString(),beam.Damage,beam.HeadshotDamage,
            beam.Age,beam.Lifespan,beam.MaxDistance,beam.Homing,beam.Initialized,sceneMember=Contains(f.Scene,beam),flags=beam.Flags.ToString(),position=Components(beam.Position),
            spawn=Components(beam.SpawnPosition),back=Components(beam.BackPosition),direction=Components(beam.Direction),velocity=Components(beam.Velocity),
            directionDotHuman=toward.LengthSquared>.00001f?Vector3.Dot(beam.Direction,toward.Normalized()):0,
            humanBody=new{body.Slot,position=Components(body.Position),body.Bottom,body.Top,body.Radius,type=body.Type.ToString()},
            probeRadius=.1f,worldBlocked,worldHit=worldBlocked?Components(world.Position):null,rayHit,rayFraction=rayHit?ray.Distance:-1,
            segmentHit,segmentFraction=segmentHit?segment.Distance:-1,rayWorldBlocked,
            firstWorldFraction=rayWorldBlocked?(rayWall.Position-beam.SpawnPosition).Length/40:-1,
            effectiveness=beamIndex is >=0 and <9?p.BeamEffectiveness[beamIndex].ToString():"not-native-index",
            human=NativePlayerView(p),bot=NativePlayerView(bot),ai=BotAiView(bot),los=NativeReciprocalLos(f,bot),
            cameraBlocksInput=f.Scene.CameraSequences.Current?.BlockInput??false,suppression=f.Scene.Services.SuppressDamage,privateDefinition=beam.ModCustomDefinition!=null,
            nativeShots=f.Scene.GameState.ShotsFired[1],nativeHits=f.Scene.GameState.ShotsHit[1]};
    }

    private static object NativeReciprocalLos(Fixture f,PlayerEntity bot)
    {
        Vector3 Field(PlayerEntity p,string field)=>(Vector3)typeof(PlayerEntity).GetField(field,Hidden)!.GetValue(p)!;
        var human=f.Player;var botBody=NetHistoricalTrace.CurrentBody(bot);var humanBody=NetHistoricalTrace.CurrentBody(human);
        Vector3 Center(HistoricalBody body)=>body.Type==HistoricalBodyType.BipedCylinder?body.Position.AddY((body.Bottom+body.Top)/2):body.Position;
        bool Blocked(Vector3 start,Vector3 end,TestFlags flags)
        {var result=new CollisionResult();return CollisionDetection.CheckBetweenPoints(start,end,flags,f.Scene,ref result);}
        Vector3 botMuzzle=Field(bot,"_muzzlePos"),botAim=Field(bot,"_aimPosition"),humanMuzzle=Field(human,"_muzzlePos");
        Vector3 botCamera=bot.CameraInfo.Position==Vector3.Zero?bot.Position.AddY(bot.IsAltForm ? .5f : 1f):bot.CameraInfo.Position;
        float humanHeight=human.IsAltForm?Math.Max(.35f,Fixed.ToFloat(human.Values.AltColYPos)):1;
        Vector3 humanCamera=human.CameraInfo.Position==Vector3.Zero?human.Position.AddY(humanHeight):human.CameraInfo.Position;
        return new{botMuzzle=Components(botMuzzle),botAim=Components(botAim),humanMuzzle=Components(humanMuzzle),
            botBodyCenter=Components(Center(botBody)),humanBodyCenter=Components(Center(humanBody)),
            botMuzzleToHumanBody=Blocked(botMuzzle,Center(humanBody),TestFlags.Beams),
            humanMuzzleToBotBody=Blocked(humanMuzzle,Center(botBody),TestFlags.Beams),
            botMuzzleToActualAim=Blocked(botMuzzle,botAim,TestFlags.Beams),
            botCameraToHumanCamera=Blocked(botCamera,humanCamera,TestFlags.None),
            botCameraToHuman06=Blocked(botCamera,human.Position.AddY(humanHeight*.6f),TestFlags.None),
            botCameraToHuman03=Blocked(botCamera,human.Position.AddY(Math.Min(.3f,humanHeight*.3f)),TestFlags.None),
            valuesMeanWorldBlocked=true};
    }

}
