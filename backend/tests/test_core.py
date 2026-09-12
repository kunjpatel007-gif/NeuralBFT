"""Unit tests for the Consensus Arena simulator."""
import sys
import os
import asyncio
import pytest

# Ensure backend imports work
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

from simulator.node import Node, Message
from simulator.network import NetworkManager
from simulator.consensus.base import BaseConsensus
from simulator.consensus.pow import PoWMechanism
from simulator.consensus.pos import PoSMechanism
from simulator.consensus.dpos import DPoSMechanism
from simulator.consensus.pbft import PBFTMechanism
from ml.detector import ByzantineDetector
from mitigation.policy import ReputationManager


# ── Node Tests ──────────────────────────────────────────────

class TestNode:
    def test_default_status(self):
        node = Node(id="n1")
        assert node.status == "Trusted"
        assert node.reputation == 100.0
        assert node.is_byzantine is False

    def test_update_status_trusted(self):
        node = Node(id="n1", reputation=85.0)
        node.update_status()
        assert node.status == "Trusted"

    def test_update_status_watched(self):
        node = Node(id="n1", reputation=55.0)
        node.update_status()
        assert node.status == "Watched"

    def test_update_status_quarantined(self):
        node = Node(id="n1", reputation=20.0)
        node.update_status()
        assert node.status == "Quarantined"

    def test_boundary_70(self):
        node = Node(id="n1", reputation=70.0)
        node.update_status()
        assert node.status == "Trusted"

    def test_boundary_40(self):
        node = Node(id="n1", reputation=40.0)
        node.update_status()
        assert node.status == "Watched"

    def test_boundary_39(self):
        node = Node(id="n1", reputation=39.9)
        node.update_status()
        assert node.status == "Quarantined"


# ── Message Tests ───────────────────────────────────────────

class TestMessage:
    def test_message_creation(self):
        msg = Message(id="m1", sender_id="n1", receiver_id="n2",
                      type="PREPARE", payload={"block": 1}, timestamp=1234.0)
        assert msg.sender_id == "n1"
        assert msg.receiver_id == "n2"
        assert msg.type == "PREPARE"


# ── NetworkManager Tests ────────────────────────────────────

class TestNetworkManager:
    def test_initialization(self):
        nm = NetworkManager()
        assert nm.nodes == []
        assert nm.messages_in_flight == []
        assert nm.current_round == 0

    def test_add_message(self):
        nm = NetworkManager()
        msg = Message(id="m1", sender_id="n1", receiver_id="n2",
                      type="TEST", payload=None, timestamp=0)
        nm.add_message(msg)
        assert len(nm.messages_in_flight) == 1

    def test_inject_fault(self):
        nm = NetworkManager()
        node = Node(id="target")
        nm.nodes.append(node)
        assert node.is_byzantine is False
        nm.inject_fault("target", "malicious")
        assert node.is_byzantine is True

    def test_get_state_schema(self):
        nm = NetworkManager()
        nm.nodes = [Node(id="n1"), Node(id="n2")]
        nm.active_consensus = PBFTMechanism()
        state = nm.get_state()
        # Verify required top-level keys
        assert "round" in state
        assert "consensus" in state
        assert "nodes" in state
        assert "messages" in state
        assert "metrics" in state
        # Verify node schema
        node_state = state["nodes"][0]
        assert "id" in node_state
        assert "is_byzantine" in node_state
        assert "reputation" in node_state
        assert "status" in node_state
        # Verify metrics schema
        assert "block_time_ms" in state["metrics"]
        assert "msg_overhead" in state["metrics"]

    @pytest.mark.asyncio
    async def test_run_round_increments(self):
        nm = NetworkManager()
        nm.nodes = [Node(id=f"n{i}") for i in range(5)]
        nm.active_consensus = PoWMechanism()
        state = await nm.run_round()
        assert nm.current_round == 1
        assert isinstance(state, dict)


# ── Consensus Tests ─────────────────────────────────────────

class TestConsensus:
    def _make_network(self, n=10, n_byz=2):
        nm = NetworkManager()
        for i in range(n):
            nm.nodes.append(Node(
                id=f"node_{i}",
                is_byzantine=(i < n_byz),
                reputation=100.0
            ))
        return nm

    @pytest.mark.asyncio
    async def test_pow_produces_messages(self):
        nm = self._make_network()
        pow_mech = PoWMechanism()
        nm.active_consensus = pow_mech
        result = await pow_mech.execute_round(nm)
        assert isinstance(result, bool)

    @pytest.mark.asyncio
    async def test_pos_produces_messages(self):
        nm = self._make_network()
        pos_mech = PoSMechanism()
        nm.active_consensus = pos_mech
        result = await pos_mech.execute_round(nm)
        assert isinstance(result, bool)

    @pytest.mark.asyncio
    async def test_dpos_produces_messages(self):
        nm = self._make_network()
        dpos_mech = DPoSMechanism()
        nm.active_consensus = dpos_mech
        result = await dpos_mech.execute_round(nm)
        assert isinstance(result, bool)

    @pytest.mark.asyncio
    async def test_pbft_produces_messages(self):
        nm = self._make_network()
        pbft_mech = PBFTMechanism()
        nm.active_consensus = pbft_mech
        result = await pbft_mech.execute_round(nm)
        assert result is True
        # PBFT with 10 nodes should generate many messages
        assert len(nm.messages_in_flight) > 0

    @pytest.mark.asyncio
    async def test_pbft_too_few_nodes(self):
        nm = self._make_network(n=3, n_byz=0)
        pbft_mech = PBFTMechanism()
        result = await pbft_mech.execute_round(nm)
        assert result is False  # Need at least 4 valid nodes


