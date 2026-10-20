"""Account benchmark QPC spans from matching tracerpt raw/normal + xperf dumps.
Windows10MHz QPC schema validated on26200; unknown needed schemas fail closed.
No system process names/addresses are emitted. Input exports may contain them.
"""
import argparse, collections, csv, datetime, json, re, itertools, gzip, xml.etree.ElementTree as ET
from etw_cpu_intervals import account, require, target_intervals
from bisect import bisect_left, bisect_right
N = {'e': 'http://schemas.microsoft.com/win/2004/08/events/event', 't': 'http://schemas.microsoft.com/win/2004/08/events/trace'}
THREAD = '{3d6fa8d1-fe05-11d0-9dda-00c04fd7ba7c}'
PERF = '{ce1dbfb4-137e-4da6-87b0-3f59aa102cbc}'

def filetime(text):
    m = re.fullmatch('(.*T\\d\\d:\\d\\d:\\d\\d)\\.(\\d{9})([+-]\\d\\d:\\d\\d|Z)', text)
    require(m is not None and int(m[2]) % 100 == 0, 'unsupported SystemTime precision')
    dt = datetime.datetime.fromisoformat(m[1] + m[3].replace('Z', '+00:00'))
    delta = dt - datetime.datetime(1601, 1, 1, tzinfo=datetime.timezone.utc)
    return (delta.days * 86400 + delta.seconds) * 10000000 + int(m[2]) // 100

def event_blocks(path, chunk_limit=1024 * 1024):
    """Parse bounded chunks, resetting Expat at complete tracerpt Event boundaries.

    Each Event is capped at1MiB; completed events are grouped into approximately
    1MiB chunks. XML root/framing is checked independently across the entire file.
    Plain and losslessly gzip-compressed exports have identical semantics.
    """
    maximum = 1024 * 1024
    require(0 < chunk_limit <= maximum, 'invalid XML chunk limit')
    block = []
    block_size = 0
    chunks = []
    chunk_size = 0
    opened = closed = declaration_seen = False
    opener = gzip.open if str(path).endswith('.gz') else open

    def parse_chunk(parts):
        root = ET.fromstring(b'<Events>' + b''.join(parts) + b'</Events>')
        return list(root)

    with opener(path, 'rb') as stream:
        while True:
            line = stream.readline(maximum + 1)
            if not line:
                break
            require(len(line) <= maximum, 'oversized tracerpt XML line')
            stripped = line.strip().lstrip(b'\xef\xbb\xbf')
            if not block:
                if not stripped:
                    continue
                if stripped.startswith(b'<?xml '):
                    require(not opened and not closed and not declaration_seen,
                            'unexpected XML declaration')
                    ET.fromstring(stripped + b'<Events/>')
                    declaration_seen = True
                    continue
                if stripped == b'<Events>':
                    require(not opened and not closed, 'duplicate XML root')
                    opened = True
                    continue
                if stripped == b'</Events>':
                    require(opened and not closed, 'unexpected XML root end')
                    closed = True
                    continue
                require(opened and not closed and stripped.startswith(b'<Event '),
                        'unsupported tracerpt XML framing')
            block.append(line)
            block_size += len(line)
            require(block_size <= maximum, 'oversized tracerpt Event')
            if stripped == b'</Event>':
                if chunks and chunk_size + block_size > chunk_limit:
                    yield from parse_chunk(chunks)
                    chunks = []
                    chunk_size = 0
                chunks.extend(block)
                chunk_size += block_size
                block = []
                block_size = 0
    require(opened and closed and not block, 'incomplete tracerpt XML framing')
    if chunks:
        yield from parse_chunk(chunks)


def events(path, raw):
    """Yield numeric/audit fields from one independently parsed event."""
    for element in event_blocks(path):
        system = element.find('e:System', N)
        clock = system.find('e:TimeCreated', N).attrib
        yield dict(
            time=int(clock['RawTime']) if raw else filetime(clock['SystemTime']),
            cpu=int(system.find('e:Execution', N).attrib['ProcessorID']),
            version=int(system.findtext('e:Version', namespaces=N)),
            op=int(system.findtext('e:Opcode', namespaces=N)),
            guid=element.findtext('t:ExtendedTracingInfo/t:EventGuid', namespaces=N),
            data={item.attrib['Name']: (item.text or '').strip()
                  for item in element.findall('e:EventData/e:Data', N)},
            decodeError=element.find('e:ProcessingErrorData', N) is not None,
            binary=element.find('e:BinaryEventData', N) is not None)


