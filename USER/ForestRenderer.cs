using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Small helper: keeps a quad / label facing the active camera.
/// Added at runtime by ForestRenderer (leaf spores, pulse fireflies, labels).
/// </summary>
public class ForestBillboard : MonoBehaviour
{
    private static Camera _cam;

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        transform.rotation = _cam.transform.rotation;
    }
}

/// <summary>
/// Builds a 3D bioluminescent neural tree from the Random Forest JSON sent by the Python backend.
/// Decision nodes = plasma orbs, leaves = glowing spores, branches = tapered bark tubes with glowing veins.
/// Decision paths animate as firefly pulses with long trails.
/// All textures are generated procedurally in code. No external assets.
/// </summary>
public class ForestRenderer : MonoBehaviour
{
    [Header("Tree Layout")]
    public float horizontalSpacing = 2.5f;
    public float verticalSpacing = 3.0f;
    public float nodeRadius = 0.4f;

    [Header("Materials")]
    public Material decisionNodeMat;   // Optional override: used for the inner plasma core of decision nodes
    public Material leafNodeMat;       // Optional override: replaces the spore billboard material (leave empty for the glow look)
    public Material branchMat;         // Optional override: replaces the procedural bark material
    public Material pulseTrailMat;     // Optional override: replaces the pulse trail material

    // Internal tracking
    private Dictionary<int, GameObject> _nodeObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3> _nodePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, TreeNodeData> _treeData = new Dictionary<int, TreeNodeData>();
    private Dictionary<int, int> _nodeDepth = new Dictionary<int, int>();
    private GameObject _treeRoot;
    private float _totalTreeWidth = 1f;

    // Pulse tracking
    private List<DataPulse> _activePulses = new List<DataPulse>();

    private struct TreeNodeData
    {
        public int id;
        public int left;
        public int right;
        public string feature;
        public float threshold;
        public bool isLeaf;
    }

    private struct FlickerLight
    {
        public Light light;
        public float baseIntensity;
        public float phase;
    }

    private List<FlickerLight> _flickerLights = new List<FlickerLight>();
    private Transform _dustRoot;
    private MaterialPropertyBlock _mpb;

    // ── Palette ──
    private static readonly Color TrunkGlow       = new Color(0.2f, 1.0f, 0.1f, 1f);
    private static readonly Color VeinColor       = new Color(0.0f, 1.0f, 0.8f, 0.8f);
    private static readonly Color DecisionCore    = new Color(0.05f, 0.3f, 0.7f, 1f);
    private static readonly Color DecisionGlow    = new Color(0.1f, 0.5f, 1.0f, 1f);
    private static readonly Color DecisionLight   = new Color(0.1f, 0.7f, 0.9f, 1f);
    private static readonly Color LeafSpore       = new Color(0.5f, 0.2f, 1.0f, 0.9f);
    private static readonly Color LeafLight       = new Color(0.5f, 0.2f, 1.0f, 1f);
    private static readonly Color HonestPulse     = new Color(0.0f, 1.0f, 0.4f, 1f);
    private static readonly Color ByzantinePulse  = new Color(1.0f, 0.3f, 0.0f, 1f);
    private static readonly Color DustTeal        = new Color(0.0f, 0.6f, 0.5f, 0.3f);
    private static readonly Color DustPurple      = new Color(0.5f, 0.2f, 1.0f, 0.3f);

    // ── Cached procedural assets ──
    private Texture2D _gradientTex, _barkTex, _veinTex, _veinEmissionTex;
    private Material _glowEnvelopeMat, _coreMat, _barkMat, _discMat, _leafSpriteMat;
    private Material _veinLineMat, _honestPulseMat, _byzantinePulseMat, _trailMat, _dustMat;
    private Shader _litShader;

    // ── Lifecycle ──

    void Awake()
    {
        CreateAmbientDust();
    }

    void Update()
    {
        float t = Time.time;
        for (int i = 0; i < _flickerLights.Count; i++)
        {
            FlickerLight f = _flickerLights[i];
            if (f.light == null) continue;
            f.light.intensity = f.baseIntensity * (1f + 0.4f * Mathf.Sin(t * 2f + f.phase));
        }
    }

