import random
import time
import uuid
from .base import BaseConsensus
from ..node import Message

class PBFTMechanism(BaseConsensus):
    name = "PBFT"
    
    async def execute_round(self, network) -> bool:
        valid_nodes = [n for n in network.nodes if n.status != 'Quarantined']
        n_count = len(valid_nodes)
        
        if n_count < 4:
            return False
            
        # f = (n - 1) // 3
        # In this simplified simulation we will just have the leader broadcast PRE_PREPARE,
        # then everyone broadcasts PREPARE, then everyone broadcasts COMMIT.
        
        leader = valid_nodes[0]
        
        # Phase 1: Pre-prepare
        for node in network.nodes:
            if node.id != leader.id:
                if leader.is_byzantine and random.random() < 0.3:
                    continue # delay/silence
                msg = Message(
                    id=str(uuid.uuid4()),
                    sender_id=leader.id,
                    receiver_id=node.id,
                    type="PRE_PREPARE",
                    payload={"block_id": "block_pbft"},
                    timestamp=time.time()
                )
                network.add_message(msg)
                
        # Phase 2: Prepare
        for sender in valid_nodes:
            if sender.id == leader.id:
                continue
            for receiver in network.nodes:
                if sender.id != receiver.id:
                    msg = Message(
                        id=str(uuid.uuid4()),
                        sender_id=sender.id,
                        receiver_id=receiver.id,
                        type="PREPARE",
                        payload={"block_id": "block_pbft"},
                        timestamp=time.time()
                    )
                    network.add_message(msg)
                    
        # Phase 3: Commit
        for sender in valid_nodes:
            for receiver in network.nodes:
                if sender.id != receiver.id:
                    msg = Message(
                        id=str(uuid.uuid4()),
                        sender_id=sender.id,
                        receiver_id=receiver.id,
                        type="COMMIT",
                        payload={"block_id": "block_pbft"},
                        timestamp=time.time()
                    )
                    network.add_message(msg)
                    
        # Check if the leader's partition has enough nodes to reach 2f+1 consensus
        n_total = len(network.nodes)
        f = (n_total - 1) // 3
        required_votes = 2 * f + 1
        partition_size = sum(1 for n in valid_nodes if n.partition_id == leader.partition_id)
        
        if partition_size < required_votes:
            return False
            
        # Consensus reached! Produce a block
        block_hash = "0x" + uuid.uuid4().hex[:8].upper()
        network.latest_blocks.append({
            "hash": block_hash,
            "proposer": leader.id,
            "tx_count": random.randint(10, 50),
            "consensus": "PBFT"
        })
        
        return True
        
    def apply_mitigation(self, node) -> None:
        pass
