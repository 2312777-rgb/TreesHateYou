using System;
using UnityEngine;

[ExecuteAlways]
public class InteriorGrassMask : MonoBehaviour
{
    private void OnEnable() { Refresh(); }

    public void Refresh()
    {
        float lowest = float.MaxValue;
        foreach (MeshRenderer floor in GetComponentsInChildren<MeshRenderer>())
            if (floor.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) >= 0)
                lowest = Mathf.Min(lowest, floor.bounds.min.y);
        Bounds local = new Bounds();
        bool found = false;
        foreach (MeshRenderer floor in GetComponentsInChildren<MeshRenderer>())
        {
            if (floor.name.IndexOf("wooden_floor", StringComparison.OrdinalIgnoreCase) < 0 ||
                floor.bounds.min.y > lowest + 0.3f) continue;
            MeshFilter mesh = floor.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) continue;
            Bounds bounds = mesh.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                point = transform.InverseTransformPoint(mesh.transform.TransformPoint(point));
                if (!found) { local = new Bounds(point, Vector3.zero); found = true; }
                else local.Encapsulate(point);
            }
        }
        if (!found) return;
        Vector3 min = local.min;
        Vector3 max = local.max;
        // Clip blades inside the floor volume, including tiny panel seams. Roots outside
        // stay in the Terrain and the outside-facing portions continue to render.
        min.y -= 2f;
        max.y += 1f;
        Shader.SetGlobalMatrix("_HomesteadGrassWorldToLocal", transform.worldToLocalMatrix);
        Shader.SetGlobalVector("_HomesteadGrassMin", min);
        Shader.SetGlobalVector("_HomesteadGrassMax", max);
        Shader.SetGlobalFloat("_HomesteadGrassMaskEnabled", 1f);
    }

    private void OnDisable() { Shader.SetGlobalFloat("_HomesteadGrassMaskEnabled", 0f); }
}
