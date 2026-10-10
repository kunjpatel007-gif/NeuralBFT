import pytest
import os
import asyncio
from backend.simulator.node import Node, Message
from backend.simulator.network import NetworkManager
from backend.simulator.consensus.pow import PoWMechanism
from backend.simulator.consensus.pos import PoSMechanism
from backend.simulator.consensus.pbft import PBFTMechanism
from backend.ml.detector import ByzantineDetector
from backend.mitigation.policy import ReputationManager

class TestNode:
    def test_node_initialization(self):
        node = Node(id="n1")
        assert node.id == "n1"
        assert node.is_byzantine is False
        assert node.reputation == 100.0
        assert node.status == "Trusted"

    def test_status_updates(self):
        node = Node(id="n1", reputation=95.0)
        node.update_status()
        assert node.status == "Verified"

        node.reputation = 75.0
        node.update_status()
        assert node.status == "Trusted"

        node.reputation = 55.0
        node.update_status()
        assert node.status == "Watched"

        node.reputation = 35.0
        node.update_status()
        assert node.status == "High Risk"
        
        node.reputation = 15.0
        node.update_status()
        assert node.status == "Quarantined"
        
        node.reputation = 5.0
        node.update_status()
        assert node.status == "Blacklisted"

class TestByzantineDetector:
    def test_detector_initialization(self):
        detector = ByzantineDetector()
        assert detector.nn is not None
        assert detector.iso is not None

    def test_evaluate(self):
        detector = ByzantineDetector()
        res = detector.evaluate({"msg_freq": 50})
        assert "nn_prob" in res
        assert "anomaly_score" in res
        assert "combined_threat" in res
        assert 0.0 <= res["combined_threat"] <= 1.0

class TestReputationManager:
    def test_reputation_manager_init(self):
        detector = ByzantineDetector()
        rm = ReputationManager(detector=detector)
        assert rm.detector == detector

    def test_evaluate_round(self):
        detector = ByzantineDetector()
        rm = ReputationManager(detector=detector)
        node = Node(id="n1", reputation=100.0, alpha=20.0, beta=1.0)
        
        # Honest features
        features = {"n1": {"msg_freq": 50.0, "vote_inconsistency": 0.0, "latency": 100.0, "fork_attempts": 0.0, "silence_ratio": 0.0}}
        rm.evaluate_round([node], features)
        
        assert node.reputation <= 100.0
        assert node.alpha >= 19.0


class TestConsensusEligibility:
    def _net(self):
        net = NetworkManager()
        for i in range(10):
            net.nodes.append(Node(id=f"node_{i}"))
        return net

    def test_blacklisted_never_wins_pow_or_leads(self):
        from backend.simulator.consensus.base import is_eligible
        net = self._net()
        for n in net.nodes[:3]:
            n.is_byzantine, n.status = True, "Blacklisted"
        assert not is_eligible(net.nodes[0]) and is_eligible(net.nodes[5])
        mech = PoWMechanism()
        for r in range(30):
            net.current_round = r
            block = asyncio.run(mech.execute_round(net)) if asyncio.iscoroutinefunction(mech.execute_round) else mech.execute_round(net)
        accepted = [b for b in net.latest_blocks if not b.get("is_rejected")]
        banned = {n.id for n in net.nodes[:3]}
        assert all(b.get("proposer") not in banned for b in accepted)

    def test_blocks_and_tps_bounded(self):
        net = self._net()
        for _ in range(130):
            asyncio.run(net.run_round())
        assert len(net.latest_blocks) <= NetworkManager.MAX_BLOCKS_KEPT
        assert net.get_state()["metrics"]["tps"] >= 0


class TestValidateCommand:
    def test_rejects_bad_commands(self):
        from backend.server.api import validate_command
        for bad in [{"action": "nope"}, {"action": "sybil_swarm", "count": "x"},
                    {"action": "inject_fault", "node_id": 5, "fault_type": "x"},
                    {"action": "switch_consensus", "consensus": "FOO"}]:
            assert validate_command(bad) is None

    def test_accepts_good_commands(self):
        from backend.server.api import validate_command
        assert validate_command({"action": "switch_consensus", "consensus": "DPoS"}) is not None
        assert validate_command({"action": "sybil_swarm", "count": 3}) is not None


class TestCsvBackup:
    def test_backup_when_bucket_differs(self, tmp_path):
        from unittest.mock import MagicMock
        det = ByzantineDetector()
        csv = tmp_path / "x.csv"
        csv.write_text("a,b\n1,2\n")
        det.csv_path = str(csv)
        blob = MagicMock(); blob.md5_hash = "different"
        det._backup_local_csv_if_different(blob)
        assert any("local-backup" in p.name for p in tmp_path.iterdir())