    void OnDestroy()
    {
        Object[] owned =
        {
            _gradientTex, _barkTex, _veinTex, _veinEmissionTex,
            _glowEnvelopeMat, _coreMat, _barkMat, _discMat, _leafSpriteMat,
            _veinLineMat, _honestPulseMat, _byzantinePulseMat, _trailMat, _dustMat
        };
        foreach (Object o in owned)
            if (o != null) Destroy(o);
    }

    // ── Public API (unchanged signatures) ──

    /// <summary>
    /// Called once when Python sends the full tree structure.
    /// Destroys any existing tree and rebuilds from scratch.
    /// </summary>
    public void BuildTree(JArray treeNodes)
    {
        // Destroy old tree
        if (_treeRoot != null)
            Destroy(_treeRoot);

        _nodeObjects.Clear();
        _nodePositions.Clear();
        _treeData.Clear();
        _nodeDepth.Clear();
        _flickerLights.Clear();
        _activePulses.Clear();

        _treeRoot = new GameObject("RandomForest_Tree");
        _treeRoot.transform.SetParent(transform, false);
        _treeRoot.transform.localPosition = Vector3.zero;

        // Parse all tree nodes
        foreach (JObject node in treeNodes)
        {
            TreeNodeData tnd = new TreeNodeData
            {
                id = node["id"]?.Value<int>() ?? 0,
                left = node["left"]?.Value<int>() ?? -1,
                right = node["right"]?.Value<int>() ?? -1,
                feature = node["feature"]?.ToString() ?? "LEAF",
                threshold = node["threshold"]?.Value<float>() ?? 0f,
                isLeaf = node["is_leaf"]?.Value<bool>() ?? true
            };
            _treeData[tnd.id] = tnd;
        }

        // Calculate positions using a recursive layout
        _totalTreeWidth = GetTreeWidth(0);
        CalculatePositions(0, 0, 0, _totalTreeWidth);
        CenterDustOnTree();

        int rootLeft = -1, rootRight = -1;
        if (_treeData.TryGetValue(0, out TreeNodeData rootNode))
        {
            rootLeft = rootNode.left;
            rootRight = rootNode.right;
        }

        // Spawn 3D objects
        foreach (var kvp in _treeData)
        {
            TreeNodeData tnd = kvp.Value;
            if (!_nodePositions.ContainsKey(tnd.id)) continue;

            Vector3 pos = _nodePositions[tnd.id];
            int depth = _nodeDepth.TryGetValue(tnd.id, out int storedDepth) ? storedDepth : 0;

            GameObject nodeObj;
            if (tnd.isLeaf)
            {
                nodeObj = CreateLeafNode(tnd, pos);
            }
            else
            {
                bool isRoot = tnd.id == 0;
                bool isTrunkChild = tnd.id == rootLeft || tnd.id == rootRight;
                nodeObj = CreateDecisionNode(tnd, pos, isRoot, isTrunkChild);

                if (isRoot) CreateTrunkDisc(pos);

                if (tnd.left >= 0 && _nodePositions.ContainsKey(tnd.left))
                    CreateOrganicBranch(pos, _nodePositions[tnd.left], depth);

                if (tnd.right >= 0 && _nodePositions.ContainsKey(tnd.right))
                    CreateOrganicBranch(pos, _nodePositions[tnd.right], depth);
            }

            _nodeObjects[tnd.id] = nodeObj;
        }
    }

