import numpy as np
import os
import pandas as pd
from sklearn.ensemble import RandomForestClassifier, IsolationForest


def _gcs_enabled() -> bool:
    """Cloud Storage sync is for the deployed backend only. It is on by default on
    Cloud Run (which always sets K_SERVICE) and off everywhere else, so a local run
    never contacts Google and only ever reads the local CSV. USE_GCS=1 or USE_GCS=0
    overrides the default either way."""
    flag = os.environ.get("USE_GCS")
    if flag is not None:
        return flag.strip().lower() in ("1", "true", "yes", "on")
    return bool(os.environ.get("K_SERVICE"))

# 13 features: 5 base + 6 temporal deltas/ratios + invalid_hash_rate + identity_age_score
FEATURE_NAMES = [
    'msg_freq', 'vote_inconsistency', 'latency', 'fork_attempts', 'silence_ratio',
    'msg_freq_delta', 'latency_delta', 'vote_drift_delta', 'silence_delta',
    'freq_spike_ratio', 'lat_spike_ratio',
    'invalid_hash_rate',
    'identity_age_score'
]

class ByzantineDetector:
    def __init__(self):
        self.csv_path = os.path.join(os.path.dirname(__file__), 'master_training_data_ORGANIC.csv')
        self.bucket_name = "neuralbft-ml-data-2026"
        
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
        self._init_gcs()
        self._initialize_dataset()
        
    def _init_gcs(self):
        self.storage_client = None
        self.bucket = None
        if not _gcs_enabled():
            print("GCS disabled (local mode) - using the local CSV only.")
            return

        import time
        from google.cloud import storage
        key_path = os.path.join(os.path.dirname(__file__), '..', 'gcp-key.json')
        max_attempts = 3 if not os.path.exists(key_path) else 1
        for attempt in range(1, max_attempts + 1):
            try:
                if os.path.exists(key_path):
                    from google.oauth2 import service_account
                    creds = service_account.Credentials.from_service_account_file(key_path)
                    self.storage_client = storage.Client(credentials=creds)
                    print("GCS initialized with local gcp-key.json")
                else:
                    self.storage_client = storage.Client()
                    print("GCS initialized with Cloud Run default identity")
                self.bucket = self.storage_client.bucket(self.bucket_name)
                blob = self.bucket.blob('master_training_data_ORGANIC.csv')
                if blob.exists():
                    self._backup_local_csv_if_different(blob)
                    blob.download_to_filename(self.csv_path)
                    print(f"GCS SUCCESS: Downloaded CSV (attempt {attempt})")
                else:
                    if os.path.exists(self.csv_path):
                        blob.upload_from_filename(self.csv_path)
                        print("GCS INITIALIZED: Bucket empty, uploaded baseline CSV")
                    else:
                        print("WARNING: Bucket empty and no local CSV. Will bootstrap synthetic.")
                return
            except Exception as e:
                print(f"GCS attempt {attempt}/{max_attempts} failed: {e}")
                if attempt < max_attempts:
                    time.sleep(3)
                else:
                    print("GCS permanently unavailable - running local-only mode.")
                    self.storage_client = None
                    self.bucket = None
    def _backup_local_csv_if_different(self, blob):
        """The bucket copy overwrites the local CSV on startup. Keep a timestamped
        local copy first so hand-curated training data can never be lost."""
        try:
            if not os.path.exists(self.csv_path):
                return
            import base64, hashlib, shutil, time
            blob.reload()
            with open(self.csv_path, 'rb') as fh:
                local_md5 = base64.b64encode(hashlib.md5(fh.read()).digest()).decode()
            if local_md5 != blob.md5_hash:
                stamp = time.strftime('%Y%m%d-%H%M%S')
                dst = self.csv_path[:-4] + f'.local-backup-{stamp}.csv'
                shutil.copy2(self.csv_path, dst)
                print(f"GCS: local CSV differs from bucket copy; backed up to {os.path.basename(dst)}")
        except Exception as e:
            print(f"GCS: could not back up local CSV before download: {e}")

    def _initialize_dataset(self):
        """Loads the CSV if it exists. If it has fewer columns than expected (old schema),
        auto-migrates it by adding new feature columns with sensible defaults.
        Only bootstraps synthetic data if no CSV exists at all."""
        
        # Also check for the backup file from migration
        backup_path = os.path.join(os.path.dirname(__file__), 'master_training_data_v2_backup.csv')
        
        if os.path.exists(self.csv_path):
            try:
                self.df = pd.read_csv(self.csv_path)
                self.df = self._migrate_schema(self.df)
                print(f"🟢 SUCCESS: Loaded Master Dataset ({len(self.df)} rows, {len(self.df.columns)} cols).")
                self._retrain_all()
                return
            except Exception as e:
                print(f"🔴 ERROR: Failed to load CSV ({e}). Checking backup...")
        
        # Try the backup (pre-migration rename)
        if os.path.exists(backup_path):
            try:
                self.df = pd.read_csv(backup_path)
                print(f"🟡 INFO: Found backup dataset ({len(self.df)} rows). Migrating schema...")
                self.df = self._migrate_schema(self.df)
                self._save_csv()  # Save as the new master
                print(f"🟢 SUCCESS: Migrated to {len(self.df.columns)} columns. Saved as new master.")
                self._retrain_all()
                return
            except Exception as e:
                print(f"🔴 ERROR: Failed to load backup ({e}). Building factory defaults...")
        
        print("🟡 INFO: No Master Dataset found. Building factory baseline...")
        self._bootstrap_synthetic_dataset()
    
    def _migrate_schema(self, df: pd.DataFrame) -> pd.DataFrame:
        """Adds missing feature columns to an old-schema DataFrame with historically accurate defaults,
        then appends a small synthetic supplement so the model has examples of the new attack types."""
        rng = np.random.RandomState(99)
        n = len(df)
        
        needs_supplement = False
        
        for feat in FEATURE_NAMES:
            if feat not in df.columns:
                needs_supplement = True
                if feat == 'invalid_hash_rate':
                    # ACCURATE: None of the old attacks involved state tampering.
                    # ALL old rows (honest AND byzantine) get 0.0.
                    df[feat] = 0.0
                    print(f"  ➕ Added '{feat}' = 0.0 for all {n} old rows (no old attacks used tampering)")
                elif feat == 'identity_age_score':
                    # ACCURATE: All old nodes existed from round 0. They are established.
                    # Give them all high age scores (0.7-1.0) with slight jitter.
                    df[feat] = rng.uniform(0.7, 1.0, n)
                    print(f"  ➕ Added '{feat}' = 0.7-1.0 for all {n} old rows (all were established nodes)")
                else:
                    df[feat] = 0.0
                    print(f"  ➕ Added '{feat}' = 0.0 (default)")
        
        # Ensure column order matches FEATURE_NAMES + label
        expected_cols = FEATURE_NAMES + ['label']
        df = df[[c for c in expected_cols if c in df.columns]]
        
        # Append a synthetic supplement with actual examples of the NEW attack types
        # so the model has SOMETHING to learn from before online training kicks in
        if needs_supplement:
            df = self._append_new_attack_examples(df, rng)
        
        return df
    
    def _append_new_attack_examples(self, df: pd.DataFrame, rng) -> pd.DataFrame:
        """Appends ~200 synthetic rows specifically for state tampering and Sybil attacks
        so the model can detect them before online training provides real examples."""
        
        n_tamp = 100   # State tampering attackers
        n_sybil = 100  # Sybil swarm nodes
        n_honest_new = 100  # Honest counterexamples with the new features
        
        # ── State Tampering Attackers ──
        # Behaviorally NORMAL (that's the whole point — ML can't catch them by behavior alone)
        # But invalid_hash_rate is high
        tamperers = pd.DataFrame({
            'msg_freq':            rng.normal(50, 10, n_tamp),
            'vote_inconsistency':  rng.uniform(0, 0.1, n_tamp),     # Looks honest!
            'latency':             rng.normal(100, 20, n_tamp),      # Looks honest!
            'fork_attempts':       rng.poisson(0.1, n_tamp).astype(float),
            'silence_ratio':       rng.uniform(0, 0.05, n_tamp),
            'msg_freq_delta':      rng.normal(5, 5, n_tamp),
            'latency_delta':       rng.normal(10, 5, n_tamp),
            'vote_drift_delta':    rng.uniform(0, 0.05, n_tamp),
            'silence_delta':       rng.uniform(0, 0.02, n_tamp),
            'freq_spike_ratio':    rng.normal(1.0, 0.1, n_tamp),
            'lat_spike_ratio':     rng.normal(1.0, 0.1, n_tamp),
            'invalid_hash_rate':   rng.uniform(0.5, 1.0, n_tamp),   # THE SIGNAL
            'identity_age_score':  rng.uniform(0.5, 1.0, n_tamp),   # Established nodes
            'label': 1
        })
        
        # ── Sybil Swarm Nodes ──
        # Behaviorally aggressive (spam) AND brand new (low age)
        sybils = pd.DataFrame({
            'msg_freq':            rng.normal(120, 20, n_sybil),
            'vote_inconsistency':  rng.uniform(0.3, 0.9, n_sybil),
            'latency':             rng.normal(50, 15, n_sybil),       # Fast (local bots)
            'fork_attempts':       rng.poisson(2, n_sybil).astype(float),
            'silence_ratio':       rng.uniform(0.1, 0.4, n_sybil),
            'msg_freq_delta':      rng.normal(70, 30, n_sybil),
            'latency_delta':       rng.normal(30, 10, n_sybil),
            'vote_drift_delta':    rng.uniform(0.3, 0.8, n_sybil),
            'silence_delta':       rng.uniform(0.1, 0.4, n_sybil),
            'freq_spike_ratio':    rng.normal(2.5, 0.5, n_sybil),
            'lat_spike_ratio':     rng.normal(0.5, 0.2, n_sybil),
            'invalid_hash_rate':   rng.uniform(0, 0.05, n_sybil),    # Not tampering
            'identity_age_score':  rng.uniform(0, 0.05, n_sybil),    # THE SIGNAL: brand new
            'label': 1
        })
        
        # ── Honest counterexamples (established, clean) ──
        honest_new = pd.DataFrame({
            'msg_freq':            rng.normal(50, 10, n_honest_new),
            'vote_inconsistency':  rng.uniform(0, 0.1, n_honest_new),
            'latency':             rng.normal(100, 20, n_honest_new),
            'fork_attempts':       rng.poisson(0.1, n_honest_new).astype(float),
            'silence_ratio':       rng.uniform(0, 0.05, n_honest_new),
            'msg_freq_delta':      rng.normal(5, 5, n_honest_new),
            'latency_delta':       rng.normal(10, 5, n_honest_new),
            'vote_drift_delta':    rng.uniform(0, 0.05, n_honest_new),
            'silence_delta':       rng.uniform(0, 0.02, n_honest_new),
            'freq_spike_ratio':    rng.normal(1.0, 0.1, n_honest_new),
            'lat_spike_ratio':     rng.normal(1.0, 0.1, n_honest_new),
            'invalid_hash_rate':   0.0,
            'identity_age_score':  rng.uniform(0.6, 1.0, n_honest_new),
            'label': 0
        })
        
        combined = pd.concat([df, tamperers, sybils, honest_new], ignore_index=True)
        print(f"  📊 Appended 300 synthetic rows (100 tamperers + 100 sybils + 100 honest)")
        print(f"  📊 Total dataset: {len(combined)} rows")
        return combined
        
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
            rng.normal(1.0, 0.1, n_honest),        # lat_spike_ratio
            rng.uniform(0, 0.01, n_honest),        # invalid_hash_rate
            rng.uniform(0.5, 1.0, n_honest)        # identity_age_score
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
            rng.normal(5.0, 1.0, n_byzantine),     # lat_spike_ratio (Spiked 5x normal)
            np.where(rng.random(n_byzantine) > 0.7,
                     rng.uniform(0.5, 1.0, n_byzantine),
                     rng.uniform(0, 0.05, n_byzantine)),  # invalid_hash_rate
            rng.uniform(0, 0.3, n_byzantine)              # identity_age_score
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
            temp_path = self.csv_path + ".tmp"
            self.df.to_csv(temp_path, index=False)
            os.replace(temp_path, self.csv_path) # Atomic crash-proof overwrite
            if hasattr(self, 'bucket') and self.bucket:
                blob = self.bucket.blob('master_training_data_ORGANIC.csv')
                blob.upload_from_filename(self.csv_path)
                print("☁️ SUCCESS: Uploaded updated CSV to Google Cloud Storage")
        except Exception as e:
            print(f"🔴 ERROR: Failed to save dataset to disk/GCS: {e}")

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

    def export_tree_structure(self) -> dict:
        """Exports the exact mathematical branching structure of the primary decision tree."""
        if not hasattr(self.nn, 'estimators_') or len(self.nn.estimators_) == 0:
            return {"type": "tree_structure", "nodes": []}
            
        tree_ = self.nn.estimators_[0].tree_
        # Column of the "Byzantine" class in tree_.value (classes are e.g. [0, 1] or [False, True])
        classes = list(getattr(self.nn, "classes_", []))
        byz_col = next((k for k, c in enumerate(classes) if c in (1, True, "1", "byzantine")), len(classes) - 1)
        nodes = []
        for i in range(tree_.node_count):
            feature_idx = tree_.feature[i]
            # Visual-only extras for the 3D tree: how much training data reaches this node, and
            # the share of it that is Byzantine (the leaf's verdict)
            counts = tree_.value[i][0]
            total = float(counts.sum())
            nodes.append({
                "id": i,
                "left": int(tree_.children_left[i]),
                "right": int(tree_.children_right[i]),
                "feature": FEATURE_NAMES[feature_idx] if feature_idx >= 0 else "LEAF",
                "threshold": float(tree_.threshold[i]),
                "is_leaf": bool(feature_idx < 0),
                "samples": int(tree_.n_node_samples[i]),
                "byzantine": float(counts[byz_col] / total) if total > 0 and byz_col >= 0 else 0.0
            })
        return {"type": "tree_structure", "nodes": nodes}

    def get_decision_path(self, features: dict) -> dict:
        """Traces the exact path a node's data took through the Random Forest."""
        if not hasattr(self.nn, 'estimators_') or len(self.nn.estimators_) == 0:
            return {"path": []}
            
        row = np.array([[features.get(f, 0.0) for f in FEATURE_NAMES]])
        tree = self.nn.estimators_[0]
        
        try:
            path_sparse = tree.decision_path(row)
            node_ids = path_sparse.indices.tolist()
            return {"path": node_ids}
        except Exception:
            return {"path": []}




