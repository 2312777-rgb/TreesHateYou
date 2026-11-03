using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FirewoodCollector : MonoBehaviour
{
    public const int MaxCarriedWood = 3;
    private sealed class CarriedLog
    {
        public CollectibleFirewood Wood;
        public Vector3 Scale;
        public Collider[] Colliders;
        public bool[] States;
    }
    private readonly List<CarriedLog> carried = new List<CarriedLog>();
    public int CarriedWoodCount => carried.Count;
    public bool IsCarryingWood => carried.Count > 0;
    public bool IsThrowing { get; private set; }
    public int LastPickupFrame { get; private set; } = -1;
    public int LastDropFrame { get; private set; } = -1;
    public bool HasPickupTarget => CarriedWoodCount < MaxCarriedWood && !IsThrowing && FindTarget() != null;
    private Camera playerCamera;
    private CollectibleFirewood target;
    private CollectibleFirewood heldWood;
    private Vector3 worldScale;
    private Collider[] colliders;
    private bool[] colliderStates;
    private DailyStoryController story;

    private void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
    }

    private CollectibleFirewood FindTarget()
    {
        if (playerCamera == null || CarriedWoodCount >= MaxCarriedWood || IsThrowing) return null;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(ray, 2.5f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            CollectibleFirewood wood = hit.collider.GetComponentInParent<CollectibleFirewood>();
            return wood != null && !wood.IsCollected ? wood : null;
        }
        return null;
    }

    private void Update()
    {
        FurniturePlacement furniture = GetComponent<FurniturePlacement>();
        if (furniture != null && (furniture.IsHolding || furniture.LastInteractionFrame == Time.frameCount)) return;
        if (story == null) story = GetComponent<DailyStoryController>();
        if (Cursor.lockState != CursorLockMode.Locked || IsThrowing) return;
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
        bool drop = Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetKeyDown(KeyCode.F);
        bool drop = Input.GetKeyDown(KeyCode.G);
#else
        bool pressed = false;
        bool drop = false;
#endif
        WorldItemReticle tool = GetComponent<WorldItemReticle>();
        if (IsCarryingWood && drop) { DropWood(); return; }
        target = FindTarget();
        if (pressed && target != null && (tool == null || tool.LastPickupFrame != Time.frameCount)) PickUp(target);
    }

    private void PickUp(CollectibleFirewood wood)
    {
        if (CarriedWoodCount >= MaxCarriedWood || IsThrowing) return;
        heldWood = wood;
        LastPickupFrame = Time.frameCount;
        Transform item = wood.transform;
        worldScale = item.lossyScale;
        Rigidbody body = item.GetComponent<Rigidbody>();
        if (body != null)
        {
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
            body.useGravity = false;
        }
        colliders = item.GetComponentsInChildren<Collider>();
        colliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderStates[i] = colliders[i].enabled;
            colliders[i].enabled = false;
        }
        carried.Add(new CarriedLog { Wood = wood, Scale = worldScale, Colliders = colliders, States = colliderStates });
        Renderer renderer = item.GetComponentInChildren<Renderer>();
        float length = renderer != null ? renderer.bounds.size.magnitude : 1f;
        item.SetParent(playerCamera.transform, true);
        item.localRotation = Quaternion.Euler(-35f, 15f, -15f);
        item.localScale *= 0.8f / Mathf.Max(length, 0.01f);
        Vector3 center = playerCamera.transform.TransformPoint(new Vector3(-0.38f, -0.30f + (CarriedWoodCount - 1) * 0.14f, 0.85f));
        item.position += center - (renderer != null ? renderer.bounds.center : item.position);
    }

    public bool TakeStoredWood()
    {
        if (IsThrowing || CarriedWoodCount >= MaxCarriedWood || playerCamera == null) return false;
        GameObject prefab = Resources.Load<GameObject>("Chopping/Firewood");
        if (prefab == null) return false;
        GameObject log = Instantiate(prefab, playerCamera.transform.position, Quaternion.identity);
        Renderer renderer = log.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            Vector3 size = renderer.bounds.size;
            log.transform.localScale *= 1.15f / Mathf.Max(size.x, size.y, size.z, 0.01f);
        }
        CollectibleFirewood wood = log.GetComponent<CollectibleFirewood>();
        if (wood == null) wood = log.AddComponent<CollectibleFirewood>();
        PickUp(wood);
        return true;
    }

    public bool ThrowIntoStorage(Transform storage, System.Action onDelivered, int maxLogs = MaxCarriedWood, bool ontoTable = false)
    {
        if (!IsCarryingWood || IsThrowing || storage == null) return false;
        StartCoroutine(ThrowArc(storage, onDelivered, maxLogs, ontoTable));
        return true;
    }

    private IEnumerator ThrowArc(Transform storage, System.Action onDelivered, int maxLogs, bool ontoTable)
    {
        IsThrowing = true;
        int delivered = 0;
        while (IsCarryingWood && storage != null && delivered < maxLogs)
        {
        SelectLastLog();
        if (heldWood == null)
        {
            carried.RemoveAt(carried.Count - 1);
            continue;
        }
        Transform item = heldWood.transform;
        item.SetParent(null, true);
        Vector3 start = item.position;
        Renderer crate = storage.GetComponent<Renderer>();
        Vector3 end = crate != null ? crate.bounds.center : storage.position + Vector3.up * 0.25f;
        if (ontoTable && crate != null) end.y = crate.bounds.max.y + 0.1f;
        float elapsed = 0f;
        while (elapsed < 0.45f && item != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.45f);
            item.position = Vector3.Lerp(start, end, t) + Vector3.up * (0.35f * 4f * t * (1f - t));
            yield return null;
        }
        if (heldWood != null && storage != null)
        {
            heldWood.Collect();
            carried.RemoveAt(carried.Count - 1);
            heldWood = null;
            onDelivered?.Invoke();
            delivered++;
        }
        else if (heldWood != null) DropWood();
        }
        IsThrowing = false;
    }

    private void SelectLastLog()
    {
        CarriedLog log = carried[carried.Count - 1];
        heldWood = log.Wood;
        worldScale = log.Scale;
        colliders = log.Colliders;
        colliderStates = log.States;
    }

    private void DropWood()
    {
        if (!IsCarryingWood) return;
        LastDropFrame = Time.frameCount;
        SelectLastLog();
        if (heldWood == null)
        {
            carried.RemoveAt(carried.Count - 1);
            return;
        }
        Transform item = heldWood.transform;
        item.SetParent(null, true);
        item.localScale = worldScale;
        Renderer renderer = item.GetComponentInChildren<Renderer>();
        Vector3 center = playerCamera.transform.position + playerCamera.transform.forward * 1.1f - playerCamera.transform.right * 0.3f;
        item.position += center - (renderer != null ? renderer.bounds.center : item.position);
        // Dynamic rigidbodies require convex mesh colliders; the storage prefab uses a static mesh.
        foreach (MeshCollider mesh in item.GetComponentsInChildren<MeshCollider>()) mesh.convex = true;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderStates[i];
        Rigidbody body = item.GetComponent<Rigidbody>();
        if (body == null) body = item.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.angularVelocity = Vector3.zero;
        body.linearVelocity = playerCamera.transform.forward * 0.5f;
        carried.RemoveAt(carried.Count - 1);
        heldWood = null;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        while (IsCarryingWood && playerCamera != null) DropWood();
        IsThrowing = false;
    }

    private void OnGUI()
    {
        if (story != null && (story.HasInteractionPrompt || story.IsTransitioning)) return;
        if (!HasPickupTarget && !IsCarryingWood) return;
        GUIStyle style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        WorldItemReticle tool = GetComponent<WorldItemReticle>();
        if (IsCarryingWood && tool != null && tool.HasToolPickupTarget) return;
        string prompt = IsThrowing ? "Đang quăng gỗ vào thùng..." : IsCarryingWood
            ? "G — Thả gỗ trước"
            : "F — Cầm gỗ (tay trái)";
        if (!IsThrowing && HasPickupTarget) prompt = "F — Cầm gỗ | G — Thả gỗ";
        if (IsCarryingWood) prompt += " (" + CarriedWoodCount + "/" + MaxCarriedWood + ")";
        GUI.Box(new Rect(Screen.width * 0.5f - 175f, Screen.height - 90f, 350f, 40f), prompt, style);
    }
}
