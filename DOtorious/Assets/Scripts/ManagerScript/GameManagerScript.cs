using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManagerScript : MonoBehaviour
{
    public static GameManagerScript GM;

    private void Awake()
    {
        if (SceneManager.sceneCount > 0)
        {
            if (GM == null)
            {
                DontDestroyOnLoad(this.gameObject);
                GM = this;
            }
        }
        else
        {
            Destroy(GM);
        }
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
