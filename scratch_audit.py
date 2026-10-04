import pandas as pd
import numpy as np
from sklearn.ensemble import RandomForestClassifier

df = pd.read_csv("backend/ml/master_training_data.csv")

# Focus on the new parameters: invalid_hash_rate, identity_age_score
# and the new attack labels if we can isolate them. 
# Since we just appended 300 rows (100 tamper, 100 sybil, 100 honest) at the end, let's look at them.

new_rows = df.tail(300)
tamper = new_rows.iloc[0:100]
sybil = new_rows.iloc[100:200]
honest = new_rows.iloc[200:300]

print("=== FEATURE MEANS ===")
print("Honest:")
print(f"  invalid_hash_rate: {honest['invalid_hash_rate'].mean():.4f}")
print(f"  identity_age_score: {honest['identity_age_score'].mean():.4f}")
print(f"  msg_freq: {honest['msg_freq'].mean():.4f}")

print("\nState Tamper:")
print(f"  invalid_hash_rate: {tamper['invalid_hash_rate'].mean():.4f}")
print(f"  identity_age_score: {tamper['identity_age_score'].mean():.4f}")
print(f"  msg_freq: {tamper['msg_freq'].mean():.4f}")

print("\nSybil Swarm:")
print(f"  invalid_hash_rate: {sybil['invalid_hash_rate'].mean():.4f}")
print(f"  identity_age_score: {sybil['identity_age_score'].mean():.4f}")
print(f"  msg_freq: {sybil['msg_freq'].mean():.4f}")

X = df.drop(columns=['label'])
y = df['label']

rf = RandomForestClassifier(random_state=42)
rf.fit(X, y)

print("\n=== FEATURE IMPORTANCES ACROSS WHOLE DATASET ===")
importances = pd.Series(rf.feature_importances_, index=X.columns).sort_values(ascending=False)
for k, v in importances.items():
    print(f"{k}: {v:.4f}")
