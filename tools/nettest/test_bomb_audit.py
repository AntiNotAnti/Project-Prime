import copy
import hashlib
import json
import struct
import tempfile
import zlib
import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location("bomb_audit",Path(__file__).with_name("bomb-audit.py"))
bomb=importlib.util.module_from_spec(spec);spec.loader.exec_module(bomb)

def key(shot=1):
    return dict(AuthorityEpoch=1,MatchId=2,ShooterSlot=0,Generation=1,LifeId=1,ShotId=shot)
def state(shot=1,kind="MorphBall",index=0,links=(0,0),x=0,rotated=False):
    return dict(Key=key(shot),Kind=kind,PlacedFrame=10,Position=dict(X=x,Y=0,Z=0),
        Facing=dict(X=int(rotated),Y=0,Z=int(not rotated)),Up=dict(X=0,Y=1,Z=0),
        Speed=dict(X=0,Y=0,Z=0),Countdown=80,Index=index,Link0=links[0],Link1=links[1],Recolor=0)
def event(sequence,kind,s,frame):
    return dict(Type="Event",Stage="authority",Key=copy.deepcopy(s["Key"]),Sequence=sequence,
        Frame=frame,Kind=kind,Reason=0,State=copy.deepcopy(s))
def snapshot(frame,frontier,states):
    return dict(Type="Snapshot",Stage="authority",Frame=frame,Frontier=frontier,States=copy.deepcopy(states))
def request(shot=1,action="Place"):
    return dict(Type="Request",Stage="received",Key=key(shot),Frame=9,RequestFrame=9,Action=action)
def damage(shot=1,kind="MorphBall",frame=12,seq=1):
    return dict(Type="Damage",Stage="authority-damage",Key=key(shot),Frame=frame,DamageEvent=seq,
        Kind=kind,VictimSlot=1,VictimGeneration=1,VictimLife=1,HealthDamage=10,TurretDamage=0,
        HealthAfter=89,TurretHealthAfter=0,Position=dict(X=0,Y=0,Z=0))
def fixture(kind="MorphBall"):
    s=state(kind=kind)
    rows=[snapshot(0,0,[]),request(),event(1,"Placed",s,10),snapshot(11,1,[s]),
          damage(kind=kind),event(2,"Detonated",s,56),snapshot(57,2,[])]
    peer_rows=[]
    for r in rows:
        if r["Type"] == "Damage":continue
        r=copy.deepcopy(r);r["Stage"]="requested" if r["Type"] == "Request" else "applied";peer_rows.append(r)
    release=dict(Status="PASS",Epoch=1,Match=2,Semantic=0,Award=0,Bomb=2)
    at=dict(Epoch=1,Match=2,SemanticLatest=0,SemanticPresented=0,AwardLatest=0,AwardPresented=0,BombFrontier=2)
    def drain(role):
        return dict(Enabled=True,Role=role,Quiescing=True,Epoch=1,Match=2,SemanticLatest=0,
            SemanticPresented=0,AwardLatest=0,AwardPresented=0,ActiveProjectiles=0,ActiveBombs=0,
            ClaimsPending=0,ClaimGraceSeconds=2.4,OfferedShotKeys=[],AcceptedShotKeys=[],
            Reliable=[dict(Failed=False,Pending=0)],ConnectionLost=False,Error=None,
            Release=copy.deepcopy(release),AtRelease=copy.deepcopy(at),BombFrontier=2,
            BombPendingRequests=0,BombPendingEvents=0,BombFaulted=False)
    health=[dict(Epoch=1,Match=2,Frame=f,Slot=1,Generation=1,Life=1,Hp=89,TurretHp=0,
        Kills=0,Deaths=0,DamageEvent=1) for f in (60,120)]
    def report(role,rs):
        return dict(Schema=1,Protocol=48,Overflow=0,ConnectionLost=False,SemanticOverrun=0,
            SemanticDisconnects=0,LastError=None,Frames=120,Windows=[dict(LocalSlot=0)],States=copy.deepcopy(health),
            Transport=dict(QueueDrops=0,SocketErrors=0),ServerSegments=[dict(StepFailures=0)],
            Drain=drain(role),Facts=[],Bombs=dict(Rows=rs,Overflow=0))
    server=report("server",rows);peer=report("client",peer_rows)
    peer["Facts"]=[dict(Baseline=True,Epoch=1,Match=2,Id=0,AwardFrontier=0)]
    processes=[dict(name=n,exit=0,timedOut=False) for n in ("server","peer-0")]
    manifest=dict(settings=dict(players=1,protocol=48,drain_seconds=5),assemblySha256="a"*64,
        assemblyAfterSha256="a"*64,drain=dict(status="PASS",release=release,server=copy.deepcopy(server["Drain"]),
        peers={"peer-0":copy.deepcopy(peer["Drain"])},stableSeconds=3))
    return server,{"peer-0":peer},manifest,dict(processes=processes)

