using System.Collections.Immutable;
using MphRead.Hud;
using Xunit;
namespace MphRead.Tests;
[Collection("Match baseline globals")]
public class PostMatchPresentationTests
{
    [Fact]
    public void PanelUsesFrozenActualDamageRatherThanEfficiencyNumerator()
    {
        using var state = new MatchBaselineTests.State();
        var match = state.Configure(GameMode.Battle);
        state.Activate(0, 0);
        match.Players[0].DamageDealt = 321;
        match.Players[0].BeamDamageDealt = 99;
        match.Players[0].Assists = 3;
        match.CaptureResult(12);
        match.Players[0].DamageDealt = 777;
        var panel = new PostMatchPresentation(match.Result!);
        Assert.Contains(panel.Pages[1], row => row.Contains("321 /"));
        Assert.DoesNotContain(panel.Pages[1], row => row.Contains("777 /") || row.Contains("99 /"));
    }
    [Fact]
    public void BestBeamHasStableTiesAndNoInventedBombCategory()
    {
        Assert.Equal("None", PostMatchPresentation.BestWeapon(ImmutableArray.Create(0, 0, 0)));
        Assert.Equal("Power Beam", PostMatchPresentation.BestWeapon(ImmutableArray.Create(3, 3, 1)));
    }
}
