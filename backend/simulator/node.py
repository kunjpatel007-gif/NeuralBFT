import asyncio
from dataclasses import dataclass
from typing import Any, TYPE_CHECKING

if TYPE_CHECKING:
    from .network import NetworkManager

@dataclass
class Message:
    id: str
    sender_id: str
    receiver_id: str
    type: str
    payload: Any
    timestamp: float

@dataclass
class Node:
    id: str
    is_byzantine: bool = False
    reputation: float = 100.0
    status: str = 'Trusted'
    partition_id: int = 0
    ml_prob: float = 0.0
    network_profile: str = "Standard Wi-Fi"
    lag_timer: int = 0
    ml_features: dict = None
    alpha: float = 20.0  # Bayesian Honest Counter
    beta: float = 1.0    # Bayesian Suspicious Counter

    def update_status(self):
        if self.reputation >= 90.0:
            self.status = 'Verified'
        elif self.reputation >= 70.0:
            self.status = 'Trusted'
        elif self.reputation >= 50.0:
            self.status = 'Watched'
        elif self.reputation >= 30.0:
            self.status = 'High Risk'
        elif self.reputation >= 10.0:
            self.status = 'Quarantined'
        else:
            self.status = 'Blacklisted'

    async def receive_message(self, msg: Message):
        # Base receive logic
        pass

    async def broadcast(self, msg: Message, network: 'NetworkManager'):
        network.add_message(msg)
