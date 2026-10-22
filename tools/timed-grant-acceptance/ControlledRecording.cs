using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Controlled authored-map contact, not a natural AI or live UDP campaign.
// No TimedGrantFact, presentation.Accept or fake replay records are created here.
internal static class ControlledRecording
{
    internal readonly record struct Files(string Recording, string Clip);
    internal static Files? Run(string effect, Scene scene, PlayerEntity owner,
        Action<uint> frame, Action<int, string> capture, Acceptance.Report report)
    {
        string[] names = { "DoubleDamage", "Cloak", "DeathAlt" };
        int type = Array.IndexOf(names, effect);
        if (type < 0) throw new ArgumentException("--record-authored-effect must be DoubleDamage, Cloak or DeathAlt.");
        ItemType itemType = type == 0 ? ItemType.DoubleDamage : type == 1 ? ItemType.Cloak : ItemType.Deathalt;
        Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Host"));
        Reflection.Session(nameof(NetSession.IsAuthority), true);
        object? previousSink = Reflection.Get(typeof(NetSession), "_snapshotSink");
        int snapshots = 0;
        SnapshotSink sink = payload =>
        {
            snapshots++;
            report.Stages.Add(new { stage = "controlled-production-snapshot", frame = NetSession.NetFrame,
                bytes = payload.Length, sha256 = Convert.ToHexString(SHA256.HashData(payload)) });
        };
        Reflection.Set(typeof(NetSession), "_snapshotSink", sink);
        bool started = false;
        try
        {
            // Register only real authored item actors already loaded by this map.
            // They initialized offline before fixture authority was established.
            foreach (var entity in scene.Entities)
                if (entity is ItemInstanceEntity item && item.Owner != null && item.Owner.Id >= 0)
                    Reflection.Static("NetPowerUpLedger", "Register", item);
            if (!(bool)Reflection.Static("DemoRecorder", "Start")!)
                throw new InvalidOperationException("Production recorder refused controlled native authority.");
            started = true;
            uint clock = 100;
            void Step()
            {
                frame(++clock);
                owner.Controls.ClearAll();
                scene.OnSimulationFrame();
                // BroadcastSnapshot is the production serializer/recorder path
                // used by dedicated authority. Host simulation remains native and
                // visible; switch role only for this actual snapshot publication.
                Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Server"));
                try { NetSession.BroadcastSnapshot(); }
                finally { Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Host")); }
                Reflection.Static("ReplayCapture", "AfterSimulation", scene);
            }
            for (int i = 0; i < 30; i++) Step();
            object world = Reflection.Get(Reflection.Type("Mods.Network.ReplayCapture"), "WorldCapture")!;
            if (snapshots == 0 || Reflection.Get(world, "World") == null)
            {
                report.Missing.Add("NOT_RUN: controlled authored recording has no proven production snapshot/bootstrap world.");
                return null;
            }
            ItemInstanceEntity? selected = null;
            foreach (var entity in scene.Entities)
                if (entity is ItemInstanceEntity item && item.ItemType == itemType && item.DespawnTimer != 0
                    && item.Owner != null && item.Owner.Id >= 0) { selected = item; break; }
            if (selected == null)
            {
                report.Missing.Add("NOT_RUN: loaded authored room contains no available " + effect + " item actor.");
                return null;
            }
            Reflection.Static("NetPowerUpLedger", "Register", selected);
            Vector3 itemPosition = selected.Position;
            int spawner = selected.Owner!.Id;
            Reflection.Call(owner, "ModPlaceAt", itemPosition);
            owner.Speed = Vector3.Zero;
            Step();
            object ledger = Reflection.Get(Reflection.Type("Mods.Network.NetPowerUpLedger"), "Ledger")!;
            object? grant = null;
            var life = ShotKey.For(owner.SlotIndex, 0);
            foreach (object entry in (IEnumerable)Reflection.Get(ledger, "History")!)
                if (Reflection.Get(entry, "Type")!.ToString() == effect
                    && Equals(Reflection.Get(entry, "Life"), life)
                    && Reflection.Get(entry, "EndReason")!.ToString() == "Active") grant = entry;
            if (grant == null || selected.DespawnTimer != 0)
                throw new InvalidOperationException("Actual authored native contact did not produce and consume a matching authority grant.");
            report.Stages.Add(new { stage = "controlled-authored-native-contact", effect, room = scene.Room!.Meta.Name,
                mapHash = Reflection.Static("ReplayMapIdentity", "Compute", scene.Room.Meta.Name),
                fixturePositioning = true, positioning = itemPosition.ToString(), spawner,
                role = NetSession.Role.ToString(), authority = NetSession.IsAuthority, liveUdp = false,
                sequence = Reflection.Get(grant, "Sequence"), spawn = Reflection.Get(grant, "Spawn")!.ToString(),
                life = life.ToString(), contact = Reflection.Get(grant, "ContactFrame"),
                sourceContact = Reflection.Get(grant, "SourceContactFrame"), end = Reflection.Get(grant, "EndFrame"),
                loadedAssetProfile = Directory.GetCurrentDirectory() });
            capture(type, "controlled-authored-contact");
            for (int i = 0; i < 60; i++) Step();
            capture(type, "controlled-authority-positive");
            string? recording = Reflection.Get(Reflection.Type("Mods.Network.DemoRecorder"), "CurrentPath") as string;
            Reflection.Set(Reflection.Type("Mods.Network.DemoClip"), "PostRollSeconds", 0);
            string? clip = Reflection.Static("DemoClip", "Save") as string;
            Reflection.Static("DemoRecorder", "Stop"); started = false;
            object? writer = Reflection.Get(Reflection.Type("Mods.Network.DemoRecorder"), "_lastWriter");
            if (writer == null || Reflection.Get(writer, "Completion") is not Task completion || !completion.Wait(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("Production recording writer did not complete its bounded flush.");
            Reflection.Static("DemoRecorder", "PollSaves");
            var wait = Stopwatch.StartNew();
            while (Reflection.Bool(Reflection.Type("Mods.Network.DemoClip"), "IsSaving") && wait.Elapsed < TimeSpan.FromSeconds(10))
            { Reflection.Static("DemoClip", "Tick", scene.Size); Thread.Sleep(10); }
            if (Reflection.Get(writer, "Error") != null || Reflection.Get(Reflection.Type("Mods.Network.DemoRecorder"), "LastError") != null
                || Reflection.Get(Reflection.Type("Mods.Network.DemoClip"), "LastError") != null)
                throw new InvalidOperationException("Production recording or clip writer reported failure.");
            if (recording == null || clip == null || !File.Exists(recording) || !File.Exists(clip))
            {
                report.Missing.Add("NOT_RUN: controlled native production recording/clip file finalization unavailable.");
                return null;
            }
            string Preserve(string source, string suffix)
            {
                string destination = Path.Combine(report.Output, effect + "-authored-" + suffix + Path.GetExtension(source));
                File.Copy(source, destination, overwrite: false);
                report.Stages.Add(new { stage = "controlled-production-replay-file", effect, suffix,
                    source, path = destination, bytes = new FileInfo(destination).Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))), snapshots });
                return destination;
            }
            return new(Preserve(recording, "recording"), Preserve(clip, "clip"));
        }
        finally
        {
            if (started) Reflection.Static("DemoRecorder", "Stop");
            Reflection.Set(typeof(NetSession), "_snapshotSink", previousSink);
            Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Client"));
            Reflection.Session(nameof(NetSession.IsAuthority), false);
        }
    }
}
