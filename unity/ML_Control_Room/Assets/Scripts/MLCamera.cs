using UnityEngine;

/// <summary>
/// Orbit camera for the ML Control Room.
/// Left-click drag to orbit around the tree.
/// Right-click drag to pan.
/// Scroll wheel to zoom.
/// Double-click a node to focus on it.
/// Press R to reset view.
/// Press F to fit the entire tree in view.
/// </summary>
public class MLCamera : MonoBehaviour
{
    [Header("Orbit")]
    public float orbitSpeed = 0.3f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float minDistance = 3f;
    public float maxDistance = 200f;

    [Header("Pan")]
    public float panSpeed = 0.05f;

    [Header("Smoothing")]
    public float smoothTime = 0.08f;

    [Header("Auto-Rotate")]
    public bool autoRotate = true;
    public float autoRotateSpeed = 2f;
    public float idleTimeBeforeAutoRotate = 5f;

    // Internal state
    private float _yaw = 0f;
    private float _pitch = 25f;
    private float _distance = 40f;
    private Vector3 _focusPoint = Vector3.zero;

    // Smoothed values
    private float _smoothYaw;
    private float _smoothPitch;
    private float _smoothDistance;
    private Vector3 _smoothFocus;

    // Velocity refs for SmoothDamp
    private float _yawVel;
    private float _pitchVel;
    private float _distVel;
    private Vector3 _focusVel;

    // Idle tracking for auto-rotate
    private float _lastInputTime;

    void Start()
    {
        _smoothYaw = _yaw;
        _smoothPitch = _pitch;
        _smoothDistance = _distance;
        _smoothFocus = _focusPoint;
        _lastInputTime = Time.time;

        // Start looking at origin from a nice angle
        _yaw = 30f;
        _pitch = 25f;
        _distance = 50f;
    }

    void LateUpdate()
    {
        HandleOrbit();
        HandleZoom();
        HandlePan();
        HandleReset();
        HandleAutoRotate();

        // Smooth all values
        _smoothYaw = Mathf.SmoothDamp(_smoothYaw, _yaw, ref _yawVel, smoothTime);
        _smoothPitch = Mathf.SmoothDamp(_smoothPitch, _pitch, ref _pitchVel, smoothTime);
        _smoothDistance = Mathf.SmoothDamp(_smoothDistance, _distance, ref _distVel, smoothTime);
        _smoothFocus = Vector3.SmoothDamp(_smoothFocus, _focusPoint, ref _focusVel, smoothTime);

        // Apply rotation and position
        Quaternion rotation = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -_smoothDistance);
        transform.position = _smoothFocus + offset;
        transform.LookAt(_smoothFocus);
    }

    void HandleOrbit()
    {
        if (Input.GetMouseButton(0) && !Input.GetKey(KeyCode.LeftAlt))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
            {
                _yaw += dx * orbitSpeed * 100f * Time.deltaTime;
                _pitch -= dy * orbitSpeed * 100f * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
                _lastInputTime = Time.time;
            }
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _distance -= scroll * zoomSpeed * _distance * 0.3f;
            _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            _lastInputTime = Time.time;
        }
    }

    void HandlePan()
    {
        if (Input.GetMouseButton(1))
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
            {
                Vector3 right = transform.right;
                Vector3 up = transform.up;
                _focusPoint -= (right * dx + up * dy) * panSpeed * _distance * 0.1f;
                _lastInputTime = Time.time;
            }
        }
    }

    void HandleReset()
    {
        // R = Reset to default view
        if (Input.GetKeyDown(KeyCode.R))
        {
            _yaw = 30f;
            _pitch = 25f;
            _distance = 50f;
            _focusPoint = Vector3.zero;
            _lastInputTime = Time.time;
        }

        // F = Fit tree in view (find ForestRenderer bounds)
        if (Input.GetKeyDown(KeyCode.F))
        {
            ForestRenderer fr = FindAnyObjectByType<ForestRenderer>();
            if (fr != null)
            {
                // Find center of all children
                Renderer[] renderers = fr.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    _focusPoint = bounds.center;
                    _distance = bounds.size.magnitude * 1.2f;
                    _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
                }
            }
            _lastInputTime = Time.time;
        }
    }

    void HandleAutoRotate()
    {
        if (!autoRotate) return;

        float idleTime = Time.time - _lastInputTime;
        if (idleTime > idleTimeBeforeAutoRotate)
        {
            // Slowly orbit when idle for a cinematic effect
            _yaw += autoRotateSpeed * Time.deltaTime;
        }
    }
}
