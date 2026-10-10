import random
import time
import uuid
from abc import ABC, abstractmethod

# Statuses that lose consensus authority (propose / lead / win / vote).
# Nodes stay on the network, keep sending messages and keep being scored,
# so they can still be healed and re-ranked.
INELIGIBLE_STATUSES = ('Quarantined', 'Blacklisted')


def is_eligible(node) -> bool:
    return node.status not in INELIGIBLE_STATUSES


class BaseConsensus(ABC):
    name: str

    @abstractmethod
    async def execute_round(self, network) -> bool:
        """Execute one round. Returns True if block was produced."""
        pass

    @abstractmethod
    def apply_mitigation(self, node) -> None:
        """Apply protocol-specific mitigation to a flagged node."""
        pass

    def rejected_attempt(self, network, msg_type: str, block_id: str) -> None:
        """An ineligible Byzantine node still tries to propose; honest nodes reject it.
        Broadcasts the proposal (so the UI shows the rejection) and records a rejected block."""
        from ..node import Message
        attackers = [n for n in network.nodes if not is_eligible(n) and n.is_byzantine]
        if not attackers or random.random() > 0.5:
            return
        attacker = random.choice(attackers)
        for node in network.nodes:
            if node.id != attacker.id:
                network.add_message(Message(
                    id=str(uuid.uuid4()), sender_id=attacker.id, receiver_id=node.id,
                    type=msg_type, payload={"block_id": block_id}, timestamp=time.time()))
        network.latest_blocks.append({
            "hash": "0x" + uuid.uuid4().hex[:8].upper(),
            "proposer": attacker.id,
            "tx_count": random.randint(10, 50),
            "consensus": self.name,
            "round": network.current_round,
            "is_rejected": True
        })
