using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class LeaderboardManager : MonoBehaviour
{
    public GameObject rowPrefab;
    public Transform rowContainer;
    private List<LeaderboardRow> rows = new List<LeaderboardRow>();

    void Start()
    {
        // 1. Destroy any dummy rows left in the prefab from the Editor
        if (rowContainer != null)
        {
            foreach (Transform child in rowContainer)
            {
                if (child.gameObject == rowPrefab) child.gameObject.SetActive(false);
                else Destroy(child.gameObject);
            }
        }

        // 2. Style the main Canvas
        RectTransform canvasRt = GetComponent<RectTransform>();
        if (canvasRt != null)
        {
            canvasRt.localScale = new Vector3(0.008f, 0.008f, 0.008f);
            canvasRt.pivot = new Vector2(0.5f, 1f); // TOP-CENTER pivot so it grows downwards!
            // Position it nicely in the upper right
            transform.position = new Vector3(14f, 8f, 0f);
        }

        // 3. Dark glass aesthetic - Scorch any teal backgrounds on ALL panel children!
        var allImages = GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var img in allImages)
        {
            // Skip row objects, we want them completely transparent
            if (img.GetComponentInParent<LeaderboardRow>() != null && img.GetComponent<LeaderboardRow>() == null) continue;
            
            img.material = null; 
            img.color = Color.white;
            
            // Add SciFi panel to the main background
            if (img.gameObject == this.gameObject || img.transform.parent == this.transform)
            {
                if (img.gameObject.GetComponent<SciFiPanel>() == null)
                    img.gameObject.AddComponent<SciFiPanel>();
            }
        }
            
        // Removed Outline component since SciFiPanel draws its own border

        // 4. Style the title header
        var allTMP = GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        foreach (var tmp in allTMP)
        {
            if (tmp.GetComponentInParent<LeaderboardRow>() == null)
            {
                ColorUtility.TryParseHtmlString("#e8e8ea", out Color titleColor);
                tmp.color = titleColor; 
                tmp.fontStyle = TMPro.FontStyles.Normal;
                tmp.fontSize = 24f; // Clean, modern typography
                tmp.alignment = TMPro.TextAlignmentOptions.Center; // Put heading in the center
            }
        }

        // 5. Fix the RowContainer layout
        if (rowContainer != null)
        {
            RectTransform rt = rowContainer.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0, -60f); // Tighter top margin
            }

            UnityEngine.UI.VerticalLayoutGroup vlg = rowContainer.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandHeight = false;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.spacing = 6f; // Tighter, cleaner spacing like a data table
            }
        }
    }

    public void UpdateBoard(List<NodeData> allNodes)
    {
        while (rows.Count > allNodes.Count)
        {
            Destroy(rows[rows.Count - 1].gameObject);
            rows.RemoveAt(rows.Count - 1);
        }

        while (rows.Count < allNodes.Count)
        {
            GameObject newRow = Instantiate(rowPrefab, rowContainer);
            newRow.SetActive(true); 
            
            RectTransform rowRect = newRow.GetComponent<RectTransform>();
            if (rowRect != null) rowRect.sizeDelta = new Vector2(720f, 40f); // Sleeker rows
            
            rows.Add(newRow.GetComponent<LeaderboardRow>());
        }

        // DYNAMIC RESIZING: Expand the canvas so it NEVER overflows!
        RectTransform canvasRt = GetComponent<RectTransform>();
        if (canvasRt != null && rowContainer != null)
        {
            float totalHeight = 80f + (allNodes.Count * 46f); // 80 header + rows(40h + 6spc)
            canvasRt.sizeDelta = new Vector2(760, Mathf.Max(200f, totalHeight));
            rowContainer.GetComponent<RectTransform>().sizeDelta = new Vector2(720, totalHeight);
        }

        var sortedNodes = allNodes.OrderByDescending(n => n.reputation).ToList();
        
        for(int i = 0; i < sortedNodes.Count; i++)
        {
            // Pass the index so we can do alternating row colors
            rows[i].UpdateData(sortedNodes[i], i);
        }
    }
}