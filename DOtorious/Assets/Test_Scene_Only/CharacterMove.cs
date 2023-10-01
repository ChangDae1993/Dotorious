using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMove : MonoBehaviour
{
    public float walkSpeed;
    public float dashSpeed;

    public int key = 0;

    public float jumpForce;
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
        PlayerDash();
        PlayerJump();
    }

    public void PlayerMove(float speed)
    {
        if (Input.GetAxisRaw("Horizontal") < 0f)
            key = -1;

        if (0f < Input.GetAxisRaw("Horizontal"))
            key = 1;


        if (key == 1)
            this.transform.localEulerAngles = new Vector3(0, 0, 0);
        else if (key == -1)
            this.transform.localEulerAngles = new Vector3(0, 180, 0);

        Vector2 p_vector = new Vector2(Input.GetAxisRaw("Horizontal"), .0f);
        Vector2 p_move = p_vector * walkSpeed * Time.deltaTime;
        rigid.position += p_move;
    }

    public void PlayerDash()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            rigid.AddForce(new Vector2(dashSpeed * key, 0f), ForceMode2D.Impulse);
        }
    }

    public void PlayerJump()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            if (!doublejump)
            {
                if (jumpCnt < 1)
                {
                    //그라운드에 닿으면 다시 jumpCnt 0으로 초기화
                    rigid.AddForce(new Vector2(0f, jumpForce), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
            else
            {
                if (jumpCnt < 2)
                {
                    //그라운드에 닿으면 다시 jumpCnt 0으로 초기화
                    rigid.AddForce(new Vector2(0f, jumpForce), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if(collision.gameObject.tag.Contains("Ground"))
        {
            Debug.Log("jumpCnt reset");
            jumpCnt = 0;
        }
    }
}
