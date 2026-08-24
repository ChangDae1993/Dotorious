using UnityEngine;
using UnityEngine.UI;

public class ScreenFlashCanvas : MonoBehaviour
{
    [SerializeField] private Image flashImage;
    [SerializeField] private CanvasGroup canvasGroup;

    public void SetColor(Color color)
    {
        if (flashImage == null) return;
        flashImage.color = color;
    }

    public void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = Mathf.Clamp01(alpha);
    }

    public void Clear()
    {
        SetAlpha(0f);
    }
}
