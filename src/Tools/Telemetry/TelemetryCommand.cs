using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MphRead.Telemetry;

namespace MphRead
{
    internal static class TelemetryCommand
    {
        private const int MaxDecodedBytes = 64 * 1024 * 1024;
        private sealed class Cell
        {
            public int Samples, Kills, Deaths;
        }
        private sealed class Spawn
        {
            public int Count, Within3, Within5, Within10, Visible, DistanceSamples;
            public double Distance;
        }
        private sealed class Weapon
        {
            public int Pickups, Kills, KillsAfterPickup;
            public long Damage;
            public double PickupToKillSeconds;
        }
        private sealed class HunterStats
        {
            public int Samples, Kills, Deaths;
            public long Damage;
        }
        private readonly record struct Life(byte Slot, uint Epoch);

        public static int Run(string[] args)
        {
            if (args.Length != 4 || args[0] != "telemetry" || args[1] is not ("heatmap" or "spawn-safety" or "routes" or "weapon-control"))
                throw new ArgumentException("Tools telemetry heatmap|spawn-safety|routes|weapon-control INPUT.telemetry.json.gz OUTPUT_PREFIX");
            MatchTelemetry match = Read(args[2]);
            string prefix = Path.GetFullPath(args[3]);
            Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
            var cells = new Dictionary<(int X, int Z), Cell>();
            var spawns = new Dictionary<(int X, int Z), Spawn>();
            var lives = new Dictionary<Life, TelemetryEvent>();
            var pickups = new Dictionary<(Life Life, byte Weapon), uint>();
            var weapons = new Dictionary<byte, Weapon>();
            var hunters = new Dictionary<byte, HunterStats>();
            var objectives = new Dictionary<int, int>();
            var contested = new Dictionary<uint, uint>();
            var carriedFlags = new Dictionary<uint, Life>();
            var lastPosition = new Dictionary<Life, TelemetryEvent>();
            double flagDistance = 0, contestedSeconds = 0;
            int overtime = 0;
            foreach (TelemetryEvent entry in match.Events)
            {
                var coordinate = ((int)Math.Floor(entry.X / 4), (int)Math.Floor(entry.Z / 4));
                if (!cells.TryGetValue(coordinate, out Cell? cell)) cells.Add(coordinate, cell = new());
                var life = new Life(entry.Slot, entry.Life);
                if (!hunters.TryGetValue(entry.Hunter, out HunterStats? hunter)) hunters.Add(entry.Hunter, hunter = new());
                if (entry.Kind == TelemetryKind.Position)
                {
                    cell.Samples++; hunter.Samples++;
                    if (carriedFlags.ContainsValue(life) && lastPosition.TryGetValue(life, out TelemetryEvent prior))
                        flagDistance += Math.Sqrt(Math.Pow(entry.X - prior.X, 2) + Math.Pow(entry.Y - prior.Y, 2) + Math.Pow(entry.Z - prior.Z, 2));
                    lastPosition[life] = entry;
                }
                if (entry.Kind == TelemetryKind.Spawn)
                {
                    lives[life] = entry;
                    if (!spawns.TryGetValue(coordinate, out Spawn? spawn)) spawns.Add(coordinate, spawn = new());
                    spawn.Count++;
                    if (entry.VisibleEnemies > 0) spawn.Visible++;
                    if (entry.EnemyDistance is { } distance) { spawn.Distance += distance; spawn.DistanceSamples++; }
                }
                if (entry.Kind == TelemetryKind.Death)
                {
                    cell.Deaths++; hunter.Deaths++;
                    if (lives.Remove(life, out TelemetryEvent birth))
                    {
                        double seconds = unchecked(entry.Tick - birth.Tick) / 60d;
                        Spawn spawn = spawns[((int)Math.Floor(birth.X / 4), (int)Math.Floor(birth.Z / 4))];
                        if (seconds <= 3) spawn.Within3++;
                        if (seconds <= 5) spawn.Within5++;
                        if (seconds <= 10) spawn.Within10++;
                    }
                }
                if (entry.Kind is TelemetryKind.Kill or TelemetryKind.Damage)
                {
                    if (!weapons.TryGetValue(entry.Weapon, out Weapon? weapon)) weapons.Add(entry.Weapon, weapon = new());
                    if (entry.Kind == TelemetryKind.Kill)
                    {
                        cell.Kills++; hunter.Kills++; weapon.Kills++;
                        if (life.Epoch != 0 && pickups.TryGetValue((life, entry.Weapon), out uint pickup))
                        { weapon.KillsAfterPickup++; weapon.PickupToKillSeconds += unchecked(entry.Tick - pickup) / 60d; }
                    }
                    else
                    {
                        weapon.Damage += entry.Value;
                        // Damage records describe the victim position; attribute Hunter damage to the sampled attacker.
                        if (entry.OtherHunter < 7)
                        {
                            if (!hunters.TryGetValue(entry.OtherHunter, out HunterStats? source)) hunters.Add(entry.OtherHunter, source = new());
                            source.Damage += entry.Value;
                        }
                    }
                }
                if (entry.Kind != TelemetryKind.World) continue;
                objectives[entry.Value] = objectives.GetValueOrDefault(entry.Value) + 1;
                WorldSignalKind kind = (WorldSignalKind)entry.Value;
                if (kind == WorldSignalKind.OvertimeStarted) overtime++;
                if (kind == WorldSignalKind.FlagPickedUp) carriedFlags[entry.Subject] = life;
                if (kind is WorldSignalKind.FlagDropped or WorldSignalKind.FlagCaptured or WorldSignalKind.FlagReset) carriedFlags.Remove(entry.Subject);
                if (kind == WorldSignalKind.NodeContested)
                {
                    if (entry.Weapon == 1) contested.TryAdd(entry.Subject, entry.Tick);
                    else if (contested.Remove(entry.Subject, out uint began)) contestedSeconds += unchecked(entry.Tick - began) / 60d;
                }
                if (kind == WorldSignalKind.PickupConsumed && PickupWeapon(entry.Weapon) is { } picked)
                {
                    if (!weapons.TryGetValue(picked, out Weapon? weapon)) weapons.Add(picked, weapon = new());
                    weapon.Pickups++; pickups[(life, picked)] = entry.Tick;
                }
            }
            foreach (uint began in contested.Values) contestedSeconds += unchecked(match.EndTick - began) / 60d;
            var danger = spawns.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Z).Select(p => new
            {
                x = p.Key.X * 4, z = p.Key.Z * 4, count = p.Value.Count, deathsWithin3Seconds = p.Value.Within3,
                deathsWithin5Seconds = p.Value.Within5, deathsWithin10Seconds = p.Value.Within10,
                directLineOfSightSpawns = p.Value.Visible,
                averageEnemyDistance = p.Value.DistanceSamples == 0 ? (double?)null : p.Value.Distance / p.Value.DistanceSamples
            }).ToArray();
            long totalDamage = weapons.Values.Sum(w => (long)w.Damage);
            var summary = new
            {
                match.Id, match.Room, match.Mode, match.Completed, match.DroppedEvents,
                durationSeconds = unchecked(match.EndTick - match.StartTick) / 60d, overtimeTransitions = overtime,
                coordinates = "map units, X/Z horizontal bins of width 4; Y retained in events CSV",
                limitations = "Sampled routes approximate distance; unseen cells are not proof of walkable unused space. Incomplete/dropped telemetry is not a balance baseline. Hunter wins and team bias require validated match reports.",
                spawnDanger = danger,
                weapons = weapons.OrderBy(p => p.Key).Select(p => new { weapon = p.Key, p.Value.Pickups, p.Value.Kills, p.Value.Damage,
                    p.Value.KillsAfterPickup, damageShare = totalDamage == 0 ? (double?)null : (double)p.Value.Damage / totalDamage,
                    averagePickupToKillSeconds = p.Value.KillsAfterPickup == 0 ? (double?)null : p.Value.PickupToKillSeconds / p.Value.KillsAfterPickup }),
                hunters = hunters.Where(p => p.Key < 7).OrderBy(p => p.Key).Select(p => new { hunter = p.Key, p.Value.Kills, p.Value.Deaths,
                    sampledActiveSeconds = p.Value.Samples, p.Value.Damage,
                    damagePerMinute = p.Value.Samples == 0 ? (double?)null : p.Value.Damage * 60d / p.Value.Samples }),
                objectiveTransitions = objectives.OrderBy(p => p.Key).Select(p => new { kind = ((WorldSignalKind)p.Key).ToString(), count = p.Value }),
                sampledFlagCarryDistance = flagDistance, nodeContestedSeconds = contestedSeconds
            };
            File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            using (var csv = new StreamWriter(prefix + ".csv", false, new UTF8Encoding(false)))
            {
                csv.WriteLine("tick,kind,slot,life,x,y,z,team,hunter,weapon,value,subject,other_slot");
                foreach (TelemetryEvent e in match.Events)
                    csv.WriteLine(FormattableString.Invariant($"{e.Tick},{e.Kind},{e.Slot},{e.Life},{e.X:R},{e.Y:R},{e.Z:R},{e.Team},{e.Hunter},{e.Weapon},{e.Value},{e.Subject},{e.OtherSlot}"));
            }
            WriteSvg(prefix + ".svg", cells, args[1]);
            Console.WriteLine($"Wrote {prefix}.json, .csv, .svg; events={match.Events.Length}, dropped={match.DroppedEvents}");
            return 0;
        }

