using UnityEngine;

public class ShellRotator : MonoBehaviour
{
    public Vector3 rotationAxis = new Vector3(0.2f, 1.0f, 0.1f);
    public float rotationSpeed = 15f;

    void Update()
    {
        transform.Rotate(rotationAxis.normalized, rotationSpeed * Time.deltaTime, Space.Self);
    }
}
