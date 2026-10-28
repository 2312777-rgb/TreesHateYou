using UnityEngine;
using System.Collections;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FirewoodCollector : MonoBehaviour
{
    public bool IsCarryingWood => heldWood != null;
    public bool IsThrowing { get; private set; }
    public int LastPickupFrame { get; private set; } = -1;
    public bool HasPickupTarget => !IsCarryingWood && !IsThrowing && FindTarget() != null;
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
        if (playerCamera == null || IsCarryingWood || IsThrowing) return null;
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
        if (story == null) story = GetComponent<DailyStoryController>();
        if (Cursor.lockState != CursorLockMode.Locked || IsThrowing) return;
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool drop = Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetKeyDown(KeyCode.E);
        bool drop = Input.GetKeyDown(KeyCode.G);
#else
        bool pressed = false;
        bool drop = false;
#endif
        if (IsCarryingWood)
        {
            if (drop) DropWood();
            return;
        }
        target = FindTarget();
        WorldItemReticle tool = GetComponent<WorldItemReticle>();
        if (pressed && target != null && (tool == null || tool.LastPickupFrame != Time.frameCount)) PickUp(target);
    }

    private void PickUp(CollectibleFirewood wood)
    {
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
        Renderer renderer = item.GetComponentInChildren<Renderer>();
        float length = renderer != null ? renderer.bounds.size.magnitude : 1f;
        item.SetParent(playerCamera.transform, true);
        item.localRotation = Quaternion.Euler(-35f, 15f, -15f);
        item.localScale *= 0.8f / Mathf.Max(length, 0.01f);
        Vector3 center = playerCamera.transform.TransformPoint(new Vector3(-0.38f, -0.22f, 0.85f));
        item.position += center - (renderer != null ? renderer.bounds.center : item.position);
    }

    public bool ThrowIntoStorage(Transform storage, System.Action onDelivered)
    {
        if (!IsCarryingWood || IsThrowing || storage == null) return false;
        StartCoroutine(ThrowArc(storage, onDelivered));
        return true;
    }

    private IEnumerator ThrowArc(Transform storage, System.Action onDelivered)
    {
        IsThrowing = true;
        Transform item = heldWood.transform;
        item.SetParent(null, true);
        Vector3 start = item.position;
        Renderer crate = storage.GetComponent<Renderer>();
        Vector3 end = crate != null ? crate.bounds.center : storage.position + Vector3.up * 0.25f;
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
            heldWood = null;
            onDelivered?.Invoke();
        }
        else if (heldWood != null) DropWood();
        IsThrowing = false;
    }

    private void DropWood()
    {
        if (heldWood == null) return;
        Transform item = heldWood.transform;
        item.SetParent(null, true);
        item.localScale = worldScale;
        Renderer renderer = item.GetComponentInChildren<Renderer>();
        Vector3 center = playerCamera.transform.position + playerCamera.transform.forward * 1.1f - playerCamera.transform.right * 0.3f;
        item.position += center - (renderer != null ? renderer.bounds.center : item.position);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderStates[i];
        Rigidbody body = item.GetComponent<Rigidbody>();
        if (body == null) body = item.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = playerCamera.transform.forward * 0.5f;
        heldWood = null;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (heldWood != null && playerCamera != null) DropWood();
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
            ? tool != null && tool.IsHoldingItem ? "G — Thả gỗ  |  Shift — Thả dụng cụ" : "G — Thả gỗ"
            : "E — Cầm gỗ (tay trái)";
        GUI.Box(new Rect(Screen.width * 0.5f - 175f, Screen.height - 90f, 350f, 40f), prompt, style);
    }
}
