using UnityEngine;

public static class FireplaceCollisionSetup
{
    public static int Ensure(GameObject fireplace)
    {
        if (fireplace == null) return 0;
        int added = AddMeshCollisions(fireplace, fireplace.transform.position, false);
        GameObject house = GameObject.Find("medieval_house_lite_v2");
        if (house != null) added += AddMeshCollisions(house, fireplace.transform.position, true);
        Physics.SyncTransforms();
        return added;
    }

    private static int AddMeshCollisions(GameObject root, Vector3 hearth, bool nearHearthOnly)
    {
        int added = 0;
        foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.sharedMesh == null || mesh.GetComponent<ParticleSystem>() != null) continue;
            Renderer renderer = mesh.GetComponent<Renderer>();
            if (renderer == null) continue;
            if (nearHearthOnly && Vector3.Distance(renderer.bounds.ClosestPoint(hearth), hearth) > 2f) continue;
            // Static triangle meshes preserve the hearth opening, unlike one solid box.
            if (mesh.GetComponentInParent<Rigidbody>() != null) continue;
            MeshCollider solid = null;
            foreach (MeshCollider existing in mesh.GetComponents<MeshCollider>())
                if (existing.sharedMesh == mesh.sharedMesh && !existing.isTrigger) { solid = existing; break; }
            if (solid == null)
            {
                solid = mesh.gameObject.AddComponent<MeshCollider>();
                solid.sharedMesh = mesh.sharedMesh;
                added++;
            }
            solid.convex = false;
            solid.isTrigger = false;
            solid.enabled = true;
        }
        return added;
    }
}
