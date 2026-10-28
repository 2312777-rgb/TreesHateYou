using System.Collections.Generic;
using UnityEngine;

public class TreeChoppingSystem : MonoBehaviour
{
    private readonly Dictionary<Terrain, TerrainData> originalData = new Dictionary<Terrain, TerrainData>();
    private readonly Dictionary<Terrain, TerrainData> runtimeData = new Dictionary<Terrain, TerrainData>();
    private GameObject pineStump;
    private GameObject fruitStump;
    private ChoppableTree displayedTree;
    private DiggableStump displayedStump;
    private float displayUntil;

    private static Vector3Int PositionKey(Vector3 position)
    {
        return Vector3Int.RoundToInt(position * 100f);
    }

    private void Start()
    {
        pineStump = Resources.Load<GameObject>("Chopping/PineStump");
        fruitStump = Resources.Load<GameObject>("Chopping/FruitStump");
        var trunks = new Dictionary<Vector3Int, CapsuleCollider>();
        var stumpProxies = new Dictionary<Vector3Int, Transform>();
        GameObject proxies = GameObject.Find("Terrain Solid Colliders");
        if (proxies != null)
        {
            foreach (CapsuleCollider trunk in proxies.GetComponentsInChildren<CapsuleCollider>())
                trunks[PositionKey(trunk.transform.position)] = trunk;
            foreach (Transform group in proxies.transform)
                foreach (Transform proxy in group)
                    if (proxy.name.ToLowerInvariant().Contains("stump"))
                        stumpProxies[PositionKey(proxy.position)] = proxy;
        }

        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            foreach (TreeInstance instance in data.treeInstances)
            {
                GameObject prefab = data.treePrototypes[instance.prototypeIndex].prefab;
                if (prefab == null) continue;
                Vector3 position = terrain.transform.TransformPoint(Vector3.Scale(instance.position, data.size));
                if (prefab.name.ToLowerInvariant().Contains("stump") &&
                    stumpProxies.TryGetValue(PositionKey(position), out Transform proxy))
                {
                    DiggableStump stump = proxy.gameObject.AddComponent<DiggableStump>();
                    stump.SourceTerrain = terrain;
                    stump.SourceTree = instance;
                    continue;
                }
                if (!IsStandingTree(prefab.name)) continue;
                if (!trunks.TryGetValue(PositionKey(position), out CapsuleCollider trunk)) continue;
                ChoppableTree tree = trunk.GetComponent<ChoppableTree>();
                if (tree == null) tree = trunk.gameObject.AddComponent<ChoppableTree>();
                tree.SourceTerrain = terrain;
                tree.SourceTree = instance;
                tree.IsPine = prefab.name.Contains("Pine");
            }
        }

