using UnityEngine;
using UnityEngine.InputSystem; // NEW INPUT SYSTEM

/// <summary>
/// Smooth orbital camera controller for the Consensus Arena.
/// WASD to pan, scroll to zoom, right-click drag to orbit.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Orbit Settings")]
    public Transform target;
    public float orbitSpeed = 10f;
    public float zoomSpeed = 5f;
    public float panSpeed = 10f;

    [Header("Limits")]
    public float minDistance = 5f;
    public float maxDistance = 40f;
    public float minPitch = 10f;
    public float maxPitch = 80f;

    [Header("Auto-Rotate")]
    public bool autoRotate = true;
    public float autoRotateSpeed = 5f;

    private float currentDistance = 20f;
    private float currentYaw = 0f;
    private float currentPitch = 35f;
    private Vector3 panOffset = Vector3.zero;

    void Start()
    {
        if (target == null)
        {
            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.position = Vector3.zero;
            target = pivot.transform;
        }
        currentDistance = Vector3.Distance(transform.position, target.position);
        if (currentDistance < 1f) currentDistance = 20f;
    }

    void LateUpdate()
    {
        // Right-click drag to orbit
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            currentYaw += delta.x * orbitSpeed * Time.deltaTime;
            currentPitch -= delta.y * orbitSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
            autoRotate = false;
        }

        // Scroll to zoom
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                currentDistance -= Mathf.Sign(scroll) * zoomSpeed;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            }
        }

        // WASD to pan
        float h = 0f;
        float v = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) v += 1f;
            if (Keyboard.current.sKey.isPressed) v -= 1f;
            if (Keyboard.current.dKey.isPressed) h += 1f;
            if (Keyboard.current.aKey.isPressed) h -= 1f;
        }

        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            Vector3 right = transform.right * h;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * v;
            panOffset += (right + forward) * panSpeed * Time.deltaTime;
        }

        // Auto-rotate
        if (autoRotate)
        {
            currentYaw += autoRotateSpeed * Time.deltaTime;
        }

        // Apply orbit
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -currentDistance);
        transform.position = target.position + panOffset + offset;
        transform.LookAt(target.position + panOffset);
    }
}
