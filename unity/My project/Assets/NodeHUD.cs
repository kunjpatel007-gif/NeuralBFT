using UnityEngine;
using TMPro;

[System.Serializable]
public class NodeData
{
    public string id;
    public bool is_byzantine;
    public float reputation;
    public string status;
    public float ml_prob;
    public string fault_type;
    public string hardware;
    public float alpha;       
    public float beta;        
    public int partition_id;
    public System.Collections.Generic.Dictionary<string, float> ml_features;
}

public class NodeHUD : MonoBehaviour
{
    public TextMeshProUGUI hudText;
    
    // Cyberpunk color palette
    private static readonly Color PANEL_BG     = new Color(0.06f, 0.08f, 0.12f, 0.92f);
    private static readonly Color TEXT_LABEL    = new Color(0.4f, 0.92f, 1.0f);   // Neon Cyan
    private static readonly Color TEXT_VALUE    = new Color(0.92f, 0.95f, 1.0f);   // Bright White
    private static readonly Color STATUS_GREEN  = new Color(0.1f, 1.0f, 0.4f);
    private static readonly Color STATUS_GOLD   = new Color(1.0f, 0.8f, 0.1f);
    private static readonly Color STATUS_RED    = new Color(1.0f, 0.15f, 0.2f);
    
    void Start()
    {
        // Auto-layout the Canvas to hover perfectly above the node
        Canvas myCanvas = GetComponentInChildren<Canvas>(true);
        if (myCanvas != null)
        {
            RectTransform rt = myCanvas.GetComponent<RectTransform>();
            // Lowered from 3.2f to 2.4f to match the 75% smaller monolith meshes
            rt.localPosition = new Vector3(0, 2.4f, 0); 
            rt.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            rt.sizeDelta = new Vector2(280, 180); 

            // Reset M_Hologram material back to default UI shader (fixes _MainTex error)
            // then paint the panel dark charcoal
            var imgs = myCanvas.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach(var img in imgs) {
                img.material = null;
                img.color = PANEL_BG;
            }
        }

        if (hudText != null)
        {
            RectTransform txtRt = hudText.GetComponent<RectTransform>();
            txtRt.localPosition = Vector3.zero;
            txtRt.sizeDelta = new Vector2(270, 170);
            hudText.fontSize = 20;
            hudText.alignment = TextAlignmentOptions.Center;
            hudText.richText = true; // Enable rich text for colored labels
        }

        // Dummy data for offline preview
        NodeData testData = new NodeData();
        testData.reputation = Random.Range(30f, 99f);
        testData.status = "VERIFIED";
        UpdateHUD(testData); 
    }

    public void UpdateHUD(NodeData data)
    {
        if (hudText == null) return;

        // Pick status color
        string stat = (data.status ?? "").ToUpper();
        string statusHex;
        if (stat == "TRUSTED" || stat == "VERIFIED")
            statusHex = ColorUtility.ToHtmlStringRGB(STATUS_GREEN);
        else if (stat == "WATCHED" || stat == "HIGH RISK")
            statusHex = ColorUtility.ToHtmlStringRGB(STATUS_GOLD);
        else if (stat == "QUARANTINED" || stat == "BLACKLISTED")
            statusHex = ColorUtility.ToHtmlStringRGB(STATUS_RED);
        else
            statusHex = ColorUtility.ToHtmlStringRGB(TEXT_VALUE);

        string labelHex = ColorUtility.ToHtmlStringRGB(TEXT_LABEL);
        string valHex   = ColorUtility.ToHtmlStringRGB(TEXT_VALUE);

        hudText.text = 
            $"<color=#{labelHex}>REP:</color> <color=#{valHex}>{data.reputation:F1}%</color>\n" +
            $"<color=#{labelHex}>ML THREAT:</color> <color=#{valHex}>{data.ml_prob:F2}</color>\n" +
            $"<color=#{labelHex}>\u03B1:</color> <color=#{valHex}>{data.alpha:F1}</color> <color=#{labelHex}>\u03B2:</color> <color=#{valHex}>{data.beta:F1}</color>\n" +
            $"<color=#{statusHex}>\u25CF {stat}</color>";
    }
}