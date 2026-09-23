using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this script to ANY World Space Canvas or GameObject and it will
/// automatically face the main camera every frame, like a billboard.
/// Also forcefully overrides the background panel color on startup.
/// </summary>
public class CameraFacer : MonoBehaviour
{
    [Header("Background Color Override")]
    public bool overrideBackgroundColor = false;
    public Color backgroundColor = new Color(0.08f, 0.10f, 0.14f, 0.95f); // Dark Charcoal

    void Start()
    {
        if (overrideBackgroundColor)
        {
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                img.material = null; // Reset to default UI material (kills M_Hologram shader error)
                img.color = backgroundColor;
            }
        }
    }

    void LateUpdate()
    {
        if (Camera.main == null) return;
        // Point THIS object's front face directly at the camera
        transform.rotation = Quaternion.LookRotation(
            transform.position - Camera.main.transform.position
        );
    }
}
