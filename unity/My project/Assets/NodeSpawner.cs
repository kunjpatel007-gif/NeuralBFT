using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NodeSpawner : MonoBehaviour
{
    public GameObject nodePrefab;
    // Increased from 8f to 18f to give the new elongated shapes much more space
    public float radius = 18f;

    /// <summary>
    /// Distributes N points evenly across the surface of a sphere using the
    /// Fibonacci Sphere algorithm. This creates a beautiful, even 3D distribution
    /// instead of a flat boring circle.
    /// </summary>
    private Vector3 FibonacciSpherePoint(int index, int total)
    {
        // Golden angle in radians
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        // Y goes from +1 to -1 evenly
        float y = 1f - (index / (float)(total - 1)) * 2f;
        float radiusAtY = Mathf.Sqrt(1f - y * y);

        float theta = goldenAngle * index;
        float x = Mathf.Cos(theta) * radiusAtY;
        float z = Mathf.Sin(theta) * radiusAtY;

        return new Vector3(x, y, z) * radius;
    }

    // Dynamically spawn, destroy, and arrange nodes in a 3D sphere
    public Dictionary<string, GameObject> SyncNodes(List<NodeData> serverNodes, Dictionary<string, GameObject> currentMap)
    {
        // 1. Destroy nodes that no longer exist on the server
        var serverIds = new HashSet<string>(serverNodes.Select(n => n.id));
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

        // 2. Spawn missing nodes and replace ProBuilder mesh with smooth sphere
        foreach (var node in serverNodes)
        {
            if (!currentMap.ContainsKey(node.id))
            {
                GameObject newGo = Instantiate(nodePrefab);
                newGo.name = node.id;

                // Replace the faceted ProBuilder mesh with a perfectly smooth Unity sphere
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
            {
                go.transform.position = FibonacciSpherePoint(i, count);
            }
        }

        return currentMap;
    }

    /// <summary>
    /// Replaces the rough ProBuilder mesh on a node with an ultra-sleek, 
    /// multi-layered kinetic structure: solid glowing core + rotating Dyson rings.
    /// </summary>
    void SmoothifyMesh(GameObject node)
    {
        MeshFilter[] meshFilters = node.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0) return;

        // The Inner Solid Core (Perfect geometric sphere, scaled down tightly)
        Mesh innerCoreMesh = CreateSleekCyberCore(0.35f, 1.0f);
        foreach (var mf in meshFilters)
        {
            mf.sharedMesh = innerCoreMesh;
        }

        // Clean up old colliders
        var oldColliders = node.GetComponentsInChildren<Collider>(true);
        foreach (var col in oldColliders) Destroy(col);
        
        // Add a perfectly sized BoxCollider based on the full outer size
        BoxCollider boxCol = node.AddComponent<BoxCollider>();
        boxCol.size = new Vector3(1.8f, 1.8f, 1.8f);

        // Create the Outer Dyson Sphere Rings
        int numRings = 5; // 5 intersecting rings creates a dense, complex structure
        for (int i = 0; i < numRings; i++)
        {
            GameObject ringObj = new GameObject("DysonRing_" + i);
            ringObj.transform.SetParent(node.transform, false);
            
            // Maximum Chaos: Distribute the rings using a completely random 3D orientation
            // This destroys any mathematical grid pattern and makes it look truly organic
            ringObj.transform.localRotation = Random.rotationUniform;

            // Give them a completely random starting phase so they aren't coordinated at t=0
            ringObj.transform.Rotate(Vector3.right, Random.Range(0f, 360f), Space.Self);

            LineRenderer lr = ringObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true; // Connect the circle perfectly
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            
            Material staticMat = new Material(Shader.Find("Sprites/Default"));
            staticMat.color = new Color(1f, 1f, 1f, 0.2f);
            lr.material = staticMat;

            // Draw a perfect horizontal circle in the XZ plane (y=0)
            int segments = 48; 
            lr.positionCount = segments;
            float ringRadius = 0.9f; 
            for (int j = 0; j < segments; j++)
            {
                float rad = Mathf.Deg2Rad * (j * 360f / segments);
                float x = Mathf.Sin(rad) * ringRadius;
                float z = Mathf.Cos(rad) * ringRadius;
                lr.SetPosition(j, new Vector3(x, 0, z)); // Y is 0
            }

            // Add rotation script to make the ring tumble dynamically
            var rotator = ringObj.AddComponent<ShellRotator>();
            rotator.rotationAxis = new Vector3(1, 0, 0);
            
            // Randomize speed and drop it to 80% of previous speeds (which were ~35 to 83)
            // 80% of that is roughly 28 to 66
            rotator.rotationSpeed = Random.Range(25f, 65f);
            
            // Randomly reverse the tumble direction for maximum chaos!
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