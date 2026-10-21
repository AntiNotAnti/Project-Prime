#!/usr/bin/env python3
"""Read existing-harness pose results; never runs a scene or modifies evidence."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re

HUNTERS = ("Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel")
RATES = (60, 120, 144, 240, 360, 500)
SCHEDULES = ("steady", "jitter144")


def scheduled_elapsed(schedule, rate, output_index):
    return ((.5, 1.5, .75, 1.25)[output_index % 4] if schedule == "jitter144" else 1) / rate
ORIGINAL_SCENARIO = "native-pose-v1"
SCENARIOS = (ORIGINAL_SCENARIO, "native-pose-charge-retreat-v2")
PROJECTION = re.compile(r"[0-9a-fA-F]{64}:[0-9a-fA-F]{64}\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
CAPTURES = {300: "biped-motion", 420: "charge", 468: "charge-release", 560: "alt-motion",
            650: "alt-attack", 870: "unmorph", 970: "remorph", 1021: "death", 1250: "respawn",
            1355: "scope", 1382: "teleport", 1390: "clock-reset", 1440: "final"}


def finite_number(value):
    return type(value) in (int, float) and math.isfinite(value)


def rows(value):
    return value if isinstance(value, list) and all(isinstance(row, dict) for row in value) else []


def positive_integer(value):
    return type(value) is int and value > 0


def scenario_identity(result):
    # Missing in historical original reports; explicit null/unknown is invalid.
    return result.get("scenarioId", ORIGINAL_SCENARIO)


def analyze(results, rates=RATES, hunters=HUNTERS, require_pixel_controls=False, scenario_id=None,
            require_particle_sampling=False, require_jitter=False, require_supplemental=False):
    errors, comparisons, records, observations = [], [], {}, []
    observed_scenarios = set()
    for result in results:
        if not isinstance(result, dict):
            errors.append("result is not an object")
            continue
        hunter, rate = result.get("hunter"), result.get("requestedOutputScheduleHz")
        schedule_id = result.get("scheduleId", "steady")
        cell = "jitter144" if schedule_id == "jitter144" else rate
        requested_cells = list(rates) + (["jitter144"] if require_jitter else [])
        label = f"{hunter}/{cell}"
        def require(condition, message):
            if not condition:
                errors.append(f"{label}: {message}")
        require(result.get("mode") == "full-world-display-pose" and result.get("schema") == 1,
                "unsupported diagnostic result")
        require(result.get("complete") is True and result.get("status") == "PASS" and "error" not in result,
                "runtime result failed or incomplete")
        require(hunter in hunters and cell in requested_cells, "outside requested bounded matrix")
        require(schedule_id in SCHEDULES and (schedule_id != "jitter144" or rate == 144), "unknown or incompatible output schedule")
        if hunter not in hunters or type(rate) is not int or cell not in requested_cells:
            continue
        require((hunter, cell) not in records, "duplicate hunter/rate result")
        if require_particle_sampling:
            particles = result.get("particleSampling")
            particles = particles if isinstance(particles, dict) else {}
            motion, appearance = particles.get("motionSamples"), particles.get("appearanceSamples")
            require(positive_integer(particles.get("samples")), "missing native particle sample observations")
            require(type(motion) is int and motion >= 0 and type(appearance) is int and appearance >= 0,
                    "invalid native particle sample counters")
            samples = particles.get("samples")
            require(type(samples) is int and type(motion) is int and type(appearance) is int
                    and 0 <= motion <= samples and 0 <= appearance <= samples,
                    "native particle counters exceed observed samples")
            require(motion == 0 and appearance == 0 if rate == 60 else positive_integer(motion),
                    "particle observations did not exercise the current/high-refresh policy")
            beams = result.get("beamSampling")
            beams = beams if isinstance(beams, dict) else {}
            draws, sampled = beams.get("draws"), beams.get("sampled")
            require(positive_integer(draws) and type(sampled) is int and 0 <= sampled <= draws,
                    "missing or invalid native beam draw observations")
            require(sampled == 0 if rate == 60 else positive_integer(sampled),
                    "beam observations did not exercise the current/high-refresh policy")
        if (hunter, cell) in records:
            continue
        records[hunter, cell] = result
        identity = scenario_identity(result)
        valid_identity = isinstance(identity, str) and identity in SCENARIOS
        require(valid_identity, "unknown scenario identity")
        if valid_identity:
            observed_scenarios.add(identity)
        if scenario_id is not None:
            require(identity == scenario_id, "result differs from requested scenario identity")
        require(result.get("platform") in ("Mac", "Windows"), "unsupported native qualification platform")
        require(result.get("simulationHz") == 60 and result.get("scheduledElapsedSeconds") == (1 / rate if schedule_id == "steady" else None),
                "schedule/simulation contract differs")
        require(result.get("performanceAcceptance") == "NOT_CLAIMED_READBACK_FIXTURE"
                and result.get("panelScanoutAcceptance") == "NOT_CLAIMED", "invalid timing/panel acceptance claim")
        steps = rows(result.get("steps"))
        require([row.get("tick") for row in steps] == list(range(1, 1441)), "missing or reordered canonical step")
        require(all(isinstance(row.get("canonicalProjection"), str)
                    and PROJECTION.fullmatch(row["canonicalProjection"]) for row in steps),
                "missing exact established gameplay/presentation projection")
        require(isinstance(result.get("inputTimelineSha256"), str)
                and SHA256.fullmatch(result["inputTimelineSha256"]), "missing input timeline identity")
        if require_supplemental:
            require(result.get("supplementalWitness") == "fixture-local exact combat/source-input/scene-and-global-RNG; no archival schema changes",
                    "independent supplemental witness contract unavailable")
            require(all(all(isinstance(row.get(key), str) and SHA256.fullmatch(row[key])
                            for key in ("combatWitness", "sourceInputWitness", "rngWitness"))
                        and all(type(row.get(key)) is int and 0 <= row[key] <= 0xffffffff for key in ("rng1", "rng2"))
                        for row in steps), "combat/source-input/RNG witness missing or malformed")
        schedule = rows(result.get("schedule"))
        expected_tick = 0
        for output, row in enumerate(schedule, 1):
            increment = row.get("actualSteps")
            if type(increment) is not int or increment not in (0, 1):
                require(False, "unexpected simulation step count")
                break
            expected_tick += increment
            if row.get("outputFrame") != output or row.get("sceneFrame") != expected_tick:
                require(False, "output/step mapping changed")
                break
            if row.get("scheduledElapsed") != scheduled_elapsed(schedule_id, rate, output - 1) or row.get("active") is not True:
                require(False, "normal FrameTiming schedule not active")
                break
            require(all(finite_number(row.get(key)) and 0 <= row[key] <= 1
                        for key in ("alpha", "presentationAlpha")), "invalid interpolation sample")
        require(expected_tick == 1440 and len(schedule) == result.get("outputFrames"), "output schedule incomplete")
        require(positive_integer(result.get("repeatedDrawCanonicalChecks")), "no repeated-draw canonical witness")
        require((result.get("sampledActorCarryFrames") == 0 if rate == 60 else
                 positive_integer(result.get("sampledActorCarryFrames")) and positive_integer(result.get("zeroStepCanonicalChecks"))),
                "current/high-refresh carry path unexercised")
        coverage = result.get("coverage") if isinstance(result.get("coverage"), dict) else {}
        require(all(coverage.get(key) is True for key in ("died", "respawned", "alt", "altAttack", "scope",
                "charge", "effects", "chargeEffectAttached", "clockReset")), "required native state unexercised")
        require(finite_number(coverage.get("scopeFovMin"))
                and finite_number(coverage.get("scopeFovMax"))
                and coverage["scopeFovMax"] > coverage["scopeFovMin"], "scope transition not exercised")
        fences = rows(result.get("lifetimeFences"))
        reasons = {row.get("reason") for row in fences}
        require({"clock-reset-immediate", "uncaptured-teleport-1382", "uncaptured-teleport-1384",
                 "dead", "respawn-first-frame"}.issubset(reasons), "lifetime boundary witness missing")
        require(all(row.get("position") == row.get("sampledPosition") for row in fences), "stale pose crossed a boundary")
        pictures = rows(result.get("pictures"))
        require(len(pictures) == len(CAPTURES) and {row.get("tick"): row.get("label") for row in pictures} == CAPTURES,
                "capture checkpoint missing or duplicated")
        transactions = rows(result.get("cameraControlTransactions"))
        require(result.get("cameraControlAcceptance") == "STRICT_ENTRY_EXIT_CANONICAL_REQUIRED",
                "strict camera transaction contract unavailable")
        require(len(transactions) == len(CAPTURES)
                and {row.get("tick"): row.get("label") for row in transactions} == CAPTURES
                and all(row.get("exactEstablishedProjectionMatch") is True
                        and isinstance(row.get("entryProjection"), str)
                        and PROJECTION.fullmatch(row["entryProjection"])
                        and row.get("entryProjection") == row.get("exitProjection")
                        and row.get("differences") == [] for row in transactions),
                "camera entry/exit changed canonical state or lacks complete exact witness")
        if require_supplemental:
            require(all(isinstance(row.get("supplementalEntry"), str)
                        and re.fullmatch(r"[0-9a-f]{64}:[0-9a-f]{64}:[0-9a-f]{64}", row["supplementalEntry"])
                        and row["supplementalEntry"] == row.get("supplementalExit") for row in transactions),
                    "camera transaction changed independent combat/source-input/RNG witness")
        by_label = {row.get("label"): row for row in pictures}
        pixel_mode = result.get("pixelControlAcceptance", "NOT_REQUESTED")
        require(pixel_mode in ("NOT_REQUESTED", "ISOLATED_NATIVE_GUN_AND_CHARGE_REQUIRED"),
                "unknown pixel-control contract")
        if require_pixel_controls:
            require(pixel_mode == "ISOLATED_NATIVE_GUN_AND_CHARGE_REQUIRED", "isolated native pixel controls not enabled")
        if pixel_mode == "ISOLATED_NATIVE_GUN_AND_CHARGE_REQUIRED":
            for label, field in (("biped-motion", "firstPersonGunPixels"), ("charge", "chargeEffectPixels")):
                control = by_label.get(label, {}).get(field)
                control = control if isinstance(control, dict) else {}
                witness = control.get("result")
                witness = witness if isinstance(witness, dict) else {}
                require(witness.get("status") == "PASS" and positive_integer(witness.get("changedPixels"))
                        and witness.get("unstablePixels") == 0 and witness.get("exactOwnerAndFlagsRestored") is True
                        and witness.get("sceneFrameBefore") == witness.get("sceneFrameAfter") == by_label.get(label, {}).get("tick")
                        and positive_integer(witness.get("simulationStepsBefore"))
                        and witness.get("simulationStepsBefore") == witness.get("simulationStepsAfter")
                        and finite_number(witness.get("alpha")) and 0 <= witness["alpha"] <= 1
                        and isinstance(witness.get("entryProjection"), str)
                        and PROJECTION.fullmatch(witness["entryProjection"])
                        and witness.get("entryProjection") == witness.get("exitProjection")
                        and all(isinstance(witness.get(key), str) and SHA256.fullmatch(witness[key])
                                for key in ("enabledPixelSha256", "disabledPixelSha256", "restoredPixelSha256"))
                        and witness.get("enabledPixelSha256") == witness.get("restoredPixelSha256")
                        and witness.get("enabledPixelSha256") != witness.get("disabledPixelSha256"),
                        label + " lacks stable isolated native pixels and exact canonical/owner restoration")
        for key in ("biped-motion", "alt-motion", "alt-attack", "scope"):
            third = by_label.get(key, {}).get("third")
            third = third if isinstance(third, dict) else {}
            require(third.get("status") == "PASS" and positive_integer(third.get("changedBodyPixels"))
                    and third.get("unstablePixels") == 0, key + " lacks stable positive native body pixels")
        diagnostics = rows(result.get("drawAnchorDiagnostics"))
        observations.append({"hunter": hunter, "outputScheduleHz": rate,
                             "sameDisplayTimeAnchorSamples": len(diagnostics),
                             "observedAnchorDifferences": [row.get("label") for row in diagnostics
                                 if row.get("sameDisplayTimeAnchorMatch") is False],
                             "scope": "diagnostic observations only; outside frozen canonical contract"})
    if len(observed_scenarios) != 1:
        errors.append("requested matrix must use one scenario identity; original and retreat scenarios cannot be combined")
    for hunter in hunters:
        requested_cells = list(rates) + (["jitter144"] if require_jitter else [])
        present = [cell for cell in requested_cells if (hunter, cell) in records]
        if present != requested_cells:
            errors.append(f"{hunter}: requested rate matrix incomplete")
        if not present:
            continue
        reference = records[hunter, present[0]]
        for rate in present[1:]:
            candidate = records[hunter, rate]
            first = None
            if scenario_identity(reference) != scenario_identity(candidate):
                errors.append(f"{hunter}/{rate}: fixture scenario identity differs")
                comparisons.append({"hunter": hunter, "referenceHz": present[0], "candidateHz": rate,
                                    "exactCanonicalAndInputMatch": False, "scenarioIdentityMatch": False,
                                    "firstDifference": {"tick": None, "field": "scenarioId"}})
                continue
            for key in ("platform", "assemblyMvid", "room", "framebuffer", "renderSize", "responsiveCamera", "fixedWeapon", "pixelControlAcceptance"):
                if reference.get(key) != candidate.get(key):
                    errors.append(f"{hunter}/{rate}: fixture identity differs for {key}")
            reference_steps, candidate_steps = rows(reference.get("steps")), rows(candidate.get("steps"))
            common_count = min(len(reference_steps), len(candidate_steps))
            for expected, actual in zip(reference_steps, candidate_steps):
                for key in (("tick", "canonicalProjection", "preparedInput", "consumedInput")
                            + (("combatWitness", "sourceInputWitness", "rngWitness", "rng1", "rng2") if require_supplemental else ())):
                    if expected.get(key) != actual.get(key):
                        first = {"tick": actual.get("tick"), "field": key}
                        break
                if first:
                    break
            common_prefix_exact = first is None
            if first is None and len(reference_steps) != len(candidate_steps):
                first = {"tick": common_count + 1, "field": "missingStep"}
            same_input = reference.get("inputTimelineSha256") == candidate.get("inputTimelineSha256")
            if first is None and not same_input:
                first = {"tick": None, "field": "inputTimelineSha256"}
            if first or not same_input:
                detail = "incomplete" if first and first["field"] == "missingStep" else "differs"
                errors.append(f"{hunter}/{rate}: canonical/input stream {detail} relative to {present[0]} Hz")
            comparisons.append({"hunter": hunter, "referenceHz": present[0], "candidateHz": rate,
                                "exactCanonicalAndInputMatch": first is None and same_input,
                                "exactCombatSourceInputRngMatch": first is None and require_supplemental,
                                "commonPrefixSteps": common_count, "commonPrefixCanonicalAndInputExact": common_prefix_exact,
                                "firstDifference": first})
    return {"schema": 1, "status": "PASS_BOUNDED_NATIVE_DRAW_QUALIFICATION" if not errors else "FAIL_OR_INCOMPLETE",
            "hunters": list(hunters), "outputScheduleHz": list(rates),
            "scenarioId": next(iter(observed_scenarios)) if len(observed_scenarios) == 1 else None, "errors": errors, "comparisons": comparisons,
            "drawAnchorObservations": observations,
            "performanceAcceptance": "NOT_CLAIMED_READBACK_FIXTURE", "panelScanoutAcceptance": "NOT_CLAIMED",
            "effectDisplayAcceptance": "ISOLATED_NATIVE_CHARGE_PIXELS_REQUIRED" if require_pixel_controls
                else "ATTACHMENT_AND_CAPTURE_ONLY_NO_ISOLATED_EFFECT_PIXEL_GATE",
            "cameraControlAcceptance": "STRICT_ENTRY_EXIT_CANONICAL_REQUIRED",
            "firstPersonGunAcceptance": "ISOLATED_NATIVE_GUN_PIXELS_REQUIRED" if require_pixel_controls
                else "CAPTURE_ONLY_NO_ISOLATED_GUN_PIXEL_GATE",
            "isolatedPixelControlsRequired": require_pixel_controls,
            "nativeParticleSamplingRequired": require_particle_sampling,
            "independentSupplementalWitnessesRequired": require_supplemental, "jitterRequired": require_jitter,
            "replayPlaybackAcceptance": "NOT_RUN_EXISTING_CANONICAL_PROJECTIONS_ONLY"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rates", nargs="+", type=int, choices=RATES, default=RATES)
    parser.add_argument("--require-jitter", action="store_true", help="require one declared jitter144 arm per Hunter")
    parser.add_argument("--require-supplemental", action="store_true", help="require independent combat/source-input/RNG comparisons")
    parser.add_argument("--require-pixel-controls", action="store_true",
                        help="require the separate -posecheck -pose-pixels native gun/charge positive controls")
    parser.add_argument("--require-particle-sampling", action="store_true",
                        help="require current 60 Hz and fractional native particle draw observations")
    parser.add_argument("--hunters", nargs="+", choices=HUNTERS, default=HUNTERS)
    parser.add_argument("--scenario", choices=SCENARIOS,
                        help="require this exact scenario; missing historical identities mean native-pose-v1 only")
    args = parser.parse_args()
    results, artifacts = [], []
    for path in args.results:
        data = path.read_bytes()
        if len(data) > 32 * 1024 * 1024:
            raise ValueError("Pose diagnostic JSON exceeds bounded result size")
        results.append(json.loads(data, parse_constant=lambda value: (_ for _ in ()).throw(
            ValueError("Non-finite JSON constant: " + value))))
        artifacts.append({"file": path.name, "sha256": hashlib.sha256(data).hexdigest()})
    report = analyze(results, tuple(args.rates), tuple(args.hunters), args.require_pixel_controls, args.scenario,
                     args.require_particle_sampling, args.require_jitter, args.require_supplemental)
    report["artifacts"] = artifacts
    if args.output.exists():
        raise FileExistsError("Analysis output already exists; preserve previous evidence")
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(report["status"])
    return 0 if not report["errors"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
