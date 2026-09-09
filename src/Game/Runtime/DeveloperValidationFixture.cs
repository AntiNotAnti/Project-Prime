using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// Closed, code-owned scene fixtures used only by explicit developer validation.
/// These identifiers are not room, lobby, directory, or wire identities.
/// </summary>
internal enum DeveloperValidationFixtureId
{
    None,
    Unit1Rm1Dynamic
}

internal sealed record DeveloperValidationFixtureResource(string Path, long Bytes, string Sha256);

internal sealed record DeveloperValidationFixtureContentEvidence(
    string Fingerprint, int RawEntities, int AdmittedEntities,
    int ActivePlayerSpawns, int Doors, int ForceFields,
    int Platforms, int Objects)
{
    public int SkippedEntities => RawEntities - AdmittedEntities;
    public int DynamicEntities => Doors + ForceFields + Platforms + Objects;
}

internal sealed record DeveloperValidationFixtureRegistryEvidence(
    int Doors, int ForceFields, int Objects, int Platforms)
{
    public int Total => Doors + ForceFields + Objects + Platforms;
}

/// <summary>Immutable compiled descriptor; it cannot select an arbitrary resource path.</summary>
internal sealed class DeveloperValidationFixtureDescriptor
{
    internal DeveloperValidationFixtureDescriptor(DeveloperValidationFixtureId id, string cliValue,
        string mapKey, string contentVersion, GameMode mode,
        IReadOnlyList<DeveloperValidationFixtureResource> resources,
        DeveloperValidationFixtureRegistryEvidence expectedRegistry)
    {
        Id = id;
        CliValue = cliValue;
        MapKey = mapKey;
        ContentVersion = contentVersion;
        Mode = mode;
        Resources = resources;
        ExpectedRegistry = expectedRegistry;
        string canonical = String.Join('\n', resources.Select(resource
            => $"{resource.Path}|{resource.Bytes}|{resource.Sha256}")) + "\n";
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    public DeveloperValidationFixtureId Id { get; }
    public string CliValue { get; }
    public string MapKey { get; }
    public string ContentVersion { get; }
    public GameMode Mode { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<DeveloperValidationFixtureResource> Resources { get; }
    public DeveloperValidationFixtureRegistryEvidence ExpectedRegistry { get; }

    internal RoomMetadata CreateRoomMetadata() => new(
        id: 97,
        name: MapKey,
        inGameName: "Unit1 RM1 Dynamic Validation",
        archive: "unit1_RM1",
        modelPath: "unit1_RM1_model.bin",
        animationPath: "unit1_RM1_anim.bin",
        collisionPath: "unit1_RM1_collision.bin",
        texturePath: "unit1_rm1_tex.bin",
        entityPath: "unit1_RM1_Ent.bin",
        nodePath: "unit1_RM1_node.bin",
        roomNodeName: null,
        battleTimeLimit: 12 * 60 * 30,
        timeLimit: 4 * 60 * 30,
        pointLimit: 100,
        nodeLayer: 1,
        fogEnabled: true,
        clearFog: false,
        fogColor: new ColorRgb(31, 24, 18),
        fogSlope: 5,
        fogOffset: 65200,
        light1Color: new ColorRgb(31, 24, 18),
        light1Vector: new Vector3Fx(409, -4096, 0).ToFloatVector(),
        light2Color: new ColorRgb(13, 12, 7),
        light2Vector: new Vector3Fx(0, 4095, -409).ToFloatVector(),
        farClip: 1740800,
        killHeight: -122880,
        size: RoomSize.SinglePlayer,
        cameraMin: new Vector3Fx(-151552, -4096, -110592).ToFloatVector(),
        cameraMax: new Vector3Fx(184320, 102400, 81920).ToFloatVector(),
        playerMin: new Vector3Fx(-1228800, -1228800, -1228800).ToFloatVector(),
        playerMax: new Vector3Fx(1228800, 1228800, 1228800).ToFloatVector(),
        multiplayer: false);
}

internal static class DeveloperValidationFixtures
{
    private static readonly IReadOnlyList<DeveloperValidationFixtureResource> _unit1Rm1Resources
        = Array.AsReadOnly(new[]
        {
            new DeveloperValidationFixtureResource("_archives/unit1_RM1/unit1_RM1_model.bin", 198680,
                "a11d0f37bc07dd02a88b098d4bc1ccdcb99e1c470dffd89d933224e3c11c7ce5"),
            new DeveloperValidationFixtureResource("_archives/unit1_RM1/unit1_RM1_anim.bin", 3384,
                "8929ec6452b842d9b7c9a5231516dec98d08f3edad0a3c7323845f31141d056a"),
            new DeveloperValidationFixtureResource("_archives/unit1_RM1/unit1_RM1_collision.bin", 109516,
                "bfd970ab03ed2bf87c65c3c8838ffc23c239bf565848afab17e2656c17fa6036"),
            new DeveloperValidationFixtureResource("levels/textures/unit1_rm1_tex.bin", 141248,
                "1954f58348103e23cd66d8cd2a6d061c261ea7f01275d62480fcc697d5361582"),
            new DeveloperValidationFixtureResource("levels/entities/unit1_RM1_Ent.bin", 27116,
                "b76733bdde768c6396deea49149be12c7313e1d24851c2f46286329c4e88847c"),
            new DeveloperValidationFixtureResource("levels/nodeData/unit1_RM1_node.bin", 8886,
                "76f993ece45ccd421ef83b98124021f241560006951ecd738d19b90e9e76cd7d")
        });

    private static readonly DeveloperValidationFixtureDescriptor _unit1Rm1 = new(
        DeveloperValidationFixtureId.Unit1Rm1Dynamic,
        "unit1-rm1-dynamic",
        "VALIDATION UNIT1 RM1 DYNAMIC",
        "AMHE1",
        GameMode.Battle,
        _unit1Rm1Resources,
        new DeveloperValidationFixtureRegistryEvidence(Doors: 10, ForceFields: 11,
            Objects: 9, Platforms: 3));

    public static DeveloperValidationFixtureDescriptor Require(DeveloperValidationFixtureId id)
        => id switch
        {
            DeveloperValidationFixtureId.Unit1Rm1Dynamic => _unit1Rm1,
            _ => throw new ArgumentOutOfRangeException(nameof(id), "A supported developer validation fixture is required.")
        };

    public static DeveloperValidationFixtureId Parse(string value) => value switch
    {
        "none" => DeveloperValidationFixtureId.None,
        "unit1-rm1-dynamic" => DeveloperValidationFixtureId.Unit1Rm1Dynamic,
        _ => throw new ArgumentException("Validation fixture must be none or unit1-rm1-dynamic.")
    };

    internal static bool IsAdmittedEntityType(EntityType type)
        => type is EntityType.PlayerSpawn or EntityType.Door or EntityType.ForceField
            or EntityType.Platform or EntityType.Object;

    public static DeveloperValidationFixtureContentEvidence ValidateCurrentContent(
        DeveloperValidationFixtureId id)
    {
        DeveloperValidationFixtureDescriptor descriptor = Require(id);
        return ValidateContent(descriptor, Paths.MphKey, relative =>
            ContentEnvironment.ReadBytes(Paths.Combine(Paths.FileSystem, relative)));
    }

    internal static DeveloperValidationFixtureContentEvidence ValidateContent(
        DeveloperValidationFixtureDescriptor descriptor, string contentVersion,
        Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(read);
        if (!String.Equals(contentVersion, descriptor.ContentVersion, StringComparison.Ordinal))
            throw new ProgramException("Developer validation fixture content version mismatch.");
        foreach (DeveloperValidationFixtureResource resource in descriptor.Resources)
        {
            if (Path.IsPathRooted(resource.Path)
                || resource.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
                throw new ProgramException("Developer validation fixture resource escaped the content root.");
            byte[] bytes;
            try { bytes = read(resource.Path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new ProgramException("Developer validation fixture resource is missing.");
            }
            if (bytes.LongLength != resource.Bytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(resource.Sha256)))
                throw new ProgramException("Developer validation fixture resource fingerprint mismatch.");
        }

        RoomMetadata metadata = descriptor.CreateRoomMetadata();
        IReadOnlyList<Entity> entities = Read.GetEntities(metadata.EntityPath!, -1, false);
        var evidence = new DeveloperValidationFixtureContentEvidence(descriptor.Fingerprint,
            entities.Count,
            entities.Count(entity => IsAdmittedEntityType(entity.Type)),
            entities.Count(entity => entity is Entity<PlayerSpawnEntityData> spawn && spawn.Data.Active != 0),
            entities.Count(entity => entity.Type == EntityType.Door),
            entities.Count(entity => entity.Type == EntityType.ForceField),
            entities.Count(entity => entity.Type == EntityType.Platform),
            entities.Count(entity => entity.Type == EntityType.Object));
        if (evidence.AdmittedEntities != 46
            || evidence.ActivePlayerSpawns != 2 || evidence.Doors != 10
            || evidence.ForceFields != 11 || evidence.Platforms != 3 || evidence.Objects != 20
            || evidence.DynamicEntities == 0)
            throw new ProgramException("Developer validation fixture entity inventory mismatch.");
        return evidence;
    }
}
