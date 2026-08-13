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
    
    public void AnchorCheck(bool checkOn)
    {
        if (checkOn)
        {
            radiance.color = Color.red;
        }
        else
        {
            radiance.color = Color.white;
        }
    }

    public void RopePosition(bool onoFF)
    {
        if (onoFF)
        {
            radiance.color = Color.white;
            radiance.gameObject.SetActive(true);
            Debug.Log("RopePointOn");
        }
        else
        {
            radiance.color = Color.white;
            radiance.gameObject.SetActive(false);
            Debug.Log("RopePointOff");
        }
    }
}