        internal static byte? PickupWeapon(byte item) => item switch { 5 => 1, 7 => 3, 8 => 4, 9 => 5, 10 => 6, 11 => 7, 12 => 8, _ => null };

        internal static MatchTelemetry Read(string path)
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            byte[] buffer = new byte[65536];
            int read;
            while ((read = gzip.Read(buffer)) != 0)
            {
                if (decoded.Length + read > MaxDecodedBytes) throw new InvalidDataException("Telemetry exceeds decoded byte limit.");
                decoded.Write(buffer, 0, read);
            }
            MatchTelemetry match = JsonSerializer.Deserialize<MatchTelemetry>(decoded.GetBuffer().AsSpan(0, (int)decoded.Length))
                ?? throw new InvalidDataException("Missing telemetry.");
            // Duration uses unsigned tick subtraction across wrap; intervals above
            // the signed half-range (~414 days at 60 Hz) are intentionally unsupported.
            if (match.Format != MatchTelemetry.CurrentFormat || match.Id == Guid.Empty || match.Room is not { Length: > 0 and <= 256 }
                || match.Events == null || match.Events.Length > MatchTelemetry.MaxEvents || match.DroppedEvents < 0
                || unchecked(match.EndTick - match.StartTick) > int.MaxValue)
                throw new InvalidDataException("Invalid telemetry header.");
            // Different journals drain in batches, so timestamps need not be globally sorted in the file.
            foreach (TelemetryEvent e in match.Events)
            {
                if (e.Kind > TelemetryKind.World || !float.IsFinite(e.X) || !float.IsFinite(e.Y) || !float.IsFinite(e.Z)
                    || Math.Abs(e.X) > 100000 || Math.Abs(e.Y) > 100000 || Math.Abs(e.Z) > 100000
                    || unchecked(e.Tick - match.StartTick) > unchecked(match.EndTick - match.StartTick)
                    || e.Value < 0 || e.Value > 65535 || e.Slot is > 7 and not 255 || e.OtherSlot is > 7 and not 255
                    || e.EnemyDistance is { } d && (!float.IsFinite(d) || d < 0))
                    throw new InvalidDataException("Invalid telemetry event.");
            }
            return match with { Events = match.Events.OrderBy(e => unchecked(e.Tick - match.StartTick)).ToArray() };
        }

        private static void WriteSvg(string path, Dictionary<(int X, int Z), Cell> cells, string mode)
        {
            int minX = cells.Count == 0 ? 0 : cells.Keys.Min(k => k.X), minZ = cells.Count == 0 ? 0 : cells.Keys.Min(k => k.Z);
            int maxX = cells.Count == 0 ? 1 : cells.Keys.Max(k => k.X), maxZ = cells.Count == 0 ? 1 : cells.Keys.Max(k => k.Z);
            int Value(Cell c) => mode == "routes" ? c.Samples : mode == "spawn-safety" ? c.Deaths : c.Kills;
            int maximum = Math.Max(1, cells.Values.Select(Value).DefaultIfEmpty(0).Max());
            using var svg = new StreamWriter(path, false, new UTF8Encoding(false));
            svg.WriteLine(FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minX * 4} {minZ * 4} {(maxX - minX + 1) * 4} {(maxZ - minZ + 1) * 4}\"><title>Prime Hunters {mode}; map X/Z, four-unit cells</title>"));
            foreach (var (point, cell) in cells.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Z))
            {
                int value = Value(cell);
                string opacity = (.1 + .9 * value / maximum).ToString("F3", CultureInfo.InvariantCulture);
                svg.WriteLine(FormattableString.Invariant($"<rect x=\"{point.X * 4}\" y=\"{point.Z * 4}\" width=\"4\" height=\"4\" fill=\"#ee5533\" fill-opacity=\"{opacity}\"><title>X={point.X * 4}, Z={point.Z * 4}; samples={cell.Samples}, kills={cell.Kills}, deaths={cell.Deaths}</title></rect>"));
            }
            svg.WriteLine("</svg>");
        }
    }
}
