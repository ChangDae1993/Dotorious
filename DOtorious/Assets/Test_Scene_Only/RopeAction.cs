using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RopeAction : MonoBehaviour
{
    [Header("Rope Point Detect")]
    public float pointDetectRange;
    public LayerMask ropePointLayer;
    public Collider2D[] ropePoint;
    //public List<Collider2D> ropePoint = new List<Collider2D>();

    [Space(10f)]
    [Header("Rope Make")]
    public SpringJoint2D joint;
    public Transform ropeShootPoint;
    public bool ropeOnOff;

    // Start is called before the first frame update
    void Start()
    {
        joint.anchor = ropeShootPoint.position;
        //joint.autoConfigureConnectedAnchor = false;
        //joint.autoConfigureDistance = false;
        //joint.distance = f;
    }

    // Update is called once per frame
    void Update()
    {
        ropePoint = Physics2D.OverlapCircleAll(this.transform.position, pointDetectRange, ropePointLayer);
        foreach (Collider2D col in ropePoint)
        {
            if (col.gameObject.TryGetComponent(out RopePoint rP))
            {
                joint.connectedAnchor = rP.transform.position;

                if (Input.GetKey(KeyCode.LeftControl))
                {
                    rP.RopePosition();

                    if (Input.GetMouseButtonDown(0))
                    {


                        if (!ropeOnOff)
                        {
                            ropeOnOff = true;
                            Debug.Log("Mouse");
                            if (!joint.enabled)
                            {
                                joint.enabled = true;
                            }
                        }
                        else
                        {
                            ropeOnOff = false;
                            if (joint.enabled)
                            {
                                joint.enabled = false;
                            }
                        }

                    }
                }
                //else
                //{
                //    if (joint.enabled)
                //    {
                //        joint.enabled = false;
                //    }
                //}

                //Debug.Log("Check");
            }

            //if(Input.GetMouseButtonDown(0))
            //{
            //    Debug.Log("Mouse");
            //}
        }

    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(this.transform.position, pointDetectRange);
    }

}
