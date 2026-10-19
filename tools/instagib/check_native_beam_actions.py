#!/usr/bin/env python3
"""Asset-free end-to-end source contract for native RmlUi Insta-Gib customization.

The native bridge has its own action registry and ABI enum. A C#-only UI test
can pass even while native clicks are silently dropped. Pin both sides here.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
read = lambda name: (root / name).read_text(encoding="utf-8")
rml = read("src/MphRead/Mods/Launcher/RmlUi/Pages/hunters/native-picker.rml")
license_rml = read("src/MphRead/Mods/Launcher/RmlUi/Pages/license/native-overview.rml")
native = read("native/rmlui-poc/projectprime_rmlui.cpp")
header = read("native/rmlui-poc/projectprime_rmlui_api.h")
managed = read("src/MphRead/Mods/Launcher/RmlUi/Host/RmlUiIntent.cs")
presenter = read("src/MphRead/Mods/Launcher/RmlUi/Pages/hunters/NativeHunterPagePresenter.cs")
control = read("src/MphRead/Mods/Launcher/Portable/LauncherCosmeticDraftController.cs")
gl_test = read("tools/rmlui-opengl-check/LicenseCardGlAcceptance.cs")

def require(ok: bool, message: str) -> None:
    if not ok:
        raise AssertionError(message)

for name, number in (("HunterBeamNext", 278), ("HunterBeamColorNext", 279),
                     ("HunterBeamSelect", 280), ("HunterBeamColorSelect", 281)):
    marker = rf"\b{re.escape(name)}\s*=\s*{number}\b"
    require(bool(re.search(marker, managed)), f"managed ABI mismatch: {name}")
    require(bool(re.search(marker, header)), f"native ABI mismatch: {name}")
    require(f"PrimeIntentKind::{name}" in native, f"native decoder missing: {name}")
    require(f"RmlUiIntentKind.{name}" in presenter, f"controller routing missing: {name}")

for i in range(9):
    for identifier, action in ((f"hunter_beam{i}", f"hunter:beam-select:{i}"),
                               (f"hunter_beam_color{i}", f"hunter:beam-color-select:{i}")):
        require(f'id="{identifier}"' in rml, f"missing gallery button: {identifier}")
        require(f'data-action="{action}"' in rml, f"missing button action: {action}")
        require(f'class:{identifier}:selected' not in rml, "selection is bound by presenter, not static")
    require(f"for (int i = 0; i < CosmeticCatalog.BeamEffects.Count; i++)" in presenter,
            "dynamic beam selected-state binding missing")

for action in ("hunter:beam-next", "hunter:beam-color-next",
               "hunter:beam-select:", "hunter:beam-color-select:"):
    require(action in native, f"native parser does not handle {action}")
    require(action in managed, f"managed parser does not handle {action}")

for entry in ("native_license_customize", "native_license_tab_customization"):
    require(f'id="{entry}"' in license_rml, f"Hunter License entry missing: {entry}")
    require(f'"{entry}"' in gl_test, f"GL acceptance must exercise {entry}")
require("license:customization" in license_rml, "License route missing")
require("case 1: OpenCustomizationRequested = true;" in
        read("src/MphRead/Mods/Launcher/RmlUi/Pages/license/NativeLicenseOverviewPage.cs"),
        "License controller has no customization handoff")
require("SelectBeam(int index)" in control and "SelectBeamColor(int index)" in control,
        "native draft cannot select a specific style or dye")
print("INSTA-GIB NATIVE BEAM UI PASS: nine styles, nine dyes, two License routes, native/C# ABI")
