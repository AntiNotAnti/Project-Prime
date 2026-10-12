using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// The actual replica bridge cache codec, without assets or gameplay claims.
internal static class ReplayFormHistoryTests
{
    private const int RowBytes = PlayerFormState.Size + 3 + 2 * sizeof(uint);
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("Replay form history: " + message);
    }
    internal static int Run()
    {
        _checks = 0;
        try
        {
            using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
            var state = ((PassiveReplaySessionHost)session.Host).State;
            var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, new ReplaySceneServices(session, state), initializeRuntime: false);
            Check(scene.Services.IsReplica && scene.Players.Items.Count == 8,
                "real asset-free replica constructs all eight player slots");
            var bridge = scene.PlayerReplication;
            byte[] initial = Capture(bridge);
            Load(bridge, initial);
            Check(Capture(bridge).AsSpan().SequenceEqual(initial), "empty unknown cache roundtrips without inventing history");
            byte[] baseline = Forms(0);
            Load(bridge, baseline);
            Check(Capture(bridge).AsSpan().SequenceEqual(baseline),
                "eight slots preserve unknown/biped/morph/alt/unmorph, turret flags, pending ownership and both local clocks");
            byte[] replacement = Forms(1000);
            int last = (PlayerEntity.SlotCapacity - 1) * RowBytes;
            Reject(bridge, baseline, replacement, bytes => bytes[last + 8] = 255, "malformed final-slot phase");
            Reject(bridge, baseline, replacement, bytes => bytes[last + 21] = 2, "noncanonical turret-active boolean");
            for (int flag = 0; flag < 3; flag++)
            {
                int at = last + PlayerFormState.Size + flag;
                Reject(bridge, baseline, replacement, bytes => bytes[at] = 2, $"noncanonical cache flag {flag}");
            }
            foreach (var pair in new[] { (Offset: 9, Value: Single.NaN), (Offset: 13, Value: Single.PositiveInfinity),
                (Offset: 17, Value: Single.NegativeInfinity) })
                Reject(bridge, baseline, replacement,
                    bytes => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(last + pair.Offset), pair.Value),
                    $"nonfinite turret coordinate {pair.Offset}");
            byte[] futureStart = (byte[])replacement.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(futureStart.AsSpan(last + 4),
                BinaryPrimitives.ReadUInt32LittleEndian(futureStart.AsSpan(last)) + 1);
            RejectBytes(bridge, baseline, futureStart, "future transition start");
            RejectBytes(bridge, baseline, replacement.AsSpan(0, replacement.Length - 1).ToArray(), "truncated final pending clock");
            RejectBytes(bridge, baseline, replacement.AsSpan(0, last + 2).ToArray(), "truncated final state");
            RejectBytes(bridge, baseline, Array.Empty<byte>(), "empty appendix");
            Load(bridge, replacement);
            Check(Capture(bridge).AsSpan().SequenceEqual(replacement), "a valid replacement commits all slots after validation");
            Load(bridge, baseline);
            Check(Capture(bridge).AsSpan().SequenceEqual(baseline), "repeated restore returns to exact recorded cache history");
            AmmoBinding(scene);
            Console.WriteLine($"PASS: replay form history {_checks} assertions; eight-slot bridge cache, transactional malformed-input rejection and restored ammo delegate bindings.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void AmmoBinding(Scene scene)
    {
        var player = scene.Players.Items[0];
        scene.GameState.Mode = GameMode.Battle;
        scene.GameState.BalancedMode = false;
        var ammo = (int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.CurrentWeapon))!.SetValue(player, BeamType.Missile);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(player, GunAnimation.ChargingMissile);
        var weapon = scene.WeaponRules[(int)BeamType.Missile];
        Check(weapon.AmmoType == 1, "actual multiplayer missile uses reserve one");
        player.EquipInfo.Weapon = weapon;
        player.EquipInfo.ChargeLevel = 37; player.EquipInfo.SmokeLevel = 9;
        player.EquipInfo.Zoomed = true; player.EquipInfo.InfiniteAmmo = false;
        ammo[0] = 0; ammo[1] = 41;
        player.EquipInfo.GetAmmo = () => ammo[0];
        player.EquipInfo.SetAmmo = value => ammo[0] = value;
        Check(player.EquipInfo.Ammo == 0 && ammo[1] == 41,
            "restored missile reproduces stale constructor-bound empty reserve zero");
        player.ModRebindReplayAmmo();
        Check(player.EquipInfo.Ammo == 41, "rebind reads restored missile reserve one");
        player.EquipInfo.Ammo = 29;
        Check(ammo[1] == 29 && ammo[0] == 0, "rebind writes reserve one without touching universal ammo");
        Check(player.CurrentWeapon == BeamType.Missile && ReferenceEquals(player.EquipInfo.Weapon, weapon)
            && player.GunAnimation == GunAnimation.ChargingMissile && player.EquipInfo.ChargeLevel == 37
            && player.EquipInfo.SmokeLevel == 9 && player.EquipInfo.Zoomed,
            "resource rebinding preserves weapon, animation, charge, smoke and zoom state");

        scene.GameState.BalancedMode = true; scene.GameState.InstaGib = false;
        scene.GameState.OneInTheChamber = false; scene.GameState.Fiesta = false;
        player.Hunter = Hunter.Trace;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.CurrentWeapon))!.SetValue(player, BeamType.Imperialist);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.GunAnimation))!.SetValue(player, GunAnimation.Charging);
        weapon = scene.WeaponRules[(int)BeamType.Imperialist + 9];
        player.EquipInfo.Weapon = weapon; player.EquipInfo.ChargeLevel = 21;
        int cost = Math.Max(1, (int)weapon.AmmoCost), reserve = cost * 2;
        var privateAmmo = typeof(PlayerEntity).GetField("_balancedImperialistAmmo", BindingFlags.Instance | BindingFlags.NonPublic)!;
        privateAmmo.SetValue(player, reserve);
        ammo[0] = 7001; ammo[1] = 29;
        player.EquipInfo.GetAmmo = () => ammo[0]; player.EquipInfo.SetAmmo = value => ammo[0] = value;
        player.ModRebindReplayAmmo();
        Check(player.EquipInfo.Ammo == reserve, "Balanced affinity Imperialist binds its private reserve");
        player.EquipInfo.Ammo = cost;
        Check((int)privateAmmo.GetValue(player)! == cost && ammo[0] == 7001 && ammo[1] == 29,
            "Balanced Imperialist writes its private reserve without touching shared pools");
        player.EquipInfo.Ammo = Int32.MaxValue;
        Check(player.EquipInfo.Ammo == cost * PlayerEntity.BalancedImperialistShotCap,
            "restored Balanced Imperialist retains its production five-shot cap");
        player.EquipInfo.Ammo = -1;
        Check(player.EquipInfo.Ammo == 0 && ammo[0] == 7001 && ammo[1] == 29,
            "restored private reserve clamps negative writes without changing ordinary ammo");
        Check(player.CurrentWeapon == BeamType.Imperialist && ReferenceEquals(player.EquipInfo.Weapon, weapon)
            && player.GunAnimation == GunAnimation.Charging && player.EquipInfo.ChargeLevel == 21,
            "Balanced resource rebinding preserves restored weapon animation and charge");
    }

    private static byte[] Capture(PlayerReplicationBridge bridge)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        bridge.WriteReplayAuthorityForms(writer); writer.Flush(); return stream.ToArray();
    }
    private static void Load(PlayerReplicationBridge bridge, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
        bridge.ReadReplayAuthorityForms(reader);
        Check(stream.Position == stream.Length, "valid bridge component consumes the exact appendix extent");
    }
    private static byte[] Forms(uint offset)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        Span<byte> bytes = stackalloc byte[PlayerFormState.Size];
        var phases = new[] { FormPhase.Unknown, FormPhase.Biped, FormPhase.Morphing, FormPhase.Alt,
            FormPhase.Unmorphing, FormPhase.Morphing, FormPhase.Alt, FormPhase.Unknown };
        for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
        {
            uint frame = offset + (uint)(100 + slot);
            var state = new PlayerFormState { Frame = frame, StartedFrame = frame - 3, Phase = phases[slot],
                TurretPosition = new Vector3(slot + offset / 10f, slot * 2, -slot),
                TurretActive = slot is 3 or 6, TurretHealth = (ushort)(40 + slot),
                DamageEventId = (ushort)(9 + slot), BodyHealth = (ushort)(80 + slot) };
            if (slot == 0) { state.Frame = 0; state.StartedFrame = UInt32.MaxValue; }
            state.Write(bytes); writer.Write(bytes);
            writer.Write(phases[slot] != FormPhase.Unknown); writer.Write(slot is 4 or 6); writer.Write(slot is 2 or 4 or 6);
            writer.Write(slot == 0 ? UInt32.MaxValue : offset + (uint)(200 + slot));
            writer.Write(slot == 2 ? UInt32.MaxValue - 1 : offset + (uint)(300 + slot));
        }
        return stream.ToArray();
    }
    private static void Reject(PlayerReplicationBridge bridge, byte[] baseline, byte[] replacement,
        Action<byte[]> corrupt, string message)
    {
        byte[] malformed = (byte[])replacement.Clone(); corrupt(malformed);
        RejectBytes(bridge, baseline, malformed, message);
    }
    private static void RejectBytes(PlayerReplicationBridge bridge, byte[] baseline, byte[] malformed, string message)
    {
        bool rejected = false;
        try
        {
            using var stream = new MemoryStream(malformed, writable: false); using var reader = new BinaryReader(stream);
            bridge.ReadReplayAuthorityForms(reader);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
        Check(rejected, message + " is rejected");
        Check(Capture(bridge).AsSpan().SequenceEqual(baseline), message + " cannot partially mutate earlier valid slots");
    }
}
