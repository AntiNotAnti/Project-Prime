"""Execute actual release workflow shell gates with a local GitHub CLI fixture."""
import os
import importlib.util
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest

WORKFLOW=Path(__file__).resolve().parents[1]/".github/workflows/release.yml"


def step_script(name):
    lines=WORKFLOW.read_text().splitlines()
    start=next(i for i,line in enumerate(lines) if line.strip()=="- name: "+name)
    run=next(i for i in range(start,len(lines)) if lines[i].strip()=="run: |")
    indent=len(lines[run])-len(lines[run].lstrip())+2
    end=run+1
    while end<len(lines) and (not lines[end].strip() or len(lines[end])-len(lines[end].lstrip())>=indent):end+=1
    return textwrap.dedent("\n".join(lines[run+1:end]))


def job_block(name):
    """Inspect one YAML job without adding a CI-only parser dependency."""
    source=WORKFLOW.read_text()
    pattern=r"(?ms)^  "+re.escape(name)+r":\n(.*?)(?=^  [a-z][a-z0-9-]*:\n|\Z)"
    match=re.search(pattern,source)
    if not match:
        raise AssertionError("Release job not found: "+name)
    return match.group(1)


class NativeReleasePackagingTests(unittest.TestCase):
    def test_desktop_rmlui_runtime_is_built_and_downloaded_at_pinned_sha(self):
        native=job_block("release-rmlui-native")
        client=job_block("release-linux")
        self.assertIn("needs: resolve",native)
        self.assertIn("ref: ${{ needs.resolve.outputs.sha }}",native)
        self.assertIn("windows-latest",native)
        self.assertIn("ubuntu-latest",native)
        self.assertIn('tools/rmlui/build-native.sh "${{ matrix.rid }}" gl2',native)
        self.assertIn("tools/rmlui/verify-runtime.py",native)
        self.assertIn("name: release-rmlui-${{ matrix.rid }}",native)
        self.assertIn("release-rmlui-native]",client)
        self.assertIn("if: matrix.server == false\n        with:\n          name: release-rmlui-${{ matrix.rid }}",client)
        self.assertIn("path: artifacts/rmlui-native/${{ matrix.rid }}/",client)
        publish=step_script("publish target")
        self.assertLess(publish.index('tools/rmlui/verify-runtime.py "artifacts/'),
            publish.index('dotnet publish src/MphRead/MphRead.csproj'))
        self.assertIn('tools/rmlui/verify-runtime.py --package "publish/$TARGET" "$RID"',publish)

    def test_android_publish_includes_both_native_rmlui_abis(self):
        script=step_script("publish the APK")
        verify=step_script("verify Android native runtimes")
        for target,abi in (("android-arm64","arm64-v8a"),("android-x64","x86_64")):
            command=f'ANDROID_NDK_ROOT="$ndk" tools/rmlui/build-native.sh {target} draw-list'
            self.assertIn(command,script)
            self.assertLess(script.index(command),script.index('dotnet publish "$proj"'))
            library=f"lib/{abi}/libProjectPrime.RmlUi.Native.so"
            self.assertIn(library,verify)
        self.assertIn('python3 tools/rmlui/verify-runtime.py --apk "$apk"',verify)

    def test_macos_release_builds_and_verifies_native_bridge(self):
        script=step_script("publish and verify macOS release")
        self.assertLess(script.index('tools/rmlui/build-native.sh "$RID" gl2'),
            script.index('dotnet publish src/MphRead/MphRead.csproj'))
        self.assertIn('tools/rmlui/verify-runtime.py --package "publish/$RID" "$RID"',script)

    def test_native_fingerprint_is_portable_across_windows_checkout_newlines(self):
        runtime_file=WORKFLOW.parents[2]/"tools/rmlui/verify-runtime.py"
        spec=importlib.util.spec_from_file_location("rmlui_verify_runtime",runtime_file)
        verifier=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        with tempfile.TemporaryDirectory() as directory:
            old_root=verifier.ROOT
            try:
                verifier.ROOT=Path(directory)
                sources=verifier.ROOT/"native/rmlui-poc"
                sources.mkdir(parents=True)
                source=sources/"projectprime_rmlui.cpp"
                source.write_bytes(b"line one\\nline two\\n")
                lf=verifier.fingerprint()
                source.write_bytes(b"line one\\r\\nline two\\r\\n")
                self.assertEqual(lf,verifier.fingerprint())
                source.write_bytes(b"line one\\r\\nline modified\\r\\n")
                self.assertNotEqual(lf,verifier.fingerprint())
            finally:
                verifier.ROOT=old_root

    def test_macos_app_bundle_seals_ui_data_as_resources(self):
        root=WORKFLOW.parents[2]
        pack=(root/"tools/package-macos.sh").read_text()
        launcher=(root/"src/MphRead/Mods/Launcher/Gui/RmlUiPrototype.cs").read_text()
        # Strict app-bundle signing treats nested data in Contents/MacOS as
        # unsigned code. The shared native bridge remains beside the apphost.
        self.assertIn("PRIME-RMLUI.json licenses rmlui; do",pack)
        self.assertLess(pack.index("PRIME-RMLUI.json licenses rmlui; do"),
                        pack.index('codesign --force --sign - --entitlements "$platform/$executable.entitlements" "$app"'))
        self.assertIn('"$contents/Resources/rmlui/prime_home.rml"',pack)
        self.assertIn('OperatingSystem.IsMacOS()',launcher)
        self.assertIn('"..", "Resources", "rmlui"',launcher)


