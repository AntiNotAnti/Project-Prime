using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Values for a future recipient-owned baseline, not a native snapshot extension
// or a client health/resource command. An occupied Life0 viewer is representable.
internal readonly record struct CustomReplicaSlot(byte Slot,bool Occupied,ushort Generation,ushort Life,
    bool Spectating,bool HasWeapon,WeaponRuntimeId Selected,uint SettledSequence,uint CommittedSourceFrame,
    WeaponStateSnapshot State);
internal readonly record struct CustomReplicaFlight(CustomActionIdentity Action,CustomSourceCertificate Certificate,
    uint LaunchWorld,uint PaymentDeadline,Vector3 SpawnPosition,Vector3 Position,Vector3 Velocity,
    Vector3 Direction,float AgeSeconds,float RemainingSeconds);
internal readonly record struct CustomReplicaCapture(ushort MatchId,ulong AuthorityEpoch,WeaponDigest Manifest,
    uint World,int SlotCount,int FlightCount);

internal sealed partial class CustomAuthorityCombatAdapter
{
    internal const int ReplicaSlotCapacity=8,ReplicaFlightCapacity=64;
    private readonly CustomReplicaSlot[] _replicaSlots=new CustomReplicaSlot[ReplicaSlotCapacity];
    private readonly CustomReplicaFlight[] _replicaFlights=new CustomReplicaFlight[ReplicaFlightCapacity];
    private bool _capturingReplica;

    /// <summary>Authority-thread, all-or-nothing value copy. Call outside controls,
    /// emission, rewind and canonical damage. Never grants a life, advances time,
    /// consumes a source, reserves delivery, or lends mutable engine objects.
    /// Callers provide the full fixed bounds even for an empty baseline.</summary>
    internal bool TryCaptureReplica(Span<CustomReplicaSlot> slots,Span<CustomReplicaFlight> flights,
        out CustomReplicaCapture capture)
    {
        capture=default;
        if (!IsEligible || _capturingReplica || _inputPhase || _emittingEquip!=null || _contact!=null
            || NetUnlagged.ShotScopeActive || NetDamage.Replaying || NetDamage.ApplyingClaim || NetHitPrediction.Predicting
            || slots.Length<ReplicaSlotCapacity
            || flights.Length<ReplicaFlightCapacity) return false;
        _capturingReplica=true;
        try
        {
            uint world=Now;
            for (byte index=0; index<ReplicaSlotCapacity; index++)
            {
                bool occupied=NetSession.SlotOccupied[index];
                ushort generation=occupied ? NetPlayerLifecycle.Generation(index) : (ushort)0;
                ushort lifeId=occupied ? NetPlayerLifecycle.Get(index) : (ushort)0;
                bool spectator=occupied && NetSession.SlotSpectating[index];
                WeaponStateSnapshot state=default; WeaponRuntimeId selected=default;
                uint settled=0,committed=0; bool hasWeapon=false;
                if (occupied && index<_scene.Players.Items.Count)
                {
                    var player=_scene.Players.Items[index];
                    spectator|=Spectating(player);
                    if (!spectator && CurrentOwner(player,out var owner)
                        && _current[index] is { Journal.IsRetired:false } current && current.Owner==owner
                        && current.Journal.TryPreview(new(10),out state))
                    {
                        hasWeapon=true; selected=current.Journal.Selected;
                        settled=current.Journal.SettledSequence; committed=current.Journal.CommittedSourceFrame;
                    }
                }
                _replicaSlots[index]=new(index,occupied,generation,lifeId,spectator,hasWeapon,
                    selected,settled,committed,state);
            }
            int count=0;
            foreach (var slot in _slots)
            {
                if (slot==null) continue;
                foreach (var beam in slot.Equip.Beams)
                {
                    // Payment retention alone is not a physical flight. Contact
                    // effects can retain a positive lifespan after being paid.
                    if (beam.Lifespan<=0 || beam.Flags.TestFlag(BeamFlags.Collided)
                        || !OwnedBeamShape(beam,out var source,out var life)
                        || !life.Payments.TryReadCommittedFlight(source,world,out uint began,out uint deadline)) continue;
                    if (count==ReplicaFlightCapacity || source.Action is not { } action
                        || source.Certificate is not { } certificate || !action.IsValid
                        || !Finite(beam.SpawnPosition) || !Finite(beam.Position) || !Finite(beam.Velocity)
                        || !Finite(beam.Direction) || !float.IsFinite(beam.Age) || beam.Age<0
                        || !float.IsFinite(beam.Lifespan)) return false;
                    _replicaFlights[count++]=new(action,certificate,began,deadline,beam.SpawnPosition,
                        beam.Position,beam.Velocity,beam.Direction,beam.Age,beam.Lifespan);
                }
            }
            _replicaSlots.AsSpan().CopyTo(slots);
            _replicaFlights.AsSpan(0,count).CopyTo(flights);
            capture=new(_match,_epoch,Manifest.Digest,world,ReplicaSlotCapacity,count);
            return true;
        }
        finally
        {
            // Scratch storage must not retain obsolete definition references.
            Array.Clear(_replicaSlots); Array.Clear(_replicaFlights); _capturingReplica=false;
        }
    }
    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
