using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

// JSON containers that match the Python backend's exact output
[Serializable]
public class ServerState
{
    public int round;
    public string consensus;
    public List<NodeData> nodes;
    public List<MessageData> messages;
    public List<BlockData> blocks;
    public MetricsData metrics;
}

[Serializable]
public class MessageData
{
    public string from;
    public string to;
    public string type;
}

[Serializable]
public class BlockData
{
    public int round;
    public string proposer;
    public string hash;
    public bool is_rejected;
    public string consensus;
    public int tx_count;
}

[Serializable]
public class MetricsData
{
    public float tps;
    public float block_time_ms;
    public int msg_overhead;
}

public class NetworkManager : MonoBehaviour
{
    [Header("WebSocket")]
    public string serverUrl = "wss://neuralbft-backend-443293282760.asia-south1.run.app";

    [Header("Prefabs")]
    [Tooltip("No longer used: messages are drawn by MessageTraffic. Kept so existing scenes keep their reference.")]
    public GameObject messagePrefab;

    [Header("References")]
    public BlockSpawner blockSpawner;
    public LeaderboardManager leaderboardManager;

    [Header("Message Visuals")]
    [Tooltip("Seconds a message takes to travel between two nodes. Slow enough to follow and click.")]
    public float secondsPerMessage = 7.3f;
    [Tooltip("Upper limit on message dots in flight at once.")]
    public int maxVisibleMessages = 400;

    private ClientWebSocket _ws;
    private string _authToken = "local_dev_token";
    private CancellationTokenSource _cts;
    private ServerState _latestState;
    private bool _hasNewState = false;
    private HashSet<int> _spawnedBlockRounds = new HashSet<int>(); // Track by round, not count

    // Maps node IDs to their spawned GameObjects in the scene
    private Dictionary<string, GameObject> _nodeMap = new Dictionary<string, GameObject>();
    // Maps node IDs to the latest data for that node, rebuilt on every state
    private readonly Dictionary<string, NodeData> _nodeData = new Dictionary<string, NodeData>();

    private MessageTraffic _traffic;
    private ConsensusCore _core;

    public NodeSpawner nodeSpawner; // Need reference to the spawner
    public LedgerZone ledgerZone;
    public TimelineController timelineController;

    #if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern string GetBrowserToken();
#endif

    async void Start()
    {
        string token = "local_dev_token";
#if UNITY_WEBGL && !UNITY_EDITOR
        try { token = GetBrowserToken(); } catch {}
#endif
        // Sent as the first message after connecting, never in the URL (URLs end up in logs)
        _authToken = token;

        // Force the GameObject name so JS SendMessage can definitively find it
        gameObject.name = "NetworkManager";

        SetupVisuals();

        _cts = new CancellationTokenSource();
        
#if !UNITY_WEBGL || UNITY_EDITOR
        await ConnectWebSocket();
#else
        Debug.Log("[NetworkManager] Running in WebGL. Awaiting state from browser via JS SendMessage...");
#endif
    }

    // Everything decorative is created here, so the scene needs no extra wiring
    void SetupVisuals()
    {
        if (gameObject.GetComponent<BackgroundManager>() == null)
            gameObject.AddComponent<BackgroundManager>();

        _core = ConsensusCore.Ensure();

        // World-space root: message paths are drawn in world coordinates
        _traffic = new GameObject("MessageTraffic").AddComponent<MessageTraffic>();
    }

    // Called by frontend_web/app.js via SendMessage
    public void OnWebStateReceived(string json)
    {
        try
        {
            // Timeline recording
            if (timelineController != null) timelineController.RecordSnapshot(json);
            if (timelineController != null && timelineController.IsRewinding) return;
            
            var parsed = JsonConvert.DeserializeObject<ServerState>(json);
            if (parsed == null || parsed.nodes == null) return; // shockwave / history frames etc.
            _latestState = parsed;
            _hasNewState = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Failed to parse JSON from browser: {ex.Message}");
        }
    }