# ── ML Detector Tests ───────────────────────────────────────

class TestByzantineDetector:
    def test_fallback_when_no_model(self):
        detector = ByzantineDetector(model_path="nonexistent.pkl")
        prob = detector.evaluate({"msg_freq": 50, "vote_inconsistency": 0.05,
                                  "latency": 100, "fork_attempts": 0,
                                  "silence_ratio": 0.02})
        assert prob == 0.5

    def test_with_trained_model(self):
        model_path = os.path.join(os.path.dirname(__file__), '..', 'ml', 'detector.pkl')
        if not os.path.exists(model_path):
            pytest.skip("Trained model not found")
        detector = ByzantineDetector(model_path=model_path)
        # Honest node features
        prob_honest = detector.evaluate({
            "msg_freq": 50, "vote_inconsistency": 0.05,
            "latency": 100, "fork_attempts": 0, "silence_ratio": 0.02
        })
        # Byzantine node features
        prob_byz = detector.evaluate({
            "msg_freq": 120, "vote_inconsistency": 0.7,
            "latency": 500, "fork_attempts": 3, "silence_ratio": 0.5
        })
        assert prob_honest < prob_byz  # Byzantine should score higher


# ── Reputation Manager Tests ────────────────────────────────

class TestReputationManager:
    def test_reputation_decrease_on_byzantine(self):
        detector = ByzantineDetector(model_path="nonexistent.pkl")
        rm = ReputationManager(detector=detector)
        node = Node(id="n1", reputation=100.0)
        # With fallback detector (0.5), reputation shouldn't change dramatically
        features = {"n1": {"msg_freq": 50, "vote_inconsistency": 0.05,
                           "latency": 100, "fork_attempts": 0, "silence_ratio": 0.02}}
        rm.evaluate_round([node], features)
        # 0.5 probability -> no change (not > 0.7 and not < 0.3)
        assert node.reputation == 100.0

    def test_reputation_clamped(self):
        detector = ByzantineDetector(model_path="nonexistent.pkl")
        rm = ReputationManager(detector=detector)
        node = Node(id="n1", reputation=100.0)
        features = {"n1": {}}
        rm.evaluate_round([node], features)
        assert 0.0 <= node.reputation <= 100.0

    def test_get_summary(self):
        detector = ByzantineDetector(model_path="nonexistent.pkl")
        rm = ReputationManager(detector=detector)
        node = Node(id="n1", reputation=80.0)
        rm.evaluate_round([node], {"n1": {}})
        summary = rm.get_summary()
        assert "n1" in summary
        assert "reputation" in summary["n1"]
        assert "status" in summary["n1"]
        assert "byzantine_prob" in summary["n1"]

# ── Capacity Tests ──────────────────────────────────────────

class TestCapacity:
    @pytest.mark.asyncio
    async def test_capacity_50_nodes(self):
        import time
        import json
        
        nm = NetworkManager()
        for i in range(50):
            nm.nodes.append(Node(id=f"node_{i}", is_byzantine=False, reputation=100.0, status="Trusted"))
            
        nm.active_consensus = PBFTMechanism()
        
        start_time = time.time()
        for round_idx in range(10):
            await nm.run_round()
            # PBFT phases: Pre-Prepare (N-1), Prepare ((N-1) * (N-1)), Commit ((N-1) * N) roughly
            # We just want to ensure it handles the message volume without crashing.
            
            # Serialize state to ensure JSON isn't too massive
            state = nm.get_state()
            state_json = json.dumps(state)
            
            # Payload should be reasonable, mostly messages.
            # O(N^2) messages at 50 nodes is roughly 2500-7500 messages per round
            assert len(state["messages"]) > 2000
            
            # Ensure no crash and payload is < 5MB
            assert len(state_json) < 5 * 1024 * 1024 
            
            nm.messages_in_flight.clear()
            
        end_time = time.time()
        
        # 10 rounds of 50-node PBFT should compute quickly in Python
        assert (end_time - start_time) < 1.5
