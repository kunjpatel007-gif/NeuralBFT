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
        // SetupBackground(); // Removed: Replaced by BackgroundManager
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