def gpu_fixture(directory):
    def chunk(tag,data):
        return struct.pack(">I",len(data))+tag+data+struct.pack(">I",zlib.crc32(tag+data)&0xffffffff)
    def capture(label,rgb):
        raw = b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",1,1,8,2,0,0,0))+chunk(b"IDAT",zlib.compress(b"\0"+rgb))+chunk(b"IEND",b"")
        path=Path(directory)/(label+".png");path.write_bytes(raw)
        return dict(path=str(path),width=1,height=1,pngSha256=hashlib.sha256(raw).hexdigest(),rgbSha256=hashlib.sha256(rgb).hexdigest())
    bodies=[("9/2/0/1/1/1","MorphBall",0,0,0),("9/2/1/1/1/1","Stinglarva",0,0,0),
        ("9/2/3/1/1/1","Lockjaw",0,0,0),("9/2/3/1/1/2","Lockjaw",1,1,0),("9/2/3/1/1/3","Lockjaw",2,1,2)]
    fixtures=[dict(key=k,kind=kind,position=[i,0,0],Index=index,Link0=l0,Link1=l1) for i,(k,kind,index,l0,l1) in enumerate(bodies)]
    pairs=[(b[0],None) for b in bodies]+[(bodies[3][0],bodies[2][0]),(bodies[4][0],bodies[2][0]),(bodies[4][0],bodies[3][0])]
    cases=[]
    for i,(source,target) in enumerate(pairs):
        for occluded in (False,True):
            rgb=b"\0\0\0";enabled=rgb if occluded else b"\3\0\0";label=f"case-{i}-{occluded}"
            cases.append(dict(name=label,key=source,targetKey=target,occluded=occluded,status="PASS",geometryVerified=True,
                worldDepth=.1 if occluded else .9,fixtureDepth=.5,changedPixels=0 if occluded else 1,unstablePixels=0,
                captures=[capture(label+"-before",rgb),capture(label+"-enabled",enabled),capture(label+"-after",rgb)]))
    return dict(schema=1,assemblySha256="a"*64,complete=True,bombVisibility=dict(schema=1,status="PASS",failures=[],
        matchState="InProgress",MenuPause=False,DialogPause=False,processFrame=True,pixelThreshold=2,fixtures=fixtures,cases=cases))


