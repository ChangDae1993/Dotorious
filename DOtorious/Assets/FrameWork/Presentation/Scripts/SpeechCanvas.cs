using UnityEngine;
using UnityEngine.UI;

public class SpeechCanvas : MonoBehaviour, IEventUI
{
    [SerializeField]
    private Text mainText;

    public void SetText(string str)
    {
        if (mainText != null)
        mainText.text = str;
    }
}

