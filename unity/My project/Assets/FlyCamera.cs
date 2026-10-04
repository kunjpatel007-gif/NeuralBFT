using UnityEngine;
using UnityEngine.InputSystem;

public class FlyCamera : MonoBehaviour
{
    public float moveSpeed = 25f;
    public float mouseSensitivity = 0.1f; // Greatly reduced for raw pixel deltas
    public float speedMultiplier = 3f;
    
    private float rotationX = 0f;
    private float rotationY = 0f;

    void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        
        // Right click + drag to look around
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            rotationY += delta.x * mouseSensitivity;
            rotationX -= delta.y * mouseSensitivity;
            
            transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0f);
        }

        if (keyboard != null)
        {
            float currentSpeed = moveSpeed;
            if (keyboard.leftShiftKey.isPressed)
            {
                currentSpeed *= speedMultiplier;
            }

            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move += Vector3.down;

            transform.Translate(move * currentSpeed * Time.deltaTime, Space.Self);
        }
        
        // Prevent rolling
        Vector3 euler = transform.eulerAngles;
        euler.z = 0f;
        transform.eulerAngles = euler;
    }
}
