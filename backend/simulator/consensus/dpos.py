import time
import uuid
from .base import BaseConsensus
from ..node import Message

class DPoSMechanism(BaseConsensus):
    name = "DPoS"
    
    def __init__(self):
        self.delegates = []
        self.delegate_index = 0
        
    def elect_delegates(self, network):
        valid_nodes = [n for n in network.nodes if n.status != 'Quarantined']
        sorted_nodes = sorted(valid_nodes, key=lambda n: n.reputation, reverse=True)
        self.delegates = sorted_nodes[:5]
        self.delegate_index = 0
        
    async def execute_round(self, network) -> bool:
        if not self.delegates:
            self.elect_delegates(network)
            
        if not self.delegates:
            return False
            
        # check for watched delegates
        re_elect = False
        for d in self.delegates:
            if d.status == 'Watched':
                re_elect = True
                break
                
        if re_elect:
            self.elect_delegates(network)
            
        if not self.delegates:
            return False
            
        proposer = self.delegates[self.delegate_index]
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
                
        return True
        
    def apply_mitigation(self, node) -> None:
        pass
