using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    public PlayerState p_State;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //이동을 Input에서 관리하고 싶다
        //근데 어케함...?

        if (Input.GetAxisRaw("Horizontal") < 0f)
        {

        }
            //key = -1;

        if (Input.GetAxisRaw("Horizontal") > 0f)
        {

        }
            //key = 1;

        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            p_State.p_weaponState = PlayerState.PlayerWeaponState.Sword;
        }
        else if(Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            p_State.p_weaponState = PlayerState.PlayerWeaponState.Rope;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            p_State.p_weaponState = PlayerState.PlayerWeaponState.Chain;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            p_State.p_weaponState = PlayerState.PlayerWeaponState.Bow;
        }
    }
}
