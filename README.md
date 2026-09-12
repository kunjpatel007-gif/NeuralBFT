# NeuralBFT (Consensus Arena)

A Byzantine Fault Tolerance (BFT) blockchain simulator featuring an integrated Machine Learning perception layer and a Bayesian reputation policy engine. 

This project simulates a decentralized consensus network operating under chaotic, real-world network conditions (e.g., public Wi-Fi, satellite latency) and dynamically mitigates adversarial nodes using a trained Neural Network.

## Architecture

The system is strictly decoupled into three layers:
1. **Core Simulator (`main.py` / `node.py`):** Handles websocket broadcasting, hardware profile assignment, and round-based consensus mechanics.
2. **Perception Layer (`detector.py`):** An `MLPClassifier` combined with an `IsolationForest` that ingests raw network telemetry (latency, message frequency, vote inconsistency, and temporal deltas) to output a probabilistic threat score (0.0 - 1.0).
3. **Policy Layer (`policy.py`):** A Bayesian reputation engine that translates ML threat scores into actionable consensus penalties.

## Key Features

* **Adaptive Trust Slashing:** Standard Bayesian reputation (`alpha / alpha + beta`) is enhanced with exponential beta scaling and adaptive alpha decay. Minor lag spikes incur gentle penalties, while blatant 99% threats instantly destroy a node's historical trust cushion.
* **EMA Network Thresholds:** The baseline threat threshold dynamically adjusts using an Exponential Moving Average of the global network state, preventing mass false-positives during global lag events.
* **Hardware Profiles:** Simulates 7 distinct network profiles (Fiber, Satellite, 3G, Public Wi-Fi, etc.), forcing the ML model to differentiate between adversarial behavior and natural packet loss.
* **Temporal Fusion:** The ML model tracks 6 temporal rate-of-change features (deltas) to detect "Stealth" nodes attempting to yo-yo between malicious and honest behavior.
* **Live UI Visualization:** Real-time vanilla JS/HTML canvas frontend rendering node state, network physics, and ML threat analytics via WebSockets.

## Tech Stack

* **Backend:** Python 3.14+, `scikit-learn`, `pandas`, `numpy`, `websockets`
* **Frontend:** HTML5, CSS Flexbox, Vanilla JavaScript (Canvas API)

## Installation & Usage

1. Clone the repository and navigate to the root directory.
2. Install the required Python dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Start the backend simulation server:
   ```bash
   python backend/main.py
   ```
4. Open the frontend UI:
   Double-click `frontend_web/index.html` in any modern web browser.

## Repository Structure

```text
├── backend/
│   ├── main.py                 # Core simulation loop and WebSocket server
│   ├── mitigation/
│   │   └── policy.py           # Bayesian Beta reputation engine
│   ├── ml/
│   │   ├── detector.py         # MLPClassifier and dataset bootstrapping
│   │   └── master_training_data.csv # Actively trained dataset
│   └── simulator/
│       ├── network.py          # Network state management
│       ├── node.py             # Node instances and hardware profiles
│       └── consensus/          # PBFT, PoW, PoS, DPoS implementations
├── frontend_web/
│   ├── index.html              # Dynamic Flexbox UI layout
│   └── app.js                  # Canvas rendering and WebSocket client
└── requirements.txt
```
