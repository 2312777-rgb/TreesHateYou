using UnityEngine;

public class ChoppableTree : MonoBehaviour
{
    public const int RequiredHits = 10;
    public int Hits { get; private set; }
    public bool IsCut { get; private set; }
    public Terrain SourceTerrain;
    public TreeInstance SourceTree;
    public Transform StandingTree;
    public bool IsPine;

    public void Chop(TreeChoppingSystem system)
    {
        if (IsCut || !IsPine) return;
        Hits = Mathf.Min(Hits + 1, RequiredHits);
        if (Hits < RequiredHits) return;
        // Keep the standing tree intact if the replacement asset is unavailable.
        if (!system.ReplaceWithStump(this)) return;
        IsCut = true;
        DailyStoryController story = system.GetComponent<DailyStoryController>();
        if (story != null) story.Progress.RecordTreeCut();
    }
}
