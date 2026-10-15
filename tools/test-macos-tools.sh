#!/usr/bin/env bash
# Negative regressions for the gates used by both workflows; no game assets.
set -euo pipefail
[[ $(uname -s) == Darwin ]] || { echo 'error: tests require macOS' >&2; exit 1; }
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
temp=$(mktemp -d)
trap 'rm -rf "$temp"' EXIT
case "$(uname -m)" in
    arm64) rid=osx-arm64; other=x86_64 ;;
    x86_64) rid=osx-x64; other=arm64 ;;
    *) exit 1 ;;
esac
project="$repo/src/MphRead/MphRead.csproj"
[[ $(dotnet msbuild "$project" -getProperty:MphReadAudioRid) == "$rid" ]] \
    || { echo 'error: local build selected the wrong audio RID' >&2; exit 1; }
for target in osx-arm64 osx-x64; do
    [[ $(dotnet msbuild "$project" -p:RuntimeIdentifier="$target" -getProperty:MphReadAudioRid) == "$target" ]] \
        || { echo 'error: explicit audio RID was ignored' >&2; exit 1; }
done
root="$temp/publish with spaces"
mkdir -p "$root/nested"
# This synthetic packaging fixture acknowledges the wrapper's success marker;
# the publish jobs separately run the real launcher's rendered window check.
printf '#include <stdio.h>\nint main(void) { puts("Launcher window check passed."); puts("GLFW extraction path check passed."); puts("Thumbnail window check passed."); return 0; }\n' > "$temp/main.c"
printf 'int native_probe(void) { return 42; }\n' > "$temp/native.c"
clang "$temp/main.c" -o "$root/ProjectPrime"
clang -dynamiclib "$temp/native.c" -o "$root/libopenal.1.dylib"
# The synthetic package must meet the same signed native-dependency contract
# as the real app. These tiny locally built Mach-O fixtures contain no assets.
clang -dynamiclib "$temp/native.c" -o "$root/libktx.dylib"
clang -dynamiclib "$temp/native.c" -o "$root/nested/extensionless-native"
"$repo/tools/sign-macos.sh" "$root"
"$repo/tools/check-macos-build.sh" "$root" "$rid"
expect_failure() {
    if "$@" > "$temp/failure.log" 2>&1; then
        cat "$temp/failure.log"
        echo "error: unexpectedly accepted: $*" >&2
        exit 1
    fi
    cat "$temp/failure.log"
}
expect_failure "$repo/tools/check-macos-build.sh" "$root" unsupported
chmod -x "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
chmod +x "$root/ProjectPrime"
mv "$root/libopenal.1.dylib" "$temp/openal"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
mv "$temp/openal" "$root/libopenal.1.dylib"
mv "$root/libktx.dylib" "$temp/ktx"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
mv "$temp/ktx" "$root/libktx.dylib"
clang -arch "$other" -dynamiclib "$temp/native.c" -o "$root/nested/wrong.dylib"
codesign --force --sign - "$root/nested/wrong.dylib"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
rm "$root/nested/wrong.dylib"
codesign --remove-signature "$root/nested/extensionless-native"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
"$repo/tools/sign-macos.sh" "$root"
# A fresh executable proves the missing-entitlement case independently of
# any metadata preserved while replacing an existing code signature.
clang "$temp/main.c" -o "$root/NoJit"
codesign --force --sign - "$root/NoJit"
mv "$root/NoJit" "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
printf '<plist version="1.0"><dict><key>com.apple.security.cs.allow-jit</key><false/></dict></plist>' > "$temp/false.plist"
codesign --force --sign - --entitlements "$temp/false.plist" "$root/ProjectPrime"
expect_failure "$repo/tools/check-macos-build.sh" "$root" "$rid"
# A compatible universal library must pass, including both signed slices.
clang -arch "$other" -dynamiclib "$temp/native.c" -o "$temp/other.dylib"
lipo -create "$root/libopenal.1.dylib" "$temp/other.dylib" -output "$temp/universal.dylib"
mv "$temp/universal.dylib" "$root/libopenal.1.dylib"
"$repo/tools/sign-macos.sh" "$root"
"$repo/tools/check-macos-build.sh" "$root" "$rid"
echo 'macOS signing gate regressions passed.'

