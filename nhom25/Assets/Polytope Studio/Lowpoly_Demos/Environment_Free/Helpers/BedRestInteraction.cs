using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class BedRestInteraction : MonoBehaviour
{
    [SerializeField] private string playerObjectName = "Player";
    [SerializeField] private float interactionDistance = 2.5f;
    [SerializeField] private float cameraHeightAboveBed = 0.65f;
    private const float restingCameraPitch = 7f;
    [SerializeField] private bool startInBed = true;
    [SerializeField] private float restingYawLimit = 60f;
    [SerializeField] private float restingPitchLimit = 35f;
    private float restingYaw;
    private float restingPitch;

    private Transform player;
    private CharacterController characterController;
    private PT_PlayerMovement movement;
    private PT_MouseLook mouseLook;
    private WorldItemReticle itemReticle;
    private Camera playerCamera;
    private Renderer bedRenderer;

    private bool isResting;
    private bool movementWasEnabled;
    private bool controllerWasEnabled;
    private bool mouseLookWasEnabled;
    private Vector3 savedPlayerPosition;
    private Quaternion savedPlayerRotation;
    private Vector3 savedCameraLocalPosition;
    private Quaternion savedCameraLocalRotation;

    private void Awake()
    {
        bedRenderer = GetComponent<Renderer>();
        GameObject playerObject = GameObject.Find(playerObjectName);
        if (playerObject == null)
        {
            Debug.LogWarning($"BedRestInteraction: Could not find Player object named '{playerObjectName}'.", this);
            return;
        }

        player = playerObject.transform;
        itemReticle = playerObject.GetComponent<WorldItemReticle>();
        characterController = playerObject.GetComponent<CharacterController>();
        movement = playerObject.GetComponent<PT_PlayerMovement>();
        playerCamera = playerObject.GetComponentInChildren<Camera>();
        if (playerCamera != null)
        {
            mouseLook = playerCamera.GetComponent<PT_MouseLook>();
        }
    }

    private System.Collections.IEnumerator Start()
    {
        // Let the player and camera finish their startup before entering bed.
        yield return null;
        if (startInBed && player != null && playerCamera != null && !isResting)
            GetIntoBed();
    }

    private void Update()
    {
        if (player == null || playerCamera == null)
        {
            return;
        }

        ReadInteractionInput(out bool enterPressed, out bool exitPressed);
        DailyStoryController story = player.GetComponent<DailyStoryController>();
        if (story != null && story.IsTransitioning) return;
        if (isResting)
        {
            if (exitPressed)
            {
                GetOutOfBed();
            }
            else
            {
                UpdateRestingLook();
            }
            return;
        }

        if (enterPressed && IsPlayerNearBed())
        {
            if (story != null && story.HasInteractionPrompt) return;
            GetIntoBed();
            if (isResting && story != null) story.SleepToNextDay();
        }
    }

    private void GetIntoBed()
    {
        if (itemReticle != null && itemReticle.IsHoldingItem) return;
        FirewoodCollector wood = player.GetComponent<FirewoodCollector>();
        if (wood != null && (wood.IsCarryingWood || wood.IsThrowing)) return;

        savedPlayerPosition = player.position;
        savedPlayerRotation = player.rotation;
        savedCameraLocalPosition = playerCamera.transform.localPosition;
        savedCameraLocalRotation = playerCamera.transform.localRotation;

        movementWasEnabled = movement != null && movement.enabled;
        controllerWasEnabled = characterController != null && characterController.enabled;
        mouseLookWasEnabled = mouseLook != null && mouseLook.enabled;

        Bounds bounds = GetBedBounds();
        if (movement != null) movement.enabled = false;
        if (characterController != null) characterController.enabled = false;
        if (mouseLook != null) mouseLook.enabled = false;

        restingYaw = 0f;
        restingPitch = 0f;
        Vector3 bedForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 viewPosition = bounds.center + bedForward * 0.9f;
        player.position = new Vector3(viewPosition.x, bounds.max.y - 0.35f, viewPosition.z);
        player.rotation = Quaternion.LookRotation(-bedForward, Vector3.up);
        playerCamera.transform.localPosition = new Vector3(0f, cameraHeightAboveBed, 0f);
        playerCamera.transform.localRotation = Quaternion.Euler(restingCameraPitch, 0f, 0f);

        if (movement != null) movement.enabled = false;
        if (characterController != null) characterController.enabled = false;
        if (mouseLook != null) mouseLook.enabled = false;

        isResting = true;
    }

    private void UpdateRestingLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 delta = Vector2.zero;
        float sensitivity = mouseLook != null ? mouseLook.mouseSensitivity : 100f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            delta = Mouse.current.delta.ReadValue() * sensitivity * 0.02f;
#elif ENABLE_LEGACY_INPUT_MANAGER
        delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"))
            * sensitivity * Time.deltaTime;
#endif
        restingYaw = Mathf.Clamp(restingYaw + delta.x, -restingYawLimit, restingYawLimit);
        restingPitch = Mathf.Clamp(restingPitch - delta.y, -restingPitchLimit, restingPitchLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(
            restingCameraPitch + restingPitch, restingYaw, 0f);
    }

    private void GetOutOfBed()
    {
        if (player == null)
        {
            isResting = false;
            return;
        }

        player.position = savedPlayerPosition;
        player.rotation = savedPlayerRotation;
        if (playerCamera != null)
        {
            playerCamera.transform.localPosition = savedCameraLocalPosition;
            playerCamera.transform.localRotation = savedCameraLocalRotation;
        }

        if (characterController != null) characterController.enabled = controllerWasEnabled;
        if (movement != null) movement.enabled = movementWasEnabled;
        if (mouseLook != null) mouseLook.enabled = mouseLookWasEnabled;

        isResting = false;
    }

    private bool IsPlayerNearBed()
    {
        Bounds bounds = GetBedBounds();
        Vector2 playerXZ = new Vector2(player.position.x, player.position.z);
        Vector2 bedXZ = new Vector2(bounds.center.x, bounds.center.z);
        return Vector2.Distance(playerXZ, bedXZ) <= interactionDistance
            && Mathf.Abs(player.position.y - bounds.center.y) <= 2.5f;
    }

    private Bounds GetBedBounds()
    {
        if (bedRenderer != null) return bedRenderer.bounds;
        Collider bedCollider = GetComponent<Collider>();
        if (bedCollider != null) return bedCollider.bounds;
        return new Bounds(transform.position, Vector3.one);
    }

    private void OnGUI()
    {
        if (!isResting && itemReticle != null && itemReticle.IsHoldingItem) return;
        FirewoodCollector wood = player != null ? player.GetComponent<FirewoodCollector>() : null;
        if (!isResting && wood != null && (wood.IsCarryingWood || wood.IsThrowing)) return;

        if (player == null || playerCamera == null || (!isResting && !IsPlayerNearBed()))
        {
            return;
        }

        string message = isResting
            ? "SPACE  -  Rời khỏi giường"
            : "E  -  Nằm nghỉ";
        DailyStoryController story = player.GetComponent<DailyStoryController>();
        if (story != null && story.IsTransitioning) return;
        if (!isResting && story != null && story.HasInteractionPrompt) return;
        if (!isResting && story != null && story.Progress.CanSleepToNextDay)
            message = "E  -  Ngủ sang ngày 2";
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        GUI.Box(new Rect((Screen.width - 300f) * 0.5f, Screen.height - 100f, 300f, 42f), message, style);
    }

    private void OnDisable()
    {
        if (isResting)
        {
            GetOutOfBed();
        }
    }

    private static void ReadInteractionInput(out bool enterPressed, out bool exitPressed)
    {
        enterPressed = false;
        exitPressed = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            enterPressed = keyboard.eKey.wasPressedThisFrame;
            exitPressed = keyboard.spaceKey.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        enterPressed = Input.GetKeyDown(KeyCode.E);
        exitPressed = Input.GetKeyDown(KeyCode.Space);
#endif
    }
}
