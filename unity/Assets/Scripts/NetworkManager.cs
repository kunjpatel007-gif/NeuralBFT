using UnityEngine;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class NetworkManager : MonoBehaviour
{
    [Header("Server Connection")]
    public string serverUrl = "ws://localhost:8765";
    
    [Header("Prefabs (assign in Inspector)")]
    public GameObject nodePrefab;
    public GameObject messagePrefab;
    
    [Header("Layout Settings")]
    public float baseRingRadius = 14f;

    private ClientWebSocket ws = null;
    private ConcurrentQueue<string> incomingMessages = new ConcurrentQueue<string>();
    private CancellationTokenSource cts = new CancellationTokenSource();
    
    private Dictionary<string, NodeVisualizer> spawnedNodes = new Dictionary<string, NodeVisualizer>();
    private SimState _latestState;
    public SimState LatestState { get { return _latestState; } private set { _latestState = value; } }
    
    public bool IsConnected { get { return ws != null && ws.State == WebSocketState.Open; } }

    void Start()
    {
        ConnectToServer();
    }

    async void ConnectToServer()
    {
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Debug.Log("[NetworkManager] Connected to Python server!");
            _ = ReceiveLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] Failed to connect: {e.Message}");
        }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        
        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            try
            {
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    sb.Append(chunk);
                    
                    if (result.EndOfMessage)
                    {
                        incomingMessages.Enqueue(sb.ToString());
                        sb.Clear();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token);
                }
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    public async void SendToServer(string json)
    {
        if (IsConnected)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
    }

    void Update()
    {
        while (incomingMessages.TryDequeue(out string json))
        {
            ProcessState(json);
        }
    }

    private void ProcessState(string json)
    {
        try
        {
            if (nodePrefab == null || messagePrefab == null)
            {
                Debug.LogWarning("[NetworkManager] Prefabs are missing!");
                return;
            }

            SimState state = JsonConvert.DeserializeObject<SimState>(json);
            if (state == null || state.nodes == null) return;
            
            LatestState = state;
            int nodeCount = state.nodes.Length;
            
            // Dynamic radius scales with node count (Churn Feature)
            float dynamicRadius = baseRingRadius + (nodeCount * 0.25f);
            
            // Keep track of active IDs to delete removed nodes
            HashSet<string> activeIds = new HashSet<string>();

            // ── Nodes ──
            for (int i = 0; i < nodeCount; i++)
            {
                var nodeData = state.nodes[i];
                activeIds.Add(nodeData.id);
                
                // Partition Math for Split-Brain visualization
                int partitionCount = 0;
                int indexInPartition = 0;
                for (int j = 0; j < nodeCount; j++) {
                    if (state.nodes[j].partition_id == nodeData.partition_id) {
                        if (j < i) indexInPartition++;
                        partitionCount++;
                    }
                }
                
                float angle = indexInPartition * Mathf.PI * 2f / Mathf.Max(1, partitionCount);
                
                // Offset rings horizontally if partitioned
                float xOffset = 0f;
                if (nodeData.partition_id == 0 && partitionCount < nodeCount) xOffset = -dynamicRadius * 1.1f;
                if (nodeData.partition_id == 1) xOffset = dynamicRadius * 1.1f;
                
                Vector3 targetPos = new Vector3(Mathf.Cos(angle) * dynamicRadius + xOffset, 0, Mathf.Sin(angle) * dynamicRadius);

                if (!spawnedNodes.TryGetValue(nodeData.id, out NodeVisualizer nv))
                {
                    GameObject nodeObj = Instantiate(nodePrefab, targetPos, Quaternion.identity, transform);
                    nodeObj.name = nodeData.id;
                    nodeObj.SetActive(true);
                    nv = nodeObj.GetComponent<NodeVisualizer>();
                    spawnedNodes[nodeData.id] = nv;
                }
                else
                {
                    // Smoothly move to new position if scaling/splitting
                    nv.transform.position = Vector3.Lerp(nv.transform.position, targetPos, Time.deltaTime * 5f);
                }
                
                nv.UpdateState(nodeData);
            }
            
            // Delete removed nodes
            List<string> toRemove = new List<string>();
            foreach (var kvp in spawnedNodes) {
                if (!activeIds.Contains(kvp.Key)) {
                    Destroy(kvp.Value.gameObject);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove) spawnedNodes.Remove(id);

            // Push metrics to UIManager
            UIManager ui = FindObjectOfType<UIManager>();
            if (ui != null && state.metrics != null) {
                ui.PushMetrics(state.metrics.tps, state.metrics.msg_overhead);
            }

            // ── Messages (capped for performance) ──
            if (state.messages != null)
            {
                int maxVisual = 80;
                int step = Mathf.Max(1, state.messages.Length / maxVisual);
                
                for (int m = 0; m < state.messages.Length; m += step)
                {
                    var msg = state.messages[m];
                    if (spawnedNodes.TryGetValue(msg.from, out var fromViz) &&
                        spawnedNodes.TryGetValue(msg.to, out var toViz))
                    {
                        GameObject msgObj = Instantiate(messagePrefab, fromViz.transform.position, Quaternion.identity, transform);
                        msgObj.SetActive(true);
                        var animator = msgObj.GetComponent<MessageAnimator>();
                        if (animator != null)
                        {
                            animator.Initialize(fromViz.transform.position, toViz.transform.position, msg.type);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkManager] Parse error: {e.Message}\n{e.StackTrace}");
        }
    }

    void OnDestroy()
    {
        cts.Cancel();
        if (ws != null) ws.Dispose();
    }
}

// Data models
[Serializable]
public class NodeData
{
    public string id;
    public bool is_byzantine;
    public float reputation;
    public string status;
    public float ml_prob;
    [System.NonSerialized] public Dictionary<string, float> ml_features;
    public int partition_id;
}

[Serializable]
public class BlockData
{
    public string hash;
    public string proposer;
    public int tx_count;
    public string consensus;
}

[Serializable]
public class MetricsData
{
    public int tps;
    public int block_time_ms;
    public int msg_overhead;
}

[Serializable]
public class MsgData
{
    public string from;
    public string to;
    public string type;
}

[Serializable]
public class SimState
{
    public int round;
    public string consensus;
    public NodeData[] nodes;
    public MsgData[] messages;
    public BlockData[] blocks;
    public MetricsData metrics;
}
