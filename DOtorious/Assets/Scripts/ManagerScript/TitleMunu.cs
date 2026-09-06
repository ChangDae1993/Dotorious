using System.Collections;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dotorious.UI
{
    [DisallowMultipleComponent]
    public sealed class TitleMunu : MonoBehaviour
    {
        private const string MasterVolumeKey = "Dotorious.MasterVolume";

        [Header("Start Presentation")]
        [SerializeField] private EventSO startEvent;
        [SerializeField] private float managerAcquireTimeout = 30f;

        [Header("Title Menu")]
        [SerializeField] private GameObject titleRoot;
        [SerializeField] private CanvasGroup titleCanvasGroup;
        [SerializeField] private GameObject menuButtons;
        [SerializeField] private GameObject optionPanel;
        [SerializeField] private Button startButton;
        [SerializeField] private Button optionButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private Button optionBackButton;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Toggle fullscreenToggle;

        [Header("Gameplay Lock")]
        [SerializeField] private ObjectPlayerController2D playerController;
        [SerializeField] private Rigidbody2D playerBody;

        private EventPlayManager eventManager;
        private Coroutine startRoutine;
        private bool playerControllerWasEnabled;
        private bool hasStarted;
        private bool introFinished;

        public bool HasStarted => hasStarted;
        public bool IsManagerReady => eventManager != null &&
                                      eventManager.isActiveAndEnabled &&
                                      eventManager.gameObject.activeInHierarchy;
        public EventSO StartEvent => startEvent;

        private void Awake()
        {
            if (titleRoot == null)
            {
                Canvas parentCanvas = GetComponentInParent<Canvas>();
                titleRoot = parentCanvas != null ? parentCanvas.gameObject : gameObject;
            }

            if (titleCanvasGroup == null && titleRoot != null)
                titleCanvasGroup = titleRoot.GetComponent<CanvasGroup>();

            if (playerController != null && playerBody == null)
                playerBody = playerController.GetComponent<Rigidbody2D>();

            PrepareTitleState();
            LockGameplayInput();
            WireUiEvents();
            StartCoroutine(AcquireEventManager());
        }

        private void OnDestroy()
        {
            UnwireUiEvents();
            if (!introFinished)
                RestoreGameplayInput();
        }

        private void OnDisable()
        {
            if (!introFinished)
                RestoreGameplayInput();
        }

        private void PrepareTitleState()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (titleCanvasGroup != null)
            {
                titleCanvasGroup.alpha = 1f;
                titleCanvasGroup.interactable = true;
                titleCanvasGroup.blocksRaycasts = true;
            }

            if (menuButtons != null) menuButtons.SetActive(true);
            if (optionPanel != null) optionPanel.SetActive(false);
            if (startButton != null) startButton.interactable = false;

            float savedVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
            AudioListener.volume = savedVolume;
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(savedVolume);
            if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        }

        private void WireUiEvents()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (optionButton != null) optionButton.onClick.AddListener(OpenOption);
            if (exitButton != null) exitButton.onClick.AddListener(ExitGame);
            if (optionBackButton != null) optionBackButton.onClick.AddListener(CloseOption);
            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(SetMasterVolume);
            if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }

        private void UnwireUiEvents()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartGame);
            if (optionButton != null) optionButton.onClick.RemoveListener(OpenOption);
            if (exitButton != null) exitButton.onClick.RemoveListener(ExitGame);
            if (optionBackButton != null) optionBackButton.onClick.RemoveListener(CloseOption);
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(SetMasterVolume);
            if (fullscreenToggle != null) fullscreenToggle.onValueChanged.RemoveListener(SetFullscreen);
        }

        private IEnumerator AcquireEventManager()
        {
            float startedAt = Time.realtimeSinceStartup;
            while (!IsManagerReady)
            {
                eventManager = EventPlayManager.Instance;
                if (IsManagerReady) break;

                if (managerAcquireTimeout > 0f &&
                    Time.realtimeSinceStartup - startedAt >= managerAcquireTimeout)
                {
                    Debug.LogError("[TitleMunu] Presentation EventPlayManager를 준비하지 못해 Start를 활성화할 수 없습니다.");
                    yield break;
                }

                yield return null;
            }

            if (!hasStarted && startButton != null)
                startButton.interactable = true;

            Select(startButton);
        }

        public void StartGame()
        {
            if (hasStarted) return;
            hasStarted = true;

            if (titleCanvasGroup != null)
            {
                titleCanvasGroup.interactable = false;
                titleCanvasGroup.blocksRaycasts = false;
            }

            if (startButton != null) startButton.interactable = false;
            if (optionButton != null) optionButton.interactable = false;
            if (exitButton != null) exitButton.interactable = false;
            if (optionPanel != null) optionPanel.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            if (startRoutine != null) StopCoroutine(startRoutine);
            startRoutine = StartCoroutine(StartPresentationWhenReady());
        }

        private IEnumerator StartPresentationWhenReady()
        {
            float startedAt = Time.realtimeSinceStartup;
            while (!IsManagerReady)
            {
                eventManager = EventPlayManager.Instance;
                if (IsManagerReady) break;

                if (managerAcquireTimeout > 0f &&
                    Time.realtimeSinceStartup - startedAt >= managerAcquireTimeout)
                {
                    RestoreMenuAfterStartFailure("Presentation EventPlayManager를 준비하지 못했습니다.");
                    yield break;
                }

                yield return null;
            }

            if (startEvent == null)
            {
                RestoreMenuAfterStartFailure("Start EventSO가 연결되지 않았습니다.");
                yield break;
            }

            Time.timeScale = 1f;
            eventManager.PlayEvent(startEvent, titleRoot != null ? titleRoot : gameObject);

            yield return null;
            float startDeadline = Time.realtimeSinceStartup + 2f;
            while (IsManagerReady && !eventManager.IsEventRunning &&
                   Time.realtimeSinceStartup < startDeadline)
                yield return null;

            if (!IsManagerReady || !eventManager.IsEventRunning)
            {
                RestoreMenuAfterStartFailure("Start 연출이 시작되지 않았습니다.");
                yield break;
            }

            while (IsManagerReady && eventManager.IsEventRunning)
                yield return null;

            if (!IsManagerReady)
            {
                RestoreMenuAfterStartFailure("Start 연출 도중 Presentation EventPlayManager가 종료되었습니다.");
                yield break;
            }

            FinishIntro();
        }

        private void FinishIntro()
        {
            introFinished = true;
            startRoutine = null;
            if (titleCanvasGroup != null) titleCanvasGroup.alpha = 0f;
            RestoreGameplayInput();
            if (titleRoot != null) titleRoot.SetActive(false);
            else gameObject.SetActive(false);
        }

        private void RestoreMenuAfterStartFailure(string reason)
        {
            Debug.LogError("[TitleMunu] " + reason);
            startRoutine = null;
            hasStarted = false;

            if (titleCanvasGroup != null)
            {
                titleCanvasGroup.alpha = 1f;
                titleCanvasGroup.interactable = true;
                titleCanvasGroup.blocksRaycasts = true;
            }

            if (menuButtons != null) menuButtons.SetActive(true);
            if (optionPanel != null) optionPanel.SetActive(false);
            if (startButton != null) startButton.interactable = IsManagerReady;
            if (optionButton != null) optionButton.interactable = true;
            if (exitButton != null) exitButton.interactable = true;
            Select(startButton);
        }

        private void LockGameplayInput()
        {
            if (playerController == null) return;

            playerControllerWasEnabled = playerController.enabled;
            playerController.SetRunInput(false);
            playerController.SetMoveInput(0f);
            playerController.enabled = false;

            if (playerBody != null)
                playerBody.linearVelocity = new Vector2(0f, playerBody.linearVelocity.y);
        }

        private void RestoreGameplayInput()
        {
            if (playerController == null) return;

            if (playerBody != null)
                playerBody.linearVelocity = new Vector2(0f, playerBody.linearVelocity.y);
            playerController.enabled = playerControllerWasEnabled;
        }

        public void OpenOption()
        {
            if (hasStarted) return;
            if (menuButtons != null) menuButtons.SetActive(false);
            if (optionPanel != null) optionPanel.SetActive(true);
            Select(volumeSlider);
        }

        public void CloseOption()
        {
            if (optionPanel != null) optionPanel.SetActive(false);
            if (menuButtons != null) menuButtons.SetActive(true);
            Select(optionButton);
        }

        public void SetMasterVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            AudioListener.volume = clamped;
            PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
            PlayerPrefs.Save();
        }

        public void SetFullscreen(bool fullscreen)
        {
            Screen.fullScreen = fullscreen;
        }

        public void ExitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            Debug.Log("[TitleMunu] Exit은 빌드에서 게임을 종료합니다.");
#endif
        }

        private static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null ||
                !selectable.gameObject.activeInHierarchy || !selectable.IsInteractable()) return;
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
