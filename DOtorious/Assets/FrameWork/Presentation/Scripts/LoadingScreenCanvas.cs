using UnityEngine;
using UnityEngine.UI;

namespace JYW.Game.EventPlay
{
    /// <summary>Customizable loading prefab. Reference-counted pause also handles simultaneous map moves.</summary>
    public sealed class LoadingScreenCanvas : MonoBehaviour
    {
        public RectTransform spinner;
        public Text tipText;
        [TextArea] public string[] tips = { "【여행의 지혜】\n다음 길에서도 작은 도토리의 모험은 계속됩니다." };
        public float rotationSpeed = 150f;
        private static int pauseOwners;
        private static float previousTimeScale;
        private bool ownsPause;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { pauseOwners = 0; previousTimeScale = 1f; }

        private void OnEnable()
        {
            if (tipText != null && tips != null && tips.Length > 0)
                tipText.text = tips[Random.Range(0, tips.Length)];
            if (pauseOwners++ == 0) previousTimeScale = Time.timeScale;
            ownsPause = true;
            Time.timeScale = 0f;
        }
        private void Update()
        {
            if (spinner != null) spinner.Rotate(0f, 0f, -rotationSpeed * Time.unscaledDeltaTime);
        }
        public void ReleasePause()
        {
            if (!ownsPause) return;
            ownsPause = false;
            pauseOwners = Mathf.Max(0, pauseOwners - 1);
            if (pauseOwners == 0 && Mathf.Approximately(Time.timeScale, 0f))
                Time.timeScale = previousTimeScale;
        }
        private void OnDisable() { ReleasePause(); }
        private void OnDestroy() { ReleasePause(); }
    }
}
