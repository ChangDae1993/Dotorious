using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RopeAction : MonoBehaviour
{
    [Header("Rope Point Detect")]
    public LayerMask ropePointLayer;
    public Collider2D[] ropePoint;
    //public List<Collider2D> ropePoint = new List<Collider2D>();
    public float pointDetectRange;


    [Header("Rope Make")]
    public SpringJoint2D joint;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
       ropePoint =  Physics2D.OverlapCircleAll(this.transform.position, pointDetectRange, ropePointLayer);
        foreach (Collider2D col in ropePoint)
        {
            if (col.gameObject.TryGetComponent(out RopePoint rP))
            {
                rP.RopePosition();
                joint.connectedAnchor = rP.transform.position;
                //Debug.Log("Check");
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(this.transform.position, pointDetectRange);
    }

}
