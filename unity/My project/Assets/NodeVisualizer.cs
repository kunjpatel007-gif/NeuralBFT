using UnityEngine;

/// <summary>
/// Everything you see of a node, built as real lit geometry. The body is a chamfered solid in
/// anodised metal whose colour is the node's trust status. Around it, a Dyson sphere of five fine
/// titanium rings turns slowly, two of them carrying a small bead in the status colour that runs
/// along the ring. Between body and rings, a fine icosahedral cage of titanium struts turns against
/// the body's spin. A level ring carries the reputation as an arc, and a quarantined node is
/// enclosed by a slowly breathing containment ring. Every change eases in.
/// </summary>
public class NodeVisualizer : MonoBehaviour
{
    const int DysonRings = 5;
    const float DysonRadius = 0.9f;
    const float ReputationRadius = 1.18f;
    const float ContainmentRadius = 1.4f;
    const float AppearSeconds = 0.8f;
    const float DismissSeconds = 0.4f;
    const float CageRadius = 0.74f;
    const float BeadRadius = 0.034f;

    static Mesh _trackMesh, _containmentMesh, _cageMesh, _beadMesh;
    static readonly Mesh[] DysonMeshes = new Mesh[DysonRings];

    MeshRenderer[] _body;
    MeshRenderer _reputationArc, _containment;
    MeshFilter _reputationFilter;
    Mesh _arcMesh;
    readonly Transform[] _dyson = new Transform[DysonRings];
    readonly float[] _dysonSpeeds = new float[DysonRings];
    readonly Transform[] _beads = new Transform[2];
    readonly float[] _beadAngle = new float[2], _beadSpeed = new float[2];
    MeshRenderer[] _beadRenderers = new MeshRenderer[2];
    Transform _cage;
    MaterialPropertyBlock _block;

