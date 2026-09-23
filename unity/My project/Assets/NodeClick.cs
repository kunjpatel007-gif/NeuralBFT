using UnityEngine;
using UnityEngine.InputSystem;

public class NodeClick : MonoBehaviour
{
    private GameObject hologramCanvas;
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
        
        // FOOLPROOF FIX: Automatically search inside this specific node for its personal Canvas
        Canvas myCanvas = GetComponentInChildren<Canvas>(true);
        if (myCanvas != null)
        {
            hologramCanvas = myCanvas.gameObject;
        }
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // If the laser hits THIS specific node
                if (hit.transform == transform)
                {
                    if (hologramCanvas != null)
                    {
                        hologramCanvas.SetActive(!hologramCanvas.activeSelf);
                    }
                }
            }
        }
    }
}