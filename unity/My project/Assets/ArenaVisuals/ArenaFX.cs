using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared rendering resources for the arena: the Arena/* shaders, a handful of shared
/// materials, procedural meshes and small helpers. Per-object variation goes through
/// MaterialPropertyBlocks, so no script here ever clones a material.
/// </summary>
public static class ArenaFX
{
    public static readonly int ColorId     = Shader.PropertyToID("_Color");
    public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    public static readonly int EnergyId    = Shader.PropertyToID("_Energy");
    public static readonly int PulseId     = Shader.PropertyToID("_Pulse");
    public static readonly int FlowId      = Shader.PropertyToID("_Flow");
    public static readonly int SeedId      = Shader.PropertyToID("_Seed");
    public static readonly int AlphaId     = Shader.PropertyToID("_Alpha");
    public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    public static readonly int RevealId    = Shader.PropertyToID("_Reveal");
    public static readonly int FlashId     = Shader.PropertyToID("_Flash");
    public static readonly int DissolveId  = Shader.PropertyToID("_Dissolve");
    public static readonly int CoreId      = Shader.PropertyToID("_Core");
    public static readonly int FalloffId   = Shader.PropertyToID("_Falloff");

    static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    static Mesh _quad, _floorQuad, _sphere, _crystal;
    static bool _warnedAboutShaders;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Keeps things correct when the editor enters play mode without a domain reload
        Materials.Clear();
        _quad = _floorQuad = _sphere = _crystal = null;
        _warnedAboutShaders = false;
    }

    // ------------------------------------------------------------------
    // Colour
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts a theme colour to what the shaders expect, scaled by an HDR intensity.
    /// Colours are always sent as vectors, so Unity never applies a second, implicit conversion.
    /// </summary>
    public static Vector4 Shade(Color color, float intensity = 1f)
    {
        Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        return new Vector4(c.r * intensity, c.g * intensity, c.b * intensity, color.a);
    }

    public static void SetColor(MaterialPropertyBlock block, Color color, float intensity = 1f)
    {
        Vector4 shaded = Shade(color, intensity);
        block.SetVector(ColorId, shaded);
        block.SetVector(BaseColorId, shaded); // only read by the fallback shader
    }

    // ------------------------------------------------------------------
    // Materials
    // ------------------------------------------------------------------

    public static Material CrystalMaterial  => Get("crystal", "Crystal", false);
    public static Material ShellMaterial    => Get("shell", "Shell", true);
    public static Material SlabMaterial     => Get("slab", "HoloSlab", false);
    public static Material GridMaterial     => Get("grid", "Grid", true);
    public static Material BackdropMaterial => Get("backdrop", "Backdrop", false);

    /// <summary>Soft additive light. Billboarded versions face the camera on their own.</summary>
    public static Material Glow(bool billboard, bool ring)
    {
        string key = "glow" + (billboard ? "-billboard" : "-flat") + (ring ? "-ring" : "-disc");
        if (!Materials.TryGetValue(key, out Material material) || material == null)
        {
            material = Get(key, "Glow", true);
            material.SetFloat("_Billboard", billboard ? 1f : 0f);
            material.SetFloat("_Ring", ring ? 1f : 0f);
            material.SetFloat("_RingRadius", 0.82f);
            material.SetFloat("_RingWidth", 0.045f);
        }
        return material;
    }

    /// <summary>Additive ribbon for lines and trails, optionally with pulses running along it.</summary>
    public static Material Beam(string name, float flow, float tiling, float speed, float edge = 1.6f)
    {
        string key = "beam-" + name;
        if (!Materials.TryGetValue(key, out Material material) || material == null)
        {
            material = Get(key, "Beam", true);
            material.SetFloat("_Flow", flow);
            material.SetFloat("_Tiling", tiling);
            material.SetFloat("_Speed", speed);
            material.SetFloat("_Edge", edge);
        }
        return material;
    }

    static Material Get(string key, string shaderName, bool transparent)
    {
        if (Materials.TryGetValue(key, out Material material) && material != null) return material;

        Shader shader = Resources.Load<Shader>("ArenaShaders/" + shaderName);
        if (shader == null || !shader.isSupported)
        {
            shader = FallbackShader(transparent);
            if (!_warnedAboutShaders)
            {
                _warnedAboutShaders = true;
                Debug.LogWarning($"[ArenaFX] Shader 'Arena/{shaderName}' is missing or failed to compile, " +
                                 "so a plain fallback is being used. Check Assets/Resources/ArenaShaders.");
            }
        }

        material = new Material(shader) { name = "Arena " + key };
        Materials[key] = material;
        return material;
    }

    static Shader FallbackShader(bool transparent)
    {
        Shader shader = transparent
            ? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            : Shader.Find("Universal Render Pipeline/Unlit");
        return shader != null ? shader : Shader.Find("Sprites/Default");
    }

    // ------------------------------------------------------------------
    // Scene helpers
    // ------------------------------------------------------------------

    public static void Unlit(Renderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    public static MeshRenderer CreateMeshObject(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        Unlit(renderer);
        return renderer;
    }

    /// <summary>A soft glow sprite. Billboarded ones ignore the object's rotation.</summary>
    public static MeshRenderer CreateGlow(Transform parent, string name, float size, bool billboard = true, bool ring = false)
    {
        MeshRenderer renderer = CreateMeshObject(parent, name, Quad, Glow(billboard, ring));
        renderer.transform.localScale = Vector3.one * size;
        return renderer;
    }

    /// <summary>A camera-facing line, ready to be given positions.</summary>
    public static LineRenderer CreateLine(Transform parent, string name, float width, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.widthMultiplier = width;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.startColor = line.endColor = Color.white;
        Unlit(line);
        return line;
    }

    /// <summary>A closed circle in the object's XZ plane.</summary>
    public static LineRenderer CreateRing(Transform parent, string name, float radius, float width, Material material, int segments = 64)
    {
        LineRenderer line = CreateLine(parent, name, width, material);
        line.loop = true;
        line.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
        }
        return line;
    }

    // ------------------------------------------------------------------
    // Easing
    // ------------------------------------------------------------------

    /// <summary>Frame-rate independent smoothing factor for Lerp(current, target, Damp(...)).</summary>
    public static float Damp(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);

    public static float EaseOutCubic(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    public static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Overshoots slightly before settling at 1.</summary>
    public static float EaseOutBack(float t)
    {
        const float overshoot = 1.70158f;
        t = Mathf.Clamp01(t) - 1f;
        return 1f + (overshoot + 1f) * t * t * t + overshoot * t * t;
    }

    public static Vector3 Bezier(Vector3 from, Vector3 control, Vector3 to, float t)
    {
        float u = 1f - t;
        return u * u * from + 2f * u * t * control + t * t * to;
    }

    // ------------------------------------------------------------------
    // Meshes
    // ------------------------------------------------------------------

    /// <summary>Unit quad in the XY plane, centred on the origin.</summary>
    public static Mesh Quad
    {
        get
        {
            if (_quad == null)
            {
                _quad = BuildQuad("Arena Quad", Vector3.right, Vector3.up);
                // Billboarding happens in the shader, so culling has to assume any orientation
                _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            }
            return _quad;
        }
    }

    /// <summary>Unit quad lying flat in the XZ plane.</summary>
    public static Mesh FloorQuad
    {
        get
        {
            if (_floorQuad == null) _floorQuad = BuildQuad("Arena Floor Quad", Vector3.right, Vector3.forward);
            return _floorQuad;
        }
    }

    /// <summary>Smooth sphere, radius 0.5.</summary>
    public static Mesh Sphere
    {
        get
        {
            if (_sphere == null) _sphere = BuildIcoSphere(0.5f, 3);
            return _sphere;
        }
    }

    /// <summary>Flat-shaded icosahedron, radius 0.5.</summary>
    public static Mesh Crystal
    {
        get
        {
            if (_crystal == null) _crystal = BuildCrystal(0.5f, 1f);
            return _crystal;
        }
    }

    static Mesh BuildQuad(string name, Vector3 right, Vector3 up)
    {
        Vector3 r = right * 0.5f, u = up * 0.5f;
        Vector3 normal = Vector3.Cross(up, right).normalized;
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { -r - u, r - u, r + u, -r + u };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.normals = new[] { normal, normal, normal, normal };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    static readonly float Phi = (1f + Mathf.Sqrt(5f)) / 2f;

    static Vector3[] IcosahedronVertices() => new[]
    {
        new Vector3(-1f,  Phi, 0f).normalized, new Vector3( 1f,  Phi, 0f).normalized,
        new Vector3(-1f, -Phi, 0f).normalized, new Vector3( 1f, -Phi, 0f).normalized,
        new Vector3(0f, -1f,  Phi).normalized, new Vector3(0f,  1f,  Phi).normalized,
        new Vector3(0f, -1f, -Phi).normalized, new Vector3(0f,  1f, -Phi).normalized,
        new Vector3( Phi, 0f, -1f).normalized, new Vector3( Phi, 0f,  1f).normalized,
        new Vector3(-Phi, 0f, -1f).normalized, new Vector3(-Phi, 0f,  1f).normalized
    };

    static readonly int[] IcosahedronFaces =
    {
        0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
        1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
        3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
        4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
    };

    /// <summary>
    /// A flat-shaded icosahedron: 20 faces with their own vertices, so every facet catches
    /// the light as a single clean plane. heightScale stretches it along Y.
    /// </summary>
    public static Mesh BuildCrystal(float radius, float heightScale)
    {
        Vector3[] corners = IcosahedronVertices();
        for (int i = 0; i < corners.Length; i++)
            corners[i] = new Vector3(corners[i].x * radius, corners[i].y * radius * heightScale, corners[i].z * radius);

        var vertices = new Vector3[60];
        var triangles = new int[60];
        for (int face = 0; face < 20; face++)
        {
            Vector3 a = corners[IcosahedronFaces[face * 3]];
            Vector3 b = corners[IcosahedronFaces[face * 3 + 1]];
            Vector3 c = corners[IcosahedronFaces[face * 3 + 2]];

            // Make sure every face winds outward
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), a) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
            }

            int index = face * 3;
            vertices[index] = a;
            vertices[index + 1] = b;
            vertices[index + 2] = c;
            triangles[index] = index;
            triangles[index + 1] = index + 1;
            triangles[index + 2] = index + 2;
        }

        var mesh = new Mesh { name = "Arena Crystal" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildIcoSphere(float radius, int subdivisions)
    {
        var vertices = new List<Vector3>(IcosahedronVertices());
        var triangles = new List<int>(IcosahedronFaces);
        var midpoints = new Dictionary<long, int>();

        for (int pass = 0; pass < subdivisions; pass++)
        {
            var next = new List<int>(triangles.Count * 4);
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Midpoint(a, b, vertices, midpoints);
                int bc = Midpoint(b, c, vertices, midpoints);
                int ca = Midpoint(c, a, vertices, midpoints);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles = next;
        }

        // Guarantee outward winding regardless of the source face order
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), a) < 0f)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
        }

        var normals = new Vector3[vertices.Count];
        var positions = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            normals[i] = vertices[i];
            positions[i] = vertices[i] * radius;
        }

        var mesh = new Mesh { name = "Arena Sphere" };
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int index)) return index;

        vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
        index = vertices.Count - 1;
        cache[key] = index;
        return index;
    }
}
