using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMove : MonoBehaviour
{
    public float walkSpeed;
    public float dashSpeed;

    public int key = 0;

    [SerializeField] private Rigidbody2D rigid;


    // Start is called before the first frame update
    void Start()
    {
        rigid = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        PlayerMove(walkSpeed);
        PlayerDash();
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
}
