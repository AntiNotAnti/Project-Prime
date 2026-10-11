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
    manifest=dict(source="fixture-source",settings=dict(players=1,protocol=48,drain_seconds=5),assemblySha256="a"*64,
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
    def result(self,kinds=("MorphBall",),visibility=None,live=None):
        return bomb.audit(self.server,self.peers,self.manifest,self.summary,kinds,self.logs,visibility,live)
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
    def test_without_drain_later_authority_suffix_is_incomplete_not_loss(self):
        del self.manifest["drain"]
        self.manifest["settings"]["drain_seconds"]=0
        self.peers["peer-0"]["Bombs"]["Rows"]=self.peers["peer-0"]["Bombs"]["Rows"][:4]
        result=self.result()
        self.assertEqual(result["gameplayStatus"],"INCOMPLETE",result)
        self.assertTrue(any(f["field"] == "Bombs.Event.TerminalInterval" for f in result["findings"]))
        self.assertFalse(any(f["field"] == "Bombs.Event" and f["status"] == "FAIL" for f in result["findings"]))
    def test_without_drain_missing_middle_or_duplicate_still_fails(self):
        del self.manifest["drain"]
        self.manifest["settings"]["drain_seconds"]=0
        self.peers["peer-0"]["Bombs"]["Rows"].pop(2)
        self.check("FAIL","Event")
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
    def live_fixture(self,directory):
        case=gpu_fixture(directory)["bombVisibility"]["cases"][0]
        # Metadata names follow the live writer's C# field casing.
        captures=[dict(Path=c["path"],Width=c["width"],Height=c["height"],RgbSha256=c["rgbSha256"],PngSha256=c["pngSha256"]) for c in case["captures"]]
        row=dict(Type="Body",Key=key(),State=state(),Frame=15,SnapshotFrame=11,PlayerSnapshotFrame=10,
            SceneFrame=15,Attempts=1,Status="PASS",Camera=dict(X=0,Y=1,Z=2),CollisionClear=True,
            WorldDepth=.9,FixtureDepth=.5,CenterDepthClear=True,SamplePixel=dict(X=0,Y=0),
            ChangedPixels=1,UnstablePixels=0,EffectParticles=1,Captures=captures)
        return dict(Schema=1,EngineSha256="a"*64,EngineMvid="fixture",Source="fixture-source",ClientSlot=0,
                    PixelThreshold=2,Rows=[row],Overflow=0)
    def test_live_damaging_full_key_pixels_can_pass_exact_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);r=self.result(live={"peer-0":report})
            self.assertEqual(r["liveVisibilityStatus"],"PASS",r);self.assertEqual(r["status"],"PASS",r)
            self.assertEqual(r["liveVisibilityCoverage"]["peer-0"]["damagingKeysWithPositiveBodyPixels"],1)
    def terminal_fixture(self,directory):
        report=self.live_fixture(directory)
        row=report["Rows"][0]
        row.update(Type="Terminal",State=copy.deepcopy(self.server["Bombs"]["Rows"][5]["State"]),
                   BirthSequence=1,BirthFrame=10,TerminalSequence=2,TerminalFrame=56,EffectId=145,
                   BodyDrawOpportunities=0,BodyAttempts=0,Classification="BirthTerminalBeforeBodyDraw",
                   AppliedBirthDrawIndex=3,AppliedTerminalDrawIndex=3,CaptureDrawIndex=4,
                   MissingExpectedGhost=False,EffectPosition=copy.deepcopy(row["State"]["Position"]))
        fields=("Key","BirthSequence","BirthFrame","TerminalSequence","TerminalFrame","AppliedBirthDrawIndex","AppliedTerminalDrawIndex","BodyDrawOpportunities","BodyAttempts","MissingExpectedGhost")
        report["Lifecycles"]=[{field:copy.deepcopy(row[field]) for field in fields}]
        report["DrawIndex"]=4
        # Transport can deliver a long authority lifetime within one render batch.
        self.peers["peer-0"]["Bombs"]["Rows"].pop(3)
        return report
    def test_coalesced_terminal_pixels_cover_key_without_claiming_body_pixels(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.terminal_fixture(directory);r=self.result(live={"peer-0":report})
            self.assertEqual(r["status"],"PASS",r)
            coverage=r["liveVisibilityCoverage"]["peer-0"]
            self.assertEqual(coverage["damagingKeysWithPositiveBodyPixels"],0)
            self.assertEqual(coverage["damagingKeysWithPositiveTerminalPixels"],1)
            self.assertEqual(coverage["terminalFallbackKeys"],[list(bomb.key(key()))])
            self.assertIn("do not prove persistent body pixels",r["scope"]["livePixelScope"])
    def test_terminal_wrong_identity_sequence_frame_state_or_native_effect_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.terminal_fixture(directory)
            for mutate in (lambda r:r["Key"].update(LifeId=2),lambda r:r.update(BirthSequence=2),
                           lambda r:r.update(TerminalSequence=3),lambda r:r.update(BirthFrame=11),
                           lambda r:r.update(TerminalFrame=57),lambda r:r.update(EffectId=128),
                           lambda r:r["State"]["Position"].update(X=1e-9),
                           lambda r:r["State"]["Up"].update(X=1e-9)):
                report=copy.deepcopy(original);mutate(report["Rows"][0])
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_terminal_missing_or_zero_pixels_never_cover_key(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.terminal_fixture(directory)
            self.assertEqual(self.result(live={})["liveVisibilityStatus"],"INCOMPLETE")
            for status in ("INCOMPLETE","FAIL"):
                report=copy.deepcopy(original);report["Rows"][0]["Status"]=status
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],status)
            for field,value in (("ChangedPixels",0),("EffectParticles",0)):
                report=copy.deepcopy(original);report["Rows"][0][field]=value
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            report=copy.deepcopy(original);report["Rows"][0]["Captures"][1]=report["Rows"][0]["Captures"][0]
            report["Rows"][0]["ChangedPixels"]=0
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_terminal_cannot_mask_any_body_attempt_or_render_opportunity(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.terminal_fixture(directory)
            for field,value in (("BodyDrawOpportunities",1),("BodyAttempts",1),("AppliedTerminalDrawIndex",4),
                                ("CaptureDrawIndex",2),("Classification","ShortAuthorityLifetime")):
                report=copy.deepcopy(original);report["Rows"][0][field]=value
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            for status in ("FAIL","INCOMPLETE","PASS"):
                report=copy.deepcopy(original);body=self.live_fixture(directory)["Rows"][0];body["SnapshotFrame"]=0
                body["Status"]=status;report["Rows"].append(body)
                r=self.result(live={"peer-0":report})
                self.assertEqual(r["liveVisibilityStatus"],"FAIL",r)
                self.assertEqual(r["liveVisibilityCoverage"]["peer-0"]["terminalFallbackKeys"],[])
    def retired_terminal_fixture(self,directory,stage="SubsequentSnapshot"):
        report=self.terminal_fixture(directory);row=report["Rows"][0]
        row.update(Classification="ExpiredBeforeFirstBodyDraw",AppliedTerminalDrawIndex=7,CaptureDrawIndex=8,
                   RetirementSnapshotFrame=56,RetirementSnapshotFrontier=2,RetirementSnapshotKeyAbsent=True,
                   RetirementDrawIndex=3,RetirementWitnessStage=stage)
        report["DrawIndex"]=8
        for field in ("AppliedTerminalDrawIndex","RetirementSnapshotFrame","RetirementSnapshotFrontier","RetirementSnapshotKeyAbsent","RetirementDrawIndex","RetirementWitnessStage"):
            report["Lifecycles"][0][field]=row[field]
        snap=snapshot(56,2,[]);self.server["Bombs"]["Rows"].insert(6,snap)
        snap=copy.deepcopy(snap);snap["Stage"]="applied"
        self.peers["peer-0"]["Bombs"]["Rows"].insert(2 if stage=="AtBirth" else 3,snap)
        return report
    def test_retirement_has_two_exact_ordered_snapshot_witnesses(self):
        with tempfile.TemporaryDirectory() as directory:
            for stage in ("AtBirth","SubsequentSnapshot"):
                self.server,self.peers,self.manifest,self.summary=fixture()
                report=self.retired_terminal_fixture(directory,stage);r=self.result(live={"peer-0":report})
                self.assertEqual(r["status"],"PASS",r)
                proof=r["liveVisibilityCoverage"]["peer-0"]["terminalFallbackProofs"][0]
                self.assertEqual(proof["classification"],"ExpiredBeforeFirstBodyDraw")
                self.assertEqual(proof["retirementWitnessStage"],stage)
    def test_retirement_rejects_late_draw_wrong_snapshot_or_frontier(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.retired_terminal_fixture(directory)
            for field,value in (("RetirementDrawIndex",4),("RetirementSnapshotFrame",55),
                                ("RetirementSnapshotFrame",57),("RetirementSnapshotFrontier",1),
                                ("RetirementSnapshotFrontier",3),("RetirementSnapshotKeyAbsent",False),
                                ("RetirementWitnessStage","Unknown"),("BodyDrawOpportunities",1),("BodyAttempts",1)):
                report=copy.deepcopy(original);report["Rows"][0][field]=value;report["Lifecycles"][0][field]=value
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            report=copy.deepcopy(original);report["Lifecycles"][0]["RetirementSnapshotFrame"]=57
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_retirement_requires_first_absent_snapshot_and_no_present_key(self):
        with tempfile.TemporaryDirectory() as directory:
            for stage in ("AtBirth","SubsequentSnapshot"):
                self.server,self.peers,self.manifest,self.summary=fixture()
                report=self.retired_terminal_fixture(directory,stage)
                index=2 if stage=="AtBirth" else 3
                self.peers["peer-0"]["Bombs"]["Rows"][index]["States"]=[state()]
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            self.server,self.peers,self.manifest,self.summary=fixture()
            report=self.retired_terminal_fixture(directory)
            earlier=dict(snapshot(55,1,[]),Stage="applied")
            self.peers["peer-0"]["Bombs"]["Rows"].insert(3,earlier)
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_atbirth_retirement_cannot_select_old_snapshot_or_later_witness(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.retired_terminal_fixture(directory,"AtBirth")
            self.peers["peer-0"]["Bombs"]["Rows"].insert(3,dict(snapshot(57,2,[state()]),Stage="applied"))
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            self.server,self.peers,self.manifest,self.summary=fixture()
            report=self.retired_terminal_fixture(directory)
            report["Rows"][0]["RetirementWitnessStage"]="AtBirth";report["Lifecycles"][0]["RetirementWitnessStage"]="AtBirth"
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_retirement_still_cannot_mask_incomplete_body_or_missing_pixels(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.retired_terminal_fixture(directory)
            report["Rows"].append(dict(self.live_fixture(directory)["Rows"][0],Status="INCOMPLETE",SnapshotFrame=0))
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            report["Rows"].pop();report["Rows"][0]["Status"]="INCOMPLETE"
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"INCOMPLETE")
    def test_terminal_actual_effect_and_lifecycle_draw_ledger_are_exact(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.terminal_fixture(directory)
            for mutate in (lambda r:r["Rows"][0].update(MissingExpectedGhost=True),
                           lambda r:r["Rows"][0]["EffectPosition"].update(X=1e-9),
                           lambda r:r["Lifecycles"][0].update(BodyDrawOpportunities=1),
                           lambda r:r["Lifecycles"][0].update(BodyAttempts=1),
                           lambda r:r["Lifecycles"][0].update(MissingExpectedGhost=True),
                           lambda r:r.update(Lifecycles=[]),
                           lambda r:r["Lifecycles"].append(copy.deepcopy(r["Lifecycles"][0])),
                           lambda r:r.update(DrawIndex=3)):
                report=copy.deepcopy(original);mutate(report)
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_terminal_requires_applied_and_authority_accepted_facts(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.terminal_fixture(directory)
            self.peers["peer-0"]["Bombs"]["Rows"]=[r for r in self.peers["peer-0"]["Bombs"]["Rows"] if r.get("Kind")!="Placed"]
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            self.server,self.peers,self.manifest,self.summary=fixture()
            report=self.terminal_fixture(directory);self.server["Bombs"]["Rows"][5]["Kind"]="Removed"
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def orientation_fixture(self,directory):
        # Fixed real Kanden10 shot4 packet/capture: derived Up differs one ULP.
        facing=dict(X=-.09957633,Y=.00010491182,Z=-.99503)
        canonical_up=dict(X=1.0446733e-05,Y=1,Z=.0001043904)
        captured_up=dict(X=1.0446732e-05,Y=1,Z=.000104390405)
        for report in (self.server,self.peers["peer-0"]):
            for row in report["Bombs"]["Rows"]:
                values=row["States"] if row["Type"]=="Snapshot" else [row["State"]] if row["Type"]=="Event" else []
                for value in values:value.update(Facing=copy.deepcopy(facing),Up=copy.deepcopy(canonical_up))
        report=self.live_fixture(directory)
        report["Rows"][0]["State"].update(Facing=facing,Up=captured_up)
        return report
    def test_live_float32_native_transform_golden_capture(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.orientation_fixture(directory)
            r=self.result(live={"peer-0":report})
            self.assertEqual(r["status"],"PASS",r)
            self.assertEqual(len(r["liveVisibilityCoverage"]["peer-0"]["derivedOrientationJoins"]),1)
            # Rounding is centered on the reconstructed transform, not raw pose.
            captured=bomb.state(report["Rows"][0]["State"])
            canonical=bomb.state(self.peers["peer-0"]["Bombs"]["Rows"][3]["States"][0])
            self.assertNotEqual(captured,canonical)
            self.assertTrue(bomb.native_capture_matches(captured,canonical))
            derived=list(bomb.capture_orientation(canonical[4],canonical[5])[1])
            bits=struct.unpack("<I",struct.pack("<f",derived[2]))[0]
            derived[2]=struct.unpack("<f",struct.pack("<I",bits+2))[0]
            mutated=tuple(list(captured[:5])+[tuple(derived)]+list(captured[6:]))
            self.assertFalse(bomb.native_capture_matches(mutated,canonical))
    def test_live_transform_keeps_identity_time_position_speed_and_pose_strict(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.orientation_fixture(directory)
            for mutate in (lambda r:r.update(SnapshotFrame=12),lambda r:r["Key"].update(LifeId=2),
                           lambda r:r["State"]["Position"].update(X=1e-9),
                           lambda r:r["State"]["Speed"].update(Z=1e-9),
                           lambda r:r["State"]["Facing"].update(X=-.0995),
                           lambda r:r["State"]["Up"].update(Z=.000105)):
                report=copy.deepcopy(original);mutate(report["Rows"][0])
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            self.peers["peer-0"]["Bombs"]["Rows"][3]["States"][0]["Up"]=original["Rows"][0]["State"]["Up"]
            # Wire authority/client snapshot comparison remains bit-for-bit.
            self.assertEqual(self.result(live={"peer-0":original})["gameplayStatus"],"FAIL")
    def test_live_missing_report_rows_or_damaging_body_incomplete(self):
        self.assertEqual(self.result(live={})["liveVisibilityStatus"],"INCOMPLETE")
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);report["Rows"]=[]
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"INCOMPLETE")
    def test_live_wrong_life_or_state_snapshot_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.live_fixture(directory)
            for mutate in (lambda r:r["Key"].update(LifeId=2),lambda r:r.update(SnapshotFrame=12),lambda r:r["State"]["Position"].update(X=2)):
                report=copy.deepcopy(original);mutate(report["Rows"][0])
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_live_birth_newer_than_snapshot_has_exact_event_reference(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);report["Rows"][0]["SnapshotFrame"]=0
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"PASS")
    def test_live_image_counts_and_duplicate_full_identity_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.live_fixture(directory);report=copy.deepcopy(original)
            report["Rows"][0]["ChangedPixels"]=0
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
            report=copy.deepcopy(original);report["Rows"].append(copy.deepcopy(report["Rows"][0]))
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_live_engine_source_slot_and_overflow_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            original=self.live_fixture(directory)
            for field,value in (("EngineSha256","b"*64),("Source","different"),("ClientSlot",1),("Overflow",1)):
                report=copy.deepcopy(original);report[field]=value
                self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_live_center_blocked_with_positive_key_pixels_still_proves_body(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);report["Rows"][0].update(WorldDepth=.1,CenterDepthClear=False)
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"PASS")
    def test_live_native_depth_equality_and_ulong_scene_frame(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory)
            report["Rows"][0].update(WorldDepth=.5-.00001,SceneFrame=2**32+1)
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"PASS")
            report["Rows"][0]["CenterDepthClear"]=False
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_live_lockjaw_requires_three_exact_canonical_edges(self):
        self.triangle_fixture()
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);template=report["Rows"][0]
            states=self.peers["peer-0"]["Bombs"]["Rows"][7]["States"]
            report["Rows"]=[dict(copy.deepcopy(template),Key=key(),State=states[0])]
            for source,target in ((1,0),(2,0),(2,1)):
                report["Rows"].append(dict(copy.deepcopy(template),Type="Link",Key=states[source]["Key"],
                    State=states[source],TargetKey=states[target]["Key"],TargetState=states[target]))
            self.assertEqual(self.result(("Lockjaw",),live={"peer-0":report})["liveVisibilityStatus"],"PASS")
            report["Rows"].pop()
            self.assertEqual(self.result(("Lockjaw",),live={"peer-0":report})["liveVisibilityStatus"],"INCOMPLETE")
            report["Rows"][-1]["TargetKey"]=key(9)
            self.assertEqual(self.result(("Lockjaw",),live={"peer-0":report})["liveVisibilityStatus"],"FAIL")
    def test_runner_loads_live_report_only_when_manifest_requests_it(self):
        spec=importlib.util.spec_from_file_location("run_net2",Path(__file__).with_name("run-net2.py"))
        runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory() as directory:
            out=Path(directory)
            for name,report in (("server",self.server),("peer-0",self.peers["peer-0"])):
                (out/(name+".json")).write_text(json.dumps(report));(out/(name+".log")).write_text("clean")
            (out/"peer-0.visibility.json").write_text(json.dumps(self.live_fixture(directory)))
            self.manifest["settings"]["bomb_visibility"]=True
            summary=runner.bomb_summary(out,self.summary["processes"],self.manifest,"bomb-samus")
            self.assertEqual(summary["coverage"]["renderedBombVisibility"],"PASS",summary)
            self.assertEqual(summary["status"],"PASS",summary)
            self.assertEqual(json.loads((out/"bomb-audit.json").read_text())["liveVisibilityStatus"],"PASS")
            for probe,expected in (("INCOMPLETE","INCOMPLETE"),("FAIL","FAIL")):
                report=self.live_fixture(directory);report["Rows"][0]["Status"]=probe
                (out/"peer-0.visibility.json").write_text(json.dumps(report))
                summary=runner.bomb_summary(out,self.summary["processes"],self.manifest,"bomb-samus")
                self.assertEqual(summary["status"],expected,summary)
                self.assertEqual(summary["gameplayStatus"],"PASS",summary)
                self.assertTrue(summary["incomplete"] if expected=="INCOMPLETE" else summary["failures"])
            self.manifest["settings"]["bomb_visibility"]=False
            summary=runner.bomb_summary(out,self.summary["processes"],self.manifest,"bomb-samus")
            self.assertEqual(summary["coverage"]["renderedBombVisibility"],"INCOMPLETE")
            self.assertEqual(summary["status"],"PASS")
    def test_live_no_alive_snapshot_and_no_body_witness_not_fabricated(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);report["Rows"]=[]
            self.peers["peer-0"]["Bombs"]["Rows"].pop(3)
            r=self.result(live={"peer-0":report});self.assertEqual(r["liveVisibilityStatus"],"INCOMPLETE")
            self.assertEqual(len(r["liveVisibilityCoverage"]["peer-0"]["missingWithNoAppliedSnapshot"]),1)
            self.assertEqual(r["liveVisibilityCoverage"]["peer-0"]["terminalFallbackKeys"],[])
            self.assertEqual(r["liveVisibilityCoverage"]["peer-0"]["damagingKeysWithPositiveTerminalPixels"],0)
    def test_live_incomplete_probe_and_prebootstrap_damage_are_scoped(self):
        with tempfile.TemporaryDirectory() as directory:
            report=self.live_fixture(directory);report["Rows"][0]=dict(report["Rows"][0],Status="INCOMPLETE",Reason="no vantage")
            for field in ("Camera","CollisionClear","WorldDepth","FixtureDepth","CenterDepthClear","SamplePixel","ChangedPixels","UnstablePixels","EffectParticles","Captures"):
                report["Rows"][0].pop(field)
            self.assertEqual(self.result(live={"peer-0":report})["liveVisibilityStatus"],"INCOMPLETE")
            report["Rows"]=[]
            self.peers["peer-0"]["Bombs"]["Rows"]=[dict(snapshot(57,2,[]),Stage="applied")]
            r=self.result(live={"peer-0":report});self.assertEqual(r["liveVisibilityStatus"],"INCOMPLETE")
            self.assertEqual(len(r["liveVisibilityCoverage"]["peer-0"]["preBootstrapDamageKeysExcluded"]),1)
    def test_runner_visibility_argument_environment_and_manifest_setting(self):
        spec=importlib.util.spec_from_file_location("run_net2",Path(__file__).with_name("run-net2.py"))
        runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)
        base=["--build","build","--paths","paths","--source","fixture-source","--out","out"]
        args=runner.parse_args(base+["--rendered","--scenario","bomb-samus","--bomb-visibility","--drain-seconds","18"])
        self.assertTrue(vars(args)["bomb_visibility"]);self.assertEqual(args.drain_seconds,18)
        env=runner.bomb_visibility_environment(args,"peer-2")
        self.assertTrue(Path(env["PRIME_NET2_BOMB_VISIBILITY"]).is_absolute())
        self.assertTrue(env["PRIME_NET2_BOMB_VISIBILITY"].endswith("peer-2.visibility.json"))
        self.assertEqual(env["PRIME_NET2_SOURCE"],"fixture-source")
        self.assertEqual(runner.bomb_visibility_environment(args,"server"),{})
        args.bomb_visibility=False;self.assertEqual(runner.bomb_visibility_environment(args,"peer-0"),{})
        import contextlib,io
        for flags in (["--bomb-visibility"],["--rendered","--bomb-visibility"],["--scenario","bomb-samus","--bomb-visibility"],["--soak","--drain-seconds","1"]):
            with contextlib.redirect_stderr(io.StringIO()),self.assertRaises(SystemExit):runner.parse_args(base+flags)
    def test_same_key_kanden_motion_and_rotation(self):
        self.server,self.peers,self.manifest,self.summary=fixture("Stinglarva")
        self.assertEqual(self.result(("Stinglarva",))["gameplayStatus"],"INCOMPLETE")
        snap=snapshot(12,1,[state(kind="Stinglarva",x=1,rotated=True)])
        self.server["Bombs"]["Rows"].insert(4,snap)
        snap=copy.deepcopy(snap);snap["Stage"]="applied";self.peers["peer-0"]["Bombs"]["Rows"].insert(4,snap)
        self.assertEqual(self.result(("Stinglarva",))["gameplayStatus"],"PASS")

if __name__ == "__main__":unittest.main()
