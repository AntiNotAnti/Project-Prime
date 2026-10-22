"""Synthetic, sanitized schema/negative controls; no Windows requirement."""
import copy, pathlib, tempfile, unittest
from unittest.mock import patch
import etw_step_cpu as adapter
from etw_cpu_intervals import account

def inputs():
 def event(t,guid,op,version,data=None,cpu=0):return dict(time=t,cpu=cpu,guid=guid,op=op,version=version,data=data or {},decodeError=False,binary=False)
 raw=[event(0,'header',0,2,dict(PerfFreq='10000000',ReservedFlags='0x1',EventsLost='0',BuffersLost='0',StartTime='1',EndTime='2')),
 event(10,adapter.THREAD,1,3),event(20,adapter.THREAD,36,5),event(30,adapter.PERF,68,2,{'InitialTime':'1025'}),
 event(50,adapter.PERF,69,2,{'InitialTime':'1040'}),event(70,adapter.PERF,67,2,{'InitialTime':'1060'}),
 event(80,adapter.THREAD,36,5),event(100,adapter.THREAD,2,3)]
 normal=copy.deepcopy(raw)
 for e in normal:e['time']+=1000
 rows=[['T-Start','1','Target (1)','2'],['CSwitch','2','Target (1)','2','','','','','Idle (0)','0','','','','','','','0'],
 ['DPC','3','0','0'],['DPCTmr','5','1','0'],['Interrupt','7','1','0'],
 ['CSwitch','8','Idle (0)','0','','','','','Target (1)','2','','','','','','','0'],['T-End','10','Target (1)','2']]
 b=dict(scenario={'steps':1},stepSpans=dict(schema=1,qpcFrequency=10000000,processId=1,nativeThreadId=2,nativeThreadIdAtReport=2,rawStartQpc=[20],rawEndQpc=[80]),threadCycles=dict(startProcessor=0,endProcessor=0,selectedAffinityMask='1'))
 return raw,normal,rows,b

class AdapterTests(unittest.TestCase):
 def run_case(self,change=None):
  raw,normal,rows,b=inputs()
  if change:change(raw,normal,rows,b)
  with patch.object(adapter,'events',side_effect=[raw,normal]),patch.object(adapter,'decoded',return_value=rows):return adapter.normalize('raw','normal','dump',b),b
 def test_exact_accounting_includes_timer(self):
  (trace,audit),b=self.run_case();result=account(trace,b)
  self.assertAlmostEqual(result['rows'][0]['cpuMs'],.0035);self.assertEqual(audit['interruptCount'],3)
 def test_visible_unrelated_decode_errors(self):
  (t,a),_=self.run_case(lambda r,n,x,b:r[0].update(decodeError=True))
  self.assertEqual(a['tracerptTotalProcessingErrors'],1);self.assertEqual(t['decodeErrors'],0)
 def check_rejected(self,change):
  with self.assertRaises((ValueError,KeyError)):self.run_case(change)
 def test_loss(self):self.check_rejected(lambda r,n,x,b:r[0]['data'].update(EventsLost='1'))
 def test_missing_loss(self):self.check_rejected(lambda r,n,x,b:r[0]['data'].pop('BuffersLost'))
 def test_nonqpc(self):self.check_rejected(lambda r,n,x,b:r[0]['data'].update(ReservedFlags='0x2'))
 def test_frequency(self):self.check_rejected(lambda r,n,x,b:b['stepSpans'].update(qpcFrequency=1000000))
 def test_unfinalized(self):self.check_rejected(lambda r,n,x,b:r[0]['data'].update(EndTime='0'))
 def test_nonconstant_offset(self):self.check_rejected(lambda r,n,x,b:n[2].update(time=9999))
 def test_unknown_switch_version(self):self.check_rejected(lambda r,n,x,b:(r[2].update(version=6),n[2].update(version=6)))
 def test_unknown_irq_version(self):self.check_rejected(lambda r,n,x,b:(r[3].update(version=3),n[3].update(version=3)))
 def test_missing_timer(self):self.check_rejected(lambda r,n,x,b:x.pop(3))
 def test_cpu_mismatch(self):self.check_rejected(lambda r,n,x,b:x[1].__setitem__(16,'1'))
 def test_clock_mismatch(self):self.check_rejected(lambda r,n,x,b:x[1].__setitem__(1,'99'))
 def test_tid_reuse(self):self.check_rejected(lambda r,n,x,b:x[-1].__setitem__(2,'Other (8)'))
 def test_broken_chain(self):self.check_rejected(lambda r,n,x,b:x[-2].__setitem__(9,'3'))
 def test_no_thread_end(self):self.check_rejected(lambda r,n,x,b:x.pop())
 def test_bad_interrupt_start(self):self.check_rejected(lambda r,n,x,b:r[4]['data'].update(InitialTime='9000'))

