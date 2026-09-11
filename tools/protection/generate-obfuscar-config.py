#!/usr/bin/env python3
"""Generate the absolute-path Obfuscar v3 configuration used by client publishes."""

from __future__ import annotations

import argparse
import copy
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


EXPECTED_MODULES = (
    "ProjectPrime.dll",
    "ProjectPrime.Game.dll",
    "Server.Shared.dll",
    "ProjectPrime.Replay.dll",
    "Audio.Ncsf.dll",
)
POLICY = {
    "KeepPublicApi": "true",
    "HidePrivateApi": "true",
    "RenameMethods": "true",
    "RenameFields": "true",
    "RenameProperties": "false",
    "RenameEvents": "false",
    "SkipGenerated": "true",
    "SkipSpecialName": "true",
    "ReuseNames": "false",
    "HideStrings": "false",
    "OptimizeMethods": "false",
    "SuppressIldasm": "false",
}
RULE_ATTRIBUTES = {
    "SkipNamespace": {"name"},
    "SkipType": {
        "name", "rx", "skipMethods", "skipFields", "skipProperties",
        "skipEvents", "skipStringHiding", "decorator", "decoratorAll",
    },
    "SkipMethod": {"type", "name", "rx", "attrib", "decorator", "decoratorAll"},
    "SkipField": {"type", "name", "rx", "attrib", "decorator", "decoratorAll"},
    "SkipProperty": {"type", "name", "rx", "attrib", "decorator", "decoratorAll"},
    "SkipEvent": {"type", "name", "rx", "attrib", "decorator", "decoratorAll"},
    "SkipStringHiding": {"type", "name", "rx", "attrib"},
    "SkipEnums": {"value"},
}
BOOLEAN_RULE_ATTRIBUTES = {
    "skipMethods", "skipFields", "skipProperties", "skipEvents", "skipStringHiding", "value",
}
XAML_CLASS_ATTRIBUTE = "{http://schemas.microsoft.com/winfx/2006/xaml}Class"
ASSEMBLY_MODULES = {
    "ProjectPrime": "ProjectPrime.dll",
    "ProjectPrime.Game": "ProjectPrime.Game.dll",
    "Server.Shared": "Server.Shared.dll",
    "ProjectPrime.Replay": "ProjectPrime.Replay.dll",
    "Audio.Ncsf": "Audio.Ncsf.dll",
}
PROJECT_MODULES = {
    "Client": "ProjectPrime.dll",
    "Android": "ProjectPrime.dll",
    "Game": "ProjectPrime.Game.dll",
    "Server.Shared": "Server.Shared.dll",
    "Shared.Replay": "ProjectPrime.Replay.dll",
    "Audio.Ncsf": "Audio.Ncsf.dll",
}


def absolute(value: str, label: str, *, must_exist: bool = False) -> Path:
    path = Path(value)
    if not path.is_absolute():
        raise ValueError(f"{label} must be absolute: {value}")
    path = path.resolve(strict=False)
    if must_exist and not path.exists():
        raise ValueError(f"{label} does not exist: {path}")
    return path


def load_rules(paths: list[Path]) -> dict[str, list[ET.Element]]:
    result = {name: [] for name in EXPECTED_MODULES}
    seen: set[tuple[str, str, tuple[tuple[str, str], ...]]] = set()
    for path in paths:
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            raise ValueError(f"invalid rule XML {path}: {exc}") from exc
        if root.tag != "PrimeProtectionRules" or root.attrib or (root.text or "").strip():
            raise ValueError(f"{path}: root must be an attribute-free PrimeProtectionRules element")
        for module in root:
            if module.tag != "Module" or set(module.attrib) != {"name"}:
                raise ValueError(f"{path}: only Module name=... is allowed at the root")
            name = module.attrib["name"]
            if name not in result:
                raise ValueError(f"{path}: rules target unexpected module {name}")
            if (module.text or "").strip():
                raise ValueError(f"{path}: Module cannot contain text")
            for rule in module:
                allowed = RULE_ATTRIBUTES.get(rule.tag)
                if allowed is None or not set(rule.attrib).issubset(allowed):
                    raise ValueError(f"{path}: invalid {rule.tag} attributes {sorted(rule.attrib)}")
                if list(rule) or (rule.text or "").strip():
                    raise ValueError(f"{path}: {rule.tag} must be empty")
                if rule.tag != "SkipEnums" and not (rule.attrib.get("name") or rule.attrib.get("rx")):
                    raise ValueError(f"{path}: {rule.tag} needs name or rx")
                for attribute in BOOLEAN_RULE_ATTRIBUTES.intersection(rule.attrib):
                    if rule.attrib[attribute] not in {"true", "false"}:
                        raise ValueError(f"{path}: {rule.tag} {attribute} must be true or false")
                if "attrib" in rule.attrib and rule.attrib["attrib"] not in {"", "public", "protected"}:
                    raise ValueError(f"{path}: {rule.tag} attrib must be public or protected")
                key = (name, rule.tag, tuple(sorted(rule.attrib.items())))
                if key not in seen:
                    seen.add(key)
                    result[name].append(copy.deepcopy(rule))
    return result


def xaml_owner_module(source_root: Path, path: Path) -> str:
    try:
        relative = path.relative_to(source_root / "src")
    except ValueError:
        return "ProjectPrime.dll"
    return PROJECT_MODULES.get(relative.parts[0], "ProjectPrime.dll")


