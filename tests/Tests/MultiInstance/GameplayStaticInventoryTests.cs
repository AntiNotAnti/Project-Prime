using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats.Collision;
using MphRead.Formats;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class GameplayStaticInventoryTests
{
    [Fact]
    public void CompiledGameplayOwnersHaveNoUnreviewedMutableStaticStorage()
    {
        // Enumerate compiled fields, including readonly arrays/collections: readonly
        // freezes a reference, not its contents. Nested AI/entity types are included.
        var owners = typeof(Scene).Assembly.GetTypes().Where(type =>
            type.Namespace == typeof(PlayerEntity).Namespace
            || type == typeof(Scene) || type == typeof(MatchRuntime)
            || type == typeof(MatchRandom) || type == typeof(MatchPlayers)
            || type == typeof(MatchRoster) || type == typeof(CollisionDetection));
        owners = owners.Concat(new[] { typeof(CameraSequence), typeof(CameraInfo), typeof(CameraSequenceManager),
            typeof(MatchLogic), typeof(MatchFlow), typeof(SpawnDirector), typeof(MatchLifecycle),
            typeof(ServerSimulation), typeof(ServerCombat), typeof(ServerBotManager), typeof(WorldStateCapture),
            typeof(ServerSceneServices), typeof(ServerInputStream), typeof(LagCompensationHistory),
            typeof(ProjectileCatchUp), typeof(MatchFeatureSet) }).Distinct();
        var fields = owners.SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => !field.IsLiteral && !(field.IsInitOnly && (field.FieldType.IsValueType || field.FieldType == typeof(string))))
            // C# compiler-generated delegate caches contain code, not gameplay state.
            .Where(field => !(field.DeclaringType!.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                && (typeof(Delegate).IsAssignableFrom(field.FieldType) || field.FieldType == field.DeclaringType)))
            .Select(field => field.DeclaringType!.FullName + "." + field.Name).OrderBy(name => name).ToArray();
        fields = fields.Except(ReviewedMetadata).ToArray();
        Assert.True(fields.Length == 0, "Review static storage and give immutable metadata an exact field allowlist entry:\n" + string.Join("\n", fields));
    }
    // Exact entries reviewed against declarations and read sites. These tables
    // contain fixed scan/sound/weapon/AI constants, never scene objects.
    private static readonly string[] ReviewedMetadata =
    [
        "MphRead.Entities.BeamProjectileEntity._homingTargetTypes",
        "MphRead.Formats.CameraSequence.<Filenames>k__BackingField",
        "MphRead.MatchFlow._alarmIntervals",
        "MphRead.Entities.DoorEntity._scanIds",
        "MphRead.Entities.ForceFieldEntity._scanIds",
        "MphRead.Entities.ItemInstanceEntity._scanIds",
        "MphRead.Entities.ItemInstanceEntity._sfxIds",
        "MphRead.Entities.NodeDefenseEntity.ProgressSeconds",
        "MphRead.Entities.ObjectEntity._secretSwitchSfx",
        "MphRead.Entities.ObjectEntity._sfxInfo",
        "MphRead.Entities.PlatformEntity._beamSfx",
        "MphRead.Entities.PlayerEntity._healthPickupAmounts",
        "MphRead.Entities.PlayerEntity._weaponOrder",
        "MphRead.Entities.PlayerEntity.<PlayerVolumes>k__BackingField",
        "MphRead.Entities.PlayerEntity+PlayerAiData._aimValues",
        "MphRead.Entities.PlayerEntity+PlayerAiData._botLevelRandomValues1",
        "MphRead.Entities.PlayerEntity+PlayerAiData._botLevelRandomValues2",
        "MphRead.Entities.PlayerEntity+PlayerAiData._dotValues",
        "MphRead.Entities.PlayerEntity+PlayerAiData._func4Ids",
        // A4 content-generation caches are synchronized on ContentEnvironment.SyncRoot;
        // their published read-only snapshots cannot change while a scene lease lives.
        "MphRead.Entities.PlayerEntity._altAttackNames",
        "MphRead.Entities.PlayerEntity._hunterNames",
        "MphRead.Entities.PlayerEntity._weaponNames",
        "MphRead.Entities.PlayerEntity._nameContext",
        "MphRead.Entities.PlayerEntity._kandenContext",
        "MphRead.Entities.PlayerEntity.<KandenAltNodeDistances>k__BackingField",
        // Language setter rejects changes with active content leases.
        "MphRead.Scene._language"
    ];
}
