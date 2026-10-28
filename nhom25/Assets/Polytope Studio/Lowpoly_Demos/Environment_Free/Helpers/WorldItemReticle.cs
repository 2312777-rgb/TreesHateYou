using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Draws a centered white reticle and identifies the lumberjack tools under it.
/// Attach this to the Player; the camera is found from the Player hierarchy.
/// </summary>
public class WorldItemReticle : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float maxDistance = 5.5f;

    private Renderer axeRenderer;
    private Renderer hammerRenderer;
    private Renderer pickaxeRenderer;
    private string hoveredTitle;
    private string hoveredDescription;
    private Image infoPanel;
    private Text infoText;
    private Sprite solidSprite;
    private Transform heldItem;
    public bool IsHoldingItem => heldItem != null;
    public bool HasToolPickupTarget => heldItem == null && TryGetPickupTarget(out _);
    public int LastPickupFrame { get; private set; } = -1;
    private Rigidbody heldBody;
    private Collider[] heldColliders;
    private bool[] colliderStates;
    private Vector3 heldWorldScale;
    private Vector3 heldRestPosition;
    private Quaternion heldRestRotation;
    private bool isSwinging;
    private float swingElapsed;
    private bool swingHitApplied;
    private TreeChoppingSystem chopping;
    private FirewoodCollector firewoodCollector;
    private DailyStoryController story;
    private const float WindupDuration = 0.16f;
    private const float StrikeDuration = 0.12f;
    private const float RecoveryDuration = 0.24f;
    private LumberjackDoorInteract[] doors;

    private void Awake()
    {
        chopping = GetComponent<TreeChoppingSystem>();
        if (chopping == null) chopping = gameObject.AddComponent<TreeChoppingSystem>();
        firewoodCollector = GetComponent<FirewoodCollector>();
        if (firewoodCollector == null) firewoodCollector = gameObject.AddComponent<FirewoodCollector>();
        story = GetComponent<DailyStoryController>();
        if (story == null) story = gameObject.AddComponent<DailyStoryController>();
        doors = FindObjectsByType<LumberjackDoorInteract>(FindObjectsSortMode.None);
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        axeRenderer = FindRenderer("Lumberjack_Axe");
        hammerRenderer = FindRenderer("Lumberjack_Hammer_Display");
        pickaxeRenderer = FindRenderer("Lumberjack_Pickaxe_Display");

        // All identified tools use the same aim-to-pick-up interaction.
        if (axeRenderer != null)
        {
            AxePickupThrow oldPickup = axeRenderer.GetComponent<AxePickupThrow>();
            if (oldPickup != null) oldPickup.enabled = false;
        }

        CreateHud();
    }

    private void Update()
    {
        hoveredTitle = null;
        hoveredDescription = null;

        if (heldItem != null)
        {
            hoveredTitle = GetItemTitle(heldItem.name);
            if (heldItem.name == "Lumberjack_Axe") chopping.ShowTarget(chopping.FindTarget(playerCamera));
            else if (heldItem.name == "Lumberjack_Pickaxe_Display")
                chopping.ShowStumpTarget(chopping.FindStumpTarget(playerCamera));
            if (DropPressed()) DropItem();
            else
            {
                if (!isSwinging && SwingPressed() && Cursor.lockState == CursorLockMode.Locked)
                {
                    isSwinging = true;
                    swingElapsed = 0f;
                    swingHitApplied = false;
                }
                if (isSwinging) UpdateSwing(Time.deltaTime);
            }
            UpdateInfoPanel();
            return;
        }

        if (playerCamera == null)
        {
            UpdateInfoPanel();
            return;
        }

        if (!TryGetPickupTarget(out Transform nearestTarget))
        {
            UpdateInfoPanel();
            return;
        }

        switch (nearestTarget.name)
        {
            case "Lumberjack_Axe":
                hoveredTitle = "Rìu";
                hoveredDescription = "Dùng để chặt gỗ";
                break;
            case "Lumberjack_Hammer_Display":
                hoveredTitle = "Búa";
                hoveredDescription = "Dụng cụ đóng và sửa chữa";
                break;
            case "Lumberjack_Pickaxe_Display":
                hoveredTitle = "Cuốc";
                hoveredDescription = "Dùng để đào đất và phá đá";
                break;
        }

        if (PickupPressed() && (firewoodCollector == null || firewoodCollector.LastPickupFrame != Time.frameCount))
            PickUpItem(nearestTarget);
        UpdateInfoPanel();
    }

    private bool TryGetPickupTarget(out Transform nearestTarget)
    {
        nearestTarget = null;
        if (playerCamera == null) return false;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float distance = float.MaxValue;
        FindTarget(ray, axeRenderer, ref nearestTarget, ref distance);
        FindTarget(ray, hammerRenderer, ref nearestTarget, ref distance);
        FindTarget(ray, pickaxeRenderer, ref nearestTarget, ref distance);
        if (nearestTarget == null || distance > maxDistance) return false;
        if (Physics.Raycast(ray, out RaycastHit obstruction, distance + 0.02f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !IsTargetTransform(obstruction.transform) && obstruction.distance < distance - 0.02f) return false;
        return true;
    }

    private static string GetItemTitle(string itemName)
    {
        switch (itemName)
        {
            case "Lumberjack_Axe": return "Rìu";
            case "Lumberjack_Hammer_Display": return "Búa";
            case "Lumberjack_Pickaxe_Display": return "Cuốc";
            default: return itemName;
        }
    }

    private void PickUpItem(Transform item)
    {
        LastPickupFrame = Time.frameCount;
        heldItem = item;
        heldWorldScale = item.lossyScale;
        heldBody = item.GetComponent<Rigidbody>();
        if (heldBody == null) heldBody = item.gameObject.AddComponent<Rigidbody>();
        if (!heldBody.isKinematic)
        {
            heldBody.linearVelocity = Vector3.zero;
            heldBody.angularVelocity = Vector3.zero;
        }
        heldBody.isKinematic = true;
        heldBody.useGravity = false;

        heldColliders = item.GetComponentsInChildren<Collider>();
        if (heldColliders.Length == 0)
            heldColliders = new Collider[] { item.gameObject.AddComponent<BoxCollider>() };
        colliderStates = new bool[heldColliders.Length];
        for (int i = 0; i < heldColliders.Length; i++)
        {
            colliderStates[i] = heldColliders[i].enabled;
            heldColliders[i].enabled = false;
        }

        float size = item.GetComponent<Renderer>().bounds.size.magnitude;
        item.SetParent(playerCamera.transform, true);
        item.localRotation = Quaternion.Euler(10f, -90f, -8f);
        // Keep every tool comfortably inside the view, regardless of its original size.
        item.localScale *= 0.7f / Mathf.Max(size, 0.01f);
        Renderer itemRenderer = item.GetComponent<Renderer>();
        Vector3 desiredCenter = playerCamera.transform.TransformPoint(new Vector3(0.38f, -0.22f, 0.8f));
        item.position += desiredCenter - itemRenderer.bounds.center;
        heldRestPosition = item.localPosition;
        heldRestRotation = item.localRotation;
        isSwinging = false;
        swingElapsed = 0f;
    }

    private void UpdateSwing(float deltaTime)
    {
        swingElapsed += deltaTime;
        if (!swingHitApplied && swingElapsed >= WindupDuration + StrikeDuration * 0.7f)
        {
            swingHitApplied = true;
            if (heldItem.name == "Lumberjack_Axe")
            {
                ChoppableTree tree = chopping.FindTarget(playerCamera);
                if (tree != null)
                {
                    tree.Chop(chopping);
                    chopping.ShowTarget(tree);
                }
            }
            else if (heldItem.name == "Lumberjack_Pickaxe_Display")
            {
                chopping.DigStump(playerCamera);
            }
        }
        Quaternion windup = Quaternion.Euler(-32f, 12f, -18f);
        Quaternion strike = Quaternion.Euler(65f, -22f, 18f);
        Quaternion offset;
        Vector3 translation;
        if (swingElapsed < WindupDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, swingElapsed / WindupDuration);
            offset = Quaternion.Slerp(Quaternion.identity, windup, t);
            translation = Vector3.Lerp(Vector3.zero, new Vector3(0.05f, 0.06f, -0.08f), t);
        }
        else if (swingElapsed < WindupDuration + StrikeDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, (swingElapsed - WindupDuration) / StrikeDuration);
            offset = Quaternion.Slerp(windup, strike, t);
            translation = Vector3.Lerp(new Vector3(0.05f, 0.06f, -0.08f),
                new Vector3(-0.12f, -0.08f, 0.16f), t);
        }
        else if (swingElapsed < WindupDuration + StrikeDuration + RecoveryDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f,
                (swingElapsed - WindupDuration - StrikeDuration) / RecoveryDuration);
            offset = Quaternion.Slerp(strike, Quaternion.identity, t);
            translation = Vector3.Lerp(new Vector3(-0.12f, -0.08f, 0.16f), Vector3.zero, t);
        }
        else
        {
            heldItem.localPosition = heldRestPosition;
            heldItem.localRotation = heldRestRotation;
            isSwinging = false;
            return;
        }

        // Swing around the grip below the tool, in camera space, rather than its mesh origin.
        Vector3 grip = new Vector3(0.38f, -0.48f, 0.65f);
        heldItem.localPosition = grip + offset * (heldRestPosition - grip) + translation;
        heldItem.localRotation = offset * heldRestRotation;
    }

    private void DropItem()
    {
        isSwinging = false;
        swingElapsed = 0f;
        heldItem.localPosition = heldRestPosition;
        heldItem.localRotation = heldRestRotation;
        heldItem.SetParent(null, true);
        heldItem.localScale = heldWorldScale;
        Renderer itemRenderer = heldItem.GetComponent<Renderer>();
        float radius = itemRenderer.bounds.extents.magnitude;
        Vector3 dropCenter = playerCamera.transform.position + playerCamera.transform.forward * (radius + 0.8f);
        heldItem.position += dropCenter - itemRenderer.bounds.center;
        for (int i = 0; i < heldColliders.Length; i++)
            if (heldColliders[i] != null) heldColliders[i].enabled = colliderStates[i];
        heldBody.useGravity = true;
        heldBody.isKinematic = false;
        heldBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        heldBody.linearVelocity = playerCamera.transform.forward * 0.5f;
        heldBody.angularVelocity = Vector3.zero;
        heldItem = null;
        hoveredTitle = null;
    }

    private static bool PickupPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.E);
