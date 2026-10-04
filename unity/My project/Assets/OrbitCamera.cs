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
    
    private float pitch = 0f;
    private float yaw = 0f;
    private Vector3 currentVelocity = Vector3.zero;

    void Start()
    {
        Vector3 euler = transform.eulerAngles;
        pitch = euler.x;
        yaw = euler.y;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null) return;

        // 1. Look Around
        if (Mouse.current.rightButton.isPressed)
        {
            yaw += Mouse.current.delta.x.ReadValue() * lookSensitivity;
            pitch -= Mouse.current.delta.y.ReadValue() * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

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
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * (1f / moveSmoothTime));
        
        transform.position += currentVelocity * Time.deltaTime;

        // 4. Boundary
        if (transform.position.magnitude > maxRadius)
        {
            transform.position = transform.position.normalized * maxRadius;
            currentVelocity = Vector3.zero; // Kill momentum if hitting the wall
        }
    }
}