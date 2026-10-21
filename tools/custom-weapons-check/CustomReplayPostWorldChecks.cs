using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
// Storage/parser fixtures only, not live authority recording or playback admission.
internal static class CustomReplayPostWorldChecks
{
    internal static int Run(string output)
    {
        int checks=0;
        output=Path.GetFullPath(output);
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidDataException(label);
            checks++;
        }
        void Reject(Action action,string label)
        {
            try
            {
                action();
            }
            catch(InvalidDataException)
            {
                Check(true,label);
                return;
            }
            throw new InvalidDataException("Expected InvalidDataException: "+label);
        }
        try
        {
            Directory.CreateDirectory(output);
            using var f=new CustomReplayStateChecks.Fixture();
            var context=new ReplayCustomWeaponContext(ReplayCustomWeaponBootstrap.ForAuthority(f.World.Start,f.World.AuthorityFrame,42,f.Context.Layout.Digest,f.Context.Bootstrap.Packages),f.Context.Definition,f.Context.Layout);
            var descriptor=ReplayCustomWeaponDescriptor.ForInspection(context);
            var state=new ReplayReplicaState();
            state.EnableCustomInspection(descriptor);
            var match = new MatchStatePacket
            {
                MatchId = f.World.Start.MatchId,
                AuthorityEpoch = f.World.Start.AuthorityEpoch,
                RoomKey = "MP3 PROVING GROUND",
                NextRoomKey = "",
                Mode = (byte)GameMode.Battle,
                RuleBits = (uint)(MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable)
            };
            byte[] matchBytes=new byte[1+MatchStatePacket.Size];
            matchBytes[0]=(byte)PacketType.MatchState;
            match.Write(matchBytes.AsSpan(1));
            state.Accept(matchBytes,1);
            state.Accept(ReplayCustomWeaponFact.Bootstrap(context).CopyBytes(),10);
            state.Accept(ReplayCustomWeaponFact.Baseline(context,2,new(f.World.Start.MatchId,f.World.Start.AuthorityEpoch,context.Bootstrap.Packages.MechanicsDigest,1001,8,0),f.Slots,Array.Empty<CustomReplicaFlight>()).CopyBytes(),11);
            for(int i=0;i<context.Layout.Count;i++)state.Accept(ReplayCustomWeaponFact.Pickup(context,state.CustomWeapons!.Sequence+1,1001,i,new(new(context.PickupWorld,context.Layout.Placements[i].SpawnId,1),990,true,0)).CopyBytes(),12);
            var initial=state.CaptureCheckpoint();
            uint origin=state.RecordingFrame;
            byte[] Envelope(ReplayReplicaCheckpoint current,uint frame)
            {
                using var stream=new MemoryStream();
                using var writer=new BinaryWriter(stream);
                void Component(byte[] bytes)
                {
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
                writer.Write(0x43575050u);
                writer.Write((ushort)12);
                writer.Write((byte)15);
                writer.Write(ReplayWorldCheckpoint.ComputeContract(stable:true));
                writer.Write(match.RoomKey);
                writer.Write((int)GameMode.Battle);
                writer.Write(123ul);
                writer.Write(frame);
                writer.Write(123u);
                writer.Write(456u);
                Component(initial.Bytes.ToArray());
                Component(current.Bytes.ToArray());
                writer.Write(0);
                for(int i=0;i<5;i++)Component(Array.Empty<byte>());
                Component(new byte[8*57]);
                Component(new byte[4]);
                Component(new byte[184]);
                Component(ReplayWorldCheckpoint.CustomWorldAppendix(current));
                writer.Write(false);
                return stream.ToArray();
            }
            byte[] world=Envelope(initial,origin);
            using var worldValue=ReplayWorldCheckpoint.FromCustomBytes(world);
            ReplayMetadata Metadata(byte version) => new()
            {
                FormatVersion = version,
                ProtocolVersion = 85,
                Type = ReplayType.FullMatch,
                RoomKey = match.RoomKey,
                Mode = GameMode.Battle,
                MapHash = 123,
                OriginRecordingFrame = origin,
                CustomWeapons = descriptor,
                Bootstrap = new() { Packets = new[] { ReplayTimelineArchive.Construction(initial) } },
                WorldCheckpoint = world,
                IndexedFireContinuation = false
            };
            var metadata=Metadata(8);
            byte[] inner=ReplayCustomWeaponFact.State(context,state.CustomWeapons!.Sequence+1,1002,f.Slots).CopyBytes();
            byte[] post=ReplayPostWorldRecord.Encode(inner,context);
            var golden=new byte[8+inner.Length];
            golden[0]=249;
            golden[1]=1;
            golden[2]=1;
            BinaryPrimitives.WriteInt32LittleEndian(golden.AsSpan(4),inner.Length);
            inner.CopyTo(golden,8);
            Check(ReplayPostWorldRecord.Tag==249&&ReplayPostWorldRecord.HeaderBytes==8&&ReplayPostWorldRecord.MaximumBytes==ReplayCustomWeaponFact.MaximumBytes+8&&post.SequenceEqual(golden),"independent tag249 version1 phase1 reserved0 int32LE envelope");
            Check(ReplayPostWorldRecord.Read(post,context).SequenceEqual(inner),"post-world envelope preserves exact supplied custom fact");
            var copy=ReplayPostWorldRecord.Read(post,context);
            copy.AsSpan().Clear();
            Check(ReplayPostWorldRecord.Read(post,context).SequenceEqual(inner),"decoded inner ownership independent of source bytes");
            byte[] timedInner = new ReplayTimedShotScope(f.World.Start.AuthorityEpoch,
                f.World.Start.MatchId, 0, TimedGrantProofMode.Shadow, 1002).Packet();
            byte[] timedPost = ReplayPostWorldRecord.Encode(timedInner, context);
            Reject(() => ReplayFormatV3.ValidateArchiveRecord(timedPost, context, 8, timed: false),
                "phase envelope cannot bypass undeclared timed-shot capability");
            ReplayFormatV3.ValidateArchiveRecord(timedPost, context, 8, timed: true);
            Check(true, "declared timed-shot syntax remains available inside post-world phase");
            Reject(() => ReplayFormatV3.ValidateArchiveRecord(post, context, 6, timed: false),
                "native archive cannot acquire phase contract through a supplied context");
            var other=new ReplayCustomWeaponContext(ReplayCustomWeaponBootstrap.ForAuthority(f.World.Start,f.World.AuthorityFrame,43,context.Layout.Digest,context.Bootstrap.Packages),context.Definition,context.Layout);
            Reject(()=>ReplayPostWorldRecord.Read(post,other),"wrong exact custom context refused");
            Reject(()=>ReplayPostWorldRecord.Read(post,null),"custom inner requires explicit context");
            Reject(()=>ReplayPostWorldRecord.Encode(post,context),"nested phase envelope refused");
            foreach(byte[] forbidden in new[]
            {
                new byte[] { 255 },new byte[] { 253 },new byte[] { 249 },new byte[] { 248 },Array.Empty<byte>()
            }
            )Reject(()=>ReplayPostWorldRecord.Encode(forbidden,context),"forbidden quiet/construction/nested/unknown/empty inner "+(forbidden.Length==0?-1:forbidden[0]));
            for(int n=0;n<post.Length;n++)Reject(()=>ReplayPostWorldRecord.Read(post[..n],context),"envelope truncation "+n);
            Reject(()=>ReplayPostWorldRecord.Read(post.Concat(new byte[] { 0 }).ToArray(),context),"envelope trailing bytes");
            foreach(int offset in new[] { 0,1,2,3,4,7 })
            {
                var bad=(byte[])post.Clone();
                bad[offset]^=128;
                Reject(()=>ReplayPostWorldRecord.Read(bad,context),"envelope signature/version/phase/reserved/length "+offset);
            }
            var badLength=(byte[])post.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(badLength.AsSpan(4),-1);
            Reject(()=>ReplayPostWorldRecord.Read(badLength,context),"negative inner length");
            Reject(()=>ReplayPostWorldRecord.Read(new byte[ReplayPostWorldRecord.MaximumBytes+1],context),"bounded envelope allocation");
            // Canonical recorder SlotIntent bytes, not a live compact transport carrier.
            var intent = new IntentPacket
            {
                MatchId = f.World.Start.MatchId,
                AuthorityEpoch = f.World.Start.AuthorityEpoch,
                SlotGeneration = 5,
                LifeId = 10,
                Frame = 1002,
                Aim = OpenTK.Mathematics.Vector3.UnitZ,
                Target = NetTargetIdentity.None,
                Buttons = IntentButtons.InPlayState
            };
            byte[] intentBytes = new byte[2 + IntentPacket.TouchCanonicalSize];
            intentBytes[0] = (byte)PacketType.SlotIntent;
            intentBytes[1] = 1;
            intent.Write(intentBytes.AsSpan(2));
            byte[] wrappedIntent = ReplayPostWorldRecord.Encode(intentBytes, context);
            Check(ReplayPostWorldRecord.Read(wrappedIntent, context).SequenceEqual(intentBytes),
                "canonical SlotIntent retains exact recorder source bytes");
            byte[] compact = new byte[2048];
            compact[0] = (byte)PacketType.SlotIntent;
            compact[1] = 1;
            int compactLength = CompactIntentCodec.Write(intent, compact.AsSpan(2));
            Reject(() => ReplayPostWorldRecord.Encode(compact[..(2 + compactLength)], context),
                "actual live compact intent carrier cannot masquerade as canonical recorder row");
            var badSlot = (byte[])intentBytes.Clone();
            badSlot[1] = 8;
            Reject(() => ReplayPostWorldRecord.Encode(badSlot, context), "SlotIntent invalid slot");
            Reject(() => ReplayPostWorldRecord.Encode(intentBytes[..(2 + IntentPacket.LegacyFullSize)], context),
                "compact/short intent is not a canonical recorder row");
            foreach (byte count in new byte[] { 1, (byte)(NetFireEvents.Capacity + 1) })
            {
                var invalidFire = (byte[])intentBytes.Clone();
                invalidFire[2 + IntentPacket.LegacyFullSize] = count;
                Reject(() => ReplayPostWorldRecord.Encode(invalidFire, context),
                    "canonical intent rejects invalid fire count/default zero ShotId " + count);
            }

            // Tracker-only adversaries isolate arithmetic from packet syntax validation.
            Check(ReplayPostWorldFrameState.MaximumRecords == 4096
                && ReplayPostWorldFrameState.MaximumBytes == 8 * 1024 * 1024,
                "fixed combined frame storage budgets");
            var recordBudget = new ReplayPostWorldFrameState();
            for (int i = 0; i < 4096; i++) recordBudget.Observe(0, post);
            Reject(() => recordBudget.Observe(0, post), "4097th same-frame deferred record refused");
            Reject(() => recordBudget.Observe(1, post), "previous deferred plus current record share 4096 budget");
            recordBudget.Observe(2, post);
            Check(true, "nonadjacent frontier retires previous deferred budget");
            var byteBudget = new ReplayPostWorldFrameState();
            byte[] counterPayload = new byte[16384];
            counterPayload[0] = ReplayPostWorldRecord.Tag;
            for (int i = 0; i < 512; i++) byteBudget.Observe(0, counterPayload);
            Reject(() => byteBudget.Observe(0, new byte[] { ReplayPostWorldRecord.Tag }),
                "exact 8MiB tracker boundary refuses one more byte");
            Reject(() => byteBudget.Observe(1, post), "deferred byte budget carries to adjacent current frame");
            var order = new ReplayPostWorldFrameState();
            Reject(() => order.Observe(0, intentBytes), "ordinary frame0 packet cannot follow complete construction");
            order.Observe(1, intentBytes);
            order.Observe(1, post);
            Reject(() => order.Observe(1, intentBytes), "ordinary packet cannot follow deferred phase at same frame");
            order.Observe(1, new byte[] { 255 });
            Reject(() => order.Observe(1, post), "quiet frontier is final for its frame");
            Reject(() => order.Observe(0, post), "tracker refuses frame rollback");
            var combined = new ReplayPostWorldFrameState();
            for (int i = 0; i < 2048; i++) combined.Observe(0, post);
            for (int i = 0; i < 2048; i++) combined.Observe(1, intentBytes);
            Reject(() => combined.Observe(1, post), "combined prior deferred and ordinary current records bounded");

            byte[] encoded=ReplayFormatV3.EncodeMetadata(metadata);
            Check(ReplayFormatV3.DecodeMetadata(85,encoded,8).FormatVersion==8,"format8 metadata owns exact custom contract");
            state.Accept(inner,origin+1);
            byte[] laterWorld=Envelope(state.CaptureCheckpoint(),origin+1);
            string path=Path.Combine(output,"phase-storage.ppdemo");
            using(var writer=new ReplayWriterV3(path,metadata))
            {
                writer.WriteRecord(0,post);
                writer.WriteRecord(0,post);
                writer.WriteRecord(1,post);
                writer.WriteCheckpoint(1,laterWorld);
                writer.WriteRecord(3,new byte[] { 255 });
            }
            using(var reader=DemoReader.Open(path,out var opened))
            {
                Check(reader!=null&&opened==ReplayOpenResult.Success&&reader.Metadata!.FormatVersion==8,"actual closed format8 storage reopens");
                var first=reader!.SeekAtOrAfter(0);
                Check(first is { } a&&a.Frame==0&&a.Data.SequenceEqual(post)&&reader.ReadNext() is { } b&&b.Frame==0,"inclusive frame0 seek preserves both late phase records in original order");
                var inclusive=reader.SeekAtOrAfter(1);
                Check(inclusive is { } c&&c.Frame==1&&c.Data.SequenceEqual(post),"inclusive seek retains packet at checkpoint frontier");
                Check(reader.SeekAfter(1) is { } d&&d.Frame==3,"existing exclusive seek retains its native semantics");
                Check(reader.Checkpoints.Count==1&&reader.ReadCheckpoint(reader.Checkpoints[0]).SequenceEqual(laterWorld),"PCW1 optional checkpoint remains exact content-free world12 bytes");
            }
            // Force two physical chunks at equal frame K without manufacturing game facts.
            var flush = typeof(ReplayWriterV3).GetMethod("FlushChunk",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            string split = Path.Combine(output, "equal-frame-chunks.ppdemo");
            using (var writer = new ReplayWriterV3(split, metadata))
            {
                writer.WriteRecord(0, post);
                writer.WriteRecord(1, intentBytes);
                flush.Invoke(writer, null);
                writer.WriteRecord(1, post);
                writer.WriteRecord(1, post);
                writer.WriteRecord(3, new byte[] { 255 });
            }
            using (var reader = DemoReader.Open(split, out var opened))
            {
                Check(reader != null && opened == ReplayOpenResult.Success, "split-chunk file opens");
                var firstAtK = reader!.SeekAtOrAfter(1);
                Check(firstAtK is { } atK && atK.Frame == 1 && atK.Data.SequenceEqual(intentBytes),
                    "inclusive seek selects earlier chunk ending at K, not later chunk starting K");
                Check(reader.ReadNext() is { } lateA && lateA.Frame == 1 && lateA.Data.SequenceEqual(post)
                    && reader.ReadNext() is { } lateB && lateB.Frame == 1 && lateB.Data.SequenceEqual(post),
                    "inclusive seek retains all equal-frame packets across physical chunk boundary");
            }
            var largeFlights = Enumerable.Range(1, 64).Select(i => f.Flight((uint)i)).ToArray();
            byte[] largePost = ReplayPostWorldRecord.Encode(ReplayCustomWeaponFact.Baseline(context, 2,
                new(f.World.Start.MatchId, f.World.Start.AuthorityEpoch,
                    context.Bootstrap.Packages.MechanicsDigest, 1001, 8, largeFlights.Length),
                f.Slots, largeFlights).CopyBytes(), context);
            int maximumLargeRecords = ReplayPostWorldFrameState.MaximumBytes / largePost.Length;
            Check(maximumLargeRecords < ReplayPostWorldFrameState.MaximumRecords,
                "large valid syntax fixture reaches byte cap before record cap");
            using (var writer = new ReplayWriterV3(Path.Combine(output, "writer-byte-budget.ppdemo"), metadata))
            {
                for (int i = 0; i < maximumLargeRecords; i++) writer.WriteRecord(0, largePost);
                Reject(() => writer.WriteRecord(0, largePost),
                    "writer8MiB budget survives automatic2MiB physical chunk flushes");
                writer.Abort();
            }
            string writerBudget = Path.Combine(output, "writer-cross-chunk-budget.ppdemo");
            using (var writer = new ReplayWriterV3(writerBudget, metadata))
            {
                for (int i = 0; i < 2048; i++) writer.WriteRecord(0, post);
                flush.Invoke(writer, null);
                for (int i = 0; i < 2048; i++) writer.WriteRecord(0, post);
                Reject(() => writer.WriteRecord(0, post), "writer4097 limit persists across physical chunks");
                Reject(() => writer.WriteRecord(1, post), "writer carries deferred budget across next-frame chunk");
                writer.Abort();
            }
            using(var writer=new ReplayWriterV3(Path.Combine(output,"reject-v7.ppdemo"),Metadata(7)))Reject(()=>writer.WriteRecord(0,post),"format7 cannot silently acquire phase249 contract");
            string extracted=Path.Combine(output,"must-not-extract.ppdemo");
            Check(ReplayArchive.Extract(path,0,3,extracted)==ReplayOpenResult.UnsupportedFormat&&!File.Exists(extracted),"format8 extraction remains explicitly closed");
            void Read(string file,string label)
            {
                using var reader=DemoReader.Open(file,out var opened);
                Check(reader!=null&&opened==ReplayOpenResult.Success&&reader.Metadata!.FormatVersion==8&&reader.Metadata.CustomWeapons!.CopyBytes().SequenceEqual(descriptor.CopyBytes()),label+" format8 descriptor preserved");
                int count=0;
                while(reader!.ReadNext() is { } record)
                {
                    if(record.Data.Length>0&&record.Data[0]==249)Check(ReplayPostWorldRecord.Read(record.Data,context).SequenceEqual(inner),label+" exact phased inner");
                    count++;
                }
                Check(reader.LastResult==ReplayOpenResult.Success&&count==4,label+" all records retained");
            }
            string copied=Path.Combine(output,"copied.ppdemo");
            Check(ReplayArchive.WithExpectedHashes(path,copied,Array.Empty<ReplayExpectedHash>())==ReplayOpenResult.Success,"storage-only copy succeeds without invented state hashes");
            Read(copied,"copy");
            Check(ReplayArchive.Recover(path,out var recovered,out _),"format8 complete-file recovery succeeds");
            Read(recovered!,"recovery");
            uint Crc(byte[] bytes)
            {
                uint value=uint.MaxValue;
                foreach(byte b in bytes)
                {
                    value^=b;
                    for(int i=0;i<8;i++)value=(value&1)!=0?(value>>1)^0xedb88320u:value>>1;
                }
                return ~value;
            }
            string Raw(string name,byte version,byte[] second)
            {
                var meta=Metadata(version);
                byte[] header=ReplayFormatV3.EncodeMetadata(meta);
                using var raw=new MemoryStream();
                using(var w=new BinaryWriter(raw,Encoding.UTF8,true))
                {
                    w.Write(0u);
                    w.Write((ushort)post.Length);
                    w.Write(post);
                    w.Write(1u);
                    w.Write((ushort)second.Length);
                    w.Write(second);
                }
                byte[] payload=raw.ToArray();
                using var zipped=new MemoryStream();
                using(var zip=new DeflateStream(zipped,CompressionLevel.Fastest,true))zip.Write(payload);
                string file=Path.Combine(output,name);
                using(var w=new BinaryWriter(File.Create(file)))
                {
                    w.Write(DemoFile.Magic);
                    w.Write(version);
                    w.Write((byte)85);
                    w.Write(header.Length);
                    w.Write(Crc(header));
                    w.Write(header);
                    w.Write(0x334b4843u);
                    w.Write(0u);
                    w.Write(1u);
                    w.Write(2u);
                    w.Write((int)zipped.Length);
                    w.Write(payload.Length);
                    w.Write(Crc(payload));
                    w.Write(zipped.ToArray());
                }
                return file;
            }
            string RawChunks(string name, params (uint Frame, byte[] Packet)[][] chunks)
            {
                string file = Path.Combine(output, name);
                byte[] header = ReplayFormatV3.EncodeMetadata(metadata);
                using var container = new BinaryWriter(File.Create(file));
                container.Write(DemoFile.Magic);
                container.Write((byte)8);
                container.Write((byte)85);
                container.Write(header.Length);
                container.Write(Crc(header));
                container.Write(header);
                foreach (var chunk in chunks)
                {
                    using var raw = new MemoryStream();
                    using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
                    {
                        foreach (var row in chunk)
                        {
                            writer.Write(row.Frame);
                            writer.Write((ushort)row.Packet.Length);
                            writer.Write(row.Packet);
                        }
                    }
                    byte[] payload = raw.ToArray();
                    using var packed = new MemoryStream();
                    using (var zip = new DeflateStream(packed, CompressionLevel.Fastest, true))
                        zip.Write(payload);
                    container.Write(0x334b4843u);
                    container.Write(chunk[0].Frame);
                    container.Write(chunk[^1].Frame);
                    container.Write((uint)chunk.Length);
                    container.Write((int)packed.Length);
                    container.Write(payload.Length);
                    container.Write(Crc(payload));
                    container.Write(packed.ToArray());
                }
                return file;
            }
            var fullDeferred = Enumerable.Repeat((Frame: 0u, Packet: post), 4096).ToArray();
            string rawCarry = RawChunks("crc-valid-cross-chunk-carry.ppdemo.part", fullDeferred,
                new[] { (Frame: 1u, Packet: post) });
            using (var reader = DemoReader.Open(rawCarry, out var opened))
            {
                Check(reader != null && opened == ReplayOpenResult.Success,
                    "CRC-correct cross-chunk budget adversary opens metadata");
                int admitted = 0;
                while (reader!.ReadNext() != null) admitted++;
                Check(admitted == 4096 && reader.LastResult == ReplayOpenResult.Corrupt,
                    "reader carries complete previous deferred4096 into next chunk before exposing prefix");
            }
            using (var reader = DemoReader.Open(rawCarry, out _))
            {
                Check(reader != null && reader.SeekAtOrAfter(1) == null
                    && reader.LastResult == ReplayOpenResult.Corrupt,
                    "inclusive target-chunk seek cannot bypass previous deferred budget");
            }
            string rawSameFrame = RawChunks("crc-valid-same-frame-overflow.ppdemo.part",
                Enumerable.Repeat((Frame: 0u, Packet: post), 2048).ToArray(),
                Enumerable.Repeat((Frame: 0u, Packet: post), 2049).ToArray());
            using (var reader = DemoReader.Open(rawSameFrame, out _))
            {
                int admitted = 0;
                while (reader!.ReadNext() != null) admitted++;
                Check(admitted == 2048 && reader.LastResult == ReplayOpenResult.Corrupt,
                    "second same-frame chunk validated as whole against previous chunk budget");
            }
            string rawOrdinaryZero = RawChunks("crc-valid-ordinary-zero.ppdemo.part",
                new[] { (Frame: 0u, Packet: intentBytes) });
            using (var reader = DemoReader.Open(rawOrdinaryZero, out _))
                Check(reader != null && reader.ReadNext() == null && reader.LastResult == ReplayOpenResult.Corrupt,
                    "CRC-correct ordinary frame0 packet cannot reinterpret complete baseline");

            foreach(int offset in new[] { 1,2,3,8+17 })
            {
                var bad=(byte[])post.Clone();
                bad[offset]^=128;
                string malformed=Raw("crc-valid-bad-"+offset+".ppdemo",8,bad);
                using var reader=DemoReader.Open(malformed,out _);
                Check(reader!=null&&reader.ReadNext()==null&&reader.LastResult==ReplayOpenResult.Corrupt,"CRC-correct entire chunk rejects malformed second phase before prefix "+offset);
            }
            string oldContainer=Raw("v7-undeclared249.ppdemo",7,post);
            using(var reader=DemoReader.Open(oldContainer,out _))Check(reader!=null&&reader.ReadNext()==null&&reader.LastResult==ReplayOpenResult.Corrupt,"format7 raw reader refuses undeclared phase before chunk prefix");
            string part=Raw("valid-footerless.ppdemo.part",8,post);
            Check(ReplayArchive.Recover(part,out var recoveredPart,out _),"footerless .part recovers valid bounded phase chunk");
            using(var reader=DemoReader.Open(recoveredPart!,out var opened))
            {
                Check(reader!=null&&opened==ReplayOpenResult.Success&&reader.Metadata!.FormatVersion==8,"recovered .part retains format8");
                int count=0;
                while(reader!.ReadNext()!=null)count++;
                Check(reader.LastResult==ReplayOpenResult.Success&&count==2,"recovered .part preserves both phase records");
            }
            using(var session=new ReplayPlaybackSession(new PassiveReplaySessionHost()))Check(!session.Join(path)&&session.LastResult==ReplayOpenResult.UnsupportedFormat,"ordinary playback admission remains closed for format8");
            var store=new MphRead.Mods.CustomWeapons.Packages.WeaponPackageStore(Path.Combine(output,"not-created"));
            Reject(()=>
            {
                using var prepared=PreparedCustomReplayArchive.Prepare(path,store);
            },"prepared custom player admission remains closed for8");
            Check(!Directory.Exists(Path.Combine(output,"not-created")),"unsupported preparation never touches package store");
            File.WriteAllText(Path.Combine(output,"post-world-results.json"),JsonSerializer.Serialize(new
            {
                checks,scope="Constructed asset-free format8 post-world storage, inclusive seek, malformed CRC-correct reader, copy/recovery. Content-free world envelope is not a loaded Scene or live authority recording."
            },new JsonSerializerOptions
            {
                WriteIndented=true
            }));
            Console.WriteLine($"PASS {checks} phase-aware replay storage checks");
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("CUSTOM POST-WORLD STORAGE FAIL "+error);
            return 1;
        }
    }
}