def decoded(path):
    """Stream only needed decoded rows; never retain the full xperf dump."""
    kinds = {'CSwitch', 'T-Start', 'T-End', 'DPC', 'DPCTmr', 'Interrupt'}
    opener = gzip.open if str(path).endswith('.gz') else open
    with opener(path, 'rt', encoding='utf-8-sig') as stream:
        for row in csv.reader(stream):
            if len(row) > 1 and row[1].strip().isdigit() and row[0].strip() in kinds:
                yield [value.strip() for value in row]



def pid(text):
    m = re.search('\\(\\s*(\\d+)\\)\\s*$', text)
    require(m is not None, 'unrecognized xperf PID field')
    return int(m[1])

def normalize(raw_path, normal_path, dump_path, benchmark):
    """Audit every event, retaining only target-window scheduler/interrupt fields."""
    span = benchmark['stepSpans']
    require(span['qpcFrequency'] == 10000000, 'benchmark clock mismatch')
    target = span['nativeThreadId']
    target_pid = span['processId']
    first = span['rawStartQpc'][0]
    last = span['rawEndQpc'][-1]
    decoded_rows = iter(decoded(dump_path))
    header = None
    origin = offset = None
    event_count = processing_errors = opaque_switches = switch_count = interrupt_count = 0
    previous_switch = {}
    window_switches = collections.defaultdict(list)
    window_after = set()
    interrupts = []
    life = []
    kinds = {(THREAD, 36): ('CSwitch', 5, 16),
             (THREAD, 1): ('T-Start', 3, None),
             (THREAD, 2): ('T-End', 3, None),
             (PERF, 68): ('DPC', 2, 3),
             (PERF, 69): ('DPCTmr', 2, 3),
             (PERF, 67): ('Interrupt', 2, 3),
             (PERF, 50): ('Interrupt', 2, 3)}
    for raw, normal in itertools.zip_longest(events(raw_path, True), events(normal_path, False)):
        require(raw is not None and normal is not None, 'raw/normal export counts differ')
        event_count += 1
        processing_errors += raw['decodeError']
        identity = ('guid', 'op', 'version', 'cpu')
        require(all(raw[key] == normal[key] for key in identity), 'raw/normal event order mismatch')
        difference = normal['time'] - raw['time']
        if offset is None:
            offset = difference
        require(difference == offset, 'nonconstant FILETIME/QPC mapping')
        if 'PerfFreq' in raw['data']:
            require(header is None, 'multiple ETL headers')
            header = raw['data']
            origin = raw['time']
            require(header['ReservedFlags'] == '0x1' and int(header['PerfFreq']) == 10000000,
                    'unsupported/non-QPC clock')
            require(int(header['EndTime']) > int(header['StartTime']), 'trace not finalized')
            for key in ('EventsLost', 'BuffersLost'):
                require(key in header and int(header[key]) == 0, 'trace loss/missing counter: ' + key)
        kind = kinds.get((raw['guid'], raw['op']))
        if kind is None:
            continue
        require(header is not None, 'needed event precedes header')
        name, version, cpu_column = kind
        require(raw['version'] == version, 'unsupported needed-provider version')
        row = next(decoded_rows, None)
        require(row is not None and row[0] == name, 'decoded event order/count mismatch: ' + name)
        require(abs((raw['time'] - origin) / 10 - int(row[1])) <= .5,
                'decoded timestamp mismatch: ' + name)
        if cpu_column is not None:
            require(raw['cpu'] == int(row[cpu_column]), 'decoded CPU mismatch: ' + name)
        if name == 'CSwitch':
            switch_count += 1
            opaque_switches += raw['decodeError'] or raw['binary']
            current = dict(qpc=raw['time'], cpu=raw['cpu'], newTid=int(row[3]), oldTid=int(row[9]))
            previous = previous_switch.get(raw['cpu'])
            if previous is not None:
                require(previous['qpc'] <= current['qpc'] and previous['newTid'] == current['oldTid'],
                        'broken scheduler chain')
            if current['qpc'] >= first and raw['cpu'] not in window_after:
                if not window_switches[raw['cpu']] and previous is not None:
                    window_switches[raw['cpu']].append(previous)
                window_switches[raw['cpu']].append(current)
                if current['qpc'] > last:
                    window_after.add(raw['cpu'])
            previous_switch[raw['cpu']] = current
        elif name in ('T-Start', 'T-End'):
            if int(row[3]) == target:
                require(pid(row[2]) == target_pid, 'target TID reused/mapped to wrong PID')
                life.append((raw['op'], raw['time']))
        else:
            interrupt_count += 1
            require('InitialTime' in raw['data'], 'undecoded required interrupt start')
            start = int(raw['data']['InitialTime']) - offset
            end = raw['time']
            require(start <= end and abs((end-start)/10-int(row[2])) <= .5,
                    'interrupt clock/duration mismatch')
            if start <= last and end >= first:
                interrupts.append(dict(cpu=raw['cpu'], startQpc=start, endQpc=end))
    require(header is not None and event_count > 0, 'missing ETL header/events')
    require(next(decoded_rows, None) is None, 'extra needed decoded event')
    require(len(life) == 2 and [entry[0] for entry in life] == [1, 2],
            'target must have unambiguous Start+End in trace')
    switches = [row for sequence in window_switches.values() for row in sequence]
    trace = dict(schema=1, clockType='QPC', qpcFrequency=10000000, eventsLost=0, buffersLost=0,
                 decodeErrors=0, allExpectedProvidersDecoded=True,
                 interruptTimestampSemanticsValidated=True,
                 targetLife=dict(pid=target_pid, tid=target, startQpc=life[0][1], endQpc=life[1][1], mappingVerified=True),
                 cswitch=switches, interrupts=interrupts)
    audit = dict(decoderScope='only required scheduler/lifecycle/interrupt providers, independently xperf joined',
                 tracerptTotalProcessingErrors=processing_errors,
                 tracerptRequiredOpaqueSwitches=opaque_switches,
                 rawNormalEventCount=event_count, constantFiletimeMinusQpc=offset,
                 switchCount=switch_count, interruptCount=interrupt_count,
                 retainedWindowSwitchCount=len(switches), retainedWindowInterruptCount=len(interrupts),
                 timestampUnit='raw100nsQPC, no cycle-to-time conversion',
                 quantization='100ns clock; not a logging-bias bound')
    return trace, audit

