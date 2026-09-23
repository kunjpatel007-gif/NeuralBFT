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
    public float maxRadius = 35f; // Outer boundary limit

    [Header("Smoothing")]
    public float moveSmoothTime = 0.1f;
    
    private float pitch = 0f;
    private float yaw = 0f;
    private Vector3 targetPos;
    private Vector3 velocity = Vector3.zero;

    void Start()
    {
        Vector3 euler = transform.eulerAngles;
        pitch = euler.x;
        yaw = euler.y;
        targetPos = transform.position;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null) return;

        // 1. Look Around (Right Click & Drag)
        if (Mouse.current.rightButton.isPressed)
        {
            yaw += Mouse.current.delta.x.ReadValue() * lookSensitivity;
            pitch -= Mouse.current.delta.y.ReadValue() * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // 2. Move (WASD + EQ for Up/Down)
        Vector3 inputDir = Vector3.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) inputDir += transform.forward;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) inputDir -= transform.forward;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputDir += transform.right;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputDir -= transform.right;
        if (Keyboard.current.eKey.isPressed) inputDir += Vector3.up;
        if (Keyboard.current.qKey.isPressed) inputDir -= Vector3.up;

        // 3. Scroll wheel to zip forward/backward instantly
        float scroll = Mouse.current.scroll.y.ReadValue();
        if (Mathf.Abs(scroll) > 0.01f)
        {
            inputDir += transform.forward * (scroll * 0.05f);
        }

        // Apply movement speed
        float speed = moveSpeed;
        if (Keyboard.current.leftShiftKey.isPressed) speed *= sprintMultiplier;

        targetPos += inputDir * (speed * Time.deltaTime);

        // 4. Apply Boundary (Keep camera within a giant sphere around the center)
        if (targetPos.magnitude > maxRadius)
        {
            targetPos = targetPos.normalized * maxRadius;
        }

        // 5. Smoothly move to the target position
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, moveSmoothTime);
    }
}