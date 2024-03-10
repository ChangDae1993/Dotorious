using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCameraScript : MonoBehaviour
{
    public GameObject Player;

    // Start is called before the first frame update
    void Start()
    {
        Player = GameObject.FindWithTag("Player");
        this.transform.SetParent(Player.transform);
        this.transform.localPosition = new Vector3(0, 0, this.transform.localPosition.z);
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 direction = (Player.transform.position - this.transform.position).normalized;
        RaycastHit[] hits = Physics.RaycastAll(this.transform.position, direction, Mathf.Infinity,
            1 << LayerMask.NameToLayer("BG_Front_Front"));

        for(int i = 0; i < hits.Length; i++)
        {
            Debug.Log("Cast");
        }
    }
}
