using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click a node to open or close its holographic info panel. The panel eases open (a slight
/// scale-up with a fade) and eases closed again, instead of popping in and out.
/// </summary>
public class NodeClick : MonoBehaviour
{
    const float OpenSeconds = 0.22f;
    const float CloseSeconds = 0.16f;

    private GameObject hologramCanvas;
    private CanvasGroup group;
    private NodeHUD hud; // when present, it owns the card's scale (it also keeps it readable at distance)
    private Vector3 baseScale = Vector3.one;
    private Camera cam;

    private float shown;   // 0 closed .. 1 open
    private bool wantOpen;

    void Start()
    {
        cam = Camera.main;

        // FOOLPROOF FIX: Automatically search inside this specific node for its personal Canvas
        Canvas myCanvas = GetComponentInChildren<Canvas>(true);
        if (myCanvas != null)
        {
            hologramCanvas = myCanvas.gameObject;
            hud = GetComponentInChildren<NodeHUD>(true);
            if (hud == null) hud = GetComponentInParent<NodeHUD>();
            baseScale = hologramCanvas.transform.localScale;
            group = hologramCanvas.GetComponent<CanvasGroup>();
            if (group == null) group = hologramCanvas.AddComponent<CanvasGroup>();
            wantOpen = hologramCanvas.activeSelf;
            shown = wantOpen ? 1f : 0f;
            Apply();
        }
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        if (cam != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

            // If the ray hits THIS specific node, toggle its panel
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.transform == transform && hologramCanvas != null)
            {
                // Something else may have closed the panel directly; start from what is on screen
                if (!hologramCanvas.activeSelf) { shown = 0f; wantOpen = false; }
                wantOpen = !wantOpen;
                if (wantOpen) hologramCanvas.SetActive(true);
            }
        }

        Animate();
    }

    void Animate()
    {
        if (hologramCanvas == null) return;

        float target = wantOpen ? 1f : 0f;
        if (Mathf.Approximately(shown, target)) return;

        float step = Time.deltaTime / (wantOpen ? OpenSeconds : CloseSeconds);
        shown = Mathf.MoveTowards(shown, target, step);
        Apply();

        if (!wantOpen && shown <= 0f) hologramCanvas.SetActive(false);
    }

    void Apply()
    {
        // Ease out on open, ease in on close, so both ends feel soft
        float k = wantOpen ? 1f - Mathf.Pow(1f - shown, 3f) : shown * shown;
        float pop = Mathf.Lerp(0.86f, 1f, k);
        if (hud != null) hud.openScale = pop;
        else hologramCanvas.transform.localScale = baseScale * pop;
        if (group != null)
        {
            group.alpha = k;
            group.interactable = wantOpen;
            group.blocksRaycasts = wantOpen;
        }
    }
}
