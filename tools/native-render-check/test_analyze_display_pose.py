"""Synthetic report-protocol tests; no renderer/world/panel acceptance data."""
import copy
import unittest

from analyze_display_pose import CAPTURES, analyze


def synthetic_result(rate):
    # The real clock is checked separately by test_display_pose_plan.py. This
    # closed report fixture exercises validation and mismatch handling only.
    schedule, tick = [], 0
    for output in range(1, rate * 24 + 1):
        next_tick = min(1440, output * 60 // rate)
        schedule.append({"outputFrame": output, "sceneFrame": next_tick,
                         "actualSteps": next_tick - tick, "scheduledElapsed": 1 / rate,
                         "active": True, "alpha": 0.5, "presentationAlpha": 0.5})
        tick = next_tick
    return {
        "schema": 1, "mode": "full-world-display-pose", "platform": "Mac", "hunter": "Samus",
        "status": "PASS", "complete": True, "simulationHz": 60, "requestedOutputScheduleHz": rate,
        "scheduledElapsedSeconds": 1 / rate, "assemblyMvid": "SYNTHETIC-NOT-A-BINARY",
        "room": "SYNTHETIC-NOT-A-WORLD", "framebuffer": [1280, 720], "renderSize": [1280, 720],
        "responsiveCamera": True, "fixedWeapon": False,
        "performanceAcceptance": "NOT_CLAIMED_READBACK_FIXTURE", "panelScanoutAcceptance": "NOT_CLAIMED",
        "steps": [{"tick": i, "canonicalProjection": "a" * 64 + ":" + "b" * 64,
                   "preparedInput": str(i), "consumedInput": str(i)} for i in range(1, 1441)],
        "schedule": schedule, "outputFrames": len(schedule), "repeatedDrawCanonicalChecks": 30,
        "sampledActorCarryFrames": 0 if rate == 60 else 20, "zeroStepCanonicalChecks": 0 if rate == 60 else 10,
        "coverage": dict.fromkeys(("died", "respawned", "alt", "altAttack", "scope", "charge", "effects",
                                   "chargeEffectAttached", "clockReset"), True)
                    | {"scopeFovMin": 0.5, "scopeFovMax": 1.0},
        "lifetimeFences": [{"reason": reason, "position": [1, 2, 3], "sampledPosition": [1, 2, 3]}
                           for reason in ("clock-reset-immediate", "uncaptured-teleport-1382",
                                          "uncaptured-teleport-1384", "dead", "respawn-first-frame")],
        "pictures": [{"label": label, "tick": tick,
                      "third": {"status": "PASS", "changedBodyPixels": 8, "unstablePixels": 0}}
                     for tick, label in CAPTURES.items()],
        "inputTimelineSha256": "c" * 64,
        "drawAnchorDiagnostics": [{"label": "synthetic", "sameDisplayTimeAnchorMatch": True}],
        "cameraControlAcceptance": "STRICT_ENTRY_EXIT_CANONICAL_REQUIRED",
        "cameraControlTransactions": [{"label": label, "tick": tick,
            "entryProjection": "a" * 64 + ":" + "b" * 64,
            "exitProjection": "a" * 64 + ":" + "b" * 64,
            "exactEstablishedProjectionMatch": True, "differences": []}
            for tick, label in CAPTURES.items()],
    }


class DisplayPoseAnalysisTests(unittest.TestCase):
    def setUp(self):
        self.results = [synthetic_result(rate) for rate in (60, 144)]

    def report(self):
        return analyze(self.results, rates=(60, 144), hunters=("Samus",))

    def test_synthetic_protocol_success(self):
        report = self.report()
        self.assertEqual(report["errors"], [])
        self.assertTrue(report["comparisons"][0]["exactCanonicalAndInputMatch"])
        self.assertEqual(report["panelScanoutAcceptance"], "NOT_CLAIMED")

    def test_particle_gate_rejects_old_and_vacuous_draw_reports(self):
        def report():
            return analyze(self.results, rates=(60, 144), hunters=("Samus",), require_particle_sampling=True)
        self.assertTrue(any("particle" in error for error in report()["errors"]))
        self.results[0]["particleSampling"] = {"samples": 10, "motionSamples": 0, "appearanceSamples": 0}
        self.results[1]["particleSampling"] = {"samples": 20, "motionSamples": 5, "appearanceSamples": 2}
        self.results[0]["beamSampling"] = {"draws": 10, "sampled": 0}
        self.results[1]["beamSampling"] = {"draws": 20, "sampled": 5}
        self.assertEqual(report()["errors"], [])
        self.results[1]["particleSampling"]["motionSamples"] = 0
        self.assertTrue(any("particle" in error for error in report()["errors"]))
        self.results[1]["particleSampling"]["motionSamples"] = 5
        self.results[0]["particleSampling"]["appearanceSamples"] = 1
        self.assertTrue(any("particle" in error for error in report()["errors"]))

    def test_native_sampling_counters_reject_missing_impossible_and_vacuous_beams(self):
        for rate, result in zip((60, 144), self.results):
            result["particleSampling"] = {"samples": 20, "motionSamples": 0 if rate == 60 else 5, "appearanceSamples": 0}
            result["beamSampling"] = {"draws": 20, "sampled": 0 if rate == 60 else 5}
        def report():
            return analyze(self.results, rates=(60, 144), hunters=("Samus",), require_particle_sampling=True)
        self.assertEqual(report()["errors"], [])
        for bad in ({}, {"draws": 0, "sampled": 0}, {"draws": 20, "sampled": 0},
                    {"draws": 20, "sampled": 21}, {"draws": 20, "sampled": True}):
            self.results[1]["beamSampling"] = bad
            self.assertTrue(any("beam" in error for error in report()["errors"]))
        self.results[1]["beamSampling"] = {"draws": 20, "sampled": 5}
        self.results[0]["beamSampling"] = {"draws": 20, "sampled": 1}
        self.assertTrue(any("beam" in error for error in report()["errors"]))
        self.results[0]["beamSampling"] = {"draws": 20, "sampled": 0}
        for field in ("motionSamples", "appearanceSamples"):
            self.results[1]["particleSampling"][field] = 21
            self.assertTrue(any("counters exceed" in error for error in report()["errors"]))
            self.results[1]["particleSampling"][field] = 5

    def test_original_identity_default_and_explicit_original_are_equivalent(self):
        self.results[1]["scenarioId"] = "native-pose-v1"
        report = self.report()
        self.assertEqual(report["errors"], [])
        self.assertEqual(report["scenarioId"], "native-pose-v1")

    def test_retreat_requires_one_explicit_scenario_throughout_matrix(self):
        for report in self.results:
            report["scenarioId"] = "native-pose-charge-retreat-v2"
        self.assertEqual(self.report()["errors"], [])
        self.assertEqual(analyze(self.results, rates=(60, 144), hunters=("Samus",),
                         scenario_id="native-pose-v1")["status"], "FAIL_OR_INCOMPLETE")
        self.results[0].pop("scenarioId")
        report = self.report()
        self.assertEqual(report["status"], "FAIL_OR_INCOMPLETE")
        self.assertEqual(report["comparisons"][0]["firstDifference"]["field"], "scenarioId")

    def test_scenarios_cannot_mix_between_hunters_or_use_unknown_identity(self):
        other = copy.deepcopy(self.results)
        for report in other:
            report.update(hunter="Sylux", scenarioId="native-pose-charge-retreat-v2")
        self.assertEqual(analyze(self.results + other, rates=(60, 144), hunters=("Samus", "Sylux"))
                         ["status"], "FAIL_OR_INCOMPLETE")
        for invalid in (None, "future-scenario", True, [], {}):
            with self.subTest(identity=invalid):
                self.results[1]["scenarioId"] = invalid
                self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def test_preserves_first_canonical_divergence(self):
        self.results[1]["steps"][19]["canonicalProjection"] = "d" * 64 + ":" + "b" * 64
        report = self.report()
        self.assertEqual(report["comparisons"][0]["firstDifference"], {"tick": 20, "field": "canonicalProjection"})
        self.assertEqual(report["status"], "FAIL_OR_INCOMPLETE")

    def test_actual_input_stream_required(self):
        self.results[1]["steps"][50]["consumedInput"] = "different"
        self.assertEqual(self.report()["comparisons"][0]["firstDifference"]["field"], "consumedInput")

    def test_truncated_stream_is_not_reported_as_prefix_divergence(self):
        self.results[1]["steps"] = self.results[1]["steps"][:379]
        self.results[1].update(complete=False, inputTimelineSha256="d" * 64)
        comparison = self.report()["comparisons"][0]
        self.assertTrue(comparison["commonPrefixCanonicalAndInputExact"])
        self.assertEqual(comparison["commonPrefixSteps"], 379)
        self.assertEqual(comparison["firstDifference"], {"tick": 380, "field": "missingStep"})
        self.assertFalse(comparison["exactCanonicalAndInputMatch"])

    def test_extra_anchors_remain_observations(self):
        self.results[1]["drawAnchorDiagnostics"][0]["sameDisplayTimeAnchorMatch"] = False
        report = self.report()
        self.assertEqual(report["errors"], [])
        self.assertEqual(report["drawAnchorObservations"][1]["observedAnchorDifferences"], ["synthetic"])

    def test_camera_changes_or_prior_observation_only_contract_cannot_pass(self):
        for mutation in (lambda r: r[1].pop("cameraControlAcceptance"),
                         lambda r: r[1]["cameraControlTransactions"].pop(),
                         lambda r: r[1]["cameraControlTransactions"][1].update(exactEstablishedProjectionMatch=False),
                         lambda r: r[1]["cameraControlTransactions"][1].update(exitProjection="d" * 64 + ":" + "b" * 64),
                         lambda r: r[1]["cameraControlTransactions"][1].update(differences=[{"field": "OwnTransform"}])):
            with self.subTest(mutation=mutation):
                self.results = [synthetic_result(rate) for rate in (60, 144)]
                mutation(self.results)
                self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def enable_synthetic_pixel_protocol(self):
        for report in self.results:
            report["pixelControlAcceptance"] = "ISOLATED_NATIVE_GUN_AND_CHARGE_REQUIRED"
            for picture in report["pictures"]:
                field = {300: "firstPersonGunPixels", 420: "chargeEffectPixels"}.get(picture["tick"])
                if field:
                    picture[field] = {"result": {"status": "PASS", "changedPixels": 10, "unstablePixels": 0,
                        "exactOwnerAndFlagsRestored": True, "sceneFrameBefore": picture["tick"],
                        "sceneFrameAfter": picture["tick"], "simulationStepsBefore": picture["tick"],
                        "simulationStepsAfter": picture["tick"], "alpha": .5,
                        "entryProjection": "a" * 64 + ":" + "b" * 64,
                        "exitProjection": "a" * 64 + ":" + "b" * 64,
                        "enabledPixelSha256": "a" * 64, "disabledPixelSha256": "b" * 64,
                        "restoredPixelSha256": "a" * 64}}

    def test_isolated_pixel_stage_requires_explicit_native_witnesses(self):
        self.assertEqual(analyze(self.results, rates=(60, 144), hunters=("Samus",),
                                require_pixel_controls=True)["status"], "FAIL_OR_INCOMPLETE")
        self.enable_synthetic_pixel_protocol()
        report = analyze(self.results, rates=(60, 144), hunters=("Samus",), require_pixel_controls=True)
        self.assertEqual(report["errors"], [])
        self.assertEqual(report["firstPersonGunAcceptance"], "ISOLATED_NATIVE_GUN_PIXELS_REQUIRED")

    def test_isolated_pixels_cannot_hide_missing_state_or_failed_restoration(self):
        for field, value in (("changedPixels", 0), ("changedPixels", True), ("unstablePixels", 1),
                             ("exactOwnerAndFlagsRestored", False), ("sceneFrameAfter", 301),
                             ("simulationStepsAfter", 301), ("alpha", float("nan")),
                             ("exitProjection", "d" * 64 + ":" + "b" * 64),
                             ("disabledPixelSha256", "a" * 64), ("restoredPixelSha256", "d" * 64)):
            with self.subTest(field=field):
                self.results = [synthetic_result(rate) for rate in (60, 144)]
                self.enable_synthetic_pixel_protocol()
                self.results[1]["pictures"][0]["firstPersonGunPixels"]["result"][field] = value
                self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def test_pixel_stage_cannot_mix_rates_or_omit_charge(self):
        self.enable_synthetic_pixel_protocol()
        self.results[1]["pictures"][1].pop("chargeEffectPixels")
        self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")
        self.results = [synthetic_result(rate) for rate in (60, 144)]
        self.enable_synthetic_pixel_protocol()
        self.results[1]["pixelControlAcceptance"] = "NOT_REQUESTED"
        self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def test_no_missing_failure_or_duplicate_result_pass(self):
        for mutation in (lambda r: r.pop(), lambda r: r.append(copy.deepcopy(r[0])),
                         lambda r: r[1].update(complete=False), lambda r: r[1].update(error="preserved failure")):
            with self.subTest(mutation=mutation):
                self.results = [synthetic_result(rate) for rate in (60, 144)]
                mutation(self.results)
                self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def test_no_bad_step_schedule_or_capture_pass(self):
        for mutation in (lambda r: r[1]["schedule"][0].update(actualSteps=True),
                         lambda r: r[1]["schedule"][0].update(alpha=float("nan")),
                         lambda r: r[1]["steps"].pop(),
                         lambda r: r[1]["pictures"][0].update(tick=301),
                         lambda r: r[1]["pictures"][0]["third"].update(changedBodyPixels=0),
                         lambda r: r[1]["lifetimeFences"][0].update(sampledPosition=[9, 9, 9]),
                         lambda r: r[1].update(assemblyMvid="other-binary")):
            with self.subTest(mutation=mutation):
                self.results = [synthetic_result(rate) for rate in (60, 144)]
                mutation(self.results)
                self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")

    def test_third_hash_cannot_expand_canonical_contract(self):
        self.results[0]["steps"][0]["canonicalProjection"] += ":" + "e" * 64
        self.assertTrue(any("established gameplay/presentation" in e for e in self.report()["errors"]))

    def test_malformed_result_rejected(self):
        self.results.extend([None, {"hunter": "Samus", "requestedOutputScheduleHz": None}])
        self.assertEqual(self.report()["status"], "FAIL_OR_INCOMPLETE")


if __name__ == "__main__":
    unittest.main()
