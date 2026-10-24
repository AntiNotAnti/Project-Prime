using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    private MapBipedJumpWitness? _certifiedJump;
    private MapHunterJumpWitness? _certifiedHunterJump;
    private int _certifiedJumpFrom, _certifiedJumpTo;
    private bool _certifiedJumpLoadedAnchors;

    private void ConfigureCertifiedJumpReplay(string[] args, JsonElement report)
    {
        if (!_movementPackageRoom || !_movementRequireClearance || !_movementJumpTrial
            || _movementAlt || _movementWall || _movementNonWalkable || _bipedTransit
            || _movementSourceLaunch || _movementSourceTeleport
            || args.Any(a => a is "-movement-local-fixture" or "-movement-spawn" or "-movement-reverse"
                or "-movement-witness" or "-movement-contact-path" or "-movement-waypoints"))
            throw new ArgumentException("Certified jump requires unchanged packaged collision, biped keyboard jump and strict clearance.");
        var definition = MapDefinition.Load(report.GetProperty("project").GetString()!);
        var compilation = MapCompiler.Compile(definition);
        var map = compilation.Map;
        if (!definition.BipedJumpRoutes || !compilation.Validation.IsValid
            || map?.AltFormCertification is not { Passed: true } certification
            || !report.GetProperty("playerForms").GetProperty("Passed").GetBoolean())
            throw new InvalidDataException("Certified jump requires a passing current compiler certificate and explicit biped jump policy.");
        int routeAt = Array.IndexOf(args, "-movement-jump-route");
        if (routeAt >= 0 && routeAt + 1 >= args.Length) throw new ArgumentException("Missing certified jump route.");
        string? route = routeAt >= 0 ? args[routeAt + 1] : null;
        var saved = report.GetProperty("playerForms").GetProperty("jumpWitnesses").EnumerateArray()
            .Where(w => route == null || w.GetProperty("Route").GetString() == route).ToArray();
        if (saved.Length != 1) throw new InvalidDataException("Select exactly one saved certified jump route.");
        var value = saved[0];
        var jump = certification.JumpWitnesses.Single(w => w.Route == value.GetProperty("Route").GetString());
        Vector3 V(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        if (!value.GetProperty("RequiresBiped").GetBoolean() || value.GetProperty("Kind").GetString() != jump.Kind
            || V(value.GetProperty("start")) != jump.Start || V(value.GetProperty("end")) != jump.End)
            throw new InvalidDataException("Saved jump endpoint/kind differs from the current compiler certificate.");
        var hunters = value.GetProperty("hunters").EnumerateArray().ToArray();
        if (jump.Hunters.Count != 7 || hunters.Length != 7)
            throw new InvalidDataException("Certified jump requires all seven independent hunter proofs.");
        for (int i = 0; i < 7; i++)
        {
            var h = hunters[i]; var expected = jump.Hunters[i];
            if (h.GetProperty("Hunter").GetString() != expected.Hunter
                || h.GetProperty("HorizontalSpeed").GetSingle() != expected.HorizontalSpeed
                || h.GetProperty("JumpSpeed").GetSingle() != expected.JumpSpeed
                || h.GetProperty("Gravity").GetSingle() != expected.Gravity
                || h.GetProperty("SweepPadding").GetSingle() != expected.SweepPadding
                || h.GetProperty("MaximumContacts").GetInt32() != expected.MaximumContacts
                || !h.GetProperty("feet").EnumerateArray().Select(V).SequenceEqual(expected.Feet))
                throw new InvalidDataException("Saved hunter jump path differs from the current compiler certificate: " + expected.Hunter);
        }
        var graph = MapNodePacker.Analyze(map.Solid, MapNodePacker.EffectiveLinks(map.Definition), default,
            map.Definition.Import?.UnitsPerUnit, map.Definition.StepHeight,
            certification.WalkingWitnesses, certification.JumpWitnesses);
        _certifiedJumpFrom = Array.IndexOf(graph.Positions, jump.Start);
        _certifiedJumpTo = Array.IndexOf(graph.Positions, jump.End);
        if (_certifiedJumpFrom < 0 || _certifiedJumpTo < 0
            || graph.Types[_certifiedJumpTo] != NodeType.Aerial
            || !graph.Neighbours[_certifiedJumpFrom].Contains(_certifiedJumpTo))
            throw new InvalidDataException("Certified jump lost its exact directed packed aerial anchors.");
        int hunterAt = Array.IndexOf(args, "-hunter");
        string hunter = hunterAt < 0 ? "Samus" : Enum.Parse<Hunter>(args[hunterAt + 1], true).ToString();
        _certifiedHunterJump = jump.Hunters.Single(h => h.Hunter == hunter);
        _certifiedJump = jump;
        _movementSourceDefinition = definition;
        _movementStart = jump.Start; _movementGoal = jump.End; _movementRoute = jump.Route;
        _movementCollision = (byte[])typeof(MapPacker).GetMethod("BuildCollision", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { map })!;
        // The proof is verified metadata only. The normal input controller
        // performs the jump; no proof feet are played back into native physics.
    }

    private void VerifyLoadedCertifiedJump(RoomEntity room)
    {
        if (_certifiedJump is not { } jump) return;
        var data = room.NodeData;
        if (data?.Simple != true) throw new InvalidDataException("Loaded certified jump requires the native packed node list.");
        var nodes = data.Data[0][0];
        var from = nodes.Single(n => n.Id == _certifiedJumpFrom);
        var to = nodes.Single(n => n.Id == _certifiedJumpTo);
        // The native format leaves the second list empty. Actual routing is
        // run-length encoded (destination count,next hop) at Index1.
        int destination = 0, valueIndex = from.Index1, nextHop = -1;
        while (destination < nodes.Count && valueIndex + 1 < from.Values.Count)
        {
            int count = from.Values[valueIndex++], hop = from.Values[valueIndex++];
            if (count == 0 || destination + count > nodes.Count || hop >= nodes.Count)
                throw new InvalidDataException("Loaded jump anchor has a malformed native route run.");
            if (to.Id < destination + count) { nextHop = hop; break; }
            destination += count;
        }
        if ((from.Position - jump.Start).Length > 1f / 4096 || (to.Position - jump.End).Length > 1f / 4096
            || to.NodeType != NodeType.Aerial || nextHop != to.Id)
            throw new InvalidDataException("Loaded package lacks the certified jump's exact directed aerial anchors.");
        _certifiedJumpLoadedAnchors = true;
    }

    private object? CertifiedJumpResult() => _certifiedJump is not { } jump ? null : new
    {
        jump.Route, jump.Kind, jump.RequiresBiped,
        compilerHunterProofs = jump.Hunters.Count,
        hunter = _certifiedHunterJump!.Hunter,
        _certifiedHunterJump.HorizontalSpeed, _certifiedHunterJump.JumpSpeed, _certifiedHunterJump.Gravity,
        _certifiedHunterJump.SweepPadding, _certifiedHunterJump.MaximumContacts,
        sourceProofFeet = _certifiedHunterJump.Feet.Length,
        sourceProofSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(_certifiedHunterJump.Feet.Select(v => new[] { v.X, v.Y, v.Z }))))),
        packedFrom = _certifiedJumpFrom, packedTo = _certifiedJumpTo,
        loadedAerialAnchorsVerified = _certifiedJumpLoadedAnchors,
        proofPlayback = false
    };
}
