const isLocal = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1' || window.location.protocol === 'file:';
// ?ws= is honoured only locally, so a crafted link cannot redirect your session elsewhere
const _base = (isLocal && new URLSearchParams(location.search).get('ws')) ||
               (isLocal ? 'ws://127.0.0.1:8765' : 'wss://neuralbft-backend-443293282760.asia-south1.run.app');
let _token = null;
try { _token = localStorage.getItem('arena_token'); } catch (e) {}
if (!_token && isLocal) _token = 'local_dev_token';
// No credentials in the URL: the session token goes in the first message instead
const WS_URL = _base;
let ws;
let reconnectTimer;
let reconnectTimeout = 1000;

function connect() {
  if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) return;
  const socket = new WebSocket(WS_URL);
  ws = socket;
  
  socket.onopen = () => { 
    if (ws === socket) { 
        socket.send(JSON.stringify({ type: 'auth', token: _token || '' }));
        reconnectTimeout = 1000; 
        console.log("3D WebSocket Connected"); 
    } 
  };
  
  socket.onmessage = (event) => { 
    if (ws === socket && window.unityInstance) { 
        window.unityInstance.SendMessage('NetworkManager', 'OnWebStateReceived', event.data);
    } 
  };
  
  socket.onclose = (event) => { 
    if (ws !== socket) return;
    // Rejected credentials (expired session, lockout): log in again
    if (event.code === 1008 && !isLocal) { window.location.href = 'login.html'; return; }
    retry(); 
  };
}

function retry() {
  reconnectTimer = setTimeout(connect, reconnectTimeout);
  reconnectTimeout = Math.min(reconnectTimeout * 2, 10000);
}

connect();
