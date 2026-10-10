using UnityEngine;

/// <summary>
/// Gives a LineRenderer the arena's flowing-light look. Message links are now drawn by
/// MessageTraffic; this remains for any line that wants the same treatment.
/// </summary>
public class PipeFlow : MonoBehaviour
{
    public float scrollSpeed = 2f;

    void Start()
    {
        LineRenderer line = GetComponent<LineRenderer>();
        if (line == null) return;

        // One shared material for every pipe: no cloning, no leak
        line.sharedMaterial = ArenaFX.Beam("pipe", 0.75f, 5f, scrollSpeed * 0.25f, 1.4f);
        line.textureMode = LineTextureMode.Stretch;
        ArenaFX.Unlit(line);
    }
}
