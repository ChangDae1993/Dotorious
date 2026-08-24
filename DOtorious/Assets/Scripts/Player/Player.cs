using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class Player : MonoBehaviour
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int WalkHash = Animator.StringToHash("Walk");
    private static readonly int RunHash = Animator.StringToHash("Run");
    private static readonly int JumpHash = Animator.StringToHash("Jump");

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float runSpeed = 8f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpSpeed = 9f;
    [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float groundNormalThreshold = 0.55f;

    [Header("References")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private readonly HashSet<Collider2D> groundContacts = new HashSet<Collider2D>();

    private float moveInput;
    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;
    private bool runHeld;
    private bool jumpHeldLastFrame;
    private bool jumpConsumed;
    private bool hasIdleParameter;
    private bool hasWalkParameter;
    private bool hasRunParameter;
    private bool hasJumpParameter;

    public bool IsGrounded => groundContacts.Count > 0;

    private void Awake()
    {
        if (body == null)
            body = GetComponent<Rigidbody2D>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        body.simulated = true;
        body.constraints |= RigidbodyConstraints2D.FreezeRotation;

        if (animator != null)
        {
            animator.applyRootMotion = false;
            CacheAnimatorParameters();
        }
    }

    private void Update()
    {
        ReadInput();
        UpdateFacing();
    }

    private void FixedUpdate()
    {
        bool grounded = IsGrounded;
        if (grounded && body.linearVelocity.y <= 0.05f)
        {
            lastGroundedTime = Time.time;
            jumpConsumed = false;
        }

        float speed = runHeld ? runSpeed : moveSpeed;
        body.linearVelocity = new Vector2(moveInput * speed, body.linearVelocity.y);

        bool jumpBuffered = Time.time <= lastJumpPressedTime + jumpBufferTime;
        bool canUseGroundJump = Time.time <= lastGroundedTime + coyoteTime;
        if (jumpBuffered && canUseGroundJump && !jumpConsumed)
            Jump();
    }

    private void LateUpdate()
    {
        UpdateAnimator();
    }

    private void ReadInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            moveInput = (right ? 1f : 0f) - (left ? 1f : 0f);
            runHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            bool jumpHeld = keyboard.spaceKey.isPressed;
            if (jumpHeld && !jumpHeldLastFrame)
                lastJumpPressedTime = Time.time;
            jumpHeldLastFrame = jumpHeld;

            return;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        moveInput = Input.GetAxisRaw("Horizontal");
        runHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = Time.time;
        jumpHeldLastFrame = Input.GetKey(KeyCode.Space);
#else
        moveInput = 0f;
        runHeld = false;
        jumpHeldLastFrame = false;
#endif
    }

    private void Jump()
    {
        body.linearVelocity = new Vector2(body.linearVelocity.x, jumpSpeed);
        lastJumpPressedTime = float.NegativeInfinity;
        jumpConsumed = true;
        groundContacts.Clear();
    }

    private void UpdateFacing()
    {
        if (spriteRenderer == null || Mathf.Abs(moveInput) < 0.01f)
            return;

        // 루트 Transform의 스케일은 씬 콜라이더 보정값과 연결되어 있으므로 건드리지 않는다.
        spriteRenderer.flipX = moveInput < 0f;
    }

    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        bool grounded = IsGrounded;
        bool moving = Mathf.Abs(moveInput) > 0.01f;
        bool running = grounded && moving && runHeld;
        bool walking = grounded && moving && !runHeld;

        if (hasIdleParameter)
            animator.SetBool(IdleHash, grounded && !moving);
        if (hasWalkParameter)
            animator.SetBool(WalkHash, walking);
        if (hasRunParameter)
            animator.SetBool(RunHash, running);
        if (hasJumpParameter)
            animator.SetBool(JumpHash, !grounded);
    }

    private void CacheAnimatorParameters()
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Bool)
                continue;

            if (parameter.nameHash == IdleHash)
                hasIdleParameter = true;
            else if (parameter.nameHash == WalkHash)
                hasWalkParameter = true;
            else if (parameter.nameHash == RunHash)
                hasRunParameter = true;
            else if (parameter.nameHash == JumpHash)
                hasJumpParameter = true;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision != null && collision.collider != null)
            groundContacts.Remove(collision.collider);
    }

    private void UpdateGroundContact(Collision2D collision)
    {
        if (collision == null || collision.collider == null)
            return;

        bool hasGroundNormal = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y >= groundNormalThreshold)
            {
                hasGroundNormal = true;
                break;
            }
        }

        if (hasGroundNormal)
            groundContacts.Add(collision.collider);
        else
            groundContacts.Remove(collision.collider);
    }

    private void OnDisable()
    {
        groundContacts.Clear();
        jumpHeldLastFrame = false;
    }
}
