#!/usr/bin/env python3
"""Verify private NCX same-frame RGB captures without promoting flight to a hit."""
import argparse
from collections import Counter, defaultdict
import hashlib
import io
import json
import math
from pathlib import Path
import re
import struct
import zlib

from ncx_timeline import STAGES, integer, row_value, token

try:
    from PIL import Image
except ImportError:
    Image = None

MAX_PIXELS = 4096 * 4096
MAX_PNG_BYTES = 64 * 1024 * 1024
HEX = re.compile(r'[0-9a-fA-F]{64}')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def png_rgb(data):
    """Bounded RGB8, non-interlaced PNG decoder; output is top-down RGB bytes."""
    if len(data) > MAX_PNG_BYTES or data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('invalid PNG')
    position, dimensions, packed, ended = 8, None, bytearray(), False
    seen_idat, idat_finished = False, False
    while position < len(data):
        if position + 12 > len(data):
            raise ValueError('truncated PNG')
        length, = struct.unpack_from('>I', data, position)
        kind = data[position + 4:position + 8]
        end = position + 12 + length
        if end > len(data) or length > MAX_PNG_BYTES:
            raise ValueError('invalid PNG chunk length')
        chunk = data[position + 8:position + 8 + length]
        crc, = struct.unpack_from('>I', data, position + 8 + length)
        if zlib.crc32(kind + chunk) & 0xffffffff != crc:
            raise ValueError('PNG CRC mismatch')
        if dimensions is None and kind != b'IHDR':
            raise ValueError('PNG lacks leading header')
        if kind == b'IHDR':
            if dimensions is not None or length != 13:
                raise ValueError('invalid PNG header')
            width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', chunk)
            if not width or not height or width > 4096 or height > 4096 or width * height > MAX_PIXELS or (depth, color, compression, filtering, interlace) != (8, 2, 0, 0, 0):
                raise ValueError('unsupported PNG dimensions/format')
            dimensions = width, height
        elif kind == b'IDAT':
            if idat_finished:
                raise ValueError('noncontiguous PNG data')
            seen_idat = True
            packed.extend(chunk)
        elif kind == b'IEND':
            if length or not seen_idat or end != len(data):
                raise ValueError('invalid PNG end')
            ended = True
            break
        elif not kind[0] & 32:
            raise ValueError('unsupported critical PNG chunk')
        elif seen_idat:
            idat_finished = True
        position = end
    if not ended or dimensions is None:
        raise ValueError('incomplete PNG')
    width, height = dimensions
    stride, expected = width * 3, height * (width * 3 + 1)
    decoder = zlib.decompressobj()
    raw = decoder.decompress(packed, expected + 1)
    if len(raw) != expected or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
        raise ValueError('PNG expanded size mismatch')
    if Image is not None:
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            if image.size != dimensions or image.mode != 'RGB':
                raise ValueError('decoded PNG format differs')
            return width, height, image.tobytes()
    result, previous = bytearray(), bytearray(stride)
    for y in range(height):
        offset = y * (stride + 1)
        mode, scan = raw[offset], bytearray(raw[offset + 1:offset + 1 + stride])
        if mode > 4:
            raise ValueError('unknown PNG filter')
        if mode == 0:
            result.extend(scan)
            previous = scan
            continue
        for x in range(stride):
            left, up, upper_left = scan[x - 3] if x >= 3 else 0, previous[x], previous[x - 3] if x >= 3 else 0
            if mode == 1:
                prediction = left
            elif mode == 2:
                prediction = up
            elif mode == 3:
                prediction = (left + up) // 2
            elif mode == 4:
                p = left + up - upper_left
                distances = abs(p - left), abs(p - up), abs(p - upper_left)
                prediction = (left, up, upper_left)[distances.index(min(distances))]
            else:
                prediction = 0
            scan[x] = (scan[x] + prediction) & 255
        result.extend(scan)
        previous = scan
    return width, height, bytes(result)


