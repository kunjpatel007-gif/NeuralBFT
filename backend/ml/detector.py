import numpy as np
import os
import pandas as pd
from sklearn.ensemble import RandomForestClassifier, IsolationForest

# Expanded to 11 features for Temporal Fusion
FEATURE_NAMES = [
    'msg_freq', 'vote_inconsistency', 'latency', 'fork_attempts', 'silence_ratio',
    'msg_freq_delta', 'latency_delta', 'vote_drift_delta', 'silence_delta',
    'freq_spike_ratio', 'lat_spike_ratio'
]

class ByzantineDetector:
    def __init__(self):
        self.csv_path = os.path.join(os.path.dirname(__file__), 'master_training_data.csv')
        
        # ── Models ──
        self.nn = RandomForestClassifier(
            n_estimators=100,
            max_depth=10,
            class_weight='balanced',
            random_state=42
        )
        
        self.iso = IsolationForest(
            n_estimators=100,
            contamination=0.15,
            random_state=42
        )
        
        self.df = None
        self._initialize_dataset()
        
    def _initialize_dataset(self):
        """Loads the CSV if it exists, otherwise bootstraps 500 fake examples and saves it."""
        if os.path.exists(self.csv_path):
            try:
                self.df = pd.read_csv(self.csv_path)
                print(f"🟢 SUCCESS: Loaded Master Dataset ({len(self.df)} rows).")
                self._retrain_all()
                return
            except Exception as e:
                print(f"🔴 ERROR: Failed to load CSV ({e}). Rebuilding factory defaults...")
        
        print("🟡 INFO: No Master Dataset found. Building factory baseline...")
        self._bootstrap_synthetic_dataset()
        
    def _bootstrap_synthetic_dataset(self):
        rng = np.random.RandomState(42)
        n_honest = 300
        n_byzantine = 200
        
        # ── Honest node profiles (Stable) ──
        honest = np.column_stack([
            rng.normal(50, 10, n_honest),          # msg_freq
            rng.uniform(0, 0.1, n_honest),          # vote_inconsistency
            rng.normal(100, 20, n_honest),           # latency
            rng.poisson(0.1, n_honest).astype(float),# fork_attempts
            rng.uniform(0, 0.05, n_honest),          # silence_ratio
            
            # Deltas are near 0 for stable honest nodes
            rng.normal(5, 5, n_honest),            # msg_freq_delta
            rng.normal(10, 5, n_honest),           # latency_delta
            rng.uniform(0, 0.05, n_honest),        # vote_drift_delta
            rng.uniform(0, 0.02, n_honest),        # silence_delta
            rng.normal(1.0, 0.1, n_honest),        # freq_spike_ratio
            rng.normal(1.0, 0.1, n_honest)         # lat_spike_ratio
        ])
        
        # ── Byzantine node profiles (Spiky / Erratic) ──
        byz = np.column_stack([
            np.where(rng.random(n_byzantine) > 0.5,
                     rng.normal(120, 20, n_byzantine),
                     rng.normal(5, 3, n_byzantine)),   # msg_freq
            rng.uniform(0.3, 0.9, n_byzantine),         # vote_inconsistency
            np.where(rng.random(n_byzantine) > 0.5,
                     rng.normal(500, 100, n_byzantine),
                     rng.normal(10, 5, n_byzantine)),   # latency
            rng.poisson(3, n_byzantine).astype(float),  # fork_attempts
            rng.uniform(0.2, 0.7, n_byzantine),         # silence_ratio
            
            # Deltas are HUGE for attackers (Temporal Spikes)
            rng.normal(70, 30, n_byzantine),       # msg_freq_delta (Sudden flood)
            rng.normal(400, 100, n_byzantine),     # latency_delta (Sudden lag)
            rng.uniform(0.3, 0.8, n_byzantine),    # vote_drift_delta (Sudden equivocation)
            rng.uniform(0.2, 0.6, n_byzantine),    # silence_delta
            rng.normal(2.5, 0.5, n_byzantine),     # freq_spike_ratio (Spiked 2.5x normal)
            rng.normal(5.0, 1.0, n_byzantine)      # lat_spike_ratio (Spiked 5x normal)
        ])
        
        X = np.clip(np.vstack([honest, byz]), 0, None)
        y = np.array([0]*n_honest + [1]*n_byzantine)
        
        # Shuffle
        idx = rng.permutation(len(X))
        X, y = X[idx], y[idx]
        
        # Create DataFrame
        data = pd.DataFrame(X, columns=FEATURE_NAMES)
        data['label'] = y
        self.df = data
        
        # Save to disk
        self._save_csv()
        self._retrain_all()
        
    def _save_csv(self):
        try:
            self.df.to_csv(self.csv_path, index=False)
        except Exception as e:
            print(f"🔴 ERROR: Failed to save dataset to disk: {e}")

    def _retrain_all(self):
        """Perform a full retrain of both models on the entire Master Dataset."""
        if self.df is None or len(self.df) == 0:
            return
            
        X = self.df[FEATURE_NAMES].values
        y = self.df['label'].values
        
        # Train Neural Network on all data
        self.nn.fit(X, y)
        
        # Train Isolation Forest on ONLY honest data (label == 0)
        honest_X = self.df[self.df['label'] == 0][FEATURE_NAMES].values
        if len(honest_X) > 10:
            self.iso.fit(honest_X)
            
        print(f"🧠 ML Models retrained on {len(self.df)} historical examples.")
        
    def evaluate(self, node_features: dict) -> dict:
        row = np.array([[node_features.get(f, 0.0) for f in FEATURE_NAMES]])
        
        # ── Neural Network probability ──
        try:
            probs = self.nn.predict_proba(row)
            nn_prob = float(probs[0, 1]) if probs.shape[1] > 1 else 0.5
        except Exception:
            nn_prob = 0.5
        
        # ── Isolation Forest anomaly score ──
        try:
            raw_score = self.iso.decision_function(row)[0]
            anomaly_score = float(np.clip(0.5 - raw_score, 0.0, 1.0))
        except Exception:
            anomaly_score = 0.0
        
        # ── Blend: Neural Net is Primary, Anomaly is Secondary ──
        # The Isolation Forest is extremely noisy on honest nodes. We dampen its output by 50%.
        # However, we still use max() so a 100% confident Neural Net detection hits with 100% power.
        combined = max(nn_prob, anomaly_score * 0.5)
        
        return {
            'nn_prob': round(nn_prob, 4),
            'anomaly_score': round(anomaly_score, 4),
            'combined_threat': round(combined, 4)
        }
    
    def train_online(self, features: dict, is_byzantine: int):
        """Append the new example to the Master Dataset and perform a full retrain.
        This guarantees the model perfectly balances factory baseline + custom feedback."""
        
        row_data = {f: features.get(f, 0.0) for f in FEATURE_NAMES}
        row_data['label'] = is_byzantine
        
        # Append to dataframe
        new_row = pd.DataFrame([row_data])
        self.df = pd.concat([self.df, new_row], ignore_index=True)
        
        # Save to disk instantly
        self._save_csv()
        
        # Retrain on the newly expanded dataset
        self._retrain_all()
        
        return len(self.df)
