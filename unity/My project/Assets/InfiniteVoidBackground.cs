using UnityEngine;

// Attach this to a full-screen Quad (or any mesh) sitting behind your camera's
// other content. It creates/assigns a material using InfiniteVoidBackground.shader
// and drives its parameters, plus optionally spawns a handful of drifting
// particles for extra depth.
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class InfiniteVoidBackground : MonoBehaviour
{
    [Header("Shader (leave empty to auto-find 'Custom/InfiniteVoidBackground')")]
    public Shader voidShader;
    private Material _voidMat;

    [Header("Colors")]
    public Color baseColor = new Color(0.02f, 0.02f, 0.06f);
    public Color accentColor = new Color(0.4f, 0.85f, 1f);

    [Header("Motion")]
    [Range(0f, 5f)] public float swirlSpeed = 0.6f;
    [Range(0f, 10f)] public float swirlStrength = 3f;
    [Range(1f, 40f)] public float ringCount = 14f;
    [Range(0f, 5f)] public float ringPulseSpeed = 1.5f;

    [Header("Stars")]
    [Range(10f, 200f)] public float starDensity = 60f;
    [Range(0f, 10f)] public float starTwinkleSpeed = 3f;

    [Header("Vignette")]
    [Range(0f, 1f)] public float vignetteInner = 0.15f;
    [Range(0f, 1.5f)] public float vignetteOuter = 0.9f;

    [Header("Optional depth particles")]
    public bool spawnDriftParticles = true;
    public int particleCount = 150;
    public float particleAreaSize = 12f;

    void OnEnable()
    {
        SetupMaterial();
        if (spawnDriftParticles && GetComponentInChildren<ParticleSystem>() == null)
            SpawnDriftParticles();
    }

    void SetupMaterial()
    {
        var mr = GetComponent<MeshRenderer>();
        if (_voidMat == null)
        {
            Shader shader = voidShader != null ? voidShader : Shader.Find("Custom/InfiniteVoidBackground");
            if (shader == null)
            {
                Debug.LogWarning("InfiniteVoidBackground: shader not found. Assign InfiniteVoidBackground.shader to 'voidShader'.");
                return;
            }
            _voidMat = new Material(shader);
            mr.sharedMaterial = _voidMat;
        }
    }

    void Update()
    {
        if (_voidMat == null) return;

        _voidMat.SetColor("_MainTint", baseColor);
        _voidMat.SetColor("_AccentColor", accentColor);
        _voidMat.SetFloat("_SwirlSpeed", swirlSpeed);
        _voidMat.SetFloat("_SwirlStrength", swirlStrength);
        _voidMat.SetFloat("_RingCount", ringCount);
        _voidMat.SetFloat("_RingSpeed", ringPulseSpeed);
        _voidMat.SetFloat("_StarDensity", starDensity);
        _voidMat.SetFloat("_StarSpeed", starTwinkleSpeed);
        _voidMat.SetFloat("_VignetteInner", vignetteInner);
        _voidMat.SetFloat("_VignetteOuter", vignetteOuter);
    }

    void SpawnDriftParticles()
    {
        var psGO = new GameObject("VoidDriftParticles");
        psGO.transform.SetParent(transform, false);
        var ps = psGO.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startLifetime = 6f;
        main.startSpeed = 0.4f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(accentColor * 0.8f, Color.white);
        main.maxParticles = particleCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = particleCount / main.startLifetime.constant;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = particleAreaSize;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var mat = new Material(Shader.Find("Particles/Standard Unlit"));
        mat.SetColor("_Color", accentColor);
        renderer.material = mat;
    }
}
