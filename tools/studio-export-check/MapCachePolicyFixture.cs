namespace MphRead.Mods.MapGen;

// Only MapDiskCache.Prune's hash-name predicate needs a dependency. Snapshot
// fixtures execute the production Acquire lease and immutable byte capture.
internal static class MapCommunityClient
{
    internal static bool ValidHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
