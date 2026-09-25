using UnityEngine;

/// <summary>
/// Dynamically colors node spheres based on their trust status.
/// Works with Standard, URP Lit, AND custom Shader Graphs.
/// If the material doesn't support any known color property, 
/// it creates a fresh URP Lit material as a fallback.
/// </summary>
public class NodeVisualizer : MonoBehaviour
{
    private MeshRenderer[] meshRenderers;
    private float pulseTimer = 0f;

    void Awake()
    {
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    void Start()
    {
        // Apply default color immediately so nodes aren't blank while offline
        NodeData dummyData = new NodeData();
        dummyData.status = "VERIFIED";
        UpdateVisuals(dummyData);
    }

    void Update()
    {
        // Subtle breathing pulse for quarantined/blacklisted nodes
        pulseTimer += Time.deltaTime;
    }

    public void UpdateVisuals(NodeData data)
    {
        if (meshRenderers == null || meshRenderers.Length == 0) return;

        string stat = (data.status ?? "").ToUpper();

        Color baseColor;
        float emissionMultiplier;

        if (stat == "TRUSTED")
        {
            ColorUtility.TryParseHtmlString("#00FF55", out baseColor); // Green Core
            emissionMultiplier = 3.5f; // Huge glow boost
        }
        else if (stat == "VERIFIED")
        {
            ColorUtility.TryParseHtmlString("#00FFFF", out baseColor); // Cyan Core
            emissionMultiplier = 3.5f;
        }
        else if (stat == "WATCHED" || stat == "HIGH RISK")
        {
            ColorUtility.TryParseHtmlString("#FFC300", out baseColor); // Yellow Core
            emissionMultiplier = 3.5f; 
        }
        else if (stat == "QUARANTINED")
        {
            ColorUtility.TryParseHtmlString("#FF0000", out baseColor); // Red Core
            float pulse = 1.053f + Mathf.Sin(pulseTimer * 4f) * 0.79f; 
            emissionMultiplier = pulse * 3.5f; // Intense pulsing glow
        }
        else if (stat == "BLACKLISTED")
        {
            ColorUtility.TryParseHtmlString("#000000", out baseColor); // Black Core
            emissionMultiplier = 0.0f; // Dead core
        }
        else
        {
            ColorUtility.TryParseHtmlString("#00FFFF", out baseColor); 
            emissionMultiplier = 3.5f; 
        }

        foreach (var mr in meshRenderers)
        {
            if (mr == null || mr.material == null) continue;
            Material mat = mr.material;

            bool colorSet = false;

            // Try every known color property name across all shader types
            string[] colorProps = { "_BaseColor", "_Color", "_MainColor", "_TintColor" };
            foreach (string prop in colorProps)
            {
                if (mat.HasProperty(prop))
                {
                    mat.SetColor(prop, baseColor);
                    colorSet = true;
                    break;
                }
            }

            // Try emission properties
            string[] emissionProps = { "_EmissionColor", "_EmissiveColor" };
            foreach (string prop in emissionProps)
            {
                if (mat.HasProperty(prop))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(prop, baseColor * emissionMultiplier);
                }
            }

            // FALLBACK: If no color property was found, the Shader Graph doesn't support 
            // standard coloring. Create a fresh URP Lit material that DOES support it.
            if (!colorSet)
            {
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
                if (urpLit != null)
                {
                    Material newMat = new Material(urpLit);
                    newMat.SetColor("_BaseColor", baseColor);
                    newMat.SetFloat("_Smoothness", 0.6f);
                    newMat.SetFloat("_Metallic", 0.1f);
                    newMat.EnableKeyword("_EMISSION");
                    newMat.SetColor("_EmissionColor", baseColor * emissionMultiplier);
                    mr.material = newMat;
                }
            }
        }

        // --- NEW: Convert the Dyson Rings from Neon to Pure Metallic Tracks ---
        LineRenderer[] lrs = GetComponentsInChildren<LineRenderer>(true);
        
        foreach (var lr in lrs)
        {
            if (lr != null)
            {
                // Assign a true PBR Lit material to the rings so they react to light instead of glowing
                if (lr.material == null || lr.material.shader.name != "Universal Render Pipeline/Lit")
                {
                    Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (litShader == null) litShader = Shader.Find("Standard");
                    lr.material = new Material(litShader);
                }
                
                lr.material.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.4f)); // Matte Steel
                lr.material.SetFloat("_Metallic", 0.6f);
                lr.material.SetFloat("_Smoothness", 0.2f); // Rough metal: kills the sharp sliding light glints
                lr.material.DisableKeyword("_EMISSION");
                lr.material.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
