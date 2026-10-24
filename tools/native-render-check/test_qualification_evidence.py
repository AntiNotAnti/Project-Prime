"""Windows-only fake-process campaign checks; no renderer or shared build runs."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from frozen_pose_runtime import frozen_runtime
from write_renderer_manifest import digest_file, source_snapshot, verify_build_binding


class FrozenRuntimeTests(unittest.TestCase):
    def test_explicit_windows_executable_and_reference(self):
        with tempfile.TemporaryDirectory() as temp:
            reference = Path(temp) / 'ProjectPrime.dll'; reference.write_bytes(b'fixture')
            executable = Path(temp) / 'dotnet.exe'; executable.write_bytes(b'fixture')
            with patch.dict(os.environ, {'PROJECT_PRIME_POSE_REFERENCE_DLL': str(reference), 'DOTNET_EXE': str(executable)}):
                self.assertEqual(frozen_runtime(self), (executable.resolve(), reference.resolve()))

    def test_missing_explicit_reference_never_silently_skips(self):
        with patch.dict(os.environ, {'PROJECT_PRIME_POSE_REFERENCE_DLL': 'Z:/missing/frozen.dll'}):
            with self.assertRaises(AssertionError):
                frozen_runtime(self)

    def test_path_dotnet_exe_fallback(self):
        with tempfile.TemporaryDirectory() as temp:
            reference = Path(temp) / 'ProjectPrime.dll'; reference.write_bytes(b'fixture')
            executable = Path(temp) / 'dotnet.exe'; executable.write_bytes(b'fixture')
            with patch.dict(os.environ, {'PROJECT_PRIME_POSE_REFERENCE_DLL': str(reference), 'DOTNET_EXE': ''}), \
                    patch('frozen_pose_runtime.shutil.which', side_effect=lambda name: str(executable) if name == 'dotnet.exe' else None):
                self.assertEqual(frozen_runtime(self)[0], executable.resolve())


class ManifestTests(unittest.TestCase):
    def test_cli_snapshot_inside_worktree_excludes_its_own_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            script = root / 'tools/native-render-check/write_renderer_manifest.py'
            script.parent.mkdir(parents=True)
            shutil.copyfile(Path(__file__).with_name('write_renderer_manifest.py'), script)
            subprocess.run(['git', 'add', '.'], cwd=root, check=True)
            subprocess.run(['git', '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture'], cwd=root, check=True)
            output = root / 'evidence.json'
            command = [sys.executable, str(script), '--snapshot-source', '--output', str(output)]
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            snapshot = json.loads(output.read_text())
            self.assertNotIn('evidence.json', snapshot['files'])
            self.assertEqual(snapshot['excludedEvidencePaths'], ['evidence.json'])
            self.assertEqual(snapshot['sourceTreeSha256'], source_snapshot(root, [output])['sourceTreeSha256'])
            original = output.read_bytes()
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output.read_bytes(), original)

    def test_full_inventory_and_missing_tracked_file(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            for name in ('unchanged.cs', 'deleted.cs', 'asset.bin'):
                (root / name).write_text(name)
            subprocess.run(['git', 'add', '.'], cwd=root, check=True)
            subprocess.run(['git', '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture'], cwd=root, check=True)
            (root / 'deleted.cs').unlink(); (root / 'untracked.cs').write_text('new')
            snapshot = source_snapshot(root)
            self.assertEqual(snapshot['files']['unchanged.cs'], digest_file(root / 'unchanged.cs'))
            self.assertIn('asset.bin', snapshot['files'])
            self.assertIsNone(snapshot['files']['deleted.cs'])
            self.assertIn('untracked.cs', snapshot['files'])

    def test_explicit_build_binding_and_frozen_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            snapshot = root / 'source.json'; snapshot.write_text('{"explicit":"fixture snapshot"}')
            client = root / 'ProjectPrime.dll'; client.write_bytes(b'client')
            harness = root / 'native-render-check.dll'; harness.write_bytes(b'harness')
            metadata = {'sourceSnapshotSha256': digest_file(snapshot), 'clientSha256': digest_file(client), 'harnessSha256': digest_file(harness)}
            binding = root / 'binding.json'; binding.write_text(json.dumps(metadata))
            workload = {'clientBinary': {'sha256': metadata['clientSha256']}, 'harnessBinary': {'sha256': metadata['harnessSha256']}}
            self.assertEqual(verify_build_binding(binding, snapshot, workload, root)['status'], 'DIGESTS_MATCH')
            client.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'Frozen binary differs'):
                verify_build_binding(binding, snapshot, workload, root)
            snapshot.write_text('changed source receipt')
            with self.assertRaisesRegex(ValueError, 'sourceSnapshotSha256'):
                verify_build_binding(binding, snapshot, workload)

    def test_explicit_before_after_build_inputs_are_checked(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); before = root / 'before.json'; after = root / 'after.json'; binding = root / 'binding.json'
            before.write_text(json.dumps({'files': {'src/app.cs': 'a', 'docs/status.md': 'old'}}))
            after.write_text(json.dumps({'files': {'src/app.cs': 'a', 'docs/status.md': 'new'}}))
            metadata = {'sourceSnapshotSha256': digest_file(before), 'endSnapshotSha256': digest_file(after),
                        'clientSha256': 'a' * 64, 'harnessSha256': 'b' * 64, 'buildInputMatch': True}
            workload = {'clientBinary': {'sha256': 'a' * 64}, 'harnessBinary': {'sha256': 'b' * 64}}
            binding.write_text(json.dumps(metadata))
            self.assertTrue(verify_build_binding(binding, before, workload, end_snapshot=after)['buildInputsIndependentlyCompared'])
            after.write_text(json.dumps({'files': {'src/app.cs': 'changed'}}))
            metadata['endSnapshotSha256'] = digest_file(after); binding.write_text(json.dumps(metadata))
            with self.assertRaisesRegex(ValueError, 'inventories differ'):
                verify_build_binding(binding, before, workload, end_snapshot=after)
            metadata['buildInputMatch'] = False; binding.write_text(json.dumps(metadata))
            with self.assertRaisesRegex(ValueError, 'changed/unverified'):
                verify_build_binding(binding, before, workload)


@unittest.skipUnless(os.name == 'nt', 'PowerShell qualification recipe is Windows-only')
class WindowsCampaignTests(unittest.TestCase):
    def test_gates_exit_failure_and_preserve_fresh_evidence(self):
        pwsh = shutil.which('pwsh')
        self.assertIsNotNone(pwsh)
        runner = Path(__file__).with_name('qualify_windows_renderer.ps1')
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); frozen = root / 'frozen'; frozen.mkdir()
            (frozen / 'ProjectPrime.dll').write_bytes(b'fake-client')
            (frozen / 'native-render-check.dll').write_bytes(b'fake-harness')
            assets = root / 'assets'; assets.mkdir(); (assets / 'paths.txt').write_text('fake paths')
            helper = root / 'fake_native.py'
            helper.write_text(r'''
import hashlib,json,os,sys
from pathlib import Path
if '--info' in sys.argv: print('FAKE dotnet preflight');raise SystemExit(0)
args=sys.argv[2:]
def value(name):return args[args.index(name)+1]
cap=int(value('-hz')); scale=int(value('-scale')); output=Path(value('-output'))
folder=Path(sys.argv[1]).parent
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
interval=1000/(cap if cap>0 else 60);step=round(interval*1000000)
duration=int(value('-seconds'))-4
if os.environ.get('FAKE_SHORT')=='1':duration=20
count=round(duration*1000/interval)
raw_count=2 if os.environ.get('FAKE_SHORT_RAW')=='1' else count
vsync='On' if cap==0 else 'Off'
identity={'assemblySha256':sha(folder/'ProjectPrime.dll'),'assemblyMvid':'fixture-mvid'}
raw={'schema':2,'droppedRecords':0,'timestampFrequency':1000000000,'summary':{'samples':raw_count},'context':identity,
 'frames':[{'IntervalMs':interval,'PresentTimestamp':1000000000+i*step,'Segment':0,'Cap':cap,'Vsync':vsync} for i in range(1,raw_count+1)]}
receipt={'complete':True,'timingEvidenceComplete':True,'launcherStartupScheduled':False,'effectiveCap':cap,
 'room':'MP3 PROVING GROUND','softwareFrequency':cap if cap>0 else 0,
 'initialWindow':{'visible':False},'finalWindow':{'visible':True},'visibilityTransitions':1,
 'cap':cap,'resolutionScale':scale,'shadows':value('-shadows'),'framebuffer':[1280,720],'focusTransitions':0,
 'windowStateTransitions':0,'vsync':vsync,'assemblyMvid':'fixture-mvid','droppedSimulationSteps':0,'stalls':0,
 'meanCompletedFrameFps':cap if cap>0 else 60,'frameIntervalP99Ms':interval,'samples':count,'measurementSeconds':duration,
 'wallSeconds':duration+4,'frameIntervalsMs':[interval]*count,
 'observedWorkload':{'startRoster':[{'bot':True}]*8,'clientBinary':{'sha256':identity['assemblySha256']},'harnessBinary':{'sha256':sha(folder/'native-render-check.dll')}}}
bad=os.environ.get('FAKE_BAD')=='1'
if bad and cap==240:receipt['focusTransitions']=1
if bad and cap==0:raw['droppedRecords']=1
if os.environ.get('FAKE_METRICS')=='1':
 if cap==60:del receipt['frameIntervalP99Ms']
 if cap==120:receipt['frameIntervalP99Ms']=None
 if cap==144:receipt['frameIntervalP99Ms']=float('nan')
 if cap==240:receipt['meanCompletedFrameFps']=float('inf')
if os.environ.get('FAKE_NATIVE_DURATION')=='1':receipt['frameIntervalsMs']=[interval]*2;receipt['samples']=2
Path(os.environ['PROJECT_PRIME_GL_FRAME_CAPTURE']).write_text(json.dumps(raw))
output.write_text('{broken' if bad and cap==144 else json.dumps(receipt))
if os.environ.get('FAKE_MUTATE')=='1' and cap==60:
 with (folder/'ProjectPrime.dll').open('ab') as stream:stream.write(b'mutated')
raise SystemExit(1 if bad and cap==120 else 0)
''')
            command = root / 'fake-dotnet.cmd'
            command.write_text('@echo off\r\n"' + sys.executable + '" "' + str(helper) + '" %*\r\n')
            env = dict(os.environ)
            def run(destination, bad=False, extra=(), python_executable=sys.executable):
                env['FAKE_BAD'] = '1' if bad else '0'
                return subprocess.run([pwsh, '-NoProfile', '-File', str(runner), '-DotNet', str(command),
                    '-Python', python_executable, '-FrozenBuild', str(frozen), '-UserData', str(assets),
                    '-OutputDirectory', str(destination), *extra], env=env, capture_output=True, text=True, timeout=90)
            passed = root / 'pass'; result = run(passed)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(len(json.loads((passed / 'summary.json').read_text(encoding='utf-8-sig'))), 6)
            # PowerShell can return several Application matches for a basename.
            # Select one executable rather than stringifying the whole array.
            python_bin = root / 'another-python'; python_bin.mkdir()
            (python_bin / 'python.cmd').write_text('@echo off\r\n"' + sys.executable + '" %*\r\n')
            saved_path = env.get('PATH', '')
            env['PATH'] = str(Path(sys.executable).parent) + os.pathsep + str(python_bin) + os.pathsep + saved_path
            try:
                basename = root / 'basename-python'; result = run(basename, python_executable='python')
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                preflight = json.loads((basename / 'preflight.json').read_text(encoding='utf-8-sig'))
                self.assertIsInstance(preflight['python'], str)
                self.assertEqual(Path(preflight['python']).resolve(), Path(sys.executable).resolve())
            finally:
                env['PATH'] = saved_path
            unlimited = json.loads((passed / 'cap--1-command.json').read_text(encoding='utf-8-sig'))
            self.assertGreaterEqual(int(unlimited['graphicsEnvironment']['PROJECT_PRIME_GL_FRAME_CAPACITY']), 20 * 4096)
            original = (passed / 'summary.json').read_bytes(); result = run(passed)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual((passed / 'summary.json').read_bytes(), original)
            failed = root / 'fail'; result = run(failed, True)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            summary = json.loads((failed / 'summary.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(len(summary), 6, 'failed/corrupt runs must not prevent later receipts')
            for row in summary:
                if row['name'] in ('cap-120', 'cap-144', 'cap-240', 'cap-0'):
                    self.assertEqual(row['gate'], 'FAIL', row)
            self.assertTrue((failed / 'cap-144-error.log').is_file())
            snapshot = root / 'snapshot.json'; snapshot.write_text('{}')
            binding = root / 'binding.json'
            binding.write_text(json.dumps({'sourceSnapshotSha256': digest_file(snapshot),
                'clientSha256': digest_file(frozen / 'ProjectPrime.dll'), 'harnessSha256': digest_file(frozen / 'native-render-check.dll'),
                'buildInputMatch': False}))
            rejected = root / 'rejected'
            result = run(rejected, extra=('-SourceSnapshot', str(snapshot), '-BuildBinding', str(binding)))
            self.assertNotEqual(result.returncode, 0)
            self.assertTrue((rejected / 'preflight-error.log').is_file())
            self.assertFalse((rejected / 'cap-60-command.json').exists())
            env['FAKE_SHORT'] = '1'
            short = root / 'short-soak'; result = run(short, extra=('-Mode', 'Soak'))
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            row = json.loads((short / 'summary.json').read_text(encoding='utf-8-sig'))
            self.assertEqual(row['gate'], 'FAIL'); self.assertEqual(row['requestedMeasuredSeconds'], 1800)
            self.assertFalse(row['durationMatch'])
            env.pop('FAKE_SHORT')
            for flag in ('FAKE_METRICS', 'FAKE_SHORT_RAW', 'FAKE_NATIVE_DURATION'):
                env[flag] = '1'
                invalid = root / flag; result = run(invalid)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                rows = json.loads((invalid / 'summary.json').read_text(encoding='utf-8-sig'))
                for row in rows[:4] if flag == 'FAKE_METRICS' else rows:
                    self.assertEqual(row['gate'], 'FAIL', row)
                env.pop(flag)
            env['FAKE_MUTATE'] = '1'
            mutated = root / 'mutated'; result = run(mutated)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertFalse(json.loads((mutated / 'summary.json').read_text(encoding='utf-8-sig'))[0]['immutable'])


if __name__ == '__main__':
    unittest.main()
