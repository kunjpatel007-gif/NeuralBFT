using UnityEngine;

/// <summary>
/// The arena's surroundings: a graphite void with a slight lift at eye level, a polished dark
/// floor with a fine engraved grid that receives the scene's soft shadows and fades into the
/// haze, and a few slow specks of dust for depth. Everything is parented to this object.
/// </summary>
public class BackgroundManager : MonoBehaviour
{
    [Tooltip("World height of the floor. The ledger's plinth stands on it.")]
    public float floorHeight = -15.52f;

    const float BackdropSize = 600f; // diameter; stays well inside the camera's far plane
    const float FloorSize = 400f;

    private Transform _backdrop;
    private Camera _camera;

    void Start()
    {
        _camera = Camera.main;
        if (_camera != null)
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = StudioEnvironment.Haze;
        }

        Transform root = new GameObject("ArenaBackground").transform;
        root.SetParent(transform, true);

        CreateBackdrop(root);
        CreateFloor(root);
        CreateMotes(root);
    }

    void LateUpdate()
    {
        // The backdrop travels with the camera, so it always reads as infinitely far away
        if (_camera == null) _camera = Camera.main;
        if (_camera != null && _backdrop != null) _backdrop.position = _camera.transform.position;
    }

    void CreateBackdrop(Transform root)
    {
        MeshRenderer renderer = ArenaFX.CreateMeshObject(root, "Backdrop", ArenaFX.Sphere, ArenaFX.BackdropMaterial);
        _backdrop = renderer.transform;
        _backdrop.localScale = Vector3.one * BackdropSize;
    }

    void CreateFloor(Transform root)
    {
        MeshRenderer renderer = ArenaMaterials.Create(root, "Floor", ArenaFX.FloorQuad, ArenaMaterials.Floor(FloorSize), false);
        renderer.transform.position = new Vector3(0f, floorHeight, 0f);
        renderer.transform.localScale = Vector3.one * FloorSize;
    }

    void CreateMotes(Transform root)
    {
        var go = new GameObject("AmbientMotes");
        go.transform.SetParent(root, false);
        go.transform.position = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 26f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.76f, 0.80f, 0.18f),
            new Color(0.80f, 0.84f, 0.88f, 0.12f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 1.8f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 34f;
        shape.radiusThickness = 1f; // fill the volume, not just the surface

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);

        // Fade in and out so motes never pop
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = ArenaFX.Glow(false, false);
        ArenaFX.Unlit(renderer);

        ps.Play();
    }
}
