using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

// Pure capability-handshake state machines. No sockets, opt-in configuration,
// source boundary, authenticated peer or gameplay admission is proven here.
internal static class ReadinessChecks
{
    private static readonly WorldBootstrapIdentity Bootstrap = new(new(0x1234,0x0102030405060708UL,0x11223344),
        0x55667788,0x99aa,0x10203040,0x50607080,0x90a0b0c0);
    private const ulong Connection = 0xfedcba9876543210UL;
    // Independently derived with Python struct/uuid/hashlib from the literal M7
    // mechanics object, CWM1 canonical row and <HQIIHIII recipient bootstrap.
    private const string ManifestSha = "a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9";
    private const string ManifestHex = "01341208070605040302014433221188776655aa994030201080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e90100010a009a776851f39f4a768c13c5d88875ef613ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228";
    private const string ReadyHex = "01341208070605040302014433221188776655aa994030201080706050c0b0a090a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e903010001000100";
    private static CustomReadinessPeer Peer(double now = 10) => new(Bootstrap,3,Connection,now);
    private static CustomReadinessClient Client() => new(Bootstrap,3);
    private static byte[] Changed(byte[] bytes,int offset,byte value)
    { var copy = bytes.ToArray(); copy[offset] = value; return copy; }
    private static void Reject(Action action,Action<bool,string> check,string label)
    {
        try { action(); }
        catch (ArgumentException) { check(true,label); return; }
        throw new InvalidOperationException("Expected rejection: "+label);
    }
    internal static void Run(Action<bool,string> check)
    {
        KnownWire(check); ServerIdentity(check); ServerContradictions(check); ServerClock(check); ServerFloodBudget(check);
        ClientOrdering(check); ClientContradictions(check); ClientDeadline(check); MetadataWorlds(check); Allocation(check);
    }
    private static void KnownWire(Action<bool,string> check)
    {
        var wire = new CustomReadinessWire(Bootstrap,3);
        check(wire.Bootstrap == Bootstrap && wire.Slot == 3 && wire.Manifest.Rows.Count == 1
            && wire.Manifest.Rows[0].ContentId == RiftlineM7.ContentId && wire.Manifest.Rows[0].RuntimeId.Value == 10
            && wire.Manifest.Digest.ToString() == ManifestSha,"readiness wire contains exactly the reviewed built-in M7 definition");
        check(wire.ManifestBytes.AsSpan().SequenceEqual(Convert.FromHexString(ManifestHex))
            && wire.ReadyBytes.AsSpan().SequenceEqual(Convert.FromHexString(ReadyHex)),
            "independent golden manifest and recipient readiness bytes match known M7 package exactly");
        check(CustomManifestPage.TryRead(wire.ManifestBytes,out var page) && page!.TotalRows == 1 && page.Index == 0
            && page.PageCount == 1 && page.Rows[0] == wire.Manifest.Rows[0]
            && CustomWeaponReady.TryRead(wire.ReadyBytes,out var ready) && ready.Bootstrap == Bootstrap
            && ready.Slot == 3 && ready.Manifest == wire.Manifest.Digest && ready.Schema == 1 && ready.Behavior == 1
            && ready.Capabilities == CustomWeaponReady.PrecisionCapability,"cached readiness wire retains exact package/schema/behavior/capability and recipient");
        check(!typeof(ICustomSourceReadiness).IsAssignableFrom(typeof(CustomPeerReadyEvidence)),
            "capability evidence does not provide the source-boundary readiness interface");
        Reject(()=>new CustomReadinessWire(default,3),check,"absent bootstrap cannot create a readiness wire");
        Reject(()=>new CustomReadinessWire(Bootstrap,8),check,"invalid recipient cannot create a readiness wire");
        Reject(()=>new CustomReadinessPeer(Bootstrap,3,0,10),check,"server state requires a nonzero connection incarnation");
        foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity })
        {
            Reject(()=>new CustomReadinessPeer(Bootstrap,3,Connection,invalid),check,"server state rejects nonfinite initial clocks");
            Reject(()=>new CustomReadinessClient(Bootstrap,3,invalid),check,"client state rejects nonfinite initial clocks");
        }
    }
    private static IEnumerable<WorldBootstrapIdentity> ForeignBootstraps()
    {
        yield return Bootstrap with { Start = Bootstrap.Start with { MatchId = 0x1235 } };
        yield return Bootstrap with { Start = Bootstrap.Start with { AuthorityEpoch = Bootstrap.Start.AuthorityEpoch+1 } };
        yield return Bootstrap with { Start = Bootstrap.Start with { StartGeneration = Bootstrap.Start.StartGeneration+1 } };
        yield return Bootstrap with { Revision = Bootstrap.Revision+1 };
        yield return Bootstrap with { SlotGeneration = (ushort)(Bootstrap.SlotGeneration+1) };
        yield return Bootstrap with { AuthorityFrame = Bootstrap.AuthorityFrame+1 };
        yield return Bootstrap with { SlowRevision = Bootstrap.SlowRevision+1 };
        yield return Bootstrap with { WorldRevision = Bootstrap.WorldRevision+1 };
    }
    private static void ServerIdentity(Action<bool,string> check)
    {
        var peer = Peer();
        check(peer.Evidence == null && !peer.Invalidated,"new server peer owns no speculative readiness evidence");
        foreach (ulong wrong in new[] { 0UL,Connection^0x8000000000000000UL,Connection+1 })
            check(peer.Receive(peer.Wire.ReadyBytes,wrong,100,10) == CustomReadinessResult.Ignored
                && peer.Evidence == null && !peer.Invalidated,"wrong connection cannot confirm or poison this live peer");
        foreach (var bootstrap in ForeignBootstraps())
        {
            var stale = new CustomReadinessWire(bootstrap,3);
            check(peer.Receive(stale.ReadyBytes,Connection,100,10) == CustomReadinessResult.Ignored
                && peer.Evidence == null && !peer.Invalidated,"every different full bootstrap field is ignored without poisoning the current peer");
        }
        foreach (var payload in new[] { Array.Empty<byte>(),peer.Wire.ReadyBytes[..64],
            Changed(peer.Wire.ReadyBytes,0,2),new byte[CustomWeaponReady.Size] })
            check(peer.Receive(payload,Connection,100,10) == CustomReadinessResult.Ignored
                && peer.Evidence == null && !peer.Invalidated,"unreadable header cannot name or poison an admitted bootstrap");
        check(peer.Receive(peer.Wire.ReadyBytes,Connection,100,10) == CustomReadinessResult.Accepted,
            "exact matching peer readiness publishes its first capability fact");
        var first = peer.Evidence;
        check(first is not null && first.Bootstrap == Bootstrap && first.Manifest == peer.Wire.Manifest.Digest
            && first.Slot == 3 && first.ConnectionId == Connection && first.OpenedWorld == 100,
            "published fact preserves full recipient/bootstrap/manifest/connection and first world");
        check(peer.Receive(peer.Wire.ReadyBytes,Connection,999,11) == CustomReadinessResult.Duplicate
            && ReferenceEquals(peer.Evidence,first) && peer.Evidence!.OpenedWorld == 100,
            "duplicate readiness cannot refresh first-open world or replace the immutable publication");
    }
    private static IEnumerable<byte[]> ReadyContradictions(CustomReadinessWire wire)
    {
        yield return Changed(wire.ReadyBytes,33,(byte)(wire.ReadyBytes[33]^1));
        yield return Changed(wire.ReadyBytes,65,4); // different recipient
        yield return Changed(wire.ReadyBytes,66,2); // different schema
        yield return Changed(wire.ReadyBytes,68,2); // different behavior
        yield return Changed(wire.ReadyBytes,70,3); // unknown capability
        yield return wire.ReadyBytes[..CustomWeaponProtocol.HeaderSize];
        yield return wire.ReadyBytes.Concat(new byte[] { 0 }).ToArray();
        yield return wire.ManifestBytes; // readable same header, wrong payload family
    }
    private static void ServerContradictions(Action<bool,string> check)
    {
        foreach (bool opened in new[] { false,true })
        foreach (var changed in ReadyContradictions(new CustomReadinessWire(Bootstrap,3)))
        {
            var peer = Peer();
            if (opened) peer.Receive(peer.Wire.ReadyBytes,Connection,100,10);
            check(peer.Receive(changed,Connection,101,11) == CustomReadinessResult.Invalidated
                && peer.Invalidated && peer.Evidence == null,"readable current-bootstrap contradiction revokes server readiness before or after confirmation");
            check(peer.Receive(peer.Wire.ReadyBytes,Connection,102,12) == CustomReadinessResult.Ignored
                && peer.Evidence == null && !peer.Due(12),"correct retry cannot restore a poisoned bootstrap capability");
        }
        var explicitRetirement = Peer(); explicitRetirement.Receive(explicitRetirement.Wire.ReadyBytes,Connection,100,10);
        explicitRetirement.Invalidate(); explicitRetirement.Invalidate();
        check(explicitRetirement.Evidence == null && explicitRetirement.Invalidated
            && explicitRetirement.Receive(explicitRetirement.Wire.ReadyBytes,Connection,101,11) == CustomReadinessResult.Ignored,
            "explicit lifecycle invalidation is idempotent and terminal");
    }
    private static void ServerClock(Action<bool,string> check)
    {
        var peer = Peer(0);
        check(peer.Due(0) && !peer.Due(0) && !peer.Due(.249999) && peer.Due(.25),
            "server manifest resend is immediately due then bounded by exact quarter-second intervals");
        foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,.24 })
            check(!peer.Due(invalid) && peer.Receive(peer.Wire.ReadyBytes,Connection,100,invalid) == CustomReadinessResult.Ignored
                && !peer.Invalidated && peer.Evidence == null,"nonfinite or backward clock does not change pending peer state");
        check(!peer.Due(.499999) && peer.Due(.5),"invalid clock probes cannot alter the next valid resend deadline");
        var inclusive = Peer(0);
        check(inclusive.Due(30) && !inclusive.Invalidated
            && inclusive.Receive(inclusive.Wire.ReadyBytes,Connection,100,30) == CustomReadinessResult.Accepted,
            "readiness may open at the inclusive thirty-second deadline");
        var first = inclusive.Evidence;
        check(!inclusive.Due(30000) && !inclusive.Invalidated
            && inclusive.Receive(inclusive.Wire.ReadyBytes,Connection,999,30000) == CustomReadinessResult.Duplicate
            && ReferenceEquals(first,inclusive.Evidence),"confirmed capability remains active beyond the pending handshake deadline");
        var expiredSend = Peer(0);
        check(!expiredSend.Due(30.000001) && expiredSend.Invalidated && expiredSend.Evidence == null,
            "pending resend retires immediately after thirty seconds");
        var expiredReceive = Peer(0);
        check(expiredReceive.Receive(expiredReceive.Wire.ReadyBytes,Connection,100,30.000001) == CustomReadinessResult.Invalidated
            && expiredReceive.Evidence == null,"late otherwise exact readiness cannot open expired pending state");
        var receiveClock = Peer(0);
        check(receiveClock.Receive(receiveClock.Wire.ReadyBytes,Connection,100,1) == CustomReadinessResult.Accepted
            && receiveClock.Receive(receiveClock.Wire.ReadyBytes,Connection,101,.999) == CustomReadinessResult.Ignored
            && receiveClock.Evidence!.OpenedWorld == 100,"backdated receive cannot revise or replace already-published capability");
    }
    private static void ClientOrdering(Action<bool,string> check)
    {
        var client = Client();
        check(!client.ManifestAccepted && !client.Confirmed && !client.Due(0)
            && client.Receive(PacketType.CustomWeaponReadyConfirmed,client.Wire.ReadyBytes) == CustomReadinessResult.Ignored,
            "client cannot confirm or send readiness before the exact manifest is accepted");
        check(client.Receive(PacketType.CustomWeaponReady,client.Wire.ReadyBytes) == CustomReadinessResult.Ignored
            && !client.Confirmed && !client.Invalidated,"wrong-direction readiness packet cannot confirm client capability");
        check(client.Receive(PacketType.CustomWeaponManifest,client.Wire.ManifestBytes) == CustomReadinessResult.Accepted
            && client.ManifestAccepted && !client.Confirmed,"exact cached single-package manifest accepts without speculative confirmation");
        check(client.Due(0) && !client.Due(0) && !client.Due(.249999) && client.Due(.25),
            "client ready resend obeys exact quarter-second spacing");
        foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,.24 })
            check(!client.Due(invalid),"client rejects nonfinite and backdated resend clocks");
        check(!client.Due(.499999) && client.Due(.5),"invalid client clock probes cannot accelerate the next valid ready send");
        check(client.Receive(PacketType.CustomWeaponReadyConfirmed,client.Wire.ReadyBytes,.5) == CustomReadinessResult.Accepted
            && client.Confirmed && !client.Due(100000),"only post-manifest exact confirmation ends client resend");
        check(client.Receive(PacketType.CustomWeaponManifest,client.Wire.ManifestBytes,100000) == CustomReadinessResult.Duplicate
            && client.Receive(PacketType.CustomWeaponReadyConfirmed,client.Wire.ReadyBytes,100000) == CustomReadinessResult.Duplicate
            && client.Confirmed,"repeated manifest/confirmation is idempotent after readiness");
        foreach (var bootstrap in ForeignBootstraps())
        {
            var stale = new CustomReadinessWire(bootstrap,3);
            check(client.Receive(PacketType.CustomWeaponManifest,stale.ManifestBytes,100000) == CustomReadinessResult.Ignored
                && client.Receive(PacketType.CustomWeaponReadyConfirmed,stale.ReadyBytes,100000) == CustomReadinessResult.Ignored
                && client.Confirmed && !client.Invalidated,"different bootstrap packets cannot replace or revoke current client readiness");
        }
    }
    private static void ServerFloodBudget(Action<bool,string> check)
    {
        var peer = Peer(0);
        check(peer.Receive(peer.Wire.ReadyBytes,Connection,100,0) == CustomReadinessResult.Accepted,"flood fixture opens one exact capability fact");
        var first = peer.Evidence;
        for (int i=1;i<CustomReadinessPeer.OffersPerSecond;i++)
            check(peer.Receive(peer.Wire.ReadyBytes,Connection,999,.999999) == CustomReadinessResult.Duplicate,
                "identical readiness within per-second offer budget is an allocation-free duplicate");
        var changed = Changed(peer.Wire.ReadyBytes,65,4);
        check(peer.Receive(changed,Connection,999,.999999) == CustomReadinessResult.Ignored && !peer.Invalidated
            && ReferenceEquals(first,peer.Evidence),"excess current-bootstrap offer is dropped before payload classification without revising capability");
        check(peer.Receive(peer.Wire.ReadyBytes,Connection,999,1) == CustomReadinessResult.Duplicate
            && ReferenceEquals(first,peer.Evidence),"offer budget reopens at exact one-second boundary without refreshing first world");
        check(peer.Receive(changed,Connection,999,1) == CustomReadinessResult.Invalidated && peer.Evidence == null,
            "current contradiction within refreshed budget still permanently revokes the capability");
        var wrongConnection = Peer(0);
        bool ignored = true;
        for (int i=0;i<1000;i++) ignored &= wrongConnection.Receive(wrongConnection.Wire.ReadyBytes,Connection+1,100,0) == CustomReadinessResult.Ignored;
        check(ignored && wrongConnection.Receive(wrongConnection.Wire.ReadyBytes,Connection,100,0) == CustomReadinessResult.Accepted,
            "wrong connection offers cannot charge the current peer's readiness budget");
        var malformed = Peer(0);
        for (int i=0;i<CustomReadinessPeer.OffersPerSecond;i++)
            check(malformed.Receive(ReadOnlySpan<byte>.Empty,Connection,100,0) == CustomReadinessResult.Ignored,"malformed current-connection offers consume the fixed shared decode budget");
        check(malformed.Receive(malformed.Wire.ReadyBytes,Connection,100,0) == CustomReadinessResult.Ignored
            && malformed.Evidence == null && malformed.Receive(malformed.Wire.ReadyBytes,Connection,100,1) == CustomReadinessResult.Accepted,
            "malformed flood is bounded before decoding and an exact later-window ready can still open");
    }
    private static void ClientContradictions(Action<bool,string> check)
    {
        var wire = new CustomReadinessWire(Bootstrap,3);
        var extra = WeaponDefinition.Create(Guid.Parse("ffffffff-0000-0000-0000-000000000001"),"unknown local package",MechanicsChecks.Rules());
        var revision = WeaponDefinition.Create(RiftlineM7.ContentId,"different M7 mechanics",
            MechanicsChecks.Rules(MechanicsChecks.Json.Replace("\"body\":65","\"body\":66")));
        foreach (var definitions in new[] { new[] { extra },new[] { RiftlineM7.Create(),extra },new[] { revision } })
        {
            var manifest = new CustomWeaponManifest(definitions);
            var page = new CustomManifestPage(Bootstrap,manifest.Digest,(byte)manifest.Rows.Count,0,manifest.Rows.ToArray());
            var bytes = new byte[page.Size]; page.Write(bytes); var client = Client();
            check(CustomManifestPage.TryRead(bytes,out _) && client.Receive(PacketType.CustomWeaponManifest,bytes) == CustomReadinessResult.Invalidated
                && !client.ManifestAccepted && !client.Confirmed,
                "syntactically valid unknown, extra or revised packages cannot satisfy strict built-in-only readiness");
        }
        var manifests = new List<byte[]> { Changed(wire.ManifestBytes,33,(byte)(wire.ManifestBytes[33]^1)),
            Changed(wire.ManifestBytes,65,2),Changed(wire.ManifestBytes,66,1),Changed(wire.ManifestBytes,67,0),
            Changed(wire.ManifestBytes,68,4),Changed(wire.ManifestBytes,70,(byte)(wire.ManifestBytes[70]^1)),
            Changed(wire.ManifestBytes,86,(byte)(wire.ManifestBytes[86]^1)),wire.ManifestBytes[..^1],
            wire.ManifestBytes.Concat(new byte[] { 0 }).ToArray(),wire.ReadyBytes };
        foreach (bool opened in new[] { false,true })
        foreach (var payload in manifests)
        {
            var client = Client();
            if (opened)
            { client.Receive(PacketType.CustomWeaponManifest,wire.ManifestBytes); client.Receive(PacketType.CustomWeaponReadyConfirmed,wire.ReadyBytes); }
            check(client.Receive(PacketType.CustomWeaponManifest,payload) == CustomReadinessResult.Invalidated
                && client.Invalidated && !client.Confirmed,"any changed current-bootstrap package row/header/shape permanently poisons client readiness");
            check(client.Receive(PacketType.CustomWeaponManifest,wire.ManifestBytes) == CustomReadinessResult.Ignored
                && client.Receive(PacketType.CustomWeaponReadyConfirmed,wire.ReadyBytes) == CustomReadinessResult.Ignored
                && !client.Due(100),"cached exact package cannot restore an invalidated client capability");
        }
        foreach (var payload in ReadyContradictions(wire))
        {
            var client = Client(); client.Receive(PacketType.CustomWeaponManifest,wire.ManifestBytes);
            check(client.Receive(PacketType.CustomWeaponReadyConfirmed,payload) == CustomReadinessResult.Invalidated
                && client.Invalidated && !client.Confirmed,"changed confirmation cannot grant different recipient/schema/capability/package");
        }
        var malformed = Client();
        foreach (var payload in new[] { Array.Empty<byte>(),wire.ManifestBytes[..64],Changed(wire.ManifestBytes,0,2),new byte[118] })
            check(malformed.Receive(PacketType.CustomWeaponManifest,payload) == CustomReadinessResult.Ignored
                && !malformed.Invalidated && !malformed.ManifestAccepted,"unreadable manifest header is distinct from a contradiction naming the current bootstrap");
    }
    private static void MetadataWorlds(Action<bool,string> check)
    {
        foreach (uint world in new uint[] { 0,uint.MaxValue,1 })
        {
            var peer = Peer();
            check(peer.Receive(peer.Wire.ReadyBytes,Connection,world,10) == CustomReadinessResult.Accepted
                && peer.Evidence!.OpenedWorld == world,"capability first-open metadata preserves full uint world including zero and wrap values");
            var first = peer.Evidence;
            check(peer.Receive(peer.Wire.ReadyBytes,Connection,unchecked(world+1),11) == CustomReadinessResult.Duplicate
                && ReferenceEquals(first,peer.Evidence) && peer.Evidence!.OpenedWorld == world,
                "wrapped duplicate world metadata cannot manufacture a newer source boundary");
        }
    }
    private static void ClientDeadline(Action<bool,string> check)
    {
        var inclusive = Client();
        check(inclusive.Receive(PacketType.CustomWeaponManifest,inclusive.Wire.ManifestBytes,30) == CustomReadinessResult.Accepted
            && inclusive.Receive(PacketType.CustomWeaponReadyConfirmed,inclusive.Wire.ReadyBytes,30) == CustomReadinessResult.Accepted
            && !inclusive.Invalidated,"client manifest and confirmation can complete at inclusive thirty seconds");
        check(!inclusive.Due(30000) && inclusive.Confirmed && !inclusive.Invalidated
            && inclusive.Receive(PacketType.CustomWeaponReadyConfirmed,inclusive.Wire.ReadyBytes,30000) == CustomReadinessResult.Duplicate,
            "confirmed client capability remains valid beyond the pending deadline");
        var noManifest = Client();
        check(!noManifest.Due(30) && !noManifest.Invalidated && !noManifest.Due(30.000001) && noManifest.Invalidated
            && noManifest.Receive(PacketType.CustomWeaponManifest,noManifest.Wire.ManifestBytes,31) == CustomReadinessResult.Ignored,
            "client without a manifest retires only after the inclusive deadline and cannot revive");
        var noConfirmation = Client(); noConfirmation.Receive(PacketType.CustomWeaponManifest,noConfirmation.Wire.ManifestBytes);
        check(noConfirmation.Due(30) && !noConfirmation.Invalidated
            && noConfirmation.Receive(PacketType.CustomWeaponReadyConfirmed,noConfirmation.Wire.ReadyBytes,30.000001) == CustomReadinessResult.Ignored
            && noConfirmation.Invalidated && !noConfirmation.Confirmed,"late confirmation cannot escape client pending deadline after manifest acceptance");
        var monotonic = Client(); monotonic.Receive(PacketType.CustomWeaponManifest,monotonic.Wire.ManifestBytes,1);
        foreach (double invalid in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity,.999 })
            check(monotonic.Receive(PacketType.CustomWeaponReadyConfirmed,monotonic.Wire.ReadyBytes,invalid) == CustomReadinessResult.Ignored
                && !monotonic.Confirmed && !monotonic.Invalidated,"invalid/backdated receive clock cannot confirm or poison pending client state");
        check(monotonic.Receive(PacketType.CustomWeaponReadyConfirmed,monotonic.Wire.ReadyBytes,1) == CustomReadinessResult.Accepted,
            "invalid receive probes do not advance client clock or block later exact confirmation");
    }
    private static bool Probe(CustomReadinessPeer peer,CustomReadinessClient client,byte[] staleReady,byte[] staleManifest,
        CustomPeerReadyEvidence first,double now)
        => peer.Receive(peer.Wire.ReadyBytes,Connection,999,now) == CustomReadinessResult.Duplicate
            && ReferenceEquals(peer.Evidence,first) && !peer.Due(now)
            && peer.Receive(peer.Wire.ReadyBytes,Connection+1,999,now) == CustomReadinessResult.Ignored
            && peer.Receive(staleReady,Connection,999,now) == CustomReadinessResult.Ignored
            && client.Receive(PacketType.CustomWeaponManifest,client.Wire.ManifestBytes,now) == CustomReadinessResult.Duplicate
            && client.Receive(PacketType.CustomWeaponReadyConfirmed,client.Wire.ReadyBytes,now) == CustomReadinessResult.Duplicate
            && client.Receive(PacketType.CustomWeaponManifest,staleManifest,now) == CustomReadinessResult.Ignored
            && client.Receive(PacketType.CustomWeaponReadyConfirmed,staleReady,now) == CustomReadinessResult.Ignored
            && client.Receive(PacketType.Intent,client.Wire.ReadyBytes,now) == CustomReadinessResult.Ignored
            && client.Confirmed && !client.Due(now);
    private static void Allocation(Action<bool,string> check)
    {
        var peer = Peer(); peer.Receive(peer.Wire.ReadyBytes,Connection,100,10);
        var client = Client(); client.Receive(PacketType.CustomWeaponManifest,client.Wire.ManifestBytes);
        client.Receive(PacketType.CustomWeaponReadyConfirmed,client.Wire.ReadyBytes);
        var stale = new CustomReadinessWire(Bootstrap with { Revision = Bootstrap.Revision+1 },3);
        var first = peer.Evidence!; bool valid = true;
        var flood = Peer(); flood.Receive(flood.Wire.ReadyBytes,Connection,100,10); var floodFirst = flood.Evidence;
        for (int i=0;i<CustomReadinessPeer.OffersPerSecond;i++) flood.Receive(flood.Wire.ReadyBytes,Connection,100,10);
        for (int i=0;i<10000;i++) valid &= Probe(peer,client,stale.ReadyBytes,stale.ManifestBytes,first,10+i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++)
        {
            valid &= Probe(peer,client,stale.ReadyBytes,stale.ManifestBytes,first,10010+i);
            valid &= flood.Receive(flood.Wire.ReadyBytes,Connection,999,10) == CustomReadinessResult.Ignored
                && ReferenceEquals(floodFirst,flood.Evidence);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread()-before;
        check(valid && bytes == 0,"100000 warmed readiness duplicate/lookup/stale/connection/type-mismatch probes allocate zero bytes");
        Console.WriteLine($"Custom readiness duplicate/lookup allocation: {bytes} bytes / 100000 warmed probes.");
    }
}
