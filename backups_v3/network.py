from typing import List, Optional, Dict
from dataclasses import dataclass
from .node import Node, Message
from .consensus.base import BaseConsensus

class NetworkManager:
    def __init__(self):
        self.nodes: List[Node] = []
        self.messages_in_flight: List[Message] = []
        self.current_round: int = 0
        self.active_consensus: Optional[BaseConsensus] = None
        self.latest_blocks: List[Dict] = []

    async def run_round(self) -> dict:
        self.current_round += 1
        if self.active_consensus:
            await self.active_consensus.execute_round(self)
        
        state = self.get_state()
        return state

    def inject_fault(self, node_id: str, fault_type: str):
        for node in self.nodes:
            if node.id == node_id:
                if fault_type == "honest":
                    node.is_byzantine = False
                    node.alpha = 20.0  # Reset Bayesian Honest counter
                    node.beta = 1.0    # Reset Bayesian Suspicious counter
                    node.reputation = 100.0
                    node.status = "Trusted"
                    if hasattr(node, "fault_type"):
                        delattr(node, "fault_type")
                else:
                    node.is_byzantine = True
                    setattr(node, "fault_type", fault_type)
                break

    def get_state(self) -> dict:
        # Calculate simple TPS based on blocks produced
        tps = len(self.latest_blocks) * 10 # Example multiplier
        return {
            "round": self.current_round,
            "consensus": self.active_consensus.name if self.active_consensus else "None",
            "nodes": [
                {
                    "id": node.id,
                    "is_byzantine": node.is_byzantine,
                    "reputation": node.reputation,
                    "status": node.status,
                    "partition_id": node.partition_id,
                    "network_profile": getattr(node, "network_profile", "Unknown"),
                    "fault_type": getattr(node, "fault_type", None),
                    "ml_prob": getattr(node, "ml_prob", 0.0),
                    "alpha": getattr(node, "alpha", 20.0),
                    "beta": getattr(node, "beta", 1.0),
                    "ml_features": getattr(node, "ml_features", {}),
                    "ml_detail": getattr(node, "ml_detail", {})
                } for node in self.nodes
            ],
            "messages": [
                {
                    "from": msg.sender_id,
                    "to": msg.receiver_id,
                    "type": msg.type
                } for msg in self.messages_in_flight
            ],
            "blocks": self.latest_blocks[-10:], # Send last 10 blocks for ticker
            "metrics": {
                "tps": tps,
                "block_time_ms": 120,
                "msg_overhead": len(self.messages_in_flight)
            }
        }

    def add_message(self, msg: Message):
        # Partition Check (Split-Brain)
        sender = next((n for n in self.nodes if n.id == msg.sender_id), None)
        receiver = next((n for n in self.nodes if n.id == msg.receiver_id), None)
        
        if sender and receiver:
            if sender.partition_id != receiver.partition_id:
                return # DROP MESSAGE: Cross-partition communication severed
                
        self.messages_in_flight.append(msg)
