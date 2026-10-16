using System;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

/// <summary>
/// Custom map vote preflight. The authoritative thread owns the vote; this
/// worker only prepares and verifies source-derived files and immutable identity.
/// It must NOT call RoomPrewarm.Begin: that is the active match's single-room
/// cache and replacing it during combat would change the live world's sources.
/// </summary>
internal static class MapVoteReadiness
{
    internal readonly record struct Result(bool Ready, string Reason)
    {
        public static Result Success => new(true, "");
        public static Result Refuse(string reason) => new(false, reason);
    }

    internal static Result VerifyOutputs(MapDefinition definition, MapOutputSet outputs)
    {
        if (!outputs.Complete)
        {
            string missing = outputs.Files.FirstOrDefault(file => !File.Exists(file)) ?? outputs.Collision;
            return Result.Refuse("Missing generated room output: " + Path.GetFileName(missing));
        }
        if (!MapBuildManifest.IsCurrent(definition, outputs))
            return Result.Refuse("Room build manifest is stale or an output failed SHA-256 verification.");
        return Result.Success;
    }

    internal static Result Prepare(string room, NetworkMapIdentity required, CancellationToken cancel)
    {
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (!required.IsCustom)
                return Result.Refuse("Only custom maps require this background preflight.");
            MapContentIdentity identity = required.Content(room);
            if (!CustomRooms.Installed.TryGet(room, out var installed)
                || !installed.Identity.Matches(identity))
                return Result.Refuse("Installed map identity no longer matches the vote.");
            if (!CustomRooms.Installed.HasExact(identity))
                return Result.Refuse("The exact immutable map package is unavailable or changed.");

            MapDefinition? definition = CustomRooms.Definitions.FirstOrDefault(d =>
                d.Name.Equals(room, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
                return Result.Refuse("No compiled room definition is registered for this map.");

            // Build under the existing cross-process generation lease; publishing
            // and per-file SHA-256 validation run on this worker, not the tick.
            CustomRooms.GenerateMissing(room);
            cancel.ThrowIfCancellationRequested();
            Result outputs = VerifyOutputs(definition, CustomRooms.OutputsFor(definition));
            if (!outputs.Ready)
                return outputs;
            if (!CustomRooms.Installed.HasExact(identity))
                return Result.Refuse("The map package changed while its room was being built.");
            cancel.ThrowIfCancellationRequested();
            return Result.Success;
        }
        catch (OperationCanceledException)
        {
            return Result.Refuse("Map preparation cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Result.Refuse("Map preparation failed: " + ex.Message);
        }
    }
}