    async Task ConnectWebSocket()
    {
        // Captured once: OnDestroy cancels and disposes the source when play mode stops,
        // and reading _cts.Token after that throws. The token itself stays safe to check.
        CancellationToken token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                Debug.Log($"[NetworkManager] Connecting to {serverUrl}...");
                await _ws.ConnectAsync(new Uri(serverUrl), token);
                byte[] auth = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { type = "auth", token = _authToken }));
                await _ws.SendAsync(new ArraySegment<byte>(auth), WebSocketMessageType.Text, true, token);
                Debug.Log("[NetworkManager] Connected!");

                await ReceiveLoop(token);
            }
            catch (Exception ex)
            {
                // Shutting down (play mode stopped or object destroyed): leave quietly
                if (token.IsCancellationRequested) break;

                Debug.LogWarning($"[NetworkManager] Connection failed: {ex.Message}. Retrying in 3s...");
                try { await Task.Delay(3000, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[65536];
        var msgBuffer = new System.IO.MemoryStream();

        while (_ws.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(
                new ArraySegment<byte>(buffer), token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[NetworkManager] Server closed connection.");
                break;
            }

            // Messages larger than the buffer arrive in several frames: reassemble first
            msgBuffer.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            string json = Encoding.UTF8.GetString(msgBuffer.GetBuffer(), 0, (int)msgBuffer.Length);
            msgBuffer.SetLength(0);

            // Timeline recording
            if (timelineController != null) timelineController.RecordSnapshot(json);
            if (timelineController != null && timelineController.IsRewinding) continue;

            try
            {
                var parsed = JsonConvert.DeserializeObject<ServerState>(json);
                if (parsed == null || parsed.nodes == null) continue;
                _latestState = parsed;
                _hasNewState = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] Bad JSON: {ex.Message}");
            }
        }
    }

    void Update()
    {
        // All Unity API calls MUST happen on the main thread (Update)
        if (!_hasNewState || _latestState == null || _latestState.nodes == null) return;
        _hasNewState = false;

        // 0. Synchronize the 3D nodes with the Python backend (add/remove/arrange)
        if (nodeSpawner != null)
        {
            _nodeMap = nodeSpawner.SyncNodes(_latestState.nodes, _nodeMap);
        }

        // 1. Update every node's HUD and visuals with live data
        _nodeData.Clear();
        foreach (var nodeData in _latestState.nodes)
        {
            if (nodeData == null || nodeData.id == null) continue;
            _nodeData[nodeData.id] = nodeData;

            if (_nodeMap.TryGetValue(nodeData.id, out GameObject nodeGO) && nodeGO != null)
            {
                var hud = nodeGO.GetComponentInChildren<NodeHUD>(true);
                if (hud != null) hud.UpdateHUD(nodeData);

                var visualizer = nodeGO.GetComponentInChildren<NodeVisualizer>(true);
                if (visualizer == null) 
                {
                    // Force-add it if the user's prefab is missing it
                    visualizer = nodeGO.AddComponent<NodeVisualizer>();
                }
                visualizer.UpdateVisuals(nodeData);
            }
        }

        // The consensus core takes on the colour of the active protocol
        if (_core != null) _core.SetConsensus(_latestState.consensus);

        // 2. Announce new blocks (only once per unique round)
        if (_latestState.blocks != null && blockSpawner != null)
        {
            foreach (var block in _latestState.blocks)
            {
                if (!_spawnedBlockRounds.Contains(block.round))
                {
                    _spawnedBlockRounds.Add(block.round);
                    blockSpawner.OnNewBlock();
                }
            }
        }

        // 2b. Forward blocks to the 3D Ledger Zone
        if (_latestState.blocks != null && ledgerZone != null)
        {
            foreach (var block in _latestState.blocks)
                ledgerZone.TrySpawnBlock(block);
        }

        // 3. Spawn real messages the moment the state arrives
        if (_latestState.messages != null && _traffic != null)
        {
            _traffic.flightSeconds = secondsPerMessage;
            _traffic.maxLiveMessages = maxVisibleMessages;
            foreach (var msg in _latestState.messages)
            {
                SpawnMessage(msg);
            }
        }

        // 4. Update leaderboard
        if (leaderboardManager != null && _latestState.nodes != null)
        {
            leaderboardManager.UpdateBoard(_latestState.nodes);
        }
    }

    void SpawnMessage(MessageData msg)
    {
        if (msg == null || msg.from == null || msg.to == null) return;
        if (!_nodeMap.TryGetValue(msg.from, out GameObject senderGO) || senderGO == null) return;
        if (!_nodeMap.TryGetValue(msg.to, out GameObject receiverGO) || receiverGO == null) return;

        // Colour follows the message type and the sender's network partition
        _nodeData.TryGetValue(msg.from, out NodeData sender);
        Color color = ArenaTheme.MessageColor(msg.type, sender != null ? sender.partition_id : 0);

        MessageInteractable info = _traffic.Launch(senderGO.transform, receiverGO.transform, color);
        if (info == null) return; // at the visible-message limit

        // INTERACTIVITY: click a message in flight to inspect its ML payload
        info.msgType = msg.type;
        info.senderId = msg.from;
        info.mlThreat = sender != null ? sender.ml_prob : 0f;
        info.mlFeatures = sender != null ? sender.ml_features : null;
    }

    public void RenderHistoricalState(ServerState state)
    {
        _latestState = state;
        _hasNewState = true;
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();   // release the underlying WaitHandle
        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            _ws.Dispose(); // release socket handles
        }
        if (_traffic != null) Destroy(_traffic.gameObject);
    }
}