    /// <summary>
    /// Called every 2 seconds with the live decision paths for all nodes.
    /// Spawns firefly orbs that travel down the tree branches.
    /// </summary>
    public void AnimatePaths(JObject nodePaths)
    {
        if (_treeRoot == null) return;

        // Clean up old pulses
        foreach (var pulse in _activePulses)
        {
            if (pulse != null && pulse.gameObject != null)
                Destroy(pulse.gameObject);
        }
        _activePulses.Clear();

        foreach (var kvp in nodePaths)
        {
            string nodeId = kvp.Key;
            JArray pathArray = kvp.Value as JArray;
            if (pathArray == null || pathArray.Count == 0) continue;

            // Convert path to list of Vector3 positions
            List<Vector3> waypoints = new List<Vector3>();
            foreach (var step in pathArray)
            {
                int treeNodeId = step.Value<int>();
                if (_nodePositions.ContainsKey(treeNodeId))
                    waypoints.Add(_nodePositions[treeNodeId]);
            }

            if (waypoints.Count < 2) continue;

            // Determine color based on node name (byzantine = orange-red, honest = green)
            bool isByzantine = nodeId.Contains("byz") || Random.value > 0.7f; // Heuristic
            Color pulseColor = isByzantine ? ByzantinePulse : HonestPulse;

            // Firefly: billboarded quad using the radial gradient texture
            GameObject pulseObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pulseObj.name = $"Pulse_{nodeId}";
            pulseObj.transform.SetParent(_treeRoot.transform, false);
            pulseObj.transform.localPosition = waypoints[0];
            pulseObj.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            DisableCollider(pulseObj);

            Renderer pulseRend = pulseObj.GetComponent<Renderer>();
            pulseRend.sharedMaterial = GetPulseMaterial(isByzantine);
            DisableShadows(pulseRend);
            pulseObj.AddComponent<ForestBillboard>();

            AddPointLight(pulseObj, pulseColor, 3f, 3f);

            // Long glowing tail
            TrailRenderer trail = pulseObj.AddComponent<TrailRenderer>();
            trail.time = 2.0f;
            trail.startWidth = 0.1f;
            trail.endWidth = 0.001f;
            trail.sharedMaterial = pulseTrailMat != null ? pulseTrailMat : GetTrailMaterial();
            trail.startColor = pulseColor;
            trail.endColor = new Color(pulseColor.r, pulseColor.g, pulseColor.b, 0f);
            DisableShadows(trail);

            // Per-node speed variation so pulses do not all arrive together
            float secondsPerSegment = 0.6f + Mathf.Abs(nodeId.GetHashCode() % 5) * 0.08f;

            DataPulse pulse = pulseObj.AddComponent<DataPulse>();
            pulse.Initialize(waypoints, secondsPerSegment);
            _activePulses.Add(pulse);
        }
    }

    // ── Layout Engine (logic preserved; Z depth and per-node depth tracking added) ──

    void CalculatePositions(int nodeId, int depth, float xOffset, float width)
    {
        if (!_treeData.ContainsKey(nodeId)) return;
        TreeNodeData tnd = _treeData[nodeId];

        float x = xOffset + width / 2f;
        float y = -depth * verticalSpacing;

        // Fan the tree out in Z so it is a volumetric structure, not a flat diagram
        float zSpread = (x - _totalTreeWidth / 2f) * 0.6f;                         // horizontal lean
        float zNoise = Mathf.Sin(nodeId * 1.3f + depth * 0.7f) * depth * 0.8f;     // organic twist

        _nodePositions[nodeId] = new Vector3(x * horizontalSpacing, y, zSpread + zNoise);
        _nodeDepth[nodeId] = depth;

        if (!tnd.isLeaf)
        {
            float leftWidth = GetTreeWidth(tnd.left);
            float rightWidth = GetTreeWidth(tnd.right);
            float totalWidth = Mathf.Max(leftWidth + rightWidth, 1);

            if (tnd.left >= 0)
                CalculatePositions(tnd.left, depth + 1, xOffset, leftWidth);
            if (tnd.right >= 0)
                CalculatePositions(tnd.right, depth + 1, xOffset + leftWidth, rightWidth);
        }
    }

    float GetTreeWidth(int nodeId)
    {
        if (!_treeData.ContainsKey(nodeId)) return 1f;
        TreeNodeData tnd = _treeData[nodeId];
        if (tnd.isLeaf) return 1f;
        return GetTreeWidth(tnd.left) + GetTreeWidth(tnd.right);
    }

    // ── Node Builders ──

    GameObject CreateDecisionNode(TreeNodeData tnd, Vector3 pos, bool isRoot, bool isTrunkChild)
    {
        GameObject node = new GameObject($"Decision_{tnd.id}_{tnd.feature}");
        node.transform.SetParent(_treeRoot.transform, false);
        node.transform.localPosition = pos;

        // Outer glow envelope + inner plasma core
        SpawnSphere("Glow", node.transform, nodeRadius * 1.2f, GetGlowEnvelopeMaterial());
        SpawnSphere("Core", node.transform, nodeRadius * 0.7f,
            decisionNodeMat != null ? decisionNodeMat : GetCoreMaterial());

        // Light: neon green on the trunk, teal-blue elsewhere
        if (isRoot)
            AddPointLight(node, TrunkGlow, 15f, 8.0f);
        else if (isTrunkChild)
            AddPointLight(node, TrunkGlow, 6f, 3.0f);
        else
            AddPointLight(node, DecisionLight, 2.5f, 1.5f);

        CreateLabel(node, $"{tnd.feature}\n< {tnd.threshold:F2}");
        return node;
    }

