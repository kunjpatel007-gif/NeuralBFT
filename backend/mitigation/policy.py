import sys
import os
import random
from collections import defaultdict
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
from ml.detector import ByzantineDetector

class AdaptiveThreshold:
    """Dynamically adjusts the baseline punishment threshold based on overall network health.
    Prevents mass false-positive punishments during global lag events."""
    def __init__(self, initial=0.40, alpha=0.05, min_t=0.40, max_t=0.75):
        self.threshold = initial
        self.alpha = alpha  # Smoothing factor
        self.min_t = min_t
        self.max_t = max_t

    def update(self, all_threat_scores: list[float]):
        if not all_threat_scores:
            return
        network_mean = sum(all_threat_scores) / len(all_threat_scores)
        # Adapt: if network is globally suspicious, raise the bar
        self.threshold = (1 - self.alpha) * self.threshold + self.alpha * network_mean
        self.threshold = max(self.min_t, min(self.max_t, self.threshold))

    @property
    def value(self) -> float:
        return self.threshold

class ReputationManager:
    def __init__(self, detector: ByzantineDetector):
        self.detector = detector
        self.state: dict[str, dict] = {}
        
        # ── Temporal History ──
        self.history: dict[str, list[dict]] = defaultdict(list)
        self.HISTORY_WINDOW = 5
        
        # ── Beta Reputation Shield ──
        self.adaptive_threshold = AdaptiveThreshold()
        
    def _compute_deltas(self, node_id: str, current_features: dict) -> dict:
        history = self.history[node_id]
        enriched = dict(current_features)
        
        if len(history) >= 2:
            avg_freq = sum(h.get('msg_freq', 50) for h in history) / len(history)
            avg_lat  = sum(h.get('latency', 100) for h in history) / len(history)
            avg_vi   = sum(h.get('vote_inconsistency', 0) for h in history) / len(history)
            avg_sr   = sum(h.get('silence_ratio', 0) for h in history) / len(history)
            
            enriched['msg_freq_delta']    = abs(current_features.get('msg_freq', 50) - avg_freq)
            enriched['latency_delta']     = abs(current_features.get('latency', 100) - avg_lat)
            enriched['vote_drift_delta']  = abs(current_features.get('vote_inconsistency', 0) - avg_vi)
            enriched['silence_delta']     = abs(current_features.get('silence_ratio', 0) - avg_sr)
            enriched['freq_spike_ratio']  = current_features.get('msg_freq', 50) / max(avg_freq, 1.0)
            enriched['lat_spike_ratio']   = current_features.get('latency', 100) / max(avg_lat, 1.0)
        else:
            enriched['msg_freq_delta'] = 0.0
            enriched['latency_delta'] = 0.0
            enriched['vote_drift_delta'] = 0.0
            enriched['silence_delta'] = 0.0
            enriched['freq_spike_ratio'] = 1.0
            enriched['lat_spike_ratio'] = 1.0
            
        return enriched
        
    def evaluate_round(self, nodes: list, round_features: dict[str, dict]) -> None:
        
        smoothed_threats = []
        node_evals = {}
        
        # ── Step 1: Compute threats for all nodes ──
        for node in nodes:
            node_id = getattr(node, 'id', str(id(node)))
            raw_features = round_features.get(node_id, {})
            
            enriched_features = self._compute_deltas(node_id, raw_features)
            
            self.history[node_id].append(dict(raw_features))
            if len(self.history[node_id]) > self.HISTORY_WINDOW:
                self.history[node_id].pop(0)
            
            result = self.detector.evaluate(enriched_features)
            nn_prob       = result['nn_prob']
            anomaly_score = result['anomaly_score']
            combined      = result['combined_threat']
            final_threat  = max(0.0, min(1.0, combined + random.uniform(-0.02, 0.02)))
            
            if not hasattr(node, 'ml_prob'):
                node.ml_prob = 0.0
                
            if not hasattr(node, 'alpha'): node.alpha = 20.0
            if not hasattr(node, 'beta'): node.beta = 1.0
            # Dynamic Trust EMA
            trust = node.alpha / (node.alpha + node.beta)
            
            # Trust Shield Bypass: Reputation protects against noise, not blatant attacks
            if final_threat > 0.7:
                trust = min(trust, 0.4)
                
            smoothed = (node.ml_prob * trust) + (final_threat * (1.0 - trust))
            smoothed_threats.append(smoothed)
            
            node_evals[node.id] = (smoothed, enriched_features, {
                'nn_prob': nn_prob,
                'anomaly_score': anomaly_score,
                'combined': combined,
                'final_threat': round(final_threat, 4)
            })

        # ── Step 2: Adjust Network Baseline Threshold ──
        self.adaptive_threshold.update(smoothed_threats)
        current_threshold = self.adaptive_threshold.value
        
        # ── Step 3: Bayesian Beta Reputation Update ──
        for node in nodes:
            smoothed, feats, eval_dict = node_evals[node.id]
            
            # Ensure counters exist (they start at 20.0 / 1.0 via node.py)
            if not hasattr(node, 'alpha'): node.alpha = 20.0
            if not hasattr(node, 'beta'): node.beta = 1.0
            
            # Decay old behavior so reformed nodes can heal faster
            node.alpha = max(1.0, node.alpha * 0.98)
            node.beta = max(1.0, node.beta * 0.98)
            
            # Bayesian update based on dynamic threshold
            if smoothed >= current_threshold:
                import math
                excess = smoothed - current_threshold
                
                # 1. Non-linear Beta Penalty anchored to EXCESS threat
                # If excess is tiny (0.05), penalty is ~1.5 (Gentle)
                # If excess is massive (0.60), penalty is ~112.0 (Critical Damage)
                k_exp = 8.0
                beta_increment = 1.0 + (math.exp(k_exp * excess) - 1.0)
                node.beta += beta_increment
                
                # 2. Adaptive Alpha Slashing anchored to EXCESS threat
                # If excess is tiny, retention is 99.9%. Good nodes keep their history!
                # If excess is massive, retention is 5%. Hackers burn to the ground.
                excess_ratio = min(1.0, excess / 0.6)
                p_slash = 3.0
                alpha_retention = 1.0 - (excess_ratio ** p_slash)
                node.alpha = node.alpha * max(alpha_retention, 0.05)
            else:
                # Honest round: Beta-Weighted Trust Recovery
                # If they have a massive criminal record (high beta), earning trust is extremely difficult.
                # Honest nodes (beta ≈ 1) earn 1.0 alpha. Hackers (beta = 100) earn 0.1 alpha.
                import math
                trust_reward = 1.0 / max(1.0, math.sqrt(node.beta))
                node.alpha += trust_reward
                
            # Convert Beta probabilistic counters to 0-100 float for the UI and network rules
            node.reputation = (node.alpha / (node.alpha + node.beta)) * 100.0
            
            # Write data to node
            node.ml_prob = smoothed
            node.ml_features = feats
            node.ml_detail = eval_dict
            
            if hasattr(node, 'update_status'):
                try:
                    node.update_status()
                except Exception:
                    pass
            
            status = getattr(node, 'status', 'unknown')
            self.state[node.id] = {
                'reputation': node.reputation,
                'status': status,
                'byzantine_prob': smoothed,
                'nn_prob': eval_dict['nn_prob'],
                'anomaly_score': eval_dict['anomaly_score']
            }
            
    def get_summary(self) -> dict[str, dict]:
        return self.state
