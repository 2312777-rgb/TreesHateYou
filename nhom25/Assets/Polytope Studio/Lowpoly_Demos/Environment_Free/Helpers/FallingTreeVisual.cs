using System.Collections;
using UnityEngine;

public class FallingTreeVisual : MonoBehaviour
{
    public void Begin(Vector3 awayFromPlayer)
    {
        StartCoroutine(Fall(awayFromPlayer));
    }

    private IEnumerator Fall(Vector3 direction)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, direction).normalized;
        Quaternion initial = transform.rotation;
        float height = 1f;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            height = Mathf.Max(height, renderer.bounds.max.y - transform.position.y);
        float stopAngle = FindGroundContactAngle(direction, height);
        // Ease in slowly, then accelerate like a tree tipping under its own weight.
        const float duration = 1.8f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.rotation = Quaternion.AngleAxis(stopAngle * t * t, axis) * initial;
            yield return null;
        }
        transform.rotation = Quaternion.AngleAxis(stopAngle, axis) * initial;
        yield return new WaitForSeconds(3f);
        if (SpawnFirewood(direction, height)) Destroy(gameObject);
    }

    private bool SpawnFirewood(Vector3 direction, float height)
    {
        GameObject prefab = Resources.Load<GameObject>("Chopping/Firewood");
        if (prefab == null)
        {
            Debug.LogError("Missing Resources/Chopping/Firewood prefab; keeping fallen tree.");
            return false;
        }
        Vector3 sideways = Vector3.Cross(Vector3.up, direction);
        var spawned = new System.Collections.Generic.List<GameObject>();
        // Keep the drops near the stump rather than hiding them under the distant canopy.
        for (int i = 0; i < 3; i++)
        {
            Vector3 position = transform.position + direction * (0.95f + i * 0.55f)
                + sideways * (i % 2 == 0 ? -0.4f : 0.4f);
            if (!TryGroundHeight(position, out float ground)) break;
            position.y = ground;
            GameObject wood = Instantiate(prefab, position,
                Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, i * 17f - 15f, 0f));
            spawned.Add(wood);
            wood.name = "Collectible Firewood";
            bool hasBounds = false;
            Bounds localBounds = new Bounds();
            foreach (MeshFilter mesh in wood.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null) continue;
                Bounds meshBounds = mesh.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    point = wood.transform.InverseTransformPoint(mesh.transform.TransformPoint(point));
                    if (!hasBounds) { localBounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else localBounds.Encapsulate(point);
                }
            }
            if (!hasBounds) break;
            float longest = Mathf.Max(localBounds.size.x, Mathf.Max(localBounds.size.y, localBounds.size.z));
            if (longest < 0.001f) break;
            wood.transform.localScale = Vector3.one * (1.15f / longest);
            Vector3 center = wood.transform.TransformPoint(localBounds.center);
            wood.transform.position += new Vector3(position.x - center.x, 0f, position.z - center.z);
            // Sample the whole footprint. A single height at the pivot buries logs on slopes.
            float minimumY = float.MaxValue;
            float highestGround = ground;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = wood.transform.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                minimumY = Mathf.Min(minimumY, point.y);
                if (TryGroundHeight(point, out float sampleGround)) highestGround = Mathf.Max(highestGround, sampleGround);
            }
            wood.transform.position += Vector3.up * (highestGround + 0.06f - minimumY);
            foreach (Collider collider in wood.GetComponentsInChildren<Collider>()) collider.enabled = false;
            BoxCollider pickupCollider = wood.AddComponent<BoxCollider>();
            pickupCollider.center = localBounds.center;
            pickupCollider.size = localBounds.size;
            wood.AddComponent<CollectibleFirewood>();
        }
        if (spawned.Count == 3 && spawned.TrueForAll(wood => wood.GetComponent<CollectibleFirewood>() != null))
            return true;
        foreach (GameObject wood in spawned) { wood.SetActive(false); Destroy(wood); }
        Debug.LogWarning("Could not place all three logs on ground; keeping the fallen tree.");
        return false;
    }

    private float FindGroundContactAngle(Vector3 direction, float height)
    {
        // Check along the trunk, not just the root, so hills stop the fall early.
        for (float angle = 1f; angle <= 100f; angle += 1f)
        {
            float radians = angle * Mathf.Deg2Rad;
            for (int sample = 1; sample <= 24; sample++)
            {
                float distance = height * sample / 24f;
                if (distance < 0.5f) continue;
                Vector3 point = transform.position + direction * (Mathf.Sin(radians) * distance)
                    + Vector3.up * (Mathf.Cos(radians) * distance);
                if (TryGroundHeight(point, out float ground) && point.y - 0.18f <= ground)
                    return Mathf.Max(0f, angle - 1f);
            }
        }
        // Unknown ground: never tip past horizontal or move the tree below the map.
        return 88f;
    }

    private static bool TryGroundHeight(Vector3 point, out float ground)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 local = terrain.transform.InverseTransformPoint(point);
            Vector3 size = terrain.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
            ground = terrain.SampleHeight(point) + terrain.transform.position.y;
            return true;
        }
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 100f, Vector3.down,
            200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider is CharacterController || hit.collider.GetComponentInParent<ChoppableTree>() != null ||
                hit.collider.GetComponentInParent<DiggableStump>() != null) continue;
            ground = hit.point.y;
            return true;
        }
        ground = 0f;
        return false;
    }
}
