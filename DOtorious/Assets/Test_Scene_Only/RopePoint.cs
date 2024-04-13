using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RopePoint : MonoBehaviour
{
    public bool pointOn;

    public SpriteRenderer radiance;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    //void Update()
    //{

    //}

    public void RopePosition(bool onoFF)
    {
        if (onoFF)
        {
            radiance.gameObject.SetActive(true);
            Debug.Log("RopePointOn");
        }
        else
        {
            radiance.gameObject.SetActive(false);
            Debug.Log("RopePointOff");
        }
    }
}
