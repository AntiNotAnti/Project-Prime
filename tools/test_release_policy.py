"""Execute actual release workflow shell gates with a local GitHub CLI fixture."""
import os
from pathlib import Path
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
    elif any(a.endswith("/releases") for a in args):print("v1.2.2")
    elif any("/releases/tags/" in a for a in args):sys.exit(1)
    else:sys.exit(2)
''')
            gh.chmod(0o755)
            env=dict(os.environ,PATH=str(directory)+os.pathsep+os.environ["PATH"],CALLS=str(directory/"calls.jsonl"),
                     RELEASE_STATE="missing",TAG_SHA="a"*40,RELEASE_SHA="a"*40,REPOSITORY="local/fixture",REPO="local/fixture",
                     TAG="v1.2.3",EXISTING_TAG="v1.2.3",INPUT_TAG="v1.2.3",PUSHED_TAG="",BUMP="none",SHA="a"*40,PUBLISH_NOW="true",
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
