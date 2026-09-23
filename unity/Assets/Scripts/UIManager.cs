using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using System.Linq;

public class UIManager : MonoBehaviour
{
    [Header("References (assigned by SceneBootstrap)")]
    public NetworkManager networkManager;

    // Top HUD
    [HideInInspector] public TextMeshProUGUI roundText;
    [HideInInspector] public TextMeshProUGUI consensusText;
    [HideInInspector] public TextMeshProUGUI connectionStatusText;
    [HideInInspector] public TextMeshProUGUI nodeCountText;
    [HideInInspector] public Image connectionDot;

    // ML Dashboard
    [HideInInspector] public GameObject nodeDetailPanel;
    [HideInInspector] public TextMeshProUGUI nodeIdText;
    [HideInInspector] public TextMeshProUGUI nodeRepText;
    [HideInInspector] public TextMeshProUGUI nodeStatusText;
    [HideInInspector] public TextMeshProUGUI mlProbText;
    [HideInInspector] public TextMeshProUGUI mlMetricsText;
    [HideInInspector] public Image reputationBarFill;

    // Leaderboard & Ticker
    [HideInInspector] public TextMeshProUGUI leaderboardText;
    [HideInInspector] public TextMeshProUGUI blockTickerText;

    // Graphs
    [HideInInspector] public RectTransform tpsGraph;
    [HideInInspector] public RectTransform msgGraph;

    // Colors
    private Color trustedColor  = new Color(0.2f, 0.9f, 1f);
    private Color watchedColor  = new Color(1f, 0.8f, 0f);
    private Color quarantinedColor = new Color(1f, 0.1f, 0.1f);
    private Color connectedColor   = new Color(0.2f, 1f, 0.4f);
    private Color disconnectedColor = new Color(1f, 0.2f, 0.2f);

    private NodeData currentlySelectedNode = null;
    private bool isNetworkSplit = false;
    private float tickerOffset = 0f;

    private List<int> tpsHistory = new List<int>();
    private List<int> msgHistory = new List<int>();

    void Update()
    {
        UpdateHUD();
        HandleNodeClick();
        AnimateTicker();
    }

    void UpdateHUD()
    {
        if (networkManager == null) return;
        var state = networkManager.LatestState;
        if (state == null) return;

        bool connected = networkManager.IsConnected;
        if (connectionStatusText) connectionStatusText.text = connected ? "UPLINK ESTABLISHED" : "SIGNAL LOST";
        if (connectionDot) connectionDot.color = connected ? connectedColor : disconnectedColor;
        if (roundText) roundText.text = $"Round: {state.round}";
        if (consensusText) consensusText.text = $"Consensus: {state.consensus}";
        if (nodeCountText && state.nodes != null) nodeCountText.text = $"Nodes: {state.nodes.Length}";

        // Leaderboard
        if (state.nodes != null && leaderboardText != null)
        {
            var sorted = state.nodes.OrderByDescending(n => n.reputation).ToList();
            string lb = "<color=#00ffff><b>LIVE REPUTATION BOARD</b></color>\n\n";
            for (int i = 0; i < sorted.Count; i++)
            {
                var n = sorted[i];
                string hex = n.status == "Trusted" ? "#33ccff" : (n.status == "Watched" ? "#ffcc00" : "#ff3333");
                lb += $"<color={hex}>{i+1}. {n.id}  -  {n.reputation:F0}</color>\n";
            }
            leaderboardText.text = lb;
        }

        // Keep detail panel live
        if (currentlySelectedNode != null && state.nodes != null)
        {
            var updated = state.nodes.FirstOrDefault(n => n.id == currentlySelectedNode.id);
            if (updated != null) ShowNodeDetail(updated);
        }

        // Ticker
        if (state.blocks != null && blockTickerText != null)
        {
            string tickerStr = "";
            foreach (var b in state.blocks)
                tickerStr += $"[{b.hash}  |  Proposer: {b.proposer}  |  Tx: {b.tx_count}  |  {b.consensus}]     ";
            blockTickerText.text = tickerStr;
        }
    }

    // Called by NetworkManager after each state update
    public void PushMetrics(int tps, int msgs)
    {
        tpsHistory.Add(tps);
        msgHistory.Add(msgs);
        if (tpsHistory.Count > 30) tpsHistory.RemoveAt(0);
        if (msgHistory.Count > 30) msgHistory.RemoveAt(0);

        DrawGraph(tpsGraph, tpsHistory, new Color(0.2f, 1f, 0.4f), 500f);
        DrawGraph(msgGraph, msgHistory, new Color(1f, 0.8f, 0f), 3000f);
    }

