"""Lossless deterministic bounded JSON text chunks for portable evidence."""
import hashlib
import json
from pathlib import Path

MAX_CHUNK_BYTES = 1024 * 1024
TEXT_CHARACTERS = 128 * 1024


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def encode(value):
    return (json.dumps(value, separators=(',', ':'), ensure_ascii=False) + '\n').encode()


def write_chunks(document, folder):
    """Write canonical compact UTF-8 JSON split at deterministic character fences."""
    raw = encode(document)
    text = raw.decode()
    folder.mkdir(parents=True, exist_ok=True)
    records, offset = [], 0
    for ordinal, start in enumerate(range(0, len(text), TEXT_CHARACTERS)):
        part = text[start:start + TEXT_CHARACTERS]
        payload = part.encode()
        end = offset + len(payload)
        item = {'schema': 1, 'ordinal': ordinal, 'startUtf8Byte': offset,
                'endUtf8Byte': end, 'text': part}
        encoded = encode(item)
        if len(encoded) > MAX_CHUNK_BYTES:
            raise ValueError('encoded chunk exceeds bound')
        name = f'{ordinal:06d}.json'
        (folder / name).write_bytes(encoded)
        records.append({'path': name, 'ordinal': ordinal, 'startUtf8Byte': offset,
                        'endUtf8Byte': end, 'sha256': digest(encoded), 'bytes': len(encoded)})
        offset = end
    return {'schema': 1, 'encoding': 'compact-utf8-json-text-chunks-v1',
            'textCharactersPerChunk': TEXT_CHARACTERS, 'maximumChunkBytes': MAX_CHUNK_BYTES,
            'projectedJSONSHA256': digest(raw), 'projectedJSONBytes': len(raw),
            'chunkCount': len(records), 'chunks': records}


def read_chunks(index, folder):
    """Require complete contiguous ordered chunks and original projection digest."""
    if (index['encoding'] != 'compact-utf8-json-text-chunks-v1' or
            index['maximumChunkBytes'] != MAX_CHUNK_BYTES or
            index['textCharactersPerChunk'] != TEXT_CHARACTERS or
            index['chunkCount'] != len(index['chunks'])):
        raise ValueError('chunk index schema/count/bound')
    parts, offset = [], 0
    for ordinal, record in enumerate(index['chunks']):
        path = folder / record['path']
        if not path.resolve().is_relative_to(folder.resolve()):
            raise ValueError('external chunk')
        raw = path.read_bytes()
        if (len(raw) != record['bytes'] or len(raw) > MAX_CHUNK_BYTES or
                digest(raw) != record['sha256']):
            raise ValueError('chunk identity/bound')
        item = json.loads(raw)
        if (record['ordinal'] != ordinal or item['ordinal'] != ordinal or
                item['schema'] != 1 or item['startUtf8Byte'] != record['startUtf8Byte'] or
                item['endUtf8Byte'] != record['endUtf8Byte'] or
                item['startUtf8Byte'] != offset):
            raise ValueError('chunk order/range')
        payload = item['text'].encode()
        if len(payload) != item['endUtf8Byte'] - offset:
            raise ValueError('chunk byte range')
        parts.append(payload)
        offset = item['endUtf8Byte']
    raw = b''.join(parts)
    if len(raw) != index['projectedJSONBytes'] or digest(raw) != index['projectedJSONSHA256']:
        raise ValueError('recomposed projection identity')
    document = json.loads(raw)
    if encode(document) != raw:
        raise ValueError('noncanonical compact encoding')
    return document