class NumericProbeTests(unittest.TestCase):
 def test_sanitized_actual_probe_and_independent_ranges(self):
  import json
  f=json.loads((pathlib.Path(__file__).parent/'probe-target-numeric.json').read_text());r=account(f['trace'],f['benchmark'])
  self.assertEqual([x['cpuMs'] for x in r['rows']],f['expectedCpuMs'])
  for row,reference in zip(r['rows'],f['expectedRangeCpuUs']):self.assertTrue(-1.000001<=reference-row['cpuMs']*1000<=3.000001)

if __name__=='__main__':unittest.main()

class FramingTests(unittest.TestCase):
    def blocks(self, value):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'input.xml'
            path.write_bytes(value)
            return list(adapter.event_blocks(path))

    def test_two_independent_events(self):
        self.assertEqual(len(self.blocks(b'<Events>\n<Event xmlns="x">\n</Event>\n<Event xmlns="x">\n</Event>\n</Events>\n')), 2)

    def test_truncated_event_rejected(self):
        with self.assertRaises(ValueError):
            self.blocks(b'<Events>\n<Event xmlns="x">\n')

    def test_unknown_root_rejected(self):
        with self.assertRaises(ValueError):
            self.blocks(b'<Unknown>\n</Unknown>\n')

    def test_extra_event_after_root_rejected(self):
        with self.assertRaises(ValueError):
            self.blocks(b'<Events>\n</Events>\n<Event xmlns="x">\n</Event>\n')

    def test_large_line_rejected(self):
        with self.assertRaises(ValueError):
            self.blocks(b'<Events>\n' + b'x' * (1024 * 1024 + 1))

    def test_duplicate_root_rejected(self):
        with self.assertRaises(ValueError):
            self.blocks(b'<Events>\n<Events>\n')

class ChunkAndGzipTests(unittest.TestCase):
    def test_chunk_resets_preserve_all_events(self):
        import xml.etree.ElementTree as ET
        text = b'<Events>\n' + b'<Event xmlns="x">\n<Data>1234</Data>\n</Event>\n' * 200 + b'</Events>\n'
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'input.xml'
            path.write_bytes(text)
            real = ET.fromstring
            with patch.object(adapter.ET, 'fromstring', wraps=real) as parser:
                result = list(adapter.event_blocks(path, chunk_limit=256))
            self.assertEqual(len(result), 200)
            self.assertGreater(parser.call_count, 2)
            self.assertTrue(all(len(call.args[0]) <= 280 for call in parser.call_args_list))

    def test_gzip_matches_plain(self):
        import gzip
        import xml.etree.ElementTree as ET
        text = b'<Events>\n<Event xmlns="x">\n</Event>\n</Events>\n'
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'input.xml.gz'
            with gzip.open(path, 'wb') as stream:
                stream.write(text)
            self.assertEqual(len(list(adapter.event_blocks(path))), 1)

    def test_bad_gzip_crc_fails(self):
        import gzip
        text = bytearray(gzip.compress(b'<Events>\n</Events>\n'))
        text[-5] ^= 255
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'input.xml.gz'
            path.write_bytes(text)
            with self.assertRaises((gzip.BadGzipFile, EOFError)):
                list(adapter.event_blocks(path))

class DeclarationTests(unittest.TestCase):
    def check(self, text, valid=False):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'input.xml'
            path.write_bytes(text)
            if valid:
                self.assertEqual(list(adapter.event_blocks(path)), [])
            else:
                with self.assertRaises(ValueError):
                    list(adapter.event_blocks(path))

    def test_start_declaration_allowed_once(self):
        self.check(b'<?xml version="1.0"?>\n<Events>\n</Events>\n', valid=True)

    def test_after_root_open_rejected(self):
        self.check(b'<Events>\n<?xml version="1.0"?>\n</Events>\n')

    def test_after_root_close_rejected(self):
        self.check(b'<Events>\n</Events>\n<?xml version="1.0"?>\n')

    def test_duplicate_declaration_rejected(self):
        self.check(b'<?xml version="1.0"?>\n<?xml version="1.0"?>\n<Events>\n</Events>\n')
