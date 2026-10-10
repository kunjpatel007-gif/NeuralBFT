import asyncio
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from simulator.node import Node
from simulator.network import NetworkManager
from simulator.consensus.pow import PoWMechanism
from simulator.consensus.pos import PoSMechanism
from simulator.consensus.dpos import DPoSMechanism
from simulator.consensus.pbft import PBFTMechanism
from ml.detector import ByzantineDetector
from mitigation.policy import ReputationManager
from server.api import StateServer

import logging
import random as _rand

logging.basicConfig(level=logging.INFO)

def assign_network_profile(node):
    profiles = [
        "Datacenter / LAN", "Fiber Optic", "Standard Wi-Fi", 
        "Satellite", "Public Cafe Wi-Fi", "Throttled 3G", "Faulty Router"
    ]
    weights = [0.10, 0.20, 0.30, 0.15, 0.15, 0.05, 0.05]
    node.network_profile = _rand.choices(profiles, weights=weights, k=1)[0]
    node.lag_timer = 0

def generate_honest_telemetry(node):
    prof = node.network_profile
    
    # Check if a new lag spike hits based on network quality
    spike_chance = {
        "Datacenter / LAN": 0.005,
        "Fiber Optic": 0.02,
        "Standard Wi-Fi": 0.05,
        "Satellite": 0.02,
        "Public Cafe Wi-Fi": 0.15,
        "Throttled 3G": 0.05,
        "Faulty Router": 0.20
    }.get(prof, 0.03)
    
    if node.lag_timer == 0 and _rand.random() < spike_chance:
        # Wildly randomized lag spike duration (1 to 6 rounds)
        node.lag_timer = _rand.randint(1, 6)
        
    if node.lag_timer > 0:
        node.lag_timer -= 1
        # ── LAG SPIKE CHAOS ──
        return {
            "msg_freq": _rand.gauss(20, 15),                 # Massive drop in throughput
            "vote_inconsistency": _rand.uniform(0, 0.15),    # Minor packet corruption
            "latency": _rand.gauss(800, 300),                # Huge ping spike
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0.1, 0.4),        # Dropping a lot of packets
        }
        
    # ── BASELINE PROFILES ──
    if prof == "Datacenter / LAN":
        return {
            "msg_freq": _rand.gauss(50, 1),
            "vote_inconsistency": _rand.uniform(0, 0.01),
            "latency": _rand.gauss(10, 2),
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0, 0.01),
        }
    elif prof == "Fiber Optic":
        return {
            "msg_freq": _rand.gauss(50, 5),
            "vote_inconsistency": _rand.uniform(0, 0.03),
            "latency": _rand.gauss(30, 5),
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0, 0.02),
        }
    elif prof == "Standard Wi-Fi":
        return {
            "msg_freq": _rand.gauss(50, 10),
            "vote_inconsistency": _rand.uniform(0, 0.1),
            "latency": _rand.gauss(100, 20),
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0, 0.05),
        }
    elif prof == "Satellite":
        return {
            "msg_freq": _rand.gauss(50, 5),
            "vote_inconsistency": _rand.uniform(0, 0.1),
            "latency": _rand.gauss(800, 50), # Consistent but massive delay
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0, 0.05),
        }
    elif prof == "Public Cafe Wi-Fi":
        return {
            "msg_freq": _rand.gauss(40, 20), # Highly erratic throughput
            "vote_inconsistency": _rand.uniform(0, 0.15),
            "latency": _rand.uniform(50, 600), # Swings wildly every round
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0.05, 0.15),
        }
    elif prof == "Throttled 3G":
        return {
            "msg_freq": _rand.gauss(15, 5), # Barely sending messages
            "vote_inconsistency": _rand.uniform(0, 0.2),
            "latency": _rand.gauss(1200, 200),
            "fork_attempts": 0,
            "silence_ratio": _rand.uniform(0.1, 0.3),
        }
    elif prof == "Faulty Router":
        # Disconnects randomly
        is_disconnected = _rand.random() < 0.3
        return {
            "msg_freq": 0 if is_disconnected else _rand.gauss(50, 10),
            "vote_inconsistency": _rand.uniform(0, 0.1),
            "latency": 5000 if is_disconnected else _rand.gauss(100, 20),
            "fork_attempts": 0,
            "silence_ratio": 0.8 if is_disconnected else _rand.uniform(0, 0.05),
        }
    
    # Fallback
    return {
        "msg_freq": _rand.gauss(50, 10),
        "vote_inconsistency": _rand.uniform(0, 0.1),
        "latency": _rand.gauss(100, 20),
        "fork_attempts": 0,
        "silence_ratio": _rand.uniform(0, 0.05),
    }