class BombAuditTests(unittest.TestCase):
    def setUp(self):
        self.server,self.peers,self.manifest,self.summary=fixture();self.logs={"server":"clean","peer-0":"clean"}
    def result(self,kinds=("MorphBall",),visibility=None):
        return bomb.audit(self.server,self.peers,self.manifest,self.summary,kinds,self.logs,visibility)
    def check(self,status,field):
        r=self.result();self.assertEqual(r["gameplayStatus"],status,r)
        self.assertTrue(any(field in f["field"] for f in r["findings"]),r)
    def test_complete_gameplay_does_not_imply_pixels(self):
        r=self.result();self.assertEqual(r["gameplayStatus"],"PASS",r);self.assertEqual(r["status"],"INCOMPLETE")
        self.assertEqual(self.result(visibility=dict(status="PASS"))["visibilityStatus"],"INCOMPLETE")
    def test_malformed_key(self):
        self.server["Bombs"]["Rows"][2]["Key"]["Generation"]=0;self.check("FAIL","Rows")
    def test_missing_ledger(self):
        del self.peers["peer-0"]["Bombs"];self.check("INCOMPLETE","Bombs")
    def test_duplicate_authority_event(self):
        self.server["Bombs"]["Rows"].append(copy.deepcopy(self.server["Bombs"]["Rows"][2]));self.check("FAIL","Sequence")
    def test_lost_authority_birth(self):
        self.server["Bombs"]["Rows"].pop(2);self.check("FAIL","Terminal")
    def test_lost_authority_death(self):
        self.server["Bombs"]["Rows"].pop(5);self.check("FAIL","Terminal")
    def test_out_of_order_client(self):
        rs=self.peers["peer-0"]["Bombs"]["Rows"];rs[2],rs[4]=rs[4],rs[2];self.check("FAIL","Event")
    def test_lost_client_birth(self):
        self.peers["peer-0"]["Bombs"]["Rows"].pop(2);self.check("FAIL","Event")
    def test_lost_client_death(self):
        self.peers["peer-0"]["Bombs"]["Rows"].pop(4);self.check("FAIL","Event")
    def test_duplicate_client_event(self):
        rs=self.peers["peer-0"]["Bombs"]["Rows"];rs.append(copy.deepcopy(rs[4]));self.check("FAIL","Event")
    def test_latejoin_live_baseline(self):
        self.peers["peer-0"]["Bombs"]["Rows"]=self.peers["peer-0"]["Bombs"]["Rows"][3:]
        r=self.result();self.assertEqual(r["gameplayStatus"],"PASS",r)
        self.assertEqual(r["metrics"]["peers"]["peer-0"]["bootstrapFrontier"],1)
    def test_latejoin_missing_live_baseline(self):
        rs=self.peers["peer-0"]["Bombs"]["Rows"]
        self.peers["peer-0"]["Bombs"]["Rows"]=rs[3:];rs[3]["States"]=[];self.check("FAIL","Snapshot.State")
    def test_snapshot_exact_pose_and_kind(self):
        s=self.peers["peer-0"]["Bombs"]["Rows"][3]["States"][0];s["Position"]["X"]=1;self.check("FAIL","Snapshot.State")
    def test_snapshot_exact_life(self):
        self.peers["peer-0"]["Bombs"]["Rows"][3]["States"][0]["Key"]["LifeId"]=2;self.check("FAIL","Snapshot.State")
    def test_authority_snapshot_unavailable(self):
        self.server["Bombs"]["Rows"]=[r for r in self.server["Bombs"]["Rows"] if r["Type"]!="Snapshot"]
        self.check("INCOMPLETE","Snapshot")
    def test_damage_wrong_life(self):
        self.server["Bombs"]["Rows"][4]["Key"]["LifeId"]=2;self.check("FAIL","Damage.Key")
    def test_damage_requires_detonation(self):
        self.server["Bombs"]["Rows"][5]["Kind"]="Removed";self.check("FAIL","Damage.Key")
    def test_contact_payment_can_precede_terminal_44_frames(self):
        self.assertEqual(self.result()["gameplayStatus"],"PASS")
    def test_damage_after_terminal(self):
        self.server["Bombs"]["Rows"][4]["Frame"]=57;self.check("FAIL","Damage.Frame")
    def test_duplicate_damage_event(self):
        self.server["Bombs"]["Rows"].append(copy.deepcopy(self.server["Bombs"]["Rows"][4]));self.check("FAIL","DamageEvent")
    def test_wrong_request_action(self):
        self.server["Bombs"]["Rows"][1]["Action"]="Detonate";self.check("FAIL","Request")
    def test_zero_damage_coverage(self):
        self.server["Bombs"]["Rows"].pop(4);self.check("INCOMPLETE","coverage.MorphBall")
    def test_all_kinds_require_independent_coverage(self):
        self.assertEqual(self.result(bomb.KINDS)["gameplayStatus"],"INCOMPLETE")
    def test_pending_fault_and_frontier(self):
        d=self.manifest["drain"]["peers"]["peer-0"]
        for f in ("BombPendingRequests","BombPendingEvents","BombFaulted"):
            d[f]=1;self.check("FAIL",f);d[f]=False if f == "BombFaulted" else 0
        d["BombFrontier"]=1;self.check("FAIL","BombFrontier")
    def test_health_mismatch(self):
        self.peers["peer-0"]["States"][0]["Hp"]=88;self.check("FAIL","States")
    def test_exit_failure(self):
        self.summary["processes"][1]["exit"]=1;self.check("FAIL","process.exit")
    def test_overflow(self):
        self.server["Bombs"]["Overflow"]=1;self.check("FAIL","Overflow")
    def test_missing_log(self):
        del self.logs["peer-0"];self.check("INCOMPLETE","log")
    def test_shots_not_required(self):
        self.peers["peer-0"]["Drain"]["OfferedShotKeys"]=["unrelated-shot"]
        self.manifest["drain"]["peers"]["peer-0"]["OfferedShotKeys"]=["unrelated-shot"]
        self.summary.update(status="FAIL",failures=["no offered shots"])
        self.assertEqual(self.result()["gameplayStatus"],"PASS")
    def add_resolution(self,kind="Refused",action="Place"):
        identity=state(2)
        for field in ("Position","Facing","Up","Speed"):
            identity[field]=dict(X=0,Y=0,Z=0)
        identity.update(PlacedFrame=0,Countdown=0)
        row=event(3,kind,identity,58);row["Reason"]=1 if kind == "Refused" else 0
        self.server["Bombs"]["Rows"].extend([request(2,action),row])
        for r in (request(2,action),row):
            r=copy.deepcopy(r);r["Stage"]="requested" if r["Type"] == "Request" else "applied"
            self.peers["peer-0"]["Bombs"]["Rows"].append(r)
        self.manifest["drain"]["release"]["Bomb"]=3
        for d in (self.server["Drain"],self.peers["peer-0"]["Drain"],self.manifest["drain"]["server"],self.manifest["drain"]["peers"]["peer-0"]):
            d["BombFrontier"]=3;d["AtRelease"]["BombFrontier"]=3;d["Release"]["Bomb"]=3
    def test_refused_request_exactly_resolves(self):
        self.add_resolution();self.assertEqual(self.result()["gameplayStatus"],"PASS")
    def test_manual_detonation_request_exactly_resolves(self):
        self.add_resolution("DetonateAccepted","Detonate");self.assertEqual(self.result()["gameplayStatus"],"PASS")
    def test_det_accept_cannot_resolve_placement_request(self):
        self.add_resolution("DetonateAccepted","Place");self.check("FAIL","Request.Action")
    def test_duplicate_request_cannot_be_hidden_by_dictionary(self):
        self.server["Bombs"]["Rows"].append(request());self.check("FAIL","Request")
    def test_release_receipt_frontier_is_exact(self):
        self.peers["peer-0"]["Drain"]["AtRelease"]["BombFrontier"]=1
        self.check("FAIL","AtRelease.BombFrontier")
    def test_shutdown_session_reset_keeps_release_proof(self):
        self.server["Drain"].update(Epoch=0,Match=0,BombFrontier=0)
        self.assertEqual(self.result()["gameplayStatus"],"PASS")
    def triangle_fixture(self):
        states=[state(1,"Lockjaw",0),state(2,"Lockjaw",1,(1,0),x=1),state(3,"Lockjaw",2,(1,2),x=2)]
        rows=[snapshot(0,0,[])]
        for i,s in enumerate(states,1):rows.extend([request(i),event(i,"Placed",s,10)])
        rows.extend([snapshot(11,3,states),damage(kind="Lockjaw")])
        for i,s in enumerate(states,4):rows.append(event(i,"Detonated",s,56))
        rows.append(snapshot(57,6,[]))
        self.server["Bombs"]["Rows"]=rows
        self.peers["peer-0"]["Bombs"]["Rows"]=[]
        for row in rows:
            if row["Type"] == "Damage":continue
            r=copy.deepcopy(row);r["Stage"]="requested" if r["Type"] == "Request" else "applied"
            self.peers["peer-0"]["Bombs"]["Rows"].append(r)
        self.manifest["drain"]["release"]["Bomb"]=6
        for d in (self.server["Drain"],self.peers["peer-0"]["Drain"],self.manifest["drain"]["server"],self.manifest["drain"]["peers"]["peer-0"]):
            d["BombFrontier"]=6;d["AtRelease"]["BombFrontier"]=6;d["Release"]["Bomb"]=6
    def test_progressive_three_edge_lockjaw_triangle(self):
        self.triangle_fixture();self.assertEqual(self.result(("Lockjaw",))["gameplayStatus"],"PASS")
    def test_no_triangle_claim_from_pair_only(self):
        self.triangle_fixture()
        for report in (self.server,self.peers["peer-0"]):
            for row in report["Bombs"]["Rows"]:
                if row["Type"] == "Snapshot" and row["States"]:
                    row["States"][2]["Link1"]=0
        self.assertEqual(self.result(("Lockjaw",))["gameplayStatus"],"INCOMPLETE")
    def test_link_needs_same_owner_admitted_key(self):
        self.triangle_fixture()
        snap=next(r for r in self.server["Bombs"]["Rows"] if r["Type"] == "Snapshot" and r["States"])
        snap["States"][2]["Link1"]=77
        self.assertEqual(self.result(("Lockjaw",))["gameplayStatus"],"FAIL")
    def test_strict_n8_missing_drain_fields_cannot_pass_as_legacy(self):
        for field in ("BombFrontier","BombPendingRequests","BombPendingEvents","BombFaulted"):
            saved=self.peers["peer-0"]["Drain"].pop(field)
            self.assertNotEqual(self.result()["gameplayStatus"],"PASS")
            self.peers["peer-0"]["Drain"][field]=saved
    def test_strict_n8_missing_release_bomb_frontier_cannot_pass(self):
        del self.manifest["drain"]["release"]["Bomb"]
        self.assertNotEqual(self.result()["gameplayStatus"],"PASS")
    def test_applied_snapshot_frontier_regression(self):
        self.peers["peer-0"]["Bombs"]["Rows"].append(dict(snapshot(58,1,[state()]),Stage="applied"))
        self.check("FAIL","Snapshot.Frontier")
    def test_gpu_render_fixture_is_separate_from_network_keys(self):
        with tempfile.TemporaryDirectory() as directory:
            r=self.result(visibility=gpu_fixture(directory))
            self.assertEqual(r["gameplayStatus"],"PASS",r);self.assertEqual(r["renderFixtureStatus"],"PASS",r)
            self.assertEqual(r["liveVisibilityStatus"],"INCOMPLETE");self.assertEqual(r["status"],"INCOMPLETE")
    def test_gpu_engine_binding_required(self):
        with tempfile.TemporaryDirectory() as directory:
            report=gpu_fixture(directory);report["assemblySha256"]="b"*64
            r=self.result(visibility=report);self.assertEqual(r["renderFixtureStatus"],"FAIL")
            self.assertEqual(r["gameplayStatus"],"PASS");self.assertEqual(r["status"],"FAIL")
            del report["assemblySha256"]
            self.assertEqual(self.result(visibility=report)["renderFixtureStatus"],"INCOMPLETE")
    def test_gpu_missing_duplicate_and_failed_case_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            original=gpu_fixture(directory)
            for mutate in (lambda rows: rows.pop(),lambda rows: rows.append(copy.deepcopy(rows[0])),lambda rows: rows[0].update(status="FAIL")):
                report=copy.deepcopy(original);mutate(report["bombVisibility"]["cases"])
                self.assertEqual(self.result(visibility=report)["renderFixtureStatus"],"FAIL")
    def test_gpu_recomputes_pixel_counters(self):
        with tempfile.TemporaryDirectory() as directory:
            report=gpu_fixture(directory);report["bombVisibility"]["cases"][0]["changedPixels"]=10
            self.assertEqual(self.result(visibility=report)["renderFixtureStatus"],"FAIL")
    def test_gpu_png_missing_or_hash_changed_cannot_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            report=gpu_fixture(directory);capture=report["bombVisibility"]["cases"][0]["captures"][0]
            capture["pngSha256"]="c"*64
            self.assertEqual(self.result(visibility=report)["renderFixtureStatus"],"FAIL")
            Path(capture["path"]).unlink()
            self.assertEqual(self.result(visibility=report)["renderFixtureStatus"],"INCOMPLETE")
    def test_runner_bomb_summary_uses_gameplay_status(self):
        spec=importlib.util.spec_from_file_location("run_net2",Path(__file__).with_name("run-net2.py"))
        runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory() as directory:
            out=Path(directory)
            for name,report in (("server",self.server),("peer-0",self.peers["peer-0"])):
                (out/(name+".json")).write_text(json.dumps(report));(out/(name+".log")).write_text("clean")
            summary=runner.bomb_summary(out,self.summary["processes"],self.manifest,"bomb-samus")
            self.assertEqual(summary["status"],"PASS",summary)
            self.assertEqual(summary["coverage"]["renderedBombVisibility"],"INCOMPLETE")
            self.assertTrue((out/"bomb-audit.json").exists())
    def test_same_key_kanden_motion_and_rotation(self):
        self.server,self.peers,self.manifest,self.summary=fixture("Stinglarva")
        self.assertEqual(self.result(("Stinglarva",))["gameplayStatus"],"INCOMPLETE")
        snap=snapshot(12,1,[state(kind="Stinglarva",x=1,rotated=True)])
        self.server["Bombs"]["Rows"].insert(4,snap)
        snap=copy.deepcopy(snap);snap["Stage"]="applied";self.peers["peer-0"]["Bombs"]["Rows"].insert(4,snap)
        self.assertEqual(self.result(("Stinglarva",))["gameplayStatus"],"PASS")

if __name__ == "__main__":unittest.main()
