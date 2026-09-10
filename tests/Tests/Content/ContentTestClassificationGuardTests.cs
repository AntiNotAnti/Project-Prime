using System;
using System.Reflection;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// Keeps the real AMHE1 integration fixtures out of the content-free CI lane.
/// The list is deliberately keyed to test methods, not source-file names: each
/// method reaches the shared OpenAmhe1 fixture, which resolves extracted data.
/// </summary>
public sealed class ContentTestClassificationGuardTests
{
    [Fact]
    public void KnownAmhe1FixturesRemainMarkedAsContentDependent()
    {
        AssertRequiresGameContent(typeof(NetworkActionIntegrationTests),
            nameof(NetworkActionIntegrationTests.Amhe1UdpEdgesReachAuthoritativeSimulationOnceUnderLossAndReordering));
        AssertRequiresGameContent(typeof(NetworkActionIntegrationTests),
            nameof(NetworkActionIntegrationTests.RepeatedUdpDesiredWeaponWaitsThroughGunTransitionAndTracksPreviousWeapon));
        AssertRequiresGameContent(typeof(NetworkActionIntegrationTests),
            nameof(NetworkActionIntegrationTests.DisconnectingFlagPublishesTheCurrentSimulationTick));
        AssertRequiresGameContent(typeof(LateJoinTests),
            nameof(LateJoinTests.DisconnectReconnectRestoresParticipatingSlotTeamAndStats));
        AssertRequiresGameContent(typeof(LateJoinTests),
            nameof(LateJoinTests.EliminatedSurvivalParticipantReconnectsAsWaitingSpectator));
        AssertRequiresGameContent(typeof(LateJoinTests),
            nameof(LateJoinTests.Amhe1LoadingDuringCountdownReadyAfterPlayingBecomesWaitingSpectator));
        AssertRequiresGameContent(typeof(LateJoinTests),
            nameof(LateJoinTests.Amhe1SimulationKeepsNextJoinOutOfCurrentMatchAndActivatesAfterRotation));
    }

    private static void AssertRequiresGameContent(Type testType, string methodName)
    {
        MethodInfo? method = testType.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(HasRequiresGameContentTrait(method!),
            $"{testType.FullName}.{methodName} must remain excluded from the content-free test lane.");
    }

    private static bool HasRequiresGameContentTrait(MethodInfo method)
    {
        foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
        {
            if (attribute.AttributeType != typeof(TraitAttribute)
                || attribute.ConstructorArguments.Count != 2)
            {
                continue;
            }

            object? name = attribute.ConstructorArguments[0].Value;
            object? value = attribute.ConstructorArguments[1].Value;
            if (name is string traitName && value is string traitValue
                && traitName == "RequiresGameContent" && traitValue == "true")
            {
                return true;
            }
        }
        return false;
    }
}
