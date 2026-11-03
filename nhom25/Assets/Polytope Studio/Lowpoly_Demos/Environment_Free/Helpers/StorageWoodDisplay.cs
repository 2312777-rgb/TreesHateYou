using System.Collections.Generic;
using UnityEngine;

public class StorageWoodDisplay : MonoBehaviour
{
    private readonly List<GameObject> logs = new List<GameObject>();
    private Bounds crateBounds;
    private bool ready;

    private void Awake()
    {
        // Measure before adding display meshes so the contents never enlarge the crate.
        foreach (MeshFilter mesh in GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.sharedMesh == null) continue;
            Bounds bounds = mesh.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                point = transform.InverseTransformPoint(mesh.transform.TransformPoint(point));
                if (!ready) { crateBounds = new Bounds(point, Vector3.zero); ready = true; }
                else crateBounds.Encapsulate(point);
            }
        }
    }

    public void SetCount(int count)
    {
        if (!ready) return;
        GameObject prefab = Resources.Load<GameObject>("Chopping/Firewood");
        if (prefab == null) return;
        int visible = Mathf.Clamp(count, 0, 12);
        while (logs.Count > visible)
        {
            GameObject last = logs[logs.Count - 1];
            logs.RemoveAt(logs.Count - 1);
            last.SetActive(false);
            Destroy(last);
        }
        while (logs.Count < visible)
        {
            int index = logs.Count;
            GameObject log = Instantiate(prefab, transform, false);
            log.name = "Stored Wood " + (index + 1);
            // Contents represent stored items; only the storage interaction may retrieve them.
            foreach (Collider collider in log.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (Rigidbody body in log.GetComponentsInChildren<Rigidbody>()) { body.isKinematic = true; body.useGravity = false; }
            MeshFilter mesh = log.GetComponentInChildren<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) { Destroy(log); break; }
            Bounds model = mesh.sharedMesh.bounds;
            bool longX = crateBounds.size.x >= crateBounds.size.z;
            float width = longX ? crateBounds.size.x : crateBounds.size.z;
            float depth = longX ? crateBounds.size.z : crateBounds.size.x;
            // Match the 1.15 metre logs spawned by FallingTreeVisual.
            float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z));
            float factor = 1.15f / (Mathf.Max(model.size.x, model.size.y, model.size.z, 0.01f) * parentScale);
            log.transform.localScale = Vector3.one * factor;
            log.transform.localRotation = Quaternion.Euler(0f, longX ? 90f : 0f, index % 2 == 0 ? 2f : -2f);
            int column = index % 3;
            int layer = index / 3;
            Vector3 center = crateBounds.center;
            float along = (column - 1) * Mathf.Min(depth * 0.25f, model.size.x * factor * 1.1f);
            float back = 0f;
            center.x += longX ? back : along;
            center.z += longX ? along : back;
            center.y = crateBounds.min.y + crateBounds.size.y * 0.18f + model.size.y * factor * (0.5f + layer);
            Vector3 modelCenter = log.transform.InverseTransformPoint(mesh.transform.TransformPoint(model.center));
            log.transform.localPosition = center - log.transform.localRotation * Vector3.Scale(modelCenter, log.transform.localScale);
            logs.Add(log);
        }
    }
}
