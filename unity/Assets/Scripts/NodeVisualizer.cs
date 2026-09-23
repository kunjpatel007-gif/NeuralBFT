using UnityEngine;
using TMPro;

/// <summary>
/// Visualizes a single node's state: color, label, byzantine halo.
/// Attach to the Node Prefab alongside a Renderer and Collider.
/// </summary>
public class NodeVisualizer : MonoBehaviour
{
    [Header("Visual Components")]
    public Renderer nodeRenderer;
    public TextMeshPro labelText;       // Floating label above the node
    public GameObject byzantineHalo;    // Red pulsing ring child object

    [Header("Materials (assign in Inspector or auto-colored)")]
    public Material trustedMat;
    public Material watchedMat;
    public Material quarantinedMat;

    // Exposed so UIManager can read it on click
    public NodeData CurrentData { get; private set; }

    // Fallback colors when no materials are assigned
    private static readonly Color TrustedColor = new Color(0.2f, 0.85f, 0.4f);
    private static readonly Color WatchedColor = new Color(1f, 0.65f, 0f);
    private static readonly Color QuarantinedColor = new Color(0.95f, 0.15f, 0.15f);

    private MaterialPropertyBlock propBlock;

    void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        if (nodeRenderer == null) nodeRenderer = GetComponent<Renderer>();
    }

    public void UpdateState(NodeData data)
    {
        CurrentData = data;

        // ── Color ──
        if (trustedMat != null && watchedMat != null && quarantinedMat != null)
        {
            // Use assigned materials
            nodeRenderer.material = data.status switch
            {
                "Trusted" => trustedMat,
                "Watched" => watchedMat,
                "Quarantined" => quarantinedMat,
                _ => trustedMat
            };
        }
        else
        {
            // Fallback: tint the existing material via property block
            Color c = data.status switch
            {
                "Verified"    => new Color(0f, 1f, 1f),
                "Trusted"     => TrustedColor,
                "Watched"     => WatchedColor,
                "High Risk"   => new Color(1f, 0.4f, 0f),
                "Quarantined" => QuarantinedColor,
                "Blacklisted" => new Color(0.4f, 0f, 0f),
                _             => TrustedColor
            };
            nodeRenderer.GetPropertyBlock(propBlock);
            propBlock.SetColor("_Color", c);
            nodeRenderer.SetPropertyBlock(propBlock);
        }

        // ── Byzantine halo ──
        if (byzantineHalo != null)
        {
            byzantineHalo.SetActive(data.is_byzantine);
            if (data.is_byzantine)
            {
                // Pulsing scale effect
                float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.15f;
                byzantineHalo.transform.localScale = Vector3.one * pulse;
            }
        }

        // ── Label ──
        if (labelText != null)
        {
            labelText.text = $"{data.id}\n<size=70%>Rep: {Mathf.Round(data.reputation)}</size>\n<size=60%>{data.status}</size>";
            // Make label face camera
            if (Camera.main != null)
            {
                labelText.transform.rotation = Quaternion.LookRotation(
                    labelText.transform.position - Camera.main.transform.position);
            }
        }
    }
}