    bool _built, _hasData, _hasTarget, _dismissed;
    ArenaTheme.NodeState _state;
    Color _color = ArenaTheme.Verified, _targetColor = ArenaTheme.Verified;
    float _contain, _targetContain;
    float _reputation, _targetReputation, _drawnReputation = -1f;
    float _age, _dismissAge;
    Vector3 _baseScale = Vector3.one;
    Vector3 _targetPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _trackMesh = _containmentMesh = _cageMesh = _beadMesh = null;
        for (int i = 0; i < DysonMeshes.Length; i++) DysonMeshes[i] = null;
    }

    /// <summary>Creates the node's visual parts. Safe to call more than once.</summary>
    public void Build()
    {
        if (_built) return;
        _built = true;

        _block = new MaterialPropertyBlock();
        _baseScale = transform.localScale;
        transform.localScale = Vector3.zero; // grows in from nothing

        // The prefab's own renderers become the anodised body
        _body = GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in _body)
        {
            renderer.sharedMaterial = ArenaMaterials.Anodized;
            ArenaMaterials.Solid(renderer);
        }

        Transform fx = new GameObject("NodeFX").transform;
        fx.SetParent(transform, false);

        // Dyson sphere: five fine rings at random tilts, each turning at its own unhurried pace
        for (int i = 0; i < DysonRings; i++)
        {
            if (DysonMeshes[i] == null)
                DysonMeshes[i] = ArenaGeometry.Torus(DysonRadius + i * 0.025f, 0.011f, 96, 10);

            _dyson[i] = new GameObject("DysonRing_" + i).transform;
            _dyson[i].SetParent(fx, false);
            _dyson[i].localRotation = Random.rotationUniform;
            ArenaMaterials.Create(_dyson[i], "Ring", DysonMeshes[i], ArenaMaterials.Titanium);
            _dysonSpeeds[i] = Random.Range(12f, 30f) * (Random.value > 0.5f ? 1f : -1f);
        }

        // Beads riding along two of the Dyson rings, in the status colour
        if (_beadMesh == null) _beadMesh = ArenaGeometry.ChamferedIcosahedron(BeadRadius, 1f, 0.2f);
        for (int i = 0; i < _beads.Length; i++)
        {
            int ring = i * 2;
            _beadRenderers[i] = ArenaMaterials.Create(_dyson[ring], "Bead", _beadMesh, ArenaMaterials.Anodized, false);
            _beads[i] = _beadRenderers[i].transform;
            _beadAngle[i] = Random.value * 360f;
            _beadSpeed[i] = Random.Range(25f, 45f) * (i == 0 ? 1f : -1f);
        }

        // An icosahedral cage of fine struts between the body and the rings
        if (_cageMesh == null) _cageMesh = BuildCage(CageRadius, 0.0065f);
        _cage = ArenaMaterials.Create(fx, "Cage", _cageMesh, ArenaMaterials.Titanium).transform;
        _cage.localRotation = Random.rotationUniform;

        // Reputation: a thin full track with a thicker coloured arc laid along it
        if (_trackMesh == null) _trackMesh = ArenaGeometry.Torus(ReputationRadius, 0.005f, 128, 8);
        ArenaMaterials.Create(fx, "ReputationTrack", _trackMesh, ArenaMaterials.Steel, false);
        _reputationArc = ArenaMaterials.Create(fx, "ReputationArc", null, ArenaMaterials.Anodized, false);
        _reputationFilter = _reputationArc.GetComponent<MeshFilter>();

        // Containment ring, shown only while the node is quarantined
        if (_containmentMesh == null) _containmentMesh = ArenaGeometry.Torus(ContainmentRadius, 0.016f, 128, 10);
        Transform tilt = new GameObject("Containment").transform;
        tilt.SetParent(fx, false);
        tilt.localRotation = Quaternion.Euler(72f, 0f, 18f);
        _containment = ArenaMaterials.Create(tilt, "Ring", _containmentMesh, ArenaMaterials.Anodized);
        _containment.enabled = false;
    }

    void OnDestroy()
    {
        if (_arcMesh != null) Destroy(_arcMesh);
    }

    /// <summary>Called whenever new data for this node arrives from the backend.</summary>
    public void UpdateVisuals(NodeData data)
    {
        if (data == null) return;
        Build();

        _state = ArenaTheme.Classify(data.status);
        _targetColor = ArenaTheme.StatusColor(_state);
        _targetContain = ArenaTheme.ShieldStrength(_state);
        _targetReputation = Mathf.Clamp01(data.reputation / 100f);

        if (!_hasData)
        {
            // First sighting: start in the right colour rather than fading from the default
            _hasData = true;
            _color = _targetColor;
        }
    }

    /// <summary>Glide to a new position; snap makes the move immediate.</summary>
    public void MoveTo(Vector3 worldPosition, bool snap)
    {
        _targetPosition = worldPosition;
        _hasTarget = true;
        if (snap) transform.position = worldPosition;
    }

    /// <summary>Kept for callers from earlier versions; nodes no longer flash.</summary>
    public void Flash(float amount) { }

    /// <summary>Shrinks the node away, then destroys it.</summary>
    public void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
    }

    void Update()
    {
        if (!_built) return;

        float dt = Time.deltaTime;
        _age += dt;

        // Scale: ease in on arrival, ease out on dismissal
        float scale = ArenaFX.EaseOutCubic(_age / AppearSeconds);
        if (_dismissed)
        {
            _dismissAge += dt;
            float t = Mathf.Clamp01(_dismissAge / DismissSeconds);
            scale *= 1f - t * t;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
        }
        transform.localScale = _baseScale * scale;

        if (_hasTarget)
            transform.position = Vector3.Lerp(transform.position, _targetPosition, ArenaFX.Damp(5f, dt));

        // Ease every value toward its target
        float ease = ArenaFX.Damp(3.5f, dt);
        _color = Color.Lerp(_color, _targetColor, ease);
        _contain = Mathf.Lerp(_contain, _targetContain, ease);
        _reputation = Mathf.Lerp(_reputation, _targetReputation, ease);

        // A slow, stately turn; blacklisted nodes stop entirely, rings included
        if (_state != ArenaTheme.NodeState.Blacklisted)
        {
            transform.Rotate(0f, 5f * dt, 0f, Space.World);
            for (int i = 0; i < DysonRings; i++)
                _dyson[i].Rotate(Vector3.right, _dysonSpeeds[i] * dt, Space.Self);

            // The cage turns gently against the body's spin, on a tilted axis
            _cage.Rotate(new Vector3(0.3f, -1f, 0.2f), 9f * dt, Space.Self);

            for (int i = 0; i < _beads.Length; i++)
            {
                _beadAngle[i] += _beadSpeed[i] * dt;
                float r = DysonRadius + i * 2 * 0.025f; // radius of the ring the bead rides
                float a = _beadAngle[i] * Mathf.Deg2Rad;
                _beads[i].localPosition = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            }
        }

        // Quarantined nodes breathe, slowly: their colour deepens and recovers
        float breath = _state == ArenaTheme.NodeState.Quarantined ? 0.82f + 0.18f * Mathf.Sin(_age * 1.6f) : 1f;

        Paint(breath);
        DrawReputationArc();
    }

    void Paint(float breath)
    {
        _block.Clear();
        ArenaMaterials.Tint(_block, _color * breath);
        foreach (MeshRenderer renderer in _body)
            if (renderer != null) renderer.SetPropertyBlock(_block);
        _reputationArc.SetPropertyBlock(_block);
        foreach (MeshRenderer bead in _beadRenderers)
            if (bead != null) bead.SetPropertyBlock(_block);

        bool contained = _contain > 0.05f;
        if (_containment.enabled != contained) _containment.enabled = contained;
        if (contained)
        {
            _containment.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, _contain);
            _containment.SetPropertyBlock(_block);
        }
    }

    /// <summary>The 30 edges of an icosahedron as fine tubes, with small joints at the 12 vertices.</summary>
    static Mesh BuildCage(float radius, float strut)
    {
        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new Vector3[]
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * radius;
        float edge = Vector3.Distance(v[0], v[1]);

        var tubes = new ArenaGeometry.TubeBuilder();
        var pair = new Vector3[2];
        for (int i = 0; i < v.Length; i++)
        {
            for (int j = i + 1; j < v.Length; j++)
            {
                if (Mathf.Abs(Vector3.Distance(v[i], v[j]) - edge) > 0.01f) continue;
                pair[0] = v[i];
                pair[1] = v[j];
                tubes.Add(pair, strut, strut, 6, false);
            }

            // A short, slightly thicker joint where five struts meet
            Vector3 n = v[i].normalized;
            pair[0] = v[i] - n * strut * 2.2f;
            pair[1] = v[i] + n * strut * 2.2f;
            tubes.Add(pair, strut * 2.4f, strut * 2.4f, 10, true);
        }
        return tubes.Build("Node Cage");
    }

    void DrawReputationArc()
    {
        if (Mathf.Abs(_reputation - _drawnReputation) < 0.004f) return;
        _drawnReputation = _reputation;

        if (_arcMesh != null) Destroy(_arcMesh);
        _arcMesh = _reputation > 0.004f ? ArenaGeometry.Torus(ReputationRadius, 0.016f, 128, 10, _reputation) : null;
        _reputationFilter.sharedMesh = _arcMesh;
    }
}
