using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NodeSpawner : MonoBehaviour
{
    public GameObject nodePrefab;
    public float radius = 18f;

    // Shared resources — created once, used by all nodes
    private Mesh     _sharedCoreMesh;
    private Material _sharedRingMat;

    void Awake()
    {
        // Build the shared mesh once
        _sharedCoreMesh = CreateSleekCyberCore(0.35f, 1.0f);

        // Build the shared ring material once
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Sprites/Default");
        
        _sharedRingMat = new Material(s);
        _sharedRingMat.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.4f)); // Matte Steel
        _sharedRingMat.SetFloat("_Metallic", 0.6f);
        _sharedRingMat.SetFloat("_Smoothness", 0.2f);
        _sharedRingMat.DisableKeyword("_EMISSION");
        _sharedRingMat.SetColor("_EmissionColor", Color.black);
    }

    void OnDestroy()
    {
        if (_sharedCoreMesh != null) Destroy(_sharedCoreMesh);
        if (_sharedRingMat  != null) Destroy(_sharedRingMat);
    }

    private Vector3 FibonacciSpherePoint(int index, int total)
    {
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
        float y = 1f - (index / (float)(total - 1)) * 2f;
        float radiusAtY = Mathf.Sqrt(1f - y * y);
        float theta = goldenAngle * index;
        float x = Mathf.Cos(theta) * radiusAtY;
        float z = Mathf.Sin(theta) * radiusAtY;
        return new Vector3(x, y, z) * radius;
    }

    public Dictionary<string, GameObject> SyncNodes(List<NodeData> serverNodes, Dictionary<string, GameObject> currentMap)
    {
        // 1. Destroy nodes that no longer exist on the server
        // Using a local reusable HashSet to avoid LINQ allocation
        var serverIds = new HashSet<string>();
        foreach (var n in serverNodes) serverIds.Add(n.id);

        var toRemove = new List<string>();
        foreach (var kvp in currentMap)
        {
            if (!serverIds.Contains(kvp.Key))
            {
                if (kvp.Value != null) Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var id in toRemove) currentMap.Remove(id);

        // 2. Spawn missing nodes
        foreach (var node in serverNodes)
        {
            if (!currentMap.ContainsKey(node.id))
            {
                GameObject newGo = Instantiate(nodePrefab);
                newGo.name = node.id;
                SmoothifyMesh(newGo);
                currentMap[node.id] = newGo;
            }
        }

        // 3. Distribute nodes across a 3D Fibonacci Sphere
        int count = serverNodes.Count;
        for (int i = 0; i < count; i++)
        {
            string id = serverNodes[i].id;
            if (currentMap.TryGetValue(id, out GameObject go) && go != null)
                go.transform.position = FibonacciSpherePoint(i, count);
        }

        return currentMap;
    }

    void SmoothifyMesh(GameObject node)
    {
        MeshFilter[] meshFilters = node.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0) return;

        // Assign the shared mesh — no per-node allocation
        foreach (var mf in meshFilters)
            mf.sharedMesh = _sharedCoreMesh;

        // Clean up old colliders
        var oldColliders = node.GetComponentsInChildren<Collider>(true);
        foreach (var col in oldColliders) Destroy(col);

        BoxCollider boxCol = node.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.8f, 1.8f, 1.8f);

        // Create Dyson rings — assign sharedMaterial, no per-ring leak
        int numRings = 5;
        for (int i = 0; i < numRings; i++)
        {
            GameObject ringObj = new GameObject("DysonRing_" + i);
            ringObj.transform.SetParent(node.transform, false);
            ringObj.transform.localRotation = Random.rotationUniform;
            ringObj.transform.Rotate(Vector3.right, Random.Range(0f, 360f), Space.Self);

            LineRenderer lr = ringObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.startWidth = 0.04f;
            lr.endWidth   = 0.04f;
            lr.sharedMaterial = _sharedRingMat; // shared — no clone, no leak

            int segments = 48;
            lr.positionCount = segments;
            float ringRadius = 0.9f;
            for (int j = 0; j < segments; j++)
            {
                float rad = Mathf.Deg2Rad * (j * 360f / segments);
                lr.SetPosition(j, new Vector3(Mathf.Sin(rad) * ringRadius, 0, Mathf.Cos(rad) * ringRadius));
            }

            var rotator = ringObj.AddComponent<ShellRotator>();
            rotator.rotationAxis  = new Vector3(1, 0, 0);
            rotator.rotationSpeed = Random.Range(25f, 65f);
            if (Random.value > 0.5f) rotator.rotationSpeed *= -1f;
        }
    }

    /// <summary>
    /// Generates a perfectly flat-shaded Icosahedron. 
    /// By elongating it, it looks like a highly advanced, sleek architectural monolith 
    /// rather than a spiky low-poly shape. Most people don't know what an Icosahedron is!
    /// </summary>
    public static Mesh CreateSleekCyberCore(float radius, float heightScale)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        Vector3[] baseVerts = new Vector3[] {
            new Vector3(-1,  t,  0).normalized,
            new Vector3( 1,  t,  0).normalized,
            new Vector3(-1, -t,  0).normalized,
            new Vector3( 1, -t,  0).normalized,
            new Vector3( 0, -1,  t).normalized,
            new Vector3( 0,  1,  t).normalized,
            new Vector3( 0, -1, -t).normalized,
            new Vector3( 0,  1, -t).normalized,
            new Vector3( t,  0, -1).normalized,
            new Vector3( t,  0,  1).normalized,
            new Vector3(-t,  0, -1).normalized,
            new Vector3(-t,  0,  1).normalized
        };

        // Scale them to make an elongated sleek shape
        for (int i = 0; i < baseVerts.Length; i++)
        {
            baseVerts[i].x *= radius;
            baseVerts[i].z *= radius;
            baseVerts[i].y *= (radius * heightScale);
        }

        int[] baseFaces = new int[] {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        // 20 faces * 3 = 60 vertices for perfect flat-shading
        Vector3[] flatVerts = new Vector3[60];
        int[] flatTris = new int[60];
        int vIndex = 0;

        for (int i = 0; i < 20; i++)
        {
            Vector3 v1 = baseVerts[baseFaces[i * 3]];
            Vector3 v2 = baseVerts[baseFaces[i * 3 + 1]];
            Vector3 v3 = baseVerts[baseFaces[i * 3 + 2]];

            // Force outward normals to ensure sleek lighting
            Vector3 cross = Vector3.Cross(v2 - v1, v3 - v1);
            if (Vector3.Dot(cross, v1) < 0)
            {
                Vector3 temp = v2;
                v2 = v3;
                v3 = temp;
            }

            flatVerts[vIndex] = v1; flatTris[vIndex] = vIndex; vIndex++;
            flatVerts[vIndex] = v2; flatTris[vIndex] = vIndex; vIndex++;
            flatVerts[vIndex] = v3; flatTris[vIndex] = vIndex; vIndex++;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = flatVerts;
        mesh.triangles = flatTris;
        mesh.RecalculateNormals(); // Crisp, clean facets
        return mesh;
    }
}