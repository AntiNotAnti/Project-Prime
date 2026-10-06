using MphRead.Mods.Render;
int checks = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; }
var ledger = new FrameTraceLedger(1000, 3);
ledger.Begin(100); ledger.Input(101); ledger.AddWait(1);
ledger.SimulationStart(102, 100); ledger.AddNetwork(2); ledger.SimulationEnd(107, 110);
ledger.RenderStart(108, 110); ledger.PreparationStart(108, 110); ledger.PreparationEnd(110, 130);
ledger.RenderEnd(114, 160); ledger.PresentStart(114); ledger.AddAcquire(1.5); ledger.Complete(119);
var a=ledger.Samples.Single();
Check(a.CpuFrameSpanMs==19 && a.PresentReturnIntervalMs==null, "first frame includes all work and blocking present; interval is unavailable");
Check(a.DriverWaitMs==1 && a.InputSampleOffsetMs==1 && a.SimulationMs==5 && a.NetworkMs==2, "input/simulation/network/wait phases are distinct");
Check(a.PreparationMs==2 && a.RenderMs==4 && a.DriverPresentCallMs==5 && a.SurfaceAcquireMs==1.5, "preparation/render/present/acquire phases are distinct");
Check(a.SimulationAllocatedBytes==10 && a.PreparationAllocatedBytes==20 && a.RenderAllocatedBytes==30, "allocation attribution follows phase ownership");
ledger.Begin(125); ledger.RenderStart(125,200); ledger.RenderEnd(126,205); ledger.PresentStart(126); ledger.Complete(129);
var b=ledger.Samples.Last();
Check(b.PresentReturnIntervalMs==10 && b.IdleBeforeFrameMs==6 && b.CpuFrameSpanMs==4, "presentation intervals include pacing idle between CPU frames");
Check(b.SimulationMs==0 && b.PreparationMs==0 && b.RenderMs==1 && b.RenderAllocatedBytes==5, "high-refresh frame without simulation remains observable");
ledger.Begin(130); ledger.Cancel();
ledger.Begin(135); ledger.PresentStart(136); ledger.Complete(140);
Check(ledger.CancelledFrames==1 && ledger.Samples.Last().PresentReturnIntervalMs==11, "cancellation does not fabricate a presented frame");
for(int i=0;i<100000;i++){ledger.Begin(150+i);ledger.PresentStart(151+i);ledger.Complete(152+i);}
Check(ledger.Full && ledger.Samples.Count==3, "capture is bounded on a long production run");
var invalid = new FrameTraceLedger(1000);
invalid.Begin(100);invalid.Complete(90);
Check(invalid.Samples.Count==0 && invalid.CancelledFrames==1,"invalid/incomplete frames are excluded explicitly");
Console.WriteLine($"Frame trace: {checks} checks passed.");