# Reproduce the full app layout, including non-code map data. A flat publish
# can sign and launch successfully while its enclosing app cannot be signed.
fixture="$temp/package-input"
mkdir -p "$fixture/maps" "$fixture/fidelity-baselines"
cp "$root/ProjectPrime" "$root/libopenal.1.dylib" "$root/libktx.dylib" "$fixture/"
# Synthetic data keeps the packaging gate independent of controller/fidelity feature branches.
printf '# Controller mapping packaging fixture\n' > "$fixture/gamecontrollerdb.txt"
printf 'Controller mapping license fixture\n' > "$fixture/gamecontrollerdb.LICENSE"
printf '{"Name":"PACKAGING TEST"}\n' > "$fixture/maps/fixture.json"
printf '{"fixture":true}\n' > "$fixture/fidelity-baselines/fixture.json"
studio_fixture="$temp/studio-input"
mkdir -p "$studio_fixture"
cp "$root/ProjectPrime" "$studio_fixture/ProjectPrimeStudio"
printf 'Portable protocol symbols fixture\n' > "$fixture/ProjectPrime.Studio.Protocol.pdb"
printf 'Portable Studio symbols fixture\n' > "$studio_fixture/ProjectPrimeStudio.pdb"
touch "$fixture/.project-prime-in-use.lock" "$studio_fixture/.project-prime-update.lock"
"$repo/tools/package-macos.sh" "$fixture" "$temp/dist" "$rid" 1.2.3 "$studio_fixture"
mkdir "$temp/unpacked"
tar -xzf "$temp/dist/ProjectPrime-v1.2.3-$rid.tar.gz" -C "$temp/unpacked"
app="$temp/unpacked/Project Prime.app"
codesign --verify --deep --strict "$temp/unpacked/Project Prime Studio.app"
cmp "$fixture/ProjectPrime.Studio.Protocol.pdb" "$app/Contents/Resources/symbols/ProjectPrime.Studio.Protocol.pdb"
cmp "$studio_fixture/ProjectPrimeStudio.pdb" "$temp/unpacked/Project Prime Studio.app/Contents/Resources/symbols/ProjectPrimeStudio.pdb"
[[ ! -e "$app/Contents/MacOS/ProjectPrime.Studio.Protocol.pdb" ]] || exit 1
[[ ! -e "$temp/unpacked/Project Prime Studio.app/Contents/MacOS/ProjectPrimeStudio.pdb" ]] || exit 1
[[ ! -e "$app/Contents/MacOS/.project-prime-in-use.lock" ]] || exit 1
[[ ! -e "$temp/unpacked/Project Prime Studio.app/Contents/MacOS/.project-prime-update.lock" ]] || exit 1
python3 "$repo/tools/paired-install-check.py" "$temp/unpacked" --version 1.2.3 --rid "$rid"
[[ -f "$app/Contents/Resources/maps/fixture.json" ]] || exit 1
[[ ! -e "$app/Contents/MacOS/maps" ]] || exit 1
cmp "$fixture/fidelity-baselines/fixture.json" "$app/Contents/Resources/fidelity-baselines/fixture.json"
[[ ! -e "$app/Contents/MacOS/fidelity-baselines" ]] || exit 1
for resource in gamecontrollerdb.txt gamecontrollerdb.LICENSE; do
    cmp "$fixture/$resource" "$app/Contents/Resources/$resource"
    [[ ! -e "$app/Contents/MacOS/$resource" ]] || exit 1
