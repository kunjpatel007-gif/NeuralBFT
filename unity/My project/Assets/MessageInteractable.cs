using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class MessageInteractable : MonoBehaviour
{
    public string senderId;
    public string msgType;
    public float mlThreat;
    public Dictionary<string, float> mlFeatures;

    private GameObject popup;

    void OnDestroy()
    {
        // Clean up the un-parented popup when the message dies
        if (popup != null) Destroy(popup);
    }

    void OnMouseDown()
    {
        // PERFORMANCE: Only allow clicking if the camera is zoomed in close (within 15 units)
        // This prevents accidental clicks and reduces clutter when looking at the whole network.
        if (Vector3.Distance(Camera.main.transform.position, transform.position) > 15f) return;

        // Toggle logic: If it's already open, clicking closes it
        if (popup != null) 
        {
            Destroy(popup);
            return;
        }

        // Create the HUD dynamically ONLY when clicked, preventing any lag from thousands of UI elements
        popup = new GameObject("ML_Payload_HUD");
        // CRITICAL FIX: DO NOT SET PARENT! If we parent it, it orbits the spinning crystal.
        
        var tmp = popup.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 1.4f; 
        
        // Multiplied brightness by 6.0x (doubled from 3.0x). Massive HDR bloom!
        tmp.color = new Color(6.0f, 6.0f, 6.0f, 1.0f);
        
        tmp.fontStyle = FontStyles.Bold; // Make it thicker to stand out

        // Build the text payload with a dark background <mark> tag for perfect readability
        string text = $"<mark=#0a0a0cD0><color=#00FFFF>[{msgType}]</color> {senderId.ToUpper()}\n";
        text += $"<size=80%>--------------------</size>\n";
        text += $"THREAT: <color=#FF4500>{(mlThreat * 100f):F1}%</color>\n";
        
        if (mlFeatures != null && mlFeatures.Count > 0)
        {
            float freq = mlFeatures.ContainsKey("msg_freq") ? mlFeatures["msg_freq"] : 0f;
            float lat = mlFeatures.ContainsKey("latency") ? mlFeatures["latency"] : 0f;
            text += $"<color=#e8e8ea>FRQ: <color=#FFD700>{freq:F0}</color> | LAT: <color=#FFD700>{lat:F0}ms</color></color>";
        }
        else
        {
            text += "<color=#e8e8ea>NO ML DATA</color>";
        }
        
        text += "</mark>";
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
