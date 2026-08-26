using UnityEngine;
using UnityEngine.UI;

public class PortraitSpeechCanvas : MonoBehaviour, IEventUI
{
    [SerializeField] private Image portraitImage;
    [SerializeField] private Text speakerText;
    [SerializeField] private Text mainText;

    public void SetLine(Sprite portrait, string speakerName)
    {
        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (speakerText != null)
        {
            string safeSpeakerName = speakerName ?? string.Empty;
            speakerText.text = safeSpeakerName;
            speakerText.gameObject.SetActive(!string.IsNullOrWhiteSpace(safeSpeakerName));
        }
    }

    public void SetText(string str)
    {
        if (mainText != null)
            mainText.text = str ?? string.Empty;
    }

    public void Clear()
    {
        SetLine(null, string.Empty);
        SetText(string.Empty);
    }
}
