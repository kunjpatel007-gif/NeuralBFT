using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SciFiPanel : MonoBehaviour
{
    private static Sprite _cachedSprite;

    void Start()
    {
        Image img = GetComponent<Image>();
        
        if (_cachedSprite == null)
        {
            _cachedSprite = CreateSciFiSprite(128, 128, 16);
        }
        
        img.sprite = _cachedSprite;
        img.type = Image.Type.Sliced;
    }

    private Sprite CreateSciFiSprite(int width, int height, int cornerSize)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color bgColor = new Color(0.04f, 0.04f, 0.05f, 0.95f); // Deep dark glass #0a0a0c
        Color scanlineColor = new Color(0.0f, 0.0f, 0.0f, 0.3f);
        Color outlineColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        Color clear = new Color(0, 0, 0, 0);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Chamfered corners math (cut off corners)
                bool isTopLeft = (x < cornerSize && y > height - cornerSize - 1) && (x + (height - y - 1) < cornerSize);
                bool isTopRight = (x > width - cornerSize - 1 && y > height - cornerSize - 1) && ((width - x - 1) + (height - y - 1) < cornerSize);
                bool isBottomLeft = (x < cornerSize && y < cornerSize) && (x + y < cornerSize);
                bool isBottomRight = (x > width - cornerSize - 1 && y < cornerSize) && ((width - x - 1) + y < cornerSize);

                if (isTopLeft || isTopRight || isBottomLeft || isBottomRight)
                {
                    tex.SetPixel(x, y, clear);
                    continue;
                }

                // Outline logic
                bool isBorder = false;
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1) isBorder = true;
                
                // Chamfer borders
                if (x + (height - y - 1) == cornerSize && x < cornerSize) isBorder = true;
                if ((width - x - 1) + (height - y - 1) == cornerSize && x > width - cornerSize - 1) isBorder = true;
                if (x + y == cornerSize && x < cornerSize) isBorder = true;
                if ((width - x - 1) + y == cornerSize && x > width - cornerSize - 1) isBorder = true;

                if (isBorder)
                {
                    tex.SetPixel(x, y, outlineColor);
                }
                else
                {
                    // Scanline texture (every 3rd row is darker)
                    if (y % 3 == 0)
                        tex.SetPixel(x, y, new Color(bgColor.r - 0.02f, bgColor.g - 0.02f, bgColor.b - 0.02f, bgColor.a));
                    else
                        tex.SetPixel(x, y, bgColor);
                }
            }
        }
        tex.Apply();

        // 9-slice borders so it stretches nicely
        Vector4 borders = new Vector4(cornerSize + 2, cornerSize + 2, cornerSize + 2, cornerSize + 2);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, borders);
    }
}
