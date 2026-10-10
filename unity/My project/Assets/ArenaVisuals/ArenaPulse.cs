using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A pooled one-shot ring of light that expands and fades: message arrivals, status changes,
/// blocks landing, consensus pulses. Call <see cref="Ring"/>; nothing needs cleaning up.
/// </summary>
public class ArenaPulse : MonoBehaviour
{
    static readonly Stack<ArenaPulse> Pool = new Stack<ArenaPulse>();
    static Transform _root;
    static MaterialPropertyBlock _block;

    MeshRenderer _renderer;
    Color _color;
    float _fromSize, _toSize, _duration, _intensity, _age;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Pool.Clear();
        _root = null;
        _block = null;
    }

    /// <param name="flatNormal">Leave null for a ring that faces the camera, or pass a surface normal to lay it flat.</param>
    public static void Ring(Vector3 position, Color color, float fromSize, float toSize, float duration,
                            float intensity = 1f, Vector3? flatNormal = null)
    {
        ArenaPulse pulse = Rent();
        bool billboard = !flatNormal.HasValue;

        pulse.transform.position = position;
        pulse.transform.rotation = billboard
            ? Quaternion.identity
            : Quaternion.FromToRotation(Vector3.forward, flatNormal.Value);
        pulse._renderer.sharedMaterial = ArenaFX.Glow(billboard, true);
        pulse._color = color;
        pulse._fromSize = fromSize;
        pulse._toSize = toSize;
        pulse._duration = Mathf.Max(0.05f, duration);
        pulse._intensity = intensity;
        pulse._age = 0f;

        pulse.Apply(0f);
        pulse.gameObject.SetActive(true);
    }

    static ArenaPulse Rent()
    {
        while (Pool.Count > 0)
        {
            ArenaPulse pooled = Pool.Pop();
            if (pooled != null) return pooled; // entries die with their scene
        }

        if (_root == null) _root = new GameObject("ArenaPulses").transform;

        MeshRenderer renderer = ArenaFX.CreateGlow(_root, "Pulse", 1f, true, true);
        ArenaPulse pulse = renderer.gameObject.AddComponent<ArenaPulse>();
        pulse._renderer = renderer;
        return pulse;
    }

    void Update()
    {
        _age += Time.deltaTime;
        float t = _age / _duration;
        if (t >= 1f)
        {
            gameObject.SetActive(false);
            Pool.Push(this);
            return;
        }
        Apply(t);
    }

    void Apply(float t)
    {
        if (_block == null) _block = new MaterialPropertyBlock();

        float fade = (1f - t) * (1f - t);
        transform.localScale = Vector3.one * Mathf.Lerp(_fromSize, _toSize, ArenaFX.EaseOutCubic(t));

        _block.Clear();
        ArenaFX.SetColor(_block, _color);
        _block.SetFloat(ArenaFX.IntensityId, _intensity * fade);
        _renderer.SetPropertyBlock(_block);
    }
}
