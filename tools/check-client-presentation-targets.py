#!/usr/bin/env python3
"""Keep Client.Presentation desktop-only unless Android explicitly opts in."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


ANDROID_OPT_IN = "PrimeEnableAndroidPresentation=true"
ANDROID_TFM = "net10.0-android36.0"
ANDROID_INTERMEDIATE_PROPERTY = "$(AndroidPresentationIntermediatePath)"
INTERMEDIATE_PROPERTIES = {"BaseIntermediateOutputPath", "MSBuildProjectExtensionsPath"}


def _tokens(value: str | None) -> set[str]:
    return {token.strip() for token in (value or "").split(";") if token.strip()}


def inspect(root: Path) -> list[str]:
    root = root.resolve()
    errors: list[str] = []
    presentation_path = root / "src/Client.Presentation/Client.Presentation.csproj"
    android_path = root / "src/Android/Android.csproj"
    client_path = root / "src/Client/Client.csproj"

    presentation = ET.parse(presentation_path).getroot()
    if list(presentation.iter("TargetFrameworks")):
        errors.append("Client.Presentation must not multi-target in the ordinary build graph")

    frameworks = list(presentation.iter("TargetFramework"))
    defaults = [item for item in frameworks if "Condition" not in item.attrib]
    opt_ins = [item for item in frameworks
               if "PrimeEnableAndroidPresentation" in item.attrib.get("Condition", "")]
    if len(defaults) != 1 or (defaults[0].text or "").strip() != "net10.0":
        errors.append("Client.Presentation default TargetFramework must be exactly net10.0")
    if len(opt_ins) != 1 or (opt_ins[0].text or "").strip() != ANDROID_TFM \
            or "== 'true'" not in opt_ins[0].attrib.get("Condition", ""):
        errors.append("Client.Presentation Android TargetFramework must require the explicit opt-in")

    for item in presentation.iter("RuntimeIdentifiers"):
        if "PrimeEnableAndroidPresentation" not in item.attrib.get("Condition", ""):
            errors.append("Client.Presentation Android RuntimeIdentifiers must require the explicit opt-in")
    default_excludes = ";".join(item.text or ""
                                for item in presentation.iter("DefaultItemExcludes"))
    if "obj/**" not in default_excludes:
        errors.append("Client.Presentation must exclude every obj subtree from default source discovery")
    for reference in presentation.iter("ProjectReference"):
        removed = _tokens(reference.attrib.get("GlobalPropertiesToRemove"))
        if not INTERMEDIATE_PROPERTIES.issubset(removed):
            errors.append(
                "Client.Presentation dependencies must not inherit its Android intermediate paths")

    android = ET.parse(android_path).getroot()
    android_refs = [item for item in android.iter("ProjectReference")
                    if item.attrib.get("Include") == "../Client.Presentation/Client.Presentation.csproj"]
    if len(android_refs) != 1:
        errors.append("Android must reference Client.Presentation exactly once")
    else:
        reference = android_refs[0]
        if ANDROID_OPT_IN not in _tokens(reference.attrib.get("AdditionalProperties")):
            errors.append("Android must explicitly opt Client.Presentation into its Android target")
        if reference.attrib.get("SetTargetFramework") != f"TargetFramework={ANDROID_TFM}":
            errors.append("Android must pin the Client.Presentation Android target")
        if "RuntimeIdentifier" not in _tokens(reference.attrib.get("GlobalPropertiesToRemove")):
            errors.append("Android must isolate Client.Presentation from app RuntimeIdentifier fan-out")
        additional = _tokens(reference.attrib.get("AdditionalProperties"))
        for name in INTERMEDIATE_PROPERTIES:
            expected = f"{name}={ANDROID_INTERMEDIATE_PROPERTY}"
            if expected not in additional:
                errors.append("Android must isolate Client.Presentation Android restore intermediates")
                break

    intermediate_paths = [item.text or ""
                          for item in android.iter("AndroidPresentationIntermediatePath")]
    if len(intermediate_paths) != 1 or "Client.Presentation/obj/android/" not in intermediate_paths[0]:
        errors.append("Android must give Client.Presentation a dedicated Android intermediate path")
    restore_targets = [item for item in android.iter("Target")
                       if item.attrib.get("Name") == "RestoreAndroidPresentation"]
    if len(restore_targets) != 1:
        errors.append("Android must explicitly restore the opt-in Client.Presentation target")
    else:
        restore_tasks = list(restore_targets[0].iter("MSBuild"))
        restore_properties = (_tokens(restore_tasks[0].attrib.get("Properties"))
                              if len(restore_tasks) == 1 else set())
        expected_restore = {
            ANDROID_OPT_IN,
            f"TargetFramework={ANDROID_TFM}",
            "RestoreRecursive=false",
            *(f"{name}={ANDROID_INTERMEDIATE_PROPERTY}"
              for name in INTERMEDIATE_PROPERTIES),
        }
        if not expected_restore.issubset(restore_properties):
            errors.append("Android presentation restore must be isolated and non-recursive")

    client = ET.parse(client_path).getroot()
    for reference in client.iter("ProjectReference"):
        if reference.attrib.get("Include") != "../Client.Presentation/Client.Presentation.csproj":
            continue
        if "PrimeEnableAndroidPresentation" in reference.attrib.get("AdditionalProperties", ""):
            errors.append("desktop Client must not opt into the Android presentation target")

    return errors


def inspect_evaluated_targets(root: Path) -> list[str]:
    """Exercise the real MSBuild conditions used by restore and project refs."""
    project = root.resolve() / "src/Client.Presentation/Client.Presentation.csproj"
    dotnet = os.environ.get("DOTNET", "dotnet")
    cases = (
        ("ordinary build", (), "net10.0", ""),
        ("desktop RID", ("-p:RuntimeIdentifier=linux-x64",), "net10.0", ""),
        ("Android RID without opt-in", ("-p:RuntimeIdentifier=android-arm64",),
         "net10.0", ""),
        ("explicit Android opt-in", ("-p:PrimeEnableAndroidPresentation=true",),
         ANDROID_TFM, "android-arm64;android-x64"),
    )
    errors: list[str] = []
    for label, properties, expected_framework, expected_rids in cases:
        result = subprocess.run(
            [dotnet, "msbuild", str(project), "-nologo", *properties,
             "-getProperty:TargetFramework", "-getProperty:TargetFrameworks",
             "-getProperty:RuntimeIdentifiers"],
            cwd=root, check=False, capture_output=True, text=True)
        if result.returncode != 0:
            errors.append(f"Client.Presentation {label} evaluation failed")
            continue
        try:
            properties_out = json.loads(result.stdout)["Properties"]
        except (json.JSONDecodeError, KeyError, TypeError):
            errors.append(f"Client.Presentation {label} evaluation was not readable")
            continue
        actual = (properties_out.get("TargetFramework", ""),
                  properties_out.get("TargetFrameworks", ""),
                  properties_out.get("RuntimeIdentifiers", ""))
        expected = (expected_framework, "", expected_rids)
        if actual != expected:
            errors.append(
                f"Client.Presentation {label} evaluated to {actual}; expected {expected}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        errors = inspect(args.root)
        if not errors:
            errors.extend(inspect_evaluated_targets(args.root))
    except (OSError, ET.ParseError, subprocess.SubprocessError) as error:
        print(f"client presentation targets: {error}", file=sys.stderr)
        return 2
    for error in errors:
        print(error)
    print(f"client presentation targets: {len(errors)} violation(s)")
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
