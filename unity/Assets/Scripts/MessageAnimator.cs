using UnityEngine;

/// <summary>
/// Animates a small glowing orb from sender to receiver, then self-destructs.
/// Color-coded by message type. Attach to the Message Prefab.
/// </summary>
public class MessageAnimator : MonoBehaviour
{
    [Header("Flight Settings")]
    public float speed = 18f;
    public float arcHeight = 1.5f;     // How high the arc goes above the straight line

    private Vector3 startPos;
    private Vector3 endPos;
    private float journeyLength;
    private float startTime;
    private bool initialized = false;

    // Color mapping for message types
    private static readonly Color PrePrepareColor = new Color(0f, 0.8f, 1f);     // Cyan
    private static readonly Color PrepareColor = new Color(0.4f, 0.6f, 1f);      // Blue
    private static readonly Color CommitColor = new Color(0.2f, 1f, 0.4f);       // Green
    private static readonly Color BlockColor = new Color(1f, 0.8f, 0f);          // Gold
    private static readonly Color DefaultColor = new Color(0.7f, 0.7f, 1f);      // Light blue

    public void Initialize(Vector3 start, Vector3 end, string messageType)
    {
        startPos = start;
        endPos = end;
        startTime = Time.time;
        journeyLength = Vector3.Distance(startPos, endPos);
        initialized = true;

        // Color the orb based on message type
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Color color = messageType switch
            {
                "PRE_PREPARE" => PrePrepareColor,
                "PREPARE" => PrepareColor,
                "COMMIT" => CommitColor,
                "BLOCK" => BlockColor,
                "BLOCK_ANNOUNCE" => BlockColor,
                "VOTE" => PrepareColor,
                _ => DefaultColor
            };
            block.SetColor("_Color", color);
            // Also set emission for glow effect
            block.SetColor("_EmissionColor", color * 2f);
            rend.SetPropertyBlock(block);
        }

        // Auto-destroy safety net (in case something goes wrong)
        Destroy(gameObject, 5f);
    }

    void Update()
    {
        if (!initialized || journeyLength <= 0) return;

        float distCovered = (Time.time - startTime) * speed;
        float t = Mathf.Clamp01(distCovered / journeyLength);

        // Lerp position with a parabolic arc
        Vector3 linearPos = Vector3.Lerp(startPos, endPos, t);
        float arc = arcHeight * 4f * t * (1f - t);   // peaks at t=0.5
        linearPos.y += arc;

        transform.position = linearPos;

        // Shrink as it approaches the target
        float scale = Mathf.Lerp(1f, 0.3f, t);
        transform.localScale = Vector3.one * scale * 0.3f;

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }
}
