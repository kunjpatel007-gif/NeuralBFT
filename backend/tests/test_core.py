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
