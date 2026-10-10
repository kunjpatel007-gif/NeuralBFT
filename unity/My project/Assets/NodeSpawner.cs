using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Keeps the 3D nodes in step with the backend: spawns new ones, retires the ones that left,
/// and spreads them evenly over a Fibonacci sphere. Nodes glide to new positions when the
/// network grows or shrinks instead of jumping.
/// </summary>
public class NodeSpawner : MonoBehaviour
{
    public GameObject nodePrefab;
    public float radius = 18f;

    private Mesh _coreMesh; // one crystal mesh shared by every node

    void Awake()
    {
        _coreMesh = ArenaGeometry.ChamferedIcosahedron(0.5f, 1.2f);
    }

    void OnDestroy()
    {
        if (_coreMesh != null) Destroy(_coreMesh);
    }

    private Vector3 FibonacciSpherePoint(int index, int total)
    {
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        float y = 1f - (total > 1 ? index / (float)(total - 1) : 0.5f) * 2f;
        float radiusAtY = Mathf.Sqrt(1f - y * y);
        float theta = goldenAngle * index;
        return new Vector3(Mathf.Cos(theta) * radiusAtY, y, Mathf.Sin(theta) * radiusAtY) * radius;
    }

    public Dictionary<string, GameObject> SyncNodes(List<NodeData> serverNodes, Dictionary<string, GameObject> currentMap)
    {
        ConsensusCore.ArenaRadius = radius;

        // 1. Retire nodes that no longer exist on the server
        var serverIds = new HashSet<string>();
        foreach (var n in serverNodes) serverIds.Add(n.id);

        var toRemove = new List<string>();
        foreach (var kvp in currentMap)
        {
            if (serverIds.Contains(kvp.Key)) continue;
            Retire(kvp.Value);
            toRemove.Add(kvp.Key);
        }
        foreach (var id in toRemove) currentMap.Remove(id);

        // 2. Spawn missing nodes and 3. spread everyone over the sphere
        int count = serverNodes.Count;
        for (int i = 0; i < count; i++)
        {
            string id = serverNodes[i].id;
            bool isNew = !currentMap.TryGetValue(id, out GameObject go) || go == null;
            if (isNew)
            {
                go = Instantiate(nodePrefab);
                go.name = id;
                PrepareNode(go);
                currentMap[id] = go;
            }

            Vector3 position = FibonacciSpherePoint(i, count);
            var visualizer = go.GetComponent<NodeVisualizer>();
            if (visualizer != null) visualizer.MoveTo(position, isNew);
            else go.transform.position = position;
        }

        return currentMap;
    }

    static void Retire(GameObject node)
    {
        if (node == null) return;
        var visualizer = node.GetComponent<NodeVisualizer>();
        if (visualizer != null) visualizer.Dismiss(); // shrinks away, then destroys itself
        else Destroy(node);
    }

    void PrepareNode(GameObject node)
    {
        // Every mesh the prefab ships with becomes the shared crystal
        foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
            mf.sharedMesh = _coreMesh;

        // One generous hit box, so the node is easy to click
        foreach (var col in node.GetComponentsInChildren<Collider>(true)) Destroy(col);
        node.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.8f, 1.8f);

        // The visualizer adds the Dyson rings, reputation gauge and containment ring
        var visualizer = node.GetComponent<NodeVisualizer>();
        if (visualizer == null) visualizer = node.AddComponent<NodeVisualizer>();
        visualizer.Build();
    }

    /// <summary>
    /// Generates the node body: an icosahedron with chamfered edges and corners, so every edge
    /// catches a thin highlight. heightScale stretches it into a taller, more gem-like form.
    /// </summary>
    public static Mesh CreateSleekCyberCore(float radius, float heightScale)
    {
        return ArenaGeometry.ChamferedIcosahedron(radius, heightScale);
    }
}
