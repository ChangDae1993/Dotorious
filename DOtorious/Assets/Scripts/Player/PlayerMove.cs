using UnityEngine;
using System.Collections;

public class PlayerMove : MonoBehaviour
{
    public int key = 0;

    // 이동 속도
    public float walkSpeed;

    // 대시 속도
    public float dashSpeed;


    // 점프 힘
    public float jumpForce;
    public float doublejumpForce;

    // 더블 점프를 허용하는지
    public bool doublejump;
    public int jumpCnt;

    [SerializeField] private Rigidbody2D rigid;
    private float inputX;
    private bool jumpInput;
    private bool dashInput;

    [Header("Grounding")]
    [SerializeField] private float groundNormalThreshold = 0.45f;

    void Awake()
    {
        rigid = GetComponent<Rigidbody2D>();

        if (rigid == null)
        {
            return;
        }

        // 기존 프리팹 중 일부는 Rigidbody2D 시뮬레이션이 꺼져 있거나 회전이 허용돼 있을 수 있어
        // 이동할 때 넘어지지 않도록 물리 동작을 강제 정리한다.
        rigid.simulated = true;
        rigid.constraints = RigidbodyConstraints2D.FreezeRotation;
        doublejump = false;
        jumpCnt = 0;
    }

    void Update()
    {
        inputX = Input.GetAxisRaw("Horizontal");
        jumpInput = Input.GetKeyDown(KeyCode.Space);
        dashInput = Input.GetKeyDown(KeyCode.LeftShift);
    }

    void FixedUpdate()
    {
        if (rigid == null)
        {
            return;
        }

        PlayerWalkMove(inputX, walkSpeed);
        if (dashInput)
        {
            PlayerDash(dashSpeed);
            dashInput = false;
        }

        if (jumpInput)
        {
            PlayerJump(jumpForce, doublejumpForce);
            jumpInput = false;
        }
    }

    public void PlayerWalkMove(float horizontal, float speed)
    {
        if (horizontal < 0f)
            key = -1;

        if (horizontal > 0f)
            key = 1;

        if (horizontal == 0)
            key = 0;

        // 방향 전환
        if (key == 1)
            this.transform.localScale = new Vector3(1, 1, 1);
        else if (key == -1)
            this.transform.localScale = new Vector3(-1, 1, 1);

        Vector2 p_move = new Vector2(horizontal * speed, rigid.linearVelocity.y);
        rigid.linearVelocity = p_move;
    }

    public void PlayerDash(float dash)
    {
        if (key == 0)
            return;

        rigid.AddForce(new Vector2(dash * key, 0f), ForceMode2D.Impulse);
    }

    public void PlayerJump(float jumpforce, float doublejumpforce)
    {
        if (jumpCnt < (doublejump ? 2 : 1))
        {
            float applyForce = jumpCnt == 0 ? jumpforce : (doublejumpforce > 0f ? doublejumpforce : jumpforce);
            rigid.AddForce(new Vector2(0f, applyForce), ForceMode2D.Impulse);
            jumpCnt++;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsGroundContact(collision))
        {
            //Debug.Log("jumpCnt reset");
            //rigid.velocity = Vector2.zero;
            jumpCnt = 0;
        }
    }

    private bool IsGroundContact(Collision2D collision)
    {
        if (collision == null)
            return false;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);
            if (contact.normal.y >= groundNormalThreshold)
                return true;
        }

        return false;
    }
}
