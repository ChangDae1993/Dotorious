using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMove : MonoBehaviour
{
    public float xInput;
    public float walkSpeed;

    [SerializeField] private Rigidbody2D rigid;
    // Start is called before the first frame update
    void Start()
    {
        walkSpeed = 0.1f;
        rigid = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        PlayerMove(walkSpeed);
    }

    public void PlayerMove(float speed)
    {
        xInput = Input.GetAxisRaw("Horizontal");
        if (Input.GetAxisRaw("Horizontal") != 0f)
        {
            this.transform.Translate(new Vector2(xInput * speed, 0));
        }
        else
        {
            speed = 0f;
            this.transform.Translate(new Vector2(speed, 0));
        }
    }
}
