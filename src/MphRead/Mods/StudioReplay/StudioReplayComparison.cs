#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.StudioReplay;

public sealed record StudioReplayProjectile(int Index, uint ShotId, uint LaunchFrame, int OwnerSlot, int Weapon,
    Vector3 Position, Vector3 Velocity, Vector3 Direction, float Age, float Lifespan, uint Flags);
public sealed record StudioReplayAnimation(int EntityIndex, string EntityType, int ModelIndex,
    int Animation0, int Animation1, int Frame0, int Frame1, int Flags0, int Flags1);
public sealed record StudioReplayCameraSnapshot(Vector3 Position, IReadOnlyList<float> View, StudioReplayView Descriptor);
public sealed record StudioReplayEvidence(int Version, string Build, string SourceHash, StudioReplayWorldSnapshot World,
    IReadOnlyList<StudioReplayProjectile> Projectiles, IReadOnlyList<StudioReplayAnimation> Animations,
    StudioReplayCameraSnapshot Camera, IReadOnlyList<StudioReplayCombat> ResolvedShots,
    StudioReplayPerformance Checkpoints, int CheckpointCount, long CheckpointBytes, string? RenderCaptureHash);
public sealed record StudioReplayEvidenceComparison(StudioReplayComparison World, bool SameSource, bool ProjectilesEqual,
    bool AnimationsEqual, bool CameraEqual, bool ResolvedShotsEqual, bool? RenderCapturesEqual, string BuildA, string BuildB);

