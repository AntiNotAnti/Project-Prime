"""Interval accounting for the strict tracerpt/xperf adapter in etw_step_cpu.py.
All intervals use verified raw QPC ticks. No cycle-to-time conversion occurs.
"""
from bisect import bisect_left, bisect_right
import json
import math
import sys


def require(value, message):
    if not value:
        raise ValueError(message)


def union(intervals):
    result = []
    for start, end in sorted(intervals):
        require(type(start) is int and type(end) is int and start <= end, 'invalid interval')
        if start == end:
            continue
        if result and start <= result[-1][1]:
            result[-1] = (result[-1][0], max(end, result[-1][1]))
        else:
            result.append((start, end))
    return result


def intersect_unions(left, right):
    """Linear intersection of sorted, disjoint interval unions."""
    result = []
    i = j = 0
    while i < len(left) and j < len(right):
        start = max(left[i][0], right[j][0])
        end = min(left[i][1], right[j][1])
        if start < end:
            result.append((start, end))
        if left[i][1] <= right[j][1]:
            i += 1
        else:
            j += 1
    return result


class Coverage:
    """Prefix-integrated interval union: O(log n) range coverage."""
    def __init__(self, intervals):
        self.intervals = intervals
        self.starts = [a for a, _ in intervals]
        self.ends = [b for _, b in intervals]
        self.prefix = [0]
        for start, end in intervals:
            self.prefix.append(self.prefix[-1] + end - start)

    def before(self, tick):
        index = bisect_right(self.ends, tick)
        total = self.prefix[index]
        if index < len(self.intervals):
            total += max(0, tick - self.starts[index])
        return total

    def between(self, start, end):
        return self.before(end) - self.before(start)


def intersect_length(intervals, start, end):
    """Compatibility utility for tiny fixtures; production accounting uses Coverage."""
    return sum(max(0, min(end, b) - max(start, a)) for a, b in intervals)


def target_intervals(trace, cpu, tid):
    switches = sorted((r for r in trace['cswitch'] if r['cpu'] == cpu), key=lambda r: r['qpc'])
    require(len(switches) >= 2, 'missing scheduler boundaries')
    running = []
    for previous, current in zip(switches, switches[1:]):
        require(previous['newTid'] == current['oldTid'], 'broken scheduler chain')
        if previous['newTid'] == tid:
            running.append((previous['qpc'], current['qpc']))
    running = union(running)
    interrupts = union((r['startQpc'], r['endQpc']) for r in trace['interrupts'] if r['cpu'] == cpu)
    excluded = intersect_unions(running, interrupts)
    return switches, running, excluded


def account(trace, benchmark):
    require(trace['schema'] == 1 and trace['clockType'] == 'QPC', 'unverified/non-QPC clock')
    for field in ('eventsLost', 'buffersLost', 'decodeErrors'):
        require(type(trace[field]) is int and trace[field] == 0, 'loss/decode failure: ' + field)
    require(trace['allExpectedProvidersDecoded'] is True and trace['interruptTimestampSemanticsValidated'] is True,
            'required decoder/interrupt timestamp validation missing')
    span = benchmark['stepSpans']
    require(span['schema'] == 1 and span['qpcFrequency'] == trace['qpcFrequency'] > 0, 'clock frequency mismatch')
    require(span['nativeThreadId'] == span['nativeThreadIdAtReport'], 'native thread changed')
    life = trace['targetLife']
    require((span['processId'], span['nativeThreadId']) == (life['pid'], life['tid']), 'PID/TID lifetime mismatch')
    require(life['mappingVerified'] is True, 'thread/process mapping unresolved')
    pin = benchmark['threadCycles']
    require(pin is not None and pin['startProcessor'] == pin['endProcessor'], 'pinned CPU required')
    cpu = pin['startProcessor']
    require(int(pin['selectedAffinityMask'], 16) == 1 << cpu, 'affinity is not exactly one matching CPU')
    switches, running, excluded = target_intervals(trace, cpu, life['tid'])
    starts, ends = span['rawStartQpc'], span['rawEndQpc']
    require(len(starts) == len(ends) == benchmark['scenario']['steps'] and len(starts) > 0, 'missing sample spans')
    for row in trace['cswitch']:
        if starts[0] <= row['qpc'] <= ends[-1] and life['tid'] in (row['newTid'], row['oldTid']):
            require(row['cpu'] == cpu, 'target migrated despite pin')
    scheduled_coverage = Coverage(running)
    interrupt_coverage = Coverage(excluded)
    rows = []
    for index, (start, end) in enumerate(zip(starts, ends)):
        require(type(start) is int and type(end) is int and start < end, 'invalid QPC span')
        require(index == 0 or ends[index - 1] <= start, 'overlapping/out-of-order spans')
        require(switches[0]['qpc'] <= start and end <= switches[-1]['qpc'], 'unknown scheduler edges')
        require(life['startQpc'] <= start and end <= life['endQpc'], 'sample outside lifecycle')
        scheduled = scheduled_coverage.between(start, end)
        interruption = interrupt_coverage.between(start, end)
        cpu_ticks = scheduled - interruption
        require(0 <= cpu_ticks <= scheduled <= end - start, 'invalid/double-subtracted accounting')
        scale = 1000 / span['qpcFrequency']
        rows.append(dict(step=index, wallMs=(end-start)*scale, scheduledMs=scheduled*scale,
                         excludedDpcIsrMs=interruption*scale, cpuMs=cpu_ticks*scale))
    ordered = sorted(row['cpuMs'] for row in rows)
    def percentile(q):
        return ordered[max(0, math.ceil(q*len(ordered))-1)]
    return dict(scope='ETW scheduled-thread running time minus ISR/DPC union',
                unit='milliseconds from raw QPC, never thread-cycle conversion', samples=len(rows),
                p50CpuMs=percentile(.5), p95CpuMs=percentile(.95), p99CpuMs=percentile(.99), rows=rows)