def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('raw', 'normal', 'dump', 'benchmark', 'output'):
        p.add_argument('--' + name, required=True)
    a = p.parse_args()
    benchmark = json.load(open(a.benchmark))
    (trace, audit) = normalize(a.raw, a.normal, a.dump, benchmark)
    result = account(trace, benchmark)
    result['decoderAudit'] = audit
    cpu = benchmark['threadCycles']['startProcessor']
    tid = benchmark['stepSpans']['nativeThreadId']
    (_, running, excluded) = target_intervals(trace, cpu, tid)
    boundaries = sorted({t for intervals in (running, excluded) for pair in intervals for t in pair})
    for (row, start, end) in zip(result['rows'], benchmark['stepSpans']['rawStartQpc'], benchmark['stepSpans']['rawEndQpc']):
        count = bisect_right(boundaries, end) - bisect_left(boundaries, start)
        error = (count + 2) * 0.0001
        require(error < 0.015, 'sample numerical uncertainty >=15us')
        row.update(numericalErrorBoundMs=error, cpuLowerMs=max(0, row['cpuMs'] - error), cpuUpperMs=min(row['wallMs'], row['cpuMs'] + error))
    with open(a.output, 'w') as stream:
        json.dump(result, stream, indent=2)
if __name__ == '__main__':
    main()
