using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.EnhancedHunters;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Content-backed synthetic archives, produced only by the pinned pre-retirement
// library. No retail content is committed. This checks reconstruction, not GL/audio.
internal static class EnhancedHistoricalMatrixTests
{
    private const string BaselineMvid = "bb5922d1-051d-4a89-940b-d14096c550fe";
    private const string WorldContract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
    private const string Room = "MP1 SANCTORUS";
    private static readonly Dictionary<string,string> FrozenFiles = new(StringComparer.Ordinal)
    {
        ["seven-alt-0-continuation.json"] = "669F2A94368CE07E591F9A0F63EDA8DA60D130EBB2091071F7EA60D68F11F3CC",
        ["seven-alt-0-world-v9.bin"] = "731DC2EEBB0D5186C40B93B6B5ACDC0392BA18B83496AC44B4F8AAE18F844AE6",
        ["seven-alt-24-continuation.json"] = "B7CB21E9879DDD03CDB912B69B721A43F61ACECF0C592A0A5693D3605E7C34A7",
        ["seven-alt-24-world-v9.bin"] = "CD2B61398B574C54AC4AA994E57E21F7F0BDAB0C282B3C9CEA34A9524F921EC3",
        ["seven-alt-48-continuation.json"] = "D8FA8E489281F069021B7FC021D1EB06415E07DAEAAB9E298D7F35BC7AF57685",
        ["seven-alt-48-world-v9.bin"] = "A94234BD0D8C837B03B8894B04D4B84CE905A9377A5CE3636289A617A220D626",
        ["seven-alt-72-continuation.json"] = "692B3D0CBFADC8F2027B00697E903BC450E372D093B9B73712872FD39E213BA0",
        ["seven-alt-72-world-v9.bin"] = "CAC3AE1F7EC1B8D1CA2FC821520CE9B169CDAFF8629EEC9D8C26E8A084173A31",
        ["seven-alt-forward.json"] = "315E8A1DAA8703579C78FE1A1667F7EB1BE58FC5A7B97DE6E9A4314A5FD4C52E",
        ["seven-alt.ppdemo"] = "B74416D7D224A451AC30B139D45B0D5B1B86A058E65C9876FFEA7480A1392B31",
        ["seven-biped-0-continuation.json"] = "D20412BC09F20F78C483A997FCE54946576A5ADE689D040ABB45326D1BC39581",
        ["seven-biped-0-world-v9.bin"] = "ED413F87EEC116E357C222B03C133939E1851F6F39B96F5CB81327995B3636E1",
        ["seven-biped-24-continuation.json"] = "01F07D7E7E36B50E939A35089F2E62AA42594612B1DF03B107B92FDC28428BC0",
        ["seven-biped-24-world-v9.bin"] = "0CFB19DAB37957CC403C8EB1A37EA051822D8FC20040138A1AC86B972742993F",
        ["seven-biped-48-continuation.json"] = "9C6F9C237CCF33F1C56405CDF084D31008E2ECF6D9267A32908DD4D268693C10",
        ["seven-biped-48-world-v9.bin"] = "B92B45453637A4B3555477D92697B1B50F41E0E75EBAC72E24DC10B5B7B6F38E",
        ["seven-biped-72-continuation.json"] = "866CDB338BBA9BC3E2B6274D1D025E3E8A6B3B3087F41806F25EF0584DA5B0AC",
        ["seven-biped-72-world-v9.bin"] = "293E8635C7751158B601BA55115EF94FD721548BFBAF1EFC244783C4D42AC5F2",
        ["seven-biped-forward.json"] = "8922AA69D1469E284CED536EAC62F36555B7489C3CC2E512BF0909631C67B4BD",
        ["seven-biped.ppdemo"] = "B5E8613A03EAE5628D3C4EBFD88525BA32EB41595BBE3EFD7DD5763F804E0F46",
        ["seven-hunters-provenance.json"] = "28585F9B97C2637E656B61ABC8153F3AD5AE212FF72D68A37A15B5385F10EEDB",
    };
    internal static bool CanRecordBaseline => typeof(Scene).Assembly.ManifestModule.ModuleVersionId.ToString()==BaselineMvid;
    private static int _checks;
    private static void Check(bool ok, string name)
    { _checks++; if (!ok) throw new InvalidOperationException(name); }
    private static ushort Generation(int slot) => (ushort)(100 + slot);
    private static ushort Life(int slot, uint frame) => (ushort)(200 + slot + (frame >= 36 ? 8 : 0));
    private static bool Alt(bool initiallyAlt, uint frame) => frame is >= 20 and < 36 || frame >= 60 ? !initiallyAlt : initiallyAlt;
    private static byte[] Packet(PacketType type, int size, Action<byte[]> write)
    {
        var body = new byte[size]; write(body);
        var packet = new byte[1 + size]; packet[0] = (byte)type; body.CopyTo(packet, 1); return packet;
    }
    private static EnhancedZone[] Zones(uint frame) => new[] {
        new EnhancedZone { Type=EnhancedZoneType.IcePatch, OwnerSlot=4, OwnerGeneration=Generation(4),
            OwnerLifeId=Life(4,frame), RemainingFrames=96, Position=new(10,5,8), Radius=1.8f },
        new EnhancedZone { Type=EnhancedZoneType.MagmaPool, OwnerSlot=5, OwnerGeneration=Generation(5),
            OwnerLifeId=Life(5,frame), RemainingFrames=120, Position=new(14,5,8), Radius=1.6f }
    };
    private static byte[] Snapshot(bool initialAlt, uint frame)
    {
        int healthAt = SnapshotHeader.Size + 7 * PlayerState.Size + NetMatchTimeSync.Size;
        int worldAt = healthAt + NetHealthSync.HeaderSize;
        return Packet(PacketType.Snapshot, worldAt + 1 + 2 * EnhancedHunterWorld.ZoneSize, body => {
            new SnapshotHeader { MatchId=7, AuthorityEpoch=9, PlayerCount=7, Frame=frame }.Write(body);
            for (byte slot=0;slot<7;slot++)
            {
                int target=(slot+1)%7;
                new PlayerState { SlotIndex=slot, SlotGeneration=Generation(slot), LifeId=Life(slot,frame),
                    Health=99, Flags=(byte)(PlayerState.FlagActive|PlayerState.FlagSpawned|(Alt(initialAlt,frame)?PlayerState.FlagAltForm:0)),
                    Position=new(2+slot*3,5,8), Facing=Vector3.UnitZ,
                    Form=new PlayerFormState { Frame=frame,StartedFrame=frame,Phase=Alt(initialAlt,frame)?FormPhase.Alt:FormPhase.Biped,
                        BodyHealth=(ushort)(slot==6&&Alt(initialAlt,frame)?49:99),
                        TurretActive=slot==6&&Alt(initialAlt,frame),TurretHealth=(ushort)(slot==6&&Alt(initialAlt,frame)?50:0),
                        TurretPosition=new(2+slot*3,5,8) },
                    Enhanced=new EnhancedHunterNetState { TargetSlot=(byte)target, TargetGeneration=Generation(target),
                        TargetLifeId=Life(target,frame), ValueA=(byte)(slot%4), ValueB=(byte)(slot+1), Flags=1,
                        TimerA=(byte)(24+slot), TimerB=(byte)(30+slot) } }.Write(body.AsSpan(SnapshotHeader.Size+slot*PlayerState.Size));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(healthAt),7);
            var world=new EnhancedHunterWorld(); var zones=Zones(frame); world.Zones[0]=zones[0];world.Zones[1]=zones[1];
            world.Write(body.AsSpan(worldAt));
        });
    }
    private static List<byte[]> Bootstrap(bool alt)
    {
        var match=new MatchStatePacket { MatchId=7, AuthorityEpoch=9, RoomKey=Room, NextRoomKey="", Mode=(byte)GameMode.Battle,
            PlayerCount=7, RuleBits=(ushort)MatchModifierFlags.EnhancedHunters, TimeRemaining=120 };
        var session=new SessionStatePacket { MatchId=7, AuthorityEpoch=9, MaxPlayers=8, Phase=SessionPhase.InMatch,
            WorldProfile=MatchWorldProfile.Resolve(7), Match=new MatchDefinition { RoomKey=Room, Mode=GameMode.Battle,
                EnhancedHunters=true, TimeLimitSeconds=120, PointGoal=999 } };
        var roster=RosterPacket.Create();roster.MatchId=7;roster.AuthorityEpoch=9;roster.Revision=1;roster.Count=7;
        for(byte slot=0;slot<7;slot++) { roster.Slots[slot]=slot;roster.Generations[slot]=Generation(slot);
            roster.Hunters[slot]=slot;roster.Names[slot]=((Hunter)slot).ToString();roster.Teams[slot]=-1; }
        return new() { Packet(PacketType.MatchState,MatchStatePacket.Size,b=>match.Write(b)),
            Packet(PacketType.SessionState,SessionStatePacket.Size,b=>session.Write(b)),
            Packet(PacketType.Roster,RosterPacket.Size,b=>roster.Write(b)),Snapshot(alt,0) };
    }
    private static SortedDictionary<string,string> AssetHashes()
    {
        var files=new SortedDictionary<string,string>(StringComparer.Ordinal);
        void Add(string? relative)
        {
            if(string.IsNullOrEmpty(relative))return;
            string name=relative.Replace('\\','/'); if(files.ContainsKey(name))return;
            using var stream=File.OpenRead(Paths.Combine(Paths.FileSystem,relative));
            files.Add(name,Convert.ToHexString(SHA256.HashData(stream)));
        }
        var (room,_)=Metadata.GetRoomByName(Room);
        foreach(string? file in new[]{room!.ModelPath,room.CollisionPath,room.EntityPath,room.NodePath,room.AnimationPath,room.TexturePath})Add(file);
        var models=new HashSet<string>(StringComparer.Ordinal);
        void Model(string name)
        {
            if(!models.Add(name))return;
            var model=Metadata.GetModelByName(name)??throw new InvalidDataException("Missing Hunter model metadata "+name);
            Add(model.ModelPath);Add(model.AnimationPath);Add(model.CollisionPath);Add(model.ExtraCollisionPath);
            foreach(var color in model.Recolors) { Add(color.ModelPath);Add(color.TexturePath);Add(color.PalettePath);Add(color.ReplacePath); }
            Add(model.AnimationShare);
        }
        for(int hunter=0;hunter<7;hunter++)foreach(string name in Metadata.HunterModels[(Hunter)hunter])Model(name);
        return files;
    }
    private static void NativeIdentities(PassiveReplayScene replay, uint frame, bool initialAlt)
    {
        Check(replay.Scene.Services is ReplaySceneServices,"reconstruction is replay-owned");
        Check(replay.Scene.GameState.EnhancedHunters&&!replay.Scene.GameState.BalancedMode,"archived rules retain independent mode");
        for(int slot=0;slot<7;slot++)
        {
            var player=replay.Scene.Players.Items[slot];
            Check(player.Hunter==(Hunter)slot&&player.SlotIndex==slot,"native Hunter/slot identity "+slot);
            Check((player.IsAltForm||player.IsMorphing)==Alt(initialAlt,frame),"native biped/alt phase "+slot);
            Check(replay.State.MatchesLife(slot,Generation(slot),Life(slot,frame)),"native generation/life identity "+slot);
            Check(EnhancedHunters.Generation(player)==Generation(slot)&&EnhancedHunters.Life(player)==Life(slot,frame),"legacy lookup uses recorded identity "+slot);
            Check(player.BipedModel2.Model.Name==Metadata.HunterModels[(Hunter)slot][0],"native biped model identity "+slot);
        }
    }
    private static void SeedExtendedState(PassiveReplayScene replay)
    {
        for(int slot=0;slot<7;slot++)
        {
            var s=replay.Scene.Players.Items[slot].EnhancedState;s.Hunter=(Hunter)slot;s.LocalLife=Life(slot,0);
            s.ContactFrames=slot+1;s.LastContactFrame=slot;s.MovementCooldown=12+slot;s.GhostFrames=40+slot;
            s.DecayFrames=20+slot;s.CrossfireFrame=3;s.CrossfireCooldown=6;s.CrossfireTarget=(byte)((slot+1)%7);
            s.CrossfireLife=Life((slot+1)%7,0);s.StationaryShot=true;
            for(int target=0;target<7;target++) { s.FrostStacks[target]=(byte)(target%3);s.BrittleTiers[target]=(byte)(target%2);
                s.FrostExpiry[target]=80+target;s.CrossfireExpiry[target]=90+target;s.FrostLives[target]=Life(target,0);
                s.FrostGenerations[target]=Generation(target);s.Brittle[target]=target%2==0; }
        }
    }
    private static string Hash(PassiveReplayScene replay,uint frame)=>ReplayStateHash.Compute(replay.Scene,frame)+":"+replay.Scene.ReplayPresentationHash(frame);
    private sealed record Observation(uint Frame,string Hash);
    private sealed record Provenance(string ProducerMvid,string ProducerSha256,ulong MapHash,string Contract,SortedDictionary<string,string> Assets);
    internal static int Run(string directory,bool record)
    {
        try
        {
            _checks=0;Headless.Enter();NetSession.Stop();
            var assembly=typeof(Scene).Assembly;
            if(record&&!CanRecordBaseline)
                throw new InvalidOperationException("Baseline generation requires the pinned pre-retirement client; candidate cannot replace historical evidence.");
            string provenance=Path.Combine(directory,"seven-hunters-provenance.json");
            if(!record)
                foreach(var file in FrozenFiles)
                    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,file.Key))))==file.Value,
                        "immutable baseline file "+file.Key);
            var assets=AssetHashes();ulong map=ReplayMapIdentity.Compute(Room);
            Check(map!=0&&assets.Count>28,"exact room and all Hunter assets are available");
            Check(ReplayWorldCheckpoint.ComputeContract(stable:true)==WorldContract,"canonical schema remains unchanged");
            if(record)
            {
                if(Directory.Exists(directory)&&Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Baseline directory must be empty; frozen evidence is never overwritten.");
                Directory.CreateDirectory(directory);
                var origin=new Provenance(BaselineMvid,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),map,WorldContract,assets);
                File.WriteAllText(provenance,JsonSerializer.Serialize(origin,new JsonSerializerOptions{WriteIndented=true}));
            }
            var frozen=JsonSerializer.Deserialize<Provenance>(File.ReadAllText(provenance))!;
            Check(frozen.ProducerMvid==BaselineMvid&&frozen.Contract==WorldContract&&frozen.MapHash==map,"frozen producer/schema/map provenance");
            Check(frozen.Assets.Count==assets.Count&&assets.All(pair=>frozen.Assets.TryGetValue(pair.Key,out string? hash)&&hash==pair.Value),"exact baseline room and Hunter asset SHA256 set");
            foreach(bool alt in new[]{false,true})
            {
                string name=alt?"seven-alt":"seven-biped";string path=Path.Combine(directory,name+".ppdemo");
                if(record)
                {
                    using var writer=new ReplayWriterV3(path,new ReplayMetadata { RoomKey=Room,Mode=GameMode.Battle,MapHash=map,
                        Bootstrap=new ReplayBootstrap { Packets=Bootstrap(alt) } });
                    foreach(uint frame in new uint[]{20,36,60,144})writer.WriteRecord(frame,Snapshot(alt,frame));
                }
                var observations=new List<Observation>();
                using var source=new PassiveReplayScene(path,new(256,192));Check(source.Step(),"initialize "+name);NativeIdentities(source,0,alt);SeedExtendedState(source);
                for(uint frame=0;frame<=96;frame++)
                {
                    if(frame!=0)Check(source.Step(),"forward step "+name+"/"+frame);
                    observations.Add(new(frame,Hash(source,frame)));
                    if(frame is not (0 or 24 or 48 or 72))continue;
                    NativeIdentities(source,frame,alt);
                    string capsule=Path.Combine(directory,name+"-"+frame+"-world-v9.bin");
                    using var current=ReplayWorldCheckpoint.Capture(source);
                    using var reflected=ReplayWorldCheckpoint.Capture(source,boundAccessors:false);
                    Check(current.Bytes.SequenceEqual(reflected.Bytes),"bound/reflection canonical bytes "+name+"/"+frame);
                    if(record)File.WriteAllBytes(capsule,current.Bytes.ToArray());
                    using var historical=ReplayWorldCheckpoint.FromBytes(File.ReadAllBytes(capsule));
                    using var restored=new PassiveReplayScene(path,new(256,192));historical.Restore(restored);
                    NativeIdentities(restored,frame,alt);
                    Check(Hash(restored,frame)==Hash(source,frame),"baseline seek hash "+name+"/"+frame);
                    // Each frozen seek point owns its continuation, including native form/life transitions.
                    var continuation=new List<Observation>();
                    for(uint offset=1;offset<=24;offset++)
                    {
                        Check(restored.Step(),"restored continuation step");continuation.Add(new(frame+offset,Hash(restored,frame+offset)));
                    }
                    string tail=Path.Combine(directory,name+"-"+frame+"-continuation.json");
                    if(record)File.WriteAllText(tail,JsonSerializer.Serialize(continuation));
                    var expected=JsonSerializer.Deserialize<List<Observation>>(File.ReadAllText(tail))!;
                    Check(continuation.SequenceEqual(expected),"frozen seek continuation "+name+"/"+frame);
                }
                string forward=Path.Combine(directory,name+"-forward.json");
                if(record)File.WriteAllText(forward,JsonSerializer.Serialize(observations));
                Check(observations.SequenceEqual(JsonSerializer.Deserialize<List<Observation>>(File.ReadAllText(forward))!),"frozen complete forward hashes "+name);
            }
            Console.WriteLine($"ENHANCED HISTORICAL MATRIX PASS {_checks} checks (seven Hunters, biped/alt, four seeks each, exact asset provenance; headless synthetic)");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("ENHANCED HISTORICAL MATRIX FAIL "+ex);return 1;}
        finally{NetSession.Stop();}
    }
}
