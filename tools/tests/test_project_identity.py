"""Focused tests for the tracked Project Prime identity guard."""
from pathlib import Path
import importlib.util
from tempfile import TemporaryDirectory
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/check-project-identity.py"
SPEC = importlib.util.spec_from_file_location("check_project_identity", SCRIPT)
GUARD = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(GUARD)


class ProjectIdentityGuardTests(unittest.TestCase):
    def test_repository_identity_passes(self):
        self.assertEqual(GUARD.inspect(ROOT), [])

    def test_retired_product_and_owner_names_are_detected(self):
        product = "fru" + "ity" + "prime"
        owner = "li" + "ve" + "te" + "k"
        self.assertTrue(GUARD.scan_text(f"{product} {owner}", "fixture"))

    def test_franchise_name_is_allowed(self):
        franchise = "met" + "roid" + " " + "pri" + "me" + " " + "hun" + "ters"
        self.assertEqual(GUARD.scan_text(franchise, "fixture"), [])

    def test_standalone_project_brand_is_detected(self):
        standalone = "pri" + "me" + " " + "hun" + "ters"
        self.assertTrue(GUARD.scan_text(standalone, "fixture"))

    def test_readme_credits_allow_only_exact_mentions(self):
        live_tek = "Live" + "Tek"
        predecessor = "Fru" + "ity" + "Pri" + "me"
        readme = (
            "# Project Prime\n\n"
            "## Credits\n\n"
            f"{live_tek} created the project this work grew from; "
            f"{predecessor} is the predecessor project and multiplayer foundation.\n\n"
            f"Support: {GUARD.SUPPORT_URL}\n\n"
            "## License\n"
        )
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "README.md").write_text(readme, encoding="utf-8")
            errors, allowed_ranges = GUARD._readme_credit_contract(root)
        self.assertEqual([], errors)
        self.assertEqual([], GUARD.scan_text(readme, "README.md", allowed_ranges=allowed_ranges))

    def test_readme_credit_names_outside_exact_section_are_rejected(self):
        live_tek = "Live" + "Tek"
        predecessor = "Fru" + "ity" + "Pri" + "me"
        readme = (
            f"{live_tek} outside credits\n\n"
            "## Credits\n\n"
            f"{live_tek} created the project this work grew from; "
            f"{predecessor} is the predecessor project and multiplayer foundation.\n\n"
            f"Support: {GUARD.SUPPORT_URL}\n"
        )
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "README.md").write_text(readme, encoding="utf-8")
            contract_errors, allowed_ranges = GUARD._readme_credit_contract(root)
        scan_errors = GUARD.scan_text(readme, "README.md", allowed_ranges=allowed_ranges)
        self.assertEqual([], contract_errors)
        self.assertTrue(scan_errors)

    def test_readme_credit_section_does_not_exempt_other_retired_variants(self):
        live_tek = "Live" + "Tek"
        predecessor = "Fru" + "ity" + "Pri" + "me"
        old_variant = "Fru" + "ity" + " " + "Pri" + "me"
        readme = (
            "## Credits\n\n"
            f"{live_tek} created the project this work grew from; "
            f"{predecessor} is the predecessor project and multiplayer foundation.\n"
            f"Unrelated text must not use {old_variant}.\n\n"
            f"Support: {GUARD.SUPPORT_URL}\n"
        )
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "README.md").write_text(readme, encoding="utf-8")
            contract_errors, allowed_ranges = GUARD._readme_credit_contract(root)
        scan_errors = GUARD.scan_text(readme, "README.md", allowed_ranges=allowed_ranges)
        self.assertEqual([], contract_errors)
        self.assertTrue(scan_errors)

    def test_handoff_compatibility_contract_is_exact(self):
        errors, allowed_ranges = GUARD._handoff_contract(ROOT)
        self.assertEqual([], errors)
        self.assertEqual(3, len(allowed_ranges["deploy-server.sh"]))
        self.assertIn("tools/systemd/projectprime-stack.service", allowed_ranges)
        self.assertEqual(1, len(allowed_ranges["tools/systemd/projectprime-stack.service"]))

    def test_external_reference_contract_is_exact(self):
        errors, allowed_ranges = GUARD._reference_contract(ROOT)
        self.assertEqual([], errors)
        self.assertEqual(1, len(allowed_ranges))


if __name__ == "__main__":
    unittest.main()
