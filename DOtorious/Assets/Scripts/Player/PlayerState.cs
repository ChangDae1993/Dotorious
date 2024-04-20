using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerState : MonoBehaviour
{
    public enum PlayerMoveState
    {
        Idle,
        Move,
        Dash,
        Jump,
    }

    public enum PlayerWeaponState
    {
        Sword,
        Rope,
        Chain,
        Bow,
    }

    public PlayerMoveState p_moveState;
    public PlayerWeaponState p_weaponState;

    // Start is called before the first frame update
    void Start()
    {
        p_moveState = PlayerMoveState.Idle;
        p_weaponState = PlayerWeaponState.Sword;
    }

    // Update is called once per frame
    //void Update()
    //{
        
    //}
}