def read_json(path):
    if path.stat().st_size > MAX_PNG_BYTES:
        raise ValueError('JSON evidence exceeds bound')
    return json.loads(path.read_text())


def presentation(report):
    try:
        schedule = report['PresentationSchedule']
        if schedule['Enabled'] is not True:
            raise ValueError('disabled schedule')
        requested, elapsed = schedule['RequestedHz'], schedule['ElapsedSeconds']
        if type(requested) not in (int, float) or not math.isfinite(requested) or requested <= 0:
            raise ValueError('invalid requested rate')
        if type(elapsed) not in (int, float) or not math.isfinite(elapsed) or elapsed <= 0:
            raise ValueError('invalid rate duration')
        frames = integer(schedule['PresentedFrames'], 1, (1 << 63) - 1)
        actual = frames / elapsed
        if not math.isfinite(actual):
            raise ValueError('invalid achieved rate')
        return dict(status='OBSERVED', requestedHz=requested, actualHz=actual,
                    presentedFrames=frames, elapsedSeconds=elapsed,
                    simulationSteps=integer(schedule['SimulationSteps'], 0, (1 << 63) - 1),
                    droppedSteps=integer(schedule['DroppedSteps'], 0, (1 << 63) - 1),
                    stalls=integer(schedule['Stalls'], 0, (1 << 63) - 1))
    except (KeyError, TypeError, ValueError, OverflowError):
        return dict(status='UNAVAILABLE')


def observer_without_fire(report, local):
    """Bounded complete owner timeline is required to label a no-fire harness."""
    try:
        timeline = report['ShotTimeline']
        if type(timeline['Schema']) is not int or timeline['Schema'] != 1 or timeline['Complete'] is not True:
            return False
        if integer(timeline['Overflow'], 0, (1 << 63) - 1) or integer(timeline['UnkeyedRows'], 0, (1 << 63) - 1):
            return False
        if not isinstance(timeline['Rows'], list):
            return False
        for row in timeline['Rows']:
            key = row_value(row)
            if row['Stage'] not in STAGES:
                return False
            if key[2] == local and row['Stage'] in ('source-authored', 'source-emitted'):
                return False
        return True
    except (KeyError, TypeError, ValueError):
        return False


