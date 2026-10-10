import asyncio
import json
import os
import websockets
import logging
import hmac
import hashlib
import secrets
import time
from collections import deque

FAULT_TYPES = ('honest', 'offline', 'malicious', 'stealth', 'state_tampering', 'spam')
CONSENSUS_NAMES = ('PoW', 'PoS', 'DPoS', 'PBFT')
NO_ARG_ACTIONS = ('split_network', 'merge_network', 'add_node', 'remove_node')


def _expected_token():
    """The access token clients must present. Only a local run may fall back to the public
    development token: a deployed service (Cloud Run sets K_SERVICE) without ADMIN_TOKEN
    refuses every connection instead of silently accepting the public default."""
    token = os.environ.get("ADMIN_TOKEN")
    if token:
        return token
    if os.environ.get("K_SERVICE"):
        return None
    return "local_dev_token"


def token_ok(token: str) -> bool:
    """Constant-time comparison, so response timing cannot leak the token."""
    expected = _expected_token()
    return expected is not None and hmac.compare_digest(str(token).encode(), expected.encode())


# ---------------------------------------------------------------------------
# Sessions: the browser never keeps the admin token. Logging in trades it for a
# signed session token that expires on its own. Sessions are stateless (signed
# with a key derived from ADMIN_TOKEN), so they work across Cloud Run instances,
# and changing ADMIN_TOKEN instantly invalidates every session.
# ---------------------------------------------------------------------------
SESSION_SECONDS = 2 * 60 * 60


def _session_key():
    expected = _expected_token()
    return None if expected is None else hashlib.sha256(b"neuralbft-session:" + expected.encode()).digest()


def issue_session() -> str:
    key = _session_key()
    payload = f"{int(time.time()) + SESSION_SECONDS}.{secrets.token_hex(12)}"
    signature = hmac.new(key, payload.encode(), hashlib.sha256).hexdigest()
    return f"s.{payload}.{signature}"


def session_ok(token: str) -> bool:
    key = _session_key()
    parts = str(token).split(".")
    if key is None or len(parts) != 4 or parts[0] != "s":
        return False
    payload = f"{parts[1]}.{parts[2]}"
    expected = hmac.new(key, payload.encode(), hashlib.sha256).hexdigest()
    if not hmac.compare_digest(parts[3].encode(), expected.encode()):
        return False
    try:
        return int(parts[1]) > time.time()
    except ValueError:
        return False


def credential_ok(token: str) -> bool:
    """A session token, or the admin token itself (local tools and older builds)."""
    return session_ok(token) or token_ok(token)


# ---------------------------------------------------------------------------
# Brute-force protection: 5 failed attempts from one address within 10 minutes
# locks that address out for 15 minutes, doubling with each repeat (max 24 h).
# ---------------------------------------------------------------------------
MAX_FAILURES = 5
FAILURE_WINDOW = 10 * 60
LOCKOUT_SECONDS = 15 * 60


class AttemptLimiter:
    def __init__(self):
        self._failures = {}   # ip -> deque of failure times
        self._locked = {}     # ip -> (locked_until, lockout_count)

    def locked(self, ip: str) -> bool:
        until, _ = self._locked.get(ip, (0, 0))
        return time.time() < until

    def fail(self, ip: str):
        now = time.time()
        window = self._failures.setdefault(ip, deque())
        window.append(now)
        while window and now - window[0] > FAILURE_WINDOW:
            window.popleft()
        if len(window) >= MAX_FAILURES:
            _, count = self._locked.get(ip, (0, 0))
            seconds = min(LOCKOUT_SECONDS * (2 ** count), 24 * 3600)
            self._locked[ip] = (now + seconds, count + 1)
            window.clear()
            logging.warning(f"Locked out {ip} for {seconds // 60} min after repeated failed logins.")
        if len(self._failures) > 10000:  # keep memory bounded
            self._failures.clear()

    def succeed(self, ip: str):
        self._failures.pop(ip, None)
        self._locked.pop(ip, None)


LIMITER = AttemptLimiter()
AUTH_TIMEOUT = 10  # seconds a client has to send its first (auth or login) message


def client_ip(websocket) -> str:
    """The caller's address. Behind Cloud Run the real client is the last X-Forwarded-For hop
    (earlier entries can be supplied by the client itself)."""
    try:
        forwarded = websocket.request.headers.get("X-Forwarded-For", "")
    except AttributeError:
        forwarded = getattr(websocket, "request_headers", {}).get("X-Forwarded-For", "")
    if forwarded:
        return forwarded.split(",")[-1].strip()
    address = getattr(websocket, "remote_address", None)
    return str(address[0]) if address else "unknown"


