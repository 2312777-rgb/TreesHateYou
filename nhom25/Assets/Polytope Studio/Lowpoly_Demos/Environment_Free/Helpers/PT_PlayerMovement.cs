using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class PT_PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float sprintSpeed = 8f;

    [Header("Jump and gravity")]
    public float gravity = -20f;
    public float jumpHeight = 1.5f;

    private CharacterController controller;
    private Vector3 verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (controller == null)
        {
            return;
        }

        ReadInput(out Vector2 input, out bool jumpPressed, out bool sprintHeld);

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

        if (jumpPressed && isGrounded)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity.y += gravity * Time.deltaTime;
        controller.Move(verticalVelocity * Time.deltaTime);
    }

    private static void ReadInput(out Vector2 movement, out bool jumpPressed, out bool sprintHeld)
    {
        movement = Vector2.zero;
        jumpPressed = false;
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

        jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        sprintHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        jumpPressed = Input.GetButtonDown("Jump");
        sprintHeld = Input.GetMouseButton(1);
#endif
    }
}

