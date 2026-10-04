using UnityEngine;
using UnityEngine.UI;

public class BackgroundManager : MonoBehaviour
{
    // Track everything we allocate so we can clean it up in OnDestroy
    private Texture2D  _gradientTex;
    private Material   _particleMat;
    private GameObject _canvasObj;
    private GameObject _psObj;

    void Start()
    {
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.black;
        }

        CreateGradientBackground();
        CreateDataMotes();
    }

    void CreateGradientBackground()
    {
        _canvasObj = new GameObject("PremiumBackgroundCanvas");
        _canvasObj.transform.SetParent(transform); // parented — destroyed with us
        Canvas canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera.main;
        canvas.planeDistance = 100f;
        _canvasObj.AddComponent<CanvasScaler>();

        GameObject bgObj = new GameObject("GradientImage");
        bgObj.transform.SetParent(_canvasObj.transform, false);
        RawImage ri = bgObj.AddComponent<RawImage>();

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        int height = 256;
        _gradientTex = new Texture2D(1, height);
        _gradientTex.wrapMode = TextureWrapMode.Clamp;

        Color topColor    = new Color(0.0f,  0.0f,  0.0f,  1f);
        Color bottomColor = new Color(0.03f, 0.04f, 0.06f, 1f);

        for (int y = 0; y < height; y++)
        {
            float t = (float)y / (height - 1);
            t = t * t * (3f - 2f * t);
            _gradientTex.SetPixel(0, y, Color.Lerp(bottomColor, topColor, t));
        }
        _gradientTex.Apply();
        ri.texture = _gradientTex;
    }

    void CreateDataMotes()
    {
        _psObj = new GameObject("AmbientDataMotes");
        _psObj.transform.SetParent(transform); // parented — destroyed with us
        _psObj.transform.position = Vector3.zero;

        ParticleSystem ps = _psObj.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.duration         = 10f;
        main.loop             = true;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(15f, 25f);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startSize        = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = 300;

        ParticleSystem.MinMaxGradient gradient = new ParticleSystem.MinMaxGradient();
        gradient.mode     = ParticleSystemGradientMode.TwoColors;
        gradient.colorMin = new Color(0f,   1f, 0.2f, 0.6f);
        gradient.colorMax = new Color(0.2f, 1f, 0.5f, 0.6f);
        main.startColor   = gradient;

        var emission = ps.emission;
        emission.rateOverTime = 15f;

        var shape = ps.shape;
        shape.shapeType       = ParticleSystemShapeType.Sphere;
        shape.radius          = 45f;
        shape.radiusThickness = 0.05f;

        var vel = ps.velocityOverLifetime;
        vel.enabled   = true;
        vel.x         = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y         = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z         = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalX  = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.orbitalY  = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.orbitalZ  = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient alphaGrad = new Gradient();
        alphaGrad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLife.color = new ParticleSystem.MinMaxGradient(alphaGrad);

        ParticleSystemRenderer psr = _psObj.GetComponent<ParticleSystemRenderer>();
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Sprites/Default");

        if (particleShader != null)
        {
            _particleMat = new Material(particleShader);
            _particleMat.SetFloat("_Blend", 1);
            psr.material = _particleMat;
        }

        ps.Simulate(25f, true, true, false);
        ps.Play();
    }

    void OnDestroy()
    {
        if (_gradientTex != null) Destroy(_gradientTex);
        if (_particleMat  != null) Destroy(_particleMat);
        // _canvasObj and _psObj are parented to us — Unity destroys them automatically
    }
}
