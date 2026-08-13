using UnityEngine;
using UnityEngine.UI;

public class MemoCanvas : MonoBehaviour, IEventUI
{
    [SerializeField]
    private Text mainText;

    public void SetText(string str)
    {
        mainText.text = str;
    }
}
