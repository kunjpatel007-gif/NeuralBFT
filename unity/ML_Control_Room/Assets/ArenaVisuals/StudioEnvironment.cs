using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lights the arena like a product studio: a soft key light with gentle shadows, a cool rim light
/// from behind, dim ambient fill, a faint depth haze, and a reflection environment with soft
/// light panels placed exactly where the lights are, so metal reads as metal.
/// </summary>
public static class StudioEnvironment
{
    public static readonly Color Haze = new Color(0.062f, 0.066f, 0.074f);

    static readonly Quaternion KeyRotation = Quaternion.Euler(50f, -35f, 0f);
    static readonly Quaternion RimRotation = Quaternion.Euler(18f, 160f, 0f);
    static readonly Quaternion FillRotation = Quaternion.Euler(20f, 70f, 0f);

    public static void Apply()
    {
        SetupLights();
        SetupAmbientAndHaze();
        SetupReflections();
    }

    static void SetupLights()
    {
        Light key = null;
        foreach (Light light in Object.FindObjectsByType<Light>())
            if (light.type == LightType.Directional) { key = light; break; }
        if (key == null) key = new GameObject("Key Light").AddComponent<Light>();

        key.type = LightType.Directional;
        key.transform.rotation = KeyRotation;
        key.color = new Color(1.0f, 0.975f, 0.95f);
        key.intensity = 1.6f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.8f;

        Light rim = new GameObject("Rim Light").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.transform.rotation = RimRotation;
        rim.color = new Color(0.78f, 0.86f, 1.0f);
        rim.intensity = 0.65f;
        rim.shadows = LightShadows.None;
    }

    static void SetupAmbientAndHaze()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.14f, 0.15f, 0.165f);
        RenderSettings.ambientEquatorColor = new Color(0.075f, 0.078f, 0.085f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.033f);

        // A little atmospheric depth: distant things settle into the background tone
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Haze;
        RenderSettings.fogDensity = 0.0075f;
    }

    static void SetupReflections()
    {
        Shader shader = Resources.Load<Shader>("ArenaShaders/StudioSky");
        if (shader == null || !shader.isSupported) return;

        var sky = new Material(shader) { name = "Arena Studio Sky" };
        sky.SetVector("_Sky", Linear(new Color(0.11f, 0.115f, 0.125f)));
        sky.SetVector("_Ground", Linear(new Color(0.03f, 0.03f, 0.033f)));
        // Each panel sits where its light comes from (the opposite of the light's forward)
        sky.SetVector("_KeyDir", -(KeyRotation * Vector3.forward));
        sky.SetVector("_RimDir", -(RimRotation * Vector3.forward));
        sky.SetVector("_FillDir", -(FillRotation * Vector3.forward));
        RenderSettings.skybox = sky; // the cameras clear to a solid colour, so this is only ever reflected

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        DynamicGI.UpdateEnvironment();

        // Render the studio once into a probe that covers the whole arena
        var probeObject = new GameObject("Studio Reflections");
        var probe = probeObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.cullingMask = 0; // the studio only, never the scene itself
        probe.resolution = 256;
        probe.hdr = true;
        probe.size = Vector3.one * 2000f;
        probe.boxProjection = false;
        probe.importance = 1;
        probe.RenderProbe();
    }

    static Vector4 Linear(Color color)
    {
        Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        return new Vector4(c.r, c.g, c.b, 1f);
    }
}