async def authenticate(websocket) -> bool:
    """Accepts a connection only with valid credentials. Preferred: the first message is
    {"type": "auth", "token": <session>} so nothing secret appears in the URL (and in request
    logs). A {"type": "login", "password": <admin token>} message is answered with a fresh
    session token and the connection is then closed. A ?token= in the URL still works for
    builds made before this change. Failed attempts count toward the lockout."""
    from urllib.parse import urlparse, parse_qs

    ip = client_ip(websocket)
    if LIMITER.locked(ip):
        await websocket.close(code=1008, reason="Too many attempts")
        return False

    try:
        path = websocket.request.path
    except AttributeError:
        path = getattr(websocket, 'path', '/')
    url_token = parse_qs(urlparse(path).query).get('token', [''])[0]

    if url_token:
        if credential_ok(url_token):
            LIMITER.succeed(ip)
            return True
    else:
        try:
            first = json.loads(await asyncio.wait_for(websocket.recv(), AUTH_TIMEOUT))
        except (asyncio.TimeoutError, json.JSONDecodeError, TypeError, websockets.exceptions.ConnectionClosed):
            first = None

        if isinstance(first, dict) and first.get('type') == 'auth' and credential_ok(first.get('token', '')):
            LIMITER.succeed(ip)
            return True

        if isinstance(first, dict) and first.get('type') == 'login' and token_ok(first.get('password', '')):
            LIMITER.succeed(ip)
            try:
                await websocket.send(json.dumps({'type': 'session', 'token': issue_session(), 'expires_in': SESSION_SECONDS}))
                await websocket.close(code=1000, reason="Logged in")
            except websockets.exceptions.ConnectionClosed:
                pass
            return False

    LIMITER.fail(ip)
    logging.warning(f"Unauthorized connection attempt blocked ({ip}).")
    await asyncio.sleep(1)  # slows down guessing even before the lockout starts
    try:
        await websocket.close(code=1008, reason="Too many attempts" if LIMITER.locked(ip) else "Unauthorized")
    except websockets.exceptions.ConnectionClosed:
        pass
    return False


def validate_command(data: dict):
    """Return a clean command dict, or None if it is malformed / unknown.
    Keeps a single bad client message from crashing the simulation loop."""
    action = data.get('action')
    out = {'action': action, '_client': data.get('_client')}
    try:
        if action in NO_ARG_ACTIONS:
            return out
        if action == 'inject_fault':
            if isinstance(data.get('node_id'), str) and data.get('fault_type') in FAULT_TYPES:
                out.update(node_id=data['node_id'], fault_type=data['fault_type'])
                return out
        elif action == 'switch_consensus':
            if data.get('consensus') in CONSENSUS_NAMES:
                out['consensus'] = data['consensus']
                return out
        elif action == 'sybil_swarm':
            out['count'] = max(1, min(int(data.get('count', 5)), 10))
            return out
        elif action == 'get_history_snapshot':
            out['round'] = int(data['round'])
            return out
        elif action in ('report_false_positive', 'report_missed_attack'):
            if isinstance(data.get('node_id'), str):
                out['node_id'] = data['node_id']
                return out
    except (TypeError, ValueError, KeyError):
        pass
    return None


class StateServer:
    def __init__(self, host: str = '0.0.0.0', port: int = 8765):
        self.host = host
        self.port = port
        self.clients: set = set()
        self.pending_commands: list = []
        self.history: deque = deque(maxlen=500)
        if not os.environ.get("ADMIN_TOKEN"):
            if os.environ.get("K_SERVICE"):
                logging.error("ADMIN_TOKEN is not set on this deployed service: every connection will be refused.")
            else:
                logging.warning("ADMIN_TOKEN is not set: local run, accepting the development token.")
    
    async def handler(self, websocket):
        """Handle new WebSocket connections."""
        if not await authenticate(websocket):
            return

        self.clients.add(websocket)
        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    if not isinstance(data, dict):
                        logging.warning("Ignoring non-object command")
                        continue
                    data['_client'] = websocket
                    cmd = validate_command(data)
                    if cmd is None:
                        logging.warning(f"Ignoring invalid command: {str(message)[:120]}")
                        continue
                    self.pending_commands.append(cmd)
                except json.JSONDecodeError:
                    logging.warning(f"Invalid JSON received: {message}")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self.clients.discard(websocket)
    
    async def broadcast_state(self, state_dict: dict):
        """Broadcast state to all connected clients."""
        # Only full state snapshots are replayable; telemetry/shockwave payloads
        # (which carry the whole tree) would just bloat the 500-deep history.
        if 'nodes' in state_dict:
            self.history.append(state_dict)
        if not self.clients:
            return
        
        # websockets.broadcast is non-blocking: one slow client can't stall the tick
        # and closed sockets are skipped (the handler's finally removes them).
        websockets.broadcast(self.clients, json.dumps(state_dict))
    
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
        if not await authenticate(websocket):
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

