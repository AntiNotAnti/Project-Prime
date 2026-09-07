"""Offline authoritative update generation and integrity checks; no binary execution."""
import importlib.util
import json
from pathlib import Path
import stat
import struct
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).resolve().parents[1] / "server-update-package.py"
spec = importlib.util.spec_from_file_location("server_update_package", SCRIPT)
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class ServerUpdatePackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="fruity-update-artifacts-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.publish = self.root / "publish"
        self.publish.mkdir()
        self.output = self.root / "dist"

    def binary(self, rid):
        data = bytearray(256)
        if rid.startswith("linux-"):
            data[:6] = b"\x7fELF\x02\x01"
            struct.pack_into("<H", data, 16, 3)
            struct.pack_into("<H", data, 18, 62 if rid == "linux-x64" else 183)
        else:
            data[:2] = b"MZ"
            struct.pack_into("<I", data, 60, 64)
            data[64:68] = b"PE\0\0"
            struct.pack_into("<H", data, 68, 0x8664)
            struct.pack_into("<H", data, 86, 2)
            struct.pack_into("<H", data, 88, 0x20B)
            struct.pack_into("<H", data, 156, 3)
        (self.publish / package.RIDS[rid]).write_bytes(data)
        (self.publish / "SERVER.txt").write_text("authoritative server")
        (self.publish / "maps").mkdir(exist_ok=True)
        (self.publish / "maps/custom.fpmap").write_bytes(b"custom-map")

    def build(self, rid="linux-x64"):
        return package.build(self.publish, self.output, rid, "v2.1.0", "owned/authoritative")

    def test_round_trip_all_server_rids_and_exact_manifest_fields(self):
        for rid in package.RIDS:
            with self.subTest(rid=rid):
                publish = self.publish
                self.publish = self.root / rid
                self.publish.mkdir()
                self.binary(rid)
                manifest = self.build(rid)
                data = package.verify(manifest, self.output, rid, "v2.1.0")
                self.assertEqual(2, data["Family"])
                self.assertEqual(7, data["Protocol"])
                self.assertEqual("v2.1.0", data["Version"])
                self.assertEqual(rid, data["Rid"])
                self.assertEqual(package.RIDS[rid], data["Executable"])
                self.assertEqual(3, len(data["Files"]))
                self.assertEqual(data["PackageBytes"], (self.output / data["Package"]).stat().st_size)
                with zipfile.ZipFile(self.output / data["Package"]) as archive:
                    self.assertEqual({file["Path"] for file in data["Files"]}, set(archive.namelist()))
                    self.assertFalse(any(item.is_dir() for item in archive.infolist()))
                self.publish = publish

    def test_generated_zip_and_manifest_are_deterministic(self):
        self.binary("linux-x64")
        first = self.build()
        first_manifest = first.read_bytes()
        archive_name = json.loads(first_manifest)["Package"]
        first_zip = (self.output / archive_name).read_bytes()
        self.output = self.root / "second"
        second = self.build()
        self.assertEqual(first_manifest, second.read_bytes())
        self.assertEqual(first_zip, (self.output / archive_name).read_bytes())

    def test_wrong_identity_version_and_manifest_hash_rejected(self):
        self.binary("linux-x64")
        manifest = self.build()
        original = json.loads(manifest.read_text())
        for key, invalid in [("Format", True), ("Family", 1), ("Protocol", 5), ("Version", "v2.0.0"), ("Rid", "linux-arm64"),
                             ("PackageSha256", "0" * 64), ("PackageBytes", 1), ("Executable", "other")]:
            with self.subTest(key=key):
                data = dict(original)
                data[key] = invalid
                manifest.write_text(json.dumps(data))
                with self.assertRaises(ValueError):
                    package.verify(manifest, self.output, "linux-x64", "v2.1.0")
        manifest.write_text(json.dumps(original))
        original["Files"][0]["Sha256"] = "0" * 64
        manifest.write_text(json.dumps(original))
        with self.assertRaisesRegex(ValueError, "file hash mismatch"):
            package.verify(manifest, self.output, "linux-x64", "v2.1.0")

    def test_corrupt_archive_rejected(self):
        self.binary("linux-x64")
        manifest = self.build()
        data = json.loads(manifest.read_text())
        with (self.output / data["Package"]).open("ab") as archive:
            archive.write(b"trailing corruption")
        with self.assertRaisesRegex(ValueError, "size/hash mismatch"):
            package.verify(manifest, self.output, "linux-x64", "v2.1.0")

    def test_unexpected_publish_files_and_assets_rejected(self):
        self.binary("linux-x64")
        for name in ["paths.txt", "AMHE1/arm9.bin", "cartridge.nds", ".DS_Store", "helper.exe", "maps/private.png", "nested/runtime.dll"]:
            with self.subTest(name=name):
                extra = self.publish / name
                extra.parent.mkdir(exist_ok=True)
                extra.write_bytes(b"unexpected")
                with self.assertRaises(ValueError):
                    self.build()
                extra.unlink()
        self.assertFalse(self.output.exists())

    def test_missing_binary_wrong_architecture_and_gui_executable_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing"):
            self.build()
        self.binary("linux-x64")
        with self.assertRaisesRegex(ValueError, "architecture"):
            self.build("linux-arm64")
        (self.publish / "FruityPrimeServer").unlink()
        self.binary("win-x64")
        binary = self.publish / "FruityPrimeServer.exe"
        data = bytearray(binary.read_bytes())
        struct.pack_into("<H", data, 156, 2)
        binary.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "console"):
            self.build("win-x64")

    def test_links_duplicate_case_and_unsafe_paths_rejected(self):
        self.binary("linux-x64")
        link = self.publish / "alias.dll"
        link.symlink_to(self.publish / "FruityPrimeServer")
        with self.assertRaisesRegex(ValueError, "link"):
            self.build()
        link.unlink()
        (self.publish / "example.dll").write_bytes(b"one")
        (self.publish / "EXAMPLE.dll").write_bytes(b"two")
        # Case insensitive filesystems cannot create both names; the archive
        # validator independently rejects duplicate case across all platforms.
        if len(list(self.publish.glob("*.dll"))) == 2:
            with self.assertRaisesRegex(ValueError, "duplicate"):
                self.build()
        for path in ["../evil.dll", "/absolute", "a/../b", "C:bad", "a\\b", "maps//x.json", "NUL.dll", "a. ", "a\n"]:
            with self.subTest(path=path), self.assertRaises(ValueError):
                package.safe_path(path)

    def test_manifest_traversal_duplicate_and_extra_archive_entries_rejected(self):
        self.binary("linux-x64")
        manifest = self.build()
        original = json.loads(manifest.read_text())
        for path in ["../outside.dll", "/absolute", "nested/runtime.dll"]:
            data = json.loads(json.dumps(original))
            data["Files"][0]["Path"] = path
            manifest.write_text(json.dumps(data))
            with self.assertRaises(ValueError):
                package.verify(manifest, self.output, "linux-x64", "v2.1.0")
        data = json.loads(json.dumps(original))
        data["Files"].append(data["Files"][0])
        manifest.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, "manifest file"):
            package.verify(manifest, self.output, "linux-x64", "v2.1.0")
        archive_path = self.output / original["Package"]
        with zipfile.ZipFile(archive_path, "a") as archive:
            archive.writestr("extra.dll", "not in manifest")
        original["PackageBytes"], original["PackageSha256"] = package.sha_file(archive_path, package.PACKAGE_LIMIT)
        manifest.write_text(json.dumps(original))
        with self.assertRaisesRegex(ValueError, "entries differ"):
            package.verify(manifest, self.output, "linux-x64", "v2.1.0")

    def test_declared_expansion_bounds_rejected(self):
        self.binary("linux-x64")
        manifest = self.build()
        data = json.loads(manifest.read_text())
        data["Files"][0]["Bytes"] = package.EXPANDED_LIMIT + 1
        manifest.write_text(json.dumps(data))
        with self.assertRaises(ValueError):
            package.verify(manifest, self.output, "linux-x64", "v2.1.0")

    def test_relay_repository_unstamped_version_and_overwrite_refused(self):
        self.binary("linux-x64")
        with self.assertRaisesRegex(ValueError, "authoritative fork"):
            package.build(self.publish, self.output, "linux-x64", "v2.1.0", "liveteklol/Fruity-Prime")
        for tag in ["2.1.0", "v1.0.0", "v2.1.0-rc1", "v2.1.0/other"]:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                package.build(self.publish, self.output, "linux-x64", tag, "owned/authoritative")
        self.build()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.build()


if __name__ == "__main__":
    unittest.main()
