"""Test isolation: never touch GCS or the real training CSV.

Detectors built in tests train from a *temporary copy* of the CSV, so
retraining / _save_csv() can't modify the hand-curated dataset.
"""
import os
import shutil
import pytest
from backend.ml.detector import ByzantineDetector



@pytest.fixture(autouse=True)
def isolated_detector(tmp_path, monkeypatch):
    import backend.ml.detector as det
    real = os.path.join(os.path.dirname(det.__file__), 'master_training_data_ORGANIC.csv')

    def fake_init_gcs(self):
        self.storage_client = None
        self.bucket = None
        self.csv_path = str(tmp_path / 'master_training_data_ORGANIC.csv')
        if os.path.exists(real):
            shutil.copy2(real, self.csv_path)

    monkeypatch.setattr(ByzantineDetector, '_init_gcs', fake_init_gcs)
