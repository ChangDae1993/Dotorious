using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMove : MonoBehaviour
{
    public int key = 0;

    //일반 이동 속도
    public float walkSpeed;

    //대쉬 속도
    public float dashSpeed;


    //점프력
    public float jumpForce;

    //이중 점프가 가능하도록 하는 bool값
    public bool doublejump;
    public int jumpCnt;

    [SerializeField] private Rigidbody2D rigid;


    // Start is called before the first frame update
    void Start()
    {
        rigid = GetComponent<Rigidbody2D>();

        doublejump = false;
        jumpCnt = 0;
    }

    // Update is called once per frame
    void Update()
    {
        PlayerMove(walkSpeed);
        PlayerDash(dashSpeed);
        PlayerJump(jumpForce);
    }

    public void PlayerMove(float speed)
    {
        if (Input.GetAxisRaw("Horizontal") < 0f)
            key = -1;

        if (Input.GetAxisRaw("Horizontal") > 0f)
            key = 1;


        if (key == 1)
            this.transform.localScale = new Vector3(1, 1, 1);
        else if (key == -1)
            this.transform.localScale = new Vector3(-1, 1, 1);

        Vector2 p_vector = new Vector2(Input.GetAxisRaw("Horizontal"), .0f);
        Vector2 p_move = p_vector * walkSpeed * Time.deltaTime;
        rigid.position += p_move;
    }

    public void PlayerDash(float dash)
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            rigid.AddForce(new Vector2(dash * key, 0f), ForceMode2D.Impulse);
        }
    }

    public void PlayerJump(float jump)
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!doublejump)
            {
                if (jumpCnt < 1)
                {
                    //그라운드에 닿으면 다시 jumpCnt 0으로 초기화
                    rigid.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
            else
            {
                if (jumpCnt < 2)
                {
                    //그라운드에 닿으면 다시 jumpCnt 0으로 초기화
                    rigid.AddForce(new Vector2(0f, jump), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.tag.Contains("Ground"))
        {
            Debug.Log("jumpCnt reset");
            //rigid.velocity = Vector2.zero;
            jumpCnt = 0;
        }
    }
}
