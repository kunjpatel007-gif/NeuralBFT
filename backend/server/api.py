import asyncio
import json
import websockets
import logging
from collections import deque

class StateServer:
    def __init__(self, host: str = '0.0.0.0', port: int = 8765):
        self.host = host
        self.port = port
        self.clients: set = set()
        self.pending_commands: list = []
        self.history: deque = deque(maxlen=500)
    
    async def handler(self, websocket):
        """Handle new WebSocket connections."""
        import os
        from urllib.parse import urlparse, parse_qs
        query = parse_qs(urlparse(websocket.path).query)
        token = query.get('token', [''])[0]
        
        expected_token = os.environ.get("ADMIN_TOKEN", "local_dev_token")
        
        if token != expected_token:
            logging.warning(f"Unauthorized connection attempt blocked.")
            await websocket.close(code=1008, reason="Unauthorized Token")
            return

        self.clients.add(websocket)
        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    data['_client'] = websocket
                    self.pending_commands.append(data)
                except json.JSONDecodeError:
                    logging.warning(f"Invalid JSON received: {message}")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self.clients.discard(websocket)
    
    async def broadcast_state(self, state_dict: dict):
        """Broadcast state to all connected clients."""
        self.history.append(state_dict)
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
    
    async def send_to_client(self, client, data: dict):
        try:
            await client.send(json.dumps(data))
        except websockets.exceptions.ConnectionClosed:
            pass

    async def start(self):
        """Start the WebSocket server."""
        return await websockets.serve(self.handler, self.host, self.port)

class TelemetryServer:
    def __init__(self, host: str = '0.0.0.0', port: int = 8766):
        self.host = host
        self.port = port
        self.clients: set = set()

    async def handler(self, websocket):
        import os
        from urllib.parse import urlparse, parse_qs
        query = parse_qs(urlparse(websocket.path).query)
        token = query.get('token', [''])[0]
        
        expected_token = os.environ.get("ADMIN_TOKEN", "local_dev_token")
        
        if token != expected_token:
            await websocket.close(code=1008, reason="Unauthorized")
            return

        self.clients.add(websocket)
        try:
            async for message in websocket:
                pass # Receive commands later if needed (e.g. "subscribe_node_1")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self.clients.discard(websocket)
    
    async def broadcast_telemetry(self, telemetry_dict: dict):
        if not self.clients:
            return
        
        message = json.dumps(telemetry_dict)
        disconnected = set()
        for client in list(self.clients):
            try:
                await client.send(message)
            except websockets.exceptions.ConnectionClosed:
                disconnected.add(client)
        
        self.clients -= disconnected

    async def start(self):
        return await websockets.serve(self.handler, self.host, self.port)

