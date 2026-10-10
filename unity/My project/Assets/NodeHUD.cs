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

/// <summary>
/// The floating card above a node: status as a coloured heading, then reputation, ML threat
/// score and the Beta-distribution parameters in a quiet two-tone layout.
/// </summary>
public class NodeHUD : MonoBehaviour
{
    public TextMeshProUGUI hudText;

    private const float BaseScale = 0.01f;      // canvas units to world units up close
    private const float HoverHeight = 2.7f;     // card centre above the node, up close
    private const float CardHeight = 1.7f;      // world height of the card at base scale
    private const float NearDistance = 14f;     // beyond this the card grows to stay readable
    private const float MaxGrowth = 3.2f;

    /// <summary>Set by NodeClick while the card eases open or closed (0.86 .. 1).</summary>
    [HideInInspector] public float openScale = 1f;

    private Transform _card;
    private Camera _cam;
    private float _growth = 1f;
    private bool _snap = true;     // jump straight to the right size the first frame it is shown
    private bool _hasData;
    private float _meshScale = -1f; // world scale the text's sharpness was last computed for

    void Start()
    {
        // Auto-layout the Canvas to hover above the node
        Canvas myCanvas = GetComponentInChildren<Canvas>(true);
        if (myCanvas == null) myCanvas = GetComponentInParent<Canvas>(true);
        if (myCanvas != null)
        {
            _card = myCanvas.transform;
            RectTransform rt = myCanvas.GetComponent<RectTransform>();
            rt.localPosition = new Vector3(0, HoverHeight, 0);
            rt.localScale = Vector3.one * BaseScale;
            rt.sizeDelta = new Vector2(320, 170);

            // Glass panel background
            var imgs = myCanvas.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var img in imgs)
            {
                img.material = null;
                img.color = Color.white; // SciFiPanel carries its own colours in the sprite
                if (img.gameObject.GetComponent<SciFiPanel>() == null)
                {
                    img.gameObject.AddComponent<SciFiPanel>();
                }
            }
        }

        if (hudText != null)
        {
            RectTransform txtRt = hudText.GetComponent<RectTransform>();
            txtRt.localPosition = Vector3.zero;
            txtRt.sizeDelta = new Vector2(292, 150);
            hudText.fontSize = 26;
            hudText.lineSpacing = 4f;
            hudText.textWrappingMode = TextWrappingModes.NoWrap;
            hudText.overflowMode = TextOverflowModes.Overflow;
            hudText.extraPadding = true; // keeps the small glyphs crisp when seen at an angle or far away
            hudText.alignment = TextAlignmentOptions.Center;
            hudText.richText = true;
            hudText.color = ArenaTheme.TextPrimary;
            // This runs the first time the card is opened, often after data has already arrived
            if (!_hasData) hudText.text = ""; // wait for live data (no fake preview values)
        }
    }

    // The whole card (panel and text together) always turns to face the camera, upright,
    // wherever you fly. Runs after the node's own spin so nothing can tilt it back.
    void OnEnable()
    {
        _snap = true;
        _meshScale = -1f;
    }

    // LateUpdate runs after every Update, so the camera (OrbitCamera) and the node (NodeVisualizer)
    // have both finished moving this frame: the card is never a frame behind either of them.
    void LateUpdate()
    {
        if (_card == null || !_card.gameObject.activeInHierarchy) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        _card.rotation = FacingCamera(_card.position, _cam.transform);

        // Keep a steady size on screen: beyond NearDistance the card grows with distance (up to
        // a limit), easing so it never jumps, and rises so it stays clear of the node's rings
        // Measured to the node, not the card, so the card's own rise cannot feed back into its size
        Vector3 anchor = _card.parent != null ? _card.parent.position : _card.position;
        float distance = Vector3.Distance(_cam.transform.position, anchor);
        float target = Mathf.Clamp(distance / NearDistance, 1f, MaxGrowth);
        _growth = _snap ? target : Mathf.Lerp(_growth, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
        _snap = false;

        _card.localScale = Vector3.one * (BaseScale * _growth * openScale);
        _card.localPosition = new Vector3(0f, HoverHeight + CardHeight * 0.5f * (_growth - 1f), 0f);

        // TextMesh Pro works out how sharp to draw its letters from their world size, but only
        // when the text is rebuilt (normally once per data update). Rebuild whenever the card's
        // size has changed noticeably, so the text is crisp at every distance, straight away.
        if (hudText != null)
        {
            float scale = hudText.rectTransform.lossyScale.y;
            if (_meshScale <= 0f || Mathf.Abs(scale - _meshScale) > _meshScale * 0.01f)
            {
                _meshScale = scale;
                hudText.ForceMeshUpdate(false, true);
            }
        }
    }

    /// <summary>
    /// Turns toward the camera's position (not just its angle), so the card swings round to face
    /// you as you fly past it sideways, while staying upright relative to your view.
    /// </summary>
    public static Quaternion FacingCamera(Vector3 position, Transform cam)
    {
        Vector3 away = position - cam.position;
        if (away.sqrMagnitude < 1e-6f) return cam.rotation;
        return Quaternion.LookRotation(away, cam.up);
    }

    // Very dark status colours (blacklisted) are lifted so the heading stays readable on the panel
    static Color Readable(Color c)
    {
        float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        return luminance < 0.35f ? Color.Lerp(c, ArenaTheme.TextPrimary, 0.55f) : c;
    }

    public void UpdateHUD(NodeData data)
    {
        if (hudText == null) return;
        _hasData = true;

        string status = (data.status ?? "").ToUpper();
        string statusHex = ArenaTheme.Hex(Readable(ArenaTheme.StatusColor(data.status)));
        string labelHex  = ArenaTheme.Hex(ArenaTheme.TextMuted);
        string valueHex  = ArenaTheme.Hex(ArenaTheme.TextPrimary);

        hudText.text =
            $"<color=#{statusHex}><b><cspace=0.12em>{status}</cspace></b></color>\n" +
            $"<size=72%><color=#{labelHex}>{data.id}</color></size>\n" +
            $"<color=#{labelHex}><size=78%>REP</size></color> <color=#{valueHex}><b>{data.reputation:F1}%</b></color>" +
            $"   <color=#{labelHex}><size=78%>THREAT</size></color> <color=#{valueHex}><b>{data.ml_prob:F2}</b></color>\n" +
            $"<size=82%><color=#{labelHex}>\u03B1</color> <color=#{valueHex}>{data.alpha:F1}</color>" +
            $"   <color=#{labelHex}>\u03B2</color> <color=#{valueHex}>{data.beta:F1}</color></size>";
    }
}
