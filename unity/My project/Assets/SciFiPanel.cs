using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Turns an Image into a flat instrument panel: near-square corners, a dark graphite fill and a
/// hairline off-white border, slightly stronger along the top edge. The sprite is generated once,
/// anti-aliased, and shared by every panel.
/// </summary>
[RequireComponent(typeof(Image))]
public class SciFiPanel : MonoBehaviour
{
    private const int Size = 128;
    private const float CornerRadius = 5f;

    private static Sprite _cachedSprite;

    void Start()
    {
        Image img = GetComponent<Image>();

        if (_cachedSprite == null)
        {
            _cachedSprite = CreatePanelSprite();
        }

        img.sprite = _cachedSprite;
        img.type = Image.Type.Sliced;
    }

    private static Sprite CreatePanelSprite()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "Arena Panel",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color glassTop    = new Color(0.072f, 0.076f, 0.084f);
        Color glassBottom = new Color(0.048f, 0.051f, 0.057f);
        Color border      = ArenaTheme.TextPrimary;
        var pixels = new Color[Size * Size];
        float half = Size * 0.5f;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Signed distance to a rounded rectangle: negative inside, in pixels
                float dx = Mathf.Abs(x + 0.5f - half) - (half - CornerRadius);
                float dy = Mathf.Abs(y + 0.5f - half) - (half - CornerRadius);
                float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
                float distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - CornerRadius;

                float coverage = Mathf.Clamp01(0.5f - distance);                 // anti-aliased edge
                float rim = Mathf.Clamp01(1f - Mathf.Abs(distance + 1.25f));     // 1px line just inside it
                float glow = 0f; // flat: no inner bleed

                float v = y / (float)(Size - 1);
                Color color = Color.Lerp(glassBottom, glassTop, v);

                // The border is brightest along the top edge, like light catching glass
                float rimStrength = rim * Mathf.Lerp(0.16f, 0.42f, v * v);
                color = Color.Lerp(color, border, Mathf.Clamp01(rimStrength + glow * v));

                color.a = coverage * 0.9f;
                pixels[y * Size + x] = color;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);

        // 9-slice borders so it stretches without distorting the corners
        float slice = CornerRadius + 4f;
        return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100, 0,
                             SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
    }
}
