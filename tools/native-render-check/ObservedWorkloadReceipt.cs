using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using MphRead;
using MphRead.Mods.Network;

// Observations in the existing window. No input/replay consumer, simulation,
// timing scheduler or render owner is created by this receipt.
sealed partial class CheckWindow
{
    private record RngObservation(uint rng1,uint rng2);
    private record RosterObservation(int slot,string hunter,bool bot);
    private record BinaryObservation(string name,string sha256,Guid mvid);
    private RngObservation? _receiptAssignedSeeds;
    private RosterObservation[]? _receiptStartRoster,_receiptEndRoster;
    private RngObservation? _receiptStartRng,_receiptEndRng;
    private BinaryObservation? _receiptClient,_receiptHarness;
    private ulong? _receiptStartTick,_receiptEndTick;
    private bool _receiptStartOffline,_receiptEndOffline,_receiptUnprofiled;
    private string? _receiptFailure;
    private long _receiptEndAllocationOverhead;
    private readonly bool _lateProvenanceControl;
    private bool _receiptBinaryIdentityObserved;
    private void ObserveAssignedWorkloadSeeds()
        => _receiptAssignedSeeds=new(Scene.Random.Rng1,Scene.Random.Rng2);
    private RosterObservation[] ObserveWorkloadRoster()=>Scene.Players.Items.Select(p=>new RosterObservation(p.SlotIndex,p.Hunter.ToString(),p.IsBot)).OrderBy(p=>p.slot).ToArray();
    private bool WorkloadOffline()=>!NetSession.Active&&!Scene.Services.IsReplica;
    private static BinaryObservation ObserveWorkloadBinary(Assembly assembly)
    {
        // Bind the file hash to the actually loaded module, rather than a later
        // unrelated DLL placed at the same path. This is not build attestation.
        using var input=File.OpenRead(assembly.Location);
        string hash=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();input.Position=0;
        using var pe=new PEReader(input);var metadata=pe.GetMetadataReader();
        Guid fileMvid=metadata.GetGuid(metadata.GetModuleDefinition().Mvid),loadedMvid=assembly.ManifestModule.ModuleVersionId;
        if(fileMvid!=loadedMvid)throw new IOException("Loaded module and preserved binary MVID differ.");
        return new(assembly.GetName().Name??"",hash,loadedMvid);
    }
    private static bool WorkloadProfilerEnabled(string name)
    {
        var type=typeof(Scene).Assembly.GetType("MphRead.Mods.Render."+name,true)!;
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        return (bool)(type.GetProperty("Enabled",flags)?.GetValue(null)??type.GetField("Enabled",flags)?.GetValue(null)
            ??throw new MissingMemberException(type.FullName,"Enabled"));
    }
    private void ObserveWorkloadBinaryIdentities()
    {
        if (_receiptBinaryIdentityObserved) return;
        _receiptBinaryIdentityObserved=true;
        try
        {
            _receiptClient=ObserveWorkloadBinary(typeof(Scene).Assembly);
            _receiptHarness=ObserveWorkloadBinary(typeof(CheckWindow).Assembly);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException or MissingMemberException)
        {_receiptFailure=error.Message;}
    }
    private void ObserveWorkloadStart()
    {
        _receiptStartTick=Scene.FrameCount;_receiptStartRoster=ObserveWorkloadRoster();
        _receiptStartRng=new(Scene.Random.Rng1,Scene.Random.Rng2);_receiptStartOffline=WorkloadOffline();
        try
        {
            if (_lateProvenanceControl) ObserveWorkloadBinaryIdentities();
            _receiptUnprofiled=!WorkloadProfilerEnabled("OpenGlFrameProfiler")&&!WorkloadProfilerEnabled("OpenGlSubmissionProfiler");
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or BadImageFormatException or MissingMemberException)
        {_receiptFailure=error.Message;}
    }
    private void ObserveWorkloadEnd()
    {
        long allocationStart=GC.GetTotalAllocatedBytes(false);
        _receiptEndTick=Scene.FrameCount;_receiptEndRoster=ObserveWorkloadRoster();
        _receiptEndRng=new(Scene.Random.Rng1,Scene.Random.Rng2);_receiptEndOffline=WorkloadOffline();
        _receiptEndAllocationOverhead=GC.GetTotalAllocatedBytes(false)-allocationStart;
    }
    private object ObservedWorkloadReceipt()=>new
    {
        schemaVersion=1,scope="Observed existing offline bot workload; no replay/input trace was executed; no build/source attestation.",
        eligibleNormalTiming=!_lateProvenanceControl&&_receiptFailure==null&&_receiptStartTick.HasValue&&_receiptEndTick.HasValue&&_receiptUnprofiled
            &&_shots==null&&!_windowCycle&&!_outlineCheck&&!_bombCheck&&!_impactCheck&&!_stableHud&&!_poseCheck&&!ResourceStressEnabled,
        binaryIdentityPhase=_lateProvenanceControl?"measurement-start-control":"before-warmup",
        failure=_receiptFailure,mode=_receiptStartOffline&&_receiptEndOffline?"offline":"not-verified-offline",phase="entry",
        inputExecution="frozen-device-snapshots-with-bot-ai",replayExecuted=false,
        seedAssignmentReadback=_receiptAssignedSeeds,measurementStartRng=_receiptStartRng,measurementEndRng=_receiptEndRng,
        measurementStartTick=_receiptStartTick,measurementEndTick=_receiptEndTick,
        startRoster=_receiptStartRoster,endRoster=_receiptEndRoster,
        clientBinary=_receiptClient,harnessBinary=_receiptHarness
    };
}
