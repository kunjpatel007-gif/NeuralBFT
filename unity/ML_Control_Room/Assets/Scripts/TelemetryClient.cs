using UnityEngine;
using NativeWebSocket;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Connects to the Python backend WebSocket (port 8765) that also carries ML telemetry.
/// Receives the Random Forest tree structure (once) and live decision paths (every 2s).
/// Passes data to ForestRenderer for 3D visualization.
/// </summary>
public class TelemetryClient : MonoBehaviour
{
    [Header("Connection")]
    public string localUrl = "ws://127.0.0.1:8765"; // backend serves telemetry on the main port
    public string prodUrl = "wss://neuralbft-backend-443293282760.asia-south1.run.app";
    
    [HideInInspector]
    public string serverUrl;

    [Header("References")]
    public ForestRenderer forestRenderer;

    private WebSocket _ws;
    private int _treeSignature = 0;       // rebuild the 3D tree whenever the forest is retrained
    private float _reconnectAt = -1f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern string GetBrowserToken();
#endif

    async void Start()
    {
        // 1. Get Token Securely
        string token = "local_dev_token"; // fallback for Editor
#if UNITY_WEBGL && !UNITY_EDITOR
        try {
            token = GetBrowserToken();
        } catch {
            Debug.LogWarning("Failed to get token from browser bridge.");
        }
#endif

        // 2. Auto-Detect Architecture
        bool isLocal = Application.isEditor || Application.absoluteURL.Contains("localhost") || Application.absoluteURL.Contains("127.0.0.1") || Application.absoluteURL.Contains("file:");
        // A local page may never have visited the login screen; the backend's local default applies
        if (isLocal && string.IsNullOrEmpty(token)) token = "local_dev_token";
        // The token is sent as the first message after connecting, never in the URL (URLs end up in logs)
        serverUrl = isLocal ? localUrl : prodUrl;
        string authMessage = JsonConvert.SerializeObject(new { type = "auth", token });

        Debug.Log($"[Telemetry] Connecting to: {serverUrl}");
        _ws = new WebSocket(serverUrl);

        _ws.OnOpen += () =>
        {
            _ = _ws.SendText(authMessage);
            Debug.Log("<color=#00FF55><b>[ML CONTROL ROOM] Connected to Python Telemetry Server (Port 8765)!</b></color>");
        };

        _ws.OnError += (e) =>
        {
            Debug.LogError($"[ML ERROR] {e}");
        };

        _ws.OnClose += (e) =>
        {
            Debug.LogWarning("[ML CONTROL ROOM] Disconnected from Telemetry Server.");
            _treeSignature = 0; // Force rebuild on reconnect
            _reconnectAt = Time.time + 3f;
        };

        _ws.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);
            ProcessTelemetry(json);
        };

        await _ws.Connect();
    }

    async void Reconnect()
    {
        try { await _ws.Connect(); }
        catch (System.Exception ex) { Debug.LogWarning($"[ML] Reconnect failed: {ex.Message}"); _reconnectAt = Time.time + 3f; }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (_ws != null)
            _ws.DispatchMessageQueue();
#endif
        if (_ws != null && _reconnectAt > 0f && Time.time >= _reconnectAt && _ws.State == WebSocketState.Closed)
        {
            _reconnectAt = -1f;
            Reconnect();
        }
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
                    int sig = treeNodes.ToString(Newtonsoft.Json.Formatting.None).GetHashCode();
                    if (sig != _treeSignature)
                    {
                        forestRenderer.BuildTree(treeNodes);
                        _treeSignature = sig;
                        Debug.Log($"<color=#00AAFF>[ML] Built 3D tree with {treeNodes.Count} nodes</color>");
                    }
                }

                // Step 2: Animate live decision paths for all nodes
                JObject nodePaths = data["node_paths"] as JObject;
                if (nodePaths != null)
                {
                    forestRenderer.AnimatePaths(nodePaths, data["node_flags"] as JObject);
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
                        if (forestRenderer.TryGetNodePosition(treeNodeId, out Vector3 pos))
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

