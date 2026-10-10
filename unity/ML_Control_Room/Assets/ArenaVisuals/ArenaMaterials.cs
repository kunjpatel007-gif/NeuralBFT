using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Physically based materials for the arena, all on URP's standard Lit shader so they receive
/// real lighting, soft shadows, ambient occlusion and studio reflections. Only plain property
/// values and a base texture are used (no shader keywords), so nothing can be stripped from a
/// build. Per-object colour goes through MaterialPropertyBlocks.
/// </summary>
public static class ArenaMaterials
{
    public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
    static Texture2D _gridTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Cache.Clear();
        _gridTexture = null;
    }

    /// <summary>Coloured anodised aluminium: the colour is set per object.</summary>
    public static Material Anodized => Lit("anodized", Color.white, 0.82f, 0.6f);

    /// <summary>Satin titanium, for rings and fine structure.</summary>
    public static Material Titanium => Lit("titanium", new Color(0.62f, 0.64f, 0.67f), 1f, 0.6f);

    /// <summary>Darker brushed steel, for pins, spines and bases.</summary>
    public static Material Steel => Lit("steel", new Color(0.42f, 0.44f, 0.47f), 1f, 0.5f);

    /// <summary>Matte ceramic: the colour is set per object.</summary>
    public static Material Ceramic => Lit("ceramic", Color.white, 0.05f, 0.5f);

    /// <summary>The floor: dark, softly polished, with a fine engraved grid.</summary>
    public static Material Floor(float worldSize)
    {
        Material material = Lit("floor", new Color(0.115f, 0.12f, 0.13f), 0f, 0.62f, GridTexture);
        // The texture holds 5 x 5 cells of 2 units, so it repeats every 10 units
        material.SetTextureScale(BaseMapId, Vector2.one * (worldSize / 10f));
        return material;
    }

    public static Material Lit(string key, Color color, float metallic, float smoothness, Texture baseMap = null)
    {
        if (Cache.TryGetValue(key, out Material material) && material != null) return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        material = new Material(shader) { name = "Arena " + key };
        material.SetColor(BaseColorId, color);
        material.SetFloat(MetallicId, metallic);
        material.SetFloat(SmoothnessId, smoothness);
        if (baseMap != null) material.SetTexture(BaseMapId, baseMap);
        // No GPU instancing: objects are tinted with MaterialPropertyBlocks and drawn by the SRP
        // Batcher. Instancing would need extra shader variants that builds strip, which made
        // tinted objects lose their colours (or vanish) in builds while looking fine in the editor.
        material.enableInstancing = false;
        Cache[key] = material;
        return material;
    }

    public static void Tint(MaterialPropertyBlock block, Color color)
    {
        block.SetColor(BaseColorId, color);
    }

    /// <summary>Configures a renderer for lit, shadowed rendering.</summary>
    public static void Solid(Renderer renderer, bool castShadows = true)
    {
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off; // use the scene's ambient lighting
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
    }

    public static MeshRenderer Create(Transform parent, string name, Mesh mesh, Material material, bool castShadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        Solid(renderer, castShadows);
        return renderer;
    }

    /// <summary>
    /// 5 x 5 grid cells with anti-aliased engraved lines, heavier on the outer border. Full mip
    /// chain and anisotropic filtering keep it crisp at grazing angles without shimmering.
    /// </summary>
    static Texture2D GridTexture
    {
        get
        {
            if (_gridTexture != null) return _gridTexture;

            const int size = 1024;
            const int cells = 5;
            float cell = size / (float)cells;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float minor = Mathf.Min(Distance(x, cell), Distance(y, cell));
                    float major = Mathf.Min(Distance(x, size), Distance(y, size));

                    float line = Mathf.Max(
                        Mathf.Clamp01(1.2f - minor) * 0.35f,   // fine lines, ~1.5 px
                        Mathf.Clamp01(2.0f - major) * 0.75f);  // border lines, ~3 px
                    byte v = (byte)Mathf.RoundToInt(Mathf.Lerp(150f, 255f, line));
                    pixels[y * size + x] = new Color32(v, v, v, 255);
                }
            }

            _gridTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Arena Floor Grid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 16,
            };
            _gridTexture.SetPixels32(pixels);
            _gridTexture.Apply(true, true);
            return _gridTexture;
        }
    }

    // Distance in pixels from pixel centre p to the nearest multiple of period
    static float Distance(int p, float period)
    {
        float position = p + 0.5f;
        float offset = position - Mathf.Round(position / period) * period;
        return Mathf.Abs(offset);
    }
}
