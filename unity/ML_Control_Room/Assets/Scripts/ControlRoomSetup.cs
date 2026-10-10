using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dresses and grades the ML Control Room exactly like the Consensus Arena. Runs automatically on
/// load: studio lighting and reflections (see StudioEnvironment), a graphite void, a polished dark
/// floor with an engraved grid under the tree, high-quality anti-aliasing and a restrained grade.
/// </summary>
public class ControlRoomSetup : MonoBehaviour
{
    const float BackdropSize = 600f; // diameter; stays well inside the camera's far plane
    const float FloorSize = 260f;

    private Transform _backdrop;
    private Camera _camera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindAnyObjectByType<ControlRoomSetup>() == null)
            new GameObject("ControlRoomSetup_Auto").AddComponent<ControlRoomSetup>();
    }

    void Start()
    {
        StudioEnvironment.Apply();
        SetupCamera();
        SetupPostProcessing();
        CreateBackdrop();
        CreateFloor();
    }

    void LateUpdate()
    {
        // The backdrop travels with the camera, so the stars always feel infinitely far away
        if (_camera == null) _camera = Camera.main;
        if (_camera != null && _backdrop != null) _backdrop.position = _camera.transform.position;
    }

    void SetupCamera()
    {
        _camera = Camera.main;
        if (_camera == null) return;

        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = StudioEnvironment.Haze;
        _camera.nearClipPlane = 0.1f;

        UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.dithering = true; // hides banding in the dark gradients
            // Clean edges on fine twigs and rings, on top of the pipeline's MSAA
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
    }

    void SetupPostProcessing()
    {
        Volume volume = FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        VolumeProfile profile = volume.profile;

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.intensity.Override(0.12f);
        bloom.threshold.Override(1.4f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(Color.white);

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.5f);
        vignette.color.Override(new Color(0.01f, 0.011f, 0.013f));

        ColorAdjustments colorAdj = GetOrAdd<ColorAdjustments>(profile);
        colorAdj.contrast.Override(10f);
        colorAdj.saturation.Override(-6f);
        colorAdj.postExposure.Override(0.15f);
        colorAdj.colorFilter.Override(Color.white);

        Tonemapping tonemap = GetOrAdd<Tonemapping>(profile);
        tonemap.mode.Override(TonemappingMode.Neutral);

        FilmGrain grain = GetOrAdd<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.1f);
        grain.response.Override(0.8f);
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
        component.active = true;
        return component;
    }

    void CreateBackdrop()
    {
        MeshRenderer renderer = ArenaFX.CreateMeshObject(transform, "Backdrop", ArenaFX.Sphere, ArenaFX.BackdropMaterial);
        _backdrop = renderer.transform;
        _backdrop.localScale = Vector3.one * BackdropSize;
    }

    void CreateFloor()
    {
        // The floor sits at the foot of the tree
        ForestRenderer forest = FindAnyObjectByType<ForestRenderer>();
        Vector3 origin = forest != null ? forest.transform.position : Vector3.zero;

        MeshRenderer renderer = ArenaMaterials.Create(transform, "Floor", ArenaFX.FloorQuad, ArenaMaterials.Floor(FloorSize), false);
        renderer.transform.position = origin + Vector3.down * 0.05f;
        renderer.transform.localScale = Vector3.one * FloorSize;
    }
}
