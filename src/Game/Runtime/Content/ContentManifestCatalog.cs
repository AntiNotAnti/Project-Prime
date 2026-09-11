using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Runtime.Content;

/// <summary>
/// Strict JSON entry points for the two known manifest schemas.  The parser
/// checks duplicate properties before System.Text.Json deserialization because
/// the default serializer otherwise keeps the last duplicate value.
/// </summary>
public static class ContentManifestJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static GameplayContentManifest ReadGameplay(string path)
        => ContentManifestValidator.ValidateGameplay(ReadManifest<GameplayContentManifest>(path, ContentManifestFileNames.Gameplay));

    public static OptionalPresentationManifest ReadOptionalPresentation(string path)
        => ContentManifestValidator.ValidateOptional(ReadManifest<OptionalPresentationManifest>(path, ContentManifestFileNames.OptionalPresentation));

    public static GameplayContentManifest ParseGameplay(ReadOnlySpan<byte> utf8Json)
        => ContentManifestValidator.ValidateGameplay(Parse<GameplayContentManifest>(utf8Json, "gameplay"));

    public static OptionalPresentationManifest ParseOptionalPresentation(ReadOnlySpan<byte> utf8Json)
        => ContentManifestValidator.ValidateOptional(Parse<OptionalPresentationManifest>(utf8Json, "optional presentation"));

    public static byte[] Serialize(GameplayContentManifest manifest)
    {
        ContentManifestValidator.ValidateGameplay(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static byte[] Serialize(OptionalPresentationManifest manifest)
    {
        ContentManifestValidator.ValidateOptional(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    private static T Parse<T>(ReadOnlySpan<byte> utf8Json, string label)
    {
        if (utf8Json.Length is 0 or > ContentManifestLimits.MaximumManifestBytes)
        {
            throw new ContentManifestValidationException(
                $"The {label} manifest exceeds the {ContentManifestLimits.MaximumManifestBytes} byte limit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = ContentManifestLimits.MaximumJsonDepth
            });
            RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(document.RootElement.GetRawText(), Options)
                ?? throw new JsonException("The manifest is null.");
        }
        catch (ContentManifestValidationException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new ContentManifestValidationException($"Invalid {label} manifest JSON: {error.Message}", error);
        }
        catch (NotSupportedException error)
        {
            throw new ContentManifestValidationException($"Unsupported {label} manifest JSON: {error.Message}", error);
        }
    }

    private static T ReadManifest<T>(string path, string expectedName)
    {
        if (String.IsNullOrWhiteSpace(path))
            throw new ContentManifestValidationException("A manifest path is required.");

        string fullPath = Path.GetFullPath(path);
        if (!String.Equals(Path.GetFileName(fullPath), expectedName, StringComparison.Ordinal))
            throw new ContentManifestValidationException($"Expected {expectedName}.");
        if (!File.Exists(fullPath) || File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            throw new ContentManifestValidationException("Manifest path is missing or is a symbolic link.");

        long length = new FileInfo(fullPath).Length;
        if (length is <= 0 or > ContentManifestLimits.MaximumManifestBytes)
            throw new ContentManifestValidationException("Manifest size is outside the permitted bounds.");
        try
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            return typeof(T) == typeof(GameplayContentManifest)
                ? (T)(object)ParseGameplay(bytes)
                : (T)(object)ParseOptionalPresentation(bytes);
        }
        catch (ContentManifestValidationException)
        {
            throw;
        }
        catch (IOException error)
        {
            throw new ContentManifestValidationException("Unable to read the local manifest.", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new ContentManifestValidationException("Unable to read the local manifest.", error);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = ContentManifestLimits.MaximumJsonDepth,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ContentManifestValidationException($"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }
}

/// <summary>Pure schema and identity validation for local content manifests.</summary>
public static class ContentManifestValidator
{
    private static readonly HashSet<string> AnnouncerEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "three", "two", "one", "go", "overtime", "matchPoint", "victory", "defeat",
        "firstHunt", "doubleKill", "tripleKill", "interceptor", "defender", "primeSlayer",
        "capture", "assist"
    };

    private static readonly Dictionary<OptionalPresentationKind, HashSet<string>> AllowedExtensions = new()
    {
        [OptionalPresentationKind.Announcer] = Extensions(".wav", ".ogg", ".mp3"),
        [OptionalPresentationKind.Music] = Extensions(".wav", ".ogg", ".mp3", ".flac"),
        [OptionalPresentationKind.HudTheme] = Extensions(".json", ".png", ".jpg", ".jpeg", ".webp"),
        [OptionalPresentationKind.CosmeticEffects] = Extensions(".json", ".png", ".jpg", ".jpeg", ".webp")
    };

    public static GameplayContentManifest ValidateGameplay(GameplayContentManifest manifest)
    {
        if (manifest is null) throw new ContentManifestValidationException("Gameplay manifest is required.");
        if (manifest.Format != GameplayContentManifest.CurrentFormat)
            throw new ContentManifestValidationException("Unsupported gameplay manifest format.");
        ValidateIdentity(new(manifest.StableId, manifest.Version, manifest.ContentHash));
        ValidateGameplayIdentity(manifest.Gameplay);
        ContentFileEntry[] files = ValidateFiles(manifest.Files, requireFiles: true, allowedExtensions: null,
            allowManifestNames: false);
        if (files.Length > ContentManifestLimits.MaximumFiles)
            throw new ContentManifestValidationException("Gameplay manifest contains too many files.");
        return manifest with { Files = files };
    }

    public static OptionalPresentationManifest ValidateOptional(OptionalPresentationManifest manifest)
    {
        if (manifest is null) throw new ContentManifestValidationException("Optional presentation manifest is required.");
        if (manifest.Format != OptionalPresentationManifest.CurrentFormat)
            throw new ContentManifestValidationException("Unsupported optional presentation manifest format.");
        if (!Enum.IsDefined(manifest.Kind))
            throw new ContentManifestValidationException("Unknown optional presentation kind.");
        ValidateIdentity(new(manifest.StableId, manifest.Version, manifest.ContentHash));
        string? displayName = ValidateOptionalDisplayName(manifest.DisplayName);
        ContentFileEntry[] files = ValidateFiles(manifest.Files, requireFiles: true, AllowedExtensions[manifest.Kind],
            allowManifestNames: false);
        OptionalPresentationEvent[] events = manifest.Events ?? throw new ContentManifestValidationException("Events are required.");
        if (events.Length > ContentManifestLimits.MaximumManifestItems)
            throw new ContentManifestValidationException("Optional presentation manifest contains too many events.");

        var eventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesByPath = files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach (OptionalPresentationEvent mapping in events)
        {
            if (mapping is null || String.IsNullOrWhiteSpace(mapping.Key))
                throw new ContentManifestValidationException("Optional presentation event mappings require a key.");
            string key = NormalizeAnnouncerEventKey(mapping.Key);
            if (manifest.Kind != OptionalPresentationKind.Announcer)
                throw new ContentManifestValidationException("Only announcer packs may declare event mappings.");
            if (!eventKeys.Add(key))
                throw new ContentManifestValidationException("Duplicate optional presentation event key: " + mapping.Key);
            if (!filesByPath.ContainsKey(mapping.Path))
                throw new ContentManifestValidationException("Event mapping references an undeclared file: " + mapping.Path);
        }

        if (files.Length + events.Length > ContentManifestLimits.MaximumManifestItems)
            throw new ContentManifestValidationException("Optional presentation manifest contains too many items.");
        return manifest with { Files = files, Events = events, DisplayName = displayName };
    }

    public static bool IsIdentity(ContentPackIdentity identity)
    {
        try
        {
            ValidateIdentity(identity);
            return true;
        }
        catch (ContentManifestValidationException)
        {
            return false;
        }
    }

    public static void ValidateIdentity(ContentPackIdentity identity)
    {
        ValidateStableToken(identity.StableId, ContentManifestLimits.MaximumStableIdLength, "stable id", allowSlash: false);
        ValidateStableToken(identity.Version, ContentManifestLimits.MaximumVersionLength, "version", allowSlash: false);
        if (identity.ContentHash is not { Length: 64 } || identity.ContentHash.Any(c => !Char.IsAsciiHexDigit(c)))
            throw new ContentManifestValidationException("Content hash must be a 64-character SHA-256 hex string.");
    }

    internal static string NormalizeAnnouncerEventKey(string key)
    {
        if (!AnnouncerEventKeys.Contains(key))
            throw new ContentManifestValidationException("Unknown announcer event key: " + key);
        return AnnouncerEventKeys.First(known => String.Equals(known, key, StringComparison.OrdinalIgnoreCase));
    }

    internal static ContentFileEntry[] ValidateFiles(ContentFileEntry[]? files, bool requireFiles,
        HashSet<string>? allowedExtensions, bool allowManifestNames)
    {
        if (files is null || files.Length == 0 && requireFiles)
            throw new ContentManifestValidationException("Manifest must declare at least one file.");
        if (files is null || files.Length > ContentManifestLimits.MaximumFiles)
            throw new ContentManifestValidationException("Manifest contains too many files.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ContentFileEntry file in files)
        {
            if (file is null || !IsSafeRelativePath(file.Path))
                throw new ContentManifestValidationException("Manifest contains an unsafe relative file path.");
            if (!paths.Add(file.Path))
                throw new ContentManifestValidationException("Manifest contains duplicate file paths: " + file.Path);
            if (!allowManifestNames && IsManifestFileName(file.Path))
                throw new ContentManifestValidationException("A manifest cannot declare another manifest file.");
            if (file.Bytes is < 0 or > ContentManifestLimits.MaximumIndividualFileBytes)
                throw new ContentManifestValidationException("Manifest file size is outside the permitted bounds: " + file.Path);
            if ((total = checked(total + file.Bytes)) > ContentManifestLimits.MaximumTotalFileBytes)
                throw new ContentManifestValidationException("Manifest files exceed the total byte limit.");
            if (file.Sha256 is not { Length: 64 } || file.Sha256.Any(c => !Char.IsAsciiHexDigit(c)))
                throw new ContentManifestValidationException("Manifest file hash is not SHA-256: " + file.Path);
            if (allowedExtensions != null
                && !allowedExtensions.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))
            {
                throw new ContentManifestValidationException("File extension is not allowed for this presentation kind: " + file.Path);
            }
        }
        if (files.Length + 1 > ContentManifestLimits.MaximumManifestItems)
            throw new ContentManifestValidationException("Manifest contains too many items.");
        return files.ToArray();
    }

    internal static void ValidateGameplayIdentity(GameplayContentIdentity? identity)
    {
        if (identity is null) throw new ContentManifestValidationException("Gameplay identity is required.");
        ValidateIdentityToken(identity.Map, "map identity");
        ValidateIdentityToken(identity.Collision, "collision identity");
        ValidateIdentityToken(identity.Entities, "entity identity");
        ValidateIdentityToken(identity.GameplayData, "gameplay-data identity");
    }

    internal static bool IsSafeRelativePath(string? relative)
    {
        if (relative is null || relative.Length is 0 or > ContentManifestLimits.MaximumPathLength
            || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.Contains('?') || relative.Contains('*') || relative.Any(Char.IsControl))
            return false;
        string[] parts = relative.Split('/');
        return parts.All(part => part.Length > 0 && part is not "." and not "..");
    }

    private static void ValidateIdentityToken(string value, string name)
        => ValidateStableToken(value, ContentManifestLimits.MaximumIdentityLength, name, allowSlash: false);

    private static void ValidateStableToken(string? value, int maximum, string name, bool allowSlash)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(Char.IsControl)
            || value.Any(c => Char.IsWhiteSpace(c) && c != ' ' && c != '\t')
            || !allowSlash && value.Any(c => c is '/' or '\\' or ':' or '?' or '*'))
            throw new ContentManifestValidationException($"Invalid {name}.");
    }

    private static string? ValidateOptionalDisplayName(string? displayName)
    {
        if (displayName == null)
        {
            return null;
        }
        // Display names are presentation text, not stable identities. Keep
        // ordinary spaces for readable labels, but reject controls and other
        // Unicode whitespace so a manifest cannot inject line breaks or
        // non-space separators into the launcher.
        if (String.IsNullOrWhiteSpace(displayName)
            || displayName.Length > ContentManifestLimits.MaximumDisplayNameLength
            || displayName.Any(Char.IsControl)
            || displayName.Any(c => Char.IsWhiteSpace(c) && c != ' '))
        {
            throw new ContentManifestValidationException("Invalid optional presentation display name.");
        }
        return displayName.Trim();
    }

    private static bool IsManifestFileName(string path)
        => String.Equals(path, ContentManifestFileNames.Gameplay, StringComparison.OrdinalIgnoreCase)
        || String.Equals(path, ContentManifestFileNames.OptionalPresentation, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Extensions(params string[] extensions)
        => new(extensions, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Local-only filesystem integrity checks.  Directory traversal is explicit so
/// a reparse point is rejected before it could be followed.
/// </summary>
internal static class InstalledContentFiles
{
    public static void ValidatePackDirectory(string root, string manifestName, IEnumerable<ContentFileEntry> declarations,
        bool presentation)
    {
        string canonicalRoot = CanonicalRoot(root);
        string manifestPath = SafePath(canonicalRoot, manifestName);
        if (!File.Exists(manifestPath) || IsReparse(manifestPath))
            throw new ContentManifestValidationException("Content manifest is missing or is a symbolic link.");

        ContentFileEntry[] files = declarations.ToArray();
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifestName };
        long total = 0;
        foreach (ContentFileEntry declaration in files)
        {
            string path = SafePath(canonicalRoot, declaration.Path);
            if (!expected.Add(declaration.Path))
                throw new ContentManifestValidationException("Duplicate or manifest file path: " + declaration.Path);
            if (!File.Exists(path) || IsReparse(path) || Directory.Exists(path))
                throw new ContentManifestValidationException("Declared content file is missing or unsafe: " + declaration.Path);
            var info = new FileInfo(path);
            if (info.Length != declaration.Bytes || info.Length > ContentManifestLimits.MaximumIndividualFileBytes)
                throw new ContentManifestValidationException("Declared content file size changed: " + declaration.Path);
            total = checked(total + info.Length);
            if (total > ContentManifestLimits.MaximumTotalFileBytes)
                throw new ContentManifestValidationException("Installed content exceeds the total byte limit.");
            string hash;
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 64 * 1024, options: FileOptions.SequentialScan);
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            catch (IOException error)
            {
                throw new ContentManifestValidationException("Unable to read content file: " + declaration.Path, error);
            }
            catch (UnauthorizedAccessException error)
            {
                throw new ContentManifestValidationException("Unable to read content file: " + declaration.Path, error);
            }
            if (!String.Equals(hash, declaration.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ContentManifestValidationException("Content file hash mismatch: " + declaration.Path);
        }

        int entries = 0;
        foreach (string path in EnumerateWithoutFollowingLinks(canonicalRoot, ref entries))
        {
            string relative = Path.GetRelativePath(canonicalRoot, path).Replace('\\', '/');
            if (!expected.Contains(relative))
                throw new ContentManifestValidationException("Unlisted file in content pack: " + relative);
        }

        // The argument is intentionally retained in the method contract to
        // make the two call sites self-documenting; extension policy is done
        // by ContentManifestValidator before this integrity pass.
        _ = presentation;
    }

    public static string CanonicalRoot(string root)
    {
        if (String.IsNullOrWhiteSpace(root))
            throw new ContentManifestValidationException("Content root is required.");
        string full = Path.GetFullPath(root);
        if (!Directory.Exists(full) || IsReparse(full))
            throw new ContentManifestValidationException("Content root is missing or is a symbolic link.");
        return full;
    }

    public static string SafePath(string root, string relative)
    {
        if (!ContentManifestValidator.IsSafeRelativePath(relative))
            throw new ContentManifestValidationException("Unsafe relative content path: " + relative);
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, comparison) || !ContentManifestValidator.IsSafeRelativePath(
                Path.GetRelativePath(root, full).Replace('\\', '/')))
            throw new ContentManifestValidationException("Content path escapes its canonical root: " + relative);

        string current = root;
        foreach (string part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && IsReparse(current))
                throw new ContentManifestValidationException("Content path contains a symbolic link: " + relative);
        }
        return full;
    }

    private static IReadOnlyList<string> EnumerateWithoutFollowingLinks(string root, ref int entries)
    {
        var pending = new Stack<string>();
        var files = new List<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (++entries > ContentManifestLimits.MaximumCatalogEntries)
                    throw new ContentManifestValidationException("Content pack contains too many filesystem entries.");
                if (IsReparse(entry))
                    throw new ContentManifestValidationException("Content pack cannot contain symbolic links or reparse points.");
                if (Directory.Exists(entry)) pending.Push(entry);
                else if (File.Exists(entry)) files.Add(entry);
                else throw new ContentManifestValidationException("Unsupported content filesystem entry: " + entry);
            }
        }
        return files;
    }

    private static bool IsReparse(string path)
        => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}

/// <summary>
/// Bounded catalog of local required and optional packs.  Discovery performs
/// no network or downloader work; invalid optional packs become issues and are
/// eligible for built-in fallback while required matching remains fail-closed.
/// </summary>
public sealed class InstalledContentCatalog
{
    private readonly List<InstalledGameplayPack> _gameplay;
    private readonly List<InstalledOptionalPresentationPack> _optional;

    public IReadOnlyList<InstalledGameplayPack> GameplayPacks => _gameplay;
    public IReadOnlyList<InstalledOptionalPresentationPack> OptionalPresentationPacks => _optional;
    public IReadOnlyList<ContentDiscoveryIssue> Issues { get; }

    public InstalledContentCatalog(
        IEnumerable<InstalledGameplayPack>? gameplayPacks = null,
        IEnumerable<InstalledOptionalPresentationPack>? optionalPacks = null,
        IEnumerable<ContentDiscoveryIssue>? issues = null)
    {
        _gameplay = gameplayPacks?.ToList() ?? new List<InstalledGameplayPack>();
        _optional = optionalPacks?.Where(pack => pack is not null && !pack.IsBuiltIn).ToList()
            ?? new List<InstalledOptionalPresentationPack>();
        Issues = (issues ?? Array.Empty<ContentDiscoveryIssue>()).ToArray();
        ValidateCatalogBounds();
    }

    public static InstalledContentCatalog Discover(string root)
    {
        string canonicalRoot = InstalledContentFiles.CanonicalRoot(root);
        var candidates = new List<string>();
        int entries = 0;
        bool rootHasManifest = File.Exists(Path.Combine(canonicalRoot, ContentManifestFileNames.Gameplay))
            || File.Exists(Path.Combine(canonicalRoot, ContentManifestFileNames.OptionalPresentation));
        if (rootHasManifest) candidates.Add(canonicalRoot);

        foreach (string entry in Directory.EnumerateFileSystemEntries(canonicalRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (++entries > ContentManifestLimits.MaximumCatalogEntries)
                throw new ContentManifestValidationException("Installed content catalog has too many entries.");
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                throw new ContentManifestValidationException("Installed content catalog cannot contain symbolic links.");
            if (Directory.Exists(entry)) candidates.Add(entry);
        }

        var gameplay = new List<InstalledGameplayPack>();
        var optional = new List<InstalledOptionalPresentationPack>();
        var issues = new List<ContentDiscoveryIssue>();
        var gameplayIds = new HashSet<string>(StringComparer.Ordinal);
        var optionalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string candidate in candidates.Order(StringComparer.Ordinal))
        {
            string gameplayPath = Path.Combine(candidate, ContentManifestFileNames.Gameplay);
            string optionalPath = Path.Combine(candidate, ContentManifestFileNames.OptionalPresentation);
            bool hasGameplay = File.Exists(gameplayPath);
            bool hasOptional = File.Exists(optionalPath);
            if (hasGameplay && hasOptional)
            {
                issues.Add(new(candidate, "A pack directory cannot contain both gameplay and optional manifests.", true));
                continue;
            }
            if (!hasGameplay && !hasOptional) continue;

            try
            {
                if (hasGameplay)
                {
                    GameplayContentManifest manifest = ContentManifestJson.ReadGameplay(gameplayPath);
                    InstalledContentFiles.ValidatePackDirectory(candidate, ContentManifestFileNames.Gameplay,
                        manifest.Files, presentation: false);
                    string key = IdentityKey(manifest.PackIdentity);
                    if (!gameplayIds.Add(key)) throw new ContentManifestValidationException("Duplicate gameplay pack identity.");
                    gameplay.Add(new(manifest, InstalledContentFiles.CanonicalRoot(candidate)));
                }
                else
                {
                    OptionalPresentationManifest manifest = ContentManifestJson.ReadOptionalPresentation(optionalPath);
                    InstalledContentFiles.ValidatePackDirectory(candidate, ContentManifestFileNames.OptionalPresentation,
                        manifest.Files, presentation: true);
                    string key = OptionalKey(manifest.Kind, manifest.PackIdentity);
                    if (!optionalIds.Add(key)) throw new ContentManifestValidationException("Duplicate optional pack identity.");
                    optional.Add(new(manifest, InstalledContentFiles.CanonicalRoot(candidate)));
                }
            }
            catch (ContentManifestValidationException error)
            {
                issues.Add(new(candidate, error.Message, hasGameplay));
            }
            catch (IOException error)
            {
                issues.Add(new(candidate, error.Message, hasGameplay));
            }
            catch (UnauthorizedAccessException error)
            {
                issues.Add(new(candidate, error.Message, hasGameplay));
            }
        }

        return new InstalledContentCatalog(gameplay, optional, issues);
    }

    public bool TryMatchGameplay(ContentPackIdentity expected, out InstalledGameplayPack? pack)
    {
        expected.Validate();
        pack = _gameplay.FirstOrDefault(candidate => SameIdentity(candidate.Identity, expected));
        return pack is not null;
    }

    public InstalledGameplayPack RequireGameplay(ContentPackIdentity expected)
    {
        if (TryMatchGameplay(expected, out InstalledGameplayPack? pack)) return pack!;
        throw new RequiredContentMismatchException(
            $"Required gameplay content mismatch for {expected.StableId}@{expected.Version}#{expected.ContentHash}.");
    }

    public OptionalPresentationSelection SelectOptional(OptionalPresentationKind kind,
        ContentPackIdentity? requested)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!requested.HasValue)
            return OptionalPresentationSelection.BuiltIn(kind, OptionalPresentationFallbackReason.NotRequested);
        requested.Value.Validate();
        InstalledOptionalPresentationPack? pack = _optional.FirstOrDefault(candidate =>
            candidate.Manifest.Kind == kind && SameIdentity(candidate.Identity, requested.Value));
        return pack is null
            ? OptionalPresentationSelection.BuiltIn(kind, OptionalPresentationFallbackReason.Missing)
            : new(kind, pack, false, OptionalPresentationFallbackReason.None);
    }

    private void ValidateCatalogBounds()
    {
        if (_gameplay.Count + _optional.Count > ContentManifestLimits.MaximumInstalledPacks)
            throw new ContentManifestValidationException("Installed content catalog contains too many packs.");
        var gameplayIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < _gameplay.Count; index++)
        {
            InstalledGameplayPack pack = _gameplay[index];
            if (pack is null || String.IsNullOrWhiteSpace(pack.RootDirectory))
                throw new ContentManifestValidationException("Invalid installed gameplay pack.");
            pack = pack with { Manifest = ContentManifestValidator.ValidateGameplay(pack.Manifest) };
            _gameplay[index] = pack;
            if (!gameplayIds.Add(IdentityKey(pack.Identity)))
                throw new ContentManifestValidationException("Duplicate installed gameplay pack identity.");
        }
        var optionalIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < _optional.Count; index++)
        {
            InstalledOptionalPresentationPack pack = _optional[index];
            if (pack is null || String.IsNullOrWhiteSpace(pack.RootDirectory))
                throw new ContentManifestValidationException("Invalid installed optional presentation pack.");
            pack = pack with { Manifest = ContentManifestValidator.ValidateOptional(pack.Manifest) };
            _optional[index] = pack;
            if (!optionalIds.Add(OptionalKey(pack.Manifest.Kind, pack.Identity)))
                throw new ContentManifestValidationException("Duplicate installed optional presentation identity.");
        }
    }

    private static bool SameIdentity(ContentPackIdentity left, ContentPackIdentity right)
        => String.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
        && String.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && String.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static string IdentityKey(ContentPackIdentity identity)
        => $"{identity.StableId}\u001f{identity.Version}\u001f{identity.ContentHash.ToLowerInvariant()}";

    private static string OptionalKey(OptionalPresentationKind kind, ContentPackIdentity identity)
        => $"{kind}\u001f{IdentityKey(identity)}";
}
