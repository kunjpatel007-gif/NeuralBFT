using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public class ForestBillboard : MonoBehaviour
{
    private static Camera _cam;
    public static Camera GetCamera()
    {
        if (_cam == null) _cam = Camera.main;
        return _cam;
    }
    void LateUpdate()
    {
        Camera cam = GetCamera();
        if (cam == null) return;
        // Turn toward the camera's position, upright to the view, so labels face you as you orbit
        Vector3 away = transform.position - cam.transform.position;
        transform.rotation = away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away, cam.transform.up) : cam.transform.rotation;
    }
}

/// <summary>Turns an object slowly about its local Y axis.</summary>
public class ForestSpin : MonoBehaviour
{
    public float degreesPerSecond = 6f;
    void Update() { transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self); }
}

/// <summary>
/// Draws the Random Forest's decision tree as a machined sculpture under studio light.
///  - Every decision is a faceted anodised solid in the colour of the feature it tests, wearing a
///    fine titanium collar square to its branch. It clicks round a fifth of a turn whenever a
///    sample passes through it.
///  - Branches are satin-titanium tubes whose thickness follows how much training data flows
///    through them, so the main routes read at a glance, like a river delta.
///  - Leaves are coloured by their verdict (green honest .. red Byzantine) and sized by how much
///    data ends there; each one swells slightly as samples arrive and grows with its visit count.
///  - The tree stands on a two-tier plinth with turning rings, with a feature legend on the floor.
/// Each network node's sample travels its decision path as a small polished shard: off-white for
/// honest nodes, muted red for Byzantine ones. The tree grows in when built and sways very slightly.
/// </summary>
public class ForestRenderer : MonoBehaviour
{
    [Header("Tree Layout")]
    public float horizontalSpacing = 2.5f;
    public float verticalSpacing = 3.0f;
    public float nodeRadius = 0.4f;

    [Header("Materials (legacy)")]
    [Tooltip("No longer used: the tree is drawn with the Arena shaders. Kept so existing scenes keep their references.")]
    public Material decisionNodeMat;
    public Material leafNodeMat;
    public Material branchMat;
    public Material pulseTrailMat;

    private struct TreeNodeData
    {
        public int id, left, right;
        public string feature;
        public float threshold;
        public bool isLeaf;
        public int samples;      // training samples reaching this node (0 if the backend does not send it)
        public float byzantine;  // share of them that are Byzantine, or -1 if unknown
    }

    // A decision or leaf that reacts when a sample passes through it
    private class NodeMotion
    {
        public Transform body, collar;
        public Quaternion rest;
        public float angle, targetAngle; // the body clicks round in steps
        public float swell;              // brief size bump when a sample arrives (leaves)
        public int visits;
        public float baseScale;
        public bool isLeaf;
    }

    private struct BranchCurve
    {
        public Vector3 from, to;
        public Vector3[] points;
    }

    private readonly Dictionary<int, GameObject> _nodeObjects = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, Vector3> _nodePositions = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, TreeNodeData> _treeData = new Dictionary<int, TreeNodeData>();
    private readonly Dictionary<int, int> _nodeDepth = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector3> _nodeDir = new Dictionary<int, Vector3>();
    private readonly List<BranchCurve> _branchCurves = new List<BranchCurve>();
    private readonly Dictionary<int, NodeMotion> _motion = new Dictionary<int, NodeMotion>();
    private readonly List<Transform> _spinners = new List<Transform>();
    private readonly List<float> _spinSpeeds = new List<float>();
    private int _rootSamples;
    private GameObject _treeRoot;

    private int _maxDepth, _leafCount, _labelMaxDepth = int.MaxValue;
    private float _treeScale = 1f, _decay = 0.78f, _spreadAngle = 30f, _nodeScale = 1f;
    private float _growth = 1f; // 0..1 while a freshly built tree grows in

    private const int BezierSegments = 12;
    private const int TwigSegmentBudget = 3000;
    private const float GrowSeconds = 1.6f;

    // Palette
    private static readonly Color LeafBud     = ArenaTheme.Line;
    private static readonly Color HonestPulse = ArenaTheme.TextPrimary;
    private static readonly Color ByzantinePulse = ArenaTheme.Quarantined;

    private Mesh _twigMesh, _branchMesh;
    private Mesh _plinthMesh, _plinthTopMesh;
    private static Mesh _nodeMesh, _ringMesh;
    private readonly ArenaGeometry.TubeBuilder _branches = new ArenaGeometry.TubeBuilder();
    private MaterialPropertyBlock _block;
    private Transform _dustRoot;
    private ParticleSystem _dustPs;

    void Awake()
    {
        _block = new MaterialPropertyBlock();
        CreateAmbientDust();
    }

