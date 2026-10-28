using UnityEngine;

public class DiggableStump : MonoBehaviour
{
    public const int RequiredHits = 10;
    public int Hits { get; private set; }
    public bool IsRemoved => removed;
    public Terrain SourceTerrain;
    public TreeInstance SourceTree;
    private bool removed;

    public void Dig(TreeChoppingSystem system)
    {
        if (removed) return;
        Hits = Mathf.Min(Hits + 1, RequiredHits);
        if (Hits < RequiredHits) return;
        if (SourceTerrain != null && !system.RemoveTerrainTree(SourceTerrain, SourceTree)) return;
        removed = true;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
