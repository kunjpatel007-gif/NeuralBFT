using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lights and grades the picture. Runs automatically on load: studio lighting and reflections
/// (see StudioEnvironment), high-quality anti-aliasing, and a restrained, filmic grade with no
/// glare: bloom only on the brightest specular glints, a soft vignette and a trace of grain.
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
        // Remove the stray editor Particle System that used to spawn white hovering rectangles
        GameObject strayParticles = GameObject.Find("Particle System");
        if (strayParticles != null) Destroy(strayParticles);

        StudioEnvironment.Apply();
        SetupCamera();
        SetupPostProcessing();
    }

    void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = StudioEnvironment.Haze;
        cam.nearClipPlane = 0.1f;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.dithering = true; // hides banding in the dark gradients
            // Clean edges on fine rings and thin lines, on top of the pipeline's MSAA
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
    }

    void SetupPostProcessing()
    {
        // Find the global Volume (URP creates one by default), or make one
        Volume volume = FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            GameObject volumeGO = new GameObject("Global Volume");
            volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        VolumeProfile profile = volume.profile;

        // BLOOM: only the brightest specular glints get a faint halo
        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.intensity.Override(0.12f);
        bloom.threshold.Override(1.4f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(Color.white);

        // VIGNETTE: a gentle falloff that draws the eye to the centre
        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.5f);
        vignette.color.Override(new Color(0.01f, 0.011f, 0.013f));

        // COLOUR: neutral and slightly desaturated, so status colours read as information
        ColorAdjustments colorAdj = GetOrAdd<ColorAdjustments>(profile);
        colorAdj.contrast.Override(10f);
        colorAdj.saturation.Override(-6f);
        colorAdj.postExposure.Override(0.15f);
        colorAdj.colorFilter.Override(Color.white);

        // TONEMAPPING: Neutral rolls highlights off smoothly without shifting colours
        Tonemapping tonemap = GetOrAdd<Tonemapping>(profile);
        tonemap.mode.Override(TonemappingMode.Neutral);

        // DEPTH OF FIELD: off. It blurred the node cards and labels when seen from a distance
        if (profile.TryGet(out DepthOfField dof))
        {
            dof.mode.Override(DepthOfFieldMode.Off);
            dof.active = false;
        }

        // GRAIN: very fine, just enough to take the digital edge off
        FilmGrain grain = GetOrAdd<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.1f);
        grain.response.Override(0.8f);

        Debug.Log("[SceneSetup] Studio lighting, SMAA and a restrained filmic grade configured");
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
        component.active = true;
        return component;
    }
}
