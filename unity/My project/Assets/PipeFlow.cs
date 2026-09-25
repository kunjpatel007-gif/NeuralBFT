using UnityEngine;

public class PipeFlow : MonoBehaviour
{
    public float scrollSpeed = 2f;
    private Material mat;
    private static Texture2D dashedTex;

    void Start()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null)
        {
            if (dashedTex == null)
            {
                dashedTex = new Texture2D(64, 4, TextureFormat.RGBA32, false);
                dashedTex.wrapMode = TextureWrapMode.Repeat;
                for (int x = 0; x < 64; x++)
                {
                    Color c = (x % 16 < 8) ? Color.white : new Color(1f, 1f, 1f, 0.1f);
                    for (int y = 0; y < 4; y++) dashedTex.SetPixel(x, y, c);
                }
                dashedTex.Apply();
            }

            // Must use a particle or unlit shader that supports texture tiling/offset
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent"); // fallback
            if (shader == null) shader = Shader.Find("Sprites/Default");
            
            mat = new Material(shader);
            mat.mainTexture = dashedTex;
            
            // Keep the color that was previously assigned by NetworkManager
            mat.SetColor("_BaseColor", lr.material.color); 
            mat.SetColor("_Color", lr.material.color); 
            
            lr.material = mat;
            lr.textureMode = LineTextureMode.Tile;
        }
    }

    void Update()
    {
        if (mat != null)
        {
            float offset = Time.time * scrollSpeed;
            mat.mainTextureOffset = new Vector2(-offset, 0); // Negative to scroll forward
        }
    }
}