    void OnDestroy()
    {
        if (_twigMesh != null) Destroy(_twigMesh);
        if (_branchMesh != null) Destroy(_branchMesh);
        if (_plinthMesh != null) Destroy(_plinthMesh);
        if (_plinthTopMesh != null) Destroy(_plinthTopMesh);
    }

    static Mesh NodeMesh => _nodeMesh != null ? _nodeMesh : (_nodeMesh = ArenaGeometry.ChamferedIcosahedron(0.5f, 1.15f));

    void Update()
    {
        if (_treeRoot == null) return;

        // Grow in, then sway a fraction of a degree so the tree never looks frozen
        if (_growth < 1f)
        {
            _growth = Mathf.Min(1f, _growth + Time.deltaTime / GrowSeconds);
            _treeRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.02f, 1f, ArenaFX.EaseOutCubic(_growth));
        }

        float time = Time.time;
        _treeRoot.transform.localRotation = Quaternion.Euler(
            Mathf.Sin(time * 0.31f) * 0.5f, 0f, Mathf.Sin(time * 0.23f + 1.7f) * 0.5f);

        float dt = Time.deltaTime;
        for (int i = 0; i < _spinners.Count; i++)
            if (_spinners[i] != null) _spinners[i].Rotate(0f, _spinSpeeds[i] * dt, 0f, Space.Self);

        float ease = ArenaFX.Damp(7f, dt);
        foreach (NodeMotion m in _motion.Values)
        {
            if (m.body == null) continue;
            if (Mathf.Abs(m.targetAngle - m.angle) > 0.05f)
            {
                m.angle = Mathf.Lerp(m.angle, m.targetAngle, ease);
                m.body.localRotation = m.rest * Quaternion.Euler(0f, m.angle, 0f);
            }
            if (m.swell > 0.001f || m.isLeaf)
            {
                m.swell = Mathf.Max(0f, m.swell - dt * 1.6f);
                float grown = m.isLeaf ? 1f + 0.22f * Mathf.Log10(1f + m.visits) : 1f;
                float bump = 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(m.swell) * Mathf.PI);
                m.body.localScale = Vector3.one * (m.baseScale * grown * bump);
            }
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void BuildTree(JArray treeNodes)
    {
        if (_treeRoot != null) Destroy(_treeRoot);
        if (_twigMesh != null) { Destroy(_twigMesh); _twigMesh = null; }
        if (_branchMesh != null) { Destroy(_branchMesh); _branchMesh = null; }
        _branches.Clear();

        _nodeObjects.Clear(); _nodePositions.Clear(); _treeData.Clear();
        _nodeDepth.Clear(); _nodeDir.Clear(); _branchCurves.Clear();
        _motion.Clear(); _spinners.Clear(); _spinSpeeds.Clear();

        _treeRoot = new GameObject("RandomForest_Tree");
        _treeRoot.transform.SetParent(transform, false);
        _treeRoot.transform.localPosition = Vector3.zero;
        _treeRoot.transform.localScale = Vector3.one * 0.02f;
        _growth = 0f;

        foreach (JObject node in treeNodes)
        {
            TreeNodeData tnd = new TreeNodeData
            {
                id = node["id"]?.Value<int>() ?? 0,
                left = node["left"]?.Value<int>() ?? -1,
                right = node["right"]?.Value<int>() ?? -1,
                feature = node["feature"]?.ToString() ?? "LEAF",
                threshold = node["threshold"]?.Value<float>() ?? 0f,
                isLeaf = node["is_leaf"]?.Value<bool>() ?? true,
                samples = node["samples"]?.Value<int>() ?? 0,
                byzantine = node["byzantine"] != null ? node["byzantine"].Value<float>() : -1f
            };
            _treeData[tnd.id] = tnd;
        }
        if (!_treeData.ContainsKey(0)) return;
        _rootSamples = Mathf.Max(0, _treeData[0].samples);

        _maxDepth = 0; _leafCount = 0;
        MeasureTree(0, 0);
        ConfigureForNodeCount();

        Vector3 rootPos = new Vector3(0f, verticalSpacing * _treeScale * 1.6f, 0f);
        CalculatePositions(0, 0, rootPos, Vector3.up);
        CenterDustOnTree();
        CreateBase();
        CreateLegend();
        CreateTrunk(rootPos);

        foreach (var kvp in _treeData)
        {
            TreeNodeData tnd = kvp.Value;
            if (!_nodePositions.ContainsKey(tnd.id)) continue;

            Vector3 pos = _nodePositions[tnd.id];
            int depth = _nodeDepth[tnd.id];

            if (tnd.isLeaf) { _nodeObjects[tnd.id] = CreateLeafNode(tnd, pos); continue; }
            _nodeObjects[tnd.id] = CreateDecisionNode(tnd, pos, tnd.id == 0, depth);

            if (tnd.left >= 0 && _nodePositions.ContainsKey(tnd.left)) CreateOrganicBranch(pos, _nodePositions[tnd.left], depth, tnd.left);
            if (tnd.right >= 0 && _nodePositions.ContainsKey(tnd.right)) CreateOrganicBranch(pos, _nodePositions[tnd.right], depth, tnd.right);
        }
        BuildBranchMesh();
        BuildTwigs();
    }

