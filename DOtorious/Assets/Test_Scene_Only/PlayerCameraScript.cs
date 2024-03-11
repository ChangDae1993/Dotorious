using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerCameraScript : MonoBehaviour
{
    public GameObject Player;

    public RaycastHit[] hits;
    public Vector3 direction;
    public List<GameObject> transparentOBJ = new List<GameObject>();
    // Start is called before the first frame update
    void Start()
    {
        Player = GameObject.FindWithTag("Player");
        //this.transform.SetParent(Player.transform);
        //this.transform.localPosition = new Vector3(0, 0, this.transform.localPosition.z);
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position = new Vector3(
            Player.transform.position.x, 
            Player.transform.position.y, 
            this.transform.position.z);

        direction = (Player.transform.position - this.transform.position).normalized;
        hits = Physics.RaycastAll(this.transform.position, direction, Mathf.Infinity,
            1 << LayerMask.NameToLayer("BG_Front_Front"));

        if (hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.TryGetComponent(out BGFFAlpha alpha))
                {
                    if (!transparentOBJ.Contains(hits[i].transform.gameObject))
                    {
                        transparentOBJ.Add(hits[i].transform.gameObject);
                    }
                    alpha.alphaOn = true;
                }
            }
        }
        else
        {
            if (transparentOBJ.Count > 0)
            {
                for (int i = 0; i < transparentOBJ.Count; i++)
                {
                    if (transparentOBJ[i].transform.TryGetComponent(out BGFFAlpha alpha))
                    {
                        alpha.alphaOn = false;
                        transparentOBJ.Remove(transparentOBJ[i].gameObject);
                    }
                }
            }
        }
    }
}
