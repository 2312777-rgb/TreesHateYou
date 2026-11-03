using System.Collections.Generic;
using UnityEngine;

public class WorkbenchWoodDisplay : MonoBehaviour
{
    private readonly List<GameObject> logs = new List<GameObject>();

    public void SetCount(int count)
    {
        while (logs.Count > 0)
        {
            GameObject old = logs[logs.Count - 1];
            logs.RemoveAt(logs.Count - 1);
            if (old != null) { old.SetActive(false); Destroy(old); }
        }
        Renderer table = GetComponent<Renderer>();
        GameObject prefab = Resources.Load<GameObject>("Chopping/Firewood");
        if (table == null || prefab == null) return;
        Bounds top = table.bounds;
        Vector3 across = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        for (int i = 0; i < Mathf.Min(count, DailyStoryProgress.FurnitureCost); i++)
        {
            GameObject log = Instantiate(prefab);
            log.name = "Crafting Wood " + (i + 1);
            foreach (Collider collider in log.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (Rigidbody body in log.GetComponentsInChildren<Rigidbody>())
            { body.isKinematic = true; body.useGravity = false; }
            log.transform.rotation = Quaternion.LookRotation(Vector3.Cross(across, Vector3.up), Vector3.up);
            Renderer mesh = log.GetComponentInChildren<Renderer>();
            if (mesh == null) { Destroy(log); continue; }
            Vector3 size = mesh.bounds.size;
            float longest = Mathf.Max(size.x, size.y, size.z, 0.01f);
            log.transform.localScale *= Mathf.Min(1.15f, Mathf.Min(top.size.x, top.size.z) * 0.75f) / longest;
            Bounds bounds = mesh.bounds;
            Vector3 center = top.center + across * ((i - 1) * 0.22f);
            center.y = top.max.y + bounds.extents.y + 0.008f;
            log.transform.position += center - bounds.center;
            log.transform.SetParent(transform, true);
            logs.Add(log);
        }
    }
}
