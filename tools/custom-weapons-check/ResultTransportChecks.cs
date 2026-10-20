using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Pure authority-result codecs. Consistent source/health fixtures below do not
// prove endpoint authentication, admission, collision or multiplayer HP mutation.
internal static class ResultTransportChecks
{
    // Independent Python struct.pack vectors. Canonical M7 SHA uses literal
    // mechanics JSON, uuid.bytes and hashlib; no production writer derived them.
    private const string ReceiptHex = "01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d000000e8030000be0000008001010000803f00002040000040400000000000000000000080bf8877665544332211c900000000000207c8000000be000000800000803f00000040000040400000000000000000000080bf01e8030000020410000100000000000000000000000000000000e8030000030310000133040000000000000d000000e8030000";
    private const string ContactHex = "01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d000000cd000000043412785601010000a0bf000020400000704001960000001e001e00bc9a1122334455667788";
    private const string StateHex = "01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a00c9000000403020100d0000000a00e8030000e9030000030310000133040000000000000d000000e8030000";
    private const string AckHex = "01341208070605040302014433221188776655aa996400000080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903ccbb0a000d000000018877665544332211";
    private sealed class Fixture
    {
        internal readonly WeaponDefinition Definition = RiftlineM7.Create();
        internal readonly CustomWeaponManifest Manifest;
        internal readonly WorldBootstrapIdentity Bootstrap = new(new(0x1234,0x0102030405060708UL,0x11223344),
            0x55667788,0x99aa,100,0x50607080,0x90a0b0c0);
        internal readonly WeaponOwnerContext Owner;
        internal readonly CustomReceiptMessage Receipt;
        internal readonly CustomContactMessage Contact;
        internal readonly CustomStateMessage State;
        internal Fixture(uint tick = 1000)
        {
            Manifest = new(new[] { Definition }); Owner = new(0x1234,0x0102030405060708UL,3,0x99aa,0xbbcc);
            var runtime = new WeaponRuntimeState(Definition,Owner,tick);
            if (runtime.SetEquipped(tick,true) != WeaponActionResult.Accepted
                || runtime.TrySetOptic(tick,11,1) != WeaponActionResult.Accepted) throw new InvalidOperationException("Real resource fixture could not equip/scope.");
            var before = runtime.Snapshot();
            if (runtime.TryFire(tick,13,true) != WeaponActionResult.Accepted) throw new InvalidOperationException("Real resource fixture could not fire.");
            var action = new CustomWeaponAction(Bootstrap,Manifest.Digest,3,0xbbcc,new(10),13,tick,190,128,
                CustomWeaponActionKind.Fire,1,new(1,2.5f,3),new(0,0,-1));
            var source = new CustomSourceCertificate(action,200,190,128,new(1,2,3),new(0,0,-1),true,false,false);
            Receipt = new(0x1122334455667788UL,new(action,201,CustomJournalDisposition.Accepted,
                WeaponActionResult.Accepted,CustomEmissionDisposition.Spawned,source,before,runtime.Snapshot()));
            Contact = new(Bootstrap,0x8877665544332211UL,new(new(new(CustomActionIdentity.For(action),new(4,0x1234,0x5678),1,
                CustomImpactChannel.Body),new(-1.25f,2.5f,3.75f),true),205,new(150,0,30,30,0x9abc)));
            runtime.Preview(unchecked(tick+1),out var projected);
            State = new(Bootstrap,Manifest.Digest,new(10),201,0x10203040,13,new(10),tick,projected);
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        var f = new Fixture(); Golden(f,check); LengthsAndHeaders(f,check); MasksAndEnums(f,check);
        Identity(f,check); RetainedStance(f,check); ReceiptStates(f,check); Health(f,check); WrappedState(check); Allocation(f,check);
        OutboxReservations(f,check); OutboxDelivery(f,check); OutboxCatchup(f,check);
        OutboxFlights(f,check); OutboxClockAndTokens(f,check); OutboxAllocation(f,check);
        ClientCache(f,check); ClientAllocation(f,check);
    }
    private static byte[] Encode(CustomReceiptMessage value,Fixture f)
    { var bytes = new byte[CustomReceiptMessage.Size]; CustomCombatResults.Write(value,bytes,f.Manifest,f.Definition); return bytes; }
    private static byte[] Encode(CustomContactMessage value,Fixture f)
    { var bytes = new byte[CustomContactMessage.Size]; CustomCombatResults.Write(value,bytes,f.Manifest,f.Definition); return bytes; }
    private static byte[] Encode(CustomStateMessage value,Fixture f)
    { var bytes = new byte[CustomStateMessage.Size]; CustomCombatResults.Write(value,bytes,f.Manifest,f.Definition); return bytes; }
    private static byte[] Encode(CustomResultAck value)
    { var bytes = new byte[CustomResultAck.Size]; value.Write(bytes); return bytes; }
    private static bool Read(int kind,ReadOnlySpan<byte> bytes,Fixture f,out bool absent,
        CustomWeaponManifest? manifest = null,WeaponDefinition? definition = null)
    {
        bool read;
        switch (kind)
        {
            case 0: read = CustomCombatResults.TryReadReceipt(bytes,manifest ?? f.Manifest,definition ?? f.Definition,out var receipt); absent = receipt == default; return read;
            case 1: read = CustomCombatResults.TryReadContact(bytes,manifest ?? f.Manifest,definition ?? f.Definition,out var contact); absent = contact == default; return read;
            case 2: read = CustomCombatResults.TryReadState(bytes,manifest ?? f.Manifest,definition ?? f.Definition,out var state); absent = state == default; return read;
            default: read = CustomResultAck.TryRead(bytes,out var ack); absent = ack == default; return read;
        }
    }
    private static void Refuse(int kind,byte[] bytes,Fixture f,Action<bool,string> check,string label)
        => check(!Read(kind,bytes,f,out bool absent) && absent,label);
    private static byte[] Change(byte[] bytes,int at,byte value)
    { var copy = bytes.ToArray(); copy[at] = value; return copy; }
    private static byte[] Zero(byte[] bytes,int at,int count)
    { var copy = bytes.ToArray(); copy.AsSpan(at,count).Clear(); return copy; }
    private static void Reject(Action action,Action<bool,string> check,string label)
    {
        try { action(); } catch (ArgumentException) { check(true,label); return; }
        throw new InvalidOperationException("Expected result codec rejection: "+label);
    }
    private static void Golden(Fixture f,Action<bool,string> check)
    {
        check(CustomReceiptMessage.Size == 209 && CustomContactMessage.Size == 116 && CustomStateMessage.Size == 113
            && CustomResultAck.Size == 83 && CustomCombatResults.SnapshotSize == 25,"reviewed result wire widths are fixed and independently accounted");
        check(Encode(f.Receipt,f).AsSpan().SequenceEqual(Convert.FromHexString(ReceiptHex))
            && Encode(f.Contact,f).AsSpan().SequenceEqual(Convert.FromHexString(ContactHex))
            && Encode(f.State,f).AsSpan().SequenceEqual(Convert.FromHexString(StateHex))
            && Encode(f.Receipt.Ack).AsSpan().SequenceEqual(Convert.FromHexString(AckHex)),
            "independent Python golden vectors match full receipt, contact, state and application acknowledgement bytes");
        check(CustomCombatResults.TryReadReceipt(Convert.FromHexString(ReceiptHex),f.Manifest,f.Definition,out var receipt) && receipt == f.Receipt
            && CustomCombatResults.TryReadContact(Convert.FromHexString(ContactHex),f.Manifest,f.Definition,out var contact) && contact == f.Contact
            && CustomCombatResults.TryReadState(Convert.FromHexString(StateHex),f.Manifest,f.Definition,out var state) && state == f.State
            && CustomResultAck.TryRead(Convert.FromHexString(AckHex),out var ack) && ack == f.Receipt.Ack,
            "independent result bytes decode to exact typed identities, snapshots, health and tokens");
        check(f.Receipt.Ack.Kind == CustomResultKind.Receipt && f.Contact.Ack.Kind == CustomResultKind.Contact
            && f.Contact.Ack.Owner == f.Owner && f.Contact.Ack.Sequence == 13
            && f.Receipt.Ack.Token != f.Contact.Ack.Token && f.Contact.Outcome.Lethal,
            "receipt/contact acknowledgements remain separate full-key domains and lethality derives from actual body removal");
    }
    private static void RetainedStance(Fixture f,Action<bool,string> check)
    {
        var source = f.Receipt.Receipt.Source!.Value with { Crouched = true };
        var action = source.Action with { Origin = source.MuzzleOrigin };
        source = source with { Action = action };
        var message = f.Receipt with { Receipt = f.Receipt.Receipt with { Action = action, Source = source } };
        var bytes = Encode(message,f);
        check(bytes.Length == 209 && bytes[158] == 9
            && CustomCombatResults.TryReadReceipt(bytes,f.Manifest,f.Definition,out var copy) && copy == message
            && copy.Receipt.Source!.Value.Crouched && copy.Receipt.Source.Value.MuzzleOrigin == action.Origin,
            "receipt retains accepted source crouch and muzzle in spare bit3 without widening its body");
        check(!f.Receipt.Receipt.Source!.Value.Crouched
            && Encode(f.Receipt,f).AsSpan().SequenceEqual(Convert.FromHexString(ReceiptHex)),
            "default standing source remains byte-exact to the published independent receipt fixture");
    }
    private static void LengthsAndHeaders(Fixture f,Action<bool,string> check)
    {
        var vectors = new[] { Convert.FromHexString(ReceiptHex),Convert.FromHexString(ContactHex),Convert.FromHexString(StateHex),Convert.FromHexString(AckHex) };
        for (int kind=0;kind<vectors.Length;kind++)
        {
            var bytes = vectors[kind];
            for (int length=0;length<bytes.Length;length++)
                check(!Read(kind,bytes.AsSpan(0,length),f,out bool absent) && absent,"every truncated result length refuses with default output");
            Refuse(kind,bytes.Concat(new byte[] { 0 }).ToArray(),f,check,"result reader refuses any extra byte");
            Refuse(kind,Change(bytes,0,2),f,check,"result reader refuses unknown payload version");
            foreach (var field in new (int At,int Size)[] { (1,2),(3,8),(11,4),(15,4),(19,2),(21,4),(25,4),(29,4),(33,32),(66,2) })
                Refuse(kind,Zero(bytes,field.At,field.Size),f,check,"zero full-bootstrap/digest/life field is rejected atomically");
            Refuse(kind,Change(bytes,65,8),f,check,"result reader refuses invalid owner slot");
            var native = bytes.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(native.AsSpan(68),4);
            Refuse(kind,native,f,check,"native runtime row cannot enter custom result identity domain");
            if (kind != 2) Refuse(kind,Zero(bytes,70,4),f,check,"terminal result and acknowledgement require a nonzero action sequence");
            int tokenAt = kind == 0 ? 109 : kind == 1 ? 108 : kind == 3 ? 75 : 74;
            Refuse(kind,Zero(bytes,tokenAt,kind == 2 ? 4 : 8),f,check,"result token or state revision cannot be absent");
        }
        Reject(()=>CustomCombatResults.Write(f.Receipt,new byte[208],f.Manifest,f.Definition),check,"short receipt write rejects");
        Reject(()=>CustomCombatResults.Write(f.Contact,new byte[115],f.Manifest,f.Definition),check,"short contact write rejects");
        Reject(()=>CustomCombatResults.Write(f.State,new byte[112],f.Manifest,f.Definition),check,"short state write rejects");
        Reject(()=>f.Receipt.Ack.Write(new byte[82]),check,"short acknowledgement write rejects");
    }
    private static void MasksAndEnums(Fixture f,Action<bool,string> check)
    {
        var receipt = Convert.FromHexString(ReceiptHex); var contact = Convert.FromHexString(ContactHex);
        for (int value=1;value<=255;value++) if (value != 7)
            Refuse(0,Change(receipt,124,(byte)value),f,check,"partial or unknown optional receipt masks cannot describe a terminal result");
        for (int value=16;value<=255;value++) Refuse(0,Change(receipt,158,(byte)value),f,check,"unknown source modifier bits refuse");
        Refuse(0,Change(receipt,158,7),f,check,"simultaneous Double and Prime source modifiers cannot enter result wire");
        foreach (var field in new (int At,int Maximum)[] { (121,(byte)CustomJournalDisposition.EngineFault),(122,(byte)WeaponActionResult.InvalidOptic),(123,(byte)CustomEmissionDisposition.Uncertain) })
            for (int value=field.Maximum+1;value<=255;value++) Refuse(0,Change(receipt,field.At,(byte)value),f,check,"unknown terminal result enums refuse atomically");
        var denied = f.Receipt with { Receipt = new(f.Receipt.Receipt.Action,201,CustomJournalDisposition.SourceDenied,
            WeaponActionResult.InvalidLife,CustomEmissionDisposition.NotAttempted,null,null,null) };
        var empty = Encode(denied,f);
        check(empty[124] == 0 && empty.AsSpan(125,84).IndexOfAnyExcept((byte)0) < 0
            && CustomCombatResults.TryReadReceipt(empty,f.Manifest,f.Definition,out var copy) && copy == denied,
            "source denial has exact absent-zero payload and no fabricated resource snapshots");
        for (int at=125;at<209;at++) Refuse(0,Change(empty,at,1),f,check,"every absent optional byte must remain canonical zero");
        Refuse(0,Change(receipt,124,0),f,check,"presence zero cannot hide nonzero source or snapshot bytes");
        for (int value=4;value<=255;value++) Refuse(1,Change(contact,97,(byte)value),f,check,"unknown contact classification flags refuse");
        foreach (byte bad in new byte[] { 0,2,255 }) Refuse(1,Change(contact,83,bad),f,check,"precision contact component must be exactly one");
        foreach (byte bad in new byte[] { 0,3,255 }) Refuse(1,Change(contact,84,bad),f,check,"contact channel must be one of the two declared player channels");
        foreach (byte bad in new byte[] { 0,3,255 }) Refuse(3,Change(Convert.FromHexString(AckHex),74,bad),f,check,"application acknowledgement requires exact receipt/contact kind");
    }
    private static IEnumerable<CustomResultAck> ForeignKeys(CustomResultAck ack)
    {
        yield return ack with { Bootstrap = ack.Bootstrap with { Start = ack.Bootstrap.Start with { MatchId = 0x1235 } } };
        yield return ack with { Bootstrap = ack.Bootstrap with { Start = ack.Bootstrap.Start with { AuthorityEpoch = ack.Bootstrap.Start.AuthorityEpoch^0x8000000000000000UL } } };
        yield return ack with { Bootstrap = ack.Bootstrap with { Start = ack.Bootstrap.Start with { StartGeneration = 2 } } };
        yield return ack with { Bootstrap = ack.Bootstrap with { Revision = 2 } };
        yield return ack with { Bootstrap = ack.Bootstrap with { SlotGeneration = 2 } };
        yield return ack with { Bootstrap = ack.Bootstrap with { AuthorityFrame = 2 } };
        yield return ack with { Bootstrap = ack.Bootstrap with { SlowRevision = 2 } };
        yield return ack with { Bootstrap = ack.Bootstrap with { WorldRevision = 2 } };
        yield return ack with { Manifest = ack.Manifest with { A = ack.Manifest.A^1 } };
        yield return ack with { Slot = 4 }; yield return ack with { Life = 0xbbcd };
        yield return ack with { Weapon = new(11) }; yield return ack with { Sequence = 14 };
        yield return ack with { Kind = CustomResultKind.Contact }; yield return ack with { Token = ack.Token+1 };
    }
    private static void Identity(Fixture f,Action<bool,string> check)
    {
        var original = f.Receipt.Ack;
        foreach (var changed in ForeignKeys(original))
            check(CustomResultAck.TryRead(Encode(changed),out var copy) && copy == changed && copy != original,
                "ack parser preserves every full bootstrap/manifest/owner/weapon/sequence/kind/token substitution for exact application joining");
        var different = WeaponDefinition.Create(RiftlineM7.ContentId,"revised",MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65","\"body\":66")));
        var other = new CustomWeaponManifest(new[] { different });
        var bytes = new[] { Convert.FromHexString(ReceiptHex),Convert.FromHexString(ContactHex),Convert.FromHexString(StateHex) };
        for (int kind=0;kind<bytes.Length;kind++)
        {
            check(!Read(kind,bytes[kind],f,out bool absent,definition:different) && absent
                && !Read(kind,bytes[kind],f,out absent,manifest:other) && absent,
                "result readers require exact manifest and definition revision rather than content UUID alone");
            var changedDigest = Change(bytes[kind],33,(byte)(bytes[kind][33]^1)); Refuse(kind,changedDigest,f,check,"foreign nonzero manifest cannot select current frozen definition");
            var runtime = bytes[kind].ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(runtime.AsSpan(68),11);
            Refuse(kind,runtime,f,check,"unknown custom row cannot resolve through a different runtime identity");
        }
        var wrongOwner = new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,4,f.Owner.Generation,f.Owner.Life);
        check(!CustomCombatResults.Valid(f.Receipt with { Receipt = f.Receipt.Receipt with { Before = f.Receipt.Receipt.Before!.Value with { Owner = wrongOwner } } },f.Manifest,f.Definition),
            "receipt snapshot cannot belong to a different actor life");
        check(!CustomCombatResults.Valid(f.Contact with { Bootstrap = f.Bootstrap with { SlotGeneration = 2 } },f.Manifest,f.Definition)
            && !CustomCombatResults.Valid(f.State with { State = f.State.State with { Weapon = different.Identity } },f.Manifest,f.Definition),
            "contact bootstrap owner and state authored identity cannot be relabeled");
    }
    private static void ReceiptStates(Fixture f,Action<bool,string> check)
    {
        var receipt = f.Receipt.Receipt;
        foreach (var changed in new[] { receipt with { Before = null },receipt with { Source = null },receipt with { After = null },
            receipt with { Emission = CustomEmissionDisposition.NotAttempted },receipt with { Result = WeaponActionResult.Empty },
            receipt with { Source = receipt.Source!.Value with { DoubleDamage = true,PrimeHunter = true } },
            receipt with { Source = receipt.Source!.Value with { ObservedWorldFrame = 202 } },
            receipt with { Source = receipt.Source!.Value with { Aim = Vector3.Zero } },
            receipt with { Source = receipt.Source!.Value with { Body = new(float.NaN,0,0) } },
            receipt with { Before = receipt.Before!.Value with { Magazine = 5 } },
            receipt with { After = receipt.After!.Value with { Reserve = 65 } },
            receipt with { After = receipt.After!.Value with { Optic = 3 } } })
            check(!CustomCombatResults.Valid(new CustomReceiptMessage(f.Receipt.Token,changed),f.Manifest,f.Definition),"inconsistent receipt source/presence/result or definition resource bounds refuse");
        foreach (int offset in new[] { 163,188 }) Refuse(0,Change(Convert.FromHexString(ReceiptHex),offset,255),f,check,"receipt snapshots reject unknown weapon phase");
        var nospawn = receipt with { Disposition = CustomJournalDisposition.NoSpawn,Result = WeaponActionResult.CapacityUnavailable,
            Emission = CustomEmissionDisposition.NoSpawn,After = receipt.Before };
        check(CustomCombatResults.TryReadReceipt(Encode(new CustomReceiptMessage(f.Receipt.Token,nospawn),f),f.Manifest,f.Definition,out var refunded)
            && refunded.Receipt == nospawn,"definite no-spawn preserves original ammo, cadence and accepted-shot identity in the result");
        var failed = receipt with { Disposition = CustomJournalDisposition.EngineFault,Result = WeaponActionResult.InvalidLife,
            Emission = CustomEmissionDisposition.Uncertain,After = receipt.After!.Value with { Phase = CustomWeaponPhase.Invalid,Optic = 0 } };
        check(CustomCombatResults.TryReadReceipt(Encode(new CustomReceiptMessage(f.Receipt.Token,failed),f),f.Manifest,f.Definition,out var uncertain)
            && uncertain.Receipt == failed,"uncertain emission result remains distinct from no-spawn and retains debited resources");
        foreach (var malformed in new[] { receipt with { After = receipt.After!.Value with { Magazine = receipt.Before!.Value.Magazine } },
            receipt with { After = receipt.After!.Value with { LastAcceptedShotId = 12 } },
            nospawn with { Emission = CustomEmissionDisposition.NotAttempted },nospawn with { Result = WeaponActionResult.Empty } })
            check(!CustomCombatResults.Valid(new CustomReceiptMessage(f.Receipt.Token,malformed),f.Manifest,f.Definition),
                "accepted fire and no-spawn result require their exact one-round or rollback transition");
        var futureAction = receipt.Action with { AckFrame = 201,AckSubFrame = 1 };
        var futureSource = receipt.Source!.Value with { Action = futureAction,TargetWorldFrame = 201,TargetWorldSubFrame = 1 };
        check(!CustomCombatResults.Valid(new CustomReceiptMessage(f.Receipt.Token,receipt with { Action = futureAction,Source = futureSource }),f.Manifest,f.Definition),
            "receipt cannot certify a fractional target beyond its observed/authority world");
        foreach (var changed in new[] { f.State with { Revision = 0 },f.State with { AuthorityFrame = 99 },
            f.State with { CommittedSource = 1002 },f.State with { SettledSequence = 12 },f.State with { Selected = new(11) },
            f.State with { State = f.State.State with { Magazine = 5 } },f.State with { State = f.State.State with { Reserve = 65 } },
            f.State with { State = f.State.State with { Optic = 3 } },f.State with { State = f.State.State with { LastAcceptedShotId = 0 } } })
            check(!CustomCombatResults.Valid(changed,f.Manifest,f.Definition),"state revision, source frontier, selected weapon and exact resource limits remain strict");
        var initial = new WeaponRuntimeState(f.Definition,f.Owner,0).Snapshot();
        var state = f.State with { Revision = 1,SettledSequence = 0,Selected = default,CommittedSource = 0,State = initial };
        check(CustomCombatResults.TryReadState(Encode(state,f),f.Manifest,f.Definition,out var initialCopy) && initialCopy == state,
            "initial unequipped state permits zero settled sequence/source tick without fabricating an accepted shot");
    }
    private static void Health(Fixture f,Action<bool,string> check)
    {
        foreach (var health in new[] { new CustomDamageResult(100,35,0,0,1),new(100,100,50,50,0),
            new(100,100,50,20,2),new(100,90,50,0,3),new(65535,0,65535,0,65535) })
        foreach (var channel in new[] { CustomImpactChannel.Body,CustomImpactChannel.Halfturret })
        {
            var contact = f.Contact.Outcome.Contact with { Key = f.Contact.Outcome.Contact.Key with { Channel = channel } };
            var message = f.Contact with { Outcome = f.Contact.Outcome with { Contact = contact,Health = health } };
            check(CustomCombatResults.TryReadContact(Encode(message,f),f.Manifest,f.Definition,out var copy) && copy == message
                && copy.Outcome.Lethal == (health.BodyRemoved > 0 && health.BodyAfter == 0),
                "contact codec preserves body/turret/split/lethal/protected-zero health observations across both channels");
        }
        var denied = f.Contact with { Outcome = f.Contact.Outcome with { Health = new(100,100,50,50,0),Disposition = CustomContactDisposition.TradeDenied } };
        check(CustomCombatResults.TryReadContact(Encode(denied,f),f.Manifest,f.Definition,out var trade) && trade == denied && !trade.Outcome.Lethal,
            "trade denied is a classified zero terminal, independent of ordinary protected zero");
        foreach (var health in new[] { new CustomDamageResult(100,101,50,50,0),new(100,100,50,51,0),new(-1,0,0,0,0),
            new(65536,0,0,0,1),new(100,90,50,50,0),new(100,100,50,50,1) })
            check(!CustomCombatResults.Valid(f.Contact with { Outcome = f.Contact.Outcome with { Health = health } },f.Manifest,f.Definition),
                "invalid health deltas/uint16 bounds/event association cannot become authoritative contact bytes");
        check(!CustomCombatResults.Valid(denied with { Outcome = denied.Outcome with { Health = new(100,90,50,50,1) } },f.Manifest,f.Definition),
            "trade-denied classification cannot conceal positive canonical health removal");
        foreach (var point in new[] { new Vector3(float.NaN,0,0),new(float.PositiveInfinity,0,0),new(1000001,0,0) })
            check(!CustomCombatResults.Valid(f.Contact with { Outcome = f.Contact.Outcome with { Contact = f.Contact.Outcome.Contact with { Point = point } } },f.Manifest,f.Definition),
                "contact points require finite bounded coordinates");
        var bytes = Convert.FromHexString(ContactHex);
        Refuse(1,Change(bytes,78,8),f,check,"contact victim slot is bounded");
        Refuse(1,Zero(bytes,79,2),f,check,"contact victim generation is mandatory");
        Refuse(1,Zero(bytes,81,2),f,check,"contact victim life is mandatory");
    }
    private static void WrappedState(Action<bool,string> check)
    {
        var f = new Fixture(uint.MaxValue-74);
        check(f.Receipt.Receipt.After!.Value.FireReadyTick == 0 && CustomCombatResults.Valid(f.Receipt,f.Manifest,f.Definition)
            && CustomCombatResults.TryReadReceipt(Encode(f.Receipt,f),f.Manifest,f.Definition,out var receipt) && receipt == f.Receipt,
            "wrapped fire deadline zero is valid data when a real accepted shot exists");
        var runtime = new WeaponRuntimeState(f.Definition,f.Owner,uint.MaxValue-200); runtime.SetEquipped(uint.MaxValue-200,true);
        runtime.TryFire(uint.MaxValue-200,13,true); runtime.TryReload(uint.MaxValue-137,14);
        var state = f.State with { SettledSequence = 14,CommittedSource = uint.MaxValue-137,State = runtime.Snapshot() };
        check(state.State.Phase == CustomWeaponPhase.Reloading && state.State.ReloadCompleteTick == 0
            && CustomCombatResults.TryReadState(Encode(state,f),f.Manifest,f.Definition,out var reloading) && reloading == state,
            "real reload crossing uint wrap retains active zero deadline rather than treating zero as absence");
        runtime.Preview(0,out var projected); state = state with { State = projected };
        check(projected.Tick == 0 && projected.Magazine == 4 && projected.Reserve == 15 && projected.ReloadCompleteTick == 0
            && CustomCombatResults.TryReadState(Encode(state,f),f.Manifest,f.Definition,out var complete) && complete == state,
            "zero presentation tick after wrapped completion stays separate from earlier committed source frontier");
    }
    private static bool Probe(Fixture f,byte[][] vectors,byte[] bad)
        => CustomCombatResults.TryReadReceipt(vectors[0],f.Manifest,f.Definition,out var receipt) && receipt == f.Receipt
            && CustomCombatResults.TryReadContact(vectors[1],f.Manifest,f.Definition,out var contact) && contact == f.Contact
            && CustomCombatResults.TryReadState(vectors[2],f.Manifest,f.Definition,out var state) && state == f.State
            && CustomResultAck.TryRead(vectors[3],out var ack) && ack == f.Receipt.Ack
            && !CustomCombatResults.TryReadReceipt(bad,f.Manifest,f.Definition,out var absent) && absent == default;
    private static void Allocation(Fixture f,Action<bool,string> check)
    {
        var vectors = new[] { Convert.FromHexString(ReceiptHex),Convert.FromHexString(ContactHex),Convert.FromHexString(StateHex),Convert.FromHexString(AckHex) };
        var bad = Change(vectors[0],33,(byte)(vectors[0][33]^1)); bool valid = true;
        for (int i=0;i<10000;i++) valid &= Probe(f,vectors,bad);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= Probe(f,vectors,bad);
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0,"100000 warmed receipt/contact/state/ack and mismatched-manifest reads allocate zero bytes");
        Console.WriteLine($"Custom result reader allocation: {bytes} bytes / 100000 warmed probes.");
    }
    private static CustomActionOutbox Box(Fixture f) => new(new(f.Bootstrap,3),f.Definition);
    private static CustomWeaponAction NonFire(CustomWeaponAction action,CustomWeaponActionKind kind)
        => action with { Kind = kind,Optic = 0,AckFrame = 0,AckSubFrame = 0,Origin = Vector3.Zero,Direction = Vector3.Zero };
    private static CustomActionReceipt Denial(CustomWeaponAction action) => new(action,201,
        CustomJournalDisposition.SourceDenied,WeaponActionResult.InvalidLife,CustomEmissionDisposition.NotAttempted,null,null,null);
    private static void OutboxReservations(Fixture f,Action<bool,string> check)
    {
        var box = Box(f); var action = f.Receipt.Receipt.Action;
        check(box.Reserve(action,0) == CustomOutboxOffer.Reserved && box.Count == 1
            && box.HasActionReservation(action) && box.HasContactReservation(action)
            && box.TryPending(0,out var pending) && pending == action,
            "Fire reserves bounded receipt and eventual contact buffers before any state-machine effect");
        check(box.Reserve(action,0) == CustomOutboxOffer.Duplicate && box.Count == 1,
            "exact action retry preserves the existing reservation");
        foreach (var changed in new[] { NonFire(action,CustomWeaponActionKind.Reload),action with { Optic = 0 },
            action with { SourceFrame = 1001 },action with { Origin = new(2,2.5f,3) },action with { Weapon = new(11) } })
            check(box.Reserve(changed,0) == (changed.Weapon.Value == 11 ? CustomOutboxOffer.Foreign : CustomOutboxOffer.Conflict)
                && box.Count == 1 && box.TryPending(0,out pending) && pending == action,
                "same owner/sequence conflict cannot replace first raw action or its capacity");
        foreach (var foreign in new[] { action with { Bootstrap = action.Bootstrap with { Revision = 2 } },
            action with { Manifest = action.Manifest with { A = action.Manifest.A^1 } },action with { Slot = 4 },default })
            check(box.Reserve(foreign,0) == CustomOutboxOffer.Foreign && box.Count == 1,
                "foreign bootstrap, manifest, recipient and default input never reserve result storage");
        for (uint seq=14;seq<29;seq++)
            check(box.Reserve(action with { Sequence = seq },0) == CustomOutboxOffer.Reserved,"all sixteen fixed result obligations can be reserved");
        check(box.Count == 16 && box.Reserve(action with { Sequence = 29 },0) == CustomOutboxOffer.Full
            && box.Reserve(action,0) == CustomOutboxOffer.Duplicate && box.TryPending(0,out pending) && pending == action,
            "seventeenth action backpressures without eviction while known duplicate remains recognizable");
        box.ReleaseUnprocessed(action with { Origin = new(9,2.5f,3) });
        check(box.Count == 16,"unprocessed reservation release requires exact immutable action");
        box.ReleaseUnprocessed(action);
        check(box.Count == 15 && !box.TryPending(0,out pending) && pending == default
            && box.Reserve(action with { Sequence = 29 },0) == CustomOutboxOffer.Reserved,
            "explicit unprocessed release makes exactly one fixed slot available");
        check(!box.TryPending(-1,out pending) && pending == default && !box.TryPending(16,out pending) && pending == default,
            "pending enumeration rejects both index bounds with default output");
        box.Close(); box.Close();
        check(box.Count == 0 && box.Reserve(action,0) == CustomOutboxOffer.Closed && !box.HasActionReservation(action)
            && !box.HasContactReservation(action) && !box.Publish(f.Receipt.Receipt) && !box.Publish(f.Contact.Outcome)
            && !box.TryNext(0,out _,out var absent,out ulong token) && absent.IsEmpty && token == 0,
            "closing is idempotent and permanently discards all pending obligations");
    }
    private static void OutboxDelivery(Fixture f,Action<bool,string> check)
    {
        var box = Box(f); var action = f.Receipt.Receipt.Action;
        box.Reserve(action,0); check(box.Publish(f.Receipt.Receipt),"accepted receipt publishes into its pre-reserved buffer");
        check(box.TryNext(0,out var type,out var bytes,out ulong token) && type == PacketType.CustomWeaponReceipt
            && CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var received)
            && received.Receipt == f.Receipt.Receipt && received.Token == token && token != 0,
            "outbox publishes exact receipt data with its own nonzero application token");
        // Read again explicitly so later checks do not depend on a short-circuit out variable.
        CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var message);
        var original = bytes.ToArray(); var ack = message.Ack;
        check(!box.Acknowledge(ack) && !box.MarkSent(0,0) && !box.MarkSent(token+100,0),
            "queued bytes are not consumed by an application ACK until actual transport admission is recorded");
        check(box.TryNext(0,out _,out var retry,out ulong same) && same == token && retry.Span.SequenceEqual(original),
            "transport backpressure keeps exact bytes and token immediately available");
        check(box.MarkSent(token,0) && !box.TryNext(.249999,out _,out _,out _)
            && box.TryNext(.25,out _,out retry,out same) && same == token && retry.Span.SequenceEqual(original),
            "reliable admission delays retries by exactly one quarter second without changing payload");
        foreach (var foreign in ForeignKeys(ack))
            check(!box.Acknowledge(foreign) && box.Count == 1,"every forged application ACK join dimension fails without consuming native-independent obligation");
        check(box.Acknowledge(ack) && box.Count == 1 && box.HasContactReservation(action),
            "receipt acknowledgement retains Fire's promised contact capacity while its projectile can still hit");
        box.ReleaseUnprocessed(action);
        check(box.Count == 1 && !box.HasActionReservation(action) && !box.TryPending(0,out _)
            && !box.Publish(f.Receipt.Receipt),"published receipt cannot be released as unprocessed or overwritten by a retry");
        check(box.Publish(f.Contact.Outcome) && box.TryNext(.25,out type,out bytes,out token)
            && type == PacketType.CustomWeaponContact && CustomCombatResults.TryReadContact(bytes.Span,f.Manifest,f.Definition,out _),
            "eventual canonical contact uses the separately reserved contact bytes");
        CustomCombatResults.TryReadContact(bytes.Span,f.Manifest,f.Definition,out var contact);
        check(!box.Acknowledge(contact.Ack) && box.MarkSent(token,.25) && box.Acknowledge(contact.Ack) && box.Count == 0,
            "contact requires its own sent token and both application ACKs finally release one entry");
        check(box.Reserve(action with { Sequence = 14 },.25) == CustomOutboxOffer.Reserved && !box.Acknowledge(ack)
            && !box.Acknowledge(contact.Ack) && box.Count == 1,"stale ACKs cannot consume a reused fixed slot");
    }
    private static void OutboxCatchup(Fixture f,Action<bool,string> check)
    {
        foreach (var disposition in new[] { CustomContactDisposition.Canonical,CustomContactDisposition.TradeDenied })
        {
            var box = Box(f); var action = f.Receipt.Receipt.Action;
            var terminal = f.Contact.Outcome with { Health = new(150,150,30,30,0),Disposition = disposition };
            box.Reserve(action,0);
            check(box.Publish(terminal) && !box.HasContactReservation(action) && box.HasActionReservation(action)
                && box.TryNext(0,out var type,out var bytes,out ulong token) && type == PacketType.CustomWeaponContact,
                "protected/trade-zero contact during catchup is publishable before launch receipt returns");
            box.TryNext(0,out _,out bytes,out token); CustomCombatResults.TryReadContact(bytes.Span,f.Manifest,f.Definition,out var contact);
            var original = bytes.ToArray();
            var contradictory = f.Receipt.Receipt with { Disposition = CustomJournalDisposition.NoSpawn,Result = WeaponActionResult.CapacityUnavailable,
                Emission = CustomEmissionDisposition.NoSpawn,After = f.Receipt.Receipt.Before };
            check(!box.Publish(contradictory) && box.HasActionReservation(action) && box.TryPending(0,out var pending) && pending == action
                && box.TryNext(0,out type,out bytes,out ulong retainedToken) && type == PacketType.CustomWeaponContact
                && retainedToken == token && bytes.Span.SequenceEqual(original),
                "definite no-emission contradicting catchup contact refuses before receipt bytes or reservation change");
            check(box.MarkSent(token,0) && box.Acknowledge(contact.Ack) && box.Count == 1,
                "out-of-order contact application ACK preserves still-unpublished action receipt");
            var changed = terminal with { Contact = terminal.Contact with { Headshot = false,Point = new(9,9,9),
                Key = terminal.Contact.Key with { Victim = new(5,4,3),Channel = CustomImpactChannel.Halfturret } } };
            check(!box.Publish(changed) && !box.Publish(terminal) && !box.TryNext(0,out _,out _,out _),
                "one contact terminal cannot be overwritten or reclassified across target/channel/point/headshot");
            check(box.Publish(f.Receipt.Receipt) && box.TryNext(0,out type,out bytes,out token) && type == PacketType.CustomWeaponReceipt,
                "receipt remains publishable after catchup contact has already been consumed");
            CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var receipt);
            check(box.MarkSent(token,0) && box.Acknowledge(receipt.Ack) && box.Count == 0 && original.Length == 116,
                "out-of-order application consumption releases capacity only after both terminals");
        }
        var deniedBox = Box(f); var deniedAction = NonFire(f.Receipt.Receipt.Action,CustomWeaponActionKind.Equip);
        check(deniedBox.Reserve(deniedAction,0) == CustomOutboxOffer.Reserved && !deniedBox.HasContactReservation(deniedAction)
            && !deniedBox.Publish(f.Contact.Outcome) && deniedBox.Publish(Denial(deniedAction)),
            "non-Fire actions reserve only receipts and cannot borrow contact capacity");
        deniedBox.TryNext(0,out _,out var deniedBytes,out ulong deniedToken);
        CustomCombatResults.TryReadReceipt(deniedBytes.Span,f.Manifest,f.Definition,out var deniedReceipt);
        check(deniedBox.MarkSent(deniedToken,0) && deniedBox.Acknowledge(deniedReceipt.Ack) && deniedBox.Count == 0,
            "no-emission source denial releases on its single receipt ACK");
        var noSpawn = Box(f); var fire = f.Receipt.Receipt.Action; noSpawn.Reserve(fire,0);
        check(!noSpawn.Publish(f.Receipt.Receipt with { Before = null }) && noSpawn.HasActionReservation(fire)
            && noSpawn.HasContactReservation(fire),"invalid receipt leaves pre-reserved effect/result capacity intact");
        var refunded = f.Receipt.Receipt with { Disposition = CustomJournalDisposition.NoSpawn,Result = WeaponActionResult.CapacityUnavailable,
            Emission = CustomEmissionDisposition.NoSpawn,After = f.Receipt.Receipt.Before };
        check(noSpawn.Publish(refunded) && !noSpawn.HasContactReservation(fire) && !noSpawn.Publish(f.Contact.Outcome),
            "definite no-spawn releases unused contact obligation and forbids later contact fabrication");
        noSpawn.TryNext(0,out _,out var noSpawnBytes,out ulong noSpawnToken);
        CustomCombatResults.TryReadReceipt(noSpawnBytes.Span,f.Manifest,f.Definition,out var noSpawnReceipt);
        check(noSpawn.MarkSent(noSpawnToken,0) && noSpawn.Acknowledge(noSpawnReceipt.Ack) && noSpawn.Count == 0,
            "refunded no-spawn needs exactly its receipt application ACK");
    }
    private static void OutboxFlights(Fixture f,Action<bool,string> check)
    {
        var box = Box(f); var action = f.Receipt.Receipt.Action; box.Reserve(action,0); box.Publish(f.Receipt.Receipt);
        box.TryNext(0,out _,out var bytes,out ulong token); CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var receipt);
        box.MarkSent(token,0); box.Acknowledge(receipt.Ack);
        uint expiry = 201u+(uint)f.Definition.Mechanics.LifetimeTicks+2;
        box.AdvanceFlights(expiry);
        check(box.Count == 1 && box.HasContactReservation(action),"flight reservation survives the exact lifetime-plus-two boundary");
        box.AdvanceFlights(expiry+1);
        check(box.Count == 0 && !box.Publish(f.Contact.Outcome),"unused contact reservation releases strictly after complete flight horizon");
        var old = Box(f); old.Reserve(action,0); old.Publish(f.Receipt.Receipt);
        var nextLife = action with { Life = (ushort)(action.Life+1) };
        check(old.Reserve(nextLife,0) == CustomOutboxOffer.Reserved && old.Publish(Denial(nextLife)) && old.Count == 2,
            "respawn same sequence reserves an independent life while preserving old-life flight result");
        check(old.Publish(f.Contact.Outcome),"new-life receipt cannot erase old-life catchup contact obligation");
        var wrapBootstrap = f.Bootstrap with { AuthorityFrame = uint.MaxValue-100 };
        var wrapAction = action with { Bootstrap = wrapBootstrap,AckFrame = uint.MaxValue-11 };
        var wrapSource = f.Receipt.Receipt.Source!.Value with { Action = wrapAction,ObservedWorldFrame = uint.MaxValue-1,TargetWorldFrame = uint.MaxValue-11 };
        var wrapReceipt = f.Receipt.Receipt with { Action = wrapAction,AuthorityFrame = uint.MaxValue,Source = wrapSource };
        var wrap = new CustomActionOutbox(new(wrapBootstrap,3),f.Definition); wrap.Reserve(wrapAction,0);
        check(wrap.Publish(wrapReceipt),"consistent result may be emitted immediately before authority uint wrap");
        wrap.TryNext(0,out _,out bytes,out token); CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out receipt);
        wrap.MarkSent(token,0); wrap.Acknowledge(receipt.Ack);
        uint life = (uint)f.Definition.Mechanics.LifetimeTicks+2;
        wrap.AdvanceFlights(life-1); check(wrap.Count == 1,"wrapped flight horizon is inclusive under serial world arithmetic");
        wrap.AdvanceFlights(life); check(wrap.Count == 0,"wrapped flight reservation releases one world past horizon");
    }
    private static void OutboxClockAndTokens(Fixture f,Action<bool,string> check)
    {
        var action = f.Receipt.Receipt.Action; var box = Box(f); box.Reserve(action,5);
        check(!box.HasExpired(20) && box.HasExpired(20.000001),"outbox deadline includes exactly fifteen seconds and expires strictly after it");
        check(box.Reserve(action,20.000001) == CustomOutboxOffer.Duplicate && box.HasExpired(20.000001),
            "duplicate retry never refreshes original application-consumption deadline");
        foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,19d })
            check(!box.TryNext(invalid,out _,out var empty,out ulong token) && empty.IsEmpty && token == 0
                && box.Reserve(action,invalid) == CustomOutboxOffer.Closed && box.Count == 1,
                "invalid or backdated wall clock cannot expose results, mutate entries or fake valid retry time");
        var field = typeof(CustomActionOutbox).GetField("_token",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var fire = Box(f); field.SetValue(fire,ulong.MaxValue-1);
        check(fire.Reserve(action,0) == CustomOutboxOffer.Closed && fire.Count == 0,
            "Fire refuses token exhaustion atomically rather than wrapping either of its two domains");
        var single = Box(f); field.SetValue(single,ulong.MaxValue-1);
        var equip = NonFire(action,CustomWeaponActionKind.Equip);
        check(single.Reserve(equip,0) == CustomOutboxOffer.Reserved && single.Publish(Denial(equip))
            && single.TryNext(0,out _,out _,out ulong last) && last == ulong.MaxValue,
            "last nonzero token remains usable by a single-result reservation");
        check(single.Reserve(equip with { Sequence = 14 },0) == CustomOutboxOffer.Closed && single.Count == 0
            && !single.MarkSent(ulong.MaxValue,0),"next token exhaustion closes all obligations and never aliases stale token zero");
    }
    private static bool OutboxProbe(CustomActionOutbox box,in CustomWeaponAction action,in CustomResultAck wrong)
        => box.Reserve(action,0) == CustomOutboxOffer.Duplicate && box.Count == 1
            && !box.HasActionReservation(action) && !box.HasContactReservation(action)
            && !box.TryPending(0,out _) && !box.Acknowledge(wrong) && !box.TryNext(0,out _,out _,out _);
    private static void OutboxAllocation(Fixture f,Action<bool,string> check)
    {
        var wire = new CustomReadinessWire(f.Bootstrap,3);
        long before = GC.GetAllocatedBytesForCurrentThread(); var box = new CustomActionOutbox(wire,f.Definition);
        long construction = GC.GetAllocatedBytesForCurrentThread()-before;
        var action = f.Receipt.Receipt.Action; box.Reserve(action,0); box.Publish(f.Receipt.Receipt); box.Publish(f.Contact.Outcome);
        box.TryNext(0,out _,out var bytes,out ulong token); CustomCombatResults.TryReadReceipt(bytes.Span,f.Manifest,f.Definition,out var receipt);
        box.MarkSent(token,0); box.TryNext(0,out _,out _,out token); box.MarkSent(token,0);
        var wrong = receipt.Ack with { Token = ulong.MaxValue }; bool valid = true;
        for (int i=0;i<10000;i++) valid &= OutboxProbe(box,action,wrong);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= OutboxProbe(box,action,wrong);
        long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && allocated == 0,"100000 warmed duplicate/reservation/pending/ACK/retry outbox probes allocate zero bytes");
        Console.WriteLine($"Custom result outbox: {construction} constructor bytes excluding wire/definition; {allocated} bytes / 100000 warmed probes.");
    }
    private static CustomStateMessage Initial(Fixture f,ushort? life = null,uint revision = 1)
    {
        var owner = new WeaponOwnerContext(f.Owner.MatchId,f.Owner.AuthorityEpoch,f.Owner.Slot,f.Owner.Generation,life ?? f.Owner.Life);
        return f.State with { Revision = revision,SettledSequence = 0,Selected = default,CommittedSource = 100,
            State = new WeaponRuntimeState(f.Definition,owner,100).Snapshot() };
    }
    private static void ClientCache(Fixture f,Action<bool,string> check)
    {
        var wire = new CustomReadinessWire(f.Bootstrap,3); var client = new CustomClientCombat(wire);
        var initial = Initial(f); var initialBytes = Encode(initial,f);
        var action = NonFire(f.Receipt.Receipt.Action,CustomWeaponActionKind.Equip) with { Sequence = 1,SourceFrame = 100 };
        check(!client.Submit(action,0) && client.State == null && !client.TryNext(0,out var absent) && absent == default,
            "client has no custom intent permission before an exact canonical current-life state");
        check(client.Receive(PacketType.CustomWeaponState,initialBytes,out var noAck) && noAck == default && client.State == initial
            && initial.State.Tick == 100 && initial.CommittedSource == 100 && initial.SettledSequence == 0 && !initial.Selected.IsValid,
            "valid initial state carries unselected source tick100 without an action ACK or manufactured settled sequence");
        check(!client.Receive(PacketType.CustomWeaponState,Change(initialBytes,33,(byte)(initialBytes[33]^1)),out noAck) && noAck == default
            && !client.Receive(PacketType.CustomWeaponState,Encode(initial with { Bootstrap = f.Bootstrap with { Revision = 2 } },f),out noAck)
            && noAck == default && !client.Receive(PacketType.Ping,initialBytes,out noAck) && noAck == default
            && client.State == initial && !client.Invalidated,
            "foreign manifest/bootstrap and unrelated packet type cannot mutate client state or mint application ACKs");
        check(client.Submit(action,0) && client.Submit(action,0) && !client.Submit(action with { SourceFrame = 101 },0)
            && !client.Submit(action with { Sequence = 3 },0) && client.TryNext(0,out var pending) && pending == action,
            "client accepts contiguous exact intents, keeps duplicate immutable, and rejects conflicts/sequence gaps");
        client.Sent(action,0);
        check(!client.TryNext(.249999,out _) && client.TryNext(.25,out pending) && pending == action,
            "client semantic sender retains original intent across quarter-second retries");
        var first = new CustomReceiptMessage(1,Denial(action));
        check(client.Receive(PacketType.CustomWeaponReceipt,Encode(first,f),out var ack) && ack == first.Ack
            && client.LastReceipt == first && !client.TryNext(.25,out _),
            "canonical receipt releases pending intent and supplies the exact full application ACK");
        check(client.Receive(PacketType.CustomWeaponReceipt,Encode(first,f),out ack) && ack == first.Ack && client.LastReceipt == first,
            "identical server receipt retry is idempotent and remains ACKable");
        var next = Initial(f,(ushort)(f.Owner.Life+1),2);
        check(client.Receive(PacketType.CustomWeaponState,Encode(next,f),out noAck) && noAck == default && client.State == next,
            "new canonical life replaces presentation resource state without touching an authority runtime");
        var nextAction = action with { Life = next.State.Owner.Life };
        var currentReceipt = new CustomReceiptMessage(2,Denial(nextAction));
        check(client.Submit(nextAction,.25) && client.Receive(PacketType.CustomWeaponReceipt,Encode(currentReceipt,f),out ack)
            && ack == currentReceipt.Ack && client.LastReceipt == currentReceipt,"new-life sequence restarts only after canonical state and keeps its own receipt");
        var lateOld = new CustomReceiptMessage(3,Denial(action with { Sequence = 2 }));
        check(client.Receive(PacketType.CustomWeaponReceipt,Encode(lateOld,f),out ack) && ack == lateOld.Ack
            && client.LastReceipt == currentReceipt && client.State == next,
            "late old-life receipt is ACKed without regressing current-life receipt or state presentation");
        check(client.Receive(PacketType.CustomWeaponState,initialBytes,out _) && client.State == next,
            "older state revision is harmless and cannot roll presentation back to a dead life");
        var contradictoryState = next with { CommittedSource = 101,State = next.State with { Tick = 101 } };
        check(!client.Receive(PacketType.CustomWeaponState,Encode(contradictoryState,f),out _) && client.Invalidated
            && !client.Submit(nextAction with { Sequence = 2 },1),"same state revision with changed canonical bytes permanently invalidates client lane");
        var duplicate = new CustomClientCombat(wire);
        duplicate.Receive(PacketType.CustomWeaponState,initialBytes,out _); duplicate.Receive(PacketType.CustomWeaponReceipt,Encode(first,f),out _);
        var changed = first with { Receipt = first.Receipt with { Disposition = CustomJournalDisposition.StaleSource,Result = WeaponActionResult.StaleFrame } };
        check(!duplicate.Receive(PacketType.CustomWeaponReceipt,Encode(changed,f),out var absentAck) && absentAck == default && duplicate.Invalidated,
            "same result token with changed valid payload cannot be acknowledged as an innocent duplicate");
        var crossed = new CustomClientCombat(wire); crossed.Receive(PacketType.CustomWeaponReceipt,Encode(first,f),out _);
        check(!crossed.Receive(PacketType.CustomWeaponContact,Encode(f.Contact with { Token = 1 },f),out absentAck)
            && absentAck == default && crossed.Invalidated,"receipt/contact type collision cannot reuse one application result token");
        var bounded = new CustomClientCombat(wire); bounded.Receive(PacketType.CustomWeaponState,initialBytes,out _);
        for (uint seq=1;seq<=16;seq++) check(bounded.Submit(action with { Sequence = seq },0),"client holds sixteen fixed pending intents");
        check(!bounded.Submit(action with { Sequence = 17 },0) && !bounded.Invalidated,"client pending saturation backpressures without invalidating or evicting first input");
        var middle = new CustomReceiptMessage(1,Denial(action with { Sequence = 8 }));
        check(bounded.Receive(PacketType.CustomWeaponReceipt,Encode(middle,f),out _) && bounded.Submit(action with { Sequence = 17 },0)
            && bounded.TryNext(15,out pending) && pending.Sequence == 1 && !bounded.Invalidated,
            "exact middle receipt frees one pending slot while oldest unconsumed action survives inclusive fifteen-second deadline");
        check(!bounded.TryNext(15.000001,out _) && bounded.Invalidated,"unconsumed action beyond fifteen seconds fails closed rather than retrying forever");
        var mismatch = new CustomClientCombat(wire); mismatch.Receive(PacketType.CustomWeaponState,initialBytes,out _); mismatch.Submit(action,0);
        var forged = new CustomReceiptMessage(1,Denial(action with { SourceFrame = 101 }));
        check(!mismatch.Receive(PacketType.CustomWeaponReceipt,Encode(forged,f),out absentAck) && absentAck == default && mismatch.Invalidated,
            "server receipt differing from pending raw action closes client rather than settling the wrong intent");
    }
    private static bool ClientProbe(CustomClientCombat client,byte[] state,byte[] receipt,byte[] contact,CustomResultAck receiptAck,CustomResultAck contactAck)
        => client.Receive(PacketType.CustomWeaponState,state,out var none) && none == default
            && client.Receive(PacketType.CustomWeaponReceipt,receipt,out var first) && first == receiptAck
            && client.Receive(PacketType.CustomWeaponContact,contact,out var second) && second == contactAck && !client.Invalidated;
    private static void ClientAllocation(Fixture f,Action<bool,string> check)
    {
        var client = new CustomClientCombat(new(f.Bootstrap,3)); var state = Encode(Initial(f),f);
        var receipt = f.Receipt with { Token = 1 }; var contact = f.Contact with { Token = 2 };
        var receiptBytes = Encode(receipt,f); var contactBytes = Encode(contact,f); bool valid = true;
        for (int i=0;i<10000;i++) valid &= ClientProbe(client,state,receiptBytes,contactBytes,receipt.Ack,contact.Ack);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) valid &= ClientProbe(client,state,receiptBytes,contactBytes,receipt.Ack,contact.Ack);
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0,"100000 warmed state/receipt/contact duplicate client-cache reads and exact ACK creation allocate zero bytes");
        Console.WriteLine($"Custom client result-cache allocation: {bytes} bytes / 100000 warmed probes.");
    }
}
