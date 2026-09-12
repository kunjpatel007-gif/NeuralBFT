import pandas as pd
import numpy as np
import os

def generate_data(num_samples: int = 5000, output_path: str = 'backend/ml/training_data.csv') -> None:
    np.random.seed(42)
    
    # 60% honest, 40% Byzantine
    num_honest = int(num_samples * 0.6)
    num_byz = num_samples - num_honest
    
    honest_labels = np.zeros(num_honest, dtype=int)
    byz_labels = np.ones(num_byz, dtype=int)
    
    labels = np.concatenate([honest_labels, byz_labels])
    
    # msg_freq
    msg_freq_honest = np.random.normal(50, 10, num_honest)
    
    byz_high = np.random.normal(120, 20, int(num_byz / 2))
    byz_low = np.random.normal(5, 3, num_byz - len(byz_high))
    msg_freq_byz = np.concatenate([byz_high, byz_low])
    
    msg_freq = np.concatenate([msg_freq_honest, msg_freq_byz])
    msg_freq = np.clip(msg_freq, 0, None)
    
    # vote_inconsistency
    vote_inc_honest = np.random.uniform(0, 0.1, num_honest)
    vote_inc_byz = np.random.uniform(0.3, 0.9, num_byz)
    vote_inc = np.concatenate([vote_inc_honest, vote_inc_byz])
    vote_inc = np.clip(vote_inc, 0, 1)
    
    # latency
    latency_honest = np.random.normal(100, 20, num_honest)
    
    byz_lat_high = np.random.normal(500, 100, int(num_byz / 2))
    byz_lat_low = np.random.normal(10, 5, num_byz - len(byz_lat_high))
    latency_byz = np.concatenate([byz_lat_high, byz_lat_low])
    
    latency = np.concatenate([latency_honest, latency_byz])
    latency = np.clip(latency, 0, None)
    
    # fork_attempts
    fork_honest = np.random.poisson(0.1, num_honest)
    fork_byz = np.random.poisson(3.0, num_byz)
    fork = np.concatenate([fork_honest, fork_byz])
    
    # silence_ratio
    silence_honest = np.random.uniform(0, 0.05, num_honest)
    silence_byz = np.random.uniform(0.2, 0.7, num_byz)
    silence = np.concatenate([silence_honest, silence_byz])
    silence = np.clip(silence, 0, 1)
    
    df = pd.DataFrame({
        'msg_freq': msg_freq,
        'vote_inconsistency': vote_inc,
        'latency': latency,
        'fork_attempts': fork,
        'silence_ratio': silence,
        'is_byzantine': labels
    })
    
    # Shuffle dataset
    df = df.sample(frac=1, random_state=42).reset_index(drop=True)
    
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    df.to_csv(output_path, index=False)
    print(f"Generated {len(df)} samples and saved to {output_path}")

if __name__ == '__main__':
    generate_data()
