using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class NetworkContractChecks
{
    // Independently derived with Python struct (little-endian scalars), uuid.bytes
    // (network UUID order), and hashlib.sha256. No production writer supplied these.
    // Canonical = b'CWM1' + pack('<HHH',1,1,1) + pack('<H',10)
    //           + UUID('9a776851-f39f-4a76-8c13-c5d88875ef61').bytes + mechanics SHA.
    private const string CanonicalHex = "43574d310100010001000a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228";
    private const string ManifestSha = "a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9";
    private const string Manifest64Sha = "35434230efd8e80aeadec9dfc692e8a095b48f2f391015210b5d964de327d539";
    // Header bootstrap = pack('<HQIIHIII',0x1234,0x0102030405060708,
    // 0x11223344,0x55667788,0x99aa,0x10203040,0x50607080,0x90a0b0c0).
    private const string PageHex = "01341208070605040302014433221188776655aa994030201080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e90100010a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228";
    private const string ReadyHex = "01341208070605040302014433221188776655aa994030201080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903010001000100";
    // Action suffix = pack('<BHHIIIBBBffffff',3,0x3344,10,0x01020304,
    // 0x11223344,0x55667788,127,1,2,1.25,-2.5,3.75,0,0,-1).
    private const string ActionHex = "01341208070605040302014433221188776655aa994030201080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e90344330a000403020144332211887766557f01020000a03f000020c0000070400000000000000000000080bf";

    internal static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { check(true, label); return; }
            throw new Exception("Expected argument rejection: " + label);
        }
        var rules = MechanicsChecks.Rules();
        var definition = WeaponDefinition.Create(Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61"), "M7", rules);
        var bootstrap = new WorldBootstrapIdentity(new(0x1234, 0x0102030405060708UL, 0x11223344),
            0x55667788, 0x99aa, 0x10203040, 0x50607080, 0x90a0b0c0);
        var manifest = new CustomWeaponManifest(new[] { definition });
        byte[] canonical = Convert.FromHexString(CanonicalHex);
        check(Convert.ToHexStringLower(SHA256.HashData(canonical)) == ManifestSha
            && manifest.Digest.ToString() == ManifestSha, "independent canonical manifest SHA-256 vector");
        byte[] rowBytes = new byte[CustomManifestRow.Size]; manifest.Rows[0].Write(rowBytes);
        check(rowBytes.AsSpan().SequenceEqual(canonical.AsSpan(10)), "manifest row golden UUID network byte order and little-endian ID");
        check(CustomManifestRow.TryRead(rowBytes, out var row) && row == manifest.Rows[0]
            && row.Identity == definition.Identity, "manifest row round trip retains exact custom identity");
        check(!default(WeaponDigest).IsValid && WeaponDigest.FromHex(ManifestSha).ToString() == ManifestSha,
            "digest storage round trip and zero sentinel");
        Reject(() => WeaponDigest.FromHex(new string('0', 64)), "zero manifest digest sentinel");
        Reject(() => WeaponDigest.FromHex(ManifestSha.ToUpperInvariant()), "digest hex must be canonical lowercase");
        Reject(() => new CustomWeaponManifest(Array.Empty<WeaponDefinition>()), "empty manifest refused");
        Reject(() => new CustomWeaponManifest(new[] { definition, definition }), "duplicate manifest content refused");
        var revised = WeaponDefinition.Create(definition.Identity.ContentId, "revision",
            MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65", "\"body\":66")));
        Reject(() => new CustomWeaponManifest(new[] { definition, revised }), "two revisions of one lineage cannot form one manifest");
        Reject(() => new CustomWeaponManifest(new WeaponDefinition[] { null! }), "null manifest entry refused");
        try { ((IList<CustomManifestRow>)manifest.Rows)[0] = default; throw new Exception("Mutable manifest rows"); }
        catch (NotSupportedException) { check(true, "manifest rows cannot mutate frozen digest"); }

        var page = new CustomManifestPage(bootstrap, manifest.Digest, 1, 0, manifest.Rows.ToArray());
        byte[] pageBytes = new byte[page.Size]; page.Write(pageBytes);
        check(pageBytes.AsSpan().SequenceEqual(Convert.FromHexString(PageHex)), "independent manifest page byte vector");
        check(CustomManifestPage.TryRead(pageBytes, out var decodedPage) && decodedPage!.Bootstrap == bootstrap
            && decodedPage.Manifest == manifest.Digest && decodedPage.Rows[0] == row, "page reader preserves exact admission");
        var mutableRows = manifest.Rows.ToArray();
        var detachedPage = new CustomManifestPage(bootstrap, manifest.Digest, 1, 0, mutableRows);
        mutableRows[0] = default;
        check(detachedPage.Rows[0] == row, "page owns a detached row copy");
        var single = new CustomManifestAssembler(bootstrap, manifest.Digest);
        using var exactRegistry = new WeaponRegistry(true, new[] { definition });
        check(!single.IsComplete && !single.CanResolve(exactRegistry), "no readiness from an empty assembly");
        check(single.Receive(decodedPage!) == CustomManifestReceipt.Complete && single.CanResolve(exactRegistry), "one page resolves against exact frozen registry");
        check(single.Receive(page) == CustomManifestReceipt.Duplicate && single.IsComplete, "completed identical page is idempotent");
        using (var missing = new WeaponRegistry(true, Array.Empty<WeaponDefinition>()))
            check(!single.CanResolve(missing), "missing definition cannot satisfy manifest");
        using (var wrong = new WeaponRegistry(true, new[] { revised }))
            check(!single.CanResolve(wrong), "same content UUID with wrong mechanics cannot upgrade manifest");
        var extra = WeaponDefinition.Create(Guid.Parse("ffffffff-0000-0000-0000-000000000001"), "extra", rules);
        using (var extraRegistry = new WeaponRegistry(true, new[] { definition, extra }))
            check(!single.CanResolve(extraRegistry), "extra registry rows cannot silently change frozen mapping");
        var disposed = new WeaponRegistry(true, new[] { definition }); disposed.Dispose();
        check(!single.CanResolve(disposed), "disposed registry cannot establish readiness");

        var definitions = Enumerable.Range(1, 64).Select(i => WeaponDefinition.Create(
            Guid.Parse($"{i:x8}-0000-0000-0000-000000000001"), "definition " + i, rules)).ToArray();
        var full = new CustomWeaponManifest(definitions.Reverse());
        check(full.Rows.Count == 64 && full.Digest.ToString() == Manifest64Sha
            && full.Digest == new CustomWeaponManifest(definitions).Digest, "independent maximum manifest digest ignores enumeration order");
        Reject(() => new CustomWeaponManifest(definitions.Append(extra)), "65 distinct manifest entries refused");
        // Crossing the signed high bit in each numeric Guid component must
        // preserve agreement between CompareTo, canonical text, pages and registry.
        string[] guidOrder =
        {
            "00000000-0000-0000-0000-000000000001", "00000000-0000-7fff-0000-000000000001",
            "00000000-0000-8000-0000-000000000001", "00000000-7fff-0000-0000-000000000001",
            "00000000-8000-0000-0000-000000000001", "7fffffff-0000-0000-0000-000000000001",
            "80000000-0000-0000-0000-000000000001", "ffffffff-0000-0000-0000-000000000001"
        };
        var highBitDefinitions = guidOrder.Select(g => WeaponDefinition.Create(Guid.Parse(g), "high bit", rules)).ToArray();
        var highBits = new CustomWeaponManifest(highBitDefinitions.Reverse());
        using (var highBitRegistry = new WeaponRegistry(true, highBitDefinitions.Reverse()))
        {
            var highBitPage = new CustomManifestPage(bootstrap, highBits.Digest, 8, 0, highBits.Rows.ToArray());
            byte[] bytes = new byte[highBitPage.Size]; highBitPage.Write(bytes);
            var highBitAssembly = new CustomManifestAssembler(bootstrap, highBits.Digest);
            check(highBits.Rows.Select(r => r.ContentId.ToString("D")).SequenceEqual(guidOrder)
                && highBitDefinitions.Select(d => d.Identity.ContentId).Order().SequenceEqual(highBits.Rows.Select(r => r.ContentId))
                && CustomManifestPage.TryRead(bytes, out var decodedHighBits)
                && highBitAssembly.Receive(decodedHighBits!) == CustomManifestReceipt.Complete
                && highBitAssembly.CanResolve(highBitRegistry), "unsigned high-bit GUID ordering agrees across text, pages and registry");
        }
        var pages = Enumerable.Range(0, 4).Select(i => new CustomManifestPage(bootstrap, full.Digest, 64,
            (byte)i, full.Rows.Skip(i * 16).Take(16).ToArray())).ToArray();
        using var fullRegistry = new WeaponRegistry(false, definitions.Reverse());
        var assembler = new CustomManifestAssembler(bootstrap, full.Digest);
        foreach (int index in new[] { 3, 1, 0 })
        {
            byte[] bytes = new byte[pages[index].Size]; pages[index].Write(bytes);
            check(bytes.Length == 868 && CustomManifestPage.TryRead(bytes, out var copy)
                && assembler.Receive(copy!) == CustomManifestReceipt.Accepted && !assembler.IsComplete
                && !assembler.CanResolve(fullRegistry), "out-of-order partial pages cannot establish readiness " + index);
        }
        check(assembler.Receive(pages[1]) == CustomManifestReceipt.Duplicate && !assembler.IsComplete, "duplicate cannot fill missing page");
        check(assembler.Receive(pages[2]) == CustomManifestReceipt.Complete && assembler.CanResolve(fullRegistry), "all four reordered pages establish exact mapping");
        var partial = new CustomManifestAssembler(bootstrap, full.Digest);
        partial.Receive(pages[0]);
        var changedRows = pages[0].Rows.ToArray();
        changedRows[0] = changedRows[0] with { Mechanics = WeaponDigest.FromHex(new string('a', 64)) };
        var conflicting = new CustomManifestPage(bootstrap, full.Digest, 64, 0, changedRows);
        check(partial.Receive(conflicting) == CustomManifestReceipt.Refused && !partial.IsComplete,
            "conflicting duplicate cannot replace accepted page bytes");
        foreach (int index in new[] { 1, 2, 3 }) partial.Receive(pages[index]);
        check(partial.CanResolve(fullRegistry), "refused duplicate preserves prior immutable page");
        var corrupt = new CustomManifestAssembler(bootstrap, full.Digest);
        corrupt.Receive(conflicting);
        foreach (int index in new[] { 1, 2 }) corrupt.Receive(pages[index]);
        check(corrupt.Receive(pages[3]) == CustomManifestReceipt.Invalidated && !corrupt.IsComplete
            && !corrupt.CanResolve(fullRegistry) && corrupt.Receive(pages[0]) == CustomManifestReceipt.Invalidated,
            "complete digest corruption permanently closes assembly");
        // Both individual pages remain locally sorted; duplicate is across page boundary.
        var crossRows = full.Rows.ToArray();
        crossRows[16] = crossRows[16] with { ContentId = crossRows[15].ContentId };
        var crossDigest = CustomWeaponManifest.ComputeDigest(crossRows);
        var cross = new CustomManifestAssembler(bootstrap, crossDigest);
        for (int i = 0; i < 3; i++)
            cross.Receive(new CustomManifestPage(bootstrap, crossDigest, 64, (byte)i, crossRows.AsSpan(i * 16, 16)));
        check(cross.Receive(new CustomManifestPage(bootstrap, crossDigest, 64, 3, crossRows.AsSpan(48, 16)))
            == CustomManifestReceipt.Invalidated, "matching digest does not excuse cross-page duplicate content");
        foreach (int total in new[] { 1, 16, 17, 31, 32, 33, 63, 64 })
        {
            var bounded = new CustomWeaponManifest(definitions.Take(total));
            var assembly = new CustomManifestAssembler(bootstrap, bounded.Digest);
            int pageCount = (total + 15) / 16;
            for (int index = pageCount - 1; index >= 0; index--)
            {
                var boundary = new CustomManifestPage(bootstrap, bounded.Digest, (byte)total, (byte)index,
                    bounded.Rows.Skip(index * 16).Take(16).ToArray());
                byte[] bytes = new byte[boundary.Size]; boundary.Write(bytes);
                if (!CustomManifestPage.TryRead(bytes, out var copy)) throw new Exception("Valid page boundary refused");
                var receipt = assembly.Receive(copy!);
                if (receipt != (index == 0 ? CustomManifestReceipt.Complete : CustomManifestReceipt.Accepted))
                    throw new Exception("Wrong page boundary receipt");
            }
            check(assembly.IsComplete, "short final-page boundary " + total);
        }

        var bootstrapChanges = new[]
        {
            bootstrap with { Start = bootstrap.Start with { MatchId = 0x1235 } },
            bootstrap with { Start = bootstrap.Start with { AuthorityEpoch = 0x0102030405060709UL } },
            bootstrap with { Start = bootstrap.Start with { StartGeneration = 0x11223345 } },
            bootstrap with { Revision = 0x55667789 }, bootstrap with { SlotGeneration = 0x99ab },
            bootstrap with { AuthorityFrame = 0x10203041 }, bootstrap with { SlowRevision = 0x50607081 },
            bootstrap with { WorldRevision = 0x90a0b0c1 }
        };
        foreach (var changed in bootstrapChanges)
        {
            var fence = new CustomManifestAssembler(bootstrap, manifest.Digest);
            check(fence.Receive(new(changed, manifest.Digest, 1, 0, manifest.Rows.ToArray())) == CustomManifestReceipt.Refused
                && !fence.IsComplete, "every bootstrap lifecycle component fences manifest admission");
        }
        var wrongDigest = WeaponDigest.FromHex(new string('b', 64));
        check(new CustomManifestAssembler(bootstrap, manifest.Digest).Receive(
            new(bootstrap, wrongDigest, 1, 0, manifest.Rows.ToArray())) == CustomManifestReceipt.Refused,
            "foreign manifest digest cannot establish readiness");
        var wrongTotal = new CustomManifestAssembler(bootstrap, full.Digest); wrongTotal.Receive(pages[0]);
        check(wrongTotal.Receive(new(bootstrap, full.Digest, 63, 0, pages[0].Rows.ToArray())) == CustomManifestReceipt.Refused,
            "page total cannot change during assembly");

        var action = new CustomWeaponAction(bootstrap, manifest.Digest, 3, 0x3344, new(10),
            0x01020304, 0x11223344, 0x55667788, 127, CustomWeaponActionKind.Fire, 2,
            new(1.25f, -2.5f, 3.75f), new Vector3(0, 0, -1));
        var ready = new CustomWeaponReady(bootstrap, manifest.Digest, 3, 1, 1, CustomWeaponReady.PrecisionCapability);
        byte[] actionBytes = new byte[CustomWeaponAction.Size], readyBytes = new byte[CustomWeaponReady.Size];
        foreach(byte optic in new byte[]{0,1,2,3,4})
        {
            var selected=action with{Optic=optic};selected.Write(actionBytes);
            check(CustomWeaponAction.TryRead(actionBytes,out var readOptic)&&readOptic==selected,"action codec preserves reviewed0..4 optic boundary "+optic);
        }
        check(!(action with{Optic=5}).IsValid,"action codec rejects optic5 beyond reviewed package capability");
        action.Write(actionBytes); ready.Write(readyBytes);
        check(actionBytes.AsSpan().SequenceEqual(Convert.FromHexString(ActionHex)), "independent custom action byte vector");
        check(readyBytes.AsSpan().SequenceEqual(Convert.FromHexString(ReadyHex)), "independent custom readiness byte vector");
        check(CustomWeaponAction.TryRead(actionBytes, out var decoded) && decoded == action
            && decoded.Owner == new WeaponOwnerContext(0x1234, 0x0102030405060708UL, 3, 0x99aa, 0x3344),
            "action preserves owner rather than native weapon identity");
        // Negating UnitZ also negates its zero components. The wire preserves
        // IEEE-754 signs; geometric equality still treats +0 and -0 alike.
        float negativeZero = BitConverter.Int32BitsToSingle(int.MinValue);
        var signedZeroAction = action with { Direction = new(negativeZero, negativeZero, -1) };
        byte[] signedZeroBytes = new byte[CustomWeaponAction.Size]; signedZeroAction.Write(signedZeroBytes);
        check(CustomWeaponAction.TryRead(signedZeroBytes, out var signedZeroCopy) && signedZeroCopy == action
            && BitConverter.SingleToInt32Bits(signedZeroCopy.Direction.X) == int.MinValue
            && BitConverter.SingleToInt32Bits(signedZeroCopy.Direction.Y) == int.MinValue
            && !signedZeroBytes.AsSpan().SequenceEqual(actionBytes), "signed-zero action round trip preserves bits and geometric equality");
        check(CustomWeaponReady.TryRead(readyBytes, out var decodedReady) && decodedReady == ready, "readiness exact round trip");
        foreach (var changed in bootstrapChanges)
            check((action with { Bootstrap = changed }) != action && (ready with { Bootstrap = changed }) != ready,
                "action and readiness envelopes retain every exact bootstrap lifecycle component");
        foreach (var changed in bootstrapChanges.Take(2).Append(bootstrapChanges[4]))
            check(CustomActionIdentity.For(action with { Bootstrap = changed }) != CustomActionIdentity.For(action),
                "custom proof owner includes match, authority and occupant generation");
        var actionIdentity = CustomActionIdentity.For(action);
        check(actionIdentity.IsValid && !default(CustomActionIdentity).IsValid
            && actionIdentity != CustomActionIdentity.For(action with { Manifest = wrongDigest })
            && actionIdentity != CustomActionIdentity.For(action with { Weapon = new(11) })
            && actionIdentity != CustomActionIdentity.For(action with { Sequence = action.Sequence + 1 }),
            "custom proof identity includes exact manifest, runtime weapon and sequence");
        // Type-level evidence: both keys can coexist in a boxed set; there is no
        // conversion operator silently dropping custom manifest/runtime identity.
        var nativeKey = new ShotKey(action.Owner.AuthorityEpoch, action.Owner.MatchId, action.Slot,
            action.Owner.Generation, action.Life, action.Sequence);
        check(new HashSet<object> { actionIdentity, nativeKey }.Count == 2
            && !typeof(CustomActionIdentity).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.Name is "op_Implicit" or "op_Explicit"), "custom and native payment-key domains do not convert or alias");
        Reject(() => CustomActionIdentity.For(default), "invalid action cannot make a proof identity");
        foreach (var kind in Enum.GetValues<CustomWeaponActionKind>())
        {
            var valid = kind == CustomWeaponActionKind.Fire ? action : action with
            { Kind = kind, Origin = Vector3.Zero, Direction = Vector3.Zero, AckFrame = 0, AckSubFrame = 0,
                Optic = kind == CustomWeaponActionKind.Optic ? (byte)2 : (byte)0 };
            valid.Write(actionBytes);
            check(CustomWeaponAction.TryRead(actionBytes, out var value) && value == valid, "allowlisted action kind " + kind);
            if (kind != CustomWeaponActionKind.Fire)
                check(!(valid with { Origin = Vector3.UnitX }).IsValid && !(valid with { Direction = Vector3.UnitZ }).IsValid
                    && !(valid with { AckFrame = 1 }).IsValid && !(valid with { AckSubFrame = 1 }).IsValid,
                    "non-fire action cannot smuggle pose or ACK " + kind);
        }
        action.Write(actionBytes);
        check((action with { Origin = new(1_000_000, -1_000_000, 0) }).IsValid
            && !(action with { Origin = new(1_000_001, 0, 0) }).IsValid
            && !(action with { Direction = new(0, 0, 1.1f) }).IsValid,
            "fire spatial boundaries are strict without silently normalizing");
        Reject(() => action.Write(new byte[CustomWeaponAction.Size - 1]), "short action output buffer");
        Reject(() => ready.Write(new byte[CustomWeaponReady.Size - 1]), "short ready output buffer");
        Reject(() => default(CustomWeaponAction).Write(actionBytes), "invalid action cannot serialize");
        Reject(() => default(CustomWeaponReady).Write(readyBytes), "invalid readiness cannot serialize");
        for (int length = 0; length < CustomWeaponAction.Size; length++)
            if (CustomWeaponAction.TryRead(actionBytes.AsSpan(0, length), out var invalid) || invalid != default)
                throw new Exception("Truncated action accepted or leaked output");
        for (int length = 0; length < CustomWeaponReady.Size; length++)
            if (CustomWeaponReady.TryRead(readyBytes.AsSpan(0, length), out var invalid) || invalid != default)
                throw new Exception("Truncated readiness accepted or leaked output");
        check(!CustomWeaponAction.TryRead(actionBytes.Concat(new byte[1]).ToArray(), out _)
            && !CustomWeaponReady.TryRead(readyBytes.Concat(new byte[1]).ToArray(), out _), "readers require exact action/ready lengths");

        // Malformed fixtures deliberately keep most of a valid packet intact,
        // exercising inner validation rather than only rejecting random headers.
        var badActions = new List<byte[]>(); var badReadies = new List<byte[]>(); var badPages = new List<byte[]>();
        void Mutation(List<byte[]> list, byte[] original, Action<byte[]> mutate)
        { var bytes = (byte[])original.Clone(); mutate(bytes); list.Add(bytes); }
        foreach (int offset in new[] { 1, 3, 11, 15, 19, 21, 25, 29 })
        {
            int width = offset == 1 || offset == 19 ? 2 : offset == 3 ? 8 : 4;
            Mutation(badActions, actionBytes, b => Array.Clear(b, offset, width));
            Mutation(badReadies, readyBytes, b => Array.Clear(b, offset, width));
        }
        Mutation(badActions, actionBytes, b => b[0] = 2);
        Mutation(badActions, actionBytes, b => Array.Clear(b, 33, 32));
        Mutation(badActions, actionBytes, b => b[65] = 8);
        Mutation(badActions, actionBytes, b => Array.Clear(b, 66, 2));
        foreach (ushort id in new ushort[] { 0, 1, 9, 74, ushort.MaxValue })
            Mutation(badActions, actionBytes, b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(68), id));
        Mutation(badActions, actionBytes, b => Array.Clear(b, 70, 4));
        foreach (byte kind in new byte[] { 0, 6, 255 }) Mutation(badActions, actionBytes, b => b[83] = kind);
        Mutation(badActions, actionBytes, b => b[84] = 5);
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1_000_001f })
            Mutation(badActions, actionBytes, b => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(85), value));
        Mutation(badActions, actionBytes, b => Array.Clear(b, 97, 12));
        Mutation(badActions, actionBytes, b => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(97), float.NaN));
        Mutation(badReadies, readyBytes, b => b[0] = 2);
        Mutation(badReadies, readyBytes, b => Array.Clear(b, 33, 32));
        Mutation(badReadies, readyBytes, b => b[65] = 8);
        foreach (int offset in new[] { 66, 68, 70 })
            foreach (ushort value in new ushort[] { 0, 2, ushort.MaxValue })
                Mutation(badReadies, readyBytes, b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), value));
        Mutation(badPages, pageBytes, b => b[0] = 2);
        Mutation(badPages, pageBytes, b => b[65] = 0);
        Mutation(badPages, pageBytes, b => b[65] = 65);
        Mutation(badPages, pageBytes, b => b[66] = 1);
        Mutation(badPages, pageBytes, b => b[67] = 0);
        Mutation(badPages, pageBytes, b => b[67] = 255);
        Mutation(badPages, pageBytes, b => Array.Clear(b, 70, 16));
        Mutation(badPages, pageBytes, b => Array.Clear(b, 86, 32));
        Mutation(badPages, pageBytes, b => b[68] = 9);
        Mutation(badPages, pageBytes, b => b[68] = 11);
        badPages.Add(pageBytes[..^1]); badPages.Add(pageBytes.Concat(new byte[1]).ToArray());
        foreach (var bytes in badActions)
            if (CustomWeaponAction.TryRead(bytes, out var value) || value != default) throw new Exception("Malformed action accepted");
        foreach (var bytes in badReadies)
            if (CustomWeaponReady.TryRead(bytes, out var value) || value != default) throw new Exception("Malformed readiness accepted");
        foreach (var bytes in badPages)
            if (CustomManifestPage.TryRead(bytes, out var value) || value != null) throw new Exception("Malformed page accepted");
        foreach (var bytes in badReadies)
            if (!CustomWeaponProtocol.ReadHeader(bytes, out var header, out var digest) && (header != default || digest != default))
                throw new Exception("Failed header reader leaked partial identity");
        check(true, "malformed scalar, lifecycle, digest, enum, geometry and page fields fail with empty output");

        var random = new Random(404107);
        for (int i = 0; i < 10_000; i++)
        {
            if (i % 4 == 0)
            {
                var bytes = new byte[random.Next(0, CustomManifestPage.MaxSize + 50)]; random.NextBytes(bytes);
                // Valid random envelopes are allowed; never require probabilistic
                // rejection. Accepted outputs must satisfy their complete contract.
                if (CustomWeaponAction.TryRead(bytes, out var value) && !value.IsValid) throw new Exception("Invalid fuzz action escaped");
                if (CustomWeaponReady.TryRead(bytes, out var readiness) && !readiness.IsValid) throw new Exception("Invalid fuzz readiness escaped");
                if (CustomManifestPage.TryRead(bytes, out var received) && (received == null || received.Size != bytes.Length))
                    throw new Exception("Invalid fuzz page escaped");
            }
            else if (i % 4 == 1)
            {
                byte[] bytes = (byte[])badActions[random.Next(badActions.Count)].Clone();
                // Randomize a non-structural field while retaining the malformed gate.
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(74), (uint)random.Next());
                if (CustomWeaponAction.TryRead(bytes, out _)) throw new Exception("Mutated malformed action accepted");
            }
            else if (i % 4 == 2)
            {
                byte[] bytes = badReadies[random.Next(badReadies.Count)];
                if (CustomWeaponReady.TryRead(bytes, out _)) throw new Exception("Malformed readiness accepted during fuzz");
            }
            else if (CustomManifestPage.TryRead(badPages[random.Next(badPages.Count)], out _))
                throw new Exception("Malformed page accepted during fuzz");
        }
        check(true, "10000 seeded malformed/fuzz payloads terminate without parser exceptions");
        byte[] invalidAction = badActions[0], invalidReady = badReadies[0];
        for (int i = 0; i < 20_000; i++)
        {
            CustomWeaponAction.TryRead(actionBytes, out _); CustomWeaponReady.TryRead(readyBytes, out _);
            CustomWeaponAction.TryRead(invalidAction, out _); CustomWeaponReady.TryRead(invalidReady, out _);
        }
        long before = GC.GetAllocatedBytesForCurrentThread(); long accepted = 0;
        for (int i = 0; i < 100_000; i++)
        {
            if (CustomWeaponAction.TryRead(actionBytes, out var value)) accepted += value.Sequence;
            if (CustomWeaponReady.TryRead(readyBytes, out var readiness)) accepted += readiness.Slot;
            if (CustomWeaponAction.TryRead(invalidAction, out _)) throw new Exception("Invalid warmed action accepted");
            if (CustomWeaponReady.TryRead(invalidReady, out _)) throw new Exception("Invalid warmed readiness accepted");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocated == 0 && accepted == 100_000L * (0x01020304L + 3),
            "100000 warmed valid/invalid action and ready reader batches allocate zero bytes");
    }
}
