using System.Linq;
using MphRead.Droid;
using MphRead.Mods.Input;
using Xunit;
namespace MphRead.Tests.Client;
[Collection("Match baseline globals")]
public class TouchResultNavigationTests
{
    [Fact]
    public void ResultsReuseVisibleNextPreviousAndMenuWithoutGameplayButtons()
    {
        var controls = new TouchControls(); controls.Layout(1280, 720, 1);
        controls.SetResultNavigation(true, false, false);
        Assert.Equal(new[] { TouchAction.Shoot, TouchAction.Morph, TouchAction.Pause }, controls.Buttons.Where(b => b.Visible).Select(b => b.Action));
        Assert.Equal("NEXT", controls.Buttons.Single(b => b.Action == TouchAction.Shoot).Label);
        Assert.Equal("PREV", controls.Buttons.Single(b => b.Action == TouchAction.Morph).Label);
        var next = controls.Buttons.Single(b => b.Action == TouchAction.Shoot);
        controls.PointerDown(1, next.CentreX, next.CentreY);
        Assert.True(controls.IsHeld(TouchAction.Shoot));
        controls.PointerUp(1);
        Assert.False(controls.IsHeld(TouchAction.Shoot));
    }
    [Fact]
    public void ReplayHasTouchRecapToggleAndOpenArchiveHasClose()
    {
        var controls = new TouchControls(); controls.Layout(1280, 720, 1);
        controls.SetSpectator(true, false);
        controls.SetResultNavigation(false, true, false);
        var toggle = controls.Buttons.Single(b => b.Action == TouchAction.WeaponMenu);
        Assert.True(toggle.Visible); Assert.Equal("RECAP", toggle.Label);
        controls.SetResultNavigation(false, true, true);
        Assert.Equal("CLOSE", toggle.Label);
        Assert.False(controls.Buttons.Single(b => b.Action == TouchAction.SpectatorView).Visible);
        controls.SetResultNavigation(false, false, false);
        Assert.False(toggle.Visible);
    }
    [Fact]
    public void VotingResultsAddVoteWithoutRestoringGameplayControls()
    {
        var controls = new TouchControls(); controls.Layout(1280, 720, 1);
        controls.SetResultNavigation(true, false, false, voting: true);
        Assert.Equal(new[] { TouchAction.Shoot, TouchAction.Morph, TouchAction.WeaponMenu, TouchAction.Pause },
            controls.Buttons.Where(b => b.Visible).Select(b => b.Action));
        Assert.Equal("NEXT", controls.Buttons.Single(b => b.Action == TouchAction.Shoot).Label);
        Assert.Equal("PREV", controls.Buttons.Single(b => b.Action == TouchAction.Morph).Label);
        Assert.Equal("VOTE", controls.Buttons.Single(b => b.Action == TouchAction.WeaponMenu).Label);

        controls.SetResultNavigation(true, false, false, voting: false);
        Assert.False(controls.Buttons.Single(b => b.Action == TouchAction.WeaponMenu).Visible);
    }

    [Fact]
    public void VotingFlagWithoutBallotDoesNotChangeOrdinaryGameplayLayout()
    {
        var controls = new TouchControls(); controls.Layout(1280, 720, 1);
        controls.SetResultNavigation(false, false, false, voting: true);
        Assert.Equal("WEAPON", controls.Buttons.Single(b => b.Action == TouchAction.WeaponMenu).Label);
        Assert.True(controls.Buttons.Single(b => b.Action == TouchAction.WeaponMenu).Visible);
        Assert.True(controls.Buttons.Single(b => b.Action == TouchAction.Shoot).Visible);
    }

    [Fact]
    public void MorphBallSwipeRebasesWhenFormChangesDuringAimContact()
    {
        var controls = new TouchControls();
        controls.Layout(1280, 720, 1);
        controls.MorphBallBoostEnabled = false;
        controls.PointerDown(1, 700, 300, 100);
        controls.PointerMove(1, 730, 300, 110);
        Assert.False(controls.TakeMorphBallBoost().Fired);

        controls.MorphBallBoostEnabled = true;
        controls.PointerMove(1, 790, 300, 125);

        (bool fired, float x, float y) = controls.TakeMorphBallBoost();
        Assert.True(fired);
        Assert.Equal(1, x, 4);
        Assert.Equal(0, y, 4);

        controls.MorphBallBoostEnabled = false;
        controls.PointerMove(1, 900, 300, 135);
        Assert.False(controls.TakeMorphBallBoost().Fired);
    }

    [Fact]
    public void WedgeGeometryMatchesTheNineStickSelectionSectors()
    {
        var wheel = new WeaponRadialSelection();
        for (int sector = 0; sector < 9; sector++)
        {
            var centre = WeaponRadialSelection.SectorPoint(sector, .5f, 1, 1);
            wheel.Update(true, false, centre.X, -centre.Y, .2f, 511);
            Assert.Equal(WeaponRadialSelection.WeaponAt(sector), wheel.Preview);
            var right = WeaponRadialSelection.SectorPoint(sector, 1, 1, 1);
            var nextLeft = WeaponRadialSelection.SectorPoint((sector + 1) % 9, 0, 1, 1);
            Assert.True((right - nextLeft).Length < .00001f);
        }
    }
}
