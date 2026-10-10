using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// One line of the leaderboard: rank and node id, status, and a reputation bar that
/// glides to its new value and colour instead of jumping.
/// </summary>
public class LeaderboardRow : MonoBehaviour
{
    public TextMeshProUGUI idText;
    public TextMeshProUGUI statusText;
    public Image reputationBar;

    private float _targetFill = -1f;
    private Color _targetColor = ArenaTheme.Verified;

    void Start()
    {
        var hlg = gameObject.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) Destroy(hlg);

        // Rows are fully transparent and sit cleanly on the main panel
        var rootImg = gameObject.GetComponent<Image>();
        if (rootImg != null) Destroy(rootImg);

        if (idText != null)
        {
            Place(idText.rectTransform, -220, new Vector2(250, 40));
            idText.alignment = TextAlignmentOptions.Left;
            idText.color = ArenaTheme.TextPrimary;
            idText.fontStyle = FontStyles.Normal;
            idText.richText = true;
        }

        if (statusText != null)
        {
            Place(statusText.rectTransform, 30, new Vector2(350, 40));
            statusText.alignment = TextAlignmentOptions.Left;
            statusText.fontStyle = FontStyles.Normal;
            statusText.richText = true;
        }

        if (reputationBar != null)
        {
            Place(reputationBar.rectTransform, 250, new Vector2(180, 12));
            reputationBar.material = null;

            // A dim track behind the bar, so its full length is always readable
            var track = new GameObject("Track", typeof(RectTransform)).AddComponent<Image>();
            track.transform.SetParent(reputationBar.transform.parent, false);
            track.transform.SetSiblingIndex(reputationBar.transform.GetSiblingIndex());
            Place(track.rectTransform, 250, new Vector2(180, 12));
            track.sprite = reputationBar.sprite;
            track.color = ArenaTheme.WithAlpha(ArenaTheme.TextPrimary, 0.08f);
            track.raycastTarget = false;
        }
    }

    static void Place(RectTransform rt, float x, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0);
        rt.sizeDelta = size;
    }

    public void UpdateData(NodeData data, int index = 0)
    {
        string stat = (data.status ?? "").ToUpper();
        Color statColor = ArenaTheme.StatusColor(data.status);
        string muted = ArenaTheme.Hex(ArenaTheme.TextMuted);

        if (idText != null)
            idText.text = $"<color=#{muted}>{index + 1:00}</color>   <b>{data.id.ToUpper()}</b>";

        if (statusText != null)
        {
            statusText.text = $"{stat}   <color=#{muted}>{data.reputation:F1}%</color>";
            statusText.color = statColor;
        }

        // A minimum fill of 2% keeps the bar from disappearing completely
        float fill = Mathf.Max(0.02f, data.reputation / 100f);
        if (reputationBar != null && _targetFill < 0f)
        {
            // First update: start in place rather than animating from empty
            reputationBar.fillAmount = fill;
            reputationBar.color = statColor;
        }
        _targetFill = fill;
        _targetColor = statColor;
    }

    void Update()
    {
        if (reputationBar == null || _targetFill < 0f) return;

        float ease = ArenaFX.Damp(5f, Time.deltaTime);
        reputationBar.fillAmount = Mathf.Lerp(reputationBar.fillAmount, _targetFill, ease);
        reputationBar.color = Color.Lerp(reputationBar.color, _targetColor, ease);
    }
}
