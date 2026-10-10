using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// The ledger: a growing stack of hexagonal blocks on a steel base. Each block is graphite
/// ceramic with a bevelled edge and a band in the colour of its consensus protocol, joined to the
/// block below by a short steel pin, and labelled with its proposer, round and hash. New blocks
/// ease down onto the stack. A rejected block arrives above the chain, is scanned, refused and
/// shattered into drifting shards (see RejectBlock).
/// </summary>
public class LedgerZone : MonoBehaviour
{
    public Vector3 basePosition = new Vector3(22f, -15f, 0f);
    public float blockHeight = 0.6f;
    public int maxVisibleBlocks = 40;

    private const float HexRadius = 1.3f;
    private const float HexHeight = 0.4f;
    private const float HexApothem = HexRadius * 0.8660254f; // centre to the middle of a side
    private const float SettleSeconds = 1.4f;
    private const float DropHeight = 1.2f;

    private static readonly Color Graphite = new Color(0.16f, 0.165f, 0.175f);

    private class Slab
    {
        public GameObject gameObject;
        public Transform transform;
        public MeshRenderer renderer;
        public float settle; // 0..1 while easing down onto the stack
        public bool landed;
    }

    private struct PendingBlock
    {
        public BlockData block;
        public string key;
    }

    private readonly List<Slab> _stack = new List<Slab>();
    private readonly Queue<PendingBlock> _pending = new Queue<PendingBlock>();
    private readonly HashSet<string> _processed = new HashSet<string>();

    private Mesh _hexMesh, _pinMesh, _baseMesh; // shared by all blocks
    private MaterialPropertyBlock _block;
    private float _nextSpawnTime;
    private bool _rejecting; // while a rejection plays, the chain waits so nothing can overlap it

    void Start()
    {
        // Force the position here in case the old value was saved in the Unity Inspector
        transform.position = new Vector3(22f, -15f, 0f);
        _block = new MaterialPropertyBlock();
        BuildBase();
    }

    void OnDestroy()
    {
        if (_hexMesh != null) Destroy(_hexMesh);
        if (_pinMesh != null) Destroy(_pinMesh);
        if (_baseMesh != null) Destroy(_baseMesh);
        if (_ringMesh != null) Destroy(_ringMesh);
        if (_shardMesh != null) Destroy(_shardMesh);
    }

    public void TrySpawnBlock(BlockData block)
    {
        if (block == null) return;
        string key = block.hash ?? $"{block.proposer}_{block.round}";
        if (!_processed.Add(key)) return;

        // Queued, so a burst of blocks arrives as a steady stream rather than a single clump
        _pending.Enqueue(new PendingBlock { block = block, key = key });
    }

    void Update()
    {
        if (_block == null) return; // Start has not run yet

        if (_pending.Count > 0 && !_rejecting && Time.time >= _nextSpawnTime)
        {
            bool rejected = _pending.Peek().block.is_rejected;
            // A rejected block waits until the stack below it has fully settled
            if (!rejected || StackSettled())
            {
                PendingBlock next = _pending.Dequeue();
                _nextSpawnTime = Time.time + (_pending.Count > 8 ? 0.12f : 0.45f);

                if (rejected) StartCoroutine(RejectBlock(next.block, next.key));
                else SpawnValidBlock(next.block, next.key);
            }
        }

        float dt = Time.deltaTime;
        for (int i = 0; i < _stack.Count; i++) Animate(_stack[i], i, dt);
    }

    // ------------------------------------------------------------------
    // Valid blocks
    // ------------------------------------------------------------------
    void SpawnValidBlock(BlockData block, string key)
    {
        Slab slab = CreateSlab($"Block_{key}", Graphite, ArenaTheme.ConsensusColor(block.consensus));
        AddPin(slab.transform);
        AddLabel(slab.transform, block.proposer, block.round, key);
        _stack.Add(slab);

        // Remove the oldest block once over the limit; the rest slide down to fill the gap
        if (_stack.Count > maxVisibleBlocks)
        {
            Destroy(_stack[0].gameObject);
            _stack.RemoveAt(0);
        }

        Animate(slab, _stack.Count - 1, 0f);
    }

    bool StackSettled()
    {
        if (_stack.Count == 0) return true;
        Slab top = _stack[_stack.Count - 1];
        return top.landed && Mathf.Abs(top.transform.localPosition.y - (_stack.Count - 1) * blockHeight) < 0.02f;
    }