class ReleasePolicyTests(unittest.TestCase):
    def execute(self, step, **overrides):
        with tempfile.TemporaryDirectory(prefix="prime release policy ") as directory:
            directory=Path(directory);(directory/"dist").mkdir();(directory/"dist"/"package.zip").write_bytes(b"fixture")
            gh=directory/"gh"
            gh.write_text('''#!/usr/bin/env python3
import json,os,sys
args=sys.argv[1:]
with open(os.environ["CALLS"],"a") as log:log.write(json.dumps(args)+"\\n")
if args[:2]==["release","view"]:
    if os.environ["RELEASE_STATE"]=="missing":sys.exit(1)
    if "--json" in args:print("true" if os.environ["RELEASE_STATE"]=="draft" else "false")
elif args[:2]==["repo","view"]:print("PUBLIC")
elif args and args[0]=="api":
    if any("/commits/" in a for a in args):
        tag=next(a.rsplit("/",1)[-1] for a in args if "/commits/" in a)
        print(os.environ["TAG_SHA"] if tag==os.environ["EXISTING_TAG"] else os.environ["SHA"])
    elif any("generate-notes" in a for a in args):print("Local fixture notes")
    elif "-X" in args and "POST" in args and any(a.endswith("/git/refs") for a in args):print("{}")
    elif any("/git/ref/" in a for a in args):
        tag=next(a.rsplit("/",1)[-1] for a in args if "/git/ref/" in a)
        if tag!=os.environ["EXISTING_TAG"]:sys.exit(1)
        print("{}")
    elif any(a.endswith("/releases") for a in args):print(os.environ["LATEST_RELEASE"])
    elif any("/releases/tags/" in a for a in args):sys.exit(1)
    else:sys.exit(2)
''')
            gh.chmod(0o755)
            env=dict(os.environ,PATH=str(directory)+os.pathsep+os.environ["PATH"],CALLS=str(directory/"calls.jsonl"),
                     RELEASE_STATE="missing",TAG_SHA="a"*40,RELEASE_SHA="a"*40,REPOSITORY="local/fixture",REPO="local/fixture",
                     TAG="v1.2.3",EXISTING_TAG="v1.2.3",LATEST_RELEASE="v1.2.2",INPUT_TAG="v1.2.3",PUSHED_TAG="",BUMP="none",SHA="a"*40,PUBLISH_NOW="true",
                     KEYSTORE="",RUNNER_TEMP=str(directory),GITHUB_OUTPUT=str(directory/"output"),GITHUB_STEP_SUMMARY=str(directory/"summary"))
            env.update(overrides)
            result=subprocess.run(["bash","-c",step_script(step)],cwd=directory,env=env,text=True,capture_output=True,timeout=5)
            calls=(directory/"calls.jsonl").read_text().splitlines() if (directory/"calls.jsonl").exists() else []
            import json
            return result,[json.loads(line) for line in calls],(directory/"output").read_text() if (directory/"output").exists() else ""

    def test_published_tag_rejected_during_resolve(self):
        result,calls,output=self.execute("resolve the tag",RELEASE_STATE="published")
        self.assertNotEqual(0,result.returncode)
        self.assertIn("already published",result.stdout)
        self.assertNotIn("sha=",output)
        self.assertFalse(any("PATCH" in call or "POST" in call for call in calls))

    def test_draft_resolution_pins_exact_commit(self):
        result,_,output=self.execute("resolve the tag",RELEASE_STATE="draft")
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn("sha="+"a"*40,output)

    def test_orphan_retry_preserves_existing_tag_commit(self):
        result,calls,output=self.execute("resolve the tag",INPUT_TAG="",BUMP="patch",SHA="b"*40,TAG_SHA="b"*40)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn("tag=v1.2.3",output)
        self.assertIn("sha="+"b"*40,output)
        self.assertFalse(any("PATCH" in call or "POST" in call for call in calls))

    def test_patch_bump_skips_stale_orphan_instead_of_rebuilding_old_commit(self):
        result,calls,output=self.execute("resolve the tag",INPUT_TAG="",BUMP="patch",SHA="a"*40,TAG_SHA="b"*40)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn("Skipping stale tag v1.2.3",result.stdout)
        self.assertIn("tag=v1.2.4",output)
        self.assertIn("sha="+"a"*40,output)
        self.assertTrue(any("POST" in call and "ref=refs/tags/v1.2.4" in call
            and "sha="+"a"*40 in call for call in calls))
        self.assertFalse(any("PATCH" in call for call in calls))

    def test_first_release_patch_bump_skips_stale_initial_tag(self):
        result,calls,output=self.execute("resolve the tag",INPUT_TAG="",BUMP="patch",
            LATEST_RELEASE="",EXISTING_TAG="v0.1.0",SHA="a"*40,TAG_SHA="b"*40)
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn("tag=v0.1.1",output)
        self.assertIn("sha="+"a"*40,output)
        self.assertFalse(any("PATCH" in call for call in calls))

    def test_auto_bump_cannot_force_move_published_tag(self):
        result,calls,output=self.execute("resolve the tag",INPUT_TAG="",BUMP="patch",RELEASE_STATE="published")
        self.assertNotEqual(0,result.returncode)
        self.assertNotIn("sha=",output)
        self.assertFalse(any("PATCH" in call or "POST" in call for call in calls))

    def test_publication_rejects_tag_moved_after_tests(self):
        result,calls,_=self.execute("release",TAG_SHA="b"*40)
        self.assertNotEqual(0,result.returncode)
        self.assertFalse(any(call and call[0]=="release" for call in calls))

    def test_published_assets_are_never_uploaded_or_edited(self):
        result,calls,_=self.execute("release",RELEASE_STATE="published")
        self.assertNotEqual(0,result.returncode)
        self.assertFalse(any(call[:2] in (["release","upload"],["release","edit"],["release","create"]) for call in calls))

    def test_draft_upload_then_publish_is_allowed(self):
        result,calls,_=self.execute("release",RELEASE_STATE="draft")
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertTrue(any(call[:2]==["release","upload"] for call in calls))
        self.assertTrue(any(call[:2]==["release","edit"] and "--draft=false" in call for call in calls))

    def test_missing_public_android_key_is_rejected(self):
        result,_,_=self.execute("unlock the android keystore")
        self.assertNotEqual(0,result.returncode)
        self.assertIn("stable ANDROID_KEYSTORE",result.stdout)

    def test_missing_draft_android_key_remains_explicit(self):
        result,_,output=self.execute("unlock the android keystore",PUBLISH_NOW="false")
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn("Draft-only",result.stdout)
        self.assertIn("have=false",output)


if __name__=="__main__":unittest.main()
