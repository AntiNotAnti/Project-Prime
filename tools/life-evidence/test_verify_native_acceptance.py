"""Falsifiers for immutable hashes and positive native acceptance denominators."""
import copy
import json
from pathlib import Path
import shutil
import tempfile
import unittest

import verify_native_acceptance as evidence


class NativeAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name) / 'evidence'
        shutil.copytree(evidence.PACKAGE, self.directory)

    def tearDown(self):
        self.temp.cleanup()

    def change(self, name, edit):
        path = self.directory / name
        data = evidence.load(path)
        edit(data)
        path.write_text(json.dumps(data, indent=2) + '\n')

    def shadow(self, edit):
        # Refresh the outer link to exercise semantic validation after hashes;
        # production verification would reject the immutable derivative first.
        name = 'native-tour-42/01-Samus-DoubleDamage-Shadow/arm.json'
        self.change(name, edit)
        digest = evidence.sha((self.directory / name).read_bytes())
        self.change('native-tour-42/receipt.json', lambda d: d['runs'][1].update(resultSha256=digest))

    def test_exact_package(self):
        result = evidence.verify(self.directory)
        self.assertEqual((result['genuineContacts'], result['nativeShots'], result['exactPairs'], result['nativeFrames']), (42, 42, 21, 75656))
        self.assertEqual(result['fullPlanStatus'], 'NOT_ACCEPTED')

    def test_log_envelopes_preserve_exact_trailing_space(self):
        for name, marker in [('timed-shot-accepted', 'TIMED ACCEPTED PASS \n'), ('timed-shot-replay-scene', 'TIMED REPLAY SCENE PASS \n')]:
            envelope = evidence.load(self.directory / ('net-assets/' + name + '.log.json'))
            decoded = evidence.decode_log_envelope(envelope)
            self.assertIn(marker, decoded)
            self.assertEqual(evidence.log_text(self.directory, 'net-assets/' + name + '.log'), decoded)
            self.assertEqual(envelope['decodedBytes'], len(decoded.encode('utf-8')))

    def test_log_envelope_cannot_normalize_trailing_space(self):
        envelope = evidence.load(self.directory / 'net-assets/timed-shot-accepted.log.json')
        envelope['text'] = envelope['text'].replace('TIMED ACCEPTED PASS \n', 'TIMED ACCEPTED PASS\n')
        with self.assertRaisesRegex(ValueError, 'decoded SHA/bytes mismatch'):
            evidence.decode_log_envelope(envelope)

    def test_log_envelope_decoder_corruption_rejected(self):
        envelope = evidence.load(self.directory / 'net-assets/timed-shot-accepted.log.json')
        envelope['decoder'] = 'trimmed-text'
        with self.assertRaisesRegex(ValueError, 'decoder metadata invalid'):
            evidence.decode_log_envelope(envelope)

    def test_immutable_derivative_bytes(self):
        path = self.directory / 'native-tour-42/00-Samus-DoubleDamage-Off/arm.json'
        path.write_bytes(path.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'digest/bytes mismatch'):
            evidence.verify(self.directory)

    def test_manifest_cannot_omit_a_member(self):
        self.change('manifest.json', lambda d: d['files'].pop())
        with self.assertRaisesRegex(ValueError, 'file denominator'):
            evidence.verify(self.directory)

    def test_native_fixture_zero_denominator(self):
        self.change('net-assets/receipt.json', lambda d: d.update(runs=[]))
        with self.assertRaisesRegex(ValueError, 'six-fixture denominator'):
            evidence.semantics(self.directory)

    def test_partial_tour(self):
        self.change('native-tour-42/receipt.json', lambda d: d['runs'].pop())
        with self.assertRaisesRegex(ValueError, 'tour incomplete'):
            evidence.semantics(self.directory)

    def test_real_raw_intent_difference(self):
        def edit(d):
            outcome = d['arm']['Shot']['Outcome']
            outcome['IntentBytes'] = 'FF' + outcome['IntentBytes'][2:]
        self.shadow(edit)
        with self.assertRaisesRegex(ValueError, 'exact native outcome'):
            evidence.semantics(self.directory)

    def test_real_raw_replay_difference(self):
        def edit(d):
            outcome = d['arm']['Shot']['Outcome']
            records = json.loads(outcome['ReplayFacts'])
            records[0]['Payload'] = 'FF' + records[0]['Payload'][2:]
            outcome['ReplayFacts'] = json.dumps(records, separators=(',', ':'))
        self.shadow(edit)
        with self.assertRaisesRegex(ValueError, 'exact native outcome'):
            evidence.semantics(self.directory)

    def test_real_canonical_fire_difference(self):
        def edit(d):
            arm = d['arm']
            arm['Shot']['Fire']['CanonicalBytes'] = 'FF' + arm['Shot']['Fire']['CanonicalBytes'][2:]
            arm['Certificate']['Fire'] = copy.deepcopy(arm['Shot']['Fire'])
        self.shadow(edit)
        with self.assertRaisesRegex(ValueError, 'exact native outcome'):
            evidence.semantics(self.directory)

    def test_room_incarnation_is_never_normalized(self):
        self.shadow(lambda d: d['arm']['Spawn'].update(Room=2))
        with self.assertRaisesRegex(ValueError, 'certificate|funding|parity'):
            evidence.semantics(self.directory)

    def test_raw_ledger_answer_required(self):
        self.shadow(lambda d: d['arm']['Certificate']['LedgerAnswer'].update(Sequence=999))
        with self.assertRaisesRegex(ValueError, 'raw ledger answer'):
            evidence.semantics(self.directory)

    def test_real_entry_identity_required(self):
        self.shadow(lambda d: d.update(entryAssemblySha256=evidence.THIN))
        with self.assertRaisesRegex(ValueError, 'loaded engine/entry'):
            evidence.semantics(self.directory)

    def test_thin_reader_not_full_fixture(self):
        self.change('original3-scene-consumers/receipt.json', lambda d: d.update(physicalThinEntrySha256=evidence.ENTRY))
        with self.assertRaisesRegex(ValueError, 'reader identity scope'):
            evidence.semantics(self.directory)

    def test_owned_survivor_rejected(self):
        self.change('native-tour-42/receipt.json', lambda d: d['runs'][0]['ownership'].update(membersAfterClose=['survivor']))
        with self.assertRaisesRegex(ValueError, 'owned process failed'):
            evidence.semantics(self.directory)

    def test_gl_missing_comparisons(self):
        self.change('gl-regression-attempt1/parity.json', lambda d: d.update(comparisons=[]))
        with self.assertRaisesRegex(ValueError, 'raw15 comparisons'):
            evidence.gl_semantics(self.directory)

    def test_gl_depth_bits_difference(self):
        self.change('gl-regression-attempt1/parity.json', lambda d: d['comparisons'][1].update(candidateSha256='0' * 64))
        with self.assertRaisesRegex(ValueError, 'pixel/depth evidence differs'):
            evidence.gl_semantics(self.directory)

    def test_gl_name_retirement_not_empty_or_inferred(self):
        self.change('gl-regression-attempt1/00-eight-high-palette-0.json', lambda d: d['cleanup']['detailedRetirement'].update(buffersRecognizedBefore=0))
        self.change('gl-regression-executor-receipt.json', lambda d: d['contexts'][0]['cleanup']['detailedRetirement'].update(buffersRecognizedBefore=0))
        with self.assertRaisesRegex(ValueError, 'name retirement missing'):
            evidence.gl_semantics(self.directory)

    def test_gl_owned_names_must_be_deleted(self):
        self.change('gl-regression-attempt1/00-eight-high-palette-0.json', lambda d: d['cleanup']['detailedRetirement'].update(bufferNamesRecognizedAfter=1))
        self.change('gl-regression-executor-receipt.json', lambda d: d['contexts'][0]['cleanup']['detailedRetirement'].update(bufferNamesRecognizedAfter=1))
        with self.assertRaisesRegex(ValueError, 'name retirement missing'):
            evidence.gl_semantics(self.directory)

    def test_gl_signaled_shutdown_not_natural_closure(self):
        self.change('gl-regression-executor-receipt.json', lambda d: d['process']['ownership'].update(signals=['SIGKILL']))
        with self.assertRaisesRegex(ValueError, 'executor ownership failed'):
            evidence.gl_semantics(self.directory)

    def test_capture_hash_inventory_cannot_change(self):
        self.change('manifest.json', lambda d: d['captureFiles'][0].update(sha256='0' * 64))
        with self.assertRaisesRegex(ValueError, 'exact30original hashes'):
            evidence.verify(self.directory)

    def test_capture_only_cannot_claim_performance(self):
        self.change('gl-regression-attempt1/validation.json', lambda d: d.update(performanceDisposition='PASS'))
        with self.assertRaisesRegex(ValueError, 'must not claim performance'):
            evidence.gl_semantics(self.directory)

    def test_original_collector_negative_retained(self):
        self.change('receipt.json', lambda d: d['steps'][0].update(ownedProcessGroupClosed=True))
        with self.assertRaisesRegex(ValueError, 'original collector closure failure'):
            evidence.semantics(self.directory)


if __name__ == '__main__':
    unittest.main()