    void Animate(Slab slab, int index, float dt)
    {
        Vector3 rest = new Vector3(0f, index * blockHeight, 0f);

        if (slab.landed)
        {
            // Follow smoothly whenever the stack shifts down
            slab.transform.localPosition = Vector3.Lerp(slab.transform.localPosition, rest, ArenaFX.Damp(3f, dt));
            return;
        }

        // Ease down from just above the stack, growing slightly as it settles
        slab.settle = Mathf.Min(1f, slab.settle + dt / SettleSeconds);
        float t = ArenaFX.EaseInOut(slab.settle);
        slab.transform.localPosition = rest + Vector3.up * (DropHeight * (1f - t));
        slab.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t);

        if (slab.settle >= 1f) slab.landed = true;
    }

    Slab CreateSlab(string objectName, Color body, Color band)
    {
        MeshRenderer renderer = ArenaMaterials.Create(transform, objectName, GetHexMesh(), ArenaMaterials.Ceramic);
        renderer.sharedMaterials = new[] { ArenaMaterials.Ceramic, ArenaMaterials.Anodized };

        _block.Clear();
        ArenaMaterials.Tint(_block, body);
        renderer.SetPropertyBlock(_block, 0);
        _block.Clear();
        ArenaMaterials.Tint(_block, band);
        renderer.SetPropertyBlock(_block, 1);

        return new Slab
        {
            gameObject = renderer.gameObject,
            transform = renderer.transform,
            renderer = renderer,
        };
    }

    // A short steel pin joining this block to the one below (or to the base)
    void AddPin(Transform slab)
    {
        float gap = blockHeight - HexHeight;
        Transform pin = ArenaMaterials.Create(slab, "ChainLink", GetPinMesh(), ArenaMaterials.Steel).transform;
        pin.localPosition = new Vector3(0f, -HexHeight * 0.5f - gap * 0.5f, 0f);
        pin.localScale = new Vector3(1f, gap + 0.04f, 1f);
    }

    // ------------------------------------------------------------------
    // Rejected blocks (~5 s, no flashes):
    //   1. arrive  - eases down above the chain, looking like any other block
    //   2. inspect - a fine titanium ring scans it top to bottom; its band turns from the
    //                consensus colour to muted red as the scan passes
    //   3. refuse  - a short, damped shake, and the verdict fades in on its face
    //   4. shatter - the ring lifts away and the block breaks into small shards that drift
    //                apart, turning, and shrink to nothing
    // ------------------------------------------------------------------
    private const float ArriveSeconds = 1.2f;
    private const float ScanSeconds = 1.1f;
    private const float RefuseSeconds = 0.7f;
    private const float ShatterSeconds = 1.5f;
    private const int ShardCount = 16;

    private static readonly Color RejectedBody = new Color(0.2f, 0.15f, 0.145f);

    private Mesh _ringMesh, _shardMesh;

    IEnumerator RejectBlock(BlockData block, string key)
    {
        _rejecting = true;
        try
        {
            foreach (object step in RejectSequence(block, key)) yield return step;
        }
        finally
        {
            _rejecting = false; // always resume the chain, even if the sequence is interrupted
            _nextSpawnTime = Time.time + 0.3f;
        }
    }

    IEnumerable RejectSequence(BlockData block, string key)
    {
        // With a backlog of blocks waiting, play the sequence a little faster
        float pace = _pending.Count > 6 ? 1.8f : 1f;
        float arriveSeconds = ArriveSeconds / pace, scanSeconds = ScanSeconds / pace;
        float refuseSeconds = RefuseSeconds / pace, shatterSeconds = ShatterSeconds / pace;

        Color consensus = ArenaTheme.ConsensusColor(block.consensus);
        Slab slab = CreateSlab($"RejectedBlock_{key}", Graphite, consensus);
        // Hovers one full slot plus a clear gap above the settled top of the chain
        Vector3 hover = new Vector3(0f, _stack.Count * blockHeight + 0.5f, 0f);

        // 1. Arrive
        for (float time = 0f; time < arriveSeconds; time += Time.deltaTime)
        {
            float t = ArenaFX.EaseInOut(time / arriveSeconds);
            slab.transform.localPosition = hover + Vector3.up * (DropHeight * (1f - t));
            slab.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t);
            yield return null;
        }
        slab.transform.localPosition = hover;
        slab.transform.localScale = Vector3.one;

        // 2. Inspect: the ring grows in above the block and passes down over it
        Transform ring = ArenaMaterials.Create(transform, "InspectionRing", GetRingMesh(), ArenaMaterials.Titanium, false).transform;
        float top = hover.y + HexHeight * 0.5f + 0.25f;
        float bottom = hover.y - HexHeight * 0.5f - 0.05f; // still well clear of the chain's top block
        for (float time = 0f; time < scanSeconds; time += Time.deltaTime)
        {
            float t = time / scanSeconds;
            float eased = ArenaFX.EaseInOut(t);
            ring.localPosition = new Vector3(0f, Mathf.Lerp(top, bottom, eased), 0f);
            ring.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, ArenaFX.EaseOutCubic(Mathf.Clamp01(t * 3f)));
            ring.localRotation = Quaternion.Euler(0f, eased * 90f, 0f);

            // The band and body change as the ring passes the middle of the block
            float verdict = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, t));
            Paint(slab, Color.Lerp(Graphite, RejectedBody, verdict), Color.Lerp(consensus, ArenaTheme.Quarantined, verdict));
            yield return null;
        }
        Paint(slab, RejectedBody, ArenaTheme.Quarantined);

        // 3. Refuse: a short side-to-side shake that dies away, and the verdict fades in
        TextMeshPro stamp = CreateText(transform, "RejectedStamp", "REJECTED", 2.2f, ArenaTheme.Quarantined, new Vector2(4f, 0.6f));
        stamp.characterSpacing = 18f;
        stamp.transform.localPosition = hover + new Vector3(0f, 0.75f, -HexApothem - 0.05f);
        stamp.alpha = 0f;

        for (float time = 0f; time < refuseSeconds; time += Time.deltaTime)
        {
            float t = time / refuseSeconds;
            float decay = (1f - t) * (1f - t);
            slab.transform.localPosition = hover + Vector3.right * (0.07f * Mathf.Sin(t * Mathf.PI * 2f * 4f) * decay);
            ring.localPosition = new Vector3(0f, Mathf.Lerp(bottom, hover.y, ArenaFX.EaseInOut(t)), 0f);
            stamp.alpha = Mathf.Clamp01(t / 0.6f);
            yield return null;
        }
        slab.transform.localPosition = hover;
        stamp.alpha = 1f;
        yield return new WaitForSeconds(0.5f / pace);

        // 4. Shatter: the block is replaced by shards that drift outward and shrink away
        var shards = new List<Transform>(ShardCount);
        var velocity = new List<Vector3>(ShardCount);
        var spin = new List<Vector3>(ShardCount);
        var size = new List<float>(ShardCount);
        for (int i = 0; i < ShardCount; i++)
        {
            bool isBand = i % 4 == 0;
            MeshRenderer piece = ArenaMaterials.Create(transform, "Shard", GetShardMesh(), isBand ? ArenaMaterials.Anodized : ArenaMaterials.Ceramic);
            _block.Clear();
            ArenaMaterials.Tint(_block, isBand ? ArenaTheme.Quarantined : RejectedBody);
            piece.SetPropertyBlock(_block);

            // Start somewhere inside the block's volume
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Random.value) * HexRadius * 0.8f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, Random.Range(-0.5f, 0.5f) * HexHeight, Mathf.Sin(angle) * radius);
            piece.transform.localPosition = hover + offset;
            piece.transform.localRotation = Random.rotationUniform;

            float s = Random.Range(0.22f, 0.42f);
            piece.transform.localScale = Vector3.one * s;

            Vector3 outward = new Vector3(offset.x, 0f, offset.z).normalized;
            if (outward.sqrMagnitude < 0.01f) outward = Random.onUnitSphere;
            shards.Add(piece.transform);
            velocity.Add(outward * Random.Range(0.6f, 1.5f) + Vector3.up * Random.Range(0.05f, 0.45f)); // never down into the chain
            spin.Add(Random.onUnitSphere * Random.Range(60f, 200f));
            size.Add(s);
        }
        slab.gameObject.SetActive(false);

        for (float time = 0f; time < shatterSeconds; time += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            float t = time / shatterSeconds;
            float drag = Mathf.Exp(-1.6f * dt);   // they glide to a stop rather than flying off
            float shrink = 1f - t * t;            // hold their size, then shrink away
            for (int i = 0; i < shards.Count; i++)
            {
                velocity[i] *= drag;
                shards[i].localPosition += velocity[i] * dt;
                shards[i].Rotate(spin[i] * dt, Space.Self);
                shards[i].localScale = Vector3.one * (size[i] * shrink);
            }

            // The ring rises and narrows away; the verdict fades with the debris
            ring.localPosition += Vector3.up * (0.6f * dt);
            ring.localScale = Vector3.one * Mathf.Lerp(1f, 0f, ArenaFX.EaseInOut(Mathf.Clamp01(t * 1.4f)));
            stamp.alpha = 1f - ArenaFX.EaseInOut(t);
            yield return null;
        }

        foreach (Transform piece in shards) Destroy(piece.gameObject);
        Destroy(ring.gameObject);
        Destroy(stamp.gameObject);
        Destroy(slab.gameObject);
    }

    void Paint(Slab slab, Color body, Color band)
    {
        _block.Clear();
        ArenaMaterials.Tint(_block, body);
        slab.renderer.SetPropertyBlock(_block, 0);
        _block.Clear();
        ArenaMaterials.Tint(_block, band);
        slab.renderer.SetPropertyBlock(_block, 1);
    }

    Mesh GetRingMesh()
    {
        if (_ringMesh == null) _ringMesh = ArenaGeometry.Torus(HexRadius * 1.15f, 0.018f, 128, 10);
        return _ringMesh;
    }

    Mesh GetShardMesh()
    {
        if (_shardMesh == null) _shardMesh = ArenaGeometry.ChamferedIcosahedron(0.5f, 0.8f, 0.16f);
        return _shardMesh;
    }

    // ------------------------------------------------------------------
    // Dressing
    // ------------------------------------------------------------------
    void BuildBase()
    {
        // A low steel plinth the stack stands on; its top sits one pin-length below the first block
        float gap = blockHeight - HexHeight;
        float plinthHeight = 0.12f;
        Transform plinth = ArenaMaterials.Create(transform, "Plinth", GetBaseMesh(plinthHeight), ArenaMaterials.Steel).transform;
        plinth.localPosition = new Vector3(0f, -HexHeight * 0.5f - gap - plinthHeight * 0.5f, 0f);

        TextMeshPro title = CreateText(transform, "LedgerTitle", "LEDGER", 2.6f, ArenaTheme.TextMuted, new Vector2(8f, 1f));
        title.characterSpacing = 30f;
        title.transform.localPosition = new Vector3(0f, -HexHeight * 0.5f - gap - plinthHeight + 0.01f, -2.6f);
        title.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies on the floor, readable from the arena
    }

    void AddLabel(Transform slab, string proposer, int round, string key)
    {
        string shortKey = key.Length > 10 ? key.Substring(0, 10) : key;
        string muted = ArenaTheme.Hex(ArenaTheme.TextMuted);
        float face = -HexApothem - 0.004f;

        // Proposer and round above the band, hash below it, both on the front face
        TextMeshPro upper = CreateText(slab, "Label", $"{proposer}  <color=#{muted}>#{round}</color>", 1.05f, ArenaTheme.TextPrimary, new Vector2(HexRadius * 0.9f, 0.16f));
        upper.transform.localPosition = new Vector3(0f, 0.095f, face);

        TextMeshPro lower = CreateText(slab, "Hash", shortKey, 0.8f, ArenaTheme.TextMuted, new Vector2(HexRadius * 0.9f, 0.13f));
        lower.characterSpacing = 6f;
        lower.transform.localPosition = new Vector3(0f, -0.1f, face);
    }

    static TextMeshPro CreateText(Transform parent, string objectName, string text, float size, Color color, Vector2 area)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = area;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.richText = true;
        return tmp;
    }

    Mesh GetHexMesh()
    {
        if (_hexMesh == null) _hexMesh = ArenaGeometry.BevelledHex(HexRadius, HexHeight, 0.04f, 0.035f, 0.01f);
        return _hexMesh;
    }

    Mesh GetPinMesh()
    {
        if (_pinMesh == null) _pinMesh = ArenaGeometry.Cylinder(0.07f, 1f);
        return _pinMesh;
    }

    Mesh GetBaseMesh(float height)
    {
        if (_baseMesh == null) _baseMesh = ArenaGeometry.BevelledHex(HexRadius + 0.35f, height, 0.03f);
        return _baseMesh;
    }
}
