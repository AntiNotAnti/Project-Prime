"""Focused false-green checks: python -m unittest test_net4_evidence."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from net4_evidence import REASONS, summarize


class Net4EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name)
        self.key = dict(AuthorityEpoch=1, MatchId=2, ShooterSlot=0, Generation=3, LifeId=4, ShotId=5)
        self.row = dict(Stage="arrival", Key=self.key, ClaimId=6, ComponentId=1,
            VictimSlot=1, VictimGeneration=7, VictimLifeId=8, Beam=0, Flags=1, Damage=10,
            AckFrame=50, LaunchFrame=49, AuthorityFrame=60, Legacy=0, Status="Unavailable",
            Reason="SourceAimUnavailable", Family="Projectile", Source="Verified",
            Geometry="SourceAimUnavailable", PhysicalMatch=False, BodyOffset=0.5)
        reasons = [0] * len(REASONS)
        reasons[REASONS.index("SourceAimUnavailable")] = 2
        self.proof = dict(Mode="Shadow", Verified=0, Rejected=0, Unavailable=2,
            LegacyAllowedRejected=0, LegacyAllowedUnavailable=2, CapacityRefused=0,
            EvidenceOverflow=0, PhysicalDuplicates=0, RadiusSamples=2, RadiusSum=1,
            RadiusMax=0.5, RadiusBuckets=[0, 0, 2, 0, 0, 0, 0, 0, 0],
            ReasonNames=REASONS, Reasons=reasons,
            Rows=[self.row, dict(self.row, Stage="settlement", AuthorityFrame=100)],
            EmissionVerified=0, EmissionRejected=0, EmissionUnavailable=1,
            EmissionReasons=[1 if reason == "SourceAimUnavailable" else 0 for reason in REASONS],
            EmissionRows=[dict(Key=self.key, AuthorityFrame=59, Status="Unavailable", Reason="SourceAimUnavailable")])
        self.server = dict(Overflow=0, Protocol=44, Version="fixture", ShotProof=self.proof)
        self.manifest = dict(assemblySha256="a" * 64, assemblyAfterSha256="a" * 64,
            harnessSha256={"run-net2.py": "b" * 64, "net2_evidence.py": "c" * 64}, source="fixture-source")

    def write(self):
        (self.out / "server.json").write_text(json.dumps(self.server), encoding="utf-8")
        (self.out / "manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def result(self):
        self.write()
        return summarize(self.out)

    def test_complete_instrumentation_never_establishes_readiness(self):
        result = self.result()
        self.assertEqual(result["instrumentationStatus"], "PASS", result)
        self.assertEqual(result["enforcementReadiness"], "NOT_ESTABLISHED")
        self.assertEqual(result["honestFalseRejectRate"], "NOT_ESTABLISHED")
        self.assertEqual(result["uniqueClaims"]["count"], 1)
        self.assertEqual(result["radiusObservations"]["samples"], 2)

    def test_missing_row_counter_correspondence_fails(self):
        self.proof["Rows"].pop()
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")

    def test_capacity_and_overflow_fail(self):
        for field in ("CapacityRefused", "EvidenceOverflow"):
            with self.subTest(field=field):
                self.proof[field] = 1
                self.assertEqual(self.result()["instrumentationStatus"], "FAIL")
                self.proof[field] = 0

    def test_reason_order_and_counts_fail(self):
        self.proof["ReasonNames"] = list(reversed(REASONS))
        self.proof["Reasons"][0] = 1
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")

    def test_verified_needs_physical_source_and_geometry(self):
        for row in self.proof["Rows"]:
            row.update(Status="Verified", Reason="Verified", Source="Verified", Geometry="Verified")
        self.proof.update(Verified=2, Unavailable=0, LegacyAllowedUnavailable=0,
                          Reasons=[2] + [0] * (len(REASONS) - 1))
        result = self.result()
        self.assertEqual(result["instrumentationStatus"], "FAIL")
        self.assertFalse(result["allVerifiedHavePhysicalMatch"])

    def test_verified_complete_physical_observation_passes(self):
        for row in self.proof["Rows"]:
            row.update(Status="Verified", Reason="Verified", Source="Verified", Geometry="Verified", PhysicalMatch=True)
        self.proof.update(Verified=2, Unavailable=0, LegacyAllowedUnavailable=0,
                          Reasons=[2] + [0] * (len(REASONS) - 1))
        self.assertEqual(self.result()["instrumentationStatus"], "PASS")

    def test_emission_counter_correspondence_fails(self):
        self.proof["EmissionReasons"][0] = 1
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")

    def test_source_geometry_and_victim_identity_required(self):
        del self.row["VictimLifeId"]
        self.assertNotEqual(self.result()["instrumentationStatus"], "PASS")

    def test_missing_emission_lane_is_not_a_pass(self):
        del self.proof["EmissionRows"]
        self.assertNotEqual(self.result()["instrumentationStatus"], "PASS")

    def test_repeated_claim_must_retain_component_and_victim(self):
        self.proof["Rows"][1]["ComponentId"] = 2
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")

    def test_off_instrumentation_is_incomplete(self):
        self.proof["Mode"] = "Off"
        self.assertEqual(self.result()["instrumentationStatus"], "INCOMPLETE")

    def test_missing_identity_is_incomplete(self):
        self.manifest.pop("harnessSha256")
        self.assertEqual(self.result()["instrumentationStatus"], "INCOMPLETE")

    def test_changed_binary_and_radius_fail(self):
        self.manifest["assemblyAfterSha256"] = "d" * 64
        self.proof["RadiusSamples"] = 1
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")

    def test_cli_is_read_only_and_outputs_json(self):
        self.write()
        before = {p.name: p.read_bytes() for p in self.out.iterdir()}
        script = Path(__file__).with_name("net4_evidence.py")
        process = subprocess.run([sys.executable, str(script), str(self.out)], capture_output=True, text=True)
        self.assertEqual(process.returncode, 0, process.stderr)
        self.assertEqual(json.loads(process.stdout)["instrumentationStatus"], "PASS")
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.out.iterdir()})

    def test_nonfinite_json_reports_failure_without_crashing(self):
        self.proof["RadiusSum"] = float("nan")
        self.assertEqual(self.result()["instrumentationStatus"], "FAIL")


if __name__ == "__main__":
    unittest.main()
