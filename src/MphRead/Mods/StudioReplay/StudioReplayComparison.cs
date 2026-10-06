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
        using var source = File.OpenRead(_playbackPath ?? LogicalPath);
        return new(1, ReplayStateHash.BuildId, Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), Snapshot(),
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
        destination = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(staging,JsonSerializer.Serialize(evidence,EvidenceJson),cancellation); cancellation.ThrowIfCancellationRequested(); File.Move(staging,destination,true); }
        finally { if(File.Exists(staging))File.Delete(staging); }
    }
    public static async Task<StudioReplayEvidence> LoadEvidenceAsync(string source, CancellationToken cancellation = default)
    {
        if(new FileInfo(source).Length > 8*1024*1024)throw new InvalidDataException("The comparison report exceeds its size budget.");
        var result = JsonSerializer.Deserialize<StudioReplayEvidence>(await File.ReadAllTextAsync(source,cancellation),EvidenceJson);
        if(result is not { Version:1 } || result.SourceHash.Length != 64 || result.SourceHash.Any(c=>!Uri.IsHexDigit(c))
            || result.Build.Length > 256 || result.Camera.View.Count != 16 || result.Camera.View.Any(v=>!float.IsFinite(v))
            || result.Projectiles.Count > 4096 || result.Animations.Count > 32768 || result.ResolvedShots.Count > 4096)
            throw new InvalidDataException("The comparison report is invalid.");
        return result;
    }
    private static readonly JsonSerializerOptions EvidenceJson = new() { IncludeFields=true, WriteIndented=true };
}
#endif
