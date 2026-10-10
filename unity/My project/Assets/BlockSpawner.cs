using UnityEngine;

/// <summary>
/// Adds each newly committed block to the chain column at the centre of the network.
/// The full ledger, with labels, is drawn by the LedgerZone.
/// </summary>
public class BlockSpawner : MonoBehaviour
{
    [Tooltip("No longer used: blocks are built procedurally by the chain column. Kept so existing scenes keep their reference.")]
    public GameObject blockPrefab;
    public int maxBlocks = 40;

    public void OnNewBlock()
    {
        ConsensusCore.Ensure().AddBlock();
    }
}