def verify_pixels(sample, frame_dir, sample_index):
    width, height = integer(sample['Width'], 1, 4096), integer(sample['Height'], 1, 4096)
    if width * height > MAX_PIXELS:
        raise ValueError('sample dimensions exceed bound')
    integer(sample['Timestamp'], 0, (1 << 63) - 1)
    integer(sample['ClockFrequency'], 1, (1 << 63) - 1)
    integer(sample['CaptureTicks'], 0, (1 << 63) - 1)
    integer(sample['Attempt'], 1, 3)
    if sample['GameplayAndCameraUnchanged'] is not True or sample['GlErrors'] != 0 or type(sample['GlErrors']) is not int or sample['Failure'] is not None:
        raise ValueError('capture guard or GL failed')
    if integer(sample['Contributors'], 1, 8192) != 1:
        raise ValueError('ambiguous contributors')
    images = sample['Images']
    if not isinstance(images, list) or len(images) != 4:
        raise ValueError('four images required')
    pixels, hashes = {}, {}
    for image in images:
        name = image['File']
        if not isinstance(name, str) or Path(name).name != name or not re.fullmatch(r'sample-[0-9]{3}-(actual|excluded|restored|mask)\.png', name):
            raise ValueError('invalid local image filename')
        label = name.rsplit('-', 1)[1][:-4]
        if name != f'sample-{sample_index:03}-{label}.png':
            raise ValueError('image does not belong to sample ordinal')
        if label in pixels:
            raise ValueError('duplicate image role')
        path = (frame_dir / name).resolve()
        if path.parent != frame_dir.resolve() or path.stat().st_size > MAX_PNG_BYTES:
            raise ValueError('image escapes capture directory or exceeds bound')
        data = path.read_bytes()
        for field in ('PngSha256', 'RgbSha256'):
            if not isinstance(image[field], str) or not HEX.fullmatch(image[field]):
                raise ValueError('missing image digest')
        if digest(data) != image['PngSha256'].lower():
            raise ValueError('PNG digest mismatch')
        w, h, rgb = png_rgb(data)
        if (w, h) != (width, height):
            raise ValueError('PNG dimensions differ')
        # Producer hashes detached GL RGB in bottom-up order; PNG is top-down.
        stride = width * 3
        bottom_up = b''.join(rgb[y * stride:(y + 1) * stride] for y in range(height - 1, -1, -1))
        if digest(bottom_up) != image['RgbSha256'].lower():
            raise ValueError('RGB digest mismatch')
        pixels[label] = rgb
        hashes[label] = dict(pngSha256=image['PngSha256'].lower(), rgbSha256=image['RgbSha256'].lower())
    if set(pixels) != {'actual', 'excluded', 'restored', 'mask'}:
        raise ValueError('image roles missing')
    if pixels['actual'] != pixels['restored'] or sample['UnstablePixels'] != 0 or type(sample['UnstablePixels']) is not int:
        raise ValueError('restoration negative control failed')
    changed = 0
    min_x, min_y, max_x, max_y = width, height, -1, -1
    expected_mask = bytearray(width * height * 3)
    actual, excluded = pixels['actual'], pixels['excluded']
    for p in range(width * height):
        i = p * 3
        if actual[i] == excluded[i] and actual[i + 1] == excluded[i + 1] and actual[i + 2] == excluded[i + 2]:
            continue
        changed += 1
        expected_mask[i:i + 3] = b'\xff\xff\xff'
        x, y = p % width, p // width
        min_x, min_y, max_x, max_y = min(min_x, x), min(min_y, y), max(max_x, x), max(max_y, y)
    if pixels['mask'] != expected_mask:
        raise ValueError('pixel attribution mask differs')
    bounds = dict(Pixels=changed, MinX=min_x if changed else -1, MinY=min_y if changed else -1,
                  MaxX=max_x, MaxY=max_y)
    if not isinstance(sample['Changed'], dict) or any(type(sample['Changed'].get(f)) is not int or sample['Changed'][f] != v for f, v in bounds.items()):
        raise ValueError('reported pixel count/bounds differ')
    status = 'VISIBLE' if changed else 'NO_TARGET_PIXELS'
    if sample['Status'] != status:
        raise ValueError('reported pixel status differs')
    return dict(status=status, width=width, height=height, changedPixels=changed,
                bounds={k: v for k, v in bounds.items() if k != 'Pixels'}, verifiedImageHashes=hashes)