def find_xaml_preserved_types(source_root: Path) -> dict[str, list[str]]:
    preserved: dict[str, set[str]] = {name: set() for name in EXPECTED_MODULES}
    for path in sorted((source_root / "src").rglob("*.axaml")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            raise ValueError(f"invalid Avalonia XAML {path}: {exc}") from exc
        owner_module = xaml_owner_module(source_root, path)
        for element in root.iter():
            value = element.attrib.get(XAML_CLASS_ATTRIBUTE)
            if value:
                preserved[owner_module].add(value.strip())
            if not isinstance(element.tag, str) or not element.tag.startswith("{"):
                continue
            namespace_uri, separator, local_name = element.tag[1:].partition("}")
            if not separator or not local_name:
                continue
            assembly: str | None = None
            if namespace_uri.startswith("clr-namespace:"):
                parts = namespace_uri[len("clr-namespace:"):].split(";")
                namespace = parts[0].strip()
                for option in parts[1:]:
                    key, equals, option_value = option.partition("=")
                    if equals and key.strip().casefold() == "assembly":
                        assembly = option_value.strip()
            elif namespace_uri.startswith("using:"):
                namespace = namespace_uri[len("using:"):].strip()
            else:
                continue
            module = ASSEMBLY_MODULES.get(assembly) if assembly else owner_module
            if module is None or not namespace:
                continue
            # Property elements use Owner.Property; preserve the CLR owner type.
            type_name = local_name.split(".", 1)[0].strip()
            if type_name:
                preserved[module].add(f"{namespace}.{type_name}")
    return {module: sorted(names) for module, names in preserved.items()}


def find_xaml_classes(source_root: Path) -> list[str]:
    classes: set[str] = set()
    for path in sorted((source_root / "src").rglob("*.axaml")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            raise ValueError(f"invalid Avalonia XAML {path}: {exc}") from exc
        for element in root.iter():
            value = element.attrib.get(XAML_CLASS_ATTRIBUTE)
            if value:
                classes.add(value.strip())
    return sorted(name for name in classes if name)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--mapping-file", required=True)
    parser.add_argument("--output-config", required=True)
    parser.add_argument("--source-root", required=True)
    parser.add_argument("--platform", required=True, choices=("desktop", "android"))
    parser.add_argument("--module", action="append", required=True)
    parser.add_argument("--search-path", action="append", default=[])
    parser.add_argument("--rules", action="append", required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        input_dir = absolute(args.input_dir, "input directory", must_exist=True)
        output_dir = absolute(args.output_dir, "output directory")
        mapping_file = absolute(args.mapping_file, "mapping file")
        output_config = absolute(args.output_config, "output config")
        source_root = absolute(args.source_root, "source root", must_exist=True)
        modules = [absolute(value, "module", must_exist=True) for value in args.module]
        search_paths = [absolute(value, "assembly search path", must_exist=True) for value in args.search_path]
        rules = [absolute(value, "rules file", must_exist=True) for value in args.rules]
        module_names = [path.name for path in modules]
        if sorted(module_names) != sorted(EXPECTED_MODULES) or len(set(module_names)) != len(module_names):
            raise ValueError(f"modules must be exactly: {', '.join(EXPECTED_MODULES)}")
        for module in modules:
            if module.parent != input_dir:
                raise ValueError(f"module is outside the protection input directory: {module}")
        for path in (output_dir, mapping_file.parent, output_config.parent):
            path.mkdir(parents=True, exist_ok=True)

        by_module = load_rules(rules)
        xaml_types = find_xaml_preserved_types(source_root)
        for module_name, type_names in xaml_types.items():
            existing = {
                rule.attrib.get("name") for rule in by_module[module_name]
                if rule.tag == "SkipType"
            }
            for type_name in type_names:
                if type_name not in existing:
                    by_module[module_name].append(ET.Element("SkipType", {
                        "name": type_name,
                        "skipMethods": "true",
                        "skipFields": "true",
                        "skipProperties": "true",
                        "skipEvents": "true",
                    }))

        root = ET.Element("Obfuscator")
        ET.SubElement(root, "Var", {"name": "InPath", "value": str(input_dir)})
        ET.SubElement(root, "Var", {"name": "OutPath", "value": str(output_dir)})
        ET.SubElement(root, "Var", {"name": "LogFile", "value": str(mapping_file)})
        for name, value in POLICY.items():
            ET.SubElement(root, "Var", {"name": name, "value": value})
        for path in sorted(set(search_paths)):
            ET.SubElement(root, "AssemblySearchPath", {"path": str(path)})
        for path in sorted(modules, key=lambda item: EXPECTED_MODULES.index(item.name)):
            module = ET.SubElement(root, "Module", {"file": str(path)})
            for rule in by_module[path.name]:
                module.append(rule)
        ET.indent(root, space="  ")
        tree = ET.ElementTree(root)
        tree.write(output_config, encoding="utf-8", xml_declaration=True)
        print(f"Generated {args.platform} Obfuscar config: {output_config}")
        print(
            "Preserved Avalonia x:Class/custom CLR element types: "
            f"{sum(len(names) for names in xaml_types.values())}")
        return 0
    except (OSError, ValueError) as exc:
        print(f"client protection config error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