        // Trees placed directly in the scene use their LOD root instead of Terrain data.
        foreach (CapsuleCollider trunk in FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None))
        {
            if (trunk.GetComponent<ChoppableTree>() != null || !IsStandingTree(trunk.name)) continue;
            if (proxies != null && trunk.transform.IsChildOf(proxies.transform)) continue;
            LODGroup lod = trunk.GetComponentInParent<LODGroup>();
            var tree = trunk.gameObject.AddComponent<ChoppableTree>();
            tree.StandingTree = lod != null ? lod.transform : trunk.transform;
            tree.IsPine = trunk.name.Contains("Pine");
        }

        foreach (MeshFilter mesh in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (!mesh.name.ToLowerInvariant().Contains("stump")) continue;
            if (proxies != null && mesh.transform.IsChildOf(proxies.transform)) continue;
            if (mesh.GetComponentInParent<DiggableStump>() != null) continue;
            LODGroup lod = mesh.GetComponentInParent<LODGroup>();
            GameObject root = lod != null ? lod.gameObject : mesh.gameObject;
            root.AddComponent<DiggableStump>();
            if (root.GetComponentInChildren<Collider>() == null && mesh.sharedMesh != null)
                mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
        }
    }

    private static bool IsStandingTree(string name)
    {
        return name.Contains("Pine_Tree") && !name.Contains("stump") && !name.Contains("logs");
    }

    public ChoppableTree FindTarget(Camera camera)
    {
        if (camera == null) return null;
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, 2.5f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            ChoppableTree tree = hit.collider.GetComponentInParent<ChoppableTree>();
            return tree != null && tree.IsPine && !tree.IsCut ? tree : null;
        }
        return null;
    }

    public void ShowTarget(ChoppableTree tree)
    {
        if (tree == null) return;
        displayedTree = tree;
        displayedStump = null;
        displayUntil = Time.time + 0.8f;
    }

    public DiggableStump FindStumpTarget(Camera camera)
    {
        if (camera == null) return null;
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, 2.5f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            DiggableStump stump = hit.collider.GetComponentInParent<DiggableStump>();
            return stump != null && !stump.IsRemoved ? stump : null;
        }
        return null;
    }

    public void ShowStumpTarget(DiggableStump stump)
    {
        if (stump == null) return;
        displayedTree = null;
        displayedStump = stump;
        displayUntil = Time.time + 0.8f;
    }

    public void DigStump(Camera camera)
    {
        DiggableStump stump = FindStumpTarget(camera);
        if (stump == null) return;
        stump.Dig(this);
        ShowStumpTarget(stump);
    }

    public bool RemoveTerrainTree(Terrain terrain, TreeInstance instance)
    {
        if (!runtimeData.TryGetValue(terrain, out TerrainData data))
        {
            originalData.Add(terrain, terrain.terrainData);
            data = Instantiate(terrain.terrainData);
            runtimeData.Add(terrain, data);
            terrain.terrainData = data;
            TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) collider.terrainData = data;
        }
        var remaining = new List<TreeInstance>(data.treeInstances);
        int index = remaining.FindIndex(t => t.prototypeIndex == instance.prototypeIndex &&
            (t.position - instance.position).sqrMagnitude < 0.0000000001f);
        if (index < 0) return false;
        remaining.RemoveAt(index);
        data.SetTreeInstances(remaining.ToArray(), false);
        terrain.Flush();
        return true;
    }

    public bool ReplaceWithStump(ChoppableTree tree)
    {
        GameObject prefab = tree.IsPine ? pineStump : fruitStump;
        if (prefab == null)
        {
            Debug.LogError("Missing chopping stump prefab in Resources/Chopping.");
            return false;
        }
        Vector3 position;
        Quaternion rotation;
        Vector3 scale;
        GameObject standingVisual;
        if (tree.SourceTerrain != null)
        {
            Terrain terrain = tree.SourceTerrain;
            standingVisual = terrain.terrainData.treePrototypes[tree.SourceTree.prototypeIndex].prefab;
            if (!RemoveTerrainTree(terrain, tree.SourceTree)) return false;
            TerrainData data = terrain.terrainData;
            position = terrain.transform.TransformPoint(Vector3.Scale(tree.SourceTree.position, data.size));
            rotation = Quaternion.Euler(0f, tree.SourceTree.rotation * Mathf.Rad2Deg, 0f);
            scale = new Vector3(tree.SourceTree.widthScale, tree.SourceTree.heightScale, tree.SourceTree.widthScale);
            foreach (Collider collider in tree.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }
        else
        {
            standingVisual = tree.StandingTree.gameObject;
            position = tree.StandingTree.position;
            rotation = tree.StandingTree.rotation;
            scale = tree.StandingTree.lossyScale;
            tree.StandingTree.gameObject.SetActive(false);
        }
        CreateFallingVisual(standingVisual, position, rotation, scale);
        GameObject stump = Instantiate(prefab, position, rotation);
        stump.name = "Chopped Tree Stump";
        stump.transform.localScale = scale;
        stump.AddComponent<DiggableStump>();
        if (stump.GetComponentInChildren<Collider>() == null)
        {
            MeshFilter mesh = stump.GetComponentInChildren<MeshFilter>();
            if (mesh != null && mesh.sharedMesh != null)
                mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
        }
        return true;
    }

    private void CreateFallingVisual(GameObject source, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // Copy only the visible meshes: the falling tree must not clone gameplay scripts
        // or leave its old upright collider behind.
        GameObject falling = new GameObject("Falling Pine Tree");
        falling.transform.SetPositionAndRotation(position, rotation);
        falling.transform.localScale = scale;
        LODGroup lod = source.GetComponentInChildren<LODGroup>(true);
        Renderer[] renderers = lod != null && lod.GetLODs().Length > 0
            ? lod.GetLODs()[0].renderers : source.GetComponentsInChildren<MeshRenderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            MeshFilter mesh = renderer.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) continue;
            GameObject part = new GameObject(renderer.name);
            part.transform.SetParent(falling.transform, false);
            Matrix4x4 matrix = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            part.transform.localPosition = matrix.GetColumn(3);
            part.transform.localRotation = matrix.rotation;
            part.transform.localScale = matrix.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            MeshRenderer copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
        }
        falling.AddComponent<FallingTreeVisual>().Begin(position - transform.position);
    }

    private void OnGUI()
    {
        if (Time.time > displayUntil) return;
        if (displayedTree == null && displayedStump == null) return;
        float progress = displayedStump != null
            ? (float)displayedStump.Hits / DiggableStump.RequiredHits
            : (float)displayedTree.Hits / ChoppableTree.RequiredHits;
        float width = Mathf.Min(280f, Screen.width * 0.5f);
        Rect bar = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 65f, width, 14f);
        Color previous = GUI.color;
        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.65f, 0.2f);
        GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f,
            (bar.width - 4f) * progress, bar.height - 4f), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        foreach (var entry in originalData)
        {
            if (entry.Key == null) continue;
            entry.Key.terrainData = entry.Value;
            TerrainCollider collider = entry.Key.GetComponent<TerrainCollider>();
            if (collider != null) collider.terrainData = entry.Value;
        }
        foreach (TerrainData data in runtimeData.Values) if (data != null) Destroy(data);
    }
}
