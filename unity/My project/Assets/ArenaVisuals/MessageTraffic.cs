using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws network traffic. Every message is a small chamfered shard in anodised metal that turns
/// as it travels, slowly and at constant speed, along a gentle arc from sender to receiver, so it
/// can be followed and clicked to inspect its payload. While messages are on a link, an engineered
/// conduit traces the route: a fine dark polished rail that starts at each node's outer shell with
/// a titanium port collar, threaded with small anodised collars that slide along it at exactly
/// the speed of the messages, in the direction the traffic is flowing. It thins away once the
/// link falls quiet.
/// </summary>
public class MessageTraffic : MonoBehaviour
{
    [Tooltip("Seconds a message takes to travel between two nodes.")]
    public float flightSeconds = 7.3f;

    [Tooltip("Upper limit on messages in flight at once.")]
    public int maxLiveMessages = 400;

    const int PipeSegments = 24;      // samples along the rail
    const int ArcSamples = 64;        // samples for measuring length along the curve
    const float PipeRadius = 0.008f;
    const float CollarRadius = 0.021f;
    const float CollarHalfLength = 0.028f;
    const float CollarSpacing = 0.8f;
    const float PortRadius = 0.034f;
    const float PortHalfLength = 0.045f;
    const float NodeShell = 1.08f;    // the rail starts just outside a node's rings (local units)
    const int MaxCollars = 48;
    const float ShardSize = 0.17f;
    const float EndFade = 0.05f; // share of the flight spent growing in and shrinking out

    class Shard
    {
        public GameObject gameObject;
        public Transform transform;
        public MeshRenderer renderer;
        public TrailRenderer trail;
        public MessageInteractable info;
        public Transform from, to;
        public Pipe pipe;
        public int direction; // +1 when travelling from the pipe's first node to its second
        public Color color;
        public Vector3 spinAxis;
        public float delay, age;
        public bool flying;
    }

    class Pipe
    {
        public (Transform, Transform) key;
        public MeshRenderer rail, collars;
        public Mesh railMesh, collarMesh;
        public int flow;           // messages travelling a->b minus those travelling b->a
        public float phase, phaseSpeed;
        public Color color;
        public int inFlight;
        public float alpha;
    }

    static Mesh _shardMesh;

    readonly List<Shard> _live = new List<Shard>();
    readonly Stack<Shard> _pool = new Stack<Shard>();
    readonly Dictionary<(Transform, Transform), Pipe> _pipes = new Dictionary<(Transform, Transform), Pipe>();
    readonly List<Pipe> _pipeList = new List<Pipe>();
    readonly Vector3[] _curve = new Vector3[PipeSegments];

    readonly ArenaGeometry.TubeBuilder _tube = new ArenaGeometry.TubeBuilder();
    readonly Vector3[] _dense = new Vector3[ArcSamples + 1];
    readonly float[] _length = new float[ArcSamples + 1];
    readonly Vector3[] _pair = new Vector3[2];

