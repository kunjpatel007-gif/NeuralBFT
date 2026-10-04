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
        transform.rotation = cam.transform.rotation;
    }
}

public class ForestRenderer : MonoBehaviour
{
    [Header("Tree Layout")]
    public float horizontalSpacing = 2.5f;   
    public float verticalSpacing = 3.0f;     
    public float nodeRadius = 0.4f;

    [Header("Materials")]
    public Material decisionNodeMat;   
    public Material leafNodeMat;       
    public Material branchMat;         
    public Material pulseTrailMat;     

    private Dictionary<int, GameObject> _nodeObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3> _nodePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, TreeNodeData> _treeData = new Dictionary<int, TreeNodeData>();
    private Dictionary<int, int> _nodeDepth = new Dictionary<int, int>();
    private Dictionary<int, Vector3> _nodeDir = new Dictionary<int, Vector3>();
    private GameObject _treeRoot;
    private List<DataPulse> _activePulses = new List<DataPulse>();

    private struct TreeNodeData
    {
        public int id, left, right;
        public string feature;
        public float threshold;
        public bool isLeaf;
    }

    private struct BranchCurve
    {
        public Vector3 from, to;
        public Vector3[] points;
    }

    private List<BranchCurve> _branchCurves = new List<BranchCurve>();

    private int _maxDepth, _leafCount, _labelMaxDepth = int.MaxValue;
    private float _treeScale = 1f, _decay = 0.78f, _spreadAngle = 30f, _nodeScale = 1f;
    private const int BezierSegments = 12;
    private const int TwigSegmentBudget = 6000;  
    private const int MaxPulseLights = 16;

    // ── CYAN / TEAL / PURPLE PALETTE ──
    private static readonly Color SandWhite      = new Color(0.7f, 1.0f, 0.95f, 1f); 
    private static readonly Color BranchTip      = new Color(0.2f, 1.0f, 0.9f, 1f);  
    private static readonly Color RootLight      = new Color(0.0f, 0.8f, 0.6f, 1f);  
    private static readonly Color NodeCore       = new Color(0.5f, 1.0f, 0.9f, 1f);
    private static readonly Color NodeGlow       = new Color(0.0f, 0.7f, 0.8f, 0.3f);
    private static readonly Color LeafSpore      = new Color(0.3f, 1.0f, 0.8f, 0.95f);
    private static readonly Color HonestPulse    = new Color(0.6f, 0.0f, 1.0f, 1f); 
    private static readonly Color ByzantinePulse = new Color(1.0f, 0.0f, 0.0f, 1f); 
    private static readonly Color DustA          = new Color(0.2f, 1.0f, 0.8f, 0.3f);
    private static readonly Color DustB          = new Color(0.0f, 0.6f, 0.8f, 0.3f);

    private Texture2D _gradientTex, _cylinderTex;
    private Mesh _twigMesh;
    private Material _glowEnvelopeMat, _coreMat, _leafSpriteMat, _lineMat, _branchCylinderMat;
    private Material _honestPulseMat, _byzantinePulseMat, _dustMat;
    private Dictionary<Color, Material> _shockwaveMats = new Dictionary<Color, Material>();
    private Shader _litShader;
    private Transform _dustRoot;
    private ParticleSystem _dustPs;

    void Awake() { CreateAmbientDust(); }

    void OnDestroy()
    {
        Object[] owned = { _gradientTex, _cylinderTex, _twigMesh, _glowEnvelopeMat, _coreMat, _leafSpriteMat, _lineMat, _branchCylinderMat, _honestPulseMat, _byzantinePulseMat, _dustMat };
        foreach (Object o in owned) if (o != null) Destroy(o);
        foreach (var kvp in _shockwaveMats) if (kvp.Value != null) Destroy(kvp.Value);
    }