def audit(campaign):
    campaign = Path(campaign)
    issues, authority, payments, contacts, source_slots = [], {}, defaultdict(list), defaultdict(list), {}
    try:
        server = read_json(campaign / 'server.json')
        timeline = server['ShotTimeline']
        if type(timeline['Schema']) is not int or timeline['Schema'] != 1 or timeline['Complete'] is not True or integer(timeline['Overflow'], 0, (1 << 63) - 1) or integer(timeline['UnkeyedRows'], 0, (1 << 63) - 1):
            issues.append('authority timeline incomplete')
        if timeline['Clock']['Domain'] != 'ProcessStopwatch':
            raise ValueError('unknown authority clock')
        integer(timeline['Clock']['Frequency'], 1, (1 << 63) - 1)
        for row in timeline['Rows']:
            try:
                key = row_value(row)
                if row['Stage'] not in STAGES:
                    raise ValueError('unknown authority stage')
                shot = token(key)
                source_slots[shot] = key[2]
                authority[shot] = True
                primary = shot, row['Component'], row['Phase']
                if row['Stage'] == 'payment-committed':
                    payments[primary].append(row)
                elif row['Stage'] == 'native-contact':
                    contacts[primary].append(row)
            except (KeyError, TypeError, ValueError):
                issues.append('invalid authority observation')
    except (OSError, KeyError, TypeError, ValueError):
        issues.append('authority evidence unavailable')
    paid_shots = {primary[0] for primary in payments}
    labels = {p.name for p in campaign.iterdir() if p.is_dir() and re.fullmatch(r'(peer|client)-[0-9]+', p.name)}
    try:
        manifest = read_json(campaign / 'manifest.json')
        if not isinstance(manifest, dict):
            raise ValueError('invalid manifest')
        for command in manifest.get('commands', []):
            name = command.get('name') if isinstance(command, dict) else None
            if isinstance(name, str) and re.fullmatch(r'(peer|client)-[0-9]+', name):
                labels.add(name)
    except (OSError, TypeError, ValueError):
        issues.append('campaign manifest unavailable')
    cameras = []
    for index, label in enumerate(sorted(labels)):
        camera = dict(camera='camera-' + str(index), samples=[], presentation=dict(status='UNAVAILABLE'))
        cameras.append(camera)
        try:
            peer = read_json(campaign / (label + '.json'))
            camera['presentation'] = presentation(peer)
            if camera['presentation']['status'] != 'OBSERVED':
                issues.append(camera['camera'] + ': presentation schedule unavailable')
            path = campaign / label / 'ncx-visibility.json'
            if path.parent.resolve().parent != campaign.resolve() or Path(str(path) + '.frames').resolve().parent != path.parent.resolve():
                raise ValueError('capture directory escapes campaign')
            raw = read_json(path)
            capacity = integer(raw['Capacity'], 1, 64)
            samples = raw['Samples']
            if type(raw['Schema']) is not int or raw['Schema'] != 1 or not isinstance(samples, list) or len(samples) > capacity:
                raise ValueError('invalid visibility schema/count')
            if integer(raw['Overflow'], 0, (1 << 63) - 1):
                issues.append(camera['camera'] + ': visibility overflow')
            camera['capturedOfferedSamples'] = len(samples)
            camera['samplingCapped'] = len(samples) == capacity
            if camera['samplingCapped']:
                issues.append(camera['camera'] + ': capped sampling; full-source visibility coverage unavailable')
            for ordinal, sample in enumerate(samples):
                observation = dict(sample=ordinal, pixelStatus='UNAVAILABLE', authorityJoin='UNAVAILABLE', role='UNAVAILABLE')
                camera['samples'].append(observation)
                try:
                    shot = sample['Shot']
                    if not isinstance(shot, str) or not HEX.fullmatch(shot) or shot.lower() not in authority:
                        raise ValueError('shot identity has no authority source')
                    shot = shot.lower()
                    component = integer(sample['Component'], 0, (1 << 32) - 1)
                    phase = integer(sample['Phase'], 0, (1 << 32) - 1)
                    kind = integer(sample['Kind'], 0, 1)
                    if kind == 0 and component == 0:
                        raise ValueError('flight has no component')
                    observation.update(shot=shot, component=component, phase=phase, kind='flight' if kind == 0 else 'cue')
                    pixels = verify_pixels(sample, Path(str(path) + '.frames'), ordinal)
                    observation.update(pixelStatus=pixels.pop('status'), pixels=pixels)
                    local = integer(sample['LocalSlot'], -1, 7)
                    victim = (integer(sample['Victim'], 0, 255), integer(sample['VictimGeneration'], 0, 65535), integer(sample['VictimLife'], 0, 65535))
                    event = integer(sample['DamageEvent'], 0, 65535)
                    weapon = integer(sample['Weapon'], 0, 255)
                    provenance = integer(sample['Provenance'], 0, 3)
                    if kind == 1 and (victim[0] >= 8 or not victim[1] or not victim[2] or not event or not provenance):
                        raise ValueError('cue lacks exact victim/event/provenance')
                    if type(sample.get('Spectating')) is bool:
                        if sample['Spectating']:
                            observation['role'] = 'observer'
                        elif sample.get('ObserverHarness') is True:
                            if local >= 0 and observer_without_fire(peer, local):
                                observation['role'] = 'observer'
                            else:
                                issues.append(camera['camera'] + ': no-fire observer proof unavailable')
                        else:
                            observation['role'] = ('shooter' if local == source_slots[shot]
                                                   else 'victim' if kind == 1 and local == victim[0] else 'other-player')
                    else:
                        issues.append(camera['camera'] + ': camera role unavailable')
                    candidates = [r for r in payments[(shot, component, phase)] if r['Weapon'] == weapon]
                    native = [r for r in contacts[(shot, component, phase)] if r['Weapon'] == weapon]
                    if kind == 1:
                        candidates = [r for r in candidates if (r['Victim'], r['VictimGeneration'], r['VictimLife']) == victim
                                      and r['DamageEventId'] == event and r['Weapon'] == weapon]
                        native = [r for r in native if (r['Victim'], r['VictimGeneration'], r['VictimLife']) == victim and r['Weapon'] == weapon]
                    compatible = len(native) == 0 or (len(candidates) == len(native) == 1 and
                        all(candidates[0][f] == native[0][f] for f in ('Kind', 'Victim', 'VictimGeneration', 'VictimLife')))
                    if kind == 1 and provenance == 1 and not native:
                        compatible = False
                    if len(candidates) == 1 and len(native) <= 1 and compatible:
                        observation['authorityJoin'] = 'UNIQUE_PAYMENT'
                    elif kind == 0 and not candidates and len(native) == 1:
                        observation['authorityJoin'] = 'UNIQUE_NATIVE_CONTACT'
                    else:
                        issues.append(camera['camera'] + ': authority association unavailable or ambiguous')
                    observation['authorityConfirmedCue'] = kind == 1 and observation['authorityJoin'] == 'UNIQUE_PAYMENT'
                    observation['flightIsAuthorityConfirmation'] = False
                except (OSError, KeyError, TypeError, ValueError, zlib.error, struct.error):
                    observation['pixelStatus'] = 'UNAVAILABLE'
                    issues.append(camera['camera'] + ': invalid pixel sample ' + str(ordinal))
            statuses = Counter(r['pixelStatus'] for r in camera['samples'])
            camera['verifiedVisibleSamples'] = statuses['VISIBLE']
            camera['verifiedNoTargetSamples'] = statuses['NO_TARGET_PIXELS']
            camera['campaignUniquePaidShots'] = len(paid_shots)
            camera['uniquePaidShotAssociations'] = len({r['shot'] for r in camera['samples'] if r['authorityJoin'] == 'UNIQUE_PAYMENT'})
            if not samples or not statuses['VISIBLE']:
                issues.append(camera['camera'] + ': no verified positive pixel sample')
        except (OSError, KeyError, TypeError, ValueError):
            issues.append(camera['camera'] + ': visibility evidence unavailable')
    if not cameras:
        issues.append('no camera evidence')
    return dict(schema=1, status='INCOMPLETE' if issues else 'COMPLETE', cameras=cameras,
                campaignUniquePaidShots=len(paid_shots), incomplete=issues,
                scope=dict(pixels='same-prepared-frame actual camera world target before HUD; exclusion plus exact restoration and mask controls',
                           denominator='captured offered samples and campaign unique paid shots are separate; no expected-camera visibility or legal-hit denominator inferred',
                           noTargetPixels='absence of attributed pixels does not establish an illegal or missed hit',
                           flight='visible flight is not authority confirmation; cue confirmation requires unique exact paid victim/event association',
                           timing='intrusive renders/readbacks disqualify CPU timing comparisons; requested and achieved presentation rates are separate'))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('campaign', type=Path)
    args = parser.parse_args(argv)
    result = audit(args.campaign)
    with (args.campaign / 'ncx-visibility-audit.json').open('x') as output:
        json.dump(result, output, indent=2, allow_nan=False)
        output.write('\n')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