    void DrawGraph(RectTransform container, List<int> data, Color color, float maxScale)
    {
        if (container == null) return;
        foreach (Transform child in container) Destroy(child.gameObject);

        float width  = container.rect.width / 30f;
        float height = container.rect.height;

        for (int i = 0; i < data.Count; i++)
        {
            GameObject bar = new GameObject("Bar");
            bar.transform.SetParent(container, false);
            Image img = bar.AddComponent<Image>();
            img.color = color;

            float normVal = Mathf.Clamp01((float)data[i] / maxScale);
            RectTransform rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot     = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(i * width, 0);
            rt.sizeDelta = new Vector2(width * 0.8f, Mathf.Max(2f, height * normVal));
        }
    }

    void AnimateTicker()
    {
        if (blockTickerText != null)
        {
            tickerOffset -= Time.deltaTime * 60f;
            if (tickerOffset < -1500f) tickerOffset = 0f;
            var rt = blockTickerText.rectTransform;
            rt.anchoredPosition = new Vector2(tickerOffset, rt.anchoredPosition.y);
        }
    }

    void HandleNodeClick()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var nv = hit.collider.GetComponentInParent<NodeVisualizer>();
                if (nv != null) ShowNodeDetail(nv.CurrentData);
            }
        }
    }

    public void ShowNodeDetail(NodeData data)
    {
        if (data == null || nodeDetailPanel == null) return;
        currentlySelectedNode = data;
        nodeDetailPanel.SetActive(true);

        if (nodeIdText)  nodeIdText.text  = $"TARGET: {data.id}";
        if (nodeRepText) nodeRepText.text = $"REPUTATION: {data.reputation:F1} / 100";
        if (nodeStatusText)
        {
            nodeStatusText.text  = $"STATUS: {data.status.ToUpper()}";
            nodeStatusText.color = data.status switch
            {
                "Verified"    => new Color(0f, 1f, 1f),
                "Trusted"     => trustedColor,
                "Watched"     => watchedColor,
                "High Risk"   => new Color(1f, 0.4f, 0f),
                "Quarantined" => quarantinedColor,
                "Blacklisted" => new Color(0.4f, 0f, 0f),
                _             => Color.white
            };
        }

        if (mlProbText)
        {
            float pct = data.ml_prob * 100f;
            mlProbText.text  = $"ML THREAT LEVEL: {pct:F1}%";
            mlProbText.color = pct > 70f ? quarantinedColor : (pct > 40f ? watchedColor : trustedColor);
        }

        if (mlMetricsText && data.ml_features != null)
        {
            string m = "<color=#aaaaaa>--- LIVE TELEMETRY ---</color>\n";
            if (data.ml_features.TryGetValue("msg_freq",          out float freq)) m += $"Msg Freq: {freq:F1} Hz\n";
            if (data.ml_features.TryGetValue("vote_inconsistency", out float vi))  m += $"Vote Drift: {vi*100f:F1}%\n";
            if (data.ml_features.TryGetValue("latency",           out float lat))  m += $"Ping: {lat:F0} ms\n";
            if (data.ml_features.TryGetValue("fork_attempts",     out float fa))   m += $"Forks: {fa}\n";
            if (data.ml_features.TryGetValue("silence_ratio",     out float sr))   m += $"Silence: {sr*100f:F1}%\n";
            mlMetricsText.text = m;
        }
        else if (mlMetricsText)
        {
            mlMetricsText.text = "Awaiting ML Telemetry...";
        }

        if (reputationBarFill)
        {
            reputationBarFill.fillAmount = data.reputation / 100f;
            reputationBarFill.color = nodeStatusText != null ? nodeStatusText.color : Color.white;
        }
    }

    // ── Button Callbacks ──
    public void SendConsensus(string consensus)
    {
        if (networkManager) networkManager.SendToServer($"{{\"action\":\"switch_consensus\",\"consensus\":\"{consensus}\"}}");
    }

    public void SendAction(string action)
    {
        if (networkManager) networkManager.SendToServer($"{{\"action\":\"{action}\"}}");
    }

    public void InjectStealthFault()
    {
        if (currentlySelectedNode != null && networkManager)
            networkManager.SendToServer($"{{\"action\":\"inject_fault\",\"node_id\":\"{currentlySelectedNode.id}\",\"fault_type\":\"stealth\"}}");
    }

    public void ToggleNetworkSplit()
    {
        isNetworkSplit = !isNetworkSplit;
        SendAction(isNetworkSplit ? "split_network" : "merge_network");
    }
}
