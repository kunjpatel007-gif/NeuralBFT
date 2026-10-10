using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The chain at the heart of the network: a slender column of the most recent blocks, each a
/// small bevelled hex plate with a band in the colour of the consensus protocol that produced it,
/// threaded on a steel spine. New blocks ease onto the top, the column recentres itself, and the
/// oldest block shrinks away at the bottom. The whole column turns very slowly.
/// </summary>
public class ConsensusCore : MonoBehaviour
{
    public static ConsensusCore Instance { get; private set; }

    /// <summary>Radius of the node sphere; set by the NodeSpawner.</summary>
    public static float ArenaRadius = 8f;

    const int VisibleBlocks = 14;
    const float Pitch = 0.24f;
    const float SettleSeconds = 1.2f;
    const float RetireSeconds = 0.6f;

    class Plate
    {
        public Transform transform;
        public float settle;  // 0..1 easing onto the column
        public float retire;  // 0..1 shrinking away, once pushed out
    }

    static Mesh _plateMesh, _spineMesh;

    readonly List<Plate> _plates = new List<Plate>();
    readonly List<Plate> _retiring = new List<Plate>();
    MaterialPropertyBlock _block;
    Transform _column, _spine;
    Color _consensusColor = ArenaTheme.Verified;
    int _pending;
    float _nextAdd;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        ArenaRadius = 8f;
        _plateMesh = _spineMesh = null;
    }

    /// <summary>Returns the column, creating it at the world origin the first time.</summary>
    public static ConsensusCore Ensure()
    {
        if (Instance == null) Instance = new GameObject("ChainColumn").AddComponent<ConsensusCore>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        _block = new MaterialPropertyBlock();

        if (_plateMesh == null) _plateMesh = ArenaGeometry.BevelledHex(0.42f, 0.12f, 0.022f, 0.03f, 0.008f);
        if (_spineMesh == null) _spineMesh = ArenaGeometry.Cylinder(0.035f, 1f);

        _column = new GameObject("Column").transform;
        _column.SetParent(transform, false);

        _spine = ArenaMaterials.Create(_column, "Spine", _spineMesh, ArenaMaterials.Steel).transform;
        _spine.localScale = new Vector3(1f, 0.001f, 1f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetConsensus(string consensus)
    {
        _consensusColor = ArenaTheme.ConsensusColor(consensus);
    }

    /// <summary>A block was committed: queue a new plate for the top of the column.</summary>
    public void AddBlock()
    {
        _pending = Mathf.Min(_pending + 1, VisibleBlocks);
    }

    /// <summary>Kept for callers from earlier versions; same as AddBlock.</summary>
    public void Pulse() => AddBlock();

    void Update()
    {
        float dt = Time.deltaTime;

        // Blocks that arrive together are laid down one after another
        if (_pending > 0 && Time.time >= _nextAdd)
        {
            _pending--;
            _nextAdd = Time.time + 0.3f;
            SpawnPlate();
        }

        _column.Rotate(0f, 4f * dt, 0f, Space.Self);

        // Keep the column centred on the origin as it grows and sheds plates
        float height = Mathf.Max(0, _plates.Count - 1) * Pitch;
        float ease = ArenaFX.Damp(3f, dt);
        for (int i = 0; i < _plates.Count; i++)
        {
            Plate plate = _plates[i];
            float rest = i * Pitch - height * 0.5f;

            if (plate.settle < 1f)
            {
                plate.settle = Mathf.Min(1f, plate.settle + dt / SettleSeconds);
                float t = ArenaFX.EaseInOut(plate.settle);
                plate.transform.localPosition = new Vector3(0f, rest + 0.8f * (1f - t), 0f);
                plate.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, t);
            }
            else
            {
                Vector3 p = plate.transform.localPosition;
                plate.transform.localPosition = new Vector3(0f, Mathf.Lerp(p.y, rest, ease), 0f);
            }
        }

        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            Plate plate = _retiring[i];
            plate.retire += dt / RetireSeconds;
            plate.transform.localScale = Vector3.one * (1f - ArenaFX.EaseInOut(plate.retire));
            if (plate.retire >= 1f)
            {
                Destroy(plate.transform.gameObject);
                _retiring.RemoveAt(i);
            }
        }

        // The spine runs a little past both ends of the column
        float spineLength = height + 0.5f;
        _spine.localScale = new Vector3(1f, Mathf.Lerp(_spine.localScale.y, spineLength, ease), 1f);
    }

    void SpawnPlate()
    {
        MeshRenderer renderer = ArenaMaterials.Create(_column, "Block", _plateMesh, ArenaMaterials.Ceramic);
        renderer.sharedMaterials = new[] { ArenaMaterials.Ceramic, ArenaMaterials.Anodized };

        // Graphite ceramic body; the band carries the protocol's colour
        _block.Clear();
        ArenaMaterials.Tint(_block, new Color(0.15f, 0.155f, 0.165f));
        renderer.SetPropertyBlock(_block, 0);
        _block.Clear();
        ArenaMaterials.Tint(_block, _consensusColor);
        renderer.SetPropertyBlock(_block, 1);

        var plate = new Plate { transform = renderer.transform };
        plate.transform.localScale = Vector3.one * 0.6f;
        _plates.Add(plate);

        if (_plates.Count > VisibleBlocks)
        {
            Plate oldest = _plates[0];
            _plates.RemoveAt(0);
            _retiring.Add(oldest);
        }
    }
}
