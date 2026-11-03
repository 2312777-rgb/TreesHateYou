using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class PT_PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float sprintSpeed = 7f;

    [Header("Gravity")]
    public float gravity = -20f;

    private CharacterController controller;
    private Vector3 verticalVelocity;
    private int teleportFrame = -1;

    public void ResetAfterTeleport()
    {
        verticalVelocity = Vector3.zero;
        teleportFrame = Time.frameCount;
    }

    private void Awake()
    {
        // The character body stays upright; pitch belongs to the camera only.
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (controller == null || Time.frameCount == teleportFrame)
        {
            return;
        }

        ReadInput(out Vector2 input, out bool sprintHeld);

        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f;
        }

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        float currentSpeed = sprintHeld ? sprintSpeed : speed;
        controller.Move(move * currentSpeed * Time.deltaTime);

        verticalVelocity.y += gravity * Time.deltaTime;
        controller.Move(verticalVelocity * Time.deltaTime);
    }

    private static void ReadInput(out Vector2 movement, out bool sprintHeld)
    {
        movement = Vector2.zero;
        sprintHeld = false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.y += 1f;

        sprintHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        sprintHeld = Input.GetMouseButton(1);
#endif
    }
}

