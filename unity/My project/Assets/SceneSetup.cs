using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drop this on any empty GameObject in the scene.
/// It configures post-processing, lighting, and ambient color on startup
/// to give the entire scene a professional cyberpunk look.
/// </summary>
public class SceneSetup : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindAnyObjectByType<SceneSetup>() == null)
        {
            GameObject go = new GameObject("SceneSetup_Auto");
            go.AddComponent<SceneSetup>();
        }
    }

    void Start()
    {
        // Nuke the old stray Particle System that was spawning the white hovering rectangles!
        GameObject strayParticles = GameObject.Find("Particle System");
        if (strayParticles != null) Destroy(strayParticles);

        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = Color.black;
        }

        SetupPostProcessing();
        SetupLighting();
        SetupAmbient();
        SetupBackground();
    }

    void SetupBackground()
    {
        // Automatically spawn the custom InfiniteVoidBackground provided by the user
        if (FindAnyObjectByType<InfiniteVoidBackground>() == null)
        {
            // Create a giant quad and parent it to the main camera so it's always in view
            GameObject voidQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            voidQuad.name = "InfiniteVoidBackground";
            
            // Remove the mesh collider so it doesn't block raycasts
            Destroy(voidQuad.GetComponent<MeshCollider>());
            
            // Prevent the giant quad from casting shadows and darkening the scene
            var mr = voidQuad.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            
            if (Camera.main != null)
            {
                voidQuad.transform.SetParent(Camera.main.transform);
                voidQuad.transform.localPosition = new Vector3(0, 0, 50f); 
                voidQuad.transform.localRotation = Quaternion.identity;
                
                // CRITICAL FIX: Calculate the EXACT size of the camera's view at Z=50
                // Otherwise we are zoomed 400% into the dead center of the shader's UVs, making it look black!
                float d = 50f;
                float h = 2.0f * d * Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float w = h * Camera.main.aspect;
                
                // Add a 10% buffer so edges don't clip if aspect ratio shifts
                voidQuad.transform.localScale = new Vector3(w * 1.1f, h * 1.1f, 1f);
            }

            var voidBg = voidQuad.AddComponent<InfiniteVoidBackground>();
            // MATCHING INDEX.HTML / STYLE.CSS COLOR SCHEME - BRIGHTENED BY 75%
            // Original --bg: #0a0a0b (0.039, 0.039, 0.043) * 1.75 = (0.068, 0.068, 0.075)
            voidBg.baseColor = new Color(0.068f, 0.068f, 0.075f); 
            // Original --faint: #4e4e53 (0.306, 0.306, 0.325) * 1.75 = (0.535, 0.535, 0.568)
            voidBg.accentColor = new Color(0.535f, 0.535f, 0.568f, 1f); 
            voidBg.vignetteInner = 0.2f; 
            voidBg.vignetteOuter = 0.85f; 
            voidBg.swirlStrength = 1.0f; // Minimal distraction
            voidBg.ringCount = 8f; // Minimal distraction
            
            // Boosted the stars/blue dots heavily and increased their density!
            voidBg.starDensity = 60f; 
            voidBg.particleCount = 0;    
        }
    }

    void CreateGridFloor()
    {
        // Create a subtle holographic grid floor using a LineRenderer grid
        GameObject gridGO = new GameObject("GridFloor");
        gridGO.transform.position = new Vector3(0, -10f, 0);

        float gridSize = 50f;
        float spacing = 2.5f;
        Color gridColor = new Color(0.15f, 0.35f, 0.5f, 0.15f);

        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        lineMat.color = gridColor;

        for (float x = -gridSize; x <= gridSize; x += spacing)
        {
            CreateGridLine(gridGO.transform, lineMat, new Vector3(x, 0, -gridSize), new Vector3(x, 0, gridSize), gridColor);
        }
        for (float z = -gridSize; z <= gridSize; z += spacing)
        {
            CreateGridLine(gridGO.transform, lineMat, new Vector3(-gridSize, 0, z), new Vector3(gridSize, 0, z), gridColor);
        }
    }

    void CreateGridLine(Transform parent, Material mat, Vector3 start, Vector3 end, Color color)
    {
        GameObject lineGO = new GameObject("GridLine");
        lineGO.transform.SetParent(parent);
        var lr = lineGO.AddComponent<LineRenderer>();
        lr.material = mat;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = 0.03f;
        lr.endWidth = 0.03f;
        lr.positionCount = 2;
        lr.SetPositions(new Vector3[] { start, end });
        lr.useWorldSpace = false;
    }

    void SetupPostProcessing()
    {
        // Find the global Volume (URP creates one by default)
        Volume volume = FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            // Create one if it doesn't exist
            GameObject volumeGO = new GameObject("Global Volume");
            volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        VolumeProfile profile = volume.profile;

        // --- BLOOM: Makes the glowing nodes radiate neon light halos ---
        if (!profile.TryGet(out Bloom bloom))
        {
            bloom = profile.Add<Bloom>(true);
        }
        bloom.active = true;
        bloom.intensity.Override(1.2f);
        bloom.threshold.Override(0.8f);
        bloom.scatter.Override(0.65f);
        bloom.tint.Override(new Color(0.9f, 0.95f, 1.0f)); // Slight cool tint

        // --- VIGNETTE: Darkens screen edges for cinematic command-center look ---
        if (!profile.TryGet(out Vignette vignette))
        {
            vignette = profile.Add<Vignette>(true);
        }
        vignette.active = true;
        vignette.intensity.Override(0.35f); // Restored darker edge vignette
        vignette.color.Override(new Color(0.0f, 0.02f, 0.05f)); 

        // --- COLOR ADJUSTMENTS: Punch up the neon contrast ---
        if (!profile.TryGet(out ColorAdjustments colorAdj))
        {
            colorAdj = profile.Add<ColorAdjustments>(true);
        }
        colorAdj.active = true;
        colorAdj.contrast.Override(12f); // Restored deep contrast
        colorAdj.saturation.Override(15f); 
        colorAdj.postExposure.Override(0.0f); // Removed artificial brightness boost
        colorAdj.colorFilter.Override(new Color(0.85f, 0.92f, 1.0f)); 

        // --- TONEMAPPING: Professional film-grade color curve ---
        if (!profile.TryGet(out Tonemapping tonemap))
        {
            tonemap = profile.Add<Tonemapping>(true);
        }
        tonemap.active = true;
        tonemap.mode.Override(TonemappingMode.ACES);

        Debug.Log("[SceneSetup] Post-processing configured: Bloom + Vignette + Color Grading + ACES Tonemapping");
    }

    void SetupLighting()
    {
        // Find the main directional light and tune it
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var light in lights)
        {
            if (light.type == LightType.Directional)
            {
                light.intensity = 0.6f; // Dim the main light so nodes glow more
                light.color = new Color(0.7f, 0.8f, 1.0f); // Cool blue-white
                light.shadowStrength = 0.5f;
            }
        }
    }

    void SetupAmbient()
    {
        // Deep space ambient — very dark with a subtle blue tint
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.02f, 0.03f, 0.06f);
    }
}
