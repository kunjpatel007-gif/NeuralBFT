using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AUTO-BUILDS the entire Consensus Arena scene at runtime.
/// Attach only this script to an empty GameObject and press Play.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [Header("Settings")]
    public float ringRadius = 14f;
    public string serverUrl = "ws://localhost:8765";

    private Color accentNeon      = new Color(0f, 0.8f, 1f);
    private Color glassBg         = new Color(0.02f, 0.05f, 0.1f, 0.85f);
    private Color quarantinedColor = new Color(1f, 0.1f, 0.1f);

    void Awake()
    {
        BuildScene();
        Destroy(this);
    }

    // ── Shader helper (URP-safe) ──────────────────────────────
    Shader GetLitShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        return s != null ? s : Shader.Find("Standard");
    }

    // ─────────────────────────────────────────────────────────
    void BuildScene()
    {
        SetupCamera();
        SetupLighting();
        SetupArena();

        GameObject nodePrefab = CreateServerTowerPrefab();
        GameObject msgPrefab  = CreateDataPulsePrefab();

        NetworkManager nm = SetupNetworkManager(nodePrefab, msgPrefab);
        BuildUI(nm);
    }

    // ── 1. Camera ─────────────────────────────────────────────
    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.transform.position  = new Vector3(0, 22, -28);
        cam.transform.LookAt(Vector3.zero);
        cam.backgroundColor     = new Color(0.02f, 0.02f, 0.05f);
        cam.clearFlags          = CameraClearFlags.SolidColor;

        if (cam.GetComponent<CameraController>() == null)
            cam.gameObject.AddComponent<CameraController>();
    }

    // ── 2. Lighting ───────────────────────────────────────────
    void SetupLighting()
    {
        var go = new GameObject("Directional Light");
        var l  = go.AddComponent<Light>();
        l.type      = LightType.Directional;
        l.color     = new Color(0.4f, 0.8f, 1f);
        l.intensity = 0.6f;
        go.transform.rotation = Quaternion.Euler(45, -45, 0);
        RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.15f);
    }

    // ── 3. Arena ──────────────────────────────────────────────
    void SetupArena()
    {
        // Simple flat floor for testing
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position   = new Vector3(0, -1f, 0);
        floor.transform.localScale = new Vector3(ringRadius * 0.4f, 1f, ringRadius * 0.4f);
        var fm = new Material(GetLitShader());
        fm.color = new Color(0.2f, 0.2f, 0.2f);
        floor.GetComponent<Renderer>().material = fm;
    }

    // ── 4. Node Prefab ────────────────────────────────────────
    GameObject CreateServerTowerPrefab()
    {
        // Basic cube for testing
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
        node.name = "NodePrefab";
        node.transform.localScale = new Vector3(1f, 1f, 1f);

        var mat = new Material(GetLitShader());
        mat.color = Color.white;
        node.GetComponent<Renderer>().material = mat;

        var nv = node.AddComponent<NodeVisualizer>();

        // Floating label
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(node.transform);
        labelGo.transform.localPosition = new Vector3(0, 1.5f, 0);
        var tmp = labelGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 3f;
        tmp.color     = Color.white;
        tmp.text      = "node_?";
        nv.labelText  = tmp;

        node.SetActive(false);
        return node;
    }

    // ── 5. Message Prefab ─────────────────────────────────────
    GameObject CreateDataPulsePrefab()
    {
        // Basic sphere for messages, no trails
        var msg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        msg.name = "MsgPrefab";
        msg.transform.localScale = Vector3.one * 0.4f;
        Destroy(msg.GetComponent<Collider>());

        var mat = new Material(GetLitShader());
        mat.color = Color.cyan;
        msg.GetComponent<Renderer>().material = mat;

        msg.AddComponent<MessageAnimator>();

        msg.SetActive(false);
        return msg;
    }

    // ── 6. NetworkManager ─────────────────────────────────────
    NetworkManager SetupNetworkManager(GameObject nodePrefab, GameObject msgPrefab)
    {
        var go = new GameObject("NetworkManager");
        var nm = go.AddComponent<NetworkManager>();
        nm.serverUrl    = serverUrl;
        nm.nodePrefab   = nodePrefab;
        nm.messagePrefab = msgPrefab;
        nm.baseRingRadius = ringRadius;
        return nm;
    }

    // ── 7. UI ─────────────────────────────────────────────────
    void BuildUI(NetworkManager nm)
    {
        // Require EventSystem for buttons to work!
        if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // Canvas
        var canvasGo = new GameObject("HUD Canvas");
        var canvas   = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var ui = canvasGo.AddComponent<UIManager>();
        ui.networkManager = nm;

        // ── Top Bar ──
        var topBar = MakePanel(canvasGo.transform, "TopBar",
            new Vector2(0,1), new Vector2(1,1), new Vector2(0,-50), new Vector2(0,0), glassBg);

        // Connection dot
        var dotGo = new GameObject("Dot");
        dotGo.transform.SetParent(topBar.transform);
        var dot = dotGo.AddComponent<Image>();
        var dotRT = dotGo.GetComponent<RectTransform>();
        dotRT.anchorMin = dotRT.anchorMax = new Vector2(0, 0.5f);
        dotRT.pivot = new Vector2(0, 0.5f);
        dotRT.anchoredPosition = new Vector2(20, 0);
        dotRT.sizeDelta = new Vector2(14, 14);
        ui.connectionDot = dot;

        ui.connectionStatusText = MakeText(topBar.transform, "ConnStatus", "UPLINK ESTABLISHED", 15,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), TextAlignmentOptions.Left, accentNeon);

        ui.roundText = MakeText(topBar.transform, "Round", "Round: 0", 20,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), TextAlignmentOptions.Center, Color.white);

        ui.consensusText = MakeText(topBar.transform, "Consensus", "Consensus: PBFT", 18,
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-200, 0), TextAlignmentOptions.Right, accentNeon);

        ui.nodeCountText = MakeText(topBar.transform, "NodeCount", "Nodes: 10", 15,
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), TextAlignmentOptions.Right, Color.white);

        // ── Leaderboard ──
        var lbPanel = MakePanel(canvasGo.transform, "Leaderboard",
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, -175), new Vector2(230, 175), glassBg);

        ui.leaderboardText = MakeText(lbPanel.transform, "LbText", "...", 14,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, TextAlignmentOptions.Left, Color.white);
        ui.leaderboardText.GetComponent<RectTransform>().sizeDelta = new Vector2(210, 340);
        ui.leaderboardText.richText = true;

        // ── ML Detail Panel ──
        var detail = MakePanel(canvasGo.transform, "MLDetail",
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-310, -230), new Vector2(-10, 230), glassBg);
        ui.nodeDetailPanel = detail;
        detail.SetActive(false);

        var repBg = MakePanel(detail.transform, "RepBg",
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-120, -55), new Vector2(120, -40), new Color(0.2f,0.2f,0.2f));
        var repFill = MakePanel(repBg.transform, "RepFill",
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 15), accentNeon);
        repFill.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
        ui.reputationBarFill = repFill.GetComponent<Image>();
        ui.reputationBarFill.type       = Image.Type.Filled;
        ui.reputationBarFill.fillMethod = Image.FillMethod.Horizontal;

        ui.nodeIdText     = MakeText(detail.transform, "NId",  "TARGET: --",       22, half1, half1, new Vector2(0,-22),  TextAlignmentOptions.Center, Color.white);
        ui.nodeRepText    = MakeText(detail.transform, "NRep", "REPUTATION: 100",  15, half1, half1, new Vector2(0,-70),  TextAlignmentOptions.Center, Color.white);
        ui.nodeStatusText = MakeText(detail.transform, "NSt",  "STATUS: TRUSTED",  18, half1, half1, new Vector2(0,-95),  TextAlignmentOptions.Center, accentNeon);
        ui.mlProbText     = MakeText(detail.transform, "ML",   "THREAT LEVEL: 0%", 15, half1, half1, new Vector2(0,-130), TextAlignmentOptions.Center, Color.yellow);
        ui.mlMetricsText  = MakeText(detail.transform, "Tele", "Awaiting...",       13, half1, half1, new Vector2(0,-200), TextAlignmentOptions.Center, new Color(0.8f,0.8f,0.8f));
        ui.mlMetricsText.GetComponent<RectTransform>().sizeDelta = new Vector2(290, 160);
        ui.mlMetricsText.richText = true;

        // Stealth inject button inside detail panel
        var stealthBtn = MakeCyberButton(detail.transform, "Inject Stealth", new Vector2(0, -290), quarantinedColor);
        stealthBtn.onClick.AddListener(() => ui.InjectStealthFault());

        // ── Graphs ──
        var tpsGo = MakePanel(canvasGo.transform, "TPS_Graph",
            new Vector2(0.35f, 1), new Vector2(0.5f, 1), new Vector2(0,-120), new Vector2(0,-55), glassBg);
        ui.tpsGraph = tpsGo.GetComponent<RectTransform>();
        MakeText(tpsGo.transform, "TLbl", "TPS", 11,
            new Vector2(0,1), new Vector2(0,1), new Vector2(4,-4), TextAlignmentOptions.Left, accentNeon);

        var msgGo = MakePanel(canvasGo.transform, "MSG_Graph",
            new Vector2(0.51f, 1), new Vector2(0.65f, 1), new Vector2(0,-120), new Vector2(0,-55), glassBg);
        ui.msgGraph = msgGo.GetComponent<RectTransform>();
        MakeText(msgGo.transform, "MLbl", "MSGS", 11,
            new Vector2(0,1), new Vector2(0,1), new Vector2(4,-4), TextAlignmentOptions.Left, new Color(1f,0.8f,0f));

        // ── Block Ticker ──
        var tickerPanel = MakePanel(canvasGo.transform, "Ticker",
            new Vector2(0,0), new Vector2(1,0), new Vector2(0,70), new Vector2(0,108), glassBg);
        // Mask so text scrolls cleanly
        tickerPanel.AddComponent<RectMask2D>();
        ui.blockTickerText = MakeText(tickerPanel.transform, "TickerText", "AWAITING BLOCKS...", 17,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), TextAlignmentOptions.Left, accentNeon);
        ui.blockTickerText.GetComponent<RectTransform>().sizeDelta = new Vector2(6000, 36);

        // ── Bottom Controls ──
        var ctrl = MakePanel(canvasGo.transform, "Controls",
            new Vector2(0,0), new Vector2(1,0), new Vector2(0,0), new Vector2(0,70), glassBg);

        float x = -470;
        // Consensus buttons
        MakeLabelledButton(ctrl.transform, "PoW",  new Vector2(x,       0), new Color(1f,0.5f,0f), () => ui.SendConsensus("PoW"));
        MakeLabelledButton(ctrl.transform, "PoS",  new Vector2(x+100,   0), accentNeon,            () => ui.SendConsensus("PoS"));
        MakeLabelledButton(ctrl.transform, "DPoS", new Vector2(x+200,   0), new Color(0.8f,0.2f,1f), () => ui.SendConsensus("DPoS"));
        MakeLabelledButton(ctrl.transform, "PBFT", new Vector2(x+300,   0), new Color(0.2f,1f,0.4f), () => ui.SendConsensus("PBFT"));
        // Network controls
        MakeLabelledButton(ctrl.transform, "Sever Net", new Vector2(x+460, 0), quarantinedColor,    () => ui.ToggleNetworkSplit());
        MakeLabelledButton(ctrl.transform, "+ Node",    new Vector2(x+590, 0), accentNeon,            () => ui.SendAction("add_node"));
        MakeLabelledButton(ctrl.transform, "- Node",    new Vector2(x+690, 0), quarantinedColor,    () => ui.SendAction("remove_node"));
    }

    // ── Anchor shorthand ──────────────────────────────────────
    static readonly Vector2 half1 = new Vector2(0.5f, 1f);

    // ── UI Factory Helpers ────────────────────────────────────
    GameObject MakePanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        return go;
    }

    TextMeshProUGUI MakeText(Transform parent, string name, string text, float size,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos,
        TextAlignmentOptions align, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.alignment = align;
        tmp.color     = color;
        tmp.fontStyle = FontStyles.Bold;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot     = anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(300, 40);
        return tmp;
    }

    Button MakeCyberButton(Transform parent, string label, Vector2 pos, Color color)
    {
        var go  = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(color.r, color.g, color.b, 0.2f);
        go.AddComponent<Outline>().effectColor = color;

        var btn = go.AddComponent<Button>();
        var cb  = btn.colors;
        cb.highlightedColor = new Color(color.r, color.g, color.b, 0.7f);
        cb.pressedColor     = color;
        btn.colors = cb;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(100, 38);

        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(go.transform, false);
        var tmp   = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = label.ToUpper();
        tmp.fontSize  = 13f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = color;
        tmp.fontStyle = FontStyles.Bold;
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        return btn;
    }

    void MakeLabelledButton(Transform parent, string label, Vector2 pos, Color color, UnityEngine.Events.UnityAction action)
    {
        var btn = MakeCyberButton(parent, label, pos, color);
        btn.onClick.AddListener(action);
    }
}
