import hashlib
import io
import json
import subprocess
import tarfile
import tempfile
import unittest
import zipfile
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools" / "update-release.py"


class UpdateReleaseTests(unittest.TestCase):
    def test_release_files_are_normalized_and_hashed(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "win"
            directory.mkdir()
            (directory / "ProjectPrime.exe").write_bytes(b"binary")
            (directory / "paths.txt").write_bytes(b"player-owned template")
            (directory / "saves").mkdir()
            (directory / "saves" / "slot.dat").write_bytes(b"player-owned save")
            (directory / "files").mkdir()
            (directory / "files" / "AMHE1.bin").write_bytes(b"cartridge-owned data")
            (directory / "content").mkdir()
            (directory / "content" / "community.dat").write_bytes(b"community content")
            (directory / "maps").mkdir()
            (directory / "maps" / "official.fpmap").write_bytes(b"map")
            subprocess.run(
                ["python3", str(TOOL), "--version", "1.2.3", "--output", str(Path(temporary) / "out"),
                 "--desktop-dir", str(directory)], check=True,
            )
            manifest = json.loads((directory / "release-files.json").read_text())
            self.assertEqual(1, manifest["schemaVersion"])
            self.assertEqual(
                hashlib.sha256(b"binary").hexdigest(),
                next(item["sha256"] for item in manifest["files"] if item["path"] == "ProjectPrime.exe"),
            )
            managed_paths = {item["path"].casefold() for item in manifest["files"]}
            self.assertNotIn("paths.txt", managed_paths)
            self.assertFalse(any(path.startswith("saves/") for path in managed_paths))
            self.assertFalse(any(path.startswith("files/") for path in managed_paths))
            self.assertFalse(any(path.startswith("content/") for path in managed_paths))

    def test_contract_requires_all_player_rids_and_rejects_missing_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "dist"
            directory.mkdir()
            for rid, suffix in {
                "win-x64": ".zip", "linux-x64": ".tar.gz", "osx-x64": ".tar.gz",
                "osx-arm64": ".tar.gz", "android": ".apk",
            }.items():
                package = directory / f"ProjectPrime-v1.2.3-{rid}{suffix}"
                if rid == "android":
                    package.write_bytes(rid.encode())
                    continue
                self._write_desktop_archive(package, rid)
            key = Path(temporary) / "key.pem"
            public = Path(temporary) / "public.pem"
            subprocess.run(["openssl", "ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", str(key)], check=True)
            subprocess.run(["openssl", "pkey", "-in", str(key), "-pubout", "-out", str(public)], check=True)
            output = Path(temporary) / "contract"
            subprocess.run(
                ["python3", str(TOOL), "--version", "1.2.3", "--dist", str(directory),
                 "--output", str(output), "--signing-key", str(key), "--public-key", str(public)], check=True,
            )
            self.assertTrue((output / "update-manifest.json").is_file())
            self.assertTrue((output / "update-manifest.sig").is_file())
            manifest = json.loads((output / "update-manifest.json").read_text())
            self.assertNotEqual("1970-01-01T00:00:00Z", manifest["publishedUtc"])
            self.assertEqual(
                datetime.fromisoformat(manifest["publishedUtc"].replace("Z", "+00:00")).tzinfo,
                timezone.utc,
            )
            self.assertTrue(manifest["publishedUtc"].endswith("Z"))
            subprocess.run(
                ["python3", str(TOOL), "--verify-manifest", str(output / "update-manifest.json"),
                 "--signature", str(output / "update-manifest.sig"),
                 "--public-key", str(public), "--asset-dir", str(directory)],
                check=True,
            )

            # The contract must inspect archive contents, not merely hash an
            # arbitrary byte blob. A modified release-files hash is rejected
            # before a manifest can be signed.
            package = directory / "ProjectPrime-v1.2.3-win-x64.zip"
            damaged = Path(temporary) / "damaged.zip"
            with zipfile.ZipFile(package) as source, zipfile.ZipFile(
                damaged, "w", zipfile.ZIP_DEFLATED
            ) as target:
                for info in source.infolist():
                    content = source.read(info.filename)
                    if info.filename == "release-files.json":
                        metadata = json.loads(content)
                        metadata["files"][0]["sha256"] = "f" * 64
                        content = json.dumps(metadata, separators=(",", ":")).encode()
                    target.writestr(info, content)
            damaged.replace(package)
            damaged_result = subprocess.run(
                ["python3", str(TOOL), "--version", "1.2.3", "--dist", str(directory),
                 "--output", str(output), "--signing-key", str(key), "--public-key", str(public)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False,
            )
            self.assertNotEqual(0, damaged_result.returncode)
            self.assertIn("hash mismatch", damaged_result.stderr)

            self._write_desktop_archive(package, "win-x64")
            (directory / "ProjectPrime-v1.2.3-android.apk").unlink()
            failed = subprocess.run(
                ["python3", str(TOOL), "--version", "1.2.3", "--dist", str(directory),
                 "--output", str(output), "--signing-key", str(key), "--public-key", str(public)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False,
            )
            self.assertNotEqual(0, failed.returncode)
            self.assertIn("android", failed.stderr)

    def test_verify_manifest_assets_rejects_signed_hash_change(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dist = root / "dist"
            dist.mkdir()
            for rid, suffix in {
                "win-x64": ".zip", "linux-x64": ".tar.gz", "osx-x64": ".tar.gz",
                "osx-arm64": ".tar.gz", "android": ".apk",
            }.items():
                package = dist / f"ProjectPrime-v2.0.0-{rid}{suffix}"
                if rid == "android":
                    package.write_bytes(b"signed-apk")
                else:
                    self._write_desktop_archive(package, rid, version="2.0.0")
            key = root / "key.pem"
            public = root / "public.pem"
            subprocess.run(["openssl", "ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", str(key)], check=True)
            subprocess.run(["openssl", "pkey", "-in", str(key), "-pubout", "-out", str(public)], check=True)
            output = root / "contract"
            subprocess.run(
                ["python3", str(TOOL), "--version", "2.0.0", "--dist", str(dist),
                 "--output", str(output), "--signing-key", str(key), "--public-key", str(public)],
                check=True,
            )
            original_android = (dist / "ProjectPrime-v2.0.0-android.apk").read_bytes()
            (dist / "ProjectPrime-v2.0.0-android.apk").write_bytes(
                bytes((original_android[0] ^ 0x01,)) + original_android[1:]
            )
            result = subprocess.run(
                ["python3", str(TOOL), "--verify-manifest", str(output / "update-manifest.json"),
                 "--signature", str(output / "update-manifest.sig"),
                 "--public-key", str(public), "--asset-dir", str(dist)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False,
            )
            self.assertNotEqual(0, result.returncode)
            self.assertIn("hash mismatch", result.stderr)

    @staticmethod
    def _write_desktop_archive(package: Path, rid: str, version: str = "1.2.3") -> None:
        executable = "ProjectPrime.exe" if rid == "win-x64" else "ProjectPrime"
        binary = b"binary-" + rid.encode()
        metadata = {
            "schemaVersion": 1,
            "version": version,
            "files": [{
                "path": executable,
                "sha256": hashlib.sha256(binary).hexdigest(),
            }],
        }
        if package.suffix == ".zip":
            with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
                archive.writestr(executable, binary)
                archive.writestr("paths.txt", b"player template")
                archive.writestr("release-files.json", json.dumps(
                    metadata, separators=(",", ":")).encode())
        else:
            with tarfile.open(package, "w:gz") as archive:
                for name, content in (
                    (executable, binary),
                    ("paths.txt", b"player template"),
                    ("release-files.json", json.dumps(
                        metadata, separators=(",", ":")).encode()),
                ):
                    info = tarfile.TarInfo("./" + name)
                    info.size = len(content)
                    archive.addfile(info, io.BytesIO(content))


if __name__ == "__main__":
    unittest.main()