#else
        return false;
#endif
    }

    private static bool SwingPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private static bool DropPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame ||
            Keyboard.current.rightShiftKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
#else
        return false;
#endif
    }

    private static Renderer FindRenderer(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        return target != null ? target.GetComponent<Renderer>() : null;
    }

    private static void FindTarget(Ray ray, Renderer targetRenderer, ref Transform nearestTarget,
        ref float nearestDistance)
    {
        if (targetRenderer == null || !targetRenderer.enabled ||
            !targetRenderer.gameObject.activeInHierarchy ||
            !targetRenderer.bounds.IntersectRay(ray, out float distance))
        {
            return;
        }

        if (distance < nearestDistance)
        {
            nearestDistance = distance;
            nearestTarget = targetRenderer.transform;
        }
    }

    private static bool IsTargetTransform(Transform target)
    {
        while (target != null)
        {
            if (target.name == "Lumberjack_Axe" ||
                target.name == "Lumberjack_Hammer_Display" ||
                target.name == "Lumberjack_Pickaxe_Display")
            {
                return true;
            }

            target = target.parent;
        }

        return false;
    }

    private void CreateHud()
    {
        GameObject canvasObject = new GameObject("World Item Reticle Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));

        // Dark edging keeps the fine white crosshair visible on both bright and dark scenes.
        CreateImage(canvas.transform, "Crosshair Outline Horizontal", new Vector2(20f, 4f),
            Vector2.zero, Color.black);
        CreateImage(canvas.transform, "Crosshair Outline Vertical", new Vector2(4f, 20f),
            Vector2.zero, Color.black);
        CreateImage(canvas.transform, "White Crosshair Horizontal", new Vector2(16f, 2f),
            Vector2.zero, Color.white);
        CreateImage(canvas.transform, "White Crosshair Vertical", new Vector2(2f, 16f),
            Vector2.zero, Color.white);

        infoPanel = CreateImage(canvas.transform, "Item Information Background",
            new Vector2(420f, 76f), new Vector2(0f, -53f), Color.clear);
        infoPanel.enabled = false;
        infoPanel.gameObject.SetActive(false);

        GameObject textObject = new GameObject("Item Information Text", typeof(RectTransform), typeof(Text));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(infoPanel.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        infoText = textObject.GetComponent<Text>();
        infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (infoText.font == null)
            infoText.font = Font.CreateDynamicFontFromOSFont("Arial", 15);
        infoText.fontSize = 20;
        infoText.alignment = TextAnchor.MiddleCenter;
        infoText.color = Color.white;
        infoText.horizontalOverflow = HorizontalWrapMode.Wrap;
        infoText.verticalOverflow = VerticalWrapMode.Overflow;
        infoText.supportRichText = true;
    }

    private Image CreateImage(Transform parent, string objectName, Vector2 size,
        Vector2 anchoredPosition, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = imageObject.GetComponent<Image>();
        image.sprite = solidSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

private void UpdateInfoPanel()
    {
        if (infoPanel == null || infoText == null)
            return;

        bool hasItem = heldItem == null && !string.IsNullOrEmpty(hoveredTitle);
        if (infoPanel.gameObject.activeSelf != hasItem)
            infoPanel.gameObject.SetActive(hasItem);

        if (hasItem)
            infoText.text = hoveredTitle;
    }

    private void OnGUI()
    {
        if (story != null && (story.IsTransitioning || story.HasInteractionPrompt)) return;
        if (firewoodCollector != null && (firewoodCollector.HasPickupTarget ||
            (firewoodCollector.IsCarryingWood && !HasToolPickupTarget))) return;
        if (heldItem == null && string.IsNullOrEmpty(hoveredTitle)) return;
        if (heldItem != null && doors != null)
        {
            foreach (LumberjackDoorInteract door in doors)
                if (door != null && door.IsInteractionPromptVisible) return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter
        };
        string prompt = heldItem != null ? "Nhấn Shift để thả đồ" : "Nhấn E để cầm đồ vật";
        GUI.Box(new Rect(Screen.width * 0.5f - 110f, Screen.height - 90f, 220f, 40f), prompt, style);
    }
}
