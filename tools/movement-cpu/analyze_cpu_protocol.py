"""Offline-only phase2. Never starts benchmark, WPR, or a simulation process."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import run_cpu_protocol as protocol

GIB = 1024**3


def archive_export(path):
    """Retain lossless gzip+receipt before removing only redundant owned scratch."""
    compressed = path.with_suffix(path.suffix + '.gz')
    receipt = path.with_suffix(path.suffix + '.archive.json')
    if compressed.exists() or receipt.exists():
        raise RuntimeError('archive destination exists; no automatic retry/overwrite')
    temporary = compressed.with_suffix(compressed.suffix + '.partial')
    original_hash = hashlib.sha256()
    original_bytes = 0
    with path.open('rb') as source, gzip.open(temporary, 'wb', compresslevel=6) as target:
        while block := source.read(1024 * 1024):
            original_hash.update(block)
            original_bytes += len(block)
            target.write(block)
    verified_hash = hashlib.sha256()
    verified_bytes = 0
    # Reading to EOF verifies gzip CRC and uncompressed length as well as SHA.
    with gzip.open(temporary, 'rb') as source:
        while block := source.read(1024 * 1024):
            verified_hash.update(block)
            verified_bytes += len(block)
    if original_bytes != verified_bytes or original_hash.digest() != verified_hash.digest():
        raise RuntimeError('lossless archive verification failed; original retained')
    temporary.replace(compressed)
    metadata = dict(originalBytes=original_bytes, originalSha256=original_hash.hexdigest(),
                    verifiedBytes=verified_bytes, verifiedSha256=verified_hash.hexdigest(),
                    gzipBytes=compressed.stat().st_size, gzipSha256=protocol.digest(compressed),
                    crcVerified=True, onlyRedundantOwnedScratchRemoved=True)
    receipt.write_text(json.dumps(metadata, indent=2))
    path.unlink()
    return metadata


def bounded_command(command, directory, name, timeout):
    """Stop only this owned export/parser on declared resource bound failure."""
    start = time.monotonic()
    with (directory / (name + '.log')).open('w') as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        try:
            while process.poll() is None:
                if time.monotonic() - start > timeout:
                    raise RuntimeError(name + ' exceeded declared timeout')
                if shutil.disk_usage(directory).free < 2 * GIB:
                    raise RuntimeError(name + ' reached2GiB emergency disk reserve')
                export_bytes = sum((directory / filename).stat().st_size for filename in ('raw.xml', 'normal.xml', 'dump.txt')
                                   if (directory / filename).exists())
                if export_bytes > 8 * GIB:
                    raise RuntimeError(name + ' exceeded8GiB scratch limit')
                time.sleep(.5)
            if process.returncode:
                raise RuntimeError(name + f' failed({process.returncode})')
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=30)
    return time.monotonic() - start


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--batch', type=Path, required=True)
    parser.add_argument('--xperf', type=Path, required=True)
    args = parser.parse_args()
    batch = args.batch.resolve()
    records = json.loads((batch / 'completed-arms.json').read_text())
    complete = json.loads((batch / 'capture-complete.json').read_text())
    if complete.get('arms') != 32 or complete.get('tracedArms') != 16 or len(records) != 32:
        raise RuntimeError('all fixed timing captures required before phase2')
    try:
        for record in records:
            if record['treatment'] != 'trace':
                continue
            directory = batch / record['directory']
            if (directory / 'analysis-receipt.json').exists() or (directory / 'cpu.json').exists():
                raise RuntimeError('analysis evidence already exists; no automatic rerun')
            if shutil.disk_usage(directory).free < 12 * GIB:
                raise RuntimeError('offline analysis requires12GiB free scratch')
            start = time.monotonic()
            etl = str(directory / 'trace.etl')
            durations = {}
            durations['rawExportSeconds'] = bounded_command(['tracerpt', etl, '-o', str(directory / 'raw.xml'),
                '-of', 'XML', '-rts', '-y'], directory, 'raw-export', 600)
            durations['normalExportSeconds'] = bounded_command(['tracerpt', etl, '-o', str(directory / 'normal.xml'),
                '-of', 'XML', '-summary', str(directory / 'trace-summary.txt'), '-y'], directory, 'normal-export', 600)
            durations['dumpSeconds'] = bounded_command([str(args.xperf), '-i', etl, '-o', str(directory / 'dump.txt'),
                '-a', 'dumper'], directory, 'dump', 600)
            durations['accountSeconds'] = bounded_command([sys.executable, str(Path(__file__).with_name('etw_step_cpu.py')),
                '--raw', str(directory / 'raw.xml'), '--normal', str(directory / 'normal.xml'),
                '--dump', str(directory / 'dump.txt'), '--benchmark', str(directory / 'benchmark.json'),
                '--output', str(directory / 'cpu.json')], directory, 'account', 1200)
            archives = {name: archive_export(directory / name) for name in ('raw.xml', 'normal.xml', 'dump.txt')}
            (directory / 'analysis-receipt.json').write_text(json.dumps(dict(
                elapsedSeconds=time.monotonic()-start, durations=durations, archives=archives,
                retainedEtlSha256=protocol.digest(directory / 'trace.etl')), indent=2))
        protocol.summarize(batch, records)
    except Exception as error:
        (batch / 'analysis-failure.txt').write_text(str(error))
        (batch / 'summary.json').write_text(json.dumps(dict(status='measurement-inconclusive-late-audit-failure',
            acceptanceClaim=False, reason=str(error), allTimingArmsRetained=True), indent=2))
        raise


if __name__ == '__main__':
    main()
