using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BGFFAlpha : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sprite;

    public bool alphaOn;

    public float alphaTimer;

    // Start is called before the first frame update
    void Start()
    {
        sprite = GetComponent<SpriteRenderer>();
        alphaTimer = 0f;
    }

    // Update is called once per frame
    void Update()
    {
        if(alphaOn)
        {
            alphaTimer -= 0.1f;

            FadeStart();
        }
        else
        {
            FadeRESET();
        }
    }

    public void FadeStart()
    {
        StartCoroutine(fadeSTartCo());
    }

    public void FadeRESET()
    {
        StartCoroutine(fadeResetCo());
    }

    IEnumerator fadeSTartCo()
    {
        if (sprite != null)
        {
            while (sprite.color.a > 0.5f)
            {
                Debug.Log("FadeOut");
                sprite.color = new Color(
                    sprite.color.r,
                    sprite.color.g,
                    sprite.color.b,
                   sprite.color.a - 0.001f);
                yield return null;
            }
        }
    }

    public void FadeReset()
    {

    }

    IEnumerator fadeResetCo()
    {
        if (sprite != null)
        {
            while (sprite.color.a <1f)
            {
                Debug.Log("FadeOut");
                sprite.color = new Color(
                    sprite.color.r,
                    sprite.color.g,
                    sprite.color.b,
                   sprite.color.a + 0.001f);
                yield return null;
            }
        }
    }
}