    public void BuildTree(JArray treeNodes)
    {
        if (_treeRoot != null) Destroy(_treeRoot);
        if (_twigMesh != null) { Destroy(_twigMesh); _twigMesh = null; }

        _nodeObjects.Clear(); _nodePositions.Clear(); _treeData.Clear();
        _nodeDepth.Clear(); _nodeDir.Clear(); _branchCurves.Clear(); _activePulses.Clear();

        _treeRoot = new GameObject("RandomForest_Tree");
        _treeRoot.transform.SetParent(transform, false);
        _treeRoot.transform.localPosition = Vector3.zero;

        foreach (JObject node in treeNodes)
        {
            TreeNodeData tnd = new TreeNodeData {
                id = node["id"]?.Value<int>() ?? 0, left = node["left"]?.Value<int>() ?? -1, right = node["right"]?.Value<int>() ?? -1,
                feature = node["feature"]?.ToString() ?? "LEAF", threshold = node["threshold"]?.Value<float>() ?? 0f, isLeaf = node["is_leaf"]?.Value<bool>() ?? true
            };
            _treeData[tnd.id] = tnd;
        }
        if (!_treeData.ContainsKey(0)) return;

        _maxDepth = 0; _leafCount = 0; MeasureTree(0, 0); ConfigureForNodeCount();

        Vector3 rootPos = new Vector3(0f, verticalSpacing * _treeScale * 1.6f, 0f);
        CalculatePositions(0, 0, rootPos, Vector3.up);
        CenterDustOnTree(); CreateTrunk(rootPos);

        foreach (var kvp in _treeData)
        {
            TreeNodeData tnd = kvp.Value;
            if (!_nodePositions.ContainsKey(tnd.id)) continue;

            Vector3 pos = _nodePositions[tnd.id];
            int depth = _nodeDepth[tnd.id];

            if (tnd.isLeaf) { _nodeObjects[tnd.id] = CreateLeafNode(tnd, pos, depth); continue; }
            _nodeObjects[tnd.id] = CreateDecisionNode(tnd, pos, tnd.id == 0, depth);

            if (tnd.left >= 0 && _nodePositions.ContainsKey(tnd.left)) CreateOrganicBranch(pos, _nodePositions[tnd.left], depth);
            if (tnd.right >= 0 && _nodePositions.ContainsKey(tnd.right)) CreateOrganicBranch(pos, _nodePositions[tnd.right], depth);
        }
        BuildTwigs();
    }

    public Vector3 GetNodePosition(int nodeId) { return _nodePositions.TryGetValue(nodeId, out Vector3 pos) ? pos : Vector3.zero; }

