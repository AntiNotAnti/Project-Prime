using System;
using System.Linq;
using System.Collections.Generic;
using MphRead.Mods.Render;
using OpenTK.Windowing.Desktop;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class NativeTimedGrantPresentationTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool ok,string name) { _checks++; if(!ok) throw new InvalidOperationException(name); }
    private static object Field(PlayerEntity p,string name) => typeof(PlayerEntity).GetField(name,Hidden)!.GetValue(p)!;
    private static void Set(PlayerEntity p,string name,object value)
    { var field=typeof(PlayerEntity).GetField(name,Hidden)!; field.SetValue(p,Convert.ChangeType(value,field.FieldType)); }
    private static void Session(string name,object? value) => typeof(NetSession).GetProperty(name)!.SetValue(null,value);
    private static PickupClaim Claim(ShotKey life,TimedPowerUp type,uint id=1)
        => new(new(life.AuthorityEpoch,life.MatchId,life.ShooterSlot,life.Generation,life.LifeId,id),
            new(life.AuthorityEpoch,life.MatchId,1,10,id),type,NetSession.NetFrame,Vector3.Zero);
    private static TimedGrantFact Grant(ShotKey life,TimedPowerUp type,uint revision=1,uint contact=100)
        => new(life,1,revision,type,revision,10,revision,contact,contact+1800,TimedGrantEnd.Active,contact);
    private static void Project(PlayerEntity p,bool quiet=false)
        => p.ModProjectTimedGrants(NetGrantNetwork.Presentation,ShotKey.For(p.SlotIndex,0),NetSession.NetFrame,quiet);
    private static List<BeamProjectileEntity> Beams(Scene scene)
    {
        var beams=new List<BeamProjectileEntity>();
        foreach(var entity in scene.Entities) if(entity is BeamProjectileEntity beam) beams.Add(beam);
        return beams;
    }
    private static float Fire(PlayerEntity p)
    {
        p.ModArmWeapon(BeamType.PowerBeam); p.EquipInfo.ChargeLevel=0;
        Set(p,"_timeSinceShot",1000); Set(p,"_muzzlePos",p.Position+new Vector3(0,1,0));
        Set(p,"_aimPosition",p.Position+new Vector3(0,1,10));
        var before=Beams(p.OwningScene).ToHashSet();
        Check((bool)typeof(PlayerEntity).GetMethod("TryFireWeapon",Hidden)!.Invoke(p,null)!,"native firing emits fixture beam");
        var beam=Beams(p.OwningScene).Single(b=>!before.Contains(b)&&b.Owner==p);
        float damage=beam.Damage; beam.Destroy(); p.OwningScene.RemoveEntity(beam); return damage;
    }
    private static int Bomb(PlayerEntity p)
    {
        bool before=NetBombs.Admitting;
        try
        {
            NetBombs.Admitting=true; Set(p,"_bombAmmo",3);
            var bomb=(BombEntity?)typeof(PlayerEntity).GetMethod("SpawnBomb",Hidden)!.Invoke(p,null);
            Check(bomb!=null,"native fixture bomb emitted"); int damage=bomb!.Damage;
            bomb.Destroy(); p.OwningScene.RemoveEntity(bomb); return damage;
        }
        finally { NetBombs.Admitting=before; }
    }
    private static void DamageProjection(PlayerEntity p)
    {
        var life=ShotKey.For(p.SlotIndex,0); NetGrantNetwork.Reset(); Project(p,true);
        float beamBase=Fire(p); int bombBase=Bomb(p);
        Check(beamBase>0&&bombBase>0,"native baseline damage denominators positive");
        Check(NetGrantNetwork.Presentation.Predict(Claim(life,TimedPowerUp.DoubleDamage),100),"owner provisional gain admitted");
        Project(p);
        Check((ushort)Field(p,"_doubleDmgTimer")==1800&&!p.DoubleDamage,"provisional native visual timer never confirms damage property");
        Check(Fire(p)==beamBase&&Bomb(p)==bombBase,"provisional double leaves actual beam and bomb damage unchanged");
        Set(p,"_doubleDamageIconTimer",.75f); Project(p);
        Check((float)Field(p,"_doubleDamageIconTimer")==.75f,"Keep preserves native HUD icon animation phase");
        Check(NetGrantNetwork.Presentation.Accept(Grant(life,TimedPowerUp.DoubleDamage),100),"matching authority grant confirms");
        Project(p); Check(!p.DoubleDamage,"confirmed contact frame is visual only before next input");
        Session(nameof(NetSession.NetFrame),101u); Project(p);
        Check(p.DoubleDamage,"confirmed double authorizes subsequent native input");
        Check(Fire(p)==beamBase*2&&Bomb(p)==bombBase*2,"confirmed double changes actual native beam and bomb damage");
        Set(p,"_doubleDamageIconTimer",.5f); Project(p,true);
        Check((float)Field(p,"_doubleDamageIconTimer")==.5f,"quiet Keep does not restart HUD phase");
    }
    private static void CloakAndDeathAlt(PlayerEntity p)
    {
        Session(nameof(NetSession.NetFrame),100u);
        var life=ShotKey.For(p.SlotIndex,0); NetGrantNetwork.Reset();
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(p,p.Flags2&~PlayerFlags2.Cloaking);
        Set(p,"_cloakTimer",80); Project(p,true);
        Check((ushort)Field(p,"_cloakTimer")==80&&!p.Flags2.TestFlag(PlayerFlags2.Cloaking),"absent timed grant preserves passive camouflage timer");
        NetGrantNetwork.Presentation.Predict(Claim(life,TimedPowerUp.Cloak),100); Project(p);
        Check(p.Flags2.TestFlag(PlayerFlags2.Cloaking)&&(ushort)Field(p,"_cloakTimer")==1800,"provisional cloak projects timed flag and timer");
        var cloakClaim=Claim(life,TimedPowerUp.Cloak);
        NetGrantNetwork.Presentation.Settle(new(cloakClaim,PickupResult.Taken,100,100,0,0,0),100); Project(p);
        Check(!p.Flags2.TestFlag(PlayerFlags2.Cloaking)&&(ushort)Field(p,"_cloakTimer")==0,"denial ends only timed cloak branch");
        Set(p,"_cloakTimer",60); Project(p,true);
        Check((ushort)Field(p,"_cloakTimer")==60,"subsequent projection leaves resumed passive buildup alone");
        p.ModForceForm(false); NetGrantNetwork.Reset(); Project(p,true);
        var deathClaim=Claim(life,TimedPowerUp.DeathAlt); NetGrantNetwork.Presentation.Predict(deathClaim,100); Project(p);
        Check(p.IsAltForm||p.IsMorphing,"initial Death Alt gain enters native morph once");
        uint formFrame=(uint)Field(p,"_modFormStartedFrame"); var speed=p.Speed;
        NetGrantNetwork.Presentation.Accept(Grant(life,TimedPowerUp.DeathAlt),100); Project(p); Project(p,true);
        Check((uint)Field(p,"_modFormStartedFrame")==formFrame&&p.Speed==speed,"Retime and Resume do not repeat native form entry impulse");
        NetGrantNetwork.Reset(); Project(p,true);
        Check((ushort)Field(p,"_deathaltTimer")==0&&(p.IsAltForm||p.IsMorphing),"end releases Death Alt without forcing unmorph");
        p.ModForceForm(false); NetGrantNetwork.Presentation.Accept(Grant(life,TimedPowerUp.DeathAlt),100); Project(p,true);
        Check(!p.IsAltForm&&!p.IsMorphing,"quiet reconstructed Death Alt defers form to authoritative form stream");
    }
    private static void ItemIdentity(PlayerEntity p)
    {
        Session(nameof(NetSession.NetFrame),100u); NetPickupNetwork.Reset(); NetGrantNetwork.Reset();
        var scene=p.OwningScene; ItemSpawnEntity? template=null;
        foreach(var candidate in scene.GetItemSpawnEntities()) { template=candidate; break; }
        Check(template!=null,"native map supplies authored spawner schema");
        object data=template!.Data;
        void Data(string name,object value)=>typeof(ItemSpawnEntityData).GetField(name)!.SetValue(data,value);
        Data(nameof(ItemSpawnEntityData.Header),new EntityDataHeader(template.Data.Header.Type,30002,
            p.Position,Vector3.UnitY,Vector3.UnitZ));
        Data(nameof(ItemSpawnEntityData.ItemType),ItemType.DoubleDamage); Data(nameof(ItemSpawnEntityData.Enabled),(byte)1);
        Data(nameof(ItemSpawnEntityData.ParentId),-1); Data(nameof(ItemSpawnEntityData.NotifyEntityId),(short)-1);
        Data(nameof(ItemSpawnEntityData.CollectedMessage),Message.None); Data(nameof(ItemSpawnEntityData.SpawnDelay),(ushort)0);
        Data(nameof(ItemSpawnEntityData.SpawnInterval),(ushort)1); Data(nameof(ItemSpawnEntityData.MaxSpawnCount),(ushort)2);
        var spawner=new ItemSpawnEntity((ItemSpawnEntityData)data,"",scene); scene.AddEntity(spawner); spawner.Process();
        Check(spawner.Item==null,"client native spawner cannot create timed item without authority incarnation");
        var life=ShotKey.For(p.SlotIndex,0);
        var key=new PowerUpSpawnKey(life.AuthorityEpoch,life.MatchId,1,spawner.Id,1);
        var fact=new PickupItemFact(key,TimedPowerUp.DoubleDamage,100,true,p.Position);
        Check(NetPickupNetwork.ReceiveItem(fact),"authority birth fact admitted"); spawner.Process();
        var first=spawner.Item ?? throw new InvalidOperationException("Authority fact did not materialize native item.");
        Check(first.ModPickupSpawn==key&&first.Owner==spawner,"authority fact creates exact authored incarnation");
        int despawn=first.DespawnTimer; var quota=spawner.ModHealthState.SpawnCount;
        NetPickupNetwork.OwnerContact(p,first); spawner.Process();
        uint sequence=NetGrantNetwork.Presentation.View(life,TimedPowerUp.DoubleDamage,100).PredictionSequence;
        Check(sequence>0&&first.ModPickupHidden&&ReferenceEquals(spawner.Item,first)
            &&first.DespawnTimer==despawn&&spawner.ModHealthState.SpawnCount==quota,
            "pending visual hide preserves live owner reference lifetime and native quota");
        var claim=new PickupClaim(new(life.AuthorityEpoch,life.MatchId,p.SlotIndex,life.Generation,life.LifeId,sequence),
            key,TimedPowerUp.DoubleDamage,100,p.Position);
        Check(NetGrantNetwork.Presentation.Settle(new(claim,PickupResult.Taken,100,100,0,0,0),100),"exact provisional refusal reconciles");
        spawner.Process(); Check(!first.ModPickupHidden&&ReferenceEquals(spawner.Item,first)&&first.DespawnTimer==despawn,
            "refusal unhides same still-available entity without re-creation");
        var granted=new TimedGrantFact(life,1,1,TimedPowerUp.DoubleDamage,1,spawner.Id,1,100,1900,TimedGrantEnd.Active,100);
        Check(NetGrantNetwork.Presentation.Accept(granted,100),"grant arrives before availability tombstone");
        spawner.Process(); Check(first.ModPickupHidden&&ReferenceEquals(spawner.Item,first),"grant-first ordering keeps consumed incarnation hidden");
        Check(NetPickupNetwork.ReceiveItem(fact with { Available=false }),"authority tombstone admitted"); spawner.Process();
        Check(first.DespawnTimer==0&&spawner.Item==null,"tombstone retires authored incarnation");
        var replacement=fact with { Spawn=key with { Generation=2 },AuthorityFrame=101 };
        Check(NetPickupNetwork.ReceiveItem(replacement),"replacement incarnation admitted"); spawner.Process();
        var second=spawner.Item;
        Check(second!=null&&!ReferenceEquals(first,second)&&second.ModPickupSpawn==replacement.Spawn&&!second.ModPickupHidden,
            "replacement incarnation creates distinct visible entity");
        first.Process(); Check(ReferenceEquals(spawner.Item,second),"retired old item processing cannot clear new owner item");
        Check(ItemSpawnEntity.SpawnItemDrop(ItemType.DoubleDamage,p.Position,p.NodeRef,100,scene)==null,
            "native client timed drop factory cannot invent dynamic identity");
        var dynamicA=fact with { Spawn=key with { SpawnId=-1,Generation=100 },AuthorityFrame=102 };
        var dynamicB=dynamicA with { Spawn=dynamicA.Spawn with { Generation=101 },Position=p.Position+Vector3.UnitX };
        Check(NetPickupNetwork.ReceiveItem(dynamicA)&&NetPickupNetwork.ReceiveItem(dynamicB),"two dynamic incarnation facts admitted");
        TimedPickupProjection.ApplyDynamic(scene);
        var dynamic=new List<ItemInstanceEntity>();
        foreach(var item in scene.GetItemInstanceEntities())
            if(item.Owner==null&&item.ModPickupSpawn is { } identity&&(identity==dynamicA.Spawn||identity==dynamicB.Spawn)&&item.DespawnTimer!=0)
                dynamic.Add(item);
        Check(dynamic.Count==2&&dynamic[0].ModPickupSpawn!=dynamic[1].ModPickupSpawn,"same -1 spawn ID materializes two distinct generations");
        TimedPickupProjection.ApplyDynamic(scene);
        var again=new List<ItemInstanceEntity>();
        foreach(var item in scene.GetItemInstanceEntities())
            if(item.Owner==null&&item.ModPickupSpawn is { } identity&&(identity==dynamicA.Spawn||identity==dynamicB.Spawn)&&item.DespawnTimer!=0)
                again.Add(item);
        Check(again.Count==2&&again.All(dynamic.Contains),"repeat dynamic projection never creates duplicates");
    }

    private static void RoamingOwnerDeathAlt(PlayerEntity p)
    {
        var scene=p.OwningScene;
        var camera=typeof(Scene).GetField("_cameraMode",Hidden)!;
        var previous=camera.GetValue(scene);
        try
        {
            NetGrantNetwork.Reset(); Session(nameof(NetSession.NetFrame),100u);
            p.ModForceForm(false); Project(p,true);
            camera.SetValue(scene,CameraMode.Roam);
            Check(!p.IsMainPlayer&&p.SlotIndex==NetSession.LocalSlot,"roaming camera preserves local owner identity");
            var life=ShotKey.For(p.SlotIndex,0);
            Check(NetGrantNetwork.Presentation.Predict(Claim(life,TimedPowerUp.DeathAlt),100),"roaming owner predicts Death Alt");
            Project(p);
            Check(p.IsAltForm||p.IsMorphing,"local Death Alt prediction morphs while camera roams");
            uint started=(uint)Field(p,"_modFormStartedFrame"); var speed=p.Speed;
            Project(p);
            Check((uint)Field(p,"_modFormStartedFrame")==started&&p.Speed==speed,
                "roaming owner repeated projection does not repeat morph impulse");
        }
        finally
        {
            camera.SetValue(scene,previous); NetGrantNetwork.Reset(); Project(p,true); p.ModForceForm(false);
        }
    }

    private static void DeadProjection(PlayerEntity p)
    {
        NetGrantNetwork.Reset(); Session(nameof(NetSession.NetFrame),100u);
        var life=ShotKey.For(p.SlotIndex,0);
        foreach(var type in new[] { TimedPowerUp.DoubleDamage,TimedPowerUp.Cloak,TimedPowerUp.DeathAlt })
            Check(NetGrantNetwork.Presentation.Accept(Grant(life,type),100),"death fixture has confirmed timed ownership");
        Session(nameof(NetSession.NetFrame),101u); Project(p,true);
        Check(p.DoubleDamage&&p.Flags2.TestFlag(PlayerFlags2.Cloaking)&&(ushort)Field(p,"_deathaltTimer")>0,
            "death fixture projects all three active native effects");
        var ownership=NetGrantNetwork.Presentation.View(life,TimedPowerUp.DoubleDamage,101);
        int health=p.Health;
        try
        {
            // A replicated death can precede its grant-ending delta. Local
            // presentation must become dead immediately despite stale live truth.
            p.Health=0; Project(p,true); Project(p,true);
            Check(!p.DoubleDamage&&!p.ModConfirmedTimedEffect(TimedPowerUp.DeathAlt)
                &&(ushort)Field(p,"_doubleDmgTimer")==0&&(ushort)Field(p,"_deathaltTimer")==0
                &&!p.Flags2.TestFlag(PlayerFlags2.Cloaking)&&(ushort)Field(p,"_cloakTimer")==0,
                "dead actor clears native timed state even before delayed end fact");
            Check(NetGrantNetwork.Presentation.View(life,TimedPowerUp.DoubleDamage,101)==ownership,
                "death presentation does not rewrite authoritative ownership model");
        }
        finally { p.Health=health; NetGrantNetwork.Reset(); Project(p,true); }
    }

    private static void DynamicSamePositionTwins(PlayerEntity owner)
    {
        var scene = owner.OwningScene;
        var oldRole = NetSession.Role;
        bool oldAuthority = NetSession.IsAuthority;
        int oldLocal = NetSession.LocalSlot;
        uint oldFrame = NetSession.NetFrame;
        var oldServer = NetSession.ServerSession;
        var oldSend = NetPickupNetwork.SendClaim;
        var oldFailure = NetPickupNetwork.ClientFailure;
        ItemInstanceEntity? authorityA = null, authorityB = null;
        var projected = new List<ItemInstanceEntity>();
        var delivered = new List<PickupItemFact>();
        var offered = new List<PickupClaim>();
        int failures = 0;
        try
        {
            NetPowerUpLedger.Reset();
            Session(nameof(NetSession.Role), NetRole.Server);
            Session(nameof(NetSession.IsAuthority), true);
            Session(nameof(NetSession.LocalSlot), -1);
            Session(nameof(NetSession.NetFrame), 500u);
            var peer = NetPickupNetwork.Join(); // starts before genuine births
            authorityA = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage, owner.Position,
                owner.NodeRef, 6000, scene);
            authorityB = ItemSpawnEntity.SpawnItem(ItemType.DoubleDamage, owner.Position,
                owner.NodeRef, 6000, scene);
            Check(authorityA != null && authorityB != null, "real authority twin factories succeed");
            Check(NetPowerUpLedger.TrySpawnKey(authorityA!, out var keyA), "native A exact key retained");
            Check(NetPowerUpLedger.TrySpawnKey(authorityB!, out var keyB), "native B exact key retained");
            Check(keyA.SpawnId == -1 && keyB.SpawnId == -1 && keyA != keyB
                && authorityA!.Position == authorityB!.Position, "same-position native twins have distinct full keys");
            Check(NetReliableChannel.IsReliable(PacketType.PickupItem)
                && NetReliableChannel.IsReliable(PacketType.PickupClaim)
                && NetReliableChannel.IsReliable(PacketType.PickupVerdict), "existing pickup transport registration is reliable");
            bool DrainDiscovery(PacketType packet, ReadOnlySpan<byte> bytes)
            {
                Check(packet == PacketType.PickupItem && bytes.Length % PickupItemFact.Size == 0,
                    "native birth/removal delivery uses existing item fact batches");
                for (int at = 0; at < bytes.Length; at += PickupItemFact.Size)
                {
                    Check(PickupItemFact.TryRead(bytes.Slice(at, PickupItemFact.Size), out var fact),
                        "actual publisher bytes decode");
                    delivered.Add(fact);
                }
                return true;
            }
            Check(NetPickupNetwork.Pump(peer, 0, DrainDiscovery)
                && delivered.Count == 2 && NetPickupNetwork.PeerPending(peer) == 0,
                "both native birth facts drain once through retained admission");
            var birthA = delivered.Single(f => f.Spawn == keyA);
            var birthB = delivered.Single(f => f.Spawn == keyB);
            Check(birthA.Available && birthB.Available && birthA.Position == birthB.Position,
                "discovery preserves both same-position incarnations");
            // Keep original instances registered for genuine later destruction,
            // but remove them from the scene before switching this fixture to client.
            scene.RemoveEntity(authorityA!); scene.RemoveEntity(authorityB!);
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false);
            Session(nameof(NetSession.LocalSlot), owner.SlotIndex);
            Session(nameof(NetSession.ServerSession), null);
            NetPickupNetwork.ClientFailure = _ => failures++;
            Check(NetSession.WorldIsReady && owner.ModIsInPlay, "declared local owner contact scope is ready");
            Check(NetPickupNetwork.ReceiveItem(birthA) && NetPickupNetwork.ReceiveItem(birthB),
                "two real authority birth facts enter owner discovery");
            Check(!NetPickupNetwork.ReceiveItem(birthA), "duplicate native birth is a no-op");
            TimedPickupProjection.ApplyDynamic(scene);
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.Owner == null && item.ModPickupSpawn is { } key
                    && (key == keyA || key == keyB) && item.DespawnTimer != 0) projected.Add(item);
            Check(projected.Count == 2 && projected.Select(i => i.ModPickupSpawn).Distinct().Count() == 2,
                "same-position discovery materializes two distinct native instances");
            TimedPickupProjection.ApplyDynamic(scene);
            int liveTwins = 0;
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.ModPickupSpawn is { } key && (key == keyA || key == keyB)
                    && item.DespawnTimer != 0) liveTwins++;
            Check(liveTwins == 2,
                "duplicate projection does not merge or duplicate twins");
            var clientA = projected.Single(i => i.ModPickupSpawn == keyA);
            var clientB = projected.Single(i => i.ModPickupSpawn == keyB);
            NetPickupNetwork.SendClaim = claim => { offered.Add(claim); return true; };
            NetPickupNetwork.OwnerContact(owner, clientA);
            NetPickupNetwork.OwnerContact(owner, clientB);
            NetPickupNetwork.OwnerContact(owner, clientA); // duplicate same-instance contact
            NetPickupNetwork.PumpOwner();
            Check(offered.Count == 2 && offered.Select(c => c.Spawn).ToHashSet().SetEquals(new[] { keyA, keyB })
                && offered.Select(c => c.Key).Distinct().Count() == 2
                && offered.All(c => c.Position == owner.Position),
                "actual owner contact emits separate exact-key claims despite identical contact positions");
            delivered.Clear();
            Session(nameof(NetSession.Role), NetRole.Server);
            Session(nameof(NetSession.IsAuthority), true);
            Session(nameof(NetSession.LocalSlot), -1);
            Session(nameof(NetSession.NetFrame), 501u);
            authorityA!.Destroy(); authorityA = null; // production native removal publisher
            Check(NetPickupNetwork.Pump(peer, 0, DrainDiscovery) && delivered.Count == 1,
                "one native removal publishes one retained tombstone");
            var removal = delivered.Single();
            Check(removal.Spawn == keyA && !removal.Available, "removal cannot alias same-position sibling");
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false);
            Session(nameof(NetSession.LocalSlot), owner.SlotIndex);
            Check(NetPickupNetwork.ReceiveItem(removal), "native removal reaches owner projection");
            Check(!NetPickupNetwork.ReceiveItem(removal) && !NetPickupNetwork.ReceiveItem(birthA),
                "duplicate removal and late original birth cannot resurrect consumed identity");
            TimedPickupProjection.ApplyDynamic(scene);
            Check(clientA.DespawnTimer == 0 && clientB.DespawnTimer != 0 && clientB.ModPickupSpawn == keyB,
                "exact-key retirement preserves the same-position sibling instance");
            Check(!NetPickupNetwork.ReceiveItem(birthA with { AuthorityFrame = 502 }) && failures == 1
                && !NetPickupNetwork.ItemFacts[keyA].Available,
                "newer contradictory birth is an explicit negative and cannot resurrect A");
        }
        finally
        {
            Session(nameof(NetSession.Role), NetRole.Server);
            Session(nameof(NetSession.IsAuthority), true);
            authorityA?.Destroy(); authorityB?.Destroy();
            foreach (var item in projected) { item.Destroy(); scene.RemoveEntity(item); }
            NetPowerUpLedger.Reset();
            NetPickupNetwork.SendClaim = oldSend;
            NetPickupNetwork.ClientFailure = oldFailure;
            Session(nameof(NetSession.Role), oldRole);
            Session(nameof(NetSession.IsAuthority), oldAuthority);
            Session(nameof(NetSession.LocalSlot), oldLocal);
            Session(nameof(NetSession.ServerSession), oldServer);
            Session(nameof(NetSession.NetFrame), oldFrame);
        }
    }

    private static void AuthorityBits(PlayerEntity p)
    {
        Session(nameof(NetSession.Role),NetRole.Server); Session(nameof(NetSession.IsAuthority),true); Session(nameof(NetSession.LocalSlot),-1);
        Set(p,"_doubleDmgTimer",333); p.ModSetShotState(0,0,false,false);
        Check((ushort)Field(p,"_doubleDmgTimer")==333,"authority native grant survives owner false bit");
        Set(p,"_doubleDmgTimer",0); p.ModSetShotState(0,0,true,false);
        Check((ushort)Field(p,"_doubleDmgTimer")==0&&!p.DoubleDamage,"owner true bit cannot create authority grant");
    }
    public static int Run(string room)
    {
        var contextSettings=DesktopGlContext.Settings(background:true);
        contextSettings.ClientSize=new Vector2i(320,240); contextSettings.StartVisible=false;
        using var contextWindow=new NativeWindow(contextSettings);
        using var graphics=new DesktopGraphicsSession(contextWindow);
        var sim=new ServerSim(); bool script=NetTestScript.Enabled;
        try
        {
            _checks=0; NetTestScript.Enabled=false;
            Check(sim.Start(room,GameMode.Battle,2,_=>{},()=>{}),"licensed asset native fixture starts");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId=1,AuthorityEpoch=1,RoomKey=room,Mode=(byte)GameMode.Battle },false);
            var roster=RosterPacket.Create(); roster.MatchId=1; roster.AuthorityEpoch=1; roster.Revision=1; roster.Count=2;
            for(byte i=0;i<2;i++) { roster.Slots[i]=i; roster.Generations[i]=1; roster.Hunters[i]=(byte)Hunter.Samus; }
            NetSession.ApplyRoster(roster); for(int i=0;i<120;i++) sim.Step();
            var player=PlayerEntity.Players[1]; player.Health=199; player.OwningScene.GameState.PrimeHunter=-1;
            player.OwningScene.GameState.BalancedMode=false; player.OwningScene.GameState.EnhancedHunters=false;
            player.OwningScene.Players.MainPlayerIndex=1;
            typeof(Scene).GetField("_cameraMode",Hidden)!.SetValue(player.OwningScene,CameraMode.Player);
            player.SetUpHud(); Check(player.HudReady,"native owner HUD initialized before form presentation");
            Session(nameof(NetSession.Role),NetRole.Client); Session(nameof(NetSession.IsAuthority),false);
            Session(nameof(NetSession.LocalSlot),1); Session(nameof(NetSession.ServerSession),null); Session(nameof(NetSession.NetFrame),100u);
            player.Position=new Vector3(1000,1000,1000); player.PrevPosition=player.Position;
            Check(player.IsMainPlayer&&player.ModIsInPlay,"native owner presentation hooks active");
            DamageProjection(player); CloakAndDeathAlt(player); ItemIdentity(player); DynamicSamePositionTwins(player); RoamingOwnerDeathAlt(player); DeadProjection(player); AuthorityBits(player);
            Console.WriteLine($"PASS: {_checks} native timed grant presentation assertions"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetTestScript.Enabled=script; NetGrantNetwork.Reset(); }
    }
}
