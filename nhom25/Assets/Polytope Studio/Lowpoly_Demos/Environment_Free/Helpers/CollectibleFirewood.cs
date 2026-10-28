using UnityEngine;

public class CollectibleFirewood : MonoBehaviour
{
    public bool IsCollected { get; private set; }

    public bool Collect()
    {
        if (IsCollected) return false;
        IsCollected = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
}
