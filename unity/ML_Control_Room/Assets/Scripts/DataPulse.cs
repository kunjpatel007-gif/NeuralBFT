using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class DataPulse : MonoBehaviour
{
    private List<Vector3> _waypoints;
    private float _speed = 1.0f; 
    private int _currentTarget = 1;
    private float _t = 0f;
    private bool _initialized = false;
    private bool _finished = false;
    private bool _isByzantine = false;

    public void Initialize(List<Vector3> waypoints, float secondsPerSegment, bool isByzantine = false)
    {
        _waypoints = waypoints;
        _speed = secondsPerSegment;
        _currentTarget = 1;
        _t = 0f;
        _isByzantine = isByzantine;
        _initialized = true;

        if (_waypoints.Count > 0)
            transform.localPosition = _waypoints[0];
    }

    void Update()
    {
        if (!_initialized || _finished) return;
        if (_waypoints == null || _currentTarget >= _waypoints.Count)
        {
            _finished = true;
            if (_isByzantine) {
                SpawnDestructionEffect();
                Destroy(gameObject);
            } else {
                StartCoroutine(FadeAndDestroy());
            }
            return;
        }

        _t += Time.deltaTime / _speed;
        Vector3 from = _waypoints[_currentTarget - 1];
        Vector3 to = _waypoints[_currentTarget];

        float eased = _t * _t * (3f - 2f * _t); 
        transform.localPosition = Vector3.Lerp(from, to, eased);

        // Remove the pulse scaling if we want it to look like a solid physical prefab
        // But if it's not a prefab, maybe keep it? Let's just keep the scale stable so the prefab looks good.

        if (_t >= 1f)
        {
            _t = 0f;
            _currentTarget++;
            // Only flash honest nodes, don't flash bad nodes (they are stealthy until destroyed)
            if (!_isByzantine) FlashNode(_currentTarget - 1);
        }
    }

    void FlashNode(int waypointIndex)
    {
        if (waypointIndex < 0 || waypointIndex >= _waypoints.Count) return;

        GameObject flash = new GameObject("Flash");
        flash.transform.SetParent(transform.parent);
        flash.transform.localPosition = _waypoints[waypointIndex];

        Light flashLight = flash.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.range = 1.5f;
        flashLight.intensity = 3.0f;

        Renderer rend = GetComponent<Renderer>();
        if (rend != null && rend.material != null)
            flashLight.color = rend.material.GetColor("_BaseColor");
        else
            flashLight.color = Color.white;

        Destroy(flash, 0.2f);
    }

    void SpawnDestructionEffect()
    {
        GameObject fx = new GameObject("FlushFX");
        fx.transform.position = transform.position;
        fx.transform.SetParent(transform.parent);

        ParticleSystem ps = fx.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f); 
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 14f); 
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.02f); // Very thin
        main.startColor = new Color(1f, 0.1f, 0.1f, 1f); 
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 15, 25) }); // Fewer, cleaner sparks
        
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        
        main.gravityModifier = 0f; // Shoot straight like lasers, no gravity arc

        var renderer = fx.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // Sci-fi spark lines
        renderer.lengthScale = 4.0f; 
        
        Renderer myRend = GetComponent<Renderer>();
        if (myRend != null) renderer.sharedMaterial = myRend.sharedMaterial;

        // Very subtle flash so it's not blinding when it happens frequently
        Light flashLight = fx.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.range = 2.0f;
        flashLight.intensity = 2.0f;
        flashLight.color = new Color(1f, 0.1f, 0.1f, 1f);

        fx.AddComponent<FlushAnimator>();
    }

    IEnumerator FadeAndDestroy()
    {
        yield return new WaitForSeconds(1.5f);
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = rend.material;
            Color baseColor = mat.GetColor("_BaseColor");

            float fadeDuration = 0.5f;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
                mat.SetColor("_BaseColor", new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
                yield return null;
            }
        }
        Destroy(gameObject);
    }
}

public class FlushAnimator : MonoBehaviour
{
    private Light _light;
    private float _elapsed = 0f;
    private float _duration = 0.35f;
    private LineRenderer _ring1, _ring2;

    void Start()
    {
        _light = GetComponent<Light>();
        
        Material m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit"));
        m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.8f));
        if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(1f, 0.1f, 0.1f, 0.8f));
        
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        _ring1 = CreateRing("Ring1", m, 0.04f);
        _ring2 = CreateRing("Ring2", m, 0.01f);

        Destroy(gameObject, 0.5f);
    }
    
    LineRenderer CreateRing(string n, Material mat, float width)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 32;
        lr.loop = true;
        lr.startWidth = width; lr.endWidth = width;
        lr.sharedMaterial = mat;
        return lr;
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        
        if (_light != null) _light.intensity = Mathf.Lerp(2f, 0f, t);

        // Expanding horizontal data rings
        if (_ring1 != null) {
            float r1 = Mathf.Lerp(0.1f, 1.8f, Mathf.Sqrt(t)); 
            UpdateRing(_ring1, r1, Mathf.Lerp(0.7f, 0f, t*t));
        }
        if (_ring2 != null) {
            float r2 = Mathf.Lerp(0.1f, 2.5f, t); 
            UpdateRing(_ring2, r2, Mathf.Lerp(0.3f, 0f, t*t));
        }
    }
    
    void UpdateRing(LineRenderer lr, float radius, float alpha)
    {
        Color c = new Color(1f, 0.1f, 0.1f, alpha);
        lr.startColor = c;
        lr.endColor = c;
        for (int i = 0; i < 32; i++) {
            float angle = i * Mathf.PI * 2f / 32f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }
}
