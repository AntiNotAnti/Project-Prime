using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
namespace MphRead.Mods.Oddball;
/// <summary>Runtime Oddball overlay for built-in and installed custom maps; no package or geometry edits.</summary>
public static class OddballMap
{
    public static bool IsCombatHall(string name) => String.Equals(name, OddballMatch.RoomKey, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Entity> Resolve(RoomMetadata room, IReadOnlyList<Entity> original)
    {
        // Preserve the already-playtested Combat Hall arrangement exactly.
        if (IsCombatHall(room.Name)) return ResolveCombatHall(room, original);
        var starts = original.OfType<Entity<PlayerSpawnEntityData>>()
            .Where(s => s.Data.Active != 0 && Finite(s.Position)).OrderBy(s => s.EntityId).ToArray();
        if (starts.Length == 0 && room.EntityPath != null)
        {
            starts = Read.GetEntities(room.EntityPath, -1, false, allowHook: true)
                .OfType<Entity<PlayerSpawnEntityData>>().Where(s => s.Data.Active != 0 && Finite(s.Position))
                .GroupBy(s => s.EntityId).Select(g => g.First()).OrderBy(s => s.EntityId).ToArray();
        }
        if (starts.Length == 0) throw new ProgramException($"{room.InGameName ?? room.Name} has no active player starts for Oddball.");
        var used = original.Select(e => e.EntityId).Concat(starts.Select(e => e.EntityId)).ToHashSet();
        int next = short.MaxValue;
        short NewId()
        {
            while (next > 0 && used.Contains((short)next)) next--;
            if (next <= 0) throw new ProgramException("No free entity IDs for the Oddball overlay.");
            short value = (short)next--; used.Add(value); return value;
        }
        Vector3 mean = Vector3.Zero;
        foreach (var s in starts) mean += s.Position;
        mean /= starts.Length;
        // Use authored opposing teams when present. Otherwise divide the authored
        // starts along their widest horizontal axis, deterministically, by count.
        bool authoredTeams = starts.Any(s => s.Data.TeamIndex == 0) && starts.Any(s => s.Data.TeamIndex == 1);
        float xRange = starts.Max(s => s.Position.X) - starts.Min(s => s.Position.X);
        float zRange = starts.Max(s => s.Position.Z) - starts.Min(s => s.Position.Z);
        var ordered = starts.OrderBy(s => xRange > zRange ? s.Position.X : s.Position.Z).ThenBy(s => s.EntityId).ToArray();
        var teams = new Dictionary<short, int>();
        for (int i = 0; i < ordered.Length; i++)
            teams[ordered[i].EntityId] = authoredTeams && ordered[i].Data.TeamIndex is 0 or 1
                ? ordered[i].Data.TeamIndex : i < Math.Max(1, ordered.Length / 2) ? 0 : 1;
        var result = original.Where(e => e.Type is not (EntityType.PlayerSpawn or EntityType.OctolithFlag or EntityType.FlagBase)).ToList();
        foreach (var s in starts)
            result.Add(Spawn(s.EntityId, s.Position, s.NodeName, teams[s.EntityId], 0, 1, s.UpVector, s.FacingVector));
        // Maps authored for 2/4 players need four lanes per team at 4v4. Their
        // cloned starts are spread only onto verified nearby ground at load time.
        for (int team = 0; team < 2; team++)
        {
            var own = starts.Where(s => teams[s.EntityId] == team).ToArray();
            if (own.Length == 0) own = [starts[^1]];
            int count = result.OfType<Entity<PlayerSpawnEntityData>>().Count(s => s.Data.TeamIndex == team);
            for (int i = count; i < 4; i++)
            {
                var s = own[(i - count) % own.Length];
                result.Add(Spawn(NewId(), s.Position, s.NodeName, team, 0, 1, s.UpVector, s.FacingVector));
            }
            var home = own.OrderByDescending(s => Vector3.DistanceSquared(s.Position, mean)).ThenBy(s => s.EntityId).First();
            result.Add(Make<FlagBaseEntityData>(EntityType.FlagBase, NewId(), home.Position, home.NodeName, 108, team));
        }
        var seed = starts.OrderBy(s => Vector3.DistanceSquared(s.Position, mean)).ThenBy(s => s.EntityId).First();
        result.Add(Make<OctolithFlagEntityData>(EntityType.OctolithFlag, NewId(), seed.Position, seed.NodeName, 41, 255));
        return result;
    }

    internal static bool Finite(Vector3 p) => Single.IsFinite(p.X) && Single.IsFinite(p.Y) && Single.IsFinite(p.Z);

    /// <summary>Resolve to an actual non-damaging, supported walking surface with headroom.</summary>
    internal static bool TryFloor(Scene scene, Vector3 seed, out Vector3 floor)
    {
        floor = default;
        if (!Finite(seed)) return false;
        CollisionResult hit = default;
        if (!CollisionDetection.CheckBetweenPoints(seed.AddY(2), seed.AddY(-6), TestFlags.Players, scene, ref hit)
            || hit.Plane.Y < .7f || hit.EntityCollision != null || hit.Flags.TestFlag(CollisionFlags.Damaging)) return false;
        floor = hit.Position;
        if (!Finite(floor) || floor.Y <= (scene.Room?.Meta.KillHeight ?? -10000) + .25f) return false;
        // The center and a small supporting footprint must belong to the same
        // walking surface, not a thin lip over a void, with clearance to reach it.
        foreach (Vector3 offset in new[] { Vector3.Zero, Vector3.UnitX * .55f, -Vector3.UnitX * .55f,
            Vector3.UnitZ * .55f, -Vector3.UnitZ * .55f })
        {
            CollisionResult support = default;
            if (!CollisionDetection.CheckBetweenPoints(floor + offset + Vector3.UnitY * .6f,
                floor + offset - Vector3.UnitY * .65f, TestFlags.Players, scene, ref support)
                || support.Plane.Y < .7f || support.Flags.TestFlag(CollisionFlags.Damaging)) return false;
            CollisionResult roof = default;
            if (CollisionDetection.CheckBetweenPoints(floor + offset + Vector3.UnitY * .12f,
                floor + offset + Vector3.UnitY * 2.0f, TestFlags.Players, scene, ref roof)) return false;
        }
        return true;
    }

    /// <summary>Called after room collision and all map entities have initialized, before play.</summary>
    internal static void ConfigureScene(Scene scene)
    {
        var flags = new List<OctolithFlagEntity>();
        foreach (var f in scene.GetOctolithFlagEntities()) flags.Add(f);
        var flag = flags.Single();
        var activeSpawns = new List<PlayerSpawnEntity>();
        foreach (var s in scene.GetPlayerSpawnEntities()) if (s.IsActive) activeSpawns.Add(s);
        var spawns = activeSpawns.OrderBy(s => s.Id).ToArray();
        if (spawns.Length == 0) throw new ProgramException("Oddball needs an active player start.");
        var occupied = new List<Vector3>();
        foreach (var spawn in spawns)
        {
            Vector3 position = spawn.Position;
            if (occupied.Any(p => Vector3.DistanceSquared(p, position) < 1.44f))
            {
                bool placed = false;
                for (int ring = 1; ring <= 3 && !placed; ring++)
                    for (int direction = 0; direction < 8 && !placed; direction++)
                    {
                        float angle = direction * MathF.PI / 4;
                        Vector3 proposed = position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * (ring * 1.5f);
                        if (!TryFloor(scene, proposed, out Vector3 safe) || occupied.Any(p => Vector3.DistanceSquared(p, safe) < 1.44f)) continue;
                        CollisionResult wall = default;
                        if (CollisionDetection.CheckBetweenPoints(position.AddY(1), safe.AddY(1), TestFlags.Players, scene, ref wall)) continue;
                        spawn.Position = safe;
                        position = safe; placed = true;
                    }
            }
            occupied.Add(position);
        }
        // Combat Hall's measured central placement is already approved.
        if (IsCombatHall(scene.Room!.Meta.Name) && TryFloor(scene, flag.BasePosition, out _))
        {
            Console.WriteLine($"[oddball-map-v1.3] {scene.Room.Meta.Name}: retained Combat Hall skull={flag.BasePosition}; starts={spawns.Length}");
            return;
        }
        Vector3 center = Vector3.Zero;
        foreach (var s in spawns) center += s.Position;
        center /= spawns.Length;
        var candidates = new List<Vector3> { center, flag.BasePosition };
        foreach (var item in scene.GetItemSpawnEntities()) candidates.Add(item.Position);
        foreach (var s in spawns) candidates.Add(s.Position);
        if (scene.Room.NodeData is { Simple: true } nodes)
        {
            candidates.AddRange(nodes.Data[0][0]
                .Where(n => n.NodeType is NodeType.Navigation or NodeType.Special)
                .OrderBy(n => Vector3.DistanceSquared(n.Position, center)).Take(256).Select(n => n.Position));
        }
        Vector3[] red = spawns.Where(s => s.Data.TeamIndex == 0).Select(s => s.Position).ToArray();
        Vector3[] blue = spawns.Where(s => s.Data.TeamIndex == 1).Select(s => s.Position).ToArray();
        if (red.Length == 0 || blue.Length == 0) throw new ProgramException("Oddball needs starts for both teams.");
        OddballMapNavigation? routing = scene.Room.NodeData is { Simple: true } graph && graph.Data[0][0].Count > 0
            ? new OddballMapNavigation(scene, graph.Data[0][0], red, blue) : null;
        float best = Single.MaxValue; Vector3 chosen = default; int valid = 0, routed = 0; bool bestRouted = false;
        foreach (Vector3 candidate in candidates.Distinct())
        {
            if (!TryFloor(scene, candidate, out Vector3 floor)) continue;
            valid++;
            float r = red.Average(p => Vector3.Distance(p, floor));
            float b = blue.Average(p => Vector3.Distance(p, floor));
            bool reachable = false;
            if (routing != null && routing.TryCost(floor, out float routeRed, out float routeBlue))
            { r = routeRed; b = routeBlue; reachable = true; routed++; }
            // Prefer a candidate reached by the native biped routing graph from
            // both teams, rather than a deceptively central disconnected platform.
            float cost = MathF.Abs(r - b) * 3 + r + b + .15f * Vector3.Distance(center, floor);
            // Keep the skull out of an opening spawn's pickup radius where possible.
            if (spawns.Any(s => Vector3.DistanceSquared(s.Position, floor) < 9)) cost += 20;
            if ((reachable && !bestRouted) || (reachable == bestRouted && cost < best))
            { best = cost; chosen = floor; bestRouted = reachable; }
        }
        if (valid == 0) throw new ProgramException($"{scene.Room.Meta.InGameName ?? scene.Room.Meta.Name}: no safe Oddball floor found. Repair this map's spawn/collision data.");
        flag.OddballSetBase(chosen);
        Console.WriteLine($"[oddball-map-v1.3] {scene.Room.Meta.Name}: skull={chosen}; starts={red.Length}/{blue.Length}; safeCandidates={valid}; routedCandidates={routed}; selectionCost={best:F3}");
    }

    private static IReadOnlyList<Entity> ResolveCombatHall(RoomMetadata room, IReadOnlyList<Entity> original)
    {
        if(!String.Equals(room.Name,OddballMatch.RoomKey,StringComparison.OrdinalIgnoreCase))
            throw new ProgramException("Oddball Test 1 supports Combat Hall only.");
        if(original.Any(e=>e.EntityId==30000 && e.Type==EntityType.OctolithFlag))return original;
        // These four authored ground starts define the two ends of Combat Hall.
        var ground=original.OfType<Entity<PlayerSpawnEntityData>>().Where(e=>e.EntityId is 4 or 5 or 6 or 7).OrderBy(e=>e.EntityId).ToArray();
        if(ground.Length!=4)throw new ProgramException("Combat Hall native start markers are missing; refusing an unmeasured substitute.");
        Vector3 center=Vector3.Zero;foreach(var start in ground)center+=start.Position;center/=4;
        var result=new List<Entity>();
        foreach(var entity in original)
        {
            if(entity is Entity<PlayerSpawnEntityData> spawn)
            {
                int team=spawn.Position.Z<center.Z?0:1;
                bool upper=spawn.Position.Y>center.Y+1;
                result.Add(Spawn(spawn.EntityId,spawn.Position,spawn.NodeName,team,upper?(byte)1:spawn.Data.Availability,spawn.Data.Active,spawn.UpVector,spawn.FacingVector));
            }
            else if(entity.Type is not (EntityType.OctolithFlag or EntityType.FlagBase))result.Add(entity);
        }
        // Two source-aligned rows per end provide four distinct ground starts per team.
        for(int i=0;i<4;i++)
        {
            var p=ground[i];int team=p.Position.Z<center.Z?0:1;
            result.Add(Spawn((short)(30010+i),p.Position+Vector3.UnitZ*(team==0?2:-2),p.NodeName,team,0,1,p.UpVector,p.FacingVector));
        }
        result.Add(Make<OctolithFlagEntityData>(EntityType.OctolithFlag,30000,center,ground[0].NodeName,41,255));
        // Invisible, non-scoring navigation destinations for each team's carrier bots.
        for(int team=0;team<2;team++)
        {
            Vector3 home=Vector3.Zero;int n=0;
            foreach(var p in ground)if((p.Position.Z<center.Z?0:1)==team){home+=p.Position;n++;}
            home/=n;
            result.Add(Make<FlagBaseEntityData>(EntityType.FlagBase,(short)(30001+team),home,ground[0].NodeName,108,team));
        }
        return result;
    }
    private static Entity<PlayerSpawnEntityData> Spawn(short id,Vector3 pos,string node,int team,byte availability,byte active,Vector3 up,Vector3 facing)
    {
        byte[] bytes=new byte[43];var header=new EntityDataHeader((ushort)EntityType.PlayerSpawn,id,pos,up,facing);
        MemoryMarshal.Write(bytes.AsSpan(),in header);bytes[40]=availability;bytes[41]=active;bytes[42]=(byte)team;
        var data=Read.ReadStruct<PlayerSpawnEntityData>(bytes);
        return new Entity<PlayerSpawnEntityData>(Entry(node,43),EntityType.PlayerSpawn,id,data,header);
    }
    private static Entity<T> Make<T>(EntityType type,short id,Vector3 pos,string node,int size,int team) where T:struct
    {
        byte[] bytes=new byte[size];var header=new EntityDataHeader((ushort)type,id,pos,Vector3.UnitY,Vector3.UnitZ);
        MemoryMarshal.Write(bytes.AsSpan(),in header);
        if(type==EntityType.FlagBase)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40),team);else bytes[40]=(byte)team;
        return new Entity<T>(Entry(node,size),type,id,Read.ReadStruct<T>(bytes),header);
    }
    private static EntityEntry Entry(string node,int size)
    {
        byte[] bytes=new byte[24];byte[] name=Encoding.ASCII.GetBytes(node);Array.Copy(name,bytes,Math.Min(15,name.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16),ushort.MaxValue);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18),(ushort)size);
        return Read.ReadStruct<EntityEntry>(bytes);
    }
}