    GameObject CreateLeafNode(TreeNodeData tnd, Vector3 pos)
    {
        GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Quad);
        leaf.name = $"Leaf_{tnd.id}";
        leaf.transform.SetParent(_treeRoot.transform, false);
        leaf.transform.localPosition = pos;
        leaf.transform.localScale = Vector3.one * (nodeRadius * 1.8f);
        DisableCollider(leaf);

        Renderer rend = leaf.GetComponent<Renderer>();
        rend.sharedMaterial = leafNodeMat != null ? leafNodeMat : GetLeafSpriteMaterial();
        DisableShadows(rend);
        leaf.AddComponent<ForestBillboard>();

        Light l = AddPointLight(leaf, LeafLight, 1.5f, 1.0f);
        _flickerLights.Add(new FlickerLight { light = l, baseIntensity = 1.0f, phase = tnd.id });

        return leaf;
    }

    void CreateTrunkDisc(Vector3 rootPos)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "TrunkEnergyDisc";
        disc.transform.SetParent(_treeRoot.transform, false);
        disc.transform.localPosition = rootPos + Vector3.down * 0.5f;
        disc.transform.localScale = new Vector3(4f, 0.05f, 4f);
        DisableCollider(disc);

        Renderer rend = disc.GetComponent<Renderer>();
        rend.sharedMaterial = GetDiscMaterial();
        DisableShadows(rend);
    }

    /// <summary>
    /// Tapered bark tube between two nodes with a glowing vein.
    /// Thickness: 0.25 at depth 0, multiplied by 0.7 per depth level.
    /// </summary>
    void CreateOrganicBranch(Vector3 from, Vector3 to, int depth)
    {
        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.001f) return;

        float thickness = 0.25f * Mathf.Pow(0.7f, depth);

        GameObject branch = new GameObject("Branch");
        branch.transform.SetParent(_treeRoot.transform, false);

        // Bark cylinder (Unity cylinder: height 2 along Y, diameter 1)
        GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cyl.name = "Bark";
        cyl.transform.SetParent(branch.transform, false);
        cyl.transform.localPosition = (from + to) * 0.5f;
        cyl.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
        cyl.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);
        DisableCollider(cyl);

        Renderer rend = cyl.GetComponent<Renderer>();
        rend.sharedMaterial = branchMat != null ? branchMat : GetBarkMaterial();

        // Per-branch UV tiling so bark rings keep a constant size on long branches
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(_mpb);
        Vector4 st = new Vector4(2f, Mathf.Max(1f, length * 0.6f), 0f, 0f);
        _mpb.SetVector("_BaseMap_ST", st);
        _mpb.SetVector("_MainTex_ST", st);
        rend.SetPropertyBlock(_mpb);

        // Bioluminescent vein line along the branch axis
        LineRenderer lr = branch.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = 0.02f;
        lr.endWidth = 0.02f;
        lr.startColor = VeinColor;
        lr.endColor = VeinColor;
        lr.textureMode = LineTextureMode.Tile;
        lr.sharedMaterial = GetVeinLineMaterial();
        DisableShadows(lr);
    }

    void CreateLabel(GameObject parent, string text)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent.transform, false);
        labelObj.transform.localPosition = new Vector3(0, nodeRadius * 0.6f + 0.15f, 0);

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = 0.03f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.7f, 0.9f, 1f, 0.9f); // Soft cyan

        labelObj.AddComponent<ForestBillboard>();
    }

    // ── Atmosphere ──

    void CreateAmbientDust()
    {
        GameObject go = new GameObject("AmbientDust");
        go.transform.SetParent(transform, false);
        _dustRoot = go.transform;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // required before changing duration

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 20f;
        main.startLifetime = 20f;
        main.startSpeed = 0f;
        main.startSize = 0.03f;
        main.startColor = new ParticleSystem.MinMaxGradient(DustTeal, DustPurple);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 200f / 20f; // steady state of ~200 live particles

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 20f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);   // slow upward drift
        vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = GetDustMaterial();
        DisableShadows(pr);

        ps.Play();
    }

    void CenterDustOnTree()
    {
        if (_dustRoot == null || _nodePositions.Count == 0) return;

        bool first = true;
        Bounds b = new Bounds();
        foreach (var kvp in _nodePositions)
        {
            if (first) { b = new Bounds(kvp.Value, Vector3.zero); first = false; }
            else b.Encapsulate(kvp.Value);
        }
        _dustRoot.localPosition = b.center;
    }

    // ── Procedural Textures ──

    /// <summary>64x64 radial gradient: white core fading to transparent violet.</summary>
    Texture2D GetGradientTexture()
    {
        if (_gradientTex != null) return _gradientTex;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color center = new Color(1f, 1f, 1f, 1f);
        Color edge = new Color(0.5f, 0.1f, 1f, 0f);
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) / half;
                float brightness = Mathf.Clamp01(1f - Mathf.Pow(dist, 1.5f));
                tex.SetPixel(x, y, Color.Lerp(edge, center, brightness));
            }
        }
        tex.Apply();
        _gradientTex = tex;
        return tex;
    }

    /// <summary>64x128 dark bark with lighter rings every 16 px and per-pixel grain.</summary>
    Texture2D GetBarkTexture()
    {
        if (_barkTex != null) return _barkTex;

        const int w = 64, h = 128;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        System.Random rng = new System.Random(1337);
        Color baseC = new Color(0.12f, 0.07f, 0.03f, 1f);
        Color ringC = new Color(0.2f, 0.12f, 0.05f, 1f);

        for (int y = 0; y < h; y++)
        {
            bool isRing = (y % 16) < 2;
            for (int x = 0; x < w; x++)
            {
                Color c = isRing ? ringC : baseC;
                float n = ((float)rng.NextDouble() * 2f - 1f) * 0.03f;
                c.r += n; c.g += n; c.b += n;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(true);
        _barkTex = tex;
        return tex;
    }

    /// <summary>8x64 energy vein: bright cyan in columns 3-4, fading to transparent at the edges.</summary>
    Texture2D GetVeinTexture()
    {
        if (_veinTex == null) _veinTex = BuildVeinTexture(false);
        return _veinTex;
    }

    /// <summary>Same vein, with RGB darkened toward the edges so it can be used as an emission map.</summary>
    Texture2D GetVeinEmissionTexture()
    {
        if (_veinEmissionTex == null) _veinEmissionTex = BuildVeinTexture(true);
        return _veinEmissionTex;
    }

    Texture2D BuildVeinTexture(bool forEmission)
    {
        const int w = 8, h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color centerC = new Color(0f, 1f, 0.9f, 1f);
        Color edgeC = new Color(0f, 0.5f, 0.4f, 0f);

        for (int x = 0; x < w; x++)
        {
            float d = Mathf.Min(Mathf.Abs(x - 3), Mathf.Abs(x - 4)); // 0 at x=3,4 ... 3 at x=0,7
            Color c = Color.Lerp(centerC, edgeC, d / 3f);
            if (forEmission)
            {
                float k = c.a * c.a;
                c = new Color(c.r * k, c.g * k, c.b * k, 1f);
            }
            for (int y = 0; y < h; y++)
                tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    // ── Material Factory (all cached) ──

    Shader GetLitShader()
    {
        if (_litShader != null) return _litShader;
        _litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (_litShader == null) _litShader = Shader.Find("Standard");
        return _litShader;
    }

    Material MakeSpriteMaterial(Texture2D tex, Color tint)
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        Material m = new Material(s);
        if (tex != null) m.mainTexture = tex;
        m.color = tint;
        return m;
    }

    static void SetBaseColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static void SetBaseMap(Material m, Texture2D t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
    }

    static void SetFloatIfPresent(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    static void SetEmission(Material m, Color c, Texture2D map = null)
    {
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c);
        if (map != null) m.SetTexture("_EmissionMap", map);
    }

    static void MakeTransparent(Material m)
    {
        m.SetOverrideTag("RenderType", "Transparent");
        SetFloatIfPresent(m, "_Surface", 1f);
        SetFloatIfPresent(m, "_Blend", 0f);
        SetFloatIfPresent(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloatIfPresent(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(m, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloatIfPresent(m, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(m, "_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    Material GetGlowEnvelopeMaterial()
    {
        if (_glowEnvelopeMat != null) return _glowEnvelopeMat;
        Material m = new Material(GetLitShader());
        SetBaseColor(m, new Color(DecisionGlow.r, DecisionGlow.g, DecisionGlow.b, 0.35f));
        SetFloatIfPresent(m, "_Smoothness", 0f);
        SetFloatIfPresent(m, "_Metallic", 0f);
        SetEmission(m, DecisionGlow * 2f);
        MakeTransparent(m);
        _glowEnvelopeMat = m;
        return m;
    }

    Material GetCoreMaterial()
    {
        if (_coreMat != null) return _coreMat;
        Material m = new Material(GetLitShader());
        SetBaseColor(m, DecisionCore);
        SetFloatIfPresent(m, "_Smoothness", 0.8f);
        SetFloatIfPresent(m, "_Metallic", 0f);
        SetEmission(m, new Color(0.1f, 0.6f, 1.0f) * 4f);
        _coreMat = m;
        return m;
    }

    Material GetBarkMaterial()
    {
        if (_barkMat != null) return _barkMat;
        Material m = new Material(GetLitShader());
        SetBaseColor(m, Color.white);
        SetBaseMap(m, GetBarkTexture());
        SetFloatIfPresent(m, "_Smoothness", 0.15f);
        SetFloatIfPresent(m, "_Metallic", 0f);
        // Surface vein glow: a center line alone would be hidden inside the opaque cylinder
        SetEmission(m, new Color(VeinColor.r, VeinColor.g, VeinColor.b) * 1.5f, GetVeinEmissionTexture());
        _barkMat = m;
        return m;
    }

    Material GetDiscMaterial()
    {
        if (_discMat != null) return _discMat;
        Material m = new Material(GetLitShader());
        SetBaseColor(m, TrunkGlow);
        SetFloatIfPresent(m, "_Smoothness", 0.5f);
        SetFloatIfPresent(m, "_Metallic", 0f);
        SetEmission(m, new Color(TrunkGlow.r, TrunkGlow.g, TrunkGlow.b) * 4f);
        _discMat = m;
        return m;
    }

    Material GetLeafSpriteMaterial()
    {
        if (_leafSpriteMat == null)
            _leafSpriteMat = MakeSpriteMaterial(GetGradientTexture(), LeafSpore);
        return _leafSpriteMat;
    }

    Material GetVeinLineMaterial()
    {
        if (_veinLineMat == null)
            _veinLineMat = MakeSpriteMaterial(GetVeinTexture(), Color.white);
        return _veinLineMat;
    }

    Material GetPulseMaterial(bool byzantine)
    {
        if (byzantine)
        {
            if (_byzantinePulseMat == null)
                _byzantinePulseMat = MakeSpriteMaterial(GetGradientTexture(), ByzantinePulse);
            return _byzantinePulseMat;
        }
        if (_honestPulseMat == null)
            _honestPulseMat = MakeSpriteMaterial(GetGradientTexture(), HonestPulse);
        return _honestPulseMat;
    }

    Material GetTrailMaterial()
    {
        if (_trailMat == null)
            _trailMat = MakeSpriteMaterial(null, Color.white); // color comes from the trail's vertex colors
        return _trailMat;
    }

    Material GetDustMaterial()
    {
        if (_dustMat == null)
            _dustMat = MakeSpriteMaterial(GetGradientTexture(), Color.white); // color comes from particle colors
        return _dustMat;
    }

    // ── Small Helpers ──

    GameObject SpawnSphere(string name, Transform parent, float scale, Material mat)
    {
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        s.transform.SetParent(parent, false);
        s.transform.localPosition = Vector3.zero;
        s.transform.localScale = Vector3.one * scale;
        DisableCollider(s);

        Renderer r = s.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        DisableShadows(r);
        return s;
    }

    Light AddPointLight(GameObject go, Color color, float range, float intensity)
    {
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        return l;
    }

    static void DisableCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    static void DisableShadows(Renderer r)
    {
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}
