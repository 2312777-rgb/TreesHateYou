#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HomesteadGrassCleanup
{
    private const string Marker = "Homestead Interior - Grass Cleared";
    private static double nextAttempt;

    static HomesteadGrassCleanup() { EditorApplication.update += TryClean; }

    private static void TryClean()
    {
        if (EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextAttempt) return;
        nextAttempt = EditorApplication.timeSinceStartup + 2;
        if (SceneManager.GetActiveScene().name != "Environment_Free") return;
        if (GameObject.Find(Marker) != null)
        {
            EditorApplication.update -= TryClean;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureMask();
            return;
        }
        if (GameObject.Find("medieval_house_lite_v2") == null) return;
        // Terrain edits must be saved against the original asset, after gameplay
        // restores any temporary TerrainData clones used for chopping.
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= TryClean;
        try { Clean(); }
        catch (Exception exception) { Debug.LogError("Interior grass cleanup failed: " + exception); }
    }

    private static void EnsureMask()
    {
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null) return;
        InteriorGrassMask mask = house.GetComponent<InteriorGrassMask>();
        if (mask == null)
        {
            mask = Undo.AddComponent<InteriorGrassMask>(house);
            EditorSceneManager.MarkSceneDirty(house.scene);
            EditorSceneManager.SaveScene(house.scene);
        }
        mask.Refresh();
    }

    [MenuItem("Tools/Homestead/Clear grass inside house only")]
    private static void Clean()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null) return;
        var floors = new List<MeshFilter>();
        float floorY = float.MaxValue;
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>())
            if (renderer.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) >= 0)
                floorY = Mathf.Min(floorY, renderer.bounds.min.y);
        Bounds footprint = new Bounds();
        bool found = false;
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) < 0 ||
                renderer.bounds.min.y > floorY + 0.3f) continue;
            MeshFilter mesh = renderer.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) continue;
            floors.Add(mesh);
            if (!found) { footprint = renderer.bounds; found = true; }
            else footprint.Encapsulate(renderer.bounds);
        }
        if (!found) throw new InvalidOperationException("No floor footprint; refusing to clear surrounding grass.");
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove grass inside house only");
        int removed = 0;
        int clearedCells = 0;
        int outsideChanges = 0;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (!AssetDatabase.Contains(data)) throw new InvalidOperationException("Terrain data is transient; stop Play first.");
            if (data.detailWidth == 0 || data.detailHeight == 0) continue;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            float dx = size.x / data.detailWidth;
            float dz = size.z / data.detailHeight;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((footprint.min.x - origin.x) / dx));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((footprint.min.z - origin.z) / dz));
            int x1 = Mathf.Min(data.detailWidth - 1, Mathf.FloorToInt((footprint.max.x - origin.x) / dx));
            int z1 = Mathf.Min(data.detailHeight - 1, Mathf.FloorToInt((footprint.max.z - origin.z) / dz));
            if (x0 > x1 || z0 > z1) continue;
            // Use a separately imported on-disk baseline. This also restores any
            // unsaved partial attempt before comparing the outside of the house.
            string backupPath = "Assets/Editor/GrassCleanupBaseline.asset";
            File.Copy(AssetDatabase.GetAssetPath(data), backupPath, true);
            AssetDatabase.ImportAsset(backupPath, ImportAssetOptions.ForceSynchronousImport);
            TerrainData baseline = AssetDatabase.LoadAssetAtPath<TerrainData>(backupPath);
            if (baseline == null) throw new InvalidOperationException("Cannot read saved grass baseline.");
            Undo.RegisterCompleteObjectUndo(data, "Clear interior grass detail layers");
            // A square patch also covers either orientation of a previous partial write.
            int span = Mathf.Max(x1 - x0 + 1, z1 - z0 + 1);
            x1 = Mathf.Min(data.detailWidth - 1, x0 + span - 1);
            z1 = Mathf.Min(data.detailHeight - 1, z0 + span - 1);
            try
            {
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 point = new Vector3(origin.x + (x + 0.5f) * dx, floorY,
                        origin.z + (z + 0.5f) * dz);
                    bool inside = InsideFloor(point, floors);
                    int original = baseline.GetDetailLayer(x, z, 1, 1, layer)[0, 0];
                    int desired = inside ? 0 : original;
                    int current = data.GetDetailLayer(x, z, 1, 1, layer)[0, 0];
                    if (inside && original > 0) { removed += original; clearedCells++; }
                    if (current != desired) data.SetDetailLayer(x, z, layer, new int[,] { { desired } });
                    int verified = data.GetDetailLayer(x, z, 1, 1, layer)[0, 0];
                    if (!inside && verified != original) outsideChanges++;
                    if (inside && verified != 0)
                        throw new InvalidOperationException("Interior cell did not clear at " + x + "," + z + " layer " + layer);
                }
            }
            finally { AssetDatabase.DeleteAsset(backupPath); }
            terrain.Flush();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }
        int meshGrass = 0;
        var disabled = new HashSet<GameObject>();
        foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
        {
            if (renderer.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Vector3 point = renderer.bounds.center;
            point.y = floorY;
            if (!InsideFloor(point, floors) || renderer.bounds.min.y > floorY + 0.3f) continue;
            LODGroup lod = renderer.GetComponentInParent<LODGroup>();
            GameObject root = lod != null ? lod.gameObject : renderer.gameObject;
            if (!disabled.Add(root)) continue;
            Undo.RecordObject(root, "Disable grass mesh inside house");
            root.SetActive(false);
            meshGrass++;
        }
        if (outsideChanges != 0) throw new InvalidOperationException("Outside detail verification failed.");
        EnsureMask();
        if (GameObject.Find(Marker) == null)
        {
            GameObject marker = new GameObject(Marker);
            marker.transform.position = footprint.center;
            Undo.RegisterCreatedObjectUndo(marker, "Interior cleanup marker");
        }
        EditorSceneManager.MarkSceneDirty(house.scene);
        EditorSceneManager.SaveScene(house.scene);
        Undo.CollapseUndoOperations(undo);
        Directory.CreateDirectory("Temp/HomesteadFoundation");
        File.WriteAllText("Temp/HomesteadFoundation/grass-report.txt", "Removed grass instances: " + removed +
            "\nCleared detail cells: " + clearedCells + "\nDisabled grass meshes: " + meshGrass +
            "\nVerified outside detail changes: " + outsideChanges + "\nScope: ground-floor panels only\n");
        Debug.Log("Interior grass cleared. Outside grass preserved.");
    }

    private static bool InsideFloor(Vector3 world, List<MeshFilter> floors)
    {
        foreach (MeshFilter floor in floors)
        {
            Bounds bounds = floor.sharedMesh.bounds;
            Vector3 local = floor.transform.InverseTransformPoint(world);
            Vector3 up = floor.transform.InverseTransformDirection(Vector3.up);
            // Drop only the floor-normal axis; do not use a world AABB that includes outside corners.
            int normal = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? 0 : 1;
            if (Mathf.Abs(up.z) > Mathf.Abs(up[normal])) normal = 2;
            bool inside = true;
            for (int axis = 0; axis < 3; axis++)
                if (axis != normal && (local[axis] < bounds.min[axis] || local[axis] > bounds.max[axis])) inside = false;
            if (inside) return true;
        }
        return false;
    }
}
#endif
