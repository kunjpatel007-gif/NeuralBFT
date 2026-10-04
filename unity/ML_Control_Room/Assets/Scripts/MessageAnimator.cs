using UnityEngine;

public class MessageAnimator : MonoBehaviour
{
    public Color GetMessageColor(string messageType, int partitionId)
    {
        if (partitionId == 1)
        {
            // Network Partition 2 (Severed Half) - Navy Blue & Red pairs
            return messageType switch
            {
                "PRE_PREPARE" => new Color(0.1f, 0.2f, 0.8f, 1.0f), // Navy Blue
                "PREPARE"     => new Color(1.0f, 0.1f, 0.2f, 1.0f), // Red
                "COMMIT"      => new Color(1.0f, 0.2f, 0.2f, 1.0f), // Red
                _             => new Color(1.0f, 0.2f, 0.2f, 1.0f), // Red default
            };
        }
        else
        {
            // Network Partition 1 (Main Half) - Purple & Pink pairs
            return messageType switch
            {
                "PRE_PREPARE" => new Color(0.6f, 0.2f, 1.0f, 1.0f), // Purple
                "PREPARE"     => new Color(1.0f, 0.2f, 0.8f, 1.0f), // Pink
                "COMMIT"      => new Color(1.0f, 0.4f, 0.9f, 1.0f), // Lighter Pink
                _             => new Color(1.0f, 0.4f, 0.9f, 1.0f), // Pink default
            };
        }
    }
}