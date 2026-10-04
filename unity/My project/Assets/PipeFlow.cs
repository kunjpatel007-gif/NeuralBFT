using UnityEngine;

/// <summary>
/// Animates a dashed scrolling texture along a LineRenderer pipe.
/// Uses static shared resources to prevent per-pipe material/texture leaks.
/// </summary>
public class PipeFlow : MonoBehaviour
{
    public float scrollSpeed = 2f;

    // Static shared resources — created once, shared by ALL pipes, never leaked
    private static Texture2D s_DashedTex;
    private static Material  s_FlowMat;

    private LineRenderer _lr;

    void Start()
    {
        _lr = GetComponent<LineRenderer>();
        if (_lr == null) return;

        // Build the shared dashed texture exactly once
        if (s_DashedTex == null)
        {
            s_DashedTex = new Texture2D(64, 4, TextureFormat.RGBA32, false);
            s_DashedTex.wrapMode = TextureWrapMode.Repeat;
            for (int x = 0; x < 64; x++)
            {
                Color c = (x % 16 < 8) ? Color.white : new Color(1f, 1f, 1f, 0.1f);
                for (int y = 0; y < 4; y++) s_DashedTex.SetPixel(x, y, c);
            }
            s_DashedTex.Apply();
        }

        // Build the shared material exactly once
        if (s_FlowMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            s_FlowMat = new Material(shader);
            s_FlowMat.mainTexture = s_DashedTex;
        }

        // Assign via sharedMaterial — no cloning, no leak
        _lr.sharedMaterial = s_FlowMat;
        _lr.textureMode    = LineTextureMode.Tile;
    }

    void Update()
    {
        // Animate the texture offset on the shared material (visible to all pipes — intentional)
        if (s_FlowMat != null)
        {
            float offset = Time.time * scrollSpeed;
            s_FlowMat.mainTextureOffset = new Vector2(-offset, 0);
        }
    }

    // Static resources are cleaned up when the application quits
    void OnApplicationQuit()
    {
        if (s_FlowMat  != null) { Destroy(s_FlowMat);  s_FlowMat  = null; }
        if (s_DashedTex != null) { Destroy(s_DashedTex); s_DashedTex = null; }
    }
}
