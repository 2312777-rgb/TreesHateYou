#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One-time edit-mode foundation repair. Never edits Terrain assets in Play mode.</summary>
[InitializeOnLoad]
public static class HomesteadFoundationBake
{
    private const string MarkerName = "Homestead Foundation - Leveled";
    private static double nextAttempt;

    static HomesteadFoundationBake()
    {
        EditorApplication.update += TryBake;
    }

    private static void TryBake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextAttempt) return;
        nextAttempt = EditorApplication.timeSinceStartup + 2;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Environment_Free") return;
        GameObject existing = GameObject.Find(MarkerName);
        if (existing != null)
        {
            EditorApplication.update -= TryBake;
            VerifyGround(existing.transform.position.y);
            return;
        }
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null || Terrain.activeTerrains.Length == 0) return;
        EditorApplication.update -= TryBake;
        try { Bake(house); }
        catch (Exception exception) { Debug.LogError("Foundation repair failed: " + exception); }
    }

    private static void VerifyGround(float targetHeight)
    {
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house == null) return;
        Bounds floor = new Bounds();
        bool found = false;
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) < 0 ||
                Mathf.Abs(renderer.bounds.min.y - targetHeight - 0.08f) > 0.3f) continue;
            if (!found) { floor = renderer.bounds; found = true; }
            else floor.Encapsulate(renderer.bounds);
        }
        if (!found) return;
        int samples = 0;
        float maxError = 0f;
        for (int z = 0; z <= 8; z++)
        for (int x = 0; x <= 8; x++)
        {
            Vector3 point = new Vector3(Mathf.Lerp(floor.min.x, floor.max.x, x / 8f), 0f,
                Mathf.Lerp(floor.min.z, floor.max.z, z / 8f));
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 local = point - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                maxError = Mathf.Max(maxError, Mathf.Abs(terrain.SampleHeight(point) + terrain.transform.position.y - targetHeight));
                samples++;
                break;
            }
        }
        Directory.CreateDirectory("Temp/HomesteadFoundation");
        File.AppendAllText("Temp/HomesteadFoundation/report.txt", "Verified ground samples: " + samples +
            "\nMaximum height error: " + maxError + " metres\n");
        if (samples != 81 || maxError > 0.03f)
            Debug.LogWarning("Foundation verification needs attention: samples=" + samples + ", error=" + maxError);
    }

    [MenuItem("Tools/Homestead/Level ground and repair house")]
    private static void RunManually()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house != null) Bake(house);
    }

    private static void Bake(GameObject house)
    {
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Level homestead ground and camera");
        Undo.RecordObject(house.transform, "Keep house upright");
        house.transform.rotation = Quaternion.Euler(0f, house.transform.eulerAngles.y, 0f);
        Physics.SyncTransforms();
        Bounds footprint = new Bounds();
        bool hasFloor = false;
        float floorBottom = float.MaxValue;
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Bounds bounds = renderer.bounds;
            floorBottom = Mathf.Min(floorBottom, bounds.min.y);
        }
        if (floorBottom == float.MaxValue)
            throw new InvalidOperationException("No wooden floor found; refusing to guess foundation height.");
        // Only ground-floor panels define the footprint; upper floors do not expand it.
        foreach (MeshRenderer renderer in house.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) < 0 ||
                renderer.bounds.min.y > floorBottom + 0.3f) continue;
            if (!hasFloor) { footprint = renderer.bounds; hasFloor = true; }
            else footprint.Encapsulate(renderer.bounds);
        }
        footprint.Expand(new Vector3(1.6f, 0f, 1.6f));
        float targetHeight = floorBottom - 0.08f;
        const float feather = 4f;
        int changedSamples = 0;
        float beforeMin = float.MaxValue;
        float beforeMax = float.MinValue;
        var touched = new List<TerrainData>();
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            TerrainData data = terrain.terrainData;
            if (!AssetDatabase.Contains(data))
                throw new InvalidOperationException("Terrain uses transient data. Stop Play and reopen the saved scene first.");
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int resolution = data.heightmapResolution;
            float spacingX = size.x / (resolution - 1);
            float spacingZ = size.z / (resolution - 1);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((footprint.min.x - feather - origin.x) / spacingX));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((footprint.min.z - feather - origin.z) / spacingZ));
            int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((footprint.max.x + feather - origin.x) / spacingX));
            int z1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((footprint.max.z + feather - origin.z) / spacingZ));
            if (x0 > x1 || z0 > z1) continue;
            float[,] heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            float normalized = (targetHeight - origin.y) / size.y;
            if (normalized < 0f || normalized > 1f)
                throw new InvalidOperationException("Foundation height lies outside Terrain range.");
            Undo.RegisterCompleteObjectUndo(data, "Flatten house foundation");
            for (int z = 0; z < heights.GetLength(0); z++)
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                float worldX = origin.x + (x0 + x) * spacingX;
                float worldZ = origin.z + (z0 + z) * spacingZ;
                float dx = Mathf.Max(footprint.min.x - worldX, 0f, worldX - footprint.max.x);
                float dz = Mathf.Max(footprint.min.z - worldZ, 0f, worldZ - footprint.max.z);
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance >= feather) continue;
                float weight = 1f - Mathf.SmoothStep(0f, 1f, distance / feather);
                if (distance == 0f)
                {
                    float oldHeight = origin.y + heights[z, x] * size.y;
                    beforeMin = Mathf.Min(beforeMin, oldHeight);
                    beforeMax = Mathf.Max(beforeMax, oldHeight);
                }
                heights[z, x] = Mathf.Lerp(heights[z, x], normalized, weight);
                changedSamples++;
            }
            data.SetHeights(x0, z0, heights);
            terrain.Flush();
            EditorUtility.SetDirty(data);
            touched.Add(data);
        }
        if (touched.Count == 0) throw new InvalidOperationException("No Terrain overlaps the house.");
        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            Undo.RecordObject(player.transform, "Remove player roll and pitch");
            player.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        }
        GameObject marker = GameObject.Find(MarkerName);
        if (marker == null) { marker = new GameObject(MarkerName); Undo.RegisterCreatedObjectUndo(marker, "Foundation marker"); }
        marker.transform.position = new Vector3(footprint.center.x, targetHeight, footprint.center.z);
        EditorSceneManager.MarkSceneDirty(house.scene);
        foreach (TerrainData data in touched) AssetDatabase.SaveAssetIfDirty(data);
        EditorSceneManager.SaveScene(house.scene);
        Undo.CollapseUndoOperations(undo);
        Directory.CreateDirectory("Temp/HomesteadFoundation");
        File.WriteAllText("Temp/HomesteadFoundation/report.txt",
            "Applied in edit mode\nHouse roll/pitch: 0\nPlayer roll/pitch: 0\nGround before: " + beforeMin + " .. " + beforeMax +
            "\nFlat pad height: " + targetHeight + "\nHeight samples adjusted: " + changedSamples +
            "\nTerrain assets saved: " + touched.Count + "\nFeather: 4 metres\n");
        Debug.Log("Homestead foundation leveled and saved. See Temp/HomesteadFoundation/report.txt");
    }
}
#endif
