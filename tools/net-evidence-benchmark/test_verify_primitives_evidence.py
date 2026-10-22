"""Portable evidence falsifiers; no build/native/performance execution."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))
import verify_evidence

PACKAGE = Path(__file__).resolve().parents[2] / "docs/evidence/net-campaign-2026-10-14/primitive-overhead-7588d986"


def check(path):
    with mock.patch.object(sys, "argv", ["verify_evidence.py", "--evidence", str(path)]), contextlib.redirect_stdout(io.StringIO()):
        verify_evidence.main()


def rewrite_derivative(base, relative, mutate):
    path = base / relative
    data = json.loads(path.read_text()); mutate(data["payload"])
    path.write_text(json.dumps(data, indent=2) + "\n")
    content = path.read_bytes(); manifest_path = base / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    for artifact in manifest["derivedFiles"]:
        if artifact["relativePath"] == relative:
            artifact["sha256"] = hashlib.sha256(content).hexdigest(); artifact["bytes"] = len(content)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")


class PortablePrimitiveEvidenceTests(unittest.TestCase):
    def test_all_360_measured_records_and_negative_scope_verify(self):
        check(PACKAGE)

    def test_negative_attempt_cannot_be_promoted_to_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp) / "evidence"; shutil.copytree(PACKAGE, base)
            rewrite_derivative(base, "attempt1/receipt.sanitized.json", lambda payload: payload.update(success=True))
            with self.assertRaises(AssertionError): check(base)

    def test_failed_attempt_samples_cannot_be_removed_after_rehashing(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp) / "evidence"; shutil.copytree(PACKAGE, base)
            rewrite_derivative(base, "attempt1/batch-lines.sanitized.json", lambda payload: payload["lines"].pop())
            with self.assertRaises(AssertionError): check(base)

    def test_observed_nonzero_allocations_cannot_be_normalized_to_zero(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp) / "evidence"; shutil.copytree(PACKAGE, base)
            def normalize(payload):
                rows = next(case["Raw"] for case in payload["Cases"] if case["Name"] == "typed_claim_payment_in_page")
                next(row for row in rows if row["CallerAllocatedBytes"] > 0)["CallerAllocatedBytes"] = 0
            rewrite_derivative(base, "attempt2/results.sanitized.json", normalize)
            with self.assertRaises(AssertionError): check(base)


if __name__ == "__main__": unittest.main()
