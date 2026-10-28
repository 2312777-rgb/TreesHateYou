using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class LumberjackDoorInteract : MonoBehaviour
{
    [SerializeField] private float interactionDistance = 2.5f;
    [SerializeField] private float openAngle = 95f;
    [SerializeField] private float openSpeed = 180f;
    [SerializeField] private bool hingeAtLocalMinX = true;

    private Transform player;
    private Vector3 hingePoint;
    private float currentAngle;
    private float targetAngle;
    public bool IsInteractionPromptVisible
    {
        get
        {
            if (!isActiveAndEnabled || player == null ||
                Vector3.Distance(player.position, hingePoint) > interactionDistance) return false;
            DailyStoryController story = player.GetComponent<DailyStoryController>();
            return story == null || (!story.IsTransitioning && !story.HasInteractionPrompt);
        }
    }

    private void Start()
    {
        GameObject playerObject = GameObject.Find("Player");
        if (playerObject != null)
            player = playerObject.transform;

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        Bounds bounds = meshFilter != null && meshFilter.sharedMesh != null
            ? meshFilter.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);

        float hingeX = hingeAtLocalMinX ? bounds.min.x : bounds.max.x;
        hingePoint = transform.TransformPoint(new Vector3(hingeX, bounds.center.y, bounds.center.z));
    }

    private void Update()
    {
        if (player == null)
            return;

        bool inRange = IsInteractionPromptVisible;
        if (inRange && InteractPressed())
            targetAngle = Mathf.Approximately(targetAngle, 0f) ? openAngle : 0f;

        float nextAngle = Mathf.MoveTowards(currentAngle, targetAngle, openSpeed * Time.deltaTime);
        float deltaAngle = nextAngle - currentAngle;
        if (!Mathf.Approximately(deltaAngle, 0f))
        {
            transform.RotateAround(hingePoint, Vector3.up, deltaAngle);
            currentAngle = nextAngle;
        }
    }

    private void OnGUI()
    {
        if (!IsInteractionPromptVisible)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 110f, Screen.height - 90f, 220f, 40f), "Nhấn E để mở / đóng cửa", style);
    }

    private static bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.E))
            return true;
#endif
        return false;
    }
}
