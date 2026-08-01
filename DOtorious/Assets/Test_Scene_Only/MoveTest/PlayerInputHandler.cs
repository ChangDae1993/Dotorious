using UnityEngine;
using UnityEngine.InputSystem;

// Reads raw input via the Input System and exposes it as plain data.
// Knows nothing about physics or movement.
public class PlayerInputHandler : MonoBehaviour
{
    public float MoveInput { get; private set; }
    public bool JumpPressed { get; private set; }

    private InputAction moveAction;
    private InputAction jumpAction;

    private void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Axis");
        moveAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/d")
            .With("Positive", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick/x");

        jumpAction = new InputAction("Jump", InputActionType.Button, binding: "<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        jumpAction.performed += OnJumpPerformed;
    }

    private void OnDisable()
    {
        jumpAction.performed -= OnJumpPerformed;
        moveAction.Disable();
        jumpAction.Disable();
    }

    private void OnDestroy()
    {
        moveAction.Dispose();
        jumpAction.Dispose();
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        JumpPressed = true;
    }

    private void Update()
    {
        MoveInput = moveAction.ReadValue<float>();
    }

    // Movement logic calls this once it has consumed the jump press,
    // so a single key press never triggers two jumps.
    public void ConsumeJump()
    {
        JumpPressed = false;
    }
}
