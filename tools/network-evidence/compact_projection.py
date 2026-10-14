"""Declared original-index/value projections; no runtime acceptance inference."""
import hashlib
import json
import re
from collections import Counter

WINDOW_PATHS = ['Seconds', 'Epoch', 'Match', 'Frame', 'LocalSlot', 'Frames'] + [
    'Network.Presentation.' + k for k in ['Delay', 'JitterFrames', 'Interpolated',
                                         'Held', 'Starved', 'Snaps', 'StalledFrames', 'LongestStall']]
TRANSPORT_PATHS = ['Wire.StartedAt', 'Wire.CapturedAt', 'Wire.ElapsedSeconds'] + [
    'Wire.Totals.' + k for k in ['RxPackets', 'RxBytes', 'TxPackets', 'TxBytes',
        'DequeuedPackets', 'TotalQueueAgeMilliseconds', 'MaxQueueAgeMilliseconds', 'QueueAgeHistogram']
] + ['Queue.' + k for k in ['PacketsReceived', 'PacketsSent', 'BytesReceived', 'BytesSent',
    'Invalid', 'Oversized', 'QueueCurrent', 'QueueHighWater', 'QueueDrops', 'Coalesced',
    'Processed', 'ProcessingMilliseconds', 'SocketErrors']
] + ['Contention.' + k for k in ['Acquisitions', 'Contended', 'TotalWaitMilliseconds',
    'MaximumWaitMilliseconds', 'TotalHoldMilliseconds', 'MaximumHoldMilliseconds']]
DIAGNOSTIC_PATTERN = r'Exception|FATAL|\bERROR\b|ServerStepAggregate|DroppedSteps|\bstalls?\b|\bdropped\b|\bfail(?:ed|ure)?\b'


def canonical_identity(value):
    raw = json.dumps(value, separators=(',', ':'), ensure_ascii=False).encode()
    return {'canonicalValueSHA256': hashlib.sha256(raw).hexdigest(), 'canonicalValueBytes': len(raw),
            'entryCount': len(value)}


def key_tuple(key):
    return tuple(key[k] for k in ['AuthorityEpoch', 'MatchId', 'ShooterSlot',
                                 'Generation', 'LifeId', 'ShotId'])


def positive_and_funded_keys(server):
    return sorted({key_tuple(c['Shot']['Key']) for c in server['TimedCaseJoins']['Certificates']
                   if c['Shot']['Source']['Source'] == 0 or c['Shot']['Funding'] is not None})


def funded_keys(server):
    return sorted({key_tuple(c['Shot']['Key']) for c in server['TimedCaseJoins']['Certificates']
                   if c['Shot']['Funding'] is not None})


def metric_table(values, columns):
    rows = []
    for value in values:
        row = []
        for path in columns:
            item = value
            for key in path.split('.'):
                item = item[key]  # Missing is a producer/schema failure, never fabricated null/zero.
            row.append(item)
        rows.append(row)
    return {'schema': 1, 'columns': columns, 'rows': rows}


def project_role(document, keys, chronology, key_scope=None):
    metadata = {'rule': 'bounded-proof-projection-v1', 'qualifyingShotKeys': keys,
                'chronology': chronology, 'fields': []}
    if key_scope is not None:
        metadata.update(rule='bounded-proof-projection-v2', keySelectionScope=key_scope)
    for field, columns in [('Windows', WINDOW_PATHS), ('TransportWindows', TRANSPORT_PATHS)]:
        original = document[field]
        info = {'path': [field], 'selection': 'every original row, fixed declared metric columns',
                **canonical_identity(original), 'columns': columns}
        document[field] = metric_table(original, columns)
        metadata['fields'].append(info)
    original = document['Shots']; key_set = {tuple(k) for k in keys}
    indices = []
    for index, row in enumerate(original):
        key = key_tuple(row['Key'])
        if key in key_set or (chronology and key[:5] == tuple(chronology['life']) and
                             chronology['firstShot'] <= key[5] <= chronology['lastShot']):
            indices.append(index)
    kept = [original[i] for i in indices]
    info = {'path': ['Shots'], 'selection': 'all rows for source-positive/funded certificate keys plus exact declared life/shot chronology',
            **canonical_identity(original), 'selectedOriginalIndices': indices,
            'keptOriginalIdentity': canonical_identity(kept),
            'countsByStage': dict(sorted(Counter(str(r['Stage']) for r in original).items())),
            'countsByReason': dict(sorted(Counter(str(r['Reason']) for r in original).items())),
            'countsByWeaponKind': dict(sorted(Counter(str((r['Weapon'], r['Kind'])) for r in original).items()))}
    if key_scope is not None:
        info['selection'] = 'all complete Shot rows for every funded certificate key; all other positive/contradictory authority certificate and ShotProof rows retained in full'
    document['Shots'] = kept; metadata['fields'].append(info)
    original = document.pop('Facts')
    metadata['fields'].append({'path': ['Facts'], 'selection': 'complete unrelated semantic payload field omitted',
        **canonical_identity(original), 'countsByType': dict(sorted(Counter(str(r['Type']) for r in original).items()))})
    return metadata


def apply_declared_role_projection(document, specification):
    actual = project_role(document, specification['qualifyingShotKeys'], specification['chronology'],
                          specification.get('keySelectionScope'))
    if actual != specification:
        raise ValueError('exact original field/indices/counts/metric selection')
    return document


def project_log(text):
    lines = text.splitlines(keepends=True)
    selected = sorted(set(range(min(20, len(lines)))) |
        set(range(max(0, len(lines) - 30), len(lines))) |
        {i for i, line in enumerate(lines) if re.search(DIAGNOSTIC_PATTERN, line, re.IGNORECASE)})
    kept = ''.join(lines[i] for i in selected)
    return kept, {'rule': 'first20-last30-all-diagnostic-matches-v1', 'pattern': DIAGNOSTIC_PATTERN,
        'originalLineCount': len(lines), 'selectedOriginalIndices': selected,
        'selectedOriginalTextSHA256': hashlib.sha256(kept.encode()).hexdigest(),
        'selectedOriginalTextBytes': len(kept.encode())}
