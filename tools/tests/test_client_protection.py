"""Focused tests for the release-only client protection infrastructure."""

import importlib.util
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[2]
GENERATOR = ROOT / "tools/protection/generate-obfuscar-config.py"
CHECKER_PATH = ROOT / "tools/protection/check-obfuscation.py"
SPEC = importlib.util.spec_from_file_location("check_obfuscation", CHECKER_PATH)
CHECKER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = CHECKER
SPEC.loader.exec_module(CHECKER)


class ClientProtectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="prime-protection-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_generator_emits_absolute_policy_and_xaml_preserves(self):
        source = self.root / "source"
        (source / "src/Client").mkdir(parents=True)
        (source / "src/Client/View.axaml").write_text(
            '<UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" '
            'xmlns:prime="clr-namespace:Prime.Ui" '
            'xmlns:game="clr-namespace:Prime.GameUi;assembly=ProjectPrime.Game" '
            'x:Class="Prime.Ui.View"><prime:PrimeBrandMark />'
            '<game:GameWidget /></UserControl>', encoding="utf-8")
        input_dir = self.root / "input"
        output_dir = self.root / "output"
        input_dir.mkdir()
        modules = []
        for name in CHECKER.EXPECTED_MODULES:
            path = input_dir / name
            path.write_bytes(name.encode())
            modules.extend(("--module", str(path)))
        rule = self.root / "rules.xml"
        rule.write_text(
            '<?xml version="1.0"?><PrimeProtectionRules>'
            '<Module name="ProjectPrime.dll"><SkipType name="Prime.Reflection" '
            'skipMethods="true" /></Module></PrimeProtectionRules>', encoding="utf-8")
        config = self.root / "obj/obfuscar.xml"
        mapping = self.root / "obj/Mapping.txt"
        result = subprocess.run([
            sys.executable, str(GENERATOR), "--input-dir", str(input_dir),
            "--output-dir", str(output_dir), "--mapping-file", str(mapping),
            "--output-config", str(config), "--source-root", str(source),
            "--platform", "desktop",
            "--rules", str(ROOT / "build/protection/common.rules.xml"),
            "--rules", str(rule), *modules,
        ], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        root = ET.parse(config).getroot()
        variables = {item.attrib["name"]: item.attrib["value"] for item in root.findall("Var")}
        self.assertEqual("true", variables["KeepPublicApi"])
        self.assertEqual("true", variables["HidePrivateApi"])
        self.assertEqual("false", variables["RenameProperties"])
        self.assertEqual("false", variables["HideStrings"])
        self.assertTrue(Path(variables["InPath"]).is_absolute())
        self.assertTrue(Path(variables["OutPath"]).is_absolute())
        self.assertTrue(Path(variables["LogFile"]).is_absolute())
        main = next(module for module in root.findall("Module")
                    if Path(module.attrib["file"]).name == "ProjectPrime.dll")
        skipped = {item.attrib["name"] for item in main.findall("SkipType")}
        self.assertIn("Prime.Reflection", skipped)
        presentation = next(module for module in root.findall("Module")
                            if Path(module.attrib["file"]).name
                            == "ProjectPrime.Client.Presentation.dll")
        presentation_skipped = {
            item.attrib["name"] for item in presentation.findall("SkipType")
        }
        self.assertTrue(
            {"Prime.Ui.View", "Prime.Ui.PrimeBrandMark"}.issubset(presentation_skipped))
        generated = next(item for item in presentation.findall("SkipType")
                         if item.attrib["name"] == "Prime.Ui.PrimeBrandMark")
        self.assertEqual(
            {"name": "Prime.Ui.PrimeBrandMark", "skipMethods": "true",
             "skipFields": "true", "skipProperties": "true", "skipEvents": "true"},
            generated.attrib)
        game = next(module for module in root.findall("Module")
                    if Path(module.attrib["file"]).name == "ProjectPrime.Game.dll")
        self.assertIn("Prime.GameUi.GameWidget", {
            item.attrib["name"] for item in game.findall("SkipType")})
        self.assertIn(("*", "ToString"), {
            (method.attrib.get("type"), method.attrib.get("name"))
            for method in main.findall("SkipMethod")})

        mapping.write_text(
            '[ProjectPrime]Prime.InternalChoice -> [ProjectPrime]a\n'
            '\t[ProjectPrime]Prime.InternalChoice::ToString[]( ) -> b\n',
            encoding="utf-8")
        self.assertEqual(
            ["ProjectPrime.dll:Prime.InternalChoice.ToString"],
            CHECKER.configured_preserve_violations(mapping, config))

    def test_common_rules_preserve_external_interface_contracts(self):
        root = ET.parse(ROOT / "build/protection/common.rules.xml").getroot()
        modules = {module.attrib["name"]: module for module in root.findall("Module")}
        self.assertEqual(set(CHECKER.EXPECTED_MODULES), set(modules))
        for module in modules.values():
            methods = {(rule.attrib.get("type"), rule.attrib.get("name"))
                       for rule in module.findall("SkipMethod")}
            self.assertIn(("*", "Dispose"), methods)
            self.assertIn(("*", "DisposeAsync"), methods)
            self.assertIn(("*", "ToString"), methods)
        main_types = {rule.attrib.get("name") for rule in modules["ProjectPrime.dll"].findall("SkipType")}
        core_types = {
            rule.attrib.get("name")
            for rule in modules["ProjectPrime.Client.Core.dll"].findall("SkipType")
        }
        presentation_types = {
            rule.attrib.get("name")
            for rule in modules["ProjectPrime.Client.Presentation.dll"].findall("SkipType")
        }
        game_types = {rule.attrib.get("name") for rule in modules["ProjectPrime.Game.dll"].findall("SkipType")}
        self.assertIn("MphRead.BloomPyramidWeights", main_types)
        self.assertIn("MphRead.Entities.ClientPlayerBindings", core_types)
        self.assertIn("MphRead.Cheats", presentation_types)
        self.assertIn("MphRead.Formats.CollisionWorkspace/CollisionDataComparer", game_types)

    def test_checker_rejects_renamed_external_contract_method(self):
        config = self.root / "obfuscar.xml"
        config.write_text(
            '<Obfuscator><Module file="/tmp/ProjectPrime.dll">'
            '<SkipMethod type="*" name="Dispose" />'
            '</Module></Obfuscator>', encoding="utf-8")
        mapping = self.root / "Mapping.txt"
        mapping.write_text(
            '[ProjectPrime]Prime.InternalLease -> [ProjectPrime]a\n'
            '\t[ProjectPrime]Prime.InternalLease::Dispose[]( ) -> b\n',
            encoding="utf-8")
        self.assertEqual(
            ["ProjectPrime.dll:Prime.InternalLease.Dispose"],
            CHECKER.configured_preserve_violations(mapping, config))
        mapping.write_text(
            '[ProjectPrime]Prime.InternalLease -> [ProjectPrime]a\n'
            '\t[ProjectPrime]Prime.InternalLease::Dispose[]( ) skipped: rule in configuration\n',
            encoding="utf-8")
        self.assertEqual([], CHECKER.configured_preserve_violations(mapping, config))

    def test_desktop_rules_preserve_module_initializer(self):
        rules = ET.parse(ROOT / "build/protection/desktop.rules.xml").getroot()
        main = next(module for module in rules.findall("Module")
                    if module.attrib["name"] == "ProjectPrime.dll")
        methods = {
            (method.attrib.get("type"), method.attrib.get("name"))
            for method in main.findall("SkipMethod")
        }
        self.assertIn(
            ("MphRead.Mods.DesktopPresentationServices", "Initialize"),
            methods,
        )

    def test_protected_compilation_uses_isolated_artifact_paths(self):
        props = ET.parse(ROOT / "Directory.Build.props").getroot()
        protected_groups = [
            group for group in props.findall("PropertyGroup")
            if "PrimeProtectClient" in group.attrib.get("Condition", "")
        ]
        self.assertEqual(1, len(protected_groups))
        artifacts_path = protected_groups[0].find("ArtifactsPath")
        output_path = protected_groups[0].find("BaseOutputPath")
        self.assertIsNotNone(artifacts_path)
        self.assertIsNotNone(output_path)
        self.assertIn("artifacts/prime-protection", artifacts_path.text)
        self.assertIn("MSBuildProjectName", artifacts_path.attrib.get("Condition", ""))
        self.assertIn("artifacts/prime-protection/bin/Android", output_path.text)
        self.assertIn("MSBuildProjectName", output_path.attrib.get("Condition", ""))
        excludes = protected_groups[0].find("DefaultItemExcludes")
        self.assertIsNotNone(excludes)
        self.assertIn("$(MSBuildProjectDirectory)/obj/**", excludes.text)

        for path in (
            ROOT / "build/protection/ClientProtection.targets",
            ROOT / "build/protection/AndroidClientProtection.targets",
        ):
            target = path.read_text(encoding="utf-8")
            self.assertIn("System.IO.Path]::IsPathRooted('$(IntermediateOutputPath)')", target)
            self.assertIn("$(IntermediateOutputPath)prime-protection", target)

        build_workflow = (ROOT / ".github/workflows/build.yml").read_text(encoding="utf-8")
        release_workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        build_all = (ROOT / "tools/build-all.sh").read_text(encoding="utf-8")
        expected_android_root = "src/Android/obj/Release"
        self.assertIn(expected_android_root, build_workflow)
        self.assertIn(expected_android_root, release_workflow)
        self.assertIn(expected_android_root, build_all)
        self.assertIn(
            "artifacts/prime-protection/obj/**/prime-protection/**/obfuscar.xml",
            release_workflow)
        self.assertIn(
            "src/Android/obj/**/prime-protection/**/obfuscar.xml",
            release_workflow)

        android = ET.parse(ROOT / "src/Android/Android.csproj").getroot()
        presentation = android.find(
            ".//ProjectReference[@Include='../Client.Presentation/Client.Presentation.csproj']"
        )
        self.assertIsNotNone(presentation)
        self.assertEqual(
            "RuntimeIdentifier",
            presentation.attrib.get("GlobalPropertiesToRemove"),
        )

    def test_protection_repairs_fieldmarshal_metadata_after_obfuscar(self):
        project = ET.parse(
            ROOT / "tools/protection/MetadataRepair/MetadataRepair.csproj").getroot()
        package = project.find(".//PackageReference[@Include='Mono.Cecil']")
        self.assertIsNotNone(package)
        self.assertNotIn("Version", package.attrib)
        central = ET.parse(ROOT / "Directory.Packages.props").getroot()
        central_package = central.find(".//PackageVersion[@Include='Mono.Cecil']")
        self.assertIsNotNone(central_package)
        self.assertEqual("0.11.6", central_package.attrib["Version"])

        for path in (
            ROOT / "build/protection/ClientProtection.targets",
            ROOT / "build/protection/AndroidClientProtection.targets",
        ):
            target = path.read_text(encoding="utf-8")
            obfuscar = target.index("obfuscar.console")
            repair = target.index("MetadataRepair/MetadataRepair.csproj")
            checker = target.index("check-obfuscation.py&quot; protected")
            self.assertLess(obfuscar, repair, path)
            self.assertLess(repair, checker, path)

        source = (ROOT / "tools/protection/MetadataRepair/Program.cs").read_text(
            encoding="utf-8")
        self.assertIn("field.HasMarshalInfo", source)
        self.assertIn("parameter.HasMarshalInfo", source)
        self.assertIn("method.MethodReturnType.HasMarshalInfo", source)
        self.assertIn(
            "ValidateMetadata(verified.MainModule, expectedMarshalDescriptors,",
            source)

    def test_protection_repairs_self_scoped_value_type_constraints(self):
        source = (ROOT / "tools/protection/MetadataRepair/Program.cs").read_text(
            encoding="utf-8")

        self.assertIn(
            "RepairGenericParameters(type.GenericParameters, targetType.GenericParameters",
            source)
        self.assertIn(
            "RepairGenericParameters(method.GenericParameters, targetMethod.GenericParameters",
            source)
        self.assertIn(
            'Lookup<MethodDefinition>(output, method.MetadataToken, "method")', source)
        self.assertIn("expected.Attributes != actual.Attributes", source)
        self.assertIn("expected.Constraints.Count != actual.Constraints.Count", source)
        self.assertIn("TryGetUnmanagedValueTypeConstraint(expectedType", source)
        self.assertIn('required.ElementType.FullName == "System.ValueType"', source)
        self.assertIn(
            'required.ModifierType.FullName == "System.Runtime.InteropServices.UnmanagedType"',
            source)
        self.assertIn(
            'actualType.FullName == "System.ValueType" && IsSelfScoped(actualType, output)',
            source)
        self.assertIn(
            "actualConstraint.ConstraintType = output.ImportReference(expectedType)",
            source)
        self.assertIn("contains a stripped, self-scoped unmanaged constraint", source)
        self.assertIn("UnmanagedConstraintProfiles(output.MainModule)", source)
        self.assertIn("UnmanagedConstraintProfiles(output)", source)
        self.assertIn("parameter:{parameter.Position}", source)
        self.assertIn("RequireMatchingExternalConstraint(expectedType, actualType", source)
        self.assertIn("ConstraintIdentity(constraint.ConstraintType)", source)
        self.assertIn("repaired {genericConstraints} generic constraint(s)", source)

    def test_protection_restores_stripped_default_interface_bodies(self):
        source = (ROOT / "tools/protection/MetadataRepair/Program.cs").read_text(
            encoding="utf-8")

        self.assertIn("RestoreDefaultInterfaceBodies(", source)
        self.assertIn("candidate.IsInterface", source)
        self.assertIn("candidate.HasBody", source)
        self.assertIn("actualInstructions != 0", source)
        self.assertIn("target.Body = CloneMethodBody", source)
        self.assertIn("DefaultInterfaceBodyProfiles(output.MainModule)", source)
        self.assertIn("DefaultInterfaceBodyProfiles(output)", source)
        self.assertIn("default interface bodies changed after metadata repair", source)
        self.assertIn("restored {interfaceBodies} default interface body/bodies", source)

    def test_generator_rejects_relative_build_paths(self):
        result = subprocess.run([
            sys.executable, str(GENERATOR), "--input-dir", "relative",
            "--output-dir", str(self.root / "out"),
            "--mapping-file", str(self.root / "Mapping.txt"),
            "--output-config", str(self.root / "config.xml"),
            "--source-root", str(self.root), "--platform", "desktop",
            "--rules", str(ROOT / "build/protection/common.rules.xml"),
            "--module", str(self.root / "missing.dll"),
        ], capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must be absolute", result.stderr)

    def test_mapping_parser_counts_each_kind_per_module(self):
        mapping = self.root / "Mapping.txt"
        lines = []
        for index, name in enumerate(CHECKER.EXPECTED_MODULES):
            identity = Path(name).stem
            lines.extend([
                f"[{identity}]Prime.Type{index} -> [{identity}]A.A{index}",
                "\tCalculate( System.Int32 ) -> a",
                "\tSystem.Int32 _value -> b",
            ])
        mapping.write_text("\n".join(lines), encoding="utf-8")
        stats, renames = CHECKER.mapping_stats(mapping)
        self.assertEqual(len(CHECKER.EXPECTED_MODULES), len(renames))
        for module in CHECKER.EXPECTED_MODULES:
            self.assertEqual({"types": 1, "methods": 1, "fields": 1}, stats[module])

    def test_mapping_parser_does_not_count_skipped_members_as_renames(self):
        mapping = self.root / "Mapping.txt"
        lines = ["Renamed Types:"]
        for index, name in enumerate(CHECKER.EXPECTED_MODULES):
            identity = Path(name).stem
            lines.extend([
                f"[{identity}]Prime.Type{index} -> [{identity}]A.A{index}",
                "\tCalculate( System.Int32 ) -> skipped: rule in configuration",
                "\tSystem.Int32 _value -> skipped: public member",
            ])
        mapping.write_text("\n".join(lines), encoding="utf-8")

        stats, renames = CHECKER.mapping_stats(mapping)

        self.assertEqual(len(CHECKER.EXPECTED_MODULES), len(renames))
        for module in CHECKER.EXPECTED_MODULES:
            self.assertEqual({"types": 1, "methods": 0, "fields": 0}, stats[module])

    def test_preserved_type_members_are_detected_in_exact_mapwriter_format(self):
        mapping = self.root / "Mapping.txt"
        mapping.write_text(
            "Renamed Types:\n"
            "[ProjectPrime]MphRead.Droid.MainActivity -> skipped: rule in configuration\n"
            "\tOnCreate( Android.OS.Bundle ) -> A\n",
            encoding="utf-8",
        )
        self.assertEqual(
            ["MphRead.Droid.MainActivity member OnCreate( Android.OS.Bundle )"],
            CHECKER.preserve_violations(mapping, ["MphRead.Droid*"]),
        )

    def test_android_staging_accepts_per_rid_differences(self):
        sources = []
        for rid in ("android-arm64", "android-x64"):
            directory = self.root / rid
            directory.mkdir()
            for name in CHECKER.EXPECTED_MODULES:
                path = directory / name
                path.write_bytes((rid + ":" + name).encode())
                sources.extend(("--source", str(path)))
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "stage-android",
            "--input-root", str(self.root / "input"),
            "--routing-file", str(self.root / "routes.json"), *sources,
        ], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        for rid in ("android-arm64", "android-x64"):
            for name in CHECKER.EXPECTED_MODULES:
                self.assertEqual(
                    (rid + ":" + name).encode(),
                    (self.root / "input" / rid / name).read_bytes())
        routing = json.loads((self.root / "routes.json").read_text(encoding="utf-8"))
        self.assertEqual({"android-arm64", "android-x64"}, set(routing))

    def test_android_staging_accepts_isolated_artifact_pivots(self):
        sources = []
        for rid in ("android-arm64", "android-x64"):
            directory = self.root / f"release_{rid}" / "linked/shrunk"
            directory.mkdir(parents=True)
            for name in CHECKER.EXPECTED_MODULES:
                path = directory / name
                path.write_bytes((rid + ":" + name).encode())
                sources.extend(("--source", str(path)))

        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "stage-android",
            "--input-root", str(self.root / "input"),
            "--routing-file", str(self.root / "routes.json"), *sources,
        ], capture_output=True, text=True, check=False)

        self.assertEqual(0, result.returncode, result.stderr)
        routing = json.loads((self.root / "routes.json").read_text(encoding="utf-8"))
        self.assertEqual({"android-arm64", "android-x64"}, set(routing))

    def test_android_staging_is_fresh_and_does_not_mutate_linker_outputs(self):
        sources = []
        originals = {}
        for rid in ("android-arm64", "android-x64"):
            directory = self.root / rid / "linked/shrunk"
            directory.mkdir(parents=True)
            for name in CHECKER.EXPECTED_MODULES:
                path = directory / name
                path.write_bytes(("linked:" + name).encode())
                originals[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
                sources.extend(("--source", str(path)))
        input_dir = self.root / "input"
        input_dir.mkdir()
        (input_dir / "stale.dll").write_bytes(b"stale")
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "stage-android",
            "--input-root", str(input_dir),
            "--routing-file", str(self.root / "routes.json"), *sources,
        ], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse((input_dir / "stale.dll").exists())
        self.assertEqual({"android-arm64", "android-x64"}, {path.name for path in input_dir.iterdir()})
        for rid in ("android-arm64", "android-x64"):
            self.assertEqual(
                set(CHECKER.EXPECTED_MODULES),
                {path.name for path in (input_dir / rid).iterdir()})
        for value, digest in originals.items():
            self.assertEqual(digest, hashlib.sha256(Path(value).read_bytes()).hexdigest())

    def _android_routing_command(self, package_root, output_root, sources):
        arguments = [
            sys.executable, str(CHECKER_PATH), "verify-android-routing",
            "--package-root", str(package_root), "--output-root", str(output_root),
            "--rid", "android-arm64", "--rid", "android-x64",
        ]
        for source in sources:
            arguments.extend(("--source", str(source)))
        return arguments

    def test_android_precompression_routes_accept_only_protected_hashes(self):
        output = self.root / "output"
        package = self.root / "package"
        sources = []
        for rid in ("android-arm64", "android-x64"):
            for name in CHECKER.EXPECTED_MODULES:
                protected = ("protected:" + rid + ":" + name).encode()
                (output / rid).mkdir(parents=True, exist_ok=True)
                (output / rid / name).write_bytes(protected)
                routed = package / rid / name
                routed.parent.mkdir(parents=True, exist_ok=True)
                routed.write_bytes(protected)
                sources.append(routed)

        result = subprocess.run(
            self._android_routing_command(package, output, sources),
            capture_output=True, text=True, check=False)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(
            f"{2 * len(CHECKER.EXPECTED_MODULES)} protected pre-compression inputs",
            result.stdout,
        )

    def test_android_precompression_routes_reject_original_path(self):
        output = self.root / "output"
        package = self.root / "package"
        original = self.root / "linked/android-arm64/ProjectPrime.dll"
        (output / "android-arm64").mkdir(parents=True)
        original.parent.mkdir(parents=True)
        original.write_bytes(b"original")
        (output / "android-arm64/ProjectPrime.dll").write_bytes(b"protected")

        result = subprocess.run(
            self._android_routing_command(package, output, [original]),
            capture_output=True, text=True, check=False)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("outside protection package root", result.stderr)

    def test_android_precompression_routes_reject_nonmatching_hash(self):
        output = self.root / "output"
        package = self.root / "package"
        routed = package / "android-arm64/ProjectPrime.dll"
        (output / "android-arm64").mkdir(parents=True)
        routed.parent.mkdir(parents=True)
        routed.write_bytes(b"original")
        (output / "android-arm64/ProjectPrime.dll").write_bytes(b"protected")

        result = subprocess.run(
            self._android_routing_command(package, output, [routed]),
            capture_output=True, text=True, check=False)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("hash does not match Obfuscar output", result.stderr)

    @staticmethod
    def _assembly_store(assemblies, forbidden_payload=None):
        names = b"".join(
            len(name.encode()).to_bytes(4, "little") + name.encode()
            for name in assemblies)
        header_size = 20
        descriptor_size = 28 * len(assemblies)
        data_offset = header_size + descriptor_size + len(names)
        descriptors = []
        payloads = []
        cursor = data_offset
        for index, content in enumerate(assemblies.values()):
            debug_offset = 1 if index == 0 and forbidden_payload == "debug" else 0
            debug_size = 1 if index == 0 and forbidden_payload == "debug" else 0
            config_offset = 1 if index == 0 and forbidden_payload == "config" else 0
            config_size = 1 if index == 0 and forbidden_payload == "config" else 0
            descriptors.append(
                b"".join(value.to_bytes(4, "little") for value in (
                    index, cursor, len(content), debug_offset, debug_size,
                    config_offset, config_size)))
            payloads.append(content)
            cursor += len(content)
        header = b"XABA" + (0x80010003).to_bytes(4, "little")
        header += len(assemblies).to_bytes(4, "little") + b"\0" * 8
        return header + b"".join(descriptors) + names + b"".join(payloads)

    def test_xalz_payload_uses_raw_lz4_block_decoder(self):
        content = b"protected-managed-assembly"
        literal = bytes((0xF0, len(content) - 15)) + content
        stored = b"XALZ" + len(literal).to_bytes(4, "little")
        stored += len(content).to_bytes(4, "little") + literal
        self.assertEqual(content, CHECKER.decode_stored_assembly(stored))

    def test_android_store_rejects_debug_and_config_sidecars(self):
        for kind in ("debug", "config"):
            with self.subTest(kind=kind):
                store = self._assembly_store(
                    {"ProjectPrime.dll": b"protected"}, forbidden_payload=kind)
                with self.assertRaisesRegex(ValueError, f"forbidden {kind} payload"):
                    CHECKER.parse_assembly_store(store)

    def _write_fake_objcopy(self):
        script = self.root / "llvm-objcopy"
        script.write_text(
            "#!/bin/sh\n"
            "set -eu\n"
            "destination=${2#payload=}\n"
            "cp \"$3\" \"$destination\"\n",
            encoding="utf-8")
        script.chmod(0o755)
        return script

    def _create_store_apk(self, mutate=None):
        output = self.root / "output"
        apk = self.root / "ProjectPrime-Signed.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            for rid, abi in CHECKER.ANDROID_RID_TO_ABI.items():
                if rid not in {"android-arm64", "android-x64"}:
                    continue
                assemblies = {}
                for name in CHECKER.EXPECTED_MODULES:
                    content = ("protected:" + rid + ":" + name).encode()
                    destination = output / rid / name
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(content)
                    assemblies[name] = mutate(rid, name, content) if mutate else content
                archive.writestr(
                    f"lib/{abi}/libassembly-store.so", self._assembly_store(assemblies))
        return apk, output

    def test_android_apk_verifier_compares_real_store_payload_per_rid(self):
        apk, output = self._create_store_apk()
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "verify-android-apk",
            "--apk", str(apk), "--output-root", str(output),
            "--objcopy", str(self._write_fake_objcopy()),
        ], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(
            2,
            result.stdout.count(
                f"verified {len(CHECKER.EXPECTED_MODULES)} protected assemblies"),
        )

    def test_android_apk_verifier_rejects_unprotected_store_payload(self):
        apk, output = self._create_store_apk(
            lambda rid, name, content: b"original" if rid == "android-x64"
            and name == "ProjectPrime.Game.dll" else content)
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "verify-android-apk",
            "--apk", str(apk), "--output-root", str(output),
            "--objcopy", str(self._write_fake_objcopy()),
        ], capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("embedded ProjectPrime.Game.dll hash does not match", result.stderr)

    def test_android_apk_verifier_rejects_unexpected_abi_store(self):
        apk, output = self._create_store_apk()
        with zipfile.ZipFile(apk, "a") as archive:
            archive.writestr(
                "lib/x86/libassembly-store.so",
                self._assembly_store({"ProjectPrime.dll": b"unexpected"}))
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "verify-android-apk",
            "--apk", str(apk), "--output-root", str(output),
            "--objcopy", str(self._write_fake_objcopy()),
        ], capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("assembly-store ABI set differs", result.stderr)
        self.assertIn("lib/x86/libassembly-store.so", result.stderr)

    def test_android_target_uses_effective_intermediate_path_and_transition_invalidation(self):
        target = (ROOT / "build/protection/AndroidClientProtection.targets").read_text(encoding="utf-8")
        project = (ROOT / "src/Android/Android.csproj").read_text(encoding="utf-8")
        self.assertIn("$(MSBuildProjectDirectory)/$(IntermediateOutputPath)prime-protection", target)
        self.assertIn("InitializeProjectPrimeAndroidProtectionPaths", target)
        self.assertIn("'$(_PrimeAndroidProtectionEnabled)' == 'true' or '@(_PrimePreviousProtectionState)' == 'protected'", target)
        self.assertIn("$(IntermediateOutputPath)android/lz4", target)
        self.assertIn("$(_NativeAssemblySourceDir)*.ll", target)
        self.assertIn("$(_AndroidStampDirectory)_GenerateJavaStubs.stamp", target)
        self.assertIn("$(_AndroidApplicationSharedLibraryPath)", target)
        self.assertIn('BeforeTargets="_GenerateJavaStubs;_CollectAssembliesToCompress"', target)
        self.assertIn('<Import Project="../../build/protection/AndroidClientProtection.targets" />', project)

    def test_android_target_delegates_exact_package_copy_to_checker(self):
        target = (ROOT / "build/protection/AndroidClientProtection.targets").read_text(encoding="utf-8")
        self.assertIn("check-obfuscation.py&quot; package-android", target)
        self.assertNotIn("<Copy SourceFiles=", target)

    def test_android_protection_uses_two_phase_routing_around_java_sidecars(self):
        target = (ROOT / "build/protection/AndroidClientProtection.targets").read_text(encoding="utf-8")
        self.assertIn('AfterTargets="_PrepareAssemblies"', target)
        self.assertIn('BeforeTargets="_GenerateJavaStubs"', target)
        self.assertIn('DependsOnTargets="_PrepareAssemblies;InvalidateProjectPrimeAndroidProtectionTransition;', target)
        self.assertIn('<_PrimeAndroidOriginal Include="@(_ResolvedUserAssemblies)"', target)
        self.assertIn('<_PrimeAndroidRawSearchPath Include="@(_ResolvedAssemblies', target)
        self.assertIn('<_PrimeAndroidResolvedUserOriginal Include="@(_ResolvedUserAssemblies)"', target)
        self.assertIn('<_ResolvedUserAssemblies Remove="@(_PrimeAndroidResolvedUserOriginal)" />', target)
        self.assertIn('<_ResolvedUserAssemblies Include="@(_PrimeAndroidResolvedUserProtected)" />', target)
        self.assertNotIn('<_ResolvedAssemblies Remove=', target)
        self.assertNotIn('<_ResolvedUserMonoAndroidAssemblies Remove=', target)
        self.assertIn('Name="RouteProjectPrimeAndroidCompressionInputs"', target)
        self.assertIn('AfterTargets="_RemoveRegisterAttribute"', target)
        self.assertIn('BeforeTargets="_CollectAssembliesToCompress"', target)
        self.assertIn('DependsOnTargets="_RemoveRegisterAttribute;ProtectProjectPrimeAndroidClient;', target)
        self.assertIn('<_ShrunkAssemblies Remove="@(_PrimeAndroidLateShrunkOriginal)" />', target)
        self.assertIn('<_ShrunkAssemblies Include="@(_PrimeAndroidLateShrunkProtected)" />', target)
        self.assertIn('<_ShrunkUserAssemblies Remove="@(_PrimeAndroidLateShrunkUserOriginal)" />', target)
        self.assertIn('<_ShrunkUserAssemblies Include="@(_PrimeAndroidLateShrunkUserProtected)" />', target)
        self.assertIn('DependsOnTargets="RouteProjectPrimeAndroidCompressionInputs;', target)
        self.assertIn('Name="InvalidateProjectPrimeAndroidProtectedLink"', target)
        self.assertIn('BeforeTargets="_RunILLink"', target)
        self.assertIn('<Delete Files="$(_LinkSemaphore)" />', target)
        self.assertIn('--allow-zero-module &quot;ProjectPrime.dll&quot;', target)

    def test_android_packaging_copies_exact_per_rid_outputs_and_clears_stale_data(self):
        output = self.root / "output"
        package = self.root / "package"
        routing = {}
        for rid in ("android-arm64", "android-x64"):
            routing[rid] = {}
            for name in CHECKER.EXPECTED_MODULES:
                content = ("protected:" + rid + ":" + name).encode()
                path = output / rid / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)
                routing[rid][name] = str(self.root / "linked" / rid / name)
        package.mkdir()
        (package / "stale.dll").write_bytes(b"stale")
        routes = self.root / "routes.json"
        routes.write_text(json.dumps(routing), encoding="utf-8")

        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "package-android",
            "--output-root", str(output), "--package-root", str(package),
            "--routing-file", str(routes),
        ], capture_output=True, text=True, check=False)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse((package / "stale.dll").exists())
        for rid in routing:
            for name in CHECKER.EXPECTED_MODULES:
                expected = (output / rid / name).read_bytes()
                self.assertEqual(expected, (package / rid / name).read_bytes())

    def test_android_packaging_rejects_missing_per_rid_output(self):
        routes = self.root / "routes.json"
        routes.write_text(json.dumps({
            "android-x64": {name: f"/linked/{name}" for name in CHECKER.EXPECTED_MODULES}
        }), encoding="utf-8")
        result = subprocess.run([
            sys.executable, str(CHECKER_PATH), "package-android",
            "--output-root", str(self.root / "output"),
            "--package-root", str(self.root / "package"),
            "--routing-file", str(routes),
        ], capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("per-RID Obfuscar output is missing", result.stderr)

    def test_android_transition_target_invalidates_protected_and_mode_changes(self):
        project = self.root / "transition.proj"
        target = ROOT / "build/protection/AndroidClientProtection.targets"
        project.write_text(
            '<Project><PropertyGroup><Configuration>Release</Configuration>'
            '<TargetFramework>test-tfm</TargetFramework>'
            '<IntermediateOutputPath>obj/Release/test-tfm/</IntermediateOutputPath>'
            '<_AndroidStampDirectory>$(IntermediateOutputPath)stamp/</_AndroidStampDirectory>'
            '<_NativeAssemblySourceDir>$(IntermediateOutputPath)android/</_NativeAssemblySourceDir>'
            '<_AndroidApplicationSharedLibraryPath>$(IntermediateOutputPath)app_shared_libraries/</_AndroidApplicationSharedLibraryPath>'
            '<_RemoveRegisterFlag>$(IntermediateOutputPath)assets/shrunk/shrunk.flag</_RemoveRegisterFlag>'
            '</PropertyGroup>'
            f'<Import Project="{target}" />'
            '<Target Name="ProtectProjectPrimeAndroidClient" />'
            '<Target Name="VerifyProjectPrimeAndroidCompressionInputs" />'
            '<Target Name="_PrepareAssemblies" /></Project>',
            encoding="utf-8")
        lz4 = self.root / "obj/Release/test-tfm/android/lz4"
        native_source = self.root / "obj/Release/test-tfm/android/compressed_assemblies.test.ll"
        native_object = self.root / "obj/Release/test-tfm/android/compressed_assemblies.test.o"
        shared_library = self.root / "obj/Release/test-tfm/app_shared_libraries/android-x64/libassembly-store.so"
        java_stamp = self.root / "obj/Release/test-tfm/stamp/_GenerateJavaStubs.stamp"
        remove_register = self.root / "obj/Release/test-tfm/assets/shrunk/shrunk.flag"
        protection_state = self.root / "obj/Release/test-tfm/prime-protection/client-protection-state.txt"

        generated = (native_source, native_object, shared_library, java_stamp, remove_register)

        def create_stale_outputs():
            lz4.mkdir(parents=True, exist_ok=True)
            (lz4 / "stale.bin").write_bytes(b"stale")
            for path in generated:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"stale")

        def invoke(protected):
            result = subprocess.run([
                "dotnet", "msbuild", str(project),
                "-t:InvalidateProjectPrimeAndroidProtectionTransition",
                "-v:n",
                f"-p:PrimeProtectClient={'true' if protected else 'false'}",
            ], capture_output=True, text=True, check=False)
            self.assertEqual(0, result.returncode, result.stderr or result.stdout)
            return result.stdout + result.stderr

        for protected in (True, True, False):
            with self.subTest(protected=protected):
                create_stale_outputs()
                output = invoke(protected)
                self.assertFalse(lz4.exists(), output)
                for path in generated:
                    self.assertFalse(path.exists(), path)
                if protected:
                    self.assertTrue(protection_state.is_file(), output)

        create_stale_outputs()
        invoke(False)
        self.assertTrue((lz4 / "stale.bin").is_file())
        for path in generated:
            self.assertTrue(path.is_file(), path)

    def test_protected_android_explicitly_disables_aot_before_sdk_import(self):
        project = (ROOT / "src/Android/Android.csproj").read_text(encoding="utf-8")
        target = (ROOT / "build/protection/AndroidClientProtection.targets").read_text(encoding="utf-8")
        self.assertIn("<RunAOTCompilation>false</RunAOTCompilation>", project)
        self.assertIn("<AndroidAotMode>None</AndroidAotMode>", project)
        self.assertIn("<AndroidEnableProfiledAot>false</AndroidEnableProfiledAot>", project)
        self.assertIn("<AndroidBuildRuntimeIdentifiersInParallel>false</AndroidBuildRuntimeIdentifiersInParallel>", project)
        self.assertIn("Protected Android clients must disable AOT", target)

    def test_public_checker_finds_zip_and_directory_leaks(self):
        package = self.root / "package"
        package.mkdir()
        (package / "ProjectPrime").write_bytes(b"client")
        self.assertEqual(0, subprocess.run([
            sys.executable, str(CHECKER_PATH), "public", str(package)
        ], capture_output=True, check=False).returncode)
        (package / "client.pdb").write_bytes(b"debug")
        self.assertNotEqual(0, subprocess.run([
            sys.executable, str(CHECKER_PATH), "public", str(package)
        ], capture_output=True, check=False).returncode)
        apk = self.root / "client.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            archive.writestr("assets/prime-protection/obfuscar.xml", "secret")
        self.assertNotEqual(0, subprocess.run([
            sys.executable, str(CHECKER_PATH), "public", str(apk)
        ], capture_output=True, check=False).returncode)


if __name__ == "__main__":
    unittest.main()
