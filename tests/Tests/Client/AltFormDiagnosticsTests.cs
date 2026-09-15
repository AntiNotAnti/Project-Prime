using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltFormDiagnosticsTests
{
    [Fact]
    public void RollingControlHeadingUsesRetainedHeadingAndCanonicalFallback()
    {
        Vector3 retained = PlayerEntity.ResolveAltDiagnosticControlHeading(
            rollingAlt: true, new Vector3(2, 99, 0), Vector3.UnitX);
        Assert.Equal(new Vector3(1, 0, 0), retained);

        Vector3 fallback = PlayerEntity.ResolveAltDiagnosticControlHeading(
            rollingAlt: true, Vector3.Zero, new Vector3(0, 3, -4));
        Assert.Equal(new Vector3(0, 0, -1), fallback);

        Vector3 canonical = PlayerEntity.ResolveAltDiagnosticControlHeading(
            rollingAlt: true, new Vector3(float.NaN, 0, 0), Vector3.Zero);
        Assert.Equal(new Vector3(0, 0, -1), canonical);
    }

    [Fact]
    public void AltDiagnosticFormatterIncludesAuthorityPresentationAndCounters()
    {
        var diagnostic = new AltFormDiagnostic(
            Slot: 2, Hunter: Hunter.Trace,
            AuthorityAltForm: true, PresentedAltForm: false,
            Morphing: true, Unmorphing: false,
            AltActionPhase: AltActionPhase.Active,
            AltActionTicks: 7, PoseEpoch: 11,
            ControlHeading: new Vector3(1, 0, -0.5f),
            CameraType: CameraType.Third1,
            FormReconciliationEvents: 1,
            ForcedFormCorrections: 2,
            AltActionCorrections: 3,
            MorphPhaseMismatches: 4,
            InvalidHeadingFallbacks: 5,
            CameraRecoveryEvents: 0);

        string line = NetDiagnostics.FormatAltDiagnostic(in diagnostic);

        Assert.StartsWith("[net-alt]", line);
        Assert.Contains("slot=2", line);
        Assert.Contains("hunter=Trace", line);
        Assert.Contains("authForm=a", line);
        Assert.Contains("presentedForm=b", line);
        Assert.Contains("morph=i", line);
        Assert.Contains("phase=Active:7", line);
        Assert.Contains("epoch=11", line);
        Assert.Contains("heading=1.00,0.00,-0.50", line);
        Assert.Contains("camera=Third1", line);
        Assert.Contains("formRec=1", line);
        Assert.Contains("forced=2", line);
        Assert.Contains("actionRec=3", line);
        Assert.Contains("morphMis=4", line);
        Assert.Contains("headingFallback=5", line);
        Assert.Contains("cameraRecovery=0", line);
        Assert.DoesNotContain('\n', line);
    }
}
