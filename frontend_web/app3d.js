const isLocal = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1' || window.location.protocol === 'file:';
const WS_URL = new URLSearchParams(location.search).get('ws') || 
               (isLocal ? 'ws://127.0.0.1:8765' : 'wss://neuralbft-backend-443293282760.asia-south1.run.app');
let ws;
let reconnectTimer;
let reconnectTimeout = 1000;

function connect() {
  if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) return;
  const socket = new WebSocket(WS_URL);
  ws = socket;
  
  socket.onopen = () => { 
    if (ws === socket) { 
        reconnectTimeout = 1000; 
        console.log("3D WebSocket Connected"); 
    } 
  };
  
  socket.onmessage = (event) => { 
    if (ws === socket && window.unityInstance) { 
        window.unityInstance.SendMessage('NetworkManager', 'OnWebStateReceived', event.data);
    } 
  };
  
  socket.onclose = () => { 
    if (ws === socket) retry(); 
  };
}

function retry() {
  reconnectTimer = setTimeout(connect, reconnectTimeout);
  reconnectTimeout = Math.min(reconnectTimeout * 2, 10000);
}

connect();
