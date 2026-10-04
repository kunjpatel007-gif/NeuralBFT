using UnityEngine;
using UnityEngine.UI;

public class CameraFacer : MonoBehaviour
{
    [Header("Background Color Override")]
    public bool  overrideBackgroundColor = false;
    public Color backgroundColor = new Color(0.08f, 0.10f, 0.14f, 0.95f);

    private Camera _cam; // cached — Camera.main is a tag search, never call in LateUpdate

    void Awake()
    {
        _cam = Camera.main;
    }

    void Start()
    {
        if (overrideBackgroundColor)
        {
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                img.material = null;
                img.color    = backgroundColor;
            }
        }
    }

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main; // re-acquire if scene reloaded
        if (_cam == null) return;

        transform.rotation = Quaternion.LookRotation(
            transform.position - _cam.transform.position
        );
    }
}
