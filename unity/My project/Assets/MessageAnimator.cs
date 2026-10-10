using UnityEngine;

/// <summary>
/// Message colours by type and network partition. Kept as a component for existing prefabs;
/// the palette itself lives in <see cref="ArenaTheme"/>.
/// </summary>
public class MessageAnimator : MonoBehaviour
{
    public Color GetMessageColor(string messageType, int partitionId)
    {
        return ArenaTheme.MessageColor(messageType, partitionId);
    }
}
