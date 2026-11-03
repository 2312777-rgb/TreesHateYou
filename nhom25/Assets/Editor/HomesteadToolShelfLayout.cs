#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HomesteadToolShelfLayout
{
    private const string Marker = "Homestead Tools - On Shelf V2";
    static HomesteadToolShelfLayout() { EditorApplication.update += TryArrange; }

    private static void TryArrange()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetActiveScene().name != "Environment_Free") return;
        if (GameObject.Find(Marker) != null) { EditorApplication.update -= TryArrange; return; }
        if (GameObject.Find("Lumberjack_Interior_Shelf") == null) return;
        EditorApplication.update -= TryArrange;
        Arrange();
    }

    [MenuItem("Tools/Homestead/Place tools on shelf")]
    private static void Arrange()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject shelf = GameObject.Find("Lumberjack_Interior_Shelf");
        string[] names = { "Lumberjack_Hammer_Display", "Lumberjack_Pickaxe_Display", "Lumberjack_Axe" };
        GameObject[] tools = Array.ConvertAll(names, GameObject.Find);
        if (shelf == null || Array.Exists(tools, item => item == null)) return;
        Renderer shelfRenderer = shelf.GetComponentInChildren<Renderer>();
        MeshFilter shelfMesh = shelf.GetComponentInChildren<MeshFilter>();
        if (shelfRenderer == null || shelfMesh == null) return;
        Vector3 shelfSize = shelfMesh.sharedMesh.bounds.size;
        Vector3 across = shelf.transform.TransformDirection(shelfSize.x >= shelfSize.z ? Vector3.right : Vector3.forward);
        across = Vector3.ProjectOnPlane(across, Vector3.up).normalized;
        Vector3 facing = Vector3.Cross(across, Vector3.up).normalized;
        GameObject crate = GameObject.Find("Lumberjack_Supply_Crate");
        if (crate != null && Vector3.Dot(facing, crate.transform.position - shelfRenderer.bounds.center) < 0f) facing = -facing;
        float width = (shelfSize.x >= shelfSize.z ? shelfSize.x * Mathf.Abs(shelf.transform.lossyScale.x)
            : shelfSize.z * Mathf.Abs(shelf.transform.lossyScale.z));
        float slotWidth = width * 0.28f;
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Arrange three tools on shelf");
        for (int i = 0; i < tools.Length; i++)
        {
            Transform tool = tools[i].transform;
            MeshFilter mesh = tools[i].GetComponentInChildren<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) continue;
            Vector3 size = mesh.sharedMesh.bounds.size;
            int longest = size.x >= size.y ? 0 : 1;
            if (size.z > size[longest]) longest = 2;
            int thinnest = size.x <= size.y ? 0 : 1;
            if (size.z < size[thinnest]) thinnest = 2;
            Vector3 longAxis = Vector3.zero; longAxis[longest] = 1f;
            Vector3 thinAxis = Vector3.zero; thinAxis[thinnest] = 1f;
            Undo.RecordObject(tool, "Place tool on shelf");
            Quaternion meshRelative = Quaternion.Inverse(tool.rotation) * mesh.transform.rotation;
            Quaternion source = Quaternion.LookRotation(thinAxis, longAxis);
            tool.rotation = Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Inverse(source) * Quaternion.Inverse(meshRelative);
            tool.rotation = Quaternion.AngleAxis(i == 0 ? -8f : i == 1 ? 4f : -4f, facing) * tool.rotation;
            tool.rotation = Quaternion.AngleAxis(-12f, across) * tool.rotation;
            Bounds bounds = ToolBounds(tools[i]);
            float toolWidth = Mathf.Abs(across.x) * bounds.size.x + Mathf.Abs(across.z) * bounds.size.z;
            if (toolWidth > slotWidth) tool.localScale *= slotWidth / toolWidth;
            bounds = ToolBounds(tools[i]);
            Vector3 desired = shelfRenderer.bounds.center + across * ((i - 1) * width * 0.30f);
            tool.position += new Vector3(desired.x - bounds.center.x,
                shelfRenderer.bounds.max.y + 0.002f - bounds.min.y, desired.z - bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tool);
        }
        if (GameObject.Find(Marker) == null) Undo.RegisterCreatedObjectUndo(new GameObject(Marker), "Shelf layout marker");
        EditorSceneManager.MarkSceneDirty(shelf.scene);
        EditorSceneManager.SaveScene(shelf.scene);
        Undo.CollapseUndoOperations(undo);
        Debug.Log("Three tools arranged upright on the shelf above the storage crate.");
    }

    private static Bounds ToolBounds(GameObject tool)
    {
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (MeshFilter mesh in tool.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null) continue;
            // Renderer AABBs include empty rotated corners, which made the handles float.
            foreach (Vector3 vertex in mesh.sharedMesh.vertices)
            {
                Vector3 point = mesh.transform.TransformPoint(vertex);
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }
}
#endif