    public void AnimatePaths(JObject nodePaths)
    {
        if (_treeRoot == null) return;
        
        // Remove dead pulses from tracking, but let active ones finish their journey
        _activePulses.RemoveAll(p => p == null || p.gameObject == null);

        GameObject prefab = Resources.Load<GameObject>("MessagePrefab");

        foreach (var kvp in nodePaths)
        {
            string nodeId = kvp.Key; JArray pathArray = kvp.Value as JArray;
            if (pathArray == null || pathArray.Count == 0) continue;

            List<Vector3> waypoints = new List<Vector3>();
            foreach (var step in pathArray) { int treeNodeId = step.Value<int>(); if (_nodePositions.ContainsKey(treeNodeId)) waypoints.Add(_nodePositions[treeNodeId]); }
            if (waypoints.Count < 2) continue;

            List<Vector3> curved = ExpandAlongBranches(waypoints);
            bool isByzantine = nodeId.Contains("byz") || Random.value > 0.7f;
            Color pulseColor = isByzantine ? ByzantinePulse : HonestPulse;

            GameObject pulseObj;
            if (prefab != null) {
                pulseObj = Instantiate(prefab);
                pulseObj.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f); 
                Renderer pulseRend = pulseObj.GetComponent<Renderer>(); 
                if (pulseRend != null) {
                    pulseRend.material.SetColor("_BaseColor", pulseColor);
                    pulseRend.material.SetColor("_Color", pulseColor);
                    DisableShadows(pulseRend);
                }
            } else {
                pulseObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pulseObj.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                DisableCollider(pulseObj);
                Renderer pulseRend = pulseObj.GetComponent<Renderer>(); 
                if (pulseRend != null) {
                    pulseRend.sharedMaterial = GetLineMaterial(); 
                    pulseRend.material.color = pulseColor;
                    DisableShadows(pulseRend);
                }
            }
            
            pulseObj.name = $"Pulse_{nodeId}";
            pulseObj.transform.SetParent(_treeRoot.transform, false); 
            pulseObj.transform.localPosition = waypoints[0]; 

            TrailRenderer trail = pulseObj.GetComponent<TrailRenderer>();
            if (trail == null) trail = pulseObj.AddComponent<TrailRenderer>();
            
            trail.time = 2.0f; trail.startWidth = 0.1f; trail.endWidth = 0.001f;
            trail.sharedMaterial = pulseTrailMat != null ? pulseTrailMat : GetLineMaterial();
            trail.startColor = pulseColor; trail.endColor = new Color(pulseColor.r, pulseColor.g, pulseColor.b, 0f); 
            DisableShadows(trail);

            float secondsPerSegment = 1.333f + Mathf.Abs(nodeId.GetHashCode() % 5) * 0.177f;
            DataPulse pulse = pulseObj.GetComponent<DataPulse>();
            if (pulse == null) pulse = pulseObj.AddComponent<DataPulse>();
            pulse.Initialize(curved, secondsPerSegment / BezierSegments, isByzantine); 
            _activePulses.Add(pulse);
        }
    }

    public void TriggerShockwave(List<Vector3> waypoints, string faultType)
    {
        if (_treeRoot == null || waypoints == null || waypoints.Count < 2) return;
        Color color = GetFaultColor(faultType); List<Vector3> curved = ExpandAlongBranches(waypoints);

        GameObject prefab = Resources.Load<GameObject>("MessagePrefab");
        GameObject obj;
        if (prefab != null) {
            obj = Instantiate(prefab);
            obj.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
            Renderer rend = obj.GetComponent<Renderer>(); 
            if (rend != null) {
                rend.material.SetColor("_BaseColor", color);
                rend.material.SetColor("_Color", color);
                DisableShadows(rend);
            }
        } else {
            obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            DisableCollider(obj);
            Renderer rend = obj.GetComponent<Renderer>(); 
            if (rend != null) {
                rend.sharedMaterial = GetLineMaterial(); 
                rend.material.color = color;
                DisableShadows(rend);
            }
        }

        obj.name = $"Shockwave_{faultType}";
        obj.transform.SetParent(_treeRoot.transform, false); 
        obj.transform.localPosition = curved[0]; 

        TrailRenderer trail = obj.GetComponent<TrailRenderer>();
        if (trail == null) trail = obj.AddComponent<TrailRenderer>();

        trail.time = 1.5f; trail.startWidth = 0.5f; trail.endWidth = 0f;
        trail.sharedMaterial = pulseTrailMat != null ? pulseTrailMat : GetLineMaterial();
        trail.startColor = color; trail.endColor = new Color(color.r, color.g, color.b, 0f); 
        DisableShadows(trail);

        float secondsPerSegment = (1.777f / 4f) / BezierSegments;
        DataPulse pulse = obj.GetComponent<DataPulse>();
        if (pulse == null) pulse = obj.AddComponent<DataPulse>();
        pulse.Initialize(curved, secondsPerSegment);
        Destroy(obj, (curved.Count - 1) * secondsPerSegment + trail.time + 0.5f);
    }

    static Color GetFaultColor(string faultType)
    {
        switch ((faultType ?? string.Empty).ToLowerInvariant()) {
            case "spam": case "ddos": return new Color(1f, 0.0f, 0f, 1f);
            case "state_tampering": return new Color(1f, 0.2f, 0f, 1f);
            case "stealth": case "eclipse": return new Color(1f, 0.4f, 0f, 1f);
            default: return new Color(1f, 0.0f, 0.0f, 1f);
        }
    }

    void MeasureTree(int id, int depth)
    {
        if (!_treeData.TryGetValue(id, out TreeNodeData t)) return;
        _maxDepth = Mathf.Max(_maxDepth, depth);
        if (t.isLeaf) { _leafCount++; return; }
        MeasureTree(t.left, depth + 1); MeasureTree(t.right, depth + 1);
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

        float leftWidth = GetTreeWidth(tnd.left), rightWidth = GetTreeWidth(tnd.right), total = Mathf.Max(leftWidth + rightWidth, 1f);
        Vector3 perp = Quaternion.AngleAxis(depth * 137.5f + nodeId * 47f, dir) * PerpendicularTo(dir);
        float baseLen = SegmentLength(depth), upBias = depth < 4 ? 0.08f : 0.03f;

        if (tnd.left >= 0) { Vector3 d = Vector3.Slerp(Quaternion.AngleAxis(_spreadAngle, perp) * dir, Vector3.up, upBias).normalized; float len = baseLen * Mathf.Lerp(0.8f, 1.25f, leftWidth / total); CalculatePositions(tnd.left, depth + 1, pos + d * len, d); }
        if (tnd.right >= 0) { Vector3 d = Vector3.Slerp(Quaternion.AngleAxis(-_spreadAngle, perp) * dir, Vector3.up, upBias).normalized; float len = baseLen * Mathf.Lerp(0.8f, 1.25f, rightWidth / total); CalculatePositions(tnd.right, depth + 1, pos + d * len, d); }
    }

    float GetTreeWidth(int nodeId) { if (!_treeData.ContainsKey(nodeId)) return 1f; TreeNodeData tnd = _treeData[nodeId]; if (tnd.isLeaf) return 1f; return GetTreeWidth(tnd.left) + GetTreeWidth(tnd.right); }
    static Vector3 PerpendicularTo(Vector3 dir) { Vector3 p = Vector3.Cross(dir, Vector3.forward); if (p.sqrMagnitude < 0.01f) p = Vector3.Cross(dir, Vector3.right); return p.normalized; }
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

    GameObject CreateDecisionNode(TreeNodeData tnd, Vector3 pos, bool isRoot, int depth)
    {
        float r = nodeRadius * _nodeScale;
        GameObject node = new GameObject($"Decision_{tnd.id}");
        node.transform.SetParent(_treeRoot.transform, false); node.transform.localPosition = pos;
        SpawnSphere("Glow", node.transform, r * 1.2f, GetGlowEnvelopeMaterial());
        SpawnSphere("Core", node.transform, r * 0.7f, decisionNodeMat != null ? decisionNodeMat : GetCoreMaterial());
        
        if (depth <= _labelMaxDepth) {
            string niceName = FormatFeatureName(tnd.feature);
            if (!string.IsNullOrEmpty(niceName)) {
                CreateLabel(node, $"{niceName}\n< {tnd.threshold:F1}", r);
            }
        }
        return node;
    }

    GameObject CreateLeafNode(TreeNodeData tnd, Vector3 pos, int depth)
    {
        GameObject leaf = new GameObject($"Leaf_{tnd.id}");
        leaf.transform.SetParent(_treeRoot.transform, false); 
        leaf.transform.localPosition = pos; 
        return leaf;
    }

    void CreateTrunk(Vector3 rootPos)
    {
        float tw = Mathf.Sqrt(_treeScale);
        GameObject trunk = new GameObject("Trunk"); trunk.transform.SetParent(_treeRoot.transform, false);
        LineRenderer lr = trunk.AddComponent<LineRenderer>(); lr.useWorldSpace = false; lr.positionCount = 2; lr.SetPosition(0, Vector3.zero); lr.SetPosition(1, rootPos);
        
        // THIN TRUNK
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.18f * tw), new Keyframe(1f, 0.12f * tw)); 
        lr.numCapVertices = 4;
        
        Gradient g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(SandWhite, 0f), new GradientColorKey(SandWhite, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(1f, 1f) });
        lr.colorGradient = g;
        
        lr.textureMode = LineTextureMode.Stretch; 
        lr.sharedMaterial = branchMat != null ? branchMat : GetBranchCylinderMaterial();
        DisableShadows(lr);
    }

    void CreateOrganicBranch(Vector3 from, Vector3 to, int depth)
    {
        float length = (to - from).magnitude; if (length < 0.001f) return;
        
        // THIN BRANCHES
        float thickness = Mathf.Max(0.015f, 0.12f * Mathf.Sqrt(_treeScale) * Mathf.Pow(0.72f, depth)); 
        
        Vector3[] pts = BuildBezier(from, to, length); _branchCurves.Add(new BranchCurve { from = from, to = to, points = pts });

        GameObject branch = new GameObject("Branch"); branch.transform.SetParent(_treeRoot.transform, false);
        LineRenderer lr = branch.AddComponent<LineRenderer>(); lr.useWorldSpace = false; lr.positionCount = pts.Length; lr.SetPositions(pts);
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, thickness), new Keyframe(1f, thickness * 0.7f));
        lr.startColor = SandWhite; lr.endColor = BranchTip; lr.numCapVertices = 4; lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Stretch; 
        lr.sharedMaterial = branchMat != null ? branchMat : GetBranchCylinderMaterial();
        DisableShadows(lr);
    }

    Vector3[] BuildBezier(Vector3 from, Vector3 to, float length)
    {
        Vector3 ctrl = (from + to) * 0.5f + Vector3.up * length * 0.1f; Vector3[] pts = new Vector3[BezierSegments + 1];
        for (int i = 0; i <= BezierSegments; i++) { float t = i / (float)BezierSegments; float u = 1f - t; pts[i] = u * u * from + 2f * u * t * ctrl + t * t * to; }
        return pts;
    }

    List<Vector3> ExpandAlongBranches(List<Vector3> waypoints)
    {
        List<Vector3> result = new List<Vector3>(waypoints.Count * BezierSegments + 1); result.Add(waypoints[0]);
        for (int i = 0; i < waypoints.Count - 1; i++) {
            Vector3 a = waypoints[i], b = waypoints[i + 1]; bool found = false;
            for (int c = 0; c < _branchCurves.Count && !found; c++) {
                BranchCurve bc = _branchCurves[c];
                if ((bc.from - a).sqrMagnitude < 1e-4f && (bc.to - b).sqrMagnitude < 1e-4f) { for (int k = 1; k <= BezierSegments; k++) result.Add(bc.points[k]); found = true; }
                else if ((bc.from - b).sqrMagnitude < 1e-4f && (bc.to - a).sqrMagnitude < 1e-4f) { for (int k = 1; k <= BezierSegments; k++) result.Add(bc.points[BezierSegments - k]); found = true; }
            }
            if (!found) for (int k = 1; k <= BezierSegments; k++) result.Add(Vector3.Lerp(a, b, k / (float)BezierSegments));
        }
        return result;
    }

    void BuildTwigs()
    {
        if (_leafCount == 0) return;
        float perLeaf = TwigSegmentBudget / (float)_leafCount; int levels = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(perLeaf + 2f, 2f)) - 1, 1, 6);
        List<Vector3> verts = new List<Vector3>();
        List<Color> cols = new List<Color>();

        foreach (var kvp in _treeData) {
            TreeNodeData t = kvp.Value; if (!t.isLeaf || !_nodePositions.TryGetValue(t.id, out Vector3 p)) continue;
            Vector3 dir = _nodeDir.TryGetValue(t.id, out Vector3 d) ? d : Vector3.up;
            float len = SegmentLength(Mathf.Max(_nodeDepth[t.id] - 1, 0)) * 0.45f;
            Vector3 axis = Quaternion.AngleAxis(Hash01(t.id * 13) * 360f, dir) * PerpendicularTo(dir);
            for (int s = 0; s < 2; s++) { float ang = (s == 0 ? 1f : -1f) * (22f + 14f * Hash01(t.id * 7 + s)); AddTwig(p, Quaternion.AngleAxis(ang, axis) * dir, len, 1, levels, t.id * 100 + s, verts, cols); }
        }
        if (verts.Count == 0) return;

        _twigMesh = new Mesh { name = "TwigMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        _twigMesh.SetVertices(verts); _twigMesh.SetColors(cols);
        int[] idx = new int[verts.Count]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
        _twigMesh.SetIndices(idx, MeshTopology.Lines, 0); _twigMesh.RecalculateBounds();

        GameObject go = new GameObject("Twigs"); go.transform.SetParent(_treeRoot.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _twigMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = GetLineMaterial(); DisableShadows(mr);
    }

    void AddTwig(Vector3 p, Vector3 dir, float len, int level, int maxLevel, int seed, List<Vector3> verts, List<Color> cols)
    {
        Vector3 end = p + dir * len; verts.Add(p); verts.Add(end);
        cols.Add(TwigColor((level - 1) / (float)maxLevel)); cols.Add(TwigColor(level / (float)maxLevel));
        if (level >= maxLevel) return;
        Vector3 axis = Quaternion.AngleAxis(Hash01(seed) * 360f, dir) * PerpendicularTo(dir); float a = 22f + 16f * Hash01(seed + 1);
        AddTwig(end, Quaternion.AngleAxis(a, axis) * dir, len * 0.72f, level + 1, maxLevel, seed * 2 + 1, verts, cols);
        AddTwig(end, Quaternion.AngleAxis(-a, axis) * dir, len * 0.72f, level + 1, maxLevel, seed * 2 + 2, verts, cols);
    }
    static Color TwigColor(float t) { return new Color(0.2f, 1.0f, 0.9f, Mathf.Lerp(0.9f, 0.2f, t)); } 

    void CreateLabel(GameObject parent, string text, float radius)
    {
        // RAISED AND ENLARGED TEXT
        GameObject labelObj = new GameObject("Label"); labelObj.transform.SetParent(parent.transform, false); 
        labelObj.transform.localPosition = new Vector3(0, radius * 1.5f + 1.2f, 0);
        
        TextMesh tm = labelObj.AddComponent<TextMesh>(); tm.text = text; 
        tm.characterSize = 0.045f * Mathf.Sqrt(_treeScale); 
        tm.fontSize = 48; tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center; 
        tm.color = new Color(1f, 1f, 1f, 1f); // Pure white
        
        labelObj.AddComponent<ForestBillboard>();
    }

    void CreateAmbientDust()
    {
        GameObject go = new GameObject("AmbientDust"); go.transform.SetParent(transform, false); _dustRoot = go.transform;
        ParticleSystem ps = go.AddComponent<ParticleSystem>(); _dustPs = ps; ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.prewarm = true; main.duration = 20f; main.startLifetime = 20f; main.startSpeed = 0f; main.startSize = 0.03f; main.startColor = new ParticleSystem.MinMaxGradient(DustA, DustB); main.simulationSpace = ParticleSystemSimulationSpace.Local; main.gravityModifier = 0f; main.maxParticles = 200;
        var emission = ps.emission; emission.rateOverTime = 10f;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 20f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Billboard; pr.sharedMaterial = GetDustMaterial(); DisableShadows(pr); ps.Play();
    }
    void CenterDustOnTree()
    {
        if (_dustRoot == null || _nodePositions.Count == 0) return;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero); foreach (var kvp in _nodePositions) b.Encapsulate(kvp.Value);
        _dustRoot.localPosition = b.center; 
        if (_dustPs != null) {
            var shape = _dustPs.shape;
            shape.radius = Mathf.Max(20f, b.extents.magnitude * 1.3f);
        }
    }

    Texture2D GetGradientTexture()
    {
        if (_gradientTex != null) return _gradientTex;
        _gradientTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color center = new Color(1f, 1f, 1f, 1f), edge = new Color(1f, 0.5f, 0.1f, 0f); 
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
            float dist = Mathf.Sqrt(Mathf.Pow(x + 0.5f - 32f, 2) + Mathf.Pow(y + 0.5f - 32f, 2)) / 32f;
            _gradientTex.SetPixel(x, y, Color.Lerp(edge, center, Mathf.Clamp01(1f - Mathf.Pow(dist, 1.5f))));
        }
        _gradientTex.Apply(); return _gradientTex;
    }

    Texture2D GetCylinderTexture()
    {
        if (_cylinderTex != null) return _cylinderTex;
        _cylinderTex = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++) {
            float v = y / 63f; float dist = Mathf.Abs(v - 0.5f) * 2f; 
            
            // GLASS PIPE TEXTURE: Transparent in center, opaque at edges
            float alpha = 0.15f + 0.7f * Mathf.Pow(dist, 2f); 
            Color c = new Color(1f, 1f, 1f, alpha);
            
            for (int x = 0; x < 4; x++) _cylinderTex.SetPixel(x, y, c);
        }
        _cylinderTex.Apply(); return _cylinderTex;
    }

    Shader GetLitShader() { if (_litShader != null) return _litShader; _litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); return _litShader; }

    Material MakeSpriteMaterial(Texture2D tex, Color tint)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material m = new Material(s); if (tex != null) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000; m.SetShaderPassEnabled("ShadowCaster", false);
        return m;
    }

    Material GetGlowEnvelopeMaterial()
    {
        if (_glowEnvelopeMat != null) return _glowEnvelopeMat;
        Material m = new Material(GetLitShader()); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", NodeGlow);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.0f, 0.8f, 0.9f) * 1.5f);
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000; m.SetShaderPassEnabled("ShadowCaster", false);
        _glowEnvelopeMat = m; return m;
    }

    Material GetCoreMaterial()
    {
        if (_coreMat != null) return _coreMat;
        Material m = new Material(GetLitShader()); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", NodeCore);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.8f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.2f, 1.0f, 0.9f) * 3f);
        _coreMat = m; return m;
    }

    Material GetBranchCylinderMaterial() { if (_branchCylinderMat == null) _branchCylinderMat = MakeSpriteMaterial(GetCylinderTexture(), Color.white); return _branchCylinderMat; }
    Material GetLineMaterial() { if (_lineMat == null) _lineMat = MakeSpriteMaterial(null, Color.white); return _lineMat; }
    Material GetLeafSpriteMaterial() { if (_leafSpriteMat == null) _leafSpriteMat = MakeSpriteMaterial(GetGradientTexture(), LeafSpore); return _leafSpriteMat; }
    Material GetPulseMaterial(bool byzantine) { return byzantine ? (_byzantinePulseMat ?? (_byzantinePulseMat = MakeSpriteMaterial(GetGradientTexture(), ByzantinePulse))) : (_honestPulseMat ?? (_honestPulseMat = MakeSpriteMaterial(GetGradientTexture(), HonestPulse))); }
    Material GetShockwaveMaterial(Color color) { if (_shockwaveMats.TryGetValue(color, out Material m) && m != null) return m; m = MakeSpriteMaterial(GetGradientTexture(), color); _shockwaveMats[color] = m; return m; }
    Material GetDustMaterial() { if (_dustMat == null) _dustMat = MakeSpriteMaterial(GetGradientTexture(), Color.white); return _dustMat; }

    GameObject SpawnSphere(string name, Transform parent, float scale, Material mat) { GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere); s.name = name; s.transform.SetParent(parent, false); s.transform.localPosition = Vector3.zero; s.transform.localScale = Vector3.one * scale; DisableCollider(s); Renderer r = s.GetComponent<Renderer>(); r.sharedMaterial = mat; DisableShadows(r); return s; }
    Light AddPointLight(GameObject go, Color color, float range, float intensity) { Light l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = color; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None; return l; }
    static void DisableCollider(GameObject go) { Collider col = go.GetComponent<Collider>(); if (col != null) col.enabled = false; }
    static void DisableShadows(Renderer r) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
}