    MaterialPropertyBlock _block;
    Material _trailMaterial;
    Gradient _trailFade;
    AnimationCurve _trailWidth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _shardMesh = null;
    }

    void Awake()
    {
        _block = new MaterialPropertyBlock();
        if (_shardMesh == null) _shardMesh = ArenaGeometry.ChamferedIcosahedron(0.5f, 1f, 0.18f);
        _trailMaterial = ArenaFX.Beam("trail", 0f, 1f, 0f, 1.2f);

        _trailFade = new Gradient();
        _trailFade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        _trailWidth = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    }

    /// <summary>
    /// Sends a message from one node to another. Returns the click-to-inspect payload so the
    /// caller can fill it in, or null if the scene is already at its message limit.
    /// </summary>
    public MessageInteractable Launch(Transform from, Transform to, Color color)
    {
        if (from == null || to == null || _live.Count >= maxLiveMessages) return null;

        Shard shard = _pool.Count > 0 ? _pool.Pop() : CreateShard();
        shard.from = from;
        shard.to = to;
        shard.color = color;
        shard.pipe = GetPipe(from, to, color);
        shard.spinAxis = Random.onUnitSphere;
        shard.delay = Random.Range(0f, 1.6f); // stagger departures so messages on one link don't overlap
        shard.age = 0f;
        shard.flying = false;
        _live.Add(shard);
        return shard.info;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateShards(dt);
        UpdatePipes(dt);
    }

    // ------------------------------------------------------------------
    // Shards
    // ------------------------------------------------------------------

    Shard CreateShard()
    {
        var go = new GameObject("Message");
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        var shard = new Shard { gameObject = go, transform = go.transform };
        go.AddComponent<MeshFilter>().sharedMesh = _shardMesh;
        shard.renderer = go.AddComponent<MeshRenderer>();
        shard.renderer.sharedMaterial = ArenaMaterials.Anodized;
        ArenaMaterials.Solid(shard.renderer, false);

        // A faint wake behind the shard, so its direction of travel reads at a glance
        shard.trail = go.AddComponent<TrailRenderer>();
        shard.trail.sharedMaterial = _trailMaterial;
        shard.trail.time = 0.6f;
        shard.trail.widthMultiplier = 0.035f;
        shard.trail.widthCurve = _trailWidth;
        shard.trail.colorGradient = _trailFade;
        shard.trail.minVertexDistance = 0.04f;
        shard.trail.alignment = LineAlignment.View;
        shard.trail.textureMode = LineTextureMode.Stretch;
        ArenaFX.Unlit(shard.trail);

        // A generous hit area so a moving message is easy to click (relative to the shard's scale)
        go.AddComponent<SphereCollider>().radius = 0.4f / ShardSize;
        shard.info = go.AddComponent<MessageInteractable>();
        return shard;
    }

    void UpdateShards(float dt)
    {
        float duration = Mathf.Max(0.2f, flightSeconds);

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Shard shard = _live[i];

            // Either endpoint may leave the network mid-flight
            if (shard.from == null || shard.to == null)
            {
                Recycle(i);
                continue;
            }

            if (shard.delay > 0f)
            {
                shard.delay -= dt;
                continue;
            }

            Vector3 start = shard.from.position;
            Vector3 end = shard.to.position;

            if (!shard.flying)
            {
                shard.flying = true;
                shard.pipe.inFlight++;
                shard.direction = shard.from == shard.pipe.key.Item1 ? 1 : -1;
                shard.pipe.flow += shard.direction;
                shard.transform.position = start;
                shard.transform.localScale = Vector3.zero;
                shard.gameObject.SetActive(true);
                shard.trail.Clear();
                shard.trail.emitting = true;
                Paint(shard);
            }

            shard.age += dt;
            float t = Mathf.Clamp01(shard.age / duration);

            // Constant speed along the arc: a steady target is far easier to click
            shard.transform.position = ArenaFX.Bezier(start, ControlPoint(start, end), end, t);
            shard.transform.Rotate(shard.spinAxis, 90f * dt, Space.World);

            // Grow in as it leaves the sender, swell slightly mid-route, shrink into the receiver
            float presence = ArenaFX.EaseInOut(Mathf.Clamp01(t / EndFade)) * ArenaFX.EaseInOut(Mathf.Clamp01((1f - t) / EndFade));
            float swell = 0.9f + 0.2f * Mathf.Sin(t * Mathf.PI);
            shard.transform.localScale = Vector3.one * (ShardSize * presence * swell);

            if (t >= 1f) Recycle(i);
        }
    }

    void Paint(Shard shard)
    {
        _block.Clear();
        ArenaMaterials.Tint(_block, shard.color);
        shard.renderer.SetPropertyBlock(_block);

        _block.Clear();
        ArenaFX.SetColor(_block, ArenaTheme.WithAlpha(shard.color, 0.35f));
        shard.trail.SetPropertyBlock(_block);
    }

    void Recycle(int index)
    {
        Shard shard = _live[index];
        _live.RemoveAt(index);

        if (shard.flying && shard.pipe != null)
        {
            shard.pipe.inFlight--;
            shard.pipe.flow -= shard.direction;
        }
        shard.info.ClosePopup();
        shard.trail.emitting = false;
        shard.trail.Clear();
        shard.gameObject.SetActive(false);
        shard.from = shard.to = null;
        shard.pipe = null;
        _pool.Push(shard);
    }

    // ------------------------------------------------------------------
    // Pipes: one per pair of nodes, visible while messages are travelling on it
    // ------------------------------------------------------------------

    Pipe GetPipe(Transform a, Transform b, Color color)
    {
        if (!_pipes.TryGetValue((a, b), out Pipe pipe) && !_pipes.TryGetValue((b, a), out pipe))
        {
            pipe = new Pipe { key = (a, b), color = color };
            pipe.railMesh = NewDynamicMesh("Conduit");
            pipe.collarMesh = NewDynamicMesh("Conduit Collars");
            pipe.rail = CreateWorldSpaceRenderer("Conduit", pipe.railMesh, RailMaterial);
            pipe.collars = CreateWorldSpaceRenderer("ConduitCollars", pipe.collarMesh, ArenaMaterials.Anodized);

            // Collars carry a hint of the traffic's colour over bare titanium
            _block.Clear();
            ArenaMaterials.Tint(_block, Color.Lerp(new Color(0.62f, 0.64f, 0.67f), color, 0.5f));
            pipe.collars.SetPropertyBlock(_block);

            _pipes[pipe.key] = pipe;
            _pipeList.Add(pipe);
        }
        return pipe;
    }

    // Dark, polished steel: a quiet line that still catches the studio light along its length
    static Material RailMaterial => ArenaMaterials.Lit("conduit", new Color(0.2f, 0.21f, 0.23f), 1f, 0.78f);

    static Mesh NewDynamicMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        return mesh;
    }

    MeshRenderer CreateWorldSpaceRenderer(string name, Mesh mesh, Material material)
    {
        MeshRenderer renderer = ArenaMaterials.Create(transform, name, mesh, material, false);
        // The mesh is built in world space, so keep its object at the world origin
        renderer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        renderer.transform.localScale = Vector3.one;
        renderer.enabled = false;
        return renderer;
    }

    void UpdatePipes(float dt)
    {
        float ease = ArenaFX.Damp(2f, dt);
        float duration = Mathf.Max(0.2f, flightSeconds);

        for (int i = _pipeList.Count - 1; i >= 0; i--)
        {
            Pipe pipe = _pipeList[i];
            (Transform a, Transform b) = pipe.key;

            if (a == null || b == null)
            {
                _pipes.Remove(pipe.key);
                _pipeList.RemoveAt(i);
                Destroy(pipe.rail.gameObject);
                Destroy(pipe.collars.gameObject);
                Destroy(pipe.railMesh);
                Destroy(pipe.collarMesh);
                continue;
            }

            pipe.alpha = Mathf.Lerp(pipe.alpha, pipe.inFlight > 0 ? 1f : 0f, ease);
            bool visible = pipe.alpha > 0.02f;
            if (pipe.rail.enabled != visible) pipe.rail.enabled = pipe.collars.enabled = visible;
            if (!visible) continue;

            // Measure the same arc the messages fly along
            Vector3 start = a.position, end = b.position;
            Vector3 control = ControlPoint(start, end);
            _dense[0] = start;
            _length[0] = 0f;
            for (int s = 1; s <= ArcSamples; s++)
            {
                _dense[s] = ArenaFX.Bezier(start, control, end, s / (float)ArcSamples);
                _length[s] = _length[s - 1] + Vector3.Distance(_dense[s - 1], _dense[s]);
            }
            float total = _length[ArcSamples];

            // The rail runs between the two nodes' outer shells, never through their cores
            float from = NodeShell * a.lossyScale.x;
            float to = total - NodeShell * b.lossyScale.x;
            if (to - from < 0.2f) { pipe.rail.enabled = pipe.collars.enabled = false; continue; }

            float grow = ArenaFX.EaseOutCubic(pipe.alpha);

            // Rail
            for (int s = 0; s < PipeSegments; s++)
                _curve[s] = Sample(Mathf.Lerp(from, to, s / (float)(PipeSegments - 1)), out _);
            _tube.Clear();
            _tube.Add(_curve, PipeRadius * grow, PipeRadius * grow, 8, true);
            _tube.Build("Conduit", pipe.railMesh);

            // Collars slide with the traffic at the messages' own speed, and coast to a stop
            float target = Mathf.Sign(pipe.flow) * (pipe.flow == 0 ? 0f : total / duration);
            pipe.phaseSpeed = Mathf.Lerp(pipe.phaseSpeed, target, ArenaFX.Damp(1.5f, dt));
            pipe.phase = Mathf.Repeat(pipe.phase + pipe.phaseSpeed * dt, CollarSpacing);

            _tube.Clear();
            AddCollar(from + PortHalfLength, PortRadius * grow, PortHalfLength); // port at each end
            AddCollar(to - PortHalfLength, PortRadius * grow, PortHalfLength);
            float first = from + PortHalfLength * 2f + 0.15f, last = to - PortHalfLength * 2f - 0.15f;
            int collars = 0;
            for (float s = first + pipe.phase; s < last && collars < MaxCollars; s += CollarSpacing, collars++)
            {
                // Collars shrink as they near a port, so none ever pops in or out
                float edge = Mathf.Clamp01(Mathf.Min(s - first, last - s) / 0.4f);
                AddCollar(s, CollarRadius * grow * ArenaFX.EaseInOut(edge), CollarHalfLength);
            }
            _tube.Build("Conduit Collars", pipe.collarMesh);
        }
    }

    void AddCollar(float distance, float radius, float halfLength)
    {
        if (radius < 0.0015f) return;
        Vector3 centre = Sample(distance, out Vector3 tangent);
        _pair[0] = centre - tangent * halfLength;
        _pair[1] = centre + tangent * halfLength;
        _tube.Add(_pair, radius, radius, 10, true);
    }

    /// <summary>Position and direction at a given distance along the measured arc.</summary>
    Vector3 Sample(float distance, out Vector3 tangent)
    {
        int i = 1;
        while (i < ArcSamples && _length[i] < distance) i++;
        float span = Mathf.Max(1e-5f, _length[i] - _length[i - 1]);
        float k = Mathf.Clamp01((distance - _length[i - 1]) / span);
        tangent = (_dense[i] - _dense[i - 1]) / span;
        return Vector3.Lerp(_dense[i - 1], _dense[i], k);
    }

    /// <summary>
    /// Bows each route away from the centre of the arena, so traffic flows around the central
    /// chain instead of cutting straight through it.
    /// </summary>
    static Vector3 ControlPoint(Vector3 from, Vector3 to)
    {
        Vector3 middle = (from + to) * 0.5f;
        Vector3 outward = middle.sqrMagnitude > 0.01f ? middle.normalized : Vector3.up;
        return middle + outward * (Vector3.Distance(from, to) * 0.28f);
    }
}
