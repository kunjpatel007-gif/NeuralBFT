using UnityEngine;

public class BlockSpawner : MonoBehaviour
{
    public GameObject blockPrefab;
    
    int blockCount = 0;
    float stackHeight = 0.7f;
    
    public void OnNewBlock()
    {
        Vector3 pos = new Vector3(0, blockCount * stackHeight, 0);
        Instantiate(blockPrefab, pos, Quaternion.identity);
        blockCount++;
    }
}