import copy
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import zlib

from ncx_timeline import token
from ncx_visibility import audit, main, png_rgb
from test_ncx_timeline import KEY, report, row


def png(width, height, rgb):
    raw = b''.join(b'\0' + rgb[y * width * 3:(y + 1) * width * 3] for y in range(height))
    return png_filtered(width, height, raw)


def png_filtered(width, height, raw):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw))
            + chunk(b'IEND', b''))


class VisibilityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name)
        self.profile = self.path / 'peer-0'
        self.profile.mkdir()
        self.frames = self.profile / 'ncx-visibility.json.frames'
        self.frames.mkdir()
        self.key = tuple(KEY.values())
        self.shot = token(self.key)
        self.server = report([row('source-emitted'), row('native-contact'), row('payment-committed')], 'server')
        self.peer = dict(PresentationSchedule=dict(Enabled=True, RequestedHz=120, PresentedFrames=100,
                        SimulationSteps=60, ElapsedSeconds=1, DroppedSteps=2, Stalls=3))
        self.sample = dict(Shot=self.shot, Component=1, Phase=0, Kind=1, Victim=1,
                           VictimGeneration=1, VictimLife=1, Attempt=1, DamageEvent=1, Weapon=0,
                           Provenance=1, Contributors=1, LocalSlot=0, Spectating=False,
                           Width=2, Height=2, Timestamp=100, ClockFrequency=1000, CaptureTicks=5,
                           Status='VISIBLE', Changed=dict(Pixels=1, MinX=0, MinY=0, MaxX=0, MaxY=0),
                           UnstablePixels=0, GameplayAndCameraUnchanged=True, GlErrors=0, Failure=None)
        self.images = {'actual': b'\xff\0\0' + b'\0' * 9, 'excluded': b'\0' * 12,
                       'restored': b'\xff\0\0' + b'\0' * 9, 'mask': b'\xff' * 3 + b'\0' * 9}
        self.raw = dict(Schema=1, Capacity=64, Overflow=0, Samples=[self.sample])
        self.manifest = dict(commands=[dict(name='server'), dict(name='peer-0')], secret='private@example.test')

    def tearDown(self):
        self.temp.cleanup()

    def save(self):
        images = []
        for kind, rgb in self.images.items():
            data = png(2, 2, rgb)
            name = 'sample-000-' + kind + '.png'
            (self.frames / name).write_bytes(data)
            images.append(dict(File=name, PngSha256=hashlib.sha256(data).hexdigest(),
                               RgbSha256=hashlib.sha256(rgb[6:] + rgb[:6]).hexdigest()))
        self.sample['Images'] = images
        for name, data in (('server.json', self.server), ('peer-0.json', self.peer), ('manifest.json', self.manifest)):
            (self.path / name).write_text(json.dumps(data))
        (self.profile / 'ncx-visibility.json').write_text(json.dumps(self.raw))

    def result(self):
        self.save()
        return audit(self.path)

    def test_positive_exact_cue_with_pixels_hashes_and_rates(self):
        result = self.result()
        self.assertEqual(result['status'], 'COMPLETE', result)
        sample = result['cameras'][0]['samples'][0]
        self.assertEqual(sample['pixelStatus'], 'VISIBLE')
        self.assertTrue(sample['authorityConfirmedCue'])
        self.assertEqual(sample['role'], 'shooter')
        self.assertEqual(result['cameras'][0]['presentation']['actualHz'], 100)
        self.assertEqual(result['cameras'][0]['presentation']['requestedHz'], 120)
        self.assertEqual(result['cameras'][0]['presentation']['droppedSteps'], 2)
        text = json.dumps(result)
        for secret in ('private@example.test', 'peer-0', 'AuthorityEpoch', self.temp.name):
            self.assertNotIn(secret, text)

    def test_flight_never_confirms_authority_payment(self):
        self.sample.update(Kind=0, Victim=255, VictimGeneration=0, VictimLife=0, DamageEvent=0, Provenance=0)
        sample = self.result()['cameras'][0]['samples'][0]
        self.assertEqual(sample['pixelStatus'], 'VISIBLE')
        self.assertFalse(sample['authorityConfirmedCue'])
        self.assertFalse(sample['flightIsAuthorityConfirmation'])

    def test_exact_victim_life_and_damage_event_required_for_cue_join(self):
        for field, value in (('VictimLife', 2), ('DamageEvent', 2), ('Component', 2), ('Phase', 2)):
            original = dict(self.sample)
            self.sample[field] = value
            sample = self.result()['cameras'][0]['samples'][0]
            self.assertEqual(sample['authorityJoin'], 'UNAVAILABLE')
            self.assertFalse(sample['authorityConfirmedCue'])
            self.sample = original
            self.raw['Samples'] = [self.sample]

    def test_duplicate_authority_outcomes_are_ambiguous(self):
        self.server['ShotTimeline']['Rows'].append(row('payment-committed'))
        result = self.result()
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['cameras'][0]['samples'][0]['authorityJoin'], 'UNAVAILABLE')
        self.assertEqual(result['campaignUniquePaidShots'], 1)

    def test_restore_guard_gl_and_contributor_failures_cannot_certify(self):
        for field, bad in (('GameplayAndCameraUnchanged', False), ('GlErrors', 1), ('Contributors', 2), ('UnstablePixels', 1)):
            original = self.sample[field]
            self.sample[field] = bad
            self.assertEqual(self.result()['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')
            self.sample[field] = original
        self.images['restored'] = b'\0' * 12
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')

    def test_mask_count_and_dimensions_are_recomputed_not_trusted(self):
        self.images['mask'] = b'\0' * 12
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')
        self.images['mask'] = b'\xff' * 3 + b'\0' * 9
        self.sample['Changed']['Pixels'] = 2
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')
        self.sample['Changed']['Pixels'] = 1
        self.sample['Width'] = 3
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')

    def test_no_target_pixels_is_observation_not_illegal_hit(self):
        self.images = {kind: b'\0' * 12 for kind in self.images}
        self.sample.update(Status='NO_TARGET_PIXELS', Changed=dict(Pixels=0, MinX=-1, MinY=-1, MaxX=-1, MaxY=-1))
        result = self.result()
        self.assertEqual(result['cameras'][0]['samples'][0]['pixelStatus'], 'NO_TARGET_PIXELS')
        self.assertEqual(result['cameras'][0]['verifiedVisibleSamples'], 0)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertNotIn('illegal', ' '.join(result['incomplete']))

    def test_observer_requires_explicit_spectating_evidence(self):
        self.sample.pop('Spectating')
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'UNAVAILABLE')
        self.sample['Spectating'] = True
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'observer')
        self.sample.update(Spectating=False, LocalSlot=1)
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'victim')

    def test_connected_observer_requires_complete_zero_own_source_timeline(self):
        self.sample.update(Spectating=False, LocalSlot=2, ObserverHarness=True)
        self.peer['ShotTimeline'] = report([row('intent-received')])['ShotTimeline']
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'observer')
        self.peer['ShotTimeline']['Rows'].append(row('source-authored', Key=dict(KEY, ShooterSlot=2)))
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'UNAVAILABLE')
        self.peer['ShotTimeline']['Rows'].pop()
        self.peer['ShotTimeline']['UnkeyedRows'] = 1
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'UNAVAILABLE')
        self.peer.pop('ShotTimeline')
        self.assertEqual(self.result()['cameras'][0]['samples'][0]['role'], 'UNAVAILABLE')

    def test_tampered_png_or_rgb_digest_rejects_without_echoing_path(self):
        self.save()
        self.sample['Images'][0]['RgbSha256'] = '0' * 64
        (self.profile / 'ncx-visibility.json').write_text(json.dumps(self.raw))
        self.assertEqual(audit(self.path)['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')
        self.save()
        (self.frames / 'sample-000-actual.png').write_bytes(b'bad png')
        self.assertEqual(audit(self.path)['cameras'][0]['samples'][0]['pixelStatus'], 'UNAVAILABLE')

    def test_empty_overflow_missing_rate_or_absent_authority_stays_incomplete(self):
        self.raw['Samples'] = []
        self.assertEqual(self.result()['status'], 'INCOMPLETE')
        self.raw['Samples'] = [self.sample]
        self.raw['Overflow'] = 1
        self.assertEqual(self.result()['status'], 'INCOMPLETE')
        self.raw['Overflow'] = 0
        self.peer.clear()
        self.assertEqual(self.result()['cameras'][0]['presentation']['status'], 'UNAVAILABLE')
        self.save()
        (self.path / 'server.json').unlink()
        self.assertEqual(audit(self.path)['status'], 'INCOMPLETE')

    def test_png_crc_and_bounded_expansion(self):
        data = bytearray(png(2, 2, b'\0' * 12))
        data[-1] ^= 1
        with self.assertRaises(ValueError):
            png_rgb(data)
        with self.assertRaises(ValueError):
            png_rgb(png_filtered(1, 1, b'\0' * 100000))
        with self.assertRaises(ValueError):
            png_rgb(png(4097, 1, b'\0' * (4097 * 3)))

    def test_capped_sampling_is_not_full_visibility_coverage(self):
        self.raw['Capacity'] = 1
        result = self.result()
        self.assertTrue(result['cameras'][0]['samplingCapped'])
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['cameras'][0]['verifiedVisibleSamples'], 1)

    def test_arbitrary_untrusted_fields_never_escape_privacy_export(self):
        private = 'private-person@example.test endpoint=203.0.113.99:27888'
        self.sample['Status'] = private
        self.sample['Extra'] = private
        self.server['ShotTimeline']['Rows'].append(row(private))
        result = self.result()
        self.assertNotIn(private, json.dumps(result))
        self.assertEqual(result['status'], 'INCOMPLETE')

    def test_native_contact_and_payment_disagreement_cannot_confirm(self):
        self.server['ShotTimeline']['Rows'][1]['Kind'] = 2
        result = self.result()
        self.assertEqual(result['cameras'][0]['samples'][0]['authorityJoin'], 'UNAVAILABLE')
        self.assertFalse(result['cameras'][0]['samples'][0]['authorityConfirmedCue'])

    def test_stdlib_decoder_all_five_native_png_filters(self):
        rows = [bytes([10, 20, 30, 40, 50, 60]), bytes([15, 25, 35, 50, 60, 70])]
        for mode in range(5):
            filtered, previous = bytearray(), bytes(6)
            for scan in rows:
                filtered.append(mode)
                for x, value in enumerate(scan):
                    left, up, corner = scan[x - 3] if x >= 3 else 0, previous[x], previous[x - 3] if x >= 3 else 0
                    p = left + up - corner
                    distances = abs(p - left), abs(p - up), abs(p - corner)
                    prediction = (0, left, up, (left + up) // 2, (left, up, corner)[distances.index(min(distances))])[mode]
                    filtered.append((value - prediction) & 255)
                previous = scan
            with patch('ncx_visibility.Image', None):
                self.assertEqual(png_rgb(png_filtered(2, 2, filtered))[2], b''.join(rows))

    def test_cli_create_new_preserves_existing_audit(self):
        self.save()
        main([str(self.path)])
        original = (self.path / 'ncx-visibility-audit.json').read_bytes()
        with self.assertRaises(FileExistsError):
            main([str(self.path)])
        self.assertEqual((self.path / 'ncx-visibility-audit.json').read_bytes(), original)


if __name__ == '__main__':
    unittest.main()
