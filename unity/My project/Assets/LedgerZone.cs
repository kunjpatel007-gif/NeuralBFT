using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LedgerZone : MonoBehaviour
{
    public Vector3 basePosition = new Vector3(22f, -15f, 0f);
    public float blockHeight = 0.6f;
    public int maxVisibleBlocks = 40;

    private const float HexRadius = 1.3f;
    private const float HexHeight = 0.4f;

    private List<GameObject> spawnedBlocks = new List<GameObject>();
    private HashSet<string> processedHashes = new HashSet<string>();

    private Mesh hexMesh;            // shared by all blocks
    private Material validMaterial;  // shared by all valid blocks
    private Material linkMaterial;   // shared by all chain links

    void Start()
    {
        // Force the position here in case the old value was saved in the Unity Inspector
        transform.position = new Vector3(22f, -15f, 0f);
    }

    void Update()
    {
        // Automatically smoothly move all blocks to their correct stack positions
        for (int i = 0; i < spawnedBlocks.Count; i++)
        {
            if (spawnedBlocks[i] != null)
            {
                float targetY = i * blockHeight;
                Vector3 currentPos = spawnedBlocks[i].transform.localPosition;

                // Slower ease-out lerp so they don't drop instantly
                float newY = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * 3f);
                spawnedBlocks[i].transform.localPosition = new Vector3(0f, newY, 0f);
            }
        }
    }

    public void TrySpawnBlock(BlockData block)
    {
        string key = block.hash ?? $"{block.proposer}_{block.round}";
        if (processedHashes.Contains(key)) return;
        processedHashes.Add(key);

        if (block.is_rejected)
            StartCoroutine(SpawnRejectedBlock(block, key));
        else
            SpawnValidBlock(block, key);
    }

    // ------------------------------------------------------------------
    // Valid block: holographic hexagonal data slab
    // ------------------------------------------------------------------
    void SpawnValidBlock(BlockData block, string hash)
    {
        float targetY = spawnedBlocks.Count * blockHeight;
        float startY = targetY + 5f; // Start 5 units above target to drop in

        GameObject slab = CreateHexObject($"Block_{hash}", GetValidMaterial());
        slab.transform.localPosition = new Vector3(0f, startY, 0f);

        // Proposer Label (Glowing White)
        GameObject labelProposer = new GameObject("ProposerLabel");
        labelProposer.transform.SetParent(slab.transform, false);
        labelProposer.transform.localPosition = new Vector3(0f, HexHeight * 0.5f + 0.01f, 0f);
        labelProposer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh tm1 = labelProposer.AddComponent<TextMesh>();
        tm1.text = block.proposer;
        tm1.fontSize = 64;
        tm1.characterSize = 0.022f;
        tm1.anchor = TextAnchor.LowerCenter; // Pushes it UP from the center line
        tm1.alignment = TextAlignment.Center;
        // HDR Color (values > 1) to make it catch the post-processing Bloom and glow
        tm1.color = new Color(1.5f, 1.5f, 1.5f, 1.0f); 

        // Hash Label (Dim, transparent)
        GameObject labelHash = new GameObject("HashLabel");
        labelHash.transform.SetParent(slab.transform, false);
        labelHash.transform.localPosition = new Vector3(0f, HexHeight * 0.5f + 0.01f, 0f);
        labelHash.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh tm2 = labelHash.AddComponent<TextMesh>();
        tm2.text = hash;
        tm2.fontSize = 64;
        tm2.characterSize = 0.015f; // Slightly smaller than the proposer
        tm2.anchor = TextAnchor.UpperCenter; // Pushes it DOWN from the center line
        tm2.alignment = TextAlignment.Center;
        tm2.color = new Color(0.4f, 0.7f, 0.9f, 0.5f); // Dimmer, won't glow, avoids overlap

        // Data pipe beam to the block below
        if (spawnedBlocks.Count > 0)
        {
            GameObject link = new GameObject("ChainLink");
            link.transform.SetParent(slab.transform, false);
            LineRenderer lr = link.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            // Bottom center of this block -> top center of the block below
            lr.SetPosition(0, new Vector3(0f, -HexHeight * 0.5f, 0f));
            lr.SetPosition(1, new Vector3(0f, -blockHeight + HexHeight * 0.5f, 0f));
            lr.startWidth = 0.15f;
            lr.endWidth = 0.15f;
            lr.sharedMaterial = GetLinkMaterial();
        }

        spawnedBlocks.Add(slab);

        // Remove oldest if over limit
        if (spawnedBlocks.Count > maxVisibleBlocks)
        {
            GameObject oldest = spawnedBlocks[0];
            spawnedBlocks.RemoveAt(0);
            Destroy(oldest);
        }
    }

    // ------------------------------------------------------------------
    // Rejected block: digital dissolve (~2.2 s)
    // ------------------------------------------------------------------
    IEnumerator SpawnRejectedBlock(BlockData block, string hash)
    {
        float targetY = spawnedBlocks.Count * blockHeight + 2f; // Float above the chain
        float startY = targetY + 5f;

        Material mat = CreateRejectedMaterial();
        GameObject cube = CreateHexObject($"RejectedBlock_{hash}", mat);
        cube.transform.localPosition = new Vector3(0f, startY, 0f);

        // Phase 1: Arrival (0.4 s)
        float elapsed = 0f;
        float duration = 0.4f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - (1f - t) * (1f - t);
            cube.transform.localPosition = new Vector3(0f, Mathf.Lerp(startY, targetY, easeOut), 0f);
            yield return null;
        }
        Vector3 basePos = new Vector3(0f, targetY, 0f);
        cube.transform.localPosition = basePos;

        // Phase 2: Corruption jitter (0.8 s)
        elapsed = 0f;
        duration = 0.8f;
        int framesUntilToggle = Random.Range(2, 4);
        bool bright = true;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float amp = Mathf.Lerp(0.05f, 0.3f, t);

            cube.transform.localPosition = basePos + new Vector3(
                Random.Range(-amp, amp),
                Random.Range(-amp, amp),
                Random.Range(-amp, amp));
            cube.transform.localRotation = Quaternion.Euler(
                Random.Range(-5f, 5f),
                Random.Range(-5f, 5f),
                Random.Range(-5f, 5f));

            framesUntilToggle--;
            if (framesUntilToggle <= 0)
            {
                bright = !bright;
                framesUntilToggle = Random.Range(2, 4);
                mat.SetColor("_EmissionColor", bright
                    ? new Color(1f, 0f, 0f) * 2.0f
                    : new Color(0.2f, 0f, 0f) * 0.1f);
            }
            yield return null;
        }
        cube.transform.localPosition = basePos;
        cube.transform.localRotation = Quaternion.identity;
        mat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0f) * 0.5f);

        // Phase 3: "REJECTED" stamp (0.5 s)
        // Parented to the zone (not the block) so block jitter/scale does not affect it.
        GameObject stamp = new GameObject($"RejectedStamp_{hash}");
        stamp.transform.SetParent(transform, false);
        stamp.transform.localPosition = basePos + new Vector3(0f, 0.3f, -1.6f);
        stamp.transform.localScale = Vector3.zero;

        TextMesh st = stamp.AddComponent<TextMesh>();
        st.text = "X REJECTED";
        st.fontSize = 80;
        st.characterSize = 0.025f;
        st.anchor = TextAnchor.MiddleCenter;
        st.alignment = TextAlignment.Center;
        st.color = new Color(1f, 0.2f, 0.1f, 1f);

        elapsed = 0f;
        duration = 0.5f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = 1f - Mathf.Pow(1f - Mathf.Min(1f, t * 2f), 3f); // fast ease-out, full size by half-time
            stamp.transform.localScale = Vector3.one * s;
            yield return null;
        }
        stamp.transform.localScale = Vector3.one;

        // Phase 4: Implosion (0.5 s)
        elapsed = 0f;
        duration = 0.5f;
        Vector3 originalScale = cube.transform.localScale; // Cache — don't assume it's Vector3.one
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            cube.transform.localScale = originalScale * Mathf.Lerp(1f, 0f, t * t);
            mat.SetColor("_EmissionColor", new Color(1f, 0.25f, 0.1f) * Mathf.Lerp(2f, 10f, t));
            yield return null;
        }

        Destroy(stamp);
        Destroy(cube);
        Destroy(mat);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    GameObject CreateHexObject(string objName, Material mat)
    {
        GameObject go = new GameObject(objName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = GetHexMesh();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return go;
    }

    Shader FindLit()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Sprites/Default"); // avoid magenta if URP shader is stripped
        return s;
    }

    Material GetValidMaterial()
    {
        if (validMaterial != null) return validMaterial;

        Material mat = new Material(FindLit());
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", new Color(0.05f, 0.12f, 0.2f, 0.4f)); // High transparency
            mat.SetFloat("_Smoothness", 0.95f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0f, 0.8f, 1f) * 0.15f);

            // Transparent surface (URP)
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = 3000;
        }
        validMaterial = mat;
        return validMaterial;
    }

    Material CreateRejectedMaterial()
    {
        Material mat = new Material(FindLit());
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", new Color(0.4f, 0.05f, 0.05f, 1f));
            mat.SetFloat("_Smoothness", 0.6f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0f) * 0.5f);
        }
        return mat;
    }

    Material GetLinkMaterial()
    {
        if (linkMaterial != null) return linkMaterial;
        linkMaterial = new Material(Shader.Find("Sprites/Default"));
        linkMaterial.color = new Color(0f, 0.6f, 1f, 0.4f);
        return linkMaterial;
    }

    Mesh GetHexMesh()
    {
        if (hexMesh != null) return hexMesh;

        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();

        float hh = HexHeight * 0.5f;
        Vector3[] top = new Vector3[6];
        Vector3[] bot = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            float x = Mathf.Cos(a) * HexRadius;
            float z = Mathf.Sin(a) * HexRadius;
            top[i] = new Vector3(x, hh, z);
            bot[i] = new Vector3(x, -hh, z);
        }

        Vector3 topC = new Vector3(0f, hh, 0f);
        Vector3 botC = new Vector3(0f, -hh, 0f);

        for (int i = 0; i < 6; i++)
        {
            int n = (i + 1) % 6;

            // Top and bottom caps
            AddTri(verts, norms, uvs, tris, topC, top[i], top[n], Vector3.up);
            AddTri(verts, norms, uvs, tris, botC, bot[i], bot[n], Vector3.down);

            // Side quad (two triangles, flat normal)
            float mid = (i + 0.5f) * Mathf.PI / 3f;
            Vector3 outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            AddTri(verts, norms, uvs, tris, top[i], top[n], bot[n], outward);
            AddTri(verts, norms, uvs, tris, top[i], bot[n], bot[i], outward);
        }

        hexMesh = new Mesh();
        hexMesh.name = "HexSlab";
        hexMesh.SetVertices(verts);
        hexMesh.SetNormals(norms);
        hexMesh.SetUVs(0, uvs);
        hexMesh.SetTriangles(tris, 0);
        hexMesh.RecalculateBounds();
        return hexMesh;
    }

    // Adds one flat-shaded triangle, flipping winding if needed so it faces 'outward'
    static void AddTri(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                       Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
        {
            Vector3 tmp = b; b = c; c = tmp;
        }
        int start = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        norms.Add(outward); norms.Add(outward); norms.Add(outward);
        uvs.Add(new Vector2(a.x, a.z)); uvs.Add(new Vector2(b.x, b.z)); uvs.Add(new Vector2(c.x, c.z));
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
    }

    void OnDestroy()
    {
        if (hexMesh != null) Destroy(hexMesh);
        if (validMaterial != null) Destroy(validMaterial);
        if (linkMaterial != null) Destroy(linkMaterial);
    }
}
