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
    // Start is called before the first frame update
    void Start()
    {
        joint.anchor = ropeShootPoint.position;
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

                if (Input.GetKeyDown(KeyCode.LeftControl))
                {
                    joint.connectedAnchor = rP.transform.position;
                }
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
