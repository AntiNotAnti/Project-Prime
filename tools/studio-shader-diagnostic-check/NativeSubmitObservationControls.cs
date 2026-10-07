using System.Runtime.InteropServices;
using MphRead.Mods.Render;

// Portable CPU controls for the exact source-linked managed/native metadata
// contract. These do not load an export, graphics API, driver, device or surface.
internal static class NativeSubmitObservationControls
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
            checks++;
        }
        static byte[] Bytes(NativeSubmitObservationIdentity value)
            => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).ToArray();
        static bool Zero(NativeSubmitObservationIdentity value) => Bytes(value).All(b => b == 0);
        static bool Capture(ShaderDiagnosticReadbackObservation owner, out NativeSubmitObservationIdentity value)
            => owner.TryCaptureNativeSubmitObservation("1", "1", "1", out value);

        Check(Marshal.SizeOf<NativeSubmitObservationIdentity>() == 32, "native observation ABI occupies exactly 32 bytes");
        string[] fields = { "Fixture", "Scope", "ManagedThread", "Read", "Submit", "Magic", "Reserved" };
        int[] offsets = { 0, 8, 16, 20, 22, 24, 28 };
        Check(fields.Select((field, i) => (int)Marshal.OffsetOf<NativeSubmitObservationIdentity>(field) == offsets[i]).All(v => v),
            "all seven scalar ABI offsets match independent repr(C) contract");
        var golden = new NativeSubmitObservationIdentity(0x0807060504030201, 0x1817161514131211, 0x24232221, 6, 3);
        byte[] expected = Convert.FromHexString("0102030405060708111213141516171821222324060003004F42533100000000");
        Check(BitConverter.IsLittleEndian && Bytes(golden).SequenceEqual(expected),
            "independently specified little-endian bytes preserve every scalar and OBS1 magic/reserved zero");
        var reverse = MemoryMarshal.Read<NativeSubmitObservationIdentity>(expected);
        Check(reverse.Fixture == golden.Fixture && reverse.Scope == golden.Scope && reverse.ManagedThread == golden.ManagedThread
            && reverse.Read == 6 && reverse.Submit == 3 && reverse.Magic == 0x3153424f && reverse.Reserved == 0,
            "independent ABI bytes decode to exact managed scalar values");

        string?[] flags = { null, "", "0", "1", "true", " 1", "1 " };
        int disabledCases = 0;
        foreach (string? diagnostic in flags)
        foreach (string? validation in flags)
        foreach (string? gpu in flags)
        {
            if (diagnostic == "1" && validation == "1" && gpu == "1") continue;
            var disabled = new ShaderDiagnosticReadbackObservation();
            int clocks = 0;
            using var scope = disabled.BeginLayeredScope(diagnostic, validation, gpu, new ThrowingWriter(),
                () => { clocks++; throw new IOException("unexpected disabled clock"); });
            using var read = disabled.BeginReadback(0, 0, 1, 1);
            if (disabled.HasOwnedReadbackForNativeObservation
                || disabled.TryCaptureNativeSubmitObservation(diagnostic, validation, gpu, out var identity)
                || !Zero(identity) || clocks != 0)
                throw new InvalidOperationException("nonexact native observation flags adopted metadata or sampled a clock");
            disabledCases++;
        }
        Check(disabledCases == 342, "all 342 nonexact opt-in combinations leave zero metadata and no clock access");

        var owner = new ShaderDiagnosticReadbackObservation();
        int samples = 0;
        long Clock() => ++samples;
        Check(!owner.HasOwnedReadbackForNativeObservation && !Capture(owner, out var outside) && Zero(outside),
            "native observation requires an active readback and check owner");
        NativeSubmitObservationIdentity first = default;
        IDisposable oldScope = owner.BeginLayeredScope("1", "1", "1", new ThrowingWriter(), Clock);
        Check(!Capture(owner, out var withoutRead) && Zero(withoutRead), "check owner alone cannot capture native metadata");
        IDisposable oldRead = owner.BeginReadback(0, 0, 1, 1);
        int beforeCapture = samples;
        Check(owner.HasOwnedReadbackForNativeObservation && Capture(owner, out first) && first.Fixture > 0 && first.Scope == 1
            && first.ManagedThread == (uint)Environment.CurrentManagedThreadId && first.Read == 1 && first.Submit == 1
            && samples == beforeCapture, "owned metadata capture preserves owner identity without clock or sink access");
        foreach (int position in Enumerable.Range(0, 3))
        {
            string?[] invalid = { "1", "1", "1" }; invalid[position] = "0";
            Check(!owner.TryCaptureNativeSubmitObservation(invalid[0], invalid[1], invalid[2], out var denied) && Zero(denied),
                "capture rechecks each exact opt-in before emitting metadata");
        }
        using (owner.BeginLayeredScope("1", "1", "1", new ThrowingWriter(), Clock))
        using (owner.BeginReadback(9, 9, 1, 1))
            Check(Capture(owner, out var nested) && nested.Fixture == first.Fixture && nested.Read == 1 && nested.Submit == 2
                && samples == beforeCapture, "inactive nested leases cannot replace or sample the actual readback owner");
        bool foreignDenied = false;
        var thread = new Thread(() =>
        {
            using var foreignScope = owner.BeginLayeredScope("1", "1", "1", new ThrowingWriter(), Clock);
            using var foreignRead = owner.BeginReadback(0, 0, 1, 1);
            foreignDenied = !owner.HasOwnedReadbackForNativeObservation && !Capture(owner, out var denied) && Zero(denied);
        });
        thread.Start(); thread.Join();
        Check(foreignDenied && samples == beforeCapture, "real foreign thread cannot capture, replace or revoke the current owner");
        bool ordinals = true;
        for (int submit = 3; submit <= 8; submit++) ordinals &= Capture(owner, out var identity) && identity.Submit == submit;
        Check(ordinals && !Capture(owner, out var ninthSubmit) && Zero(ninthSubmit) && samples == beforeCapture,
            "eight submissions per readback are bounded and the ninth produces zero metadata before clock access");
        oldRead.Dispose();
        Check(!Capture(owner, out var disposedRead) && Zero(disposedRead), "disposed readback cannot retain native metadata ownership");
        bool readOrdinals = true;
        for (int read = 2; read <= 8; read++)
        {
            using var lease = owner.BeginReadback(0, 0, 1, 1);
            readOrdinals &= Capture(owner, out var identity) && identity.Read == read && identity.Submit == 1;
        }
        using (owner.BeginReadback(0, 0, 1, 1))
            Check(readOrdinals && !Capture(owner, out var ninthRead) && Zero(ninthRead),
                "eight reads preserve unique ordinals and reset submit count; ninth read cannot emit native metadata");
        oldScope.Dispose();
        Check(!owner.HasOwnedReadbackForNativeObservation && !Capture(owner, out var disposedScope) && Zero(disposedScope),
            "disposed fixture cannot retain readback ownership");
        using (owner.BeginLayeredScope("1", "1", "1", new ThrowingWriter(), Clock))
        using (owner.BeginReadback(0, 0, 1, 1))
        {
            oldRead.Dispose(); oldScope.Dispose();
            Check(Capture(owner, out var next) && next.Fixture != first.Fixture && next.Scope == 2 && next.Read == 1,
                "stale lease disposal cannot revoke a replacement fixture or reuse its native identity");
        }
        var otherOwner = new ShaderDiagnosticReadbackObservation();
        using (otherOwner.BeginLayeredScope("1", "1", "1", new ThrowingWriter(), Clock))
        using (otherOwner.BeginReadback(0, 0, 1, 1))
            Check(Capture(otherOwner, out var other) && other.Fixture != first.Fixture,
                "separate managed owners cannot collide in process-wide native fixture IDs");
        return checks;
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("metadata capture must not access an observation sink");
    }
}
