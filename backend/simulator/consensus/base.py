from abc import ABC, abstractmethod

class BaseConsensus(ABC):
    name: str
    
    @abstractmethod
    async def execute_round(self, network) -> bool:
        """Execute one round. Returns True if block was produced."""
        pass
    
    @abstractmethod
    def apply_mitigation(self, node) -> None:
        """Apply protocol-specific mitigation to a flagged node."""
        pass