public sealed partial class StudioReplayPlayer
{
    public StudioReplayEvidence Evidence(StudioReplayView view, StudioReplayCapture? capture = null)
    {
        RequireOwner(); using var scope = _resources.Enter();
        if (_player is not { CanPresent: true } player) throw new InvalidOperationException("Wait for replay preparation or seeking to complete.");
        var scene = player.Current.Scene; var projectiles = new List<StudioReplayProjectile>(); var animations = new List<StudioReplayAnimation>();
        int entityIndex = 0;
        foreach (var entity in scene.Entities)
        {
            if (entity is BeamProjectileEntity beam && beam.Active)
                projectiles.Add(new(projectiles.Count, beam.ModShotId, beam.ModLaunchFrame, beam.Owner is PlayerEntity owner ? owner.SlotIndex : -1,
                    (int)beam.BeamKind, Public(beam.Position), Public(beam.Velocity), Public(beam.Direction), beam.Age, beam.Lifespan, (uint)beam.Flags));
            for (int index = 0; index < entity.ReplayModels.Count; index++)
            {
                var state = entity.ReplayModels[index].AnimInfo;
                animations.Add(new(entityIndex, entity.Type.ToString(), index, state.Index[0], state.Index[1], state.Frame[0], state.Frame[1], (int)state.Flags[0], (int)state.Flags[1]));
            }
            entityIndex++;
        }
        var matrix = scene.ViewMatrix;
        float[] transform = [matrix.M11,matrix.M12,matrix.M13,matrix.M14,matrix.M21,matrix.M22,matrix.M23,matrix.M24,
            matrix.M31,matrix.M32,matrix.M33,matrix.M34,matrix.M41,matrix.M42,matrix.M43,matrix.M44];
        string sourceHash = _playbackContentHash ?? throw new InvalidOperationException("The replay's immutable source identity is unavailable.");
        return new(1, ReplayStateHash.BuildId, sourceHash, Snapshot(),
            projectiles.ToArray(), animations.ToArray(), new(Public(scene.CameraPosition), transform, view), CombatAt(player.Transport.CurrentFrame),
            Performance, player.CheckpointCount, player.CheckpointBytes,
            capture == null ? null : Convert.ToHexString(SHA256.HashData(capture.Rgba)).ToLowerInvariant());
    }
    public static StudioReplayEvidenceComparison CompareEvidence(StudioReplayEvidence left, StudioReplayEvidence right)
    {
        bool Equal<T>(T first, T second) => JsonSerializer.Serialize(first, EvidenceJson) == JsonSerializer.Serialize(second, EvidenceJson);
        return new(Compare(left.World,right.World), left.SourceHash == right.SourceHash, Equal(left.Projectiles,right.Projectiles),
            Equal(left.Animations,right.Animations), Equal(left.Camera,right.Camera), Equal(left.ResolvedShots,right.ResolvedShots),
            left.RenderCaptureHash == null || right.RenderCaptureHash == null ? null : left.RenderCaptureHash == right.RenderCaptureHash,
            left.Build,right.Build);
    }
    public static async Task SaveEvidenceAsync(StudioReplayEvidence evidence, string destination, CancellationToken cancellation = default)
    {
        ValidateEvidence(evidence);
        destination = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(staging,JsonSerializer.Serialize(evidence,EvidenceJson),cancellation); cancellation.ThrowIfCancellationRequested(); File.Move(staging,destination,true); }
        finally { if(File.Exists(staging))File.Delete(staging); }
    }
    public static async Task<StudioReplayEvidence> LoadEvidenceAsync(string source, CancellationToken cancellation = default)
    {
        if(new FileInfo(source).Length > 8*1024*1024)throw new InvalidDataException("The comparison report exceeds its size budget.");
        StudioReplayEvidence? result;
        try { result = JsonSerializer.Deserialize<StudioReplayEvidence>(await File.ReadAllTextAsync(source,cancellation),EvidenceJson); }
        catch (JsonException ex) { throw new InvalidDataException("The comparison report is invalid.", ex); }
        ValidateEvidence(result);
        return result!;
    }
    private static void ValidateEvidence(StudioReplayEvidence? evidence)
    {
        static bool Hash(string? value, int? size = null) => value != null && value.Length is > 0 and <= 128
            && (!size.HasValue || value.Length == size) && value.All(Uri.IsHexDigit);
        static bool Vector(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        static bool Rotation(Quaternion value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);
        static bool Timing(double? value) => !value.HasValue || double.IsFinite(value.Value) && value.Value >= 0;
        static bool Combat(StudioReplayCombat? shot) => shot != null && shot.Shooter is >= 0 and < 8
            && shot.Target is >= -1 and < 8 && shot.Explanation is { Length: <= 4096 }
            && Vector(shot.Muzzle) && Vector(shot.Aim) && Vector(shot.Direction) && Vector(shot.Impact)
            && (shot.RewindTarget == null || Vector(shot.RewindTarget.Value))
            && (shot.DamageDirection == null || Vector(shot.DamageDirection.Value))
            && (!shot.RewindFrame.HasValue || double.IsFinite(shot.RewindFrame.Value) && shot.RewindFrame.Value >= 0)
            && (!shot.SettlementSeconds.HasValue || float.IsFinite(shot.SettlementSeconds.Value) && shot.SettlementSeconds.Value >= 0);
        bool valid = evidence is { Version: 1, Build: { Length: > 0 and <= 256 }, World: not null,
                Camera: { View: not null, Descriptor: not null }, Checkpoints: not null,
                Projectiles: not null, Animations: not null, ResolvedShots: not null }
            && Hash(evidence.SourceHash, 64) && Hash(evidence.World.GameplayHash) && Hash(evidence.World.PresentationHash)
            && Hash(evidence.World.FullGraphHash, 64) && (evidence.RenderCaptureHash == null || Hash(evidence.RenderCaptureHash, 64))
            && evidence.World.Players != null && evidence.World.Players.Count <= 8
            && evidence.World.Players.All(p => p != null && p.Slot is >= 0 and < 8 && Vector(p.Position) && Vector(p.Facing))
            && evidence.World.Players.Select(p => p.Slot).Distinct().Count() == evidence.World.Players.Count
            && Vector(evidence.Camera.Position) && evidence.Camera.View.Count == 16 && evidence.Camera.View.All(float.IsFinite)
            && evidence.Camera.Descriptor.Width is > 0 and <= 16384 && evidence.Camera.Descriptor.Height is > 0 and <= 16384
            && Enum.IsDefined(evidence.Camera.Descriptor.Camera) && evidence.Camera.Descriptor.PlayerSlot is >= 0 and < 8
            && Vector(evidence.Camera.Descriptor.Position) && Rotation(evidence.Camera.Descriptor.Rotation)
            && float.IsFinite(evidence.Camera.Descriptor.Fov) && evidence.Camera.Descriptor.Fov is >= 1 and <= 175
            && (!evidence.Camera.Descriptor.PresentationFrame.HasValue || double.IsFinite(evidence.Camera.Descriptor.PresentationFrame.Value)
                && evidence.Camera.Descriptor.PresentationFrame.Value >= 0)
            && (!evidence.Camera.Descriptor.PresentationAlpha.HasValue || float.IsFinite(evidence.Camera.Descriptor.PresentationAlpha.Value)
                && evidence.Camera.Descriptor.PresentationAlpha.Value is >= 0 and <= 1)
            && (evidence.Camera.Descriptor.Combat == null || Combat(evidence.Camera.Descriptor.Combat))
            && evidence.Projectiles.Count <= 4096 && evidence.Projectiles.All(p => p != null && p.Index >= 0
                && p.OwnerSlot is >= -1 and < 8 && Vector(p.Position) && Vector(p.Velocity) && Vector(p.Direction)
                && float.IsFinite(p.Age) && float.IsFinite(p.Lifespan))
            && evidence.Animations.Count <= 32768 && evidence.Animations.All(a => a != null
                && a.EntityIndex >= 0 && a.ModelIndex >= 0 && a.EntityType is { Length: > 0 and <= 128 })
            && evidence.ResolvedShots.Count <= 4096 && evidence.ResolvedShots.All(Combat)
            && evidence.CheckpointCount >= 0 && evidence.CheckpointBytes >= 0
            && evidence.Checkpoints.CheckpointSource is { Length: > 0 and <= 256 }
            && evidence.Checkpoints.SeekSimulationSteps >= 0 && evidence.Checkpoints.RejectedCheckpoints >= 0
            && Timing(evidence.Checkpoints.SeekMilliseconds) && Timing(evidence.Checkpoints.AdvanceMilliseconds)
            && Timing(evidence.Checkpoints.RenderMilliseconds) && Timing(evidence.Checkpoints.CheckpointCaptureMilliseconds);
        if (!valid) throw new InvalidDataException("The comparison report is invalid.");
    }
    private static readonly JsonSerializerOptions EvidenceJson = new() { IncludeFields=true, WriteIndented=true };
}
#endif
