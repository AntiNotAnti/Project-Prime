using System.Diagnostics;
using MphRead.Mods.Network;

int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);++checks;}
void Until(Func<bool> done,string label,Action? tick=null)
{
 var clock=Stopwatch.StartNew();while(!done()&&clock.ElapsedMilliseconds<1800){tick?.Invoke();Thread.Sleep(1);}
 Check(done(),label);
}
void Leader(DedicatedServer server,ClosedBackend backend)
{
 server.StartLeader();Until(()=>backend.Count("activate")==1,"actual validation schedules activation",()=>server.Tick(.001));
}
void Activated(DedicatedServer server,ClosedBackend backend)
{Leader(server,backend);backend.ReleaseActivation();Until(()=>server.Snapshot().Activated,"held activation response applies on control owner",()=>server.Tick(.001));}
void Attached(DedicatedServer server,ClosedBackend backend)
{Activated(server,backend);server.Attach();Until(()=>server.Publications().Any(s=>s.State==PartyReservationWireState.Reserved&&s.SelfSlot<8),"verified member attaches to exact group",()=>server.Tick(.001));}

using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Leader(server,backend);var before=server.Snapshot();
 Check(before.Exists&&!before.Activated&&before.Holds!=0,"all slots tentatively held during activation");
 Check(!server.Publications().Any(s=>s.State==PartyReservationWireState.Reserved),"no Reserved before backend activation acknowledgement");
 backend.ReleaseActivation();Until(()=>server.Snapshot().Activated,"activation acknowledgement releases Reserved",()=>server.Tick(.001));
 Check(server.Publications().Any(s=>s.State==PartyReservationWireState.Reserved),"Reserved requires still-current local hold and backend ack");
 Console.WriteLine("PASS held activation gates Reserved");
}
using(var backend=new ClosedBackend{FailCancel=1})using(var server=new DedicatedServer(backend))
{
 Leader(server,backend);string reservation=server.Snapshot().Reservation;
 Check(backend.CommittedReservation==reservation,"closed backend commits real reservation before withheld activation reply");
 Check(server.FillWorkers()==7,"activation plus seven held workers saturate budget8");
 server.Action(PartyReservationWireAction.CancelGroup,true);server.Tick();
 Check(server.Snapshot() is {Exists:true,Cancelled:true,Holds:0},"cancel releases local holds but retains saturated cleanup owner");
 Check(backend.Count("cancel")==0,"cancel cannot dispatch while all8 workers busy");
 server.ReleaseWorkers();Until(()=>backend.Count("cancel")>=1,"cleanup retries when worker capacity becomes available",()=>server.Tick(.001));
 Check(backend.Last("cancel").GetProperty("reservation_id").GetString()==reservation,"cancel uses real reservation ID before activation reply");
 Until(()=>server.Snapshot().Running<=1,"first refused cancel result returns to owner",()=>server.Tick(.001));
 server.Tick(.6);Until(()=>backend.Count("cancel")>=2,"failed cleanup result is retried",()=>server.Tick(.001));
 Until(()=>!server.Snapshot().Exists,"acknowledged cancellation retires cleanup owner",()=>server.Tick(.001));
 Check(backend.BackendCancelled&&backend.RejectedReservationIds==0,"committed remote reservation receives matching cancellation tombstone");
 backend.ReleaseActivation();Thread.Sleep(10);server.Tick();
 Check(!server.Snapshot().Exists&&server.Snapshot().Holds==0&&!server.Publications().Any(s=>s.State==PartyReservationWireState.Reserved),"delayed committed activation reply cannot reinflate cancelled group");
 Console.WriteLine("PASS cancellation real-ID / saturated dispatch / refused retry / delayed activation");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Attached(server,backend);Check(server.FillWorkers()==8,"saturate all8 before explicit Accept");
 server.Action(PartyReservationWireAction.Accept);var accepted=server.Snapshot();
 Check(accepted.ClaimRequested&&accepted.ClaimNonce!=0&&backend.Count("claim_admission")==0,"Accept latches consent and nonce even if dispatch refuses");
 server.Action(PartyReservationWireAction.Accept);server.Tick(.03);
 Check(server.Snapshot().ClaimNonce==accepted.ClaimNonce&&server.Snapshot().ClaimRequested,"duplicate Accept retains original short claim nonce");
 server.ReleaseWorkers();Until(()=>backend.Count("claim_admission")==1,"pending claim retries within fixed2sec budget",()=>server.Tick(.001));
 Check(backend.Last("claim_admission").GetProperty("claim_nonce").GetString()==accepted.ClaimNonce.ToString(),"retried claim binds original nonce");
 backend.ReleaseClaim();Until(()=>server.Snapshot().Admissions==1,"successful exact claim performs one ordinary admission boundary",()=>server.Tick(.001));
 Check(server.Snapshot().PeerCount==2&&server.Snapshot().Holds==0,"actual peer creation consumes only its reserved slot");
 Console.WriteLine("PASS saturated Accept latches and retries same bounded claim");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Attached(server,backend);server.FillWorkers();server.Action(PartyReservationWireAction.Accept);ulong nonce=server.Snapshot().ClaimNonce;
 server.Tick(2.01);
 Check(server.Publications().Any(s=>s.State==PartyReservationWireState.Expired&&s.SelfSlot==255),"unsent saturated claim explicitly expires at original2sec deadline");
 Check(server.Snapshot().Admissions==0&&server.Snapshot().Holds==0&&backend.Count("claim_admission")==0,"expired consent releases slot without peer creation");
 server.ReleaseWorkers();server.Tick();Check(server.Snapshot().Admissions==0,"late worker capacity cannot resurrect expired nonce "+nonce);
 Console.WriteLine("PASS saturated claim absolute expiry");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Attached(server,backend);server.Action(PartyReservationWireAction.Accept);
 Until(()=>backend.Count("claim_admission")==1,"claim reaches closed backend",()=>server.Tick(.001));
 server.Action(PartyReservationWireAction.DeclineSelf);server.Tick();
 Check(server.Snapshot() is {ClaimRequested:true,Declined:true,QueuePresent:true,Holds:>0},"self-decline retains exact in-flight claim peer and short slot hold");
 Check(server.Publications().Where(s=>s.State==PartyReservationWireState.Admitting).All(s=>s.ReservedCount>0),"Admitting publication remains valid when member declined after claim");
 backend.ReleaseClaim();Until(()=>server.Snapshot().Admissions==1,"already-committed exact claim may settle after self-decline",()=>server.Tick(.001));
 Check(server.Snapshot().PeerCount==2,"settlement preserves actual admitted peer");
 Console.WriteLine("PASS self-decline after claim retains bounded settlement");
}
using(var backend=new ClosedBackend{FailAck=1})using(var server=new DedicatedServer(backend))
{
 Attached(server,backend);server.Action(PartyReservationWireAction.Accept);Until(()=>backend.Count("claim_admission")==1,"ack case claim starts",()=>server.Tick(.001));
 backend.ReleaseClaim();Until(()=>server.ClaimCompleted,"claim result held for owner application");
 Check(server.FillWorkers()==8,"saturate admission acknowledgement dispatch after successful claim response");
 server.Tick();Check(server.Snapshot() is {Admitted:true,Acknowledged:false,Admissions:1,PeerCount:2,Exists:true},"actual peer retained while initial acknowledgement cannot start");
 Check(backend.Count("admitted")==0,"busy acknowledgement is not silently invented");
 server.ReleaseWorkers();Until(()=>backend.Count("admitted")==1,"actual-peer acknowledgement retries after saturation",()=>server.Tick(.001));
 Until(()=>server.Snapshot().Running==0,"failed first ack result reaches owner",()=>server.Tick(.001));server.Tick(.3);
 Until(()=>backend.Count("admitted")>=2,"lost/refused acknowledgement retries without admission callback",()=>server.Tick(.001));
 Until(()=>!server.Snapshot().Exists,"all-admitted group finalizes after acknowledged peer",()=>server.Tick(.001));
 Check(server.Snapshot().Admissions==1&&server.Snapshot().PeerCount==2&&backend.Count("cancel")==0,"ack retry creates no second peer/session and does not cancel admitted group");
 Check(backend.Last("admitted").GetProperty("claim_nonce").GetString()==backend.Last("claim_admission").GetProperty("claim_nonce").GetString()
  &&backend.Last("admitted").GetProperty("peer_created").GetBoolean(),"actual-peer ack retains exact claim identity and peer-created evidence");
 Console.WriteLine("PASS actual-peer acknowledgement saturation and retry");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Leader(server,backend);server.Rollover();backend.ReleaseActivation();Thread.Sleep(10);server.Tick();
 Check(server.Snapshot().Holds==0&&server.Snapshot().Admissions==0&&!server.Publications().Any(s=>s.State==PartyReservationWireState.Reserved),"rollover rejects old activation completion without restoring capacity holds");
 Console.WriteLine("PASS stale epoch activation completion");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Attached(server,backend);server.Action(PartyReservationWireAction.Accept);Until(()=>backend.Count("claim_admission")==1,"replacement case starts exact claim",()=>server.Tick(.001));
 server.ReplaceMemberConnection();server.Tick();Check(server.Snapshot().Holds==0,"retired connection releases old member capacity");
 server.OccupyReleasedSlot();backend.ReleaseClaim();Until(()=>!server.Snapshot().ClaimRequested,"late claim completion is retired",()=>server.Tick(.001));
 Check(server.Snapshot().Admissions==0&&server.Snapshot().PeerCount==2&&backend.Count("admitted")==0,"old claim cannot admit replacement connection or remove replacement occupant");
 Console.WriteLine("PASS replacement owner and reused-slot stale result fence");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 server.AddBot(1);Leader(server,backend);
 Check(backend.Last("activate").GetProperty("assignments")[0].GetProperty("slot").GetInt32()==2,"actual RefreshWaitlist excludes occupied bot slot from exact assignment");
 Check(server.Snapshot().Holds==4,"group tentative mask excludes both admitted human and bot");
 Console.WriteLine("PASS actual authoritative human and bot occupancy");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 for(int slot=1;slot<8;slot++)server.AddBot(slot);
 server.StartLeader();Until(()=>server.Snapshot().Exists,"full lobby creates one group FIFO entry",()=>server.Tick(.001));
 Check(server.Snapshot().Holds==0&&backend.Count("activate")==0,"full humans/bots lobby holds no partial group capacity");
 server.Attach();Until(()=>server.Publications().Any(s=>s.State==PartyReservationWireState.Waiting&&s.MemberLeaseNonce!=0),"member receives verified Waiting attachment",()=>server.Tick(.001));
 server.Action(PartyReservationWireAction.DeclineSelf);server.Tick();
 Check(!server.Snapshot().Declined&&backend.Count("cancel")==0,"Pending self-decline preserves frozen leader-owned cohort");
 server.Tick(5.01);Check(server.Snapshot().QueuePresent,"actually verified Waiting attachment survives unproven5sec cap without fake QueueId");
 Console.WriteLine("PASS Pending immutable cohort / verified Waiting bootstrap lifetime");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 server.SeedVerifiedQueuePeers(17);Check(server.BootstrapProbe(),"verified group peers do not consume16 unproven bootstrap allowance");
 // This is a classification fixture for already-verified proof publication;
 // HMAC/SQL membership authorization is covered by the separate real SQL/Edge suites.
 Console.WriteLine("PASS verified attachment bootstrap classification");
}
using(var backend=new ClosedBackend())using(var server=new DedicatedServer(backend))
{
 Check(server.BootstrapProbe(),"new unproven queue socket starts");server.Tick(5.01);
 Check(!server.ProbePresent,"unproven socket retains absolute5sec lifetime");
 Console.WriteLine("PASS absolute unproven5sec cap");
}
Console.WriteLine($"PASS {checks} actual server-control-owner assertions; actual FIFO/HTTP adapters/generated codecs; CLOSED transport and ordinary HandleHello facade; no UDP/backend credentials/live HTTP");
