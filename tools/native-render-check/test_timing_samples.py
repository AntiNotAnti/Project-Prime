"""Asset-free checks of the production long-session sample policy."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class TimingSamplesTests(unittest.TestCase):
    def test_actual_bounded_storage(self):
        source = (Path(__file__).parent / "Program.cs").read_text()
        helper = source[source.index("internal sealed class NativeTimingIntervals"):]
        dotnet = os.environ.get("DOTNET_EXE") or shutil.which("dotnet")
        self.assertIsNotNone(dotnet, "Set DOTNET_EXE to .NET 10 dotnet")
        checks = '''
using System;
using System.Collections.Generic;
static void Check(bool value, string name) { if (!value) throw new Exception(name); }
Check(NativeTimingIntervals.ValidSeconds(8,false), "minimum duration");
Check(NativeTimingIntervals.ValidSeconds(604,false), "ten-minute plus warmup");
Check(NativeTimingIntervals.ValidSeconds(1804,false), "thirty-minute plus warmup");
foreach (int seconds in new[]{-1,0,7,1805,int.MaxValue}) {
 Check(!NativeTimingIntervals.ValidSeconds(seconds,false), "reject duration");
 try { NativeTimingIntervals.CapacityFor(seconds); throw new Exception("invalid capacity admitted"); }
 catch (ArgumentOutOfRangeException) { }
}
Check(NativeTimingIntervals.ValidSeconds(120,true) && !NativeTimingIntervals.ValidSeconds(121,true), "capture stays short");
Check(NativeTimingIntervals.CapacityFor(8)==4*4096, "warmup excluded");
Check(NativeTimingIntervals.CapacityFor(604)==600*4096, "ten-minute bound");
Check(NativeTimingIntervals.CapacityFor(1804)==1800*4096, "thirty-minute bound");
var buffer = new NativeTimingIntervals(4096);
buffer.TryRecord(.25); // JIT before allocation observation.
long start=GC.GetAllocatedBytesForCurrentThread();
for(int i=1;i<4096;i++) if(!buffer.TryRecord(i)) throw new Exception("early overflow");
long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
Check(allocated==0,"recording never allocates/grows");
Check(buffer.Samples.Count==4096 && buffer.Capacity==4096 && buffer.DroppedRecords==0,"exact capacity accepted");
Check(!buffer.TryRecord(9999) && !buffer.TryRecord(9999),"overflow rejected");
Check(buffer.DroppedRecords==2 && buffer.Samples.Count==4096 && buffer.Capacity==4096,"drops counted without growth");
Check(buffer.Samples[0]==.25 && buffer.Samples[4095]==4095,"chronological samples preserved");
Console.WriteLine("TIMING_SAMPLES_PASS");
'''
        with tempfile.TemporaryDirectory(prefix="prime-timing-samples-") as temp:
            scratch = Path(temp)
            (scratch / "Program.cs").write_text(checks + helper)
            (scratch / "check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>'
                '<NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
            result = subprocess.run([dotnet, "run", "--project", str(scratch / "check.csproj"),
                                     "-c", "Release", "--verbosity", "quiet"],
                                    capture_output=True, text=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("TIMING_SAMPLES_PASS", result.stdout)

    def test_snapshot_precedes_end_receipt_and_result_work(self):
        source = (Path(__file__).parent / "Program.cs").read_text()
        record = source.index("bool recorded = _intervalBuffer.TryRecord")
        snapshot = source.index("_finalAllocated = GC.GetTotalAllocatedBytes(false)", record)
        receipt = source.index("ObserveWorkloadEnd();", snapshot)
        self.assertLess(record, snapshot)
        self.assertLess(snapshot, receipt)
        result = source[source.index("internal object Result()") : source.index("// Pure bounded storage")]
        self.assertIn("allocatedBytes = _measureEnded == 0 ? 0 : _finalAllocated - _allocated", result)
        self.assertIn("_finalCollections[i] - _collections[i]", result)
        self.assertNotIn("GC.GetTotalAllocatedBytes", result)
        self.assertNotIn("GC.CollectionCount", result)
        self.assertIn("droppedIntervalRecords = _intervalBuffer.DroppedRecords", result)
        self.assertIn("timingEvidenceComplete = Complete &&", result)


if __name__ == "__main__":
    unittest.main()
