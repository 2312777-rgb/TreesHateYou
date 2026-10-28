using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mountable axe: E to pick up nearby, right-click to throw, and right-click
/// near the thrown axe to return it to its original wall mount.
/// </summary>
public class AxePickupThrow : MonoBehaviour
{
    private enum AxeState { Mounted, Held, Thrown }

    [Header("Interaction")]
    [SerializeField] private float interactionDistance = 2.0f;
    [SerializeField] private float throwSpeed = 10.0f;
    [SerializeField] private float throwUpwardForce = 1.4f;
    [SerializeField] private float throwTorque = 2.5f;

    [Header("Held pose (camera local space)")]
    [SerializeField] private Vector3 heldLocalPosition = new Vector3(0.38f, -0.32f, 0.75f);
    [SerializeField] private Vector3 heldLocalEuler = new Vector3(0f, 0f, 45f);
    [SerializeField] private float heldScale = 0.45f;

    private Transform player;
    private Camera playerCamera;
    private Transform homeParent;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 homeScale;
    private Rigidbody body;
    private Collider axeCollider;
    private AxeState state = AxeState.Mounted;

    private void Awake()
    {
        homeParent = transform.parent;
        homePosition = transform.position;
        homeRotation = transform.rotation;
        homeScale = transform.localScale;

        body = GetComponent<Rigidbody>();
        axeCollider = GetComponent<Collider>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        GameObject playerObject = GameObject.Find("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerCamera = playerObject.GetComponentInChildren<Camera>();
        }
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Update()
    {
        if (playerCamera == null)
            return;

        bool pressedE = EPressedThisFrame();
        bool pressedRightMouse = RightMousePressedThisFrame();
        bool nearAxe = player != null &&
                       Vector3.Distance(player.position, transform.position) <= interactionDistance;

        if (state == AxeState.Mounted && nearAxe && pressedE)
            Equip();

        if (state == AxeState.Held && pressedRightMouse)
            Throw();

        if (state == AxeState.Thrown && nearAxe && pressedRightMouse)
            ReturnToMount();
    }

    private void Equip()
    {
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }
        if (axeCollider != null)
            axeCollider.enabled = false;

        transform.SetParent(playerCamera.transform, false);
        transform.localPosition = heldLocalPosition;
        transform.localRotation = Quaternion.Euler(heldLocalEuler);
        transform.localScale = Vector3.one * heldScale;
        state = AxeState.Held;
    }

    private void Throw()
    {
        Vector3 direction = playerCamera.transform.forward;
        Quaternion throwRotation = transform.rotation;

        transform.SetParent(null, true);
        transform.position = playerCamera.transform.position + direction * 1.0f;
        transform.rotation = throwRotation;
        transform.localScale = homeScale;

        if (axeCollider != null)
            axeCollider.enabled = true;
        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * throwSpeed + Vector3.up * throwUpwardForce, ForceMode.VelocityChange);
            body.AddTorque(playerCamera.transform.right * throwTorque, ForceMode.VelocityChange);
        }
        state = AxeState.Thrown;
    }

    private void ReturnToMount()
    {
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }
        transform.SetParent(homeParent, true);
        transform.position = homePosition;
        transform.rotation = homeRotation;
        transform.localScale = homeScale;
        if (axeCollider != null)
            axeCollider.enabled = true;
        state = AxeState.Mounted;
    }

    private bool EPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.E);
#else
        return false;
#endif
    }

    private bool RightMousePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(1);
#else
        return false;
#endif
    }

    private void OnGUI()
    {
        string prompt = null;
        bool nearAxe = player != null &&
                       Vector3.Distance(player.position, transform.position) <= interactionDistance;

        if (state == AxeState.Mounted && nearAxe)
            prompt = "E - Trang bị rìu";
        else if (state == AxeState.Held)
            prompt = "Chuột phải - Ném rìu";
        else if (state == AxeState.Thrown && nearAxe)
            prompt = "Chuột phải - Treo rìu lại";

        if (!string.IsNullOrEmpty(prompt))
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 18;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height - 90f, 300f, 36f), prompt, style);
        }
    }
}