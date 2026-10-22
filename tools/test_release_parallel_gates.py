#!/usr/bin/env python3
"""Preserve the short release critical path without weakening publish gates."""
from pathlib import Path
import re
import unittest


WORKFLOW = (Path(__file__).resolve().parents[1] / ".github/workflows/release.yml").read_text()


def job(name: str) -> str:
    match = re.search(
        r"(?ms)^  " + re.escape(name) + r":\n(.*?)(?=^  [a-z][a-z0-9-]*:\n|\Z)",
        WORKFLOW,
    )
    if match is None:
        raise AssertionError(f"Missing required release job: {name}")
    return match.group(1)


class ReleaseParallelGateTests(unittest.TestCase):
    def test_preparation_keeps_tagged_asset_and_map_safety(self):
        preparation = job("prepare-release")
        self.assertIn("needs: resolve", preparation)
        self.assertIn("refs/tags/${{ needs.resolve.outputs.tag }}", preparation)
        self.assertIn("tools/check-no-game-assets.sh", preparation)
        self.assertIn("tools/check-opengl-only.sh", preparation)
        self.assertIn("tools/check-maps-shipped.sh", preparation)
        self.assertIn("name: prepared-maps", preparation)
        self.assertNotIn("-- --lobby", preparation)

    def test_lobby_suite_runs_independently_of_map_preparation(self):
        lobby = job("release-lobby-acceptance")
        self.assertRegex(lobby, r"(?m)^    needs: resolve$")
        self.assertIn("refs/tags/${{ needs.resolve.outputs.tag }}", lobby)
        self.assertIn(
            "dotnet run --project tools/nettest/nettest.csproj -c Release -- --lobby",
            lobby,
        )
        self.assertNotIn("prepared-maps", lobby)

    def test_macos_does_not_repeat_source_guard_but_still_checks_packages(self):
        macos = job("macos-release")
        self.assertIn("needs: [resolve, prepare-release]", macos)
        self.assertIn("tools/check-maps-shipped.sh", macos)
        self.assertIn('tools/check-no-game-assets.sh "publish/$RID"', macos)
        self.assertNotIn("tools/check-no-game-assets.sh\n", macos)
        self.assertNotIn("tools/check-opengl-only.sh", macos)
        self.assertIn("tools/test-macos-tools.sh", macos)
        self.assertIn("tools/package-macos.sh", macos)

    def test_server_packages_copy_existing_documentation(self):
        # A cleanup moved the guide under docs/, while the release archives
        # still promise SERVER.md in the package root. Resolve the actual
        # tracked source before the expensive release builders begin.
        source = Path(__file__).resolve().parents[1] / "docs" / "SERVER.md"
        self.assertTrue(source.is_file(), f"Missing server guide: {source}")
        package = job("package-release")
        for target in ("linux-arm64", "linux-x64-server", "win-x64-server"):
            with self.subTest(target=target):
                self.assertIn(
                    f"cp docs/SERVER.md publish/{target}/SERVER.md", package
                )
        self.assertNotRegex(package, r"(?m)^\s*cp SERVER\.md\b")

    def test_publish_still_requires_all_releases_and_lobby_acceptance(self):
        publish = job("publish")
        match = re.search(r"(?m)^    needs: \[([^\]]+)\]$", publish)
        self.assertIsNotNone(match)
        needs = {name.strip() for name in match.group(1).split(",")}
        self.assertTrue(
            {
                "resolve",
                "package-release",
                "release-android",
                "macos-release",
                "v47-release-updater-acceptance",
                "release-lobby-acceptance",
            }.issubset(needs)
        )
        self.assertIn("gh release", publish)


if __name__ == "__main__":
    unittest.main()
