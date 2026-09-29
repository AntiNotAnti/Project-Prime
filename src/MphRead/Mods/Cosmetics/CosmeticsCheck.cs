using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;
namespace MphRead.Mods.Cosmetics
{
    internal static class CosmeticsCheck
    {
        public static int Run()
        {
            string previousDirectory = LauncherPrefs.Directory;
            bool previousVisible = RenderOptions.ShowCustomCosmetics;
            var previousQuality = RenderOptions.CosmeticQuality;
            string temp = Path.Combine(Path.GetTempPath(), "prime-cosmetics-" + Guid.NewGuid().ToString("N"));
            int checks = 0;
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
            try
            {
                foreach (var catalog in new System.Collections.Generic.IEnumerable<CosmeticDefinition>[]
                    { CosmeticCatalog.Skins, CosmeticCatalog.ArmorEffects, CosmeticCatalog.DeathPresentations })
                {
                    Check(catalog.Select(x => x.Key).Distinct().Count() == catalog.Count(), "unique keys");
                    Check(catalog.Select(x => x.WireId).Distinct().Count() == catalog.Count(), "unique wire IDs");
                }
                Check(CosmeticCatalog.Resolve(Hunter.Samus, new("../bad", "armor.future", "death.future")) == CosmeticLoadout.Default, "unknown keys default");
                Check(CosmeticCatalog.ResolveSkin("skin.samus.obsidian", Hunter.Trace).WireId == 0, "wrong hunter defaults");
                foreach (var armor in CosmeticCatalog.ArmorEffects)
                    Check(CosmeticCatalog.FromWire(Hunter.Samus, 0, armor.WireId, 0).ArmorEffectKey == armor.Key, "armor round trip");
                foreach (var death in CosmeticCatalog.DeathPresentations)
                    Check(CosmeticCatalog.FromWire(Hunter.Samus, 0, 0, death.WireId).DeathEffectKey == death.Key, "death round trip");
                foreach (var skin in CosmeticCatalog.Skins.Where(s => s.Hunter != null))
                    Check(CosmeticCatalog.FromWire(skin.Hunter!.Value, skin.WireId, 0, 0).SkinKey == skin.Key, "skin wire round trip");
                foreach (var effect in CosmeticCatalog.ArmorEffects.Where(e => e.WireId > 0))
                    for (int frame = 0; frame < 120; frame++)
                        for (int sample = 0; sample < 6; sample++)
                        {
                            var point = Armor.ArmorEffectParticles.Sample(effect.Motion, frame / 30f, 17, frame % 10, sample / 5f);
                            Check(float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z)
                                && point.Length < 4, "bounded finite armor path");
                        }
                var loadout = new CosmeticLoadout("skin.samus.obsidian", "armor.inferno", "death.quantum");
                Check(JsonSerializer.Deserialize<CosmeticLoadout>(JsonSerializer.Serialize(loadout)) == loadout, "JSON round trip");
                LauncherPrefs.Directory = temp; CosmeticPersistence.ResetCache();
                CosmeticPersistence.Equip(Hunter.Samus, loadout); CosmeticPersistence.ResetCache();
                Check(CosmeticPersistence.Get(Hunter.Samus) == loadout && CosmeticPersistence.IsPending(Hunter.Samus), "durable local pending save");
                Check(CosmeticPersistence.Get(Hunter.Trace) == CosmeticLoadout.Default, "hunter-specific selection");
                CosmeticPersistence.MergeRemote(Hunter.Samus, CosmeticLoadout.Default);
                Check(CosmeticPersistence.Get(Hunter.Samus) == loadout, "remote cannot overwrite pending selection");
                CosmeticPersistence.MarkSynced(Hunter.Samus, CosmeticLoadout.Default);
                Check(CosmeticPersistence.IsPending(Hunter.Samus), "stale sync acknowledgement ignored");
                CosmeticPersistence.MarkSynced(Hunter.Samus, loadout);
                Check(!CosmeticPersistence.IsPending(Hunter.Samus), "matching sync acknowledged");
                var appearance = new CosmeticAppearance(Hunter.Samus, loadout);
                var packet = CosmeticStatePacket.Create(2, Hunter.Samus, 7, 3, 9, 1, appearance);
                Span<byte> bytes = stackalloc byte[CosmeticStatePacket.Size]; packet.Write(bytes);
                Check(CosmeticStatePacket.TryRead(bytes, out var decoded) && decoded == packet, "wire round trip");
                Check(!CosmeticStatePacket.TryRead(bytes[..^1], out _), "truncated packet rejected");
                var cache = new NetCosmetics();
                Check(!cache.Accept(packet, 3, 9, 8), "old occupant rejected");
                Check(!cache.Accept(packet, 4, 9, 7) && !cache.Accept(packet, 3, 10, 7), "match/authority fencing");
                Check(cache.Accept(packet, 3, 9, 7), "valid state accepted");
                Check(cache.Get(2, Hunter.Samus, 7).Loadout == loadout, "remote appearance");
                Check(cache.Get(2, Hunter.Samus, 8).Loadout == CosmeticLoadout.Default, "slot reuse cannot leak outfit");
                Check(!cache.Accept(packet with { Revision = 0 }, 3, 9, 7), "stale revision rejected");
                Check(cache.Accept(packet with { Revision = 2, Skin = 65535, Armor = 65535, Death = 65535 }, 3, 9, 7)
                    && cache.Get(2, Hunter.Samus, 7).Loadout == CosmeticLoadout.Default, "unknown IDs sanitize");
                var replica = new ReplayReplicaState();
                var match = new MatchStatePacket { MatchId = 3, AuthorityEpoch = 9, RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle };
                byte[] matchBytes = new byte[1 + MatchStatePacket.Size]; matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1));
                replica.Accept(matchBytes, 0);
                var roster = RosterPacket.Create(); roster.MatchId = 3; roster.AuthorityEpoch = 9; roster.Revision = 1;
                roster.Count = 1; roster.Slots[0] = 2; roster.Generations[0] = 7; roster.Hunters[0] = 0; roster.Names[0] = "Cosmetic check";
                byte[] rosterBytes = new byte[1 + RosterPacket.Size]; rosterBytes[0] = (byte)PacketType.Roster; roster.Write(rosterBytes.AsSpan(1));
                replica.Accept(rosterBytes, 0);
                byte[] cosmeticBytes = new byte[1 + CosmeticStatePacket.Size]; cosmeticBytes[0] = (byte)PacketType.CosmeticState; packet.Write(cosmeticBytes.AsSpan(1));
                replica.Accept(cosmeticBytes, 0);
                Check(replica.Cosmetics.Get(2, Hunter.Samus, 7).Loadout == loadout, "replay bootstrap applies cosmetics");
                var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(replica.CaptureCheckpoint());
                Check(restored.Cosmetics.Get(2, Hunter.Samus, 7).Loadout == loadout, "replay checkpoint restores cosmetics");
                var lateJoin = new NetCosmetics();
                Span<byte> batch = stackalloc byte[Entities.PlayerEntity.SlotCapacity * CosmeticStatePacket.Size];
                int batchSize = replica.Cosmetics.Write(batch, 3, 9);
                Check(batchSize == CosmeticStatePacket.Size && CosmeticStatePacket.TryRead(batch[..batchSize], out var late)
                    && lateJoin.Accept(late, 3, 9, 7) && lateJoin.Get(2, Hunter.Samus, 7).Loadout == loadout, "late join/reconnect baseline");
                restored.Reset();
                Check(restored.Cosmetics.Get(2, Hunter.Samus, 7).Loadout == CosmeticLoadout.Default, "map transition clears cosmetics");
                RenderOptions.ShowCustomCosmetics = true; RenderOptions.CosmeticQuality = CosmeticEffectQuality.High;
                Check(CosmeticRuntime.Surface(appearance, 1, true) == default, "critical status suppresses cosmetics");
                var teamSurface = CosmeticRuntime.Surface(appearance, 1, false, team: true);
                Check(teamSurface.Skin != 0 && teamSurface.PreservePalette, "team panels retained without removing selected skin");
                Check(CosmeticRuntime.Surface(CosmeticAppearance.Default, 1, false, distance: 25).Effect == 0,
                    "armor none stays none at far LOD");
                Check(CosmeticRuntime.Surface(appearance, 1, false, distance: 40).Effect == 0, "hidden LOD has no armor shader");
                Check(CosmeticPersistence.Get((Hunter)255) == CosmeticLoadout.Default
                    && CosmeticRuntime.Local((Hunter)255) == CosmeticAppearance.Default, "invalid hunter falls back safely");
                Check(DeathPresentationRuntime.Surface(appearance, appearance.Death, 1, appearance.Death.HideBodyAt).Dissolve == 1,
                    "death dissolves completely before hiding its body");
                Check(CosmeticRuntime.Surface(appearance, 1, false, firstPerson: true).Intensity
                    < CosmeticRuntime.Surface(appearance, 1, false).Intensity, "restrained first person");
                RenderOptions.CosmeticQuality = CosmeticEffectQuality.Off;
                var surface = CosmeticRuntime.Surface(appearance, 1, false);
                Check(surface.Skin != 0 && surface.Effect == 0, "quality off retains skin");
                RenderOptions.ShowCustomCosmetics = false;
                Check(CosmeticRuntime.Surface(appearance, 1, false) == default, "competitive mode hides all");
                // Every hunter's equipment must survive replication and retain the
                // same visibility rules in biped, alternate and first-person views.
                for (int h = 0; h < 7; h++)
                {
                    var hunter = (Hunter)h;
                    var selected = new CosmeticLoadout($"skin.{hunter.ToString().ToLowerInvariant()}.alimbic",
                        "armor.inferno", "death.quantum");
                    var outfit = new CosmeticAppearance(hunter, selected);
                    var remote = new NetCosmetics();
                    var update = CosmeticStatePacket.Create(h, hunter, 7, 3, 9, 1, outfit);
                    Check(remote.Accept(update, 3, 9, 7) && remote.Get(h, hunter, 7).Loadout == selected,
                        $"{hunter} remote outfit resolves");
                    foreach (var quality in Enum.GetValues<CosmeticEffectQuality>())
                    foreach (bool team in new[] { false, true })
                    foreach (bool alt in new[] { false, true })
                    {
                        RenderOptions.ShowCustomCosmetics = true;
                        RenderOptions.CosmeticQuality = quality;
                        var drawn = CosmeticRuntime.Surface(remote.Get(h, hunter, 7), 1, false, alt: alt, team: team);
                        Check(drawn.Skin == 2 && drawn.PreservePalette == team
                            && (drawn.Effect != 0) == (quality != CosmeticEffectQuality.Off),
                            $"{hunter} {quality} team={team} alt={alt} surface");
                        RenderOptions.ShowCustomCosmetics = false;
                        Check(CosmeticRuntime.Surface(outfit, 1, false, alt: alt, team: team) == default,
                            $"{hunter} hide immediately removes surface");
                    }
                }
                RenderOptions.ShowCustomCosmetics = true;
                RenderOptions.CosmeticQuality = CosmeticEffectQuality.High;
                var state = new DeathPresentationState();
                state.Observe(100, 1, 1, 0, appearance, Vector3.Zero, Vector3.UnitZ, false, 1);
                Check(!state.Active, "alive has no death presentation");
                state.Observe(0, 1, 1, 1, appearance, Vector3.Zero, Vector3.UnitZ, false, 1);
                Check(state.Active, "accepted lethal transition starts presentation");
                using (var saved = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, leaveOpen: true)) state.Write(writer);
                    saved.Position = 0;
                    var copy = new DeathPresentationState(); using var reader = new BinaryReader(saved);
                    copy.Read(reader);
                    Check(copy.Active && copy.Key == state.Key && copy.StartTime == state.StartTime && copy.Seed == state.Seed,
                        "death appendix preserves presentation across world checkpoint restore");
                    copy.Observe(100, 2, 1, 1.1f, appearance, Vector3.Zero, Vector3.UnitZ, false, 1);
                    Check(!copy.Active, "restored death clears on respawn");
                }
                Check(CosmeticRuntime.Seed(1, 2, 3) == CosmeticRuntime.Seed(1, 2, 3)
                    && CosmeticRuntime.Seed(1, 2, 3) != CosmeticRuntime.Seed(1, 2, 4), "stable occupant effect seeds");
                state.Observe(100, 2, 1, 1.1f, appearance, Vector3.Zero, Vector3.UnitZ, false, 1);
                Check(!state.Active, "rapid respawn clears presentation");
                state.Observe(0, 2, 2, 2, appearance, Vector3.Zero, Vector3.UnitZ, false, 2);
                Check(!state.Active, "new occupant cannot inherit death");
                Console.WriteLine($"[cosmeticscheck] {checks} catalog, persistence, wire, visibility and lifecycle checks passed");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("[cosmeticscheck] FAIL: " + ex); return 1; }
            finally
            {
                LauncherPrefs.Directory = previousDirectory; CosmeticPersistence.ResetCache();
                RenderOptions.ShowCustomCosmetics = previousVisible; RenderOptions.CosmeticQuality = previousQuality;
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }
    }
}
