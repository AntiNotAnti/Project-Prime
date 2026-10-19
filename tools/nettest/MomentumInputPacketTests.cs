using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free wire/bridge units. The injected bridge host is a unit fixture,
// not a transport connection, physics owner or multiplayer acceptance claim.
internal static class MomentumInputPacketTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    internal static int Run()
    {
        Scene? scene = null;
        try
        {
            _checks = 0; Headless.Enter();
            Span<byte> compact = stackalloc byte[IntentPacket.FullSize];
            Span<byte> canonical = stackalloc byte[IntentPacket.FullSize];
            foreach (var axes in new[] { new Vector2(.25f, 0), new Vector2(.25f, -.75f), new Vector2(-.25f, .75f),
                Vector2.Zero, new Vector2(1, -1), new Vector2(float.NaN, float.PositiveInfinity), new Vector2(.00001f, -.00001f) })
            {
                var packet = new IntentPacket { MoveX = IntentPacket.PackMoveAxis(axes.X), MoveY = IntentPacket.PackMoveAxis(axes.Y),
                    HasAnalogMove = true, Aim = Vector3.UnitZ, Target = NetTargetIdentity.None, WeaponSelect = 0xFF };
                foreach (var pair in new[] { (axes.X, packet.MoveX), (axes.Y, packet.MoveY) })
                {
                    float actual = IntentPacket.UnpackMoveAxis(pair.Item2);
                    if (!float.IsFinite(pair.Item1)) Check(actual == 0, "nonfinite movement packs neutral");
                    else if (MathF.Abs(pair.Item1) < .5f / 127 && pair.Item1 != 0)
                        Check(pair.Item2 == Math.Sign(pair.Item1), "tiny nonzero input preserves sign with minimum wire magnitude");
                    else Check(MathF.Abs(actual - Math.Clamp(pair.Item1, -1, 1)) <= .5f / 127 + .0000001f, "movement quantization half-unit bound");
                }
                int size = CompactIntentCodec.Write(packet, compact);
                Check(CompactIntentCodec.TryRead(compact[..size], out var decoded) && decoded.HasAnalogMove
                    && decoded.MoveX == packet.MoveX && decoded.MoveY == packet.MoveY, "compact preserves partial/reverse/neutral axes and tail presence");
                packet.Write(canonical); var full = IntentPacket.Read(canonical);
                Check(full.HasAnalogMove && full.MoveX == packet.MoveX && full.MoveY == packet.MoveY, "canonical preserves axes and presence");
            }
            var absent = new IntentPacket { Aim = Vector3.UnitZ, Target = NetTargetIdentity.None, WeaponSelect = 0xFF,
                HasAnalogMove = false, MoveX = 32, MoveY = -95 };
            absent.Write(canonical);
            int legacyLength = IntentPacket.Size + IntentPacket.ShotStateSize;
            var legacy = IntentPacket.Read(canonical[..legacyLength]);
            Check(!legacy.HasAnalogMove && legacy.MoveX == 0 && legacy.MoveY == 0, "legacy absent analog tail cannot activate override");
            int currentSize = CompactIntentCodec.Write(absent, compact);
            Check(CompactIntentCodec.TryRead(compact[..currentSize], out var present) && present.HasAnalogMove
                && present.MoveX == 32 && present.MoveY == -95, "current wire presence derives from width rather than in-memory bool");
            var neutral = absent; neutral.MoveX = neutral.MoveY = 0; neutral.Write(canonical);
            Check(IntentPacket.Read(canonical).HasAnalogMove, "present zero tail remains present for digital fallback");

            scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            var player = new PlayerEntity(0, scene);
            var bridge = new PlayerReplicationBridge(new BridgeHost());
            var intent = new IntentPacket { SlotGeneration = 1, LifeId = 1, Frame = 1, Aim = Vector3.UnitZ,
                Buttons = IntentButtons.InPlayState | IntentButtons.MoveUp, WeaponSelect = 0xFF,
                HasAnalogMove = true, MoveX = 32, MoveY = 95 };
            void Apply() { bridge.ApplyIntent(player, intent); intent.Frame++; }
            Apply();
            Check(player.Controls.AnalogMoveActive && player.Controls.AnalogMoveX == 32 / 127f
                && player.Controls.AnalogMoveY == 95 / 127f, "actual replication bridge activates partial override");
            intent.HasAnalogMove = false; Apply();
            Check(!player.Controls.AnalogMoveActive && player.Controls.MoveUp.IsDown && player.Controls.AnalogScaleY(1) == 1,
                "absent tail clears active override while preserving digital strength");
            intent.HasAnalogMove = true; Apply(); Check(player.Controls.AnalogMoveActive, "subsequent present axes reactivate override");
            intent.MoveX = intent.MoveY = 0; Apply();
            Check(!player.Controls.AnalogMoveActive && player.Controls.MoveUp.IsDown && player.Controls.AnalogScaleY(1) == 1,
                "present neutral axes clear prior override and retain digital fallback");
            intent.MoveX = -32; intent.MoveY = -95; Apply();
            Check(player.Controls.AnalogMoveX < 0 && player.Controls.AnalogMoveY < 0, "bridge reversal has no previous analog sign");
            Console.WriteLine($"MOMENTUM INPUT PACKETS PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM INPUT PACKETS FAIL " + ex); return 1; }
        finally { if (scene != null) { try { scene.DoCleanup(); } finally { scene.UnloadGl(); } } }
    }
    private sealed class BridgeHost : IPlayerReplicationHost
    {
        public bool IsReplica => true;
        public bool Active => true;
        public bool IsAuthority => false;
        public bool IsHost => false;
        public int LocalSlot => 1;
        public uint Frame => 100;
        public bool Settling => false;
        public bool GameplayReady => true;
        public bool CanSpawn => false;
        public int Ping(int slot) => 0;
        public bool Matches(int slot, ushort generation, ushort life) => slot == 0 && generation == 1 && life == 1;
        public bool TryGetIntent(int slot, out IntentPacket intent) { intent = default; return false; }
        public bool TryGetState(int slot, out PlayerState state) { state = default; return false; }
        public uint IntentAge(int slot) => 0;
        public void OnSpawn(PlayerEntity player) { }
        public void BeginLife(PlayerEntity player, in PlayerState state) { }
        public void Spawn(PlayerEntity player, in PlayerState state) { }
        public void ReplayDamage(PlayerEntity player, in PlayerState state) { }
        public void ReplayDeath(PlayerEntity player) { }
        public void NoteDeath(int slot) { }
        public int HealthFor(PlayerEntity player, int health, bool local) => health;
        public bool SamplePosition(int slot, bool presentation, out Vector3 position, out bool alt)
        { position = default; alt = false; return false; }
        public void StampAcknowledgement(ref IntentPacket intent) { }
    }
}