    public Vector3 GetNodePosition(int nodeId) { return _nodePositions.TryGetValue(nodeId, out Vector3 pos) ? pos : Vector3.zero; }

    public bool TryGetNodePosition(int nodeId, out Vector3 pos) { return _nodePositions.TryGetValue(nodeId, out pos); }

    /// <summary>Sends one dot per network node along that node's current decision path.</summary>
    public void AnimatePaths(JObject nodePaths, JObject nodeFlags = null)
    {
        if (_treeRoot == null) return;

        foreach (var kvp in nodePaths)
        {
            string nodeId = kvp.Key;
            JArray pathArray = kvp.Value as JArray;
            if (pathArray == null || pathArray.Count == 0) continue;

            var waypoints = new List<Vector3>();
            var ids = new List<int>();
            foreach (var step in pathArray)
            {
                int treeNodeId = step.Value<int>();
                if (_nodePositions.ContainsKey(treeNodeId)) { waypoints.Add(_nodePositions[treeNodeId]); ids.Add(treeNodeId); }
            }
            if (waypoints.Count < 2) continue;

            // Ground truth from the backend (node_flags); no random colouring
            bool isByzantine = false;
            if (nodeFlags != null && nodeFlags.TryGetValue(nodeId, out JToken flag) && flag.Type == JTokenType.Boolean)
                isByzantine = flag.Value<bool>();

            // Each node keeps its own pace, so the dots spread out along the tree
            float secondsPerBranch = 1.333f + Mathf.Abs(nodeId.GetHashCode() % 5) * 0.177f;
            SpawnPulse($"Pulse_{nodeId}", waypoints, ids, isByzantine ? ByzantinePulse : HonestPulse,
                       0.4f * _nodeScale, secondsPerBranch, isByzantine);
        }
    }

    /// <summary>A fault was injected: a slightly larger, faster dot runs down the affected path.</summary>
    public void TriggerShockwave(List<Vector3> waypoints, string faultType)
    {
        if (_treeRoot == null || waypoints == null || waypoints.Count < 2) return;
        SpawnPulse($"Shockwave_{faultType}", waypoints, IdsAt(waypoints), GetFaultColor(faultType), 0.8f * _nodeScale, 1.777f / 4f, false);
    }

