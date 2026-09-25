import random
import time
import uuid
from .base import BaseConsensus
from ..node import Message

class PoSMechanism(BaseConsensus):
    name = "PoS"
    
    async def execute_round(self, network) -> bool:
        weights = []
        valid_nodes = []
        for node in network.nodes:
            weight = node.reputation
            if node.status == 'Watched':
                weight *= 0.3
            elif node.status == 'Quarantined':
                weight *= 0.0
                
            weights.append(weight)
            valid_nodes.append(node)
            
        total_weight = sum(weights)
        if total_weight <= 0:
            return False
            
        proposer = random.choices(valid_nodes, weights=weights, k=1)[0]
        
        valid_receivers = [n for n in network.nodes if n.status != 'Quarantined']
        
        # Byzantine behavior: send conflicting blocks
        if proposer.is_byzantine:
            for node in valid_receivers:
                if node.id != proposer.id:
                    msg1 = Message(
                        id=str(uuid.uuid4()),
                        sender_id=proposer.id,
                        receiver_id=node.id,
                        type="BLOCK_PROPOSAL",
                        payload={"block_id": "block_pos_A"},
                        timestamp=time.time()
                    )
                    msg2 = Message(
                        id=str(uuid.uuid4()),
                        sender_id=proposer.id,
                        receiver_id=node.id,
                        type="BLOCK_PROPOSAL",
                        payload={"block_id": "block_pos_B"},
                        timestamp=time.time()
                    )
                    network.add_message(msg1)
                    network.add_message(msg2)
        else:
            for node in valid_receivers:
                if node.id != proposer.id:
                    msg = Message(
                        id=str(uuid.uuid4()),
                        sender_id=proposer.id,
                        receiver_id=node.id,
                        type="BLOCK_PROPOSAL",
                        payload={"block_id": "block_pos"},
                        timestamp=time.time()
                    )
                    network.add_message(msg)
                    
        return True
        
    def apply_mitigation(self, node) -> None:
        pass
