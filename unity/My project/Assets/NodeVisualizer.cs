using UnityEngine;

/// <summary>
/// Dynamically colors node spheres based on their trust status.
/// FIXED: Uses MaterialPropertyBlock for zero-allocation color updates.
/// No new Material() calls. No shader leaks.
/// </summary>
public class NodeVisualizer : MonoBehaviour
{
    private MeshRenderer[]      _meshRenderers;
    private MaterialPropertyBlock _mpb;
    private float               _pulseTimer = 0f;

    // Cached shader property IDs — faster than string lookup
    private static readonly int _propBaseColor      = Shader.PropertyToID("_BaseColor");
    private static readonly int _propColor          = Shader.PropertyToID("_Color");
    private static readonly int _propEmissionColor  = Shader.PropertyToID("_EmissionColor");

    // Static cached Colors to avoid ColorUtility.TryParseHtmlString allocations every frame
    private static readonly Color _colorTrusted     = new Color(0f,    1f,    0.333f); // #00FF55
    private static readonly Color _colorVerified    = new Color(0f,    1f,    1f);     // #00FFFF
    private static readonly Color _colorWatched     = new Color(1f,    0.765f,0f);     // #FFC300
    private static readonly Color _colorQuarantined = new Color(1f,    0f,    0f);     // #FF0000
    private static readonly Color _colorBlacklisted = Color.black;

    // Shared core material — created once per node, destroyed when node dies
    private Material _sharedCoreMat;

    void Awake()
    {
        _meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            _sharedCoreMat = new Material(urpLit);
            _sharedCoreMat.SetFloat("_Smoothness", 0.6f);
            _sharedCoreMat.SetFloat("_Metallic", 0.1f);
            _sharedCoreMat.EnableKeyword("_EMISSION");
            
            foreach (var mr in _meshRenderers)
                if (mr != null) mr.sharedMaterial = _sharedCoreMat;
        }
    }

    void Start()
    {
        NodeData dummyData = new NodeData { status = "VERIFIED" };
        UpdateVisuals(dummyData);
    }

    void Update()
    {
        _pulseTimer += Time.deltaTime;
    }

    public void UpdateVisuals(NodeData data)
    {
        if (_meshRenderers == null || _meshRenderers.Length == 0) return;

        string stat = (data.status ?? "").ToUpper();

        Color baseColor;
        float emissionMultiplier;

        switch (stat)
        {
            case "TRUSTED":
                baseColor = _colorTrusted;     emissionMultiplier = 3.5f; break;
            case "WATCHED":
            case "HIGH RISK":
                baseColor = _colorWatched;     emissionMultiplier = 3.5f; break;
            case "QUARANTINED":
                baseColor = _colorQuarantined;
                float pulse = 1.053f + Mathf.Sin(_pulseTimer * 4f) * 0.79f;
                emissionMultiplier = pulse * 3.5f; break;
            case "BLACKLISTED":
                baseColor = _colorBlacklisted; emissionMultiplier = 0f;   break;
            default: // VERIFIED and unknown
                baseColor = _colorVerified;    emissionMultiplier = 3.5f; break;
        }

        Color emissionColor = baseColor * emissionMultiplier;

        foreach (var mr in _meshRenderers)
        {
            if (mr == null) continue;

            // Read current property block, update colors, write back — ZERO allocations
            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(_propBaseColor,     baseColor);
            _mpb.SetColor(_propColor,         baseColor);
            _mpb.SetColor(_propEmissionColor, emissionColor);
            mr.SetPropertyBlock(_mpb);
        }
        // Line renderers share one material — no per-frame updates needed
    }

    void OnDestroy()
    {
        if (_sharedCoreMat != null) Destroy(_sharedCoreMat);
    }
}
