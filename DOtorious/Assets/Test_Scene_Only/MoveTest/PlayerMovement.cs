using UnityEngine;

// Applies input data as actual physics movement. Owns no input reading.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInputHandler))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 60f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 12f;

    [Header("References")]
    [SerializeField] private GroundCheck groundCheck;

    private Rigidbody2D rb;
    private PlayerInputHandler input;

    public bool IsGrounded => groundCheck != null && groundCheck.IsGrounded;
    public float HorizontalVelocity => rb.velocity.x;
    public bool FacingRight { get; private set; } = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        input = GetComponent<PlayerInputHandler>();
    }

    private void FixedUpdate()
    {
        ApplyHorizontalMove();
        UpdateFacing();
        HandleJump();
    }

    private void ApplyHorizontalMove()
    {
        float targetSpeed = input.MoveInput * moveSpeed;
        float speedDiff = targetSpeed - rb.velocity.x;
        float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
        float movement = speedDiff * rate * Time.fixedDeltaTime;
        rb.velocity = new Vector2(rb.velocity.x + movement, rb.velocity.y);
    }

    private void UpdateFacing()
    {
        if (input.MoveInput > 0.01f)
            FacingRight = true;
        else if (input.MoveInput < -0.01f)
            FacingRight = false;

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (FacingRight ? 1f : -1f);
        transform.localScale = scale;
    }

    private void HandleJump()
    {
        if (!input.JumpPressed)
            return;

        if (IsGrounded)
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        input.ConsumeJump();
    }
}
