using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    private MapBipedJumpWitness? _packageJumpProbe;
    private string? _packageJumpProbeHunter;

    private void ConfigurePackageJumpProbe(string[] args, JsonElement input)
    {
        if (!_movementPackageRoom || !_movementRequireClearance || !_movementJumpTrial
            || _movementAlt || _movementWall || _movementNonWalkable || _bipedTransit
            || _movementSourceLaunch || _movementSourceTeleport
            || args.Any(a => a is "-movement-certified-jump" or "-movement-local-fixture" or "-movement-spawn"
                or "-movement-reverse" or "-movement-witness" or "-movement-waypoints" or "-movement-contact-path"))
            throw new ArgumentException("Manual packaged jump requires biped key input, unchanged collision and strict clearance.");
        var definition = MapDefinition.Load(input.GetProperty("project").GetString()!);
        var compilation = MapCompiler.Compile(definition);
        if (!compilation.Validation.IsValid || compilation.Map is not { AltFormCertification.Passed: true } map)
            throw new InvalidDataException("Manual jump requires a currently certified package.");
        var probe = input.GetProperty("nativeJumpProbe");
        Vector3 V(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        var start = V(probe.GetProperty("start")); var end = V(probe.GetProperty("end"));
        string route = probe.GetProperty("description").GetString()!;
        // Reuse the production all-hunter ballistic verifier, including every
        // form's supported endpoint and enclosing unmorph-capsule requirements.
        var parts = typeof(MapPacker).GetMethod("CollisionParts", BindingFlags.Static | BindingFlags.NonPublic)!;
        var faces = map.Solid.SelectMany(f => (IEnumerable<BuiltFace>)parts.Invoke(null, new object[] { f })!)
            .Where(f => !f.IgnorePlayers && f.Points.Length >= 3).ToArray();
        var indexType = typeof(MapAltFormCertification).GetNestedType("FaceIndex", BindingFlags.NonPublic)!;
        var index = Activator.CreateInstance(indexType, new object[] { faces, 3f })!;
        object?[] arguments = { index, start, end, route, CancellationToken.None, null };
        bool passed = (bool)typeof(MapAltFormCertification).GetMethod("TryBipedJumpRoute", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        if (!passed || arguments[5] is not MapBipedJumpWitness { Hunters.Count: 7 } proof)
            throw new InvalidDataException("Manual jump failed current all-seven-hunter path and all-form endpoint proof.");
        int hunterAt = Array.IndexOf(args, "-hunter");
        string hunter = hunterAt < 0 ? "Samus" : Enum.Parse<Hunter>(args[hunterAt + 1], true).ToString();
        if (!proof.Hunters.Any(h => h.Hunter == hunter)) throw new InvalidDataException("Missing manual hunter proof.");
        _packageJumpProbe = proof; _packageJumpProbeHunter = hunter;
        _movementSourceDefinition = definition;
        _movementStart = start; _movementGoal = end; _movementRoute = route;
        _movementCollision = (byte[])typeof(MapPacker).GetMethod("BuildCollision", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { map })!;
    }

    private object? PackageJumpProbeResult() => _packageJumpProbe is not { } proof ? null : new
    {
        scope = "Manual region access in unchanged packaged collision; not a packed bot jump or navigation anchor claim.",
        hunter = _packageJumpProbeHunter,
        allHunterBodyProofs = proof.Hunters.Count,
        allFormSupportedEndpointsAndUnmorphCapsuleVerified = true,
        proofPlayback = false
    };
}