    // Finds the tree node at each position (for callers that only have positions)
    List<int> IdsAt(List<Vector3> positions)
    {
        var ids = new List<int>(positions.Count);
        foreach (Vector3 p in positions)
        {
            int best = -1; float bestDistance = 1e-3f;
            foreach (var kvp in _nodePositions)
            {
                float d = (kvp.Value - p).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = kvp.Key; }
            }
            ids.Add(best);
        }
        return ids;
    }

    // A sample has reached the k-th node of its path
    void OnPulseReachedNode(List<int> ids, int k)
    {
        if (ids == null || k < 0 || k >= ids.Count) return;
        if (!_motion.TryGetValue(ids[k], out NodeMotion m)) return;
        if (m.isLeaf) { m.visits++; m.swell = 1f; }
        else m.targetAngle += 72f; // a fifth of a turn, eased: the decision visibly "clicks" as a sample passes
    }

    void SpawnPulse(string objectName, List<Vector3> waypoints, List<int> ids, Color color, float size, float secondsPerBranch, bool isByzantine)
    {
        List<Vector3> curved = ExpandAlongBranches(waypoints);

        var pulseObj = new GameObject(objectName);
        pulseObj.transform.SetParent(_treeRoot.transform, false);
        pulseObj.transform.localPosition = curved[0];

        DataPulse pulse = pulseObj.AddComponent<DataPulse>();
        pulse.Configure(color, size, BezierSegments);
        pulse.Initialize(curved, secondsPerBranch / BezierSegments, isByzantine);
        pulse.onNodeReached = k => OnPulseReachedNode(ids, k);
    }

    static Color GetFaultColor(string faultType)
    {
        switch ((faultType ?? string.Empty).ToLowerInvariant())
        {
            case "state_tampering": return new Color(0.82f, 0.50f, 0.32f); // muted orange
            case "stealth":
            case "eclipse":         return ArenaTheme.Watched;             // amber
            default:                return ArenaTheme.Quarantined;         // muted red (spam, ddos, others)
        }
    }

    /// <summary>One colour per feature, so you can see at a glance what each part of the tree tests.</summary>
    static Color FeatureColor(string raw)
    {
        string s = (raw ?? string.Empty).ToLowerInvariant();
        if (s.Contains("msg_freq"))    return ArenaTheme.Verified;            // steel blue
        if (s.Contains("vote_incons")) return new Color(0.60f, 0.58f, 0.76f); // muted violet
        if (s.Contains("latency"))     return ArenaTheme.Watched;             // amber
        if (s.Contains("fork"))        return ArenaTheme.Quarantined;         // muted red
        if (s.Contains("silence"))     return ArenaTheme.Trusted;             // muted green
        return ArenaTheme.Line;
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    void MeasureTree(int id, int depth)
    {
        if (!_treeData.TryGetValue(id, out TreeNodeData t)) return;
        _maxDepth = Mathf.Max(_maxDepth, depth);
        if (t.isLeaf) { _leafCount++; return; }
        MeasureTree(t.left, depth + 1);
        MeasureTree(t.right, depth + 1);
    }

    void ConfigureForNodeCount()
    {
        int n = _treeData.Count;
        _treeScale = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(_leafCount, 1)) / 3f, 0.8f, 3f);
        _decay = Mathf.Lerp(0.78f, 0.9f, Mathf.InverseLerp(5f, 14f, _maxDepth));
        _spreadAngle = Mathf.Clamp(Mathf.Atan2(horizontalSpacing, verticalSpacing) * Mathf.Rad2Deg * 0.75f, 20f, 40f);
        _nodeScale = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(30f, 300f, n)) * Mathf.Sqrt(_treeScale);
        _labelMaxDepth = n <= 40 ? int.MaxValue : (n <= 120 ? 4 : 2);
    }

    float SegmentLength(int depth) { return verticalSpacing * _treeScale * Mathf.Pow(_decay, depth); }

    void CalculatePositions(int nodeId, int depth, Vector3 pos, Vector3 dir)
    {
        if (!_treeData.ContainsKey(nodeId)) return;
        TreeNodeData tnd = _treeData[nodeId];

        _nodePositions[nodeId] = pos; _nodeDepth[nodeId] = depth; _nodeDir[nodeId] = dir;
        if (tnd.isLeaf) return;

        float leftWidth = GetTreeWidth(tnd.left), rightWidth = GetTreeWidth(tnd.right);
        float total = Mathf.Max(leftWidth + rightWidth, 1f);
        Vector3 perp = Quaternion.AngleAxis(depth * 137.5f + nodeId * 47f, dir) * PerpendicularTo(dir);
        float baseLen = SegmentLength(depth), upBias = depth < 4 ? 0.08f : 0.03f;

        if (tnd.left >= 0)
        {
            Vector3 d = Vector3.Slerp(Quaternion.AngleAxis(_spreadAngle, perp) * dir, Vector3.up, upBias).normalized;
            float len = baseLen * Mathf.Lerp(0.8f, 1.25f, leftWidth / total);
            CalculatePositions(tnd.left, depth + 1, pos + d * len, d);
        }
        if (tnd.right >= 0)
        {
            Vector3 d = Vector3.Slerp(Quaternion.AngleAxis(-_spreadAngle, perp) * dir, Vector3.up, upBias).normalized;
            float len = baseLen * Mathf.Lerp(0.8f, 1.25f, rightWidth / total);
            CalculatePositions(tnd.right, depth + 1, pos + d * len, d);
        }
    }

    float GetTreeWidth(int nodeId)
    {
        if (!_treeData.ContainsKey(nodeId)) return 1f;
        TreeNodeData tnd = _treeData[nodeId];
        if (tnd.isLeaf) return 1f;
        return GetTreeWidth(tnd.left) + GetTreeWidth(tnd.right);
    }

    static Vector3 PerpendicularTo(Vector3 dir)
    {
        Vector3 p = Vector3.Cross(dir, Vector3.forward);
        if (p.sqrMagnitude < 0.01f) p = Vector3.Cross(dir, Vector3.right);
        return p.normalized;
    }

    static float Hash01(int seed) { float h = Mathf.Sin(seed * 12.9898f) * 43758.5453f; return h - Mathf.Floor(h); }

    static string FormatFeatureName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string s = raw.ToLower();
        if (s.Contains("msg_freq")) return "Message Rate";
        if (s.Contains("vote_incons")) return "Vote Conflict";
        if (s.Contains("latency")) return "Latency";
        if (s.Contains("fork")) return "Fork Attempts";
        if (s.Contains("silence")) return "Silence Ratio";
        if (s == "leaf" || s == "-2") return "";

        // Fallback: replace underscores with spaces and capitalize
        string clean = raw.Replace("_", " ");
        if (clean.Length > 1) return char.ToUpper(clean[0]) + clean.Substring(1);
        return clean;
    }

    // ------------------------------------------------------------------
    // Tree parts
    // ------------------------------------------------------------------

    GameObject CreateDecisionNode(TreeNodeData tnd, Vector3 pos, bool isRoot, int depth)
    {
        float r = nodeRadius * _nodeScale * (isRoot ? 1.5f : 1f);
        Color color = FeatureColor(tnd.feature);

        GameObject node = new GameObject($"Decision_{tnd.id}");
        node.transform.SetParent(_treeRoot.transform, false);
        node.transform.localPosition = pos;

        // A faceted anodised solid, turned at random so no two catch the light the same way
        MeshRenderer crystal = ArenaMaterials.Create(node.transform, "Crystal", NodeMesh, ArenaMaterials.Anodized);
        crystal.transform.localScale = Vector3.one * (r * 1.7f);
        crystal.transform.localRotation = Quaternion.Euler(Hash01(tnd.id * 3) * 360f, Hash01(tnd.id * 5) * 360f, 0f);
        _block.Clear();
        ArenaMaterials.Tint(_block, color);
        crystal.SetPropertyBlock(_block);

        if (_ringMesh == null) _ringMesh = ArenaGeometry.Torus(1f, 0.012f, 128, 10);
        var motion = new NodeMotion { body = crystal.transform, rest = crystal.transform.localRotation, baseScale = r * 1.7f };
        _motion[tnd.id] = motion;

        if (isRoot)
        {
            // The root is a small gimbal: three fine rings at different tilts, turning at their own pace
            float[] tilts = { 12f, 64f, -38f };
            float[] speeds = { 6f, -9f, 4f };
            for (int i = 0; i < tilts.Length; i++)
            {
                Transform gimbal = new GameObject("RootGimbal_" + i).transform;
                gimbal.SetParent(node.transform, false);
                gimbal.localRotation = Quaternion.Euler(tilts[i], i * 47f, 0f);
                MeshRenderer ring = ArenaMaterials.Create(gimbal, "Ring", _ringMesh, ArenaMaterials.Titanium, false);
                ring.transform.localScale = Vector3.one * (r * (1.55f + 0.12f * i));
                _spinners.Add(ring.transform);
                _spinSpeeds.Add(speeds[i]);
            }
        }
        else if (depth <= 6)
        {
            // A fine collar square to the incoming branch, like a machined joint
            Vector3 dir = _nodeDir.TryGetValue(tnd.id, out Vector3 d) ? d : Vector3.up;
            MeshRenderer collar = ArenaMaterials.Create(node.transform, "Collar", _ringMesh, ArenaMaterials.Titanium, false);
            collar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            collar.transform.localScale = Vector3.one * (r * 1.25f);
            motion.collar = collar.transform;
        }

        if (depth <= _labelMaxDepth)
        {
            string niceName = FormatFeatureName(tnd.feature);
            if (!string.IsNullOrEmpty(niceName))
            {
                string muted = ArenaTheme.Hex(ArenaTheme.TextMuted);
                string flow = tnd.samples > 0 ? $"  \u00B7  {tnd.samples} samples" : "";
                CreateLabel(node, $"{niceName}\n<color=#{muted}>below {tnd.threshold:F2}{flow}</color>", r);
            }
        }
        return node;
    }

    GameObject CreateLeafNode(TreeNodeData tnd, Vector3 pos)
    {
        GameObject leaf = new GameObject($"Leaf_{tnd.id}");
        leaf.transform.SetParent(_treeRoot.transform, false);
        leaf.transform.localPosition = pos;

        // A small pale bud where each path ends
        // Sized by how much training data ends here (relative to an average leaf)
        float share = 1f;
        if (_rootSamples > 0 && tnd.samples > 0)
            share = Mathf.Clamp(Mathf.Sqrt(tnd.samples / (float)_rootSamples * Mathf.Max(_leafCount, 1)), 0.6f, 2.0f);
        float size = 0.22f * _nodeScale * share;

        MeshRenderer bud = ArenaMaterials.Create(leaf.transform, "Bud", NodeMesh, ArenaMaterials.Anodized, false);
        bud.transform.localScale = Vector3.one * size;
        bud.transform.localRotation = Quaternion.Euler(Hash01(tnd.id * 11) * 360f, Hash01(tnd.id * 17) * 360f, 0f);
        _block.Clear();
        ArenaMaterials.Tint(_block, LeafColor(tnd.byzantine));
        bud.SetPropertyBlock(_block);

        _motion[tnd.id] = new NodeMotion { body = bud.transform, rest = bud.transform.localRotation, baseScale = size, isLeaf = true };
        return leaf;
    }

    // The leaf's verdict: green where the training data was honest, red where it was Byzantine
    static Color LeafColor(float byzantine)
    {
        if (byzantine < 0f) return LeafBud;
        return byzantine < 0.5f
            ? Color.Lerp(ArenaTheme.Trusted, ArenaTheme.Watched, Mathf.SmoothStep(0f, 1f, byzantine * 2f))
            : Color.Lerp(ArenaTheme.Watched, ArenaTheme.Quarantined, Mathf.SmoothStep(0f, 1f, (byzantine - 0.5f) * 2f));
    }

    void CreateBase()
    {
        float radius = 1.6f * Mathf.Sqrt(_treeScale);

        // A low machined plinth the trunk rises from, ringed with a fine titanium band
        if (_plinthMesh != null) Destroy(_plinthMesh);
        _plinthMesh = ArenaGeometry.BevelledHex(radius, 0.12f, 0.025f);
        MeshRenderer plinth = ArenaMaterials.Create(_treeRoot.transform, "Plinth", _plinthMesh, ArenaMaterials.Steel);
        plinth.transform.localPosition = new Vector3(0f, 0.06f, 0f);

        if (_ringMesh == null) _ringMesh = ArenaGeometry.Torus(1f, 0.012f, 128, 10);
        // A second, smaller tier in graphite ceramic
        if (_plinthTopMesh != null) Destroy(_plinthTopMesh);
        _plinthTopMesh = ArenaGeometry.BevelledHex(radius * 0.62f, 0.1f, 0.02f);
        MeshRenderer top = ArenaMaterials.Create(_treeRoot.transform, "PlinthTop", _plinthTopMesh, ArenaMaterials.Ceramic);
        top.transform.localPosition = new Vector3(0f, 0.17f, 0f);
        top.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
        _block.Clear();
        ArenaMaterials.Tint(_block, new Color(0.16f, 0.165f, 0.175f));
        top.SetPropertyBlock(_block);

        // Two fine floor rings turning slowly in opposite directions
        for (int i = 0; i < 2; i++)
        {
            MeshRenderer ring = ArenaMaterials.Create(_treeRoot.transform, "BaseRing_" + i, _ringMesh, ArenaMaterials.Titanium, false);
            ring.transform.localScale = Vector3.one * (radius * (1.45f + 0.18f * i));
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            _spinners.Add(ring.transform);
            _spinSpeeds.Add(i == 0 ? 3f : -2f);
        }
    }

    // A quiet key on the floor in front of the tree: what each colour means
    void CreateLegend()
    {
        var entries = new List<(string, Color)>
        {
            ("MESSAGE RATE", FeatureColor("msg_freq")),
            ("VOTE CONFLICT", FeatureColor("vote_incons")),
            ("LATENCY", FeatureColor("latency")),
            ("FORK ATTEMPTS", FeatureColor("fork")),
            ("SILENCE RATIO", FeatureColor("silence")),
            ("HONEST LEAF", LeafColor(0f)),
            ("BYZANTINE LEAF", LeafColor(1f)),
        };

        Transform legend = new GameObject("Legend").transform;
        legend.SetParent(_treeRoot.transform, false);

        float radius = 1.6f * Mathf.Sqrt(_treeScale) * 2.6f;
        float span = 70f; // degrees of arc, centred in front of the tree (toward -Z)
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < entries.Count; i++)
        {
            float angle = Mathf.Lerp(-span * 0.5f, span * 0.5f, i / (float)(entries.Count - 1)) * Mathf.Deg2Rad;
            Vector3 at = new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * radius;
            Quaternion facing = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);

            // A small chip sitting on the floor
            MeshRenderer chip = ArenaMaterials.Create(legend, "Chip", NodeMesh, ArenaMaterials.Anodized);
            chip.transform.localPosition = at + Vector3.up * 0.13f;
            chip.transform.localRotation = Quaternion.Euler(Hash01(i * 7) * 360f, Hash01(i * 13) * 360f, 0f);
            chip.transform.localScale = Vector3.one * 0.26f;
            _block.Clear();
            ArenaMaterials.Tint(_block, entries[i].Item2);
            chip.SetPropertyBlock(_block);

            // Its name engraved flat on the floor just in front of it
            var label = new GameObject("LegendLabel");
            label.transform.SetParent(legend, false);
            label.transform.localPosition = at + facing * new Vector3(0f, 0.004f, -0.42f);
            label.transform.localRotation = facing * Quaternion.Euler(90f, 0f, 0f);
            TextMesh tm = label.AddComponent<TextMesh>();
            if (font != null)
            {
                tm.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            tm.text = entries[i].Item1;
            tm.characterSize = 0.03f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = ArenaTheme.TextMuted;
        }
    }

    void CreateTrunk(Vector3 rootPos)
    {
        float tw = Mathf.Sqrt(_treeScale);
        var points = new List<Vector3>();
        for (int i = 0; i <= 8; i++) points.Add(Vector3.Lerp(new Vector3(0f, 0.1f, 0f), rootPos, i / 8f));
        _branches.Add(points, 0.13f * tw, 0.085f * tw, 16);

        // Three machined bands up the trunk
        for (int i = 1; i <= 3; i++)
        {
            Vector3 c = Vector3.Lerp(new Vector3(0f, 0.1f, 0f), rootPos, i / 4.2f);
            float band = Mathf.Lerp(0.13f, 0.085f, i / 4.2f) * tw * 1.35f;
            _branches.Add(new[] { c - Vector3.up * 0.03f * tw, c + Vector3.up * 0.03f * tw }, band, band, 16);
        }
    }

    // Branch thickness: follows the share of training data flowing through the child node when
    // the backend sends it, otherwise thins out with depth like a real canopy
    float BranchRadius(int childId, int depth)
    {
        float trunk = 0.085f * Mathf.Sqrt(_treeScale);
        if (_rootSamples > 0 && _treeData.TryGetValue(childId, out TreeNodeData child) && child.samples > 0)
            return Mathf.Max(0.014f, trunk * Mathf.Lerp(0.16f, 1f, Mathf.Sqrt(child.samples / (float)_rootSamples)));
        return Mathf.Max(0.014f, trunk * Mathf.Pow(0.74f, depth));
    }

    void CreateOrganicBranch(Vector3 from, Vector3 to, int depth, int childId)
    {
        float length = (to - from).magnitude;
        if (length < 0.001f) return;

        float radius = BranchRadius(childId, depth);

        Vector3[] pts = BuildBezier(from, to, length);
        _branchCurves.Add(new BranchCurve { from = from, to = to, points = pts });
        _branches.Add(pts, radius, radius * 0.72f, depth < 3 ? 12 : 8);
    }

    /// <summary>All branches and the trunk as one lit, shadow-casting mesh.</summary>
    void BuildBranchMesh()
    {
        if (_branches.VertexCount == 0) return;
        _branchMesh = _branches.Build("ForestBranches");
        ArenaMaterials.Create(_treeRoot.transform, "Branches", _branchMesh, BranchMaterial());
        _branches.Clear();
    }

    // Satin, slightly warm titanium: reads as machined metal under the studio lights
    static Material BranchMaterial() { return ArenaMaterials.Lit("branch", new Color(0.66f, 0.665f, 0.67f), 0.9f, 0.55f); }

    Vector3[] BuildBezier(Vector3 from, Vector3 to, float length)
    {
        Vector3 ctrl = (from + to) * 0.5f + Vector3.up * length * 0.1f;
        Vector3[] pts = new Vector3[BezierSegments + 1];
        for (int i = 0; i <= BezierSegments; i++)
            pts[i] = ArenaFX.Bezier(from, ctrl, to, i / (float)BezierSegments);
        return pts;
    }

    /// <summary>Replaces straight node-to-node hops with the curved branches actually drawn.</summary>
    List<Vector3> ExpandAlongBranches(List<Vector3> waypoints)
    {
        var result = new List<Vector3>(waypoints.Count * BezierSegments + 1) { waypoints[0] };
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 a = waypoints[i], b = waypoints[i + 1];
            bool found = false;
            for (int c = 0; c < _branchCurves.Count && !found; c++)
            {
                BranchCurve bc = _branchCurves[c];
                if ((bc.from - a).sqrMagnitude < 1e-4f && (bc.to - b).sqrMagnitude < 1e-4f)
                {
                    for (int k = 1; k <= BezierSegments; k++) result.Add(bc.points[k]);
                    found = true;
                }
                else if ((bc.from - b).sqrMagnitude < 1e-4f && (bc.to - a).sqrMagnitude < 1e-4f)
                {
                    for (int k = 1; k <= BezierSegments; k++) result.Add(bc.points[BezierSegments - k]);
                    found = true;
                }
            }
            if (!found)
                for (int k = 1; k <= BezierSegments; k++) result.Add(Vector3.Lerp(a, b, k / (float)BezierSegments));
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Twigs: one line mesh for the whole canopy
    // ------------------------------------------------------------------

    void BuildTwigs()
    {
        if (_leafCount == 0) return;
        float perLeaf = TwigSegmentBudget / (float)_leafCount;
        int levels = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(perLeaf + 2f, 2f)) - 1, 1, 6);
        var twigs = new ArenaGeometry.TubeBuilder();

        foreach (var kvp in _treeData)
        {
            TreeNodeData t = kvp.Value;
            if (!t.isLeaf || !_nodePositions.TryGetValue(t.id, out Vector3 p)) continue;

            Vector3 dir = _nodeDir.TryGetValue(t.id, out Vector3 d) ? d : Vector3.up;
            float len = SegmentLength(Mathf.Max(_nodeDepth[t.id] - 1, 0)) * 0.45f;
            Vector3 axis = Quaternion.AngleAxis(Hash01(t.id * 13) * 360f, dir) * PerpendicularTo(dir);
            for (int s = 0; s < 2; s++)
            {
                float ang = (s == 0 ? 1f : -1f) * (22f + 14f * Hash01(t.id * 7 + s));
                AddTwig(p, Quaternion.AngleAxis(ang, axis) * dir, len, TwigRadius(t.id), 1, levels, t.id * 100 + s, twigs);
            }
        }
        if (twigs.VertexCount == 0) return;

        _twigMesh = twigs.Build("TwigMesh");
        // Fine enough that their shadows would only be noise
        ArenaMaterials.Create(_treeRoot.transform, "Twigs", _twigMesh, BranchMaterial(), false);
    }

    // Twigs start a little thinner than the branch they grow from
    float TwigRadius(int leafId)
    {
        int depth = _nodeDepth.TryGetValue(leafId, out int d) ? d : _maxDepth;
        return Mathf.Max(0.012f, BranchRadius(leafId, depth) * 0.72f) * 0.7f;
    }

    void AddTwig(Vector3 p, Vector3 dir, float len, float radius, int level, int maxLevel, int seed, ArenaGeometry.TubeBuilder twigs)
    {
        Vector3 end = p + dir * len;
        float endRadius = radius * 0.68f;
        // Each twig tapers to a fine point at the very tips of the canopy
        twigs.Add(new[] { p, end }, radius, level >= maxLevel ? radius * 0.15f : endRadius, 5, false);
        if (level >= maxLevel) return;

        Vector3 axis = Quaternion.AngleAxis(Hash01(seed) * 360f, dir) * PerpendicularTo(dir);
        float a = 22f + 16f * Hash01(seed + 1);
        AddTwig(end, Quaternion.AngleAxis(a, axis) * dir, len * 0.72f, endRadius, level + 1, maxLevel, seed * 2 + 1, twigs);
        AddTwig(end, Quaternion.AngleAxis(-a, axis) * dir, len * 0.72f, endRadius, level + 1, maxLevel, seed * 2 + 2, twigs);
    }

    // ------------------------------------------------------------------
    // Labels and atmosphere
    // ------------------------------------------------------------------

    void CreateLabel(GameObject parent, string text, float radius)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent.transform, false);
        labelObj.transform.localPosition = new Vector3(0, radius * 1.5f + 1.2f, 0);

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        // A TextMesh made from script has no font or material: without these it draws nothing
        // (or pink) in builds. Use Unity's built-in runtime font, which is always included.
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            tm.font = font;
            labelObj.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        tm.richText = true;
        tm.text = text;
        tm.characterSize = 0.045f * Mathf.Sqrt(_treeScale);
        tm.fontSize = 48;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = ArenaTheme.TextPrimary;

        labelObj.AddComponent<ForestBillboard>();
    }

    void CreateAmbientDust()
    {
        GameObject go = new GameObject("AmbientDust");
        go.transform.SetParent(transform, false);
        _dustRoot = go.transform;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        _dustPs = ps;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 20f;
        main.startLifetime = 20f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.76f, 0.80f, 0.22f),
            new Color(0.80f, 0.84f, 0.88f, 0.14f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 1.8f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 20f;

        // A few specks drifting very slowly, for depth
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
        vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);

        // Fade in and out so motes never pop
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = ArenaFX.Glow(false, false);
        ArenaFX.Unlit(pr);
        ps.Play();
    }

    void CenterDustOnTree()
    {
        if (_dustRoot == null || _nodePositions.Count == 0) return;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var kvp in _nodePositions) b.Encapsulate(kvp.Value);
        _dustRoot.localPosition = b.center;
        if (_dustPs != null)
        {
            var shape = _dustPs.shape;
            shape.radius = Mathf.Max(20f, b.extents.magnitude * 1.3f);
        }
    }
}