async def main():
    # 1. Initialize 10 nodes (Start with 0 byzantine so user has a clean slate)
    nodes = []
    for i in range(10):
        n = Node(id=f"node_{i}", is_byzantine=False, reputation=100.0, status="Trusted")
        assign_network_profile(n)
        nodes.append(n)

    # 2. Create NetworkManager with nodes
    network = NetworkManager()
    network.nodes = nodes
    network.messages_in_flight = []
    network.current_round = 0
    
    # 3. Initialize ByzantineDetector (Uses CSV Master Dataset now)
    detector = ByzantineDetector()
    
    # 4. Initialize ReputationManager with detector
    reputation_manager = ReputationManager(detector=detector)
    
    # 5. Set initial consensus to PBFT
    consensus_map = {
        'PoW': PoWMechanism,
        'PoS': PoSMechanism,
        'DPoS': DPoSMechanism,
        'PBFT': PBFTMechanism
    }
    network.active_consensus = PBFTMechanism()
    
    # 6. Start StateServer
    port = int(os.environ.get("PORT", 8765))
    server = StateServer(host='0.0.0.0', port=port)
    await server.start()
    logging.info(f"WebSocket server started on ws://0.0.0.0:{port}")
    
    
    # 7. Main loop:
    try:
        while True:
            # a. Check for pending commands
            while server.pending_commands:
                cmd = server.pending_commands.pop(0)
                action = cmd.get("action")
                if action == "inject_fault":
                    node_id = cmd.get("node_id")
                    fault_type = cmd.get("fault_type")
                    if node_id and fault_type:
                        network.inject_fault(node_id, fault_type)
                        logging.info(f"Injected fault {fault_type} on {node_id}")
                        
                        # --- Broadcast Shockwave to Unity ---
                        if server.clients:
                            target_node = next((n for n in network.nodes if n.id == node_id), None)
                            if target_node and hasattr(target_node, 'ml_features') and target_node.ml_features:
                                path_data = detector.get_decision_path(target_node.ml_features)["path"]
                                shockwave_msg = {
                                    "type": "injection_shockwave",
                                    "node_id": node_id,
                                    "fault_type": fault_type,
                                    "path": path_data
                                }
                                await server.broadcast_state(shockwave_msg)  # non-blocking broadcast
                elif action == "switch_consensus":
                    consensus_name = cmd.get("consensus")
                    if consensus_name in consensus_map:
                        network.active_consensus = consensus_map[consensus_name]()
                        logging.info(f"Switched consensus to {consensus_name}")
                elif action == "split_network":
                    # Split the nodes into two partitions
                    for idx, n in enumerate(network.nodes):
                        n.partition_id = 0 if idx < len(network.nodes)//2 else 1
                    logging.info("Network split into 2 partitions")
                elif action == "merge_network":
                    for n in network.nodes:
                        n.partition_id = 0
                    logging.info("Network partitions merged")
                elif action == "add_node":
                    if len(network.nodes) < 50:
                        new_id = f"node_{len(network.nodes)}"
                        new_node = Node(id=new_id, is_byzantine=False, reputation=100.0, status="Trusted")
                        assign_network_profile(new_node)
                        network.nodes.append(new_node)
                        logging.info(f"Added new node {new_id} ({new_node.network_profile})")
                elif action == "remove_node":
                    if len(network.nodes) > 4:
                        removed = network.nodes.pop()
                        # drop stale per-node state so a reused id doesn't inherit it
                        reputation_manager.history.pop(removed.id, None)
                        reputation_manager.state.pop(removed.id, None)
                        logging.info(f"Removed node {removed.id}")
                elif action == 'sybil_swarm':
                    count = min(cmd.get('count', 5), 10)
                    for i in range(count):
                        if len(network.nodes) < 50:
                            new_id = f'node_{len(network.nodes)}'
                            new_node = Node(id=new_id, is_byzantine=True, reputation=100.0, status='Trusted')
                            new_node.spawn_round = network.current_round
                            assign_network_profile(new_node)
                            setattr(new_node, 'fault_type', 'spam')
                            network.nodes.append(new_node)
                    logging.info(f'Sybil swarm: injected {count} malicious nodes')
                elif action == 'get_history_snapshot':
                    target_round = cmd.get('round')
                    client = cmd.get('_client')
                    if target_round is not None and client is not None:
                        snapshot = next(
                            (s for s in server.history if s.get('round') == target_round), None
                        )
                        if snapshot:
                            await server.send_to_client(client, {'type': 'history_snapshot', 'data': snapshot})
                elif action == "report_false_positive":
                    node_id = cmd.get("node_id")
                    node = next((n for n in network.nodes if n.id == node_id), None)
                    if node:
                        # Build a safe honest feature vector even if ml_features is missing
                        heal_features = dict(node.ml_features) if (hasattr(node, 'ml_features') and node.ml_features) else {}
                        heal_features['invalid_hash_rate'] = 0.0
                        age = network.current_round - getattr(node, 'spawn_round', 0)
                        heal_features['identity_age_score'] = min(1.0, age / 100.0)
                        heal_features.setdefault('msg_freq', 50.0)
                        heal_features.setdefault('vote_inconsistency', 0.05)
                        heal_features.setdefault('latency', 100.0)
                        heal_features.setdefault('fork_attempts', 0)
                        heal_features.setdefault('silence_ratio', 0.02)
                        heal_features.setdefault('msg_freq_delta', 0.0)
                        heal_features.setdefault('latency_delta', 0.0)
                        heal_features.setdefault('vote_drift_delta', 0.0)
                        heal_features.setdefault('silence_delta', 0.0)
                        heal_features.setdefault('freq_spike_ratio', 1.0)
                        heal_features.setdefault('lat_spike_ratio', 1.0)

                        count = await asyncio.to_thread(detector.train_online, heal_features, 0)  # retrain off the event loop

                        # Instant Bayesian Heal: Hard-reset alpha/beta to a clean Trusted baseline.
                        # Gentle increments (alpha+=15) are useless against a beta=100-200 criminal record.
                        node.alpha = 20.0
                        node.beta = 1.0
                        node.reputation = (node.alpha / (node.alpha + node.beta)) * 100.0
                        node.ml_prob = 0.0
                        node.ml_features = heal_features
                        # Also flush the temporal history so the next round doesn't re-punish from stale data
                        if hasattr(reputation_manager, 'history') and node_id in reputation_manager.history:
                            reputation_manager.history[node_id].clear()
                        if hasattr(node, 'update_status'):
                            node.update_status()
                        logging.info(f"Online training: {node_id} marked as FALSE POSITIVE — hard healed to Trusted (train #{count})")
                elif action == "report_missed_attack":
                    node_id = cmd.get("node_id")
                    node = next((n for n in network.nodes if n.id == node_id), None)
                    if node:
                        # Build a safe attack feature vector even if ml_features is missing
                        punish_features = dict(node.ml_features) if (hasattr(node, 'ml_features') and node.ml_features) else {}
                        punish_features['invalid_hash_rate'] = 0.8
                        age = network.current_round - getattr(node, 'spawn_round', 0)
                        punish_features['identity_age_score'] = min(1.0, age / 100.0)
                        punish_features.setdefault('msg_freq', 150.0)
                        punish_features.setdefault('vote_inconsistency', 0.8)
                        punish_features.setdefault('latency', 500.0)
                        punish_features.setdefault('fork_attempts', 3)
                        punish_features.setdefault('silence_ratio', 0.5)
                        punish_features.setdefault('msg_freq_delta', 50.0)
                        punish_features.setdefault('latency_delta', 300.0)
                        punish_features.setdefault('vote_drift_delta', 0.6)
                        punish_features.setdefault('silence_delta', 0.4)
                        punish_features.setdefault('freq_spike_ratio', 3.0)
                        punish_features.setdefault('lat_spike_ratio', 5.0)

                        count = await asyncio.to_thread(detector.train_online, punish_features, 1)  # retrain off the event loop
                        
                        # Instant Bayesian Punish: Hard-reset to Quarantined/Blacklisted.
                        # Gentle increments (beta+=20) are useless if a node built up alpha=100.
                        node.alpha = 1.0
                        node.beta = 100.0
                        node.reputation = (node.alpha / (node.alpha + node.beta)) * 100.0
                        node.ml_prob = 1.0
                        node.ml_features = punish_features
                        # Flush history so past good behavior doesn't save them
                        if hasattr(reputation_manager, 'history') and node_id in reputation_manager.history:
                            reputation_manager.history[node_id].clear()
                        if hasattr(node, 'update_status'):
                            node.update_status()
                        logging.info(f"Online training: {node_id} marked as MISSED ATTACK — hard punished to Quarantined (train #{count})")
            
            # b. Run one consensus round
            round_stats = await network.run_round()
            logging.info(f"Round {network.current_round} completed.")
            
            # c. Collect node features from the round
            round_features = {}
            for node in network.nodes:
                if node.is_byzantine:
                    fault_type = getattr(node, "fault_type", "spam")
                    
                    if fault_type == "stealth":
                        if node.reputation < 55:
                            round_features[node.id] = generate_honest_telemetry(node)
                        else:
                            round_features[node.id] = {
                                "msg_freq": _rand.gauss(140, 20),
                                "vote_inconsistency": _rand.uniform(0.5, 0.9),
                                "latency": _rand.gauss(300, 100),
                                "fork_attempts": max(0, int(_rand.gauss(3, 1.5))),
                                "silence_ratio": _rand.uniform(0.1, 0.5),
                            }
                    elif fault_type == "state_tampering":
                        # behaviourally normal; only invalid_hash_rate (added below) gives it away
                        round_features[node.id] = generate_honest_telemetry(node)
                    else:
                        round_features[node.id] = {
                            "msg_freq": _rand.gauss(120, 20),
                            "vote_inconsistency": _rand.uniform(0.3, 0.9),
                            "latency": _rand.gauss(500, 100),
                            "fork_attempts": max(0, int(_rand.gauss(3, 1.5))),
                            "silence_ratio": _rand.uniform(0.2, 0.7),
                        }
                else:
                    # Honest nodes now use their specific chaos profiles!
                    round_features[node.id] = generate_honest_telemetry(node)
            
            # Enrich features with new ML columns
            for node_id, features in round_features.items():
                node_obj = next((n for n in network.nodes if n.id == node_id), None)
                if node_obj:
                    features['invalid_hash_rate'] = 0.0
                    if hasattr(node_obj, 'fault_type') and getattr(node_obj, 'fault_type', '') == 'state_tampering' and node_obj.is_byzantine:
                        features['invalid_hash_rate'] = _rand.uniform(0.5, 1.0)
                    age = network.current_round - getattr(node_obj, 'spawn_round', 0)
                    features['identity_age_score'] = min(1.0, age / 100.0)

            # d. Run ReputationManager.evaluate_round()
            reputation_manager.evaluate_round(network.nodes, round_features)
            
            # e. Broadcast state via StateServer
            state = network.get_state()
            await server.broadcast_state(state)
            
            # f. Broadcast ML Random Forest Diagnostics
            if server.clients:
                ml_payload = {
                    "type": "telemetry_update",
                    "tree_structure": detector.export_tree_structure()["nodes"],
                    "node_paths": {},
                    "node_flags": {n.id: bool(n.is_byzantine) for n in network.nodes}
                }
                for node in network.nodes:
                    if hasattr(node, "ml_features") and node.ml_features:
                        ml_payload["node_paths"][node.id] = detector.get_decision_path(node.ml_features)["path"]
                
                await server.broadcast_state(ml_payload)

            # g. Sleep 2 seconds between rounds
            await asyncio.sleep(2)
            
            # h. Clear messages_in_flight for next round
            network.messages_in_flight.clear()
            
    except asyncio.CancelledError:
        logging.info("Main loop cancelled.")
    except KeyboardInterrupt:
        logging.info("Keyboard interrupt received. Shutting down.")

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass


