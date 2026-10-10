using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A sample travelling through the decision tree: a small polished shard that follows a list of
/// waypoints along the branches with a faint trail behind it. Honest samples rest at their leaf
/// and then shrink away; Byzantine ones are caught at the leaf and implode. No flashes.
/// </summary>
public class DataPulse : MonoBehaviour
{
    private const float LingerSeconds = 1.0f;
    private const float FadeSeconds = 0.5f;
    // Byzantine purge: caught by a closing ring, held, then broken into drifting splinters
    private const float CaptureSeconds = 0.4f;   // the ring closes in around the sample
    private const float HoldSeconds = 0.25f;     // it tightens; the sample trembles and stops turning
    private const float ShatterSeconds = 0.9f;   // splinters drift apart and shrink away
    private const int SplinterCount = 9;

    private static Mesh _shardMesh, _ringMesh;

    private Transform _ring;
    private Transform[] _splinters;
    private Vector3[] _splinterVelocity, _splinterSpin;
    private float _spinScale = 1f;

    private List<Vector3> _waypoints;
    private float _secondsPerSegment = 1.0f;
    private int _currentTarget = 1;
    private float _t;
    private bool _initialized, _isByzantine;

    private Color _color = Color.white;
    private float _size = 0.5f;
    private int _nodeStride = 1; // every Nth waypoint is a tree node; the rest shape the curve

    private Transform _shard;
    private Vector3 _spinAxis;
    private TrailRenderer _trail;
    private MaterialPropertyBlock _block;
    private float _age;
    private float _endAge = -1f; // seconds since the path finished, or -1 while travelling

    /// <summary>Called with the index of each tree node the sample reaches (0 = the root).</summary>
    public System.Action<int> onNodeReached;

    /// <summary>Sets the look. Call before <see cref="Initialize"/>.</summary>
    public void Configure(Color color, float size, int nodeStride)
    {
        _color = color;
        _size = size;
        _nodeStride = Mathf.Max(1, nodeStride);
        BuildVisuals();
    }

    public void Initialize(List<Vector3> waypoints, float secondsPerSegment, bool isByzantine = false)
    {
        _waypoints = waypoints;
        _secondsPerSegment = Mathf.Max(0.001f, secondsPerSegment);
        _currentTarget = 1;
        _t = 0f;
        _age = 0f;
        _isByzantine = isByzantine;
        _initialized = true;

        BuildVisuals();
        if (_waypoints != null && _waypoints.Count > 0)
            transform.localPosition = _waypoints[0];
        _trail.Clear();
        SetScale(0f);
    }

    void BuildVisuals()
    {
        if (_block == null) _block = new MaterialPropertyBlock();

        if (_shard == null)
        {
            if (_shardMesh == null) _shardMesh = ArenaGeometry.ChamferedIcosahedron(0.5f, 1.25f);
            MeshRenderer renderer = ArenaMaterials.Create(transform, "Shard", _shardMesh, ArenaMaterials.Anodized, false);
            _shard = renderer.transform;
            _spinAxis = Random.onUnitSphere;
            _shard.localRotation = Random.rotationUniform;
        }
        _block.Clear();
        ArenaMaterials.Tint(_block, _color);
        _shard.GetComponent<MeshRenderer>().SetPropertyBlock(_block);

        if (_trail == null)
        {
            _trail = GetComponent<TrailRenderer>();
            if (_trail == null) _trail = gameObject.AddComponent<TrailRenderer>();
        }
        _trail.sharedMaterial = ArenaFX.Beam("trail", 0f, 1f, 0f, 1.2f);
        _trail.time = 0.45f;
        _trail.widthMultiplier = _size * 0.05f;
        _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        _trail.minVertexDistance = 0.05f;
        _trail.alignment = LineAlignment.View;
        _trail.textureMode = LineTextureMode.Stretch;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        _trail.colorGradient = fade;
        ArenaFX.Unlit(_trail);

        _block.Clear();
        ArenaFX.SetColor(_block, ArenaTheme.WithAlpha(_color, 0.22f));
        _trail.SetPropertyBlock(_block);
    }

    void Update()
    {
        if (!_initialized) return;

        float dt = Time.deltaTime;
        _age += dt;
        _shard.Rotate(_spinAxis, 80f * dt * _spinScale, Space.Self);

        if (_endAge >= 0f)
        {
            Finish(dt);
            return;
        }

        // Grow in over the first moments rather than popping into existence
        SetScale(ArenaFX.EaseOutCubic(Mathf.Clamp01(_age / 0.3f)));

        if (_waypoints == null || _currentTarget >= _waypoints.Count)
        {
            _endAge = 0f;
            _trail.emitting = false;
            return;
        }

        _t += dt / _secondsPerSegment;
        Vector3 from = _waypoints[_currentTarget - 1];
        Vector3 to = _waypoints[_currentTarget];
        transform.localPosition = Vector3.Lerp(from, to, Mathf.Clamp01(_t));

        if (_t >= 1f)
        {
            _t = 0f;
            // Every Nth waypoint is a tree node; the ones between only shape the curve
            if (_currentTarget % _nodeStride == 0 && onNodeReached != null)
            {
                try { onNodeReached(_currentTarget / _nodeStride); }
                catch (System.Exception ex) { Debug.LogWarning($"[DataPulse] {ex.Message}"); }
            }
            _currentTarget++;
        }
    }

    void Finish(float dt)
    {
        _endAge += dt;

        float visible;
        float lifetime;
        if (_isByzantine)
        {
            Purge(dt);
            return;
        }
        else
        {
            // Rests at its leaf, then shrinks away
            visible = 1f - ArenaFX.EaseOutCubic(Mathf.Clamp01((_endAge - LingerSeconds) / FadeSeconds));
            lifetime = LingerSeconds + FadeSeconds;
        }
        SetScale(visible);

        if (_endAge >= lifetime) Destroy(gameObject);
    }

    // ------------------------------------------------------------------
    // Byzantine purge (~1.55 s, no flashes)
    // ------------------------------------------------------------------
    void Purge(float dt)
    {
        float age = _endAge;
        Transform cam = Camera.main != null ? Camera.main.transform : null;

        if (_ring == null)
        {
            if (_ringMesh == null) _ringMesh = ArenaGeometry.Torus(1f, 0.035f, 64, 8);
            _ring = ArenaMaterials.Create(transform, "PurgeRing", _ringMesh, ArenaMaterials.Titanium, false).transform;
        }

        // The ring always faces the viewer, so it reads as a clean circle closing in
        if (cam != null)
        {
            Vector3 toCam = cam.position - _ring.position;
            if (toCam.sqrMagnitude > 1e-6f)
                _ring.rotation = Quaternion.FromToRotation(Vector3.up, toCam.normalized) * Quaternion.Euler(0f, age * 120f, 0f);
        }

        float captureEnd = CaptureSeconds, holdEnd = CaptureSeconds + HoldSeconds;

        if (age < holdEnd)
        {
            // 1. Capture: the ring closes from wide to snug, the sample's spin winds down
            float close = ArenaFX.EaseOutCubic(Mathf.Clamp01(age / captureEnd));
            float tighten = ArenaFX.EaseInOut(Mathf.Clamp01((age - captureEnd) / HoldSeconds));
            float ringSize = Mathf.Lerp(_size * 2.4f, _size * 0.62f, close) * Mathf.Lerp(1f, 0.82f, tighten);
            _ring.localScale = Vector3.one * ringSize;
            _spinScale = 1f - close;

            // 2. Hold: a fine tremble as the ring tightens
            float tremble = tighten * (1f - tighten) * 4f * _size * 0.04f;
            _shard.localPosition = Random.insideUnitSphere * tremble;
            SetScale(1f - 0.12f * tighten);
            return;
        }

        // 3. Shatter: splinters drift outward, turning, and shrink to nothing
        if (_splinters == null) Shatter();

        float t = Mathf.Clamp01((age - holdEnd) / ShatterSeconds);
        float drag = Mathf.Exp(-3f * dt); // they glide to a stop rather than flying off
        float shrink = 1f - t * t;
        for (int i = 0; i < _splinters.Length; i++)
        {
            _splinterVelocity[i] *= drag;
            _splinters[i].localPosition += _splinterVelocity[i] * dt;
            _splinters[i].Rotate(_splinterSpin[i] * dt, Space.Self);
            _splinters[i].localScale = Vector3.one * (_size * 0.17f * shrink);
        }

        // The ring lets go: it narrows to nothing
        _ring.localScale = Vector3.one * (_size * 0.62f * 0.82f * (1f - ArenaFX.EaseInOut(Mathf.Clamp01(t * 1.6f))));

        if (t >= 1f) Destroy(gameObject);
    }

    void Shatter()
    {
        _shard.gameObject.SetActive(false);
        _splinters = new Transform[SplinterCount];
        _splinterVelocity = new Vector3[SplinterCount];
        _splinterSpin = new Vector3[SplinterCount];

        for (int i = 0; i < SplinterCount; i++)
        {
            MeshRenderer piece = ArenaMaterials.Create(transform, "Splinter", _shardMesh, ArenaMaterials.Anodized, false);
            _block.Clear();
            // Mostly the sample's own colour, a few in bare graphite
            ArenaMaterials.Tint(_block, i % 3 == 0 ? new Color(0.2f, 0.205f, 0.215f) : _color);
            piece.SetPropertyBlock(_block);

            Vector3 dir = Random.onUnitSphere;
            piece.transform.localPosition = dir * _size * 0.12f;
            piece.transform.localRotation = Random.rotationUniform;
            piece.transform.localScale = Vector3.one * (_size * 0.17f);

            _splinters[i] = piece.transform;
            _splinterVelocity[i] = dir * (_size * Random.Range(1.4f, 2.6f));
            _splinterSpin[i] = Random.onUnitSphere * Random.Range(120f, 320f);
        }
    }

    void SetScale(float visible)
    {
        _shard.localScale = Vector3.one * (_size * 0.55f * visible);
    }
}