done
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/gamecontrollerdb.txt"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/gamecontrollerdb.txt" "$app/Contents/Resources/gamecontrollerdb.txt"
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/maps/fixture.json"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/maps/fixture.json" "$app/Contents/Resources/maps/fixture.json"
codesign --verify --deep --strict "$app"
printf '\n' >> "$app/Contents/Resources/fidelity-baselines/fixture.json"
expect_failure codesign --verify --deep --strict "$app"
cp "$fixture/fidelity-baselines/fixture.json" "$app/Contents/Resources/fidelity-baselines/fixture.json"
codesign --verify --deep --strict "$app"
# The optional third app has native identity, closed dependencies, signed
# resources and source provenance; this fixture makes no GUI/Qt usability claim.
radiant_fixture="$temp/Native Radiant fixture.app"
python3 - "$repo" "$radiant_fixture" "$root" "$rid" <<'PYFIXTURE'
import hashlib, importlib.util, json, plistlib, shutil, sys, tarfile
from pathlib import Path
repo, app, native = map(Path, sys.argv[1:4])
rid = sys.argv[4]
spec = importlib.util.spec_from_file_location('native_package', repo / 'tools/project-prime-radiant/package_macos.py')
package = importlib.util.module_from_spec(spec); spec.loader.exec_module(package)
macos = app / 'Contents/MacOS'; macos.mkdir(parents=True)
for name in ('radiant', 'q3map2'):
    shutil.copy2(native / 'ProjectPrime', macos / name)
frameworks = app / 'Contents/Frameworks'; frameworks.mkdir()
shutil.copy2(native / 'libktx.dylib', frameworks / 'native-probe.dylib')
(app / 'Contents/Info.plist').write_bytes(plistlib.dumps({
    'CFBundleIdentifier': 'com.projectprime.radiant', 'CFBundleExecutable': 'radiant',
    'CFBundleName': 'Project Prime Radiant', 'CFBundlePackageType': 'APPL', 'CFBundleVersion': '1'}))
resources = app / 'Contents/Resources/netradiant'; resources.mkdir(parents=True)
# Exercise the narrow original-checkerboard image allowance in the native
# validator; the normal Game/Studio image prohibition remains unchanged.
fixture_spec = importlib.util.spec_from_file_location('real_tool', repo / 'tools/project-prime-radiant/real_tool_check.py')
real_tool = importlib.util.module_from_spec(fixture_spec); fixture_spec.loader.exec_module(real_tool)
real_tool.fixture(resources / 'gamepacks/projectprime.game')
notices = resources / 'source-notices'; notices.mkdir()
for name in ('LICENSE', 'GPL', 'LGPL'):
    (notices / name).write_text('Synthetic packaging regression fixture; no redistribution payload.\n')
shutil.copy2(repo / 'tools/project-prime-radiant/upstream.lock.json', notices / 'upstream.lock.json')
(notices / 'prime-build-receipt.json').write_text('{}\n')
with tarfile.open(notices / 'upstream-source.tar', 'w'):
    pass
source = notices / 'macos-packaging'; source.mkdir()
shutil.copy2(repo / 'tools/project-prime-radiant/package_macos.py', source / 'package_macos.py')
images = package.binaries(app)
receipt = {'schema': 1, 'bundleIdentifier': 'com.projectprime.radiant',
    'validation': {'symlinkFree': 'passed'}, 'buildReceipt': {'binaries': {}},
    'packagingScriptSha256': package.sha256(source / 'package_macos.py'),
    'candidateFilesSha256': {str(p.relative_to(resources)): package.sha256(p)
                             for p in resources.rglob('*') if p.is_file()},
    'dependencyGraph': {str(p.relative_to(app)): {'dependencies': package.commands(p)[0]}
                        for p in images}, 'guiAcceptance': 'pending'}
(resources / 'macos-package-receipt.json').write_text(json.dumps(receipt))
package.sign(app)
# Xcode 26.6 treats every operand after -verify_arch as an architecture.
# Enforce that CLI grammar even when the local older lipo accepts input-last,
# then pass the invocation to real lipo and run the complete native validator.
real_run = package.run
def current_lipo_grammar(args):
    command = [str(item) for item in args]
    if command[0] == '/usr/bin/lipo' and '-verify_arch' in command:
        operation = command.index('-verify_arch')
        if operation < 2 or any(arg not in ('arm64', 'x86_64') for arg in command[operation + 1:]):
            raise ValueError('lipo -verify_arch operands must be architecture names; input goes before the operation')
    return real_run(args)
