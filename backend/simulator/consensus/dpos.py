import random
import time
import uuid
from .base import BaseConsensus, is_eligible
from ..node import Message

class DPoSMechanism(BaseConsensus):
    name = "DPoS"
    
    def __init__(self):
        self.delegates = []
        self.delegate_index = 0
        
    def elect_delegates(self, network):
        valid_nodes = [n for n in network.nodes if is_eligible(n)]
        sorted_nodes = sorted(valid_nodes, key=lambda n: n.reputation, reverse=True)
        self.delegates = sorted_nodes[:5]
        self.delegate_index = 0
        
    async def execute_round(self, network) -> bool:
        if not self.delegates:
            self.elect_delegates(network)
            
        if not self.delegates:
            return False
            
        # re-elect if any delegate was removed, lost eligibility, or is being watched
        re_elect = False
        for d in self.delegates:
            if not any(d is n for n in network.nodes) or d.status == 'Watched' or not is_eligible(d):
                re_elect = True
                break
                
        if re_elect:
            self.elect_delegates(network)
            
        if not self.delegates:
            return False
            
        self.rejected_attempt(network, "BLOCK_PROPOSAL", "block_dpos_rejected")
        proposer = self.delegates[self.delegate_index % len(self.delegates)]
        self.delegate_index = (self.delegate_index + 1) % len(self.delegates)
        
        for node in network.nodes:
            if node.id != proposer.id:
                msg = Message(
                    id=str(uuid.uuid4()),
                    sender_id=proposer.id,
                    receiver_id=node.id,
                    type="BLOCK_PROPOSAL",
                    payload={"block_id": "block_dpos"},
                    timestamp=time.time()
                )
                network.add_message(msg)
                
        network.latest_blocks.append({
            "hash": "0x" + uuid.uuid4().hex[:8].upper(),
            "proposer": proposer.id,
            "tx_count": random.randint(10, 50),
            "consensus": "DPoS",
            "round": network.current_round,
            "is_rejected": False
        })
        
        return True
        
    def apply_mitigation(self, node) -> None:
        pass
