using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RopeAction : MonoBehaviour
{
    [Header("Rope Point Detect")]
    //public float pointDetectRange;
    //public LayerMask ropePointLayer;
    //public Collider2D[] ropePoint;
    public List<RopePoint> ropePointList = new List<RopePoint>();
    public int ropeindex;

    [Space(10f)]
    [Header("Rope Make")]
    public SpringJoint2D joint;
    public Transform ropeShootPoint;
    public bool ropeOnOff;

    // Start is called before the first frame update
    void Start()
    {
        joint.anchor = ropeShootPoint.localPosition;
        //joint.autoConfigureConnectedAnchor = false;
        //joint.autoConfigureDistance = false;
        //joint.distance = f;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            if (ropeindex < ropePointList.Count - 1)
            {
                ropePointList[ropeindex].RopePosition(true);
                ropeindex++;
            }
            else
            {
                ropePointList[ropeindex].RopePosition(true);
                ropeindex = 0;
            }
        }

        //ropePoint = Physics2D.OverlapCircleAll(this.transform.position, pointDetectRange, ropePointLayer);
        //foreach (Collider2D col in ropePoint)
        //{
        //    if (col.gameObject.TryGetComponent(out RopePoint rP))
        //    {
        //        joint.connectedAnchor = rP.transform.position;

        //        if (Input.GetMouseButton(1))
        //        { 
        //            rP.pointOn = true;
        //            if (Input.GetMouseButtonDown(0))
        //            {
        //                if (!ropeOnOff)
        //                {
        //                    ropeOnOff = true;
        //                    Debug.Log("Mouse");
        //                    if (!joint.enabled)
        //                    {
        //                        joint.enabled = true;
        //                    }
        //                }
        //                else
        //                {
        //                    ropeOnOff = false;
        //                    if (joint.enabled)
        //                    {
        //                        joint.enabled = false;
        //                    }
        //                }

        //            }
        //        }
        //        else
        //        {
        //            rP.pointOn = false;
        //        }
        //        //else
        //        //{
        //        //    if (joint.enabled)
        //        //    {
        //        //        joint.enabled = false;
        //        //    }
        //        //}

        //        //Debug.Log("Check");
        //    }

        //    //if(Input.GetMouseButtonDown(0))
        //    //{
        //    //    Debug.Log("Mouse");
        //    //}
        //}

    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.TryGetComponent(out RopePoint rp))
        {
            ropePointList.Add(rp);
        }
    }


    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.TryGetComponent(out RopePoint rp))
        {
            if (ropePointList.Count <= 1)
            {
                ropeindex = 0;
            }

            ropePointList[ropeindex].RopePosition(false);
            ropePointList.Remove(rp);
            //rp.RopePosition(false);
        }
    }

    //private void OnDrawGizmos()
    //{
    //    Gizmos.color = Color.red;
    //    Gizmos.DrawWireSphere(this.transform.position, pointDetectRange);
    //}

}
