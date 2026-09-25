import random
import time
import uuid
from .base import BaseConsensus
from ..node import Message

class PoWMechanism(BaseConsensus):
    name = "PoW"
    
    def __init__(self):
        self.difficulty = 0.05
        
    async def execute_round(self, network) -> bool:
        block_produced = False
        winner = None
        
        for node in network.nodes:
            if node.status == 'Quarantined':
                continue
                
            threshold = self.difficulty
            if node.is_byzantine:
                threshold = self.difficulty * 2
                
            if random.random() < threshold:
                if not winner or (node.is_byzantine and random.random() > 0.5):
                    winner = node
                    
        if winner:
            block_produced = True
            valid_receivers = [n for n in network.nodes if n.status != 'Quarantined']
            for node in valid_receivers:
                if node.id != winner.id:
                    msg = Message(
                        id=str(uuid.uuid4()),
                        sender_id=winner.id,
                        receiver_id=node.id,
                        type="BLOCK_ANNOUNCEMENT",
                        payload={"block_id": "block_pow"},
                        timestamp=time.time()
                    )
                    network.add_message(msg)
            block_hash = "0x" + uuid.uuid4().hex[:8].upper()
            network.latest_blocks.append({
                "hash": block_hash,
                "proposer": winner.id,
                "tx_count": random.randint(10, 50),
                "consensus": "PoW"
            })
                    
        return block_produced
        
    def apply_mitigation(self, node) -> None:
        if node.status == 'Quarantined':
            pass
