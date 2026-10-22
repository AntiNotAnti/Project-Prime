import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from net21_evidence import DENOMINATORS, audit, campaign_manifest, new_manifest


def measured():
    report = new_manifest('G0', {name: {'countingUnit': 'sample', 'minimum': 2}
                                 for name in DENOMINATORS})
    report.update(sourceSha='a' * 40, binarySha256='b' * 64,
                  config={'flags': ['OpenGL']}, seed=42,
                  networkProfile={'latencyMs': 0, 'randomLoss': 0},
                  provenance='fresh', checks={'baseline': 'PASS'})
    for metric in report['metrics'].values():
        metric.update(expected=3, observed=3, status='PASS')
    return report


class EvidenceTests(unittest.TestCase):
    def campaign_fixture(self, directory):
        key = dict(zip(('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId'),
                       (1, 2, 0, 1, 1, 3)))
        fact = {'Epoch': 1, 'Match': 2, 'Award': False, 'Id': 3, 'Type': 55, 'Payload': 'ABC', 'Baseline': False}
        manifest = {'source': 'a'*40, 'assemblySha256': 'b'*64, 'assemblyAfterSha256': 'b'*64,
                    'settings': {'protocol': 51, 'seed': -42, 'profile': 'lan', 'players': 1},
                    'faultProfile': {'rtt_ms': 0, 'loss_percent': 0}}
        server = {'Version': 'local+' + 'a'*40, 'Protocol': 51,
                  'CombatPayments': {'LifetimePayments': 0}, 'Transport': {'QueueHighWater': 0},
                  'Shots': [{'Stage': 'accepted', 'Key': key}] * 2, 'Facts': [fact, fact]}
        peer = {'Shots': [{'Stage': 'offered', 'Key': key}] * 2}
        health = dict(Overflow=0, ConnectionLost=False, SemanticOverrun=False,
                      SemanticDisconnects=0, LastError=None)
        server.update(health)
        peer.update(health, Version=server['Version'], Protocol=51)
        summary = {'status': 'PASS', 'failures': [], 'processes': [
            {'name': 'server', 'exit': 0}, {'name': 'peer-0', 'exit': 0}]}
        for name, data in (('manifest', manifest), ('server', server), ('peer-0', peer), ('summary', summary)):
            (Path(directory) / (name + '.json')).write_text(json.dumps(data))

    def test_campaign_export_zero_hits_legacy_pass_is_incomplete(self):
        with tempfile.TemporaryDirectory() as directory:
            self.campaign_fixture(directory)
            report = campaign_manifest(directory)
            self.assertEqual(report['provenance'], 'fresh')
            self.assertEqual(report['checks']['oldCampaign'], 'PASS')
            self.assertEqual(report['metrics']['hits']['observed'], 0)
            self.assertEqual(report['metrics']['hits']['status'], 'NOT_RUN')
            self.assertEqual(report['metrics']['queueOccupancy']['status'], 'PASS')
            self.assertEqual(report['metrics']['replayFacts']['observed'], 1)
            self.assertEqual(report['metrics']['offeredShots']['observed'], 1)
            self.assertEqual(report['metrics']['acceptedShots']['observed'], 1)
            self.assertIsNone(report['metrics']['claimsOffered']['observed'])
            self.assertEqual(audit(report)['status'], 'INCOMPLETE')
            self.assertEqual(len(report['evidenceFiles']), 4)

    def test_campaign_identity_mismatch_is_not_fresh(self):
        with tempfile.TemporaryDirectory() as directory:
            self.campaign_fixture(directory)
            path = Path(directory) / 'manifest.json'
            manifest = json.loads(path.read_text())
            manifest['assemblyAfterSha256'] = 'c'*64
            path.write_text(json.dumps(manifest))
            report = campaign_manifest(directory)
            self.assertIsNone(report['provenance'])
            self.assertEqual(audit(report)['status'], 'FAIL')

    def test_campaign_peer_identity_completeness_and_acceptance_fail_closed(self):
        for case in ('protocol', 'overflow', 'processes', 'acceptance'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                self.campaign_fixture(directory)
                peer_path = Path(directory) / 'peer-0.json'
                peer = json.loads(peer_path.read_text())
                if case == 'protocol':
                    peer['Protocol'] = 50
                elif case == 'overflow':
                    peer['Overflow'] = 1
                elif case == 'acceptance':
                    peer['Shots'][0]['Stage'] = 'accepted'
                    peer['Shots'][0]['Key']['ShotId'] = 999
                else:
                    path = Path(directory) / 'manifest.json'
                    manifest = json.loads(path.read_text())
                    manifest['settings']['players'] = 2
                    path.write_text(json.dumps(manifest))
                peer_path.write_text(json.dumps(peer))
                self.assertEqual(audit(campaign_manifest(directory))['status'], 'FAIL')

    def test_campaign_baseline_fact_is_not_an_authoritative_event(self):
        with tempfile.TemporaryDirectory() as directory:
            self.campaign_fixture(directory)
            path = Path(directory) / 'server.json'
            server = json.loads(path.read_text())
            baseline = dict(server['Facts'][0], Baseline=True, Id=0)
            server['Facts'].append(baseline)
            path.write_text(json.dumps(server))
            self.assertEqual(campaign_manifest(directory)['metrics']['replayFacts']['observed'], 1)
            server['Facts'][0]['Id'] = 0
            path.write_text(json.dumps(server))
            self.assertEqual(audit(campaign_manifest(directory))['status'], 'FAIL')

    def test_campaign_malformed_returns_failure_and_cli_json(self):
        with tempfile.TemporaryDirectory() as directory:
            self.campaign_fixture(directory)
            (Path(directory) / 'server.json').write_text('{"Shots": NaN}')
            report = campaign_manifest(directory)
            self.assertTrue(report['exportErrors'])
            self.assertEqual(audit(report)['status'], 'FAIL')
            run = subprocess.run([sys.executable, str(Path(__file__).with_name('net21_evidence.py')),
                                  '--campaign', directory], capture_output=True, text=True)
            self.assertEqual(run.returncode, 1)
            self.assertEqual(json.loads(run.stdout)['audit']['status'], 'FAIL')

    def test_measured_and_trusted_identity(self):
        report = measured()
        self.assertEqual(audit(report, {'sourceSha': 'a'*40, 'binarySha256': 'b'*64})['status'], 'PASS')
        self.assertEqual(audit(report, {'binarySha256': 'c'*64})['status'], 'FAIL')

    def test_missing_and_null_are_not_zero(self):
        report = measured()
        del report['metrics']['hits']
        report['metrics']['grants']['observed'] = None
        result = audit(report)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['hits'], {'observed': None, 'status': 'NOT_RUN'})
        self.assertIsNone(result['metrics']['grants']['observed'])
        self.assertEqual(audit(new_manifest('G0'))['status'], 'NOT_RUN')

    def test_measured_zero_and_missing_zero_allowed_metric(self):
        report = measured()
        for name in ('packetLoss', 'queueOccupancy'):
            report['metrics'][name].update(observed=0, expected=0, minimum=0)
        self.assertEqual(audit(report)['status'], 'PASS')
        report['metrics']['packetLoss']['observed'] = None
        result = audit(report)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['packetLoss'], {'observed': None, 'status': 'NOT_RUN'})

    def test_required_zero_is_not_run(self):
        report = measured()
        report['metrics']['hits']['observed'] = 0
        result = audit(report)
        self.assertEqual(result['metrics']['hits']['status'], 'NOT_RUN')
        self.assertEqual(result['status'], 'INCOMPLETE')

    def test_signed_seed_range(self):
        for value in (-(2 ** 31), -1, 0, 2 ** 31 - 1):
            report = measured()
            report['seed'] = value
            self.assertEqual(audit(report)['status'], 'PASS')
        for value in (-(2 ** 31) - 1, 2 ** 31, True, 1.5):
            report = measured()
            report['seed'] = value
            self.assertEqual(audit(report)['status'], 'FAIL')

    def test_positive_under_minimum_or_expected(self):
        for minimum, expected in ((4, 2), (2, 4)):
            report = measured()
            report['metrics']['hits'].update(minimum=minimum, expected=expected)
            self.assertEqual(audit(report)['status'], 'INCOMPLETE')

    def test_malformed_counts_and_statuses(self):
        for field in ('observed', 'expected', 'minimum'):
            for value in (True, -1, 1.2, float('nan'), float('inf'), '3', []):
                with self.subTest(field=field, value=value):
                    report = measured()
                    report['metrics']['hits'][field] = value
                    self.assertEqual(audit(report)['status'], 'FAIL')
        for value in ([], {}, None, 'unknown'):
            report = measured()
            report['metrics']['hits']['status'] = value
            self.assertEqual(audit(report)['status'], 'FAIL')

    def test_failures_cannot_be_hidden(self):
        for target in ('metric', 'check'):
            report = measured()
            if target == 'metric':
                report['metrics']['hits']['status'] = 'FAIL'
            else:
                report['checks']['baseline'] = 'FAIL'
            report['exclusions'] = ['hits excluded from this run']
            self.assertEqual(audit(report)['status'], 'FAIL')
        report = measured()
        del report['metrics']['hits']
        report['exclusions'] = ['hits excluded']
        self.assertEqual(audit(report)['status'], 'INCOMPLETE')

    def test_historical_remains_incomplete(self):
        report = measured()
        report['provenance'] = 'historical'
        self.assertEqual(audit(report)['status'], 'INCOMPLETE')

    def test_hash_format_and_required_metadata(self):
        for key, value in (('sourceSha', 'abc'), ('binarySha256', 'a'*40),
                           ('config', []), ('networkProfile', {}), ('seed', False)):
            report = measured()
            report[key] = value
            self.assertEqual(audit(report)['status'], 'FAIL')

    def test_generic_extra_metric_has_own_unit_and_threshold(self):
        report = measured()
        report['metrics']['reconnects'] = dict(countingUnit='completed reconnect',
                                               minimum=5, expected=5, observed=4, status='PASS')
        self.assertEqual(audit(report)['status'], 'INCOMPLETE')
        report['metrics']['reconnects']['observed'] = 5
        self.assertEqual(audit(report)['status'], 'PASS')

    def test_cli_exit_codes_and_malformed_json(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'report.json'
            for report, expected_exit in ((measured(), 0), (new_manifest('G0'), 1)):
                path.write_text(json.dumps(report))
                run = subprocess.run([sys.executable, str(Path(__file__).with_name('net21_evidence.py')), str(path)], capture_output=True, text=True)
                self.assertEqual(run.returncode, expected_exit)
                self.assertIn(json.loads(run.stdout)['status'], ('PASS', 'NOT_RUN'))
            path.write_text('{"seed": NaN}')
            run = subprocess.run([sys.executable, str(Path(__file__).with_name('net21_evidence.py')), str(path)], capture_output=True, text=True)
            self.assertEqual(run.returncode, 1)
            self.assertEqual(json.loads(run.stdout)['status'], 'FAIL')


if __name__ == '__main__':
    unittest.main()
