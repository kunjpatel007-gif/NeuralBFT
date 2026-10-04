using UnityEngine;
using NativeWebSocket;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Connects to the Python ML Telemetry WebSocket (port 8766).
/// Receives the Random Forest tree structure (once) and live decision paths (every 2s).
/// Passes data to ForestRenderer for 3D visualization.
/// </summary>
public class TelemetryClient : MonoBehaviour
{
    [Header("Connection")]
    public string localUrl = "ws://127.0.0.1:8766?token=local_dev_token";
    public string prodUrl = "wss://your-prod-telemetry-url.run.app?token=prod_token";
    
    [HideInInspector]
    public string serverUrl;

    [Header("References")]
    public ForestRenderer forestRenderer;

    private WebSocket _ws;
    private bool _treeBuilt = false;

    async void Start()
    {
        // Auto-Detect Local vs Prod Architecture
        bool isLocal = Application.isEditor || Application.absoluteURL.Contains("localhost") || Application.absoluteURL.Contains("127.0.0.1");
        serverUrl = isLocal ? localUrl : prodUrl;
        
        Debug.Log($"[Telemetry] Connecting to: {serverUrl}");
        _ws = new WebSocket(serverUrl);

        _ws.OnOpen += () =>
        {
            Debug.Log("<color=#00FF55><b>[ML CONTROL ROOM] Connected to Python Telemetry Server (Port 8766)!</b></color>");
        };

        _ws.OnError += (e) =>
        {
            Debug.LogError($"[ML ERROR] {e}");
        };

        _ws.OnClose += (e) =>
        {
            Debug.LogWarning("[ML CONTROL ROOM] Disconnected from Telemetry Server.");
            _treeBuilt = false; // Force rebuild on reconnect
        };

        _ws.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);
            ProcessTelemetry(json);
        };

        await _ws.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (_ws != null)
            _ws.DispatchMessageQueue();
#endif
    }

    void ProcessTelemetry(string json)
    {
        try
        {
            JObject data = JObject.Parse(json);
            string msgType = data["type"]?.ToString() ?? "";

            if (msgType == "telemetry_update" && forestRenderer != null)
            {
                // Step 1: Build the 3D tree structure (only once, or after retrain)
                JArray treeNodes = data["tree_structure"] as JArray;
                if (treeNodes != null && treeNodes.Count > 0)
                {
                    if (!_treeBuilt)
                    {
                        forestRenderer.BuildTree(treeNodes);
                        _treeBuilt = true;
                        Debug.Log($"<color=#00AAFF>[ML] Built 3D tree with {treeNodes.Count} nodes</color>");
                    }
                }

                // Step 2: Animate live decision paths for all nodes
                JObject nodePaths = data["node_paths"] as JObject;
                if (nodePaths != null)
                {
                    forestRenderer.AnimatePaths(nodePaths);
                }
            }
            else if (msgType == "injection_shockwave" && forestRenderer != null)
            {
                string faultType = data["fault_type"]?.ToString() ?? "";
                JArray pathArray = data["path"] as JArray;
                
                if (pathArray != null && pathArray.Count > 0)
                {
                    System.Collections.Generic.List<Vector3> waypoints = new System.Collections.Generic.List<Vector3>();
                    foreach (var step in pathArray)
                    {
                        int treeNodeId = step.Value<int>();
                        Vector3 pos = forestRenderer.GetNodePosition(treeNodeId);
                        // Only add valid positions (y <= 0 is a quick hack to check validity if root is 0,0,0)
                        if (pos.y <= 0.1f) 
                            waypoints.Add(pos);
                    }
                    if (waypoints.Count > 1)
                    {
                        forestRenderer.TriggerShockwave(waypoints, faultType);
                        Debug.Log($"<color=#FF5500>[SHOCKWAVE] Triggered {faultType.ToUpper()} injection!</color>");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ML PARSE ERROR] {ex.Message}");
        }
    }

    private async void OnApplicationQuit()
    {
        if (_ws != null)
            await _ws.Close();
    }

    private async void OnDestroy()
    {
        if (_ws != null && _ws.State == WebSocketState.Open)
            await _ws.Close();
    }
}
