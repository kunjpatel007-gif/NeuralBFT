using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LeaderboardRow : MonoBehaviour
{
    public TextMeshProUGUI idText;
    public TextMeshProUGUI statusText;
    public Image reputationBar;

    void Start()
    {
        var hlg = gameObject.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) Destroy(hlg);

        // Make row fully transparent to avoid ugly edges and just sit cleanly on the main panel
        var rootImg = gameObject.GetComponent<Image>();
        if (rootImg != null) Destroy(rootImg);

        if (idText != null)
        {
            RectTransform rt = idText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-220, 0); // Kept wide left
            rt.sizeDelta = new Vector2(250, 40); // Increased width so it doesn't wrap
            idText.alignment = TextAlignmentOptions.Left;
            ColorUtility.TryParseHtmlString("#e8e8ea", out Color idColor);
            idText.color = idColor;
            idText.fontStyle = FontStyles.Bold;
        }

        if (statusText != null)
        {
            RectTransform rt = statusText.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(30, 0); // Pushed a bit left
            rt.sizeDelta = new Vector2(350, 40); // Extra width for formatting
            statusText.alignment = TextAlignmentOptions.Left;
            statusText.fontStyle = FontStyles.Normal;
        }

        if (reputationBar != null)
        {
            RectTransform rt = reputationBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(250, 0);
            // Increased thickness from 8 to 14 so the bars look substantial, not like tiny scratches
            rt.sizeDelta = new Vector2(180, 14);
            reputationBar.material = null;
        }
    }

    public void UpdateData(NodeData data, int index = 0)
    {
        if (idText != null) idText.text = data.id.ToUpper();
        
        string stat = (data.status ?? "").ToUpper();
        // Add a bit of space before the percentage for cleanliness
        if (statusText != null) statusText.text = $"{stat}   ({data.reputation:F1}%)";
        
        Color statColor = Color.white;
        if (stat == "TRUSTED") ColorUtility.TryParseHtmlString("#00FF55", out statColor); // Emerald Green
        else if (stat == "VERIFIED") ColorUtility.TryParseHtmlString("#00FFFF", out statColor); // Hyper-Cyan
        else if (stat == "WATCHED" || stat == "HIGH RISK") ColorUtility.TryParseHtmlString("#FFD700", out statColor); // Neon Gold
        else if (stat == "QUARANTINED") ColorUtility.TryParseHtmlString("#FF4500", out statColor); // Pulsing Orange
        else if (stat == "BLACKLISTED") ColorUtility.TryParseHtmlString("#FF0000", out statColor); // Aggressive Red
        else ColorUtility.TryParseHtmlString("#00FFFF", out statColor); // Default Cyan

        if (statusText != null) statusText.color = statColor;
        if (reputationBar != null)
        {
            reputationBar.color = statColor;
            // Force a minimum fill of 2% so the visual bar never completely disappears!
            reputationBar.fillAmount = Mathf.Max(0.02f, data.reputation / 100f);
        }
    }
}