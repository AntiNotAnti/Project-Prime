"""Fail-closed checks for the opt-in headless shutdown barrier."""
import copy
import unittest
from net2_evidence import drain_barrier, drain_coverage, drain_prepared, DrainStability


class DrainTests(unittest.TestCase):
    def setUp(self):
        self.release = dict(Status="PASS", Epoch=111, Match=2, Semantic=3, Award=1)
        self.server = dict(Enabled=True, Role="server", Quiescing=False, Epoch=111, Match=2,
            SemanticLatest=3, SemanticPresented=3, AwardLatest=1, AwardPresented=1,
            ActiveProjectiles=0, ActiveBombs=0, ClaimsPending=0, ClaimGraceSeconds=2.4,
            OfferedShotKeys=[], AcceptedShotKeys=["111:2:0:1:1:9"],
            Reliable=[dict(Endpoint="a", ConnectionId=1, Pending=0, Failed=False)],
            ConnectionLost=False, Error=None)
        self.peer = dict(self.server, Role="client", Quiescing=True,
                         OfferedShotKeys=["111:2:0:1:1:9"], AcceptedShotKeys=[])
        self.peer["Reliable"] = copy.deepcopy(self.server["Reliable"])
        self.manifest = dict(settings=dict(drain_seconds=15, players=1), drain=dict(status="PASS",
            release=self.release, server=self.server, peers={"peer": self.peer}, stableSeconds=2.5))
        at = {key: self.peer[key] for key in ("Epoch", "Match", "SemanticLatest", "SemanticPresented",
                                             "AwardLatest", "AwardPresented")}
        self.server.update(Release=self.release, AtRelease=copy.deepcopy(at))
        exported = dict(self.peer, Release=self.release, AtRelease=at)
        fact = dict(Epoch=111, Match=2, Baseline=False, Award=False, Id=1, Payload="1", AwardFrontier=0)
        facts = [dict(fact, Id=i, Payload=str(i)) for i in range(1, 4)] + [dict(fact, Award=True, Payload="award")]
        self.reports = {"server": dict(Drain=dict(exported, Quiescing=False), Facts=copy.deepcopy(facts)),
                        "peer": dict(Drain=exported, Facts=[dict(fact, Baseline=True, Id=0)] + copy.deepcopy(facts))}

    def test_complete_barrier_and_frozen_interval_pass(self):
        self.assertEqual(drain_barrier(self.server, [self.peer]), (self.release, None))
        self.assertEqual(drain_coverage(self.manifest, self.reports), ("PASS", []))

    def test_default_off_preserves_legacy_adjudication(self):
        self.assertEqual(drain_coverage({}, {}), ("NOT_RUN", []))

    def test_authority_must_acknowledge_preparation_before_peer_release(self):
        self.assertEqual(drain_prepared(self.server, self.release), (True, None))
        self.server["Release"] = None
        self.assertFalse(drain_prepared(self.server, self.release)[0])
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_departure_frontier_race_is_rejected_during_preparation(self):
        self.server["AtRelease"]["SemanticLatest"] = 4
        self.assertFalse(drain_prepared(self.server, self.release)[0])

    def test_cached_preparation_pass_does_not_waive_timeout_fail(self):
        self.manifest["drain"]["status"] = "FAIL"
        self.manifest["drain"]["release"] = dict(self.release, Status="FAIL")
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_each_live_authority_resource_blocks(self):
        for key in ("ActiveProjectiles", "ActiveBombs", "ClaimsPending"):
            with self.subTest(key=key):
                server = dict(self.server, **{key: 1})
                self.assertIsNone(drain_barrier(server, [self.peer])[0])

    def test_reliable_pending_failure_and_missing_endpoint_block(self):
        for reliable in ([], [dict(Pending=1, Failed=False)], [dict(Pending=0, Failed=True)]):
            self.assertIsNone(drain_barrier(dict(self.server, Reliable=reliable), [self.peer])[0])

    def test_periodic_pending_traffic_does_not_reset_gameplay_grace(self):
        stable = DrainStability()
        peers = {"peer": self.peer}
        self.assertEqual(stable.observe(self.server, peers, 10)[2], 0)
        self.server["Reliable"][0]["Pending"] = 2
        self.assertEqual(stable.observe(self.server, peers, 12.5)[2], 2.5)
        # A quiet gameplay frontier still cannot prepare or release while any reliable packet is pending.
        self.assertIsNone(drain_barrier(self.server, [self.peer])[0])
        self.server["Reliable"][0]["Pending"] = 0
        self.assertEqual(stable.observe(self.server, peers, 13)[2], 3)
        self.assertEqual(drain_barrier(self.server, [self.peer])[0], self.release)

    def test_late_frontier_and_offered_keys_restart_full_grace(self):
        stable = DrainStability()
        peers = {"peer": self.peer}
        stable.observe(self.server, peers, 10)
        for row in (self.server, self.peer):
            row["SemanticLatest"] = row["SemanticPresented"] = 4
        self.assertEqual(stable.observe(self.server, peers, 13)[2], 0)
        self.assertEqual(stable.observe(self.server, peers, 15)[2], 2)
        self.peer["OfferedShotKeys"].append("111:2:0:1:1:10")
        self.server["AcceptedShotKeys"].append("111:2:0:1:1:10")
        self.assertEqual(stable.observe(self.server, peers, 16)[2], 0)

    def test_failed_queue_and_late_flight_invalidate_gameplay_grace(self):
        stable = DrainStability()
        peers = {"peer": self.peer}
        stable.observe(self.server, peers, 10)
        self.server["Reliable"][0]["Failed"] = True
        self.assertIsNone(stable.observe(self.server, peers, 13)[0])
        self.server["Reliable"][0]["Failed"] = False
        self.assertEqual(stable.observe(self.server, peers, 14)[2], 0)
        self.peer["ActiveProjectiles"] = 1
        self.assertIsNone(stable.observe(self.server, peers, 15)[0])

    def test_saved_preparation_pending_queue_cannot_be_waived(self):
        self.manifest["drain"].update(prepareServer=copy.deepcopy(self.server),
                                     preparePeers={"peer": self.peer}, prepareStableSeconds=2.5)
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "PASS")
        self.manifest["drain"]["prepareServer"]["Reliable"][0]["Pending"] = 1
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_peer_reliable_endpoint_missing_failed_and_pending_fail_closed(self):
        for reliable in (None, [], [dict(Pending=0, Failed=True)], [dict(Pending=-1, Failed=False)]):
            peer = dict(self.peer, Reliable=reliable)
            self.assertIsNone(drain_barrier(self.server, [peer], require_empty=False)[0])
            self.assertIsNone(drain_barrier(self.server, [peer])[0])
        pending = dict(self.peer, Reliable=[dict(Pending=1, Failed=False)])
        self.assertIsNotNone(drain_barrier(self.server, [pending], require_empty=False)[0])
        self.assertIsNone(drain_barrier(self.server, [pending])[0])

    def test_peer_pending_claim_resets_gameplay_stability(self):
        stable = DrainStability()
        peers = {"peer": self.peer}
        stable.observe(self.server, peers, 10)
        self.peer["ClaimsPending"] = 1
        self.assertIsNone(stable.observe(self.server, peers, 13)[0])
        self.peer["ClaimsPending"] = 0
        self.assertEqual(stable.observe(self.server, peers, 14)[2], 0)

    def test_reordered_received_frontier_is_not_presented_frontier(self):
        self.assertIsNone(drain_barrier(self.server, [dict(self.peer, SemanticPresented=1)])[0])

    def test_peer_must_quiesce_in_same_match(self):
        for change in (dict(Quiescing=False), dict(Match=3), dict(ConnectionLost=True), dict(Error="overflow")):
            self.assertIsNone(drain_barrier(self.server, [dict(self.peer, **change)])[0])

    def test_final_release_shot_must_be_accepted(self):
        self.assertIsNone(drain_barrier(dict(self.server, AcceptedShotKeys=[]), [self.peer])[0])

    def test_late_client_flight_cannot_generate_a_claim_after_release(self):
        for key in ("ActiveProjectiles", "ActiveBombs"):
            self.assertIsNone(drain_barrier(self.server, [dict(self.peer, **{key: 1})])[0])

    def test_missing_status_fields_fail_closed(self):
        del self.server["ClaimGraceSeconds"]
        self.assertIsNone(drain_barrier(self.server, [self.peer])[0])
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_grace_and_timeout_cannot_pass(self):
        self.manifest["drain"]["stableSeconds"] = 1
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")
        self.manifest["drain"]["status"] = "FAIL"
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_exported_release_and_frontier_are_required(self):
        self.reports["peer"]["Drain"]["AtRelease"]["SemanticPresented"] = 1
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_full_interval_includes_shutdown_tail(self):
        self.reports["peer"]["Facts"] = [f for f in self.reports["peer"]["Facts"] if f["Award"] or f["Id"] != 3]
        status, gaps = drain_coverage(self.manifest, self.reports)
        self.assertEqual(status, "FAIL")
        self.assertTrue(any("1 missing" in gap for gap in gaps))

    def test_missing_authority_fact_cannot_shrink_expected_interval(self):
        self.reports["server"]["Facts"] = [f for f in self.reports["server"]["Facts"] if f["Award"] or f["Id"] != 2]
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_award_payload_conflict_fails(self):
        self.reports["peer"]["Facts"][-1]["Payload"] = "wrong"
        self.assertEqual(drain_coverage(self.manifest, self.reports)[0], "FAIL")

    def test_rendered_drain_uses_same_exact_barrier(self):
        self.manifest["settings"]["rendered"]=True
        self.assertEqual(drain_coverage(self.manifest,self.reports),("PASS",[]))
        self.peer["Reliable"][0]["Pending"]=1
        self.assertEqual(drain_coverage(self.manifest,self.reports)[0],"FAIL")

    def test_soak_drain_remains_unsupported(self):
        self.manifest["settings"]["soak"]=True
        self.assertEqual(drain_coverage(self.manifest,self.reports)[0],"FAIL")

    def n8_status(self):
        self.server.update(BombFrontier=8,BombPendingRequests=0,BombPendingEvents=0,BombFaulted=False)
        self.peer.update(BombFrontier=8,BombPendingRequests=0,BombPendingEvents=0,BombFaulted=False)
    def test_optional_n8_frontier_includes_release_without_breaking_legacy(self):
        self.assertEqual(drain_barrier(self.server,[self.peer]),(self.release,None))
        self.n8_status();release=drain_barrier(self.server,[self.peer])[0]
        self.assertEqual(release,dict(self.release,Bomb=8))
    def test_n8_missing_peer_fields_or_regressing_frontier_blocks(self):
        self.n8_status()
        for field in ("BombFrontier","BombPendingRequests","BombPendingEvents","BombFaulted"):
            peer=copy.deepcopy(self.peer);del peer[field]
            self.assertIsNone(drain_barrier(self.server,[peer])[0])
        self.peer["BombFrontier"]=7;self.assertIsNone(drain_barrier(self.server,[self.peer])[0])
    def test_n8_missing_authority_fields_and_pending_fault_blocks(self):
        self.n8_status()
        for field in ("BombPendingRequests","BombPendingEvents","BombFaulted"):
            server=copy.deepcopy(self.server);del server[field]
            self.assertIsNone(drain_barrier(server,[self.peer])[0])
            server=copy.deepcopy(self.server);server[field]=1
            self.assertIsNone(drain_barrier(server,[self.peer])[0])


if __name__ == "__main__":
    unittest.main()
