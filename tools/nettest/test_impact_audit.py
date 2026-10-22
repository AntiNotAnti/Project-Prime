import copy
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("impact_audit", Path(__file__).with_name("impact-audit.py"))
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


def source(**changes):
    names = "epoch match shooter generation life shot component phase victim vg vl damage frame weapon flags provenance x y z".split()
    values = dict(zip(names, (123, 2, 0, 3, 4, 0xffffffff, 0xffffffff, 0xffffffff, 1, 5, 6, 65535, 0xffffffff, 8, 32, 1, 1., 2., 3.)))
    values.update(changes)
    return struct.pack("<QHBHHIIIBHHHIBBB3f", *(values[n] for n in names)).hex()


def report(published=(), accepted=(), enabled=True):
    return dict(ConfirmedImpacts=dict(Enabled=enabled, Published=len(published), Accepted=len(accepted),
        Duplicates=0, Rejected=0, Overflow=0, EvidenceOverflow=0, CueDrawSubmissions=0,
        ExactProjectileDrawSubmissions=0, PublishedFacts=list(published), AcceptedFacts=list(accepted)))


class ImpactAuditTests(unittest.TestCase):
    def setUp(self):
        self.fact = source()
        self.server = report(published=[self.fact, source(damage=1)])
        self.peers = {name: report(accepted=[self.fact]) for name in ("peer-0", "peer-1")}
        self.summary = dict(status="PASS", coverage=dict(boundedHeadlessDrain="PASS"),
                            processes=[dict(name=n) for n in ("server", *self.peers)])
        self.manifest = dict(settings=dict(confirmed_impacts=True))

    def audit(self): return module.audit(self.server, self.peers, self.summary, self.manifest)

    def test_positive_wrap_missing_unreliable_delivery_and_draw_counts(self):
        self.peers["peer-0"]["ConfirmedImpacts"]["CueDrawSubmissions"] = 1234
        result = self.audit()
        self.assertEqual(result["status"], "PASS", result)
        self.assertEqual(result["metrics"]["peers"]["peer-0"]["missingDeliveryCount"], 1)
        self.assertNotIn("pixel", " ".join(result["metrics"]))

    def test_missing_authority_or_expected_peer_is_incomplete(self):
        self.server = {}; self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.peers.pop("peer-1"); self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_missing_live_sources_drain_or_manifest_is_incomplete(self):
        self.peers["peer-1"] = report(); self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.summary["coverage"] = {}; self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.manifest = {}; self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_forged_exact_source_or_identity_fails(self):
        for changes in (dict(x=1.5), dict(epoch=124), dict(life=5), dict(component=3), dict(frame=42)):
            with self.subTest(changes=changes):
                self.setUp(); self.peers["peer-0"] = report(accepted=[source(**changes)])
                self.assertEqual(self.audit()["status"], "FAIL")

    def test_duplicate_identity_even_changed_point_fails(self):
        other = source(x=4)
        self.server = report(published=[self.fact, other])
        self.peers["peer-0"] = report(accepted=[self.fact, other])
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_codec_adversarial_fields_fail_even_if_authority_matches(self):
        for changes in (dict(epoch=0), dict(match=0), dict(shooter=8), dict(generation=0), dict(life=0),
                        dict(victim=8), dict(vg=0), dict(vl=0), dict(damage=0), dict(weapon=254),
                        dict(provenance=0), dict(provenance=4), dict(x=float("nan")), dict(y=float("inf")),
                        dict(z=-100000), dict(flags=0), dict(shot=0)):
            with self.subTest(changes=changes):
                self.setUp(); invalid = source(**changes)
                self.server = report(published=[invalid]); self.peers["peer-0"] = report(accepted=[invalid])
                self.assertEqual(self.audit()["status"], "FAIL")

    def test_truncated_extra_invalid_hex_fail(self):
        for raw in (self.fact[:-2], self.fact + "00", "zz" * 53):
            with self.subTest(raw=raw):
                self.setUp(); self.peers["peer-0"] = report(accepted=[raw])
                self.assertEqual(self.audit()["status"], "FAIL")

    def test_unknown_projectile_body_anchor_and_component_zero_are_explicit(self):
        anchor = source(shot=0, component=0, phase=0, flags=0, weapon=255, provenance=3)
        self.server = report(published=[anchor]); self.peers = {n: report(accepted=[anchor]) for n in self.peers}
        self.assertEqual(self.audit()["status"], "PASS")

    def test_counts_overflow_and_authority_roles_fail(self):
        for field, value in (("Accepted", 2), ("Accepted", True), ("Rejected", -1),
                             ("Overflow", 1), ("EvidenceOverflow", 1), ("Enabled", False)):
            with self.subTest(field=field):
                self.setUp(); self.peers["peer-0"]["ConfirmedImpacts"][field] = value
                self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.server = report(published=[self.fact], accepted=[self.fact])
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.peers["peer-0"] = report(published=[self.fact], accepted=[self.fact])
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_off_control_and_off_work_rejected(self):
        self.manifest["settings"]["confirmed_impacts"] = False
        self.server = report(enabled=False); self.peers = {n: report(enabled=False) for n in self.peers}
        self.assertEqual(self.audit()["status"], "PASS")
        self.peers["peer-0"]["ConfirmedImpacts"]["CueDrawSubmissions"] = 1
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_base_failure_retained_and_duplicate_rejection_counter_not_success(self):
        self.peers["peer-0"]["ConfirmedImpacts"]["Duplicates"] = 4
        self.assertEqual(self.audit()["status"], "PASS")
        self.summary["status"] = "FAIL"; self.assertEqual(self.audit()["status"], "FAIL")


if __name__ == "__main__": unittest.main()
