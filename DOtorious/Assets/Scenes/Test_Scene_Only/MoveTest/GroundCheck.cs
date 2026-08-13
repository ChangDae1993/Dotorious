using UnityEngine;

// Physics-based ground detection, independent of movement/input.
public class GroundCheck : MonoBehaviour
{
    [SerializeField] private Vector2 offset = new Vector2(0f, -0.85f);
    [SerializeField] private Vector2 boxSize = new Vector2(0.5f, 0.1f);
    [SerializeField] private float checkDistance = 0.05f;
    [SerializeField] private LayerMask groundLayer;

    public bool IsGrounded { get; private set; }

    private void FixedUpdate()
    {
        Vector2 origin = (Vector2)transform.position + offset;
        IsGrounded = Physics2D.BoxCast(origin, boxSize, 0f, Vector2.down, checkDistance, groundLayer);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Vector2 origin = (Vector2)transform.position + offset + Vector2.down * checkDistance;
        Gizmos.DrawWireCube(origin, boxSize);
    }
}
