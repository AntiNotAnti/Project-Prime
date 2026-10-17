using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Shared replay launch preflight for both the existing Theatre workspace and
/// the optional native RmlUi archive. Never seek an entire replay on the UI
/// or graphics owner thread.
/// </summary>
internal static class ReplayLaunchValidator
{
    internal readonly record struct Result(ReplayOpenResult Code, string? Error)
    {
        internal bool Ok => Code == ReplayOpenResult.Success;
    }

    internal static Result Probe(string source)
    {
        using DemoReader? reader = DemoReader.Open(source,
            out ReplayOpenResult result, metadataOnly: true);
        if (reader == null)
            return new(result, $"Cannot open replay: {result}.");

        if (!ReplayIdentityCompatibility.Supports(reader.ProtocolVersion))
        {
            return new(ReplayOpenResult.ProtocolMismatch,
                $"This replay uses network protocol {reader.ProtocolVersion}. "
                + $"This build can replay archived protocols "
                + $"{ReplayIdentityCompatibility.OldestReplayProtocol}-{NetConfig.ProtocolVersion}.");
        }

        if (reader.Metadata is ReplayMetadata metadata)
        {
            result = ReplayMapIdentity.Validate(metadata);
            if (result == ReplayOpenResult.MapHashMismatch
                && metadata.CustomMapIdentity == null
                && reader.ProtocolVersion < NetConfig.ProtocolVersion)
                result = ReplayOpenResult.Success;
            if (result != ReplayOpenResult.Success)
                return new(result, $"Cannot load replay map: {result}.");
        }

        return new(ReplayOpenResult.Success, null);
    }
}
