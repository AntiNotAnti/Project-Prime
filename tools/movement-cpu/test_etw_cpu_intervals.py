import copy
import unittest
from etw_cpu_intervals import account

class IntervalTests(unittest.TestCase):
    def setUp(self):
        self.trace = {'schema':1,'clockType':'QPC','qpcFrequency':1000,'eventsLost':0,'buffersLost':0,
            'decodeErrors':0,'allExpectedProvidersDecoded':True,'interruptTimestampSemanticsValidated':True,
            'targetLife':{'pid':10,'tid':20,'startQpc':0,'endQpc':110,'mappingVerified':True},
            'cswitch':[{'cpu':0,'qpc':0,'oldTid':0,'newTid':20},
                       {'cpu':0,'qpc':40,'oldTid':20,'newTid':30},
                       {'cpu':0,'qpc':60,'oldTid':30,'newTid':20},
                       {'cpu':0,'qpc':110,'oldTid':20,'newTid':0}],
            'interrupts':[{'cpu':0,'startQpc':20,'endQpc':30},
                          {'cpu':0,'startQpc':25,'endQpc':35}]}
        self.benchmark = {'scenario':{'steps':2},'threadCycles':{'selectedAffinityMask':'1','startProcessor':0,'endProcessor':0},
            'stepSpans':{'schema':1,'qpcFrequency':1000,'processId':10,'nativeThreadId':20,'nativeThreadIdAtReport':20,
                         'rawStartQpc':[10,70],'rawEndQpc':[65,100]}}
    def test_preemption_and_nested_interrupt_union(self):
        rows=account(self.trace,self.benchmark)['rows']
        self.assertEqual(rows[0]['cpuMs'],20) # 55 wall -20 preempted -15 interrupt union
        self.assertEqual(rows[1]['cpuMs'],30)
    def test_prepin_migration_allowed(self):
        self.trace['cswitch'][0]['qpc'] = 6
        self.trace['cswitch'] += [
            {'cpu':1,'qpc':0,'oldTid':0,'newTid':20},
            {'cpu':1,'qpc':5,'oldTid':20,'newTid':0}]
        self.assertEqual(account(self.trace,self.benchmark)['samples'],2)
    def test_migration_inside_measured_pin_rejected(self):
        self.trace['cswitch'] += [
            {'cpu':0,'qpc':20,'oldTid':20,'newTid':30},
            {'cpu':0,'qpc':30,'oldTid':30,'newTid':20},
            {'cpu':1,'qpc':21,'oldTid':0,'newTid':20},
            {'cpu':1,'qpc':29,'oldTid':20,'newTid':0}]
        with self.assertRaises(ValueError): account(self.trace,self.benchmark)
    def test_loss_clock_edges_decode_mapping_fail_closed(self):
        for key,value in [('eventsLost',1),('buffersLost',1),('decodeErrors',1),('clockType','cycles'),('allExpectedProvidersDecoded',False)]:
            t=copy.deepcopy(self.trace);t[key]=value
            with self.assertRaises(ValueError): account(t,self.benchmark)
        b=copy.deepcopy(self.benchmark);b['stepSpans']['rawStartQpc'][0]=-1
        with self.assertRaises(ValueError): account(self.trace,b)
    def test_chain_and_missing_samples_fail(self):
        t=copy.deepcopy(self.trace);t['cswitch'][1]['oldTid']=99
        with self.assertRaises(ValueError): account(t,self.benchmark)
        b=copy.deepcopy(self.benchmark);b['stepSpans']['rawEndQpc'].pop()
        with self.assertRaises(ValueError): account(self.trace,b)

if __name__=='__main__': unittest.main()

class SweepTests(unittest.TestCase):
    def test_prefix_coverage_matches_direct_intersection(self):
        from etw_cpu_intervals import Coverage, union, intersect_length
        intervals = union([(a, a + (a % 13) + 1) for a in range(0, 500, 7)])
        coverage = Coverage(intervals)
        for start in range(-5, 510, 3):
            for width in (0, 1, 5, 100):
                self.assertEqual(coverage.between(start, start + width), intersect_length(intervals, start, start + width))

    def test_linear_intersection_matches_product_reference(self):
        from etw_cpu_intervals import union, intersect_unions
        left = union([(x, x + 3) for x in range(0, 100, 7)])
        right = union([(x, x + 9) for x in range(0, 100, 11)])
        reference = union((max(a, c), min(b, d)) for a, b in left for c, d in right if max(a, c) < min(b, d))
        self.assertEqual(intersect_unions(left, right), reference)
