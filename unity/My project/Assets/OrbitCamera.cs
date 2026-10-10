using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A smooth Free Fly Camera that lets you fly anywhere in the arena using WASD,
/// look around with Right-Click, and limits your max distance so you don't get lost.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 15f;
    public float sprintMultiplier = 3f;
    
    [Header("Look")]
    public float lookSensitivity = 0.15f; // Slowed down to 75% of original speed
    
    [Header("Boundary")]
    public float maxRadius = 250f; // Increased so you can reach the LedgerZone at X=120

    [Header("Smoothing")]
    public float moveSmoothTime = 0.1f;
    public float lookSmoothTime = 0.06f; // small, so the view glides without feeling laggy
    
    private float pitch = 0f;
    private float yaw = 0f;
    private float smoothPitch = 0f;
    private float smoothYaw = 0f;
    private Vector3 currentVelocity = Vector3.zero;

    [Header("Intro")]
    [Tooltip("Seconds of the slow glide into the starting view when the scene opens.")]
    public float introSeconds = 3.2f;
    private Vector3 introOffset;
    private float introTime = -1f;

    void Start()
    {
        Vector3 euler = transform.eulerAngles;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x; // keep within -180..180 so the clamp works
        yaw = euler.y;
        smoothPitch = pitch;
        smoothYaw = yaw;

        // Open on a slow glide in: start a little further back and higher, then settle
        if (introSeconds > 0f)
        {
            introOffset = -transform.forward * 14f + Vector3.up * 4f;
            transform.position += introOffset;
            introTime = 0f;
        }
    }

    // Moves the camera along the remainder of the intro glide; flying still works on top of it
    void UpdateIntro()
    {
        if (introTime < 0f) return;
        float before = Remaining(introTime);
        introTime += Time.deltaTime;
        float after = Remaining(introTime);
        transform.position -= introOffset * (before - after);
        if (introTime >= introSeconds) introTime = -1f;
    }

    float Remaining(float time)
    {
        float t = Mathf.Clamp01(time / introSeconds);
        return 1f - t * t * t * (t * (t * 6f - 15f) + 10f); // smootherstep, so it starts and lands softly
    }

    void Update()
    {
        UpdateIntro();
        if (Mouse.current == null || Keyboard.current == null) return;

        // 1. Look Around
        if (Mouse.current.rightButton.isPressed)
        {
            yaw += Mouse.current.delta.x.ReadValue() * lookSensitivity;
            pitch -= Mouse.current.delta.y.ReadValue() * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }
        float lookEase = lookSmoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / lookSmoothTime) : 1f;
        smoothPitch = Mathf.Lerp(smoothPitch, pitch, lookEase);
        smoothYaw = Mathf.Lerp(smoothYaw, yaw, lookEase);
        transform.rotation = Quaternion.Euler(smoothPitch, smoothYaw, 0f);

        // 2. Move
        Vector3 inputDir = Vector3.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) inputDir += transform.forward;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) inputDir -= transform.forward;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputDir += transform.right;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputDir -= transform.right;
        if (Keyboard.current.eKey.isPressed) inputDir += Vector3.up;
        if (Keyboard.current.qKey.isPressed) inputDir -= Vector3.up;

        inputDir = inputDir.normalized;

        float scroll = Mouse.current.scroll.y.ReadValue();
        if (Mathf.Abs(scroll) > 0.01f)
        {
            inputDir += transform.forward * (scroll * 0.05f);
        }

        float speed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed) speed *= sprintMultiplier;

        // 3. Smooth Velocity (Eliminates the stopping jerk)
        Vector3 targetVelocity = inputDir * speed;
        float moveEase = moveSmoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / moveSmoothTime) : 1f;
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, moveEase); // frame-rate independent
        
        transform.position += currentVelocity * Time.deltaTime;

        // 4. Boundary
        if (transform.position.magnitude > maxRadius)
        {
            transform.position = transform.position.normalized * maxRadius;
            currentVelocity = Vector3.zero; // Kill momentum if hitting the wall
        }
    }
}