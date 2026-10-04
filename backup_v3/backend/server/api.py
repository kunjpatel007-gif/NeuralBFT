import asyncio
import json
import websockets
import logging

class StateServer:
    def __init__(self, host: str = '0.0.0.0', port: int = 8765):
        self.host = host
        self.port = port
        self.clients: set = set()
        self.pending_commands: list = []
    
    async def handler(self, websocket):
        """Handle new WebSocket connections."""
        self.clients.add(websocket)
        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    self.pending_commands.append(data)
                except json.JSONDecodeError:
                    logging.warning(f"Invalid JSON received: {message}")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self.clients.discard(websocket)
    
    async def broadcast_state(self, state_dict: dict):
        """Broadcast state to all connected clients."""
        if not self.clients:
            return
        
        message = json.dumps(state_dict)
        disconnected = set()
        for client in list(self.clients):
            try:
                await client.send(message)
            except websockets.exceptions.ConnectionClosed:
                disconnected.add(client)
        
        self.clients -= disconnected
    
    async def start(self):
        """Start the WebSocket server."""
        return await websockets.serve(self.handler, self.host, self.port)
