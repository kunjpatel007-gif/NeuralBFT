using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Click a message in flight to see its ML payload in a small floating card.
/// </summary>
public class MessageInteractable : MonoBehaviour
{
    public string senderId;
    public string msgType;
    public float mlThreat;
    public Dictionary<string, float> mlFeatures;

    private GameObject popup;

    void OnDestroy()
    {
        ClosePopup();
    }

    /// <summary>Closes the payload card, e.g. when the message arrives and is recycled.</summary>
    public void ClosePopup()
    {
        // The popup is un-parented, so it has to be cleaned up explicitly
        if (popup != null) Destroy(popup);
        popup = null;
    }

    void OnMouseDown()
    {
        // PERFORMANCE: Only allow clicking if the camera is zoomed in close (within 15 units)
        // This prevents accidental clicks and reduces clutter when looking at the whole network.
        Camera cam = Camera.main;
        if (cam == null || Vector3.Distance(cam.transform.position, transform.position) > 15f) return;

        // Toggle logic: If it's already open, clicking closes it
        if (popup != null) 
        {
            ClosePopup();
            return;
        }

        // Create the HUD dynamically ONLY when clicked, preventing any lag from thousands of UI elements
        popup = new GameObject("ML_Payload_HUD");
        // CRITICAL FIX: DO NOT SET PARENT! If we parent it, it orbits the spinning crystal.
        
        var tmp = popup.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 1.4f; 
        tmp.color = ArenaTheme.TextPrimary; // bright enough to read, not enough to glare
        tmp.fontStyle = FontStyles.Bold;

        string accent = ArenaTheme.Hex(ArenaTheme.Accent);
        string muted  = ArenaTheme.Hex(ArenaTheme.TextMuted);
        string warm   = ArenaTheme.Hex(ArenaTheme.Watched);
        string threat = ArenaTheme.Hex(Color.Lerp(ArenaTheme.Trusted, ArenaTheme.Quarantined, Mathf.Clamp01(mlThreat)));

        // A dark <mark> behind the text keeps it readable against anything
        string text = $"<mark=#0a0e18D8><color=#{accent}>{msgType}</color>  {(senderId ?? "").ToUpper()}\n";
        text += $"<size=85%><color=#{muted}>THREAT</color> <color=#{threat}>{(mlThreat * 100f):F1}%</color>\n";

        if (mlFeatures != null && mlFeatures.Count > 0)
        {
            float freq = mlFeatures.ContainsKey("msg_freq") ? mlFeatures["msg_freq"] : 0f;
            float lat = mlFeatures.ContainsKey("latency") ? mlFeatures["latency"] : 0f;
            text += $"<color=#{muted}>FRQ</color> <color=#{warm}>{freq:F0}</color>   <color=#{muted}>LAT</color> <color=#{warm}>{lat:F0}ms</color>";
        }
        else
        {
            text += $"<color=#{muted}>NO ML DATA</color>";
        }

        text += "</size></mark>";
        tmp.text = text;

        // Add the custom tracker script so it perfectly hovers without inheriting spin
        var tracker = popup.AddComponent<HoverAndLookAtCamera>();
        tracker.target = this.transform;
    }
}

public class HoverAndLookAtCamera : MonoBehaviour
{
    public Transform target;
    
    void LateUpdate()
    {
        if (target != null)
        {
            // Lock position globally 1.2 units above the target (much closer now)
            transform.position = target.position + new Vector3(0, 1.2f, 0);
        }
        else
        {
            // Auto-destroy if the target message block reaches its destination and dies
            Destroy(gameObject);
            return;
        }

        if (Camera.main != null)
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }
}
