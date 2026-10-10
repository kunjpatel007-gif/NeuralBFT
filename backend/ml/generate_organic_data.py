import pandas as pd
import numpy as np
from scipy import stats
import os

np.random.seed(42)

# File paths
import sys
_here = os.path.dirname(os.path.abspath(__file__))
input_file = os.path.join(_here, "master_training_data.csv")
output_file = os.path.join(_here, "master_training_data_ORGANIC.csv")

# Never clobber a hand-curated dataset by accident
if os.path.exists(output_file) and "--force" not in sys.argv:
    sys.exit(f"{output_file} already exists. Re-run with --force to overwrite it.")

# Load data
df = pd.read_csv(input_file)

original_df = df.iloc[:-300].copy()

# Features list based on instruction
features = ['msg_freq', 'vote_inconsistency', 'latency', 'fork_attempts', 'silence_ratio', 'msg_freq_delta', 'latency_delta', 'vote_drift_delta', 'silence_delta', 'freq_spike_ratio', 'lat_spike_ratio', 'invalid_hash_rate', 'identity_age_score']
label_col = 'label'

# Separate honest and byzantine
honest_df = original_df[original_df[label_col] == 0][features]
byzantine_df = original_df[original_df[label_col] == 1][features]

# Add tiny noise to avoid singular covariance matrix for KDE
noise_honest = np.random.normal(0, 1e-5, honest_df.shape)
noise_byzantine = np.random.normal(0, 1e-5, byzantine_df.shape)

# Fit KDE
kde_honest = stats.gaussian_kde((honest_df + noise_honest).T)
kde_byzantine = stats.gaussian_kde((byzantine_df + noise_byzantine).T)

# Generate 400 honest samples for Tamperers and Honest counter-examples
honest_samples = kde_honest.resample(400).T
honest_samples = np.clip(honest_samples, 0, None) # Clip negatives
honest_samples_df = pd.DataFrame(honest_samples, columns=features)

# Generate 200 byzantine samples for Sybil Swarmers
byzantine_samples = kde_byzantine.resample(200).T
byzantine_samples = np.clip(byzantine_samples, 0, None)
byzantine_samples_df = pd.DataFrame(byzantine_samples, columns=features)

# 1. 200 State Tamperers (Label 1)
tamperers = honest_samples_df.iloc[:200].copy()
tamperers['invalid_hash_rate'] = np.random.uniform(0.5, 1.0, 200)
tamperers['identity_age_score'] = np.clip(tamperers['identity_age_score'], 0.5, 1.0)
tamperers['label'] = 1

# 2. 200 Sybil Swarmers (Label 1)
sybils = byzantine_samples_df.iloc[:200].copy()
sybils['identity_age_score'] = np.random.uniform(0.0, 0.05, 200)
sybils['invalid_hash_rate'] = np.random.uniform(0.0, 0.1, 200)
sybils['label'] = 1

# 3. 200 Honest counter-examples (Label 0)
honest_counter = honest_samples_df.iloc[200:400].copy()
honest_counter['invalid_hash_rate'] = 0.0
honest_counter['identity_age_score'] = np.random.uniform(0.6, 1.0, 200)
honest_counter['label'] = 0

# Append everything together
new_synthetic = pd.concat([tamperers, sybils, honest_counter], ignore_index=True)

# Combine with original
final_df = pd.concat([original_df, new_synthetic], ignore_index=True)

# Clip specific ratios to 0-1
ratio_cols = ['silence_ratio', 'freq_spike_ratio', 'lat_spike_ratio', 'invalid_hash_rate', 'identity_age_score']
for col in ratio_cols:
    if col in final_df.columns:
        final_df[col] = np.clip(final_df[col], 0, 1)

# Save
final_df.to_csv(output_file, index=False)

print("Original Data Info:")
print(f"Original df total rows: {len(df)}")
print(f"Original df sliced (excluding last 300): {len(original_df)}")

print("\nNew Organic Synthetic Data Info:")
print(f"Tamperers: {len(tamperers)}")
print(f"Sybils: {len(sybils)}")
print(f"Honest Counter-examples: {len(honest_counter)}")

print("\nFinal Data Info:")
print(f"Final rows: {len(final_df)}")
print(f"Final class balance:\n{final_df['label'].value_counts()}")

print("\n=== Comparison of Old Synthetic vs New Organic ===")
old_synthetic = df.iloc[-300:]
print("\nOld Synthetic Means:")
print(old_synthetic.mean(numeric_only=True))
print("\nNew Organic Synthetic Means:")
print(new_synthetic.mean(numeric_only=True))

print("\nOld Synthetic Std:")
print(old_synthetic.std(numeric_only=True))
print("\nNew Organic Synthetic Std:")
print(new_synthetic.std(numeric_only=True))
