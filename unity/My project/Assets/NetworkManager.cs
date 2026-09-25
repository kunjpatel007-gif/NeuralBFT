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
    public GameObject messagePrefab;

    [Header("References")]
    public BlockSpawner blockSpawner;
    public LeaderboardManager leaderboardManager;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private ServerState _latestState;
    private bool _hasNewState = false;
    private HashSet<int> _spawnedBlockRounds = new HashSet<int>(); // Track by round, not count

    // Maps node IDs to their spawned GameObjects in the scene
    private Dictionary<string, GameObject> _nodeMap = new Dictionary<string, GameObject>();

    public NodeSpawner nodeSpawner; // Need reference to the spawner

    async void Start()
    {
        // Force the GameObject name so JS SendMessage can definitively find it
        gameObject.name = "NetworkManager";

        // Automatically inject the premium background system
        if (gameObject.GetComponent<BackgroundManager>() == null)
        {
            gameObject.AddComponent<BackgroundManager>();
        }

        _cts = new CancellationTokenSource();
        
#if !UNITY_WEBGL || UNITY_EDITOR
        await ConnectWebSocket();
#else
        Debug.Log("[NetworkManager] Running in WebGL. Awaiting state from browser via JS SendMessage...");
#endif
    }

    // Called by frontend_web/app.js via SendMessage
    public void OnWebStateReceived(string json)
    {
        try
        {
            _latestState = JsonConvert.DeserializeObject<ServerState>(json);
            _hasNewState = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Failed to parse JSON from browser: {ex.Message}");
        }
    }

    async Task ConnectWebSocket()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                Debug.Log($"[NetworkManager] Connecting to {serverUrl}...");
                await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                Debug.Log("[NetworkManager] Connected!");

                await ReceiveLoop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] Connection failed: {ex.Message}. Retrying in 3s...");
                await Task.Delay(3000, _cts.Token);
            }
        }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[65536];

        while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(
                new ArraySegment<byte>(buffer), _cts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[NetworkManager] Server closed connection.");
                break;
            }

            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);

            try
            {
                _latestState = JsonConvert.DeserializeObject<ServerState>(json);
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
        if (!_hasNewState || _latestState == null) return;
        _hasNewState = false;

        Debug.Log($"[NetworkManager] Processing state for {_latestState.nodes?.Count} nodes. Map has {_nodeMap.Count} mapped nodes.");

        // 0. Synchronize the 3D nodes with the Python backend (add/remove/arrange)
        if (nodeSpawner != null)
        {
            _nodeMap = nodeSpawner.SyncNodes(_latestState.nodes, _nodeMap);
        }

        // 1. Update every node's HUD and Sphere Colors with live data
        foreach (var nodeData in _latestState.nodes)
        {
            if (_nodeMap.TryGetValue(nodeData.id, out GameObject nodeGO))
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

        // 2. Spawn new blocks (only once per unique round)
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

        // 3. Spawn real messages instantaneously the exact moment the block arrives
        if (_latestState.messages != null && messagePrefab != null)
        {
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

    Vector3 FindClosestCorner(GameObject node, Vector3 targetPos)
    {
        MeshFilter mf = node.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return node.transform.position;

        Vector3 bestCorner = node.transform.position;
        float bestDist = float.MaxValue;

        // Iterate through the mesh's physical corners to find the one pointing closest to the target
        foreach (Vector3 v in mf.sharedMesh.vertices)
        {
            Vector3 worldV = node.transform.TransformPoint(v);
            float d = Vector3.Distance(worldV, targetPos);
            if (d < bestDist)
            {
                bestDist = d;
                bestCorner = worldV;
            }
        }
        return bestCorner;
    }

    void SpawnMessage(MessageData msg)
    {
        if (!_nodeMap.TryGetValue(msg.from, out GameObject senderGO)) return;
        if (!_nodeMap.TryGetValue(msg.to, out GameObject receiverGO)) return;

        Vector3 rawStart = senderGO.transform.position;
        Vector3 rawEnd = receiverGO.transform.position;

        // Shoot the pipe out of the extreme corners of the shape, not the center!
        Vector3 startPos = FindClosestCorner(senderGO, rawEnd);
        Vector3 endPos = FindClosestCorner(receiverGO, rawStart);

        GameObject shard = Instantiate(messagePrefab, startPos, Quaternion.identity);
        shard.transform.localScale = Vector3.one * 0.075f; // Start small (halved)

        // Inject the ultra-sleek, highly polished Icosahedron mesh!
        MeshFilter mf = shard.GetComponent<MeshFilter>();
        if (mf != null) mf.mesh = NodeSpawner.CreateSleekCyberCore(0.6f, 1.0f);

        // Get the message color based on partition
        Color msgColor = Color.white;
        var animator = shard.GetComponent<MessageAnimator>();
        if (animator != null)
        {
            // Find sender's partition
            int partitionId = 0;
            if (_latestState != null && _latestState.nodes != null)
            {
                var senderNode = _latestState.nodes.Find(n => n.id == msg.from);
                if (senderNode != null) partitionId = senderNode.partition_id;
            }
            msgColor = animator.GetMessageColor(msg.type, partitionId);
        }

        // Apply color safely to the shard's material
        var shardRenderer = shard.GetComponent<Renderer>();
        if (shardRenderer != null)
        {
            // Try to create a fresh emissive URP Lit material for guaranteed glow
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null)
            {
                Material glowMat = new Material(urpLit);
                glowMat.SetColor("_BaseColor", msgColor);
                glowMat.SetFloat("_Smoothness", 0.9f);
                glowMat.EnableKeyword("_EMISSION");
                glowMat.SetColor("_EmissionColor", msgColor * 0.6f); // 50% of previous 1.2
                shardRenderer.material = glowMat;
            }
        }

        // Build a super skinny, transparent, non-glowing pipe
        GameObject pipe = new GameObject("MessagePipe");
        LineRenderer lr = pipe.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, startPos);
        lr.SetPosition(1, endPos);
        lr.startWidth = 0.015f; // Super skinny
        lr.endWidth = 0.015f;
        lr.material = new Material(Shader.Find("Sprites/Default")); // Unlit standard material
        lr.material.color = new Color(msgColor.r, msgColor.g, msgColor.b, 0.06f); // 6% alpha (60% of previous 10%)
        
        // Make the pipe actively flow
        pipe.AddComponent<PipeFlow>();
        
        // Safely add a sleek neon trail to the flying message shard
        TrailRenderer tr = shard.GetComponent<TrailRenderer>();
        if (tr == null)
        {
            try { tr = shard.AddComponent<TrailRenderer>(); } catch { tr = null; }
        }

        if (tr != null)
        {
            tr.time = 0.5f; // Fade out quickly
            tr.startWidth = 0.05f;
            tr.endWidth = 0.0f;
            
            Shader trailShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (trailShader == null) trailShader = Shader.Find("Sprites/Default");
            
            if (trailShader != null)
            {
                tr.material = new Material(trailShader);
                tr.material.color = new Color(msgColor.r, msgColor.g, msgColor.b, 0.5f);
            }
        }

        // INTERACTIVITY: Add a large hit box so you can click the flying block
        BoxCollider boxCol = shard.GetComponent<BoxCollider>();
        if (boxCol == null) boxCol = shard.AddComponent<BoxCollider>();
        boxCol.size = Vector3.one * 3f; // Slightly larger than visual block for easy clicking

        var interact = shard.GetComponent<MessageInteractable>();
        if (interact == null) interact = shard.AddComponent<MessageInteractable>();
        interact.msgType = msg.type;
        interact.senderId = msg.from;

        // Feed the ML tracking data from the sender into the message payload
        if (_latestState != null && _latestState.nodes != null)
        {
            var senderNode = _latestState.nodes.Find(n => n.id == msg.from);
            if (senderNode != null)
            {
                interact.mlThreat = senderNode.ml_prob;
                interact.mlFeatures = senderNode.ml_features;
            }
        }

        // Fly straight through the pipe (Speed reduced to 60% of previous: duration up to 7.33s)
        StartCoroutine(FlyMessageLinear(shard, pipe, startPos, endPos, 7.33f));
    }

    System.Collections.IEnumerator FlyMessageLinear(GameObject shard, GameObject pipe, Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (shard == null) break;
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Ease-in-out for smooth acceleration
            float smoothT = t * t * (3f - 2f * t);

            // Base position strictly along the straight pipe
            Vector3 basePos = Vector3.Lerp(from, to, smoothT);
            shard.transform.position = basePos;
            
            // Spin the crystal as it flies for a sci-fi data transfer effect
            shard.transform.Rotate(new Vector3(0.5f, 1.0f, 0.2f) * (360f * Time.deltaTime));

            // Pulse scale: grow in the middle, shrink at start and end (halved sizes)
            float scale = 0.05f + Mathf.Sin(t * Mathf.PI) * 0.075f;
            shard.transform.localScale = Vector3.one * scale;

            yield return null;
        }

        // Clean up the temporary pipe conduit
        if (pipe != null) Destroy(pipe);

        // Arrival flash: briefly scale up then destroy (halved size)
        if (shard != null)
        {
            shard.transform.localScale = Vector3.one * 0.2f;
            yield return new WaitForSeconds(0.05f);
            if (shard != null) Destroy(shard);
        }
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
    }
}