using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    public int key = 0;

    //�Ϲ� �̵� �ӵ�
    public float walkSpeed;

    //�뽬 �ӵ�
    public float dashSpeed;


    //������
    public float jumpForce;
    public float doublejumpForce;

    //���� ������ �����ϵ��� �ϴ� bool��
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
        PlayerWalkMove(walkSpeed);
        PlayerDash(dashSpeed);
        PlayerJump(jumpForce, doublejumpForce);
    }

    public void PlayerWalkMove(float speed)
    {
        if (Input.GetAxisRaw("Horizontal") < 0f)
            key = -1;

        if (Input.GetAxisRaw("Horizontal") > 0f)
            key = 1;

        //�¿� �̵�
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

    public void PlayerJump(float jumpforce , float doublejumpforce)
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!doublejump)
            {
                if (jumpCnt < 1)
                {
                    //�׶��忡 ������ �ٽ� jumpCnt 0���� �ʱ�ȭ
                    rigid.AddForce(new Vector2(0f, jumpforce), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
            else
            {
                if (jumpCnt < 2)
                {
                    //�׶��忡 ������ �ٽ� jumpCnt 0���� �ʱ�ȭ
                    rigid.AddForce(new Vector2(0f, doublejumpforce), ForceMode2D.Impulse);
                    jumpCnt++;
                }
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.tag.Contains("Ground"))
        {
            //Debug.Log("jumpCnt reset");
            //rigid.velocity = Vector2.zero;
            jumpCnt = 0;
        }
    }
}