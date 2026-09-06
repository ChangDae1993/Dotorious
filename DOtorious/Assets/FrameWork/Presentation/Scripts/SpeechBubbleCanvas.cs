using UnityEngine;
using UnityEngine.UI;

public sealed class SpeechBubbleCanvas : MonoBehaviour, IEventUI
{
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform bubbleRoot;
    [SerializeField] private RectTransform bodyRect;
    [SerializeField] private RectTransform tailRect;
    [SerializeField] private Text mainText;

    [SerializeField, Min(1f)] private float minWidth = 240f;
    [SerializeField, Min(1f)] private float maxWidth = 760f;
    [SerializeField, Min(1f)] private float minBodyHeight = 96f;
    [SerializeField, Min(0f)] private float horizontalPadding = 72f;
    [SerializeField, Min(0f)] private float verticalPadding = 48f;
    [SerializeField, Min(1f)] private float tailWidth = 48f;
    [SerializeField, Min(1f)] private float tailHeight = 44f;
    [SerializeField, Min(0f)] private float tailBodyOverlap = 12f;
    [SerializeField, Min(0f)] private float targetScreenGap = 6f;
    [SerializeField, Min(0f)] private float screenEdgePadding = 20f;

    public void PrepareLine(string fullText)
    {
        EnsureReferences();
        if (mainText == null || bubbleRoot == null || bodyRect == null || tailRect == null)
            return;

        string previousText = mainText.text;
        HorizontalWrapMode previousHorizontalOverflow = mainText.horizontalOverflow;
        VerticalWrapMode previousVerticalOverflow = mainText.verticalOverflow;
        RectTransform textRect = mainText.rectTransform;

        mainText.text = fullText ?? string.Empty;
        mainText.horizontalOverflow = HorizontalWrapMode.Overflow;
        mainText.verticalOverflow = VerticalWrapMode.Overflow;
        textRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Max(1f, maxWidth - horizontalPadding));
        Canvas.ForceUpdateCanvases();

        float preferredWidth = mainText.preferredWidth;
        float bubbleWidth = Mathf.Clamp(
            preferredWidth + horizontalPadding,
            Mathf.Min(minWidth, maxWidth),
            Mathf.Max(minWidth, maxWidth));
        float textWidth = Mathf.Max(1f, bubbleWidth - horizontalPadding);

        mainText.horizontalOverflow = HorizontalWrapMode.Wrap;
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        Canvas.ForceUpdateCanvases();

        float bodyHeight = Mathf.Max(minBodyHeight, mainText.preferredHeight + verticalPadding);
        float bubbleHeight = bodyHeight + tailHeight - tailBodyOverlap;

        bubbleRoot.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);
        bodyRect.sizeDelta = new Vector2(bubbleWidth, bodyHeight);
        bodyRect.anchoredPosition = new Vector2(0f, tailHeight - tailBodyOverlap);
        tailRect.sizeDelta = new Vector2(tailWidth, tailHeight);
        tailRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(textWidth, Mathf.Max(1f, bodyHeight - verticalPadding));
        textRect.anchoredPosition = Vector2.zero;

        mainText.text = previousText;
        mainText.horizontalOverflow = HorizontalWrapMode.Wrap;
        mainText.verticalOverflow = previousVerticalOverflow;
        if (previousHorizontalOverflow == HorizontalWrapMode.Overflow && string.IsNullOrEmpty(previousText))
            mainText.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    public void SetText(string text)
    {
        EnsureReferences();
        if (mainText != null)
            mainText.text = text ?? string.Empty;
    }

    public void Clear()
    {
        SetText(string.Empty);
    }

    public bool TrySetTargetWorldPosition(Vector3 worldPosition, Camera projectionCamera)
    {
        EnsureReferences();
        if (projectionCamera == null || rootCanvas == null || canvasRect == null || bubbleRoot == null)
            return false;

        Vector3 screenPosition = projectionCamera.WorldToScreenPoint(worldPosition);
        if (screenPosition.z <= 0f)
            return false;

        Camera canvasCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : rootCanvas.worldCamera != null
                ? rootCanvas.worldCamera
                : projectionCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                canvasCamera,
                out Vector2 canvasPosition))
            return false;

        SetTargetCanvasPosition(canvasPosition + Vector2.up * targetScreenGap);
        return true;
    }

    private void SetTargetCanvasPosition(Vector2 desiredPosition)
    {
        Rect canvasBounds = canvasRect.rect;
        Vector2 bubbleSize = bubbleRoot.rect.size;
        Vector2 pivot = bubbleRoot.pivot;

        float minX = canvasBounds.xMin + screenEdgePadding + bubbleSize.x * pivot.x;
        float maxX = canvasBounds.xMax - screenEdgePadding - bubbleSize.x * (1f - pivot.x);
        float minY = canvasBounds.yMin + screenEdgePadding + bubbleSize.y * pivot.y;
        float maxY = canvasBounds.yMax - screenEdgePadding - bubbleSize.y * (1f - pivot.y);

        Vector2 clampedPosition = desiredPosition;
        clampedPosition.x = minX <= maxX
            ? Mathf.Clamp(desiredPosition.x, minX, maxX)
            : (canvasBounds.xMin + canvasBounds.xMax) * 0.5f;
        clampedPosition.y = minY <= maxY
            ? Mathf.Clamp(desiredPosition.y, minY, maxY)
            : (canvasBounds.yMin + canvasBounds.yMax) * 0.5f;
        float canvasScale = rootCanvas != null ? Mathf.Max(0.0001f, rootCanvas.scaleFactor) : 1f;
        clampedPosition.x = Mathf.Round(clampedPosition.x * canvasScale) / canvasScale;
        clampedPosition.y = Mathf.Round(clampedPosition.y * canvasScale) / canvasScale;
        bubbleRoot.anchoredPosition = clampedPosition;

        float halfWidth = bubbleSize.x * 0.5f;
        float tailLimit = Mathf.Max(0f, halfWidth - tailWidth * 0.5f - screenEdgePadding);
        float tailX = Mathf.Clamp(desiredPosition.x - clampedPosition.x, -tailLimit, tailLimit);
        tailX = Mathf.Round(tailX * canvasScale) / canvasScale;
        tailRect.anchoredPosition = new Vector2(tailX, 0f);
    }

    private void EnsureReferences()
    {
        if (rootCanvas == null) rootCanvas = GetComponent<Canvas>();
        if (canvasRect == null && rootCanvas != null) canvasRect = rootCanvas.transform as RectTransform;
        if (mainText == null) mainText = GetComponentInChildren<Text>(true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        minWidth = Mathf.Max(1f, minWidth);
        maxWidth = Mathf.Max(minWidth, maxWidth);
        minBodyHeight = Mathf.Max(1f, minBodyHeight);
        horizontalPadding = Mathf.Max(0f, horizontalPadding);
        verticalPadding = Mathf.Max(0f, verticalPadding);
        tailWidth = Mathf.Max(1f, tailWidth);
        tailHeight = Mathf.Max(1f, tailHeight);
        tailBodyOverlap = Mathf.Clamp(tailBodyOverlap, 0f, tailHeight);
        targetScreenGap = Mathf.Max(0f, targetScreenGap);
        screenEdgePadding = Mathf.Max(0f, screenEdgePadding);
        EnsureReferences();
    }
#endif
}
