using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BGFFAlpha : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sprite;
    // Start is called before the first frame update
    void Start()
    {
        sprite = GetComponent<SpriteRenderer>();
    }

    // Update is called once per frame
    //void Update()
    //{
        
    //}

    public void FadeStart()
    {
        StartCoroutine(fadeSTartCo());

    }

    IEnumerator fadeSTartCo()
    {
        if(sprite != null)
        {
            while(sprite.color.a > 125f)
            {
                Debug.Log("FadeOut");
                sprite.color = new Color(
                    sprite.color.r,
                    sprite.color.g,
                    sprite.color.b,
                    sprite.color.a - 0.1f);
            }
            yield return null;
        }
    }
}
