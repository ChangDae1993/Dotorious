using UnityEngine;

// Translates movement state into an animation state, and forwards it to an
// Animator if one is assigned. Safe to use with no Animator/Controller yet -
// wire an Animator with "Speed" (float) and "IsGrounded" (bool) parameters
// whenever the Idle/Move/Jump animations exist.
public class PlayerAnimatorBridge : MonoBehaviour
{
    public enum MoveAnimState { Idle, Move, Jump }

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

    [SerializeField] private PlayerMovement movement;
    [SerializeField] private Animator animator;

    public MoveAnimState CurrentState { get; private set; }

    private void Update()
    {
        if (movement == null)
            return;

        CurrentState = !movement.IsGrounded
            ? MoveAnimState.Jump
            : Mathf.Abs(movement.HorizontalVelocity) > 0.05f
                ? MoveAnimState.Move
                : MoveAnimState.Idle;

        if (animator == null)
            return;

        animator.SetFloat(SpeedHash, Mathf.Abs(movement.HorizontalVelocity));
        animator.SetBool(IsGroundedHash, movement.IsGrounded);
    }
}
