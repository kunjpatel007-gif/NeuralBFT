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
        Color edgeColor; // NEW: Explicitly define the wireframe edge color per state
        float emissionMultiplier;

        if (stat == "TRUSTED")
        {
            // Green trusted node -> Hot pink outline
            ColorUtility.TryParseHtmlString("#5fb98c", out baseColor); 
            ColorUtility.TryParseHtmlString("#FF1493", out edgeColor); // Hot Pink
            emissionMultiplier = 1.58f; 
        }
        else if (stat == "VERIFIED")
        {
            // Cyan verified node -> Bright green outline
            ColorUtility.TryParseHtmlString("#6bb8d4", out baseColor); 
            ColorUtility.TryParseHtmlString("#39FF14", out edgeColor); // Bright Neon Green
            emissionMultiplier = 0.79f;
        }
        else if (stat == "WATCHED" || stat == "HIGH RISK")
        {
            // Yellow high risk node -> Light Blue outline
            ColorUtility.TryParseHtmlString("#FFC300", out baseColor); 
            ColorUtility.TryParseHtmlString("#00BFFF", out edgeColor); // Vibrant Light Blue
            emissionMultiplier = 1.316f; 
        }
        else if (stat == "QUARANTINED")
        {
            // Red quarantined node -> Black outline
            ColorUtility.TryParseHtmlString("#e06464", out baseColor); 
            ColorUtility.TryParseHtmlString("#000000", out edgeColor); // Solid Black
            float pulse = 1.053f + Mathf.Sin(pulseTimer * 4f) * 0.79f; 
            emissionMultiplier = pulse;
        }
        else if (stat == "BLACKLISTED")
        {
            // Black blacklisted node -> Glowy red outline
            ColorUtility.TryParseHtmlString("#131315", out baseColor); 
            // Lowered from 5.0 to 1.8 to prevent the red from blowing out into pure white
            edgeColor = new Color(1.8f, 0.02f, 0.02f, 1.0f); 
            emissionMultiplier = 0.0f; 
        }
        else
        {
            // Fallback (same as verified)
            ColorUtility.TryParseHtmlString("#6bb8d4", out baseColor); 
            ColorUtility.TryParseHtmlString("#39FF14", out edgeColor); // Bright Neon Green
            emissionMultiplier = 0.79f; 
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
                    newMat.SetFloat("_Smoothness", 0.85f);
                    newMat.SetFloat("_Metallic", 0.3f);
                    newMat.EnableKeyword("_EMISSION");
                    newMat.SetColor("_EmissionColor", baseColor * emissionMultiplier);
                    mr.material = newMat;
                }
            }
        }

        // --- NEW: Apply the highly-tuned, explicit edge colors to the wireframes ---
        LineRenderer[] lrs = GetComponentsInChildren<LineRenderer>(true);
        
        // Increase the neon bloom on the edges by exactly 35% (1.35x multiplier)
        Color neonEdgeColor = new Color(edgeColor.r * 1.35f, edgeColor.g * 1.35f, edgeColor.b * 1.35f, edgeColor.a);
        
        foreach (var lr in lrs)
        {
            if (lr != null && lr.material != null)
            {
                lr.material.color = neonEdgeColor;
            }
        }
    }
}