package.run = current_lipo_grammar
package.validate_prebuilt(app, rid)
PYFIXTURE
python3 "$repo/tools/project-prime-radiant/package_macos.py" --validate-app "$radiant_fixture" --rid "$rid"
"$repo/tools/package-macos.sh" "$fixture" "$temp/dist-native" "$rid" 1.2.4 "$studio_fixture" "$radiant_fixture"
mkdir "$temp/unpacked-native"
tar -xzf "$temp/dist-native/ProjectPrime-v1.2.4-$rid.tar.gz" -C "$temp/unpacked-native"
python3 "$repo/tools/project-prime-radiant/package_macos.py" --validate-app "$temp/unpacked-native/Project Prime Radiant.app" --rid "$rid"
python3 "$repo/tools/paired-install-check.py" "$temp/unpacked-native" --version 1.2.4 --rid "$rid"
python3 - "$temp/unpacked-native" <<'PYOWNERSHIP'
import json, sys
from pathlib import Path
root = Path(sys.argv[1]); manifest = json.loads((root / '.project-prime-files.json').read_text())
assert all(any(name.startswith(app + '/Contents/') for name in manifest['Files'])
           for app in ('Project Prime.app', 'Project Prime Studio.app', 'Project Prime Radiant.app'))
assert 'Project Prime Radiant.app/Contents/MacOS/radiant' in manifest['Files']
assert 'Project Prime Radiant.app/Contents/MacOS/q3map2' in manifest['Files']
assert not any(p.is_symlink() for p in root.rglob('*'))
PYOWNERSHIP
# A valid seal does not waive the native provenance or RID checks.
wrong_radiant="$temp/Wrong identity.app"
ditto "$radiant_fixture" "$wrong_radiant"
/usr/libexec/PlistBuddy -c 'Set :CFBundleIdentifier com.projectprime.wrong' "$wrong_radiant/Contents/Info.plist"
codesign --force --sign - "$wrong_radiant"
expect_failure "$repo/tools/package-macos.sh" "$fixture" "$temp/rejected-native" "$rid" 1.2.4 "$studio_fixture" "$wrong_radiant"
wrong_arch_radiant="$temp/Wrong native architecture.app"
ditto "$radiant_fixture" "$wrong_arch_radiant"
clang -arch "$other" -dynamiclib "$temp/native.c" -o "$wrong_arch_radiant/Contents/Frameworks/native-probe.dylib"
codesign --force --sign - "$wrong_arch_radiant/Contents/Frameworks/native-probe.dylib"
codesign --force --sign - "$wrong_arch_radiant"
expect_failure "$repo/tools/package-macos.sh" "$fixture" "$temp/rejected-native" "$rid" 1.2.4 "$studio_fixture" "$wrong_arch_radiant"
unknown_image_radiant="$temp/Unknown native image.app"
ditto "$radiant_fixture" "$unknown_image_radiant"
printf 'Unsigned-source image fixture\n' > "$unknown_image_radiant/Contents/Resources/netradiant/cartridge-preview.png"
codesign --force --sign - "$unknown_image_radiant"
expect_failure python3 "$repo/tools/project-prime-radiant/package_macos.py" --validate-app "$unknown_image_radiant" --rid "$rid"
printf 'Forbidden cartridge fixture\n' > "$unknown_image_radiant/Contents/Resources/netradiant/forbidden.nds"
codesign --force --sign - "$unknown_image_radiant"
expect_failure python3 "$repo/tools/project-prime-radiant/package_macos.py" --validate-app "$unknown_image_radiant" --rid "$rid"
printf '\n' >> "$temp/unpacked-native/Project Prime Radiant.app/Contents/Resources/netradiant/gamepacks/projectprime.game/baseq3/maps/prime_acceptance.map"
expect_failure codesign --verify --deep --strict "$temp/unpacked-native/Project Prime Radiant.app"
echo 'macOS optional native Radiant packaging regressions passed.'

dotnet run --project "$repo/tools/platformtest/platformtest.csproj" -c Release
echo 'macOS bundle resource regressions passed.'
