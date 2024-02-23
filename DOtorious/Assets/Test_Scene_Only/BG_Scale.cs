using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BG_Scale : MonoBehaviour
{
    public enum Layer
    {
        Back_Back,
        Back_Middle,
        Back_Front,
        Middle_Back,
        Middle_Middle,
        Middle_Front,
        Front_Back,
        Front_Middle,
        Front_Front,
    }

    public Layer bgLayer;

    public BG_MGR bgManager;

    // Start is called before the first frame update
    void Start()
    {
        bgManager = GetComponentInParent<BG_MGR>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
