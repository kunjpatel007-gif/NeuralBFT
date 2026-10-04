using UnityEngine;
using System.Collections.Generic;

public class BlockSpawner : MonoBehaviour
{
    public GameObject blockPrefab;
    public int maxBlocks = 40;

    private int blockCount = 0;
    private float stackHeight = 0.7f;
    private Queue<GameObject> _spawnedBlocks = new Queue<GameObject>();

    public void OnNewBlock()
    {
        Vector3 pos = new Vector3(0, blockCount * stackHeight, 0);
        GameObject b = Instantiate(blockPrefab, pos, Quaternion.identity);
        _spawnedBlocks.Enqueue(b);
        blockCount++;

        // Prune oldest block once we exceed the cap
        if (_spawnedBlocks.Count > maxBlocks)
        {
            GameObject oldest = _spawnedBlocks.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
    }

    void OnDestroy()
    {
        // Clean up any remaining blocks when this component is destroyed
        while (_spawnedBlocks.Count > 0)
        {
            GameObject b = _spawnedBlocks.Dequeue();
            if (b != null) Destroy(b);
        }
    }
}