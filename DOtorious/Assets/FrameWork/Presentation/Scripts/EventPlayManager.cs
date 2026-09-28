using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static JYW.Game.EventPlay.EventSO;
using JYW.FrameWork;

namespace JYW.Game.EventPlay
{
    public class EventPlayManager : Singleton<EventPlayManager>
    {
        public static string OwningSceneName =>
            FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation) ? "Presentation" : string.Empty;

        [SerializeField] private GameObject softSpeechPrefab;
        [SerializeField] private GameObject hardSpeechPrefab;
        [SerializeField] private GameObject memoPrefab;
        [SerializeField] private GameObject eventCamera;
        [SerializeField] private GameObject choiceCanvasPrefab;
        [SerializeField] private GameObject choiceContentsPrefab;

        [HideInInspector] public bool isLockCamera = false;
        [HideInInspector] public bool isLockMove = false;

        public bool isLockKeyboard
        {
            get => isLockMove;
            set => isLockMove = value;
        }

        public bool isLockMouse
        {
            get => isLockCamera;
            set => isLockCamera = value;
        }

        [HideInInspector] public List<GameObject> cachedObjects = new List<GameObject>();

        // 새 프리팹 참조는 기존 직렬화 필드 뒤에만 추가합니다.
        [SerializeField] private GameObject tooltipPrefab;
        [SerializeField] private GameObject blackLabelPrefab;
        [SerializeField] private GameObject portraitSpeechPrefab;
        [SerializeField] private GameObject screenFlashPrefab;
        [SerializeField] private GameObject speechBubblePrefab;
        [SerializeField] private GameObject loadingScreenPrefab;

        private GameObject fadeCanvas = null;
        private CanvasGroup fadeCanvasGroup = null;

        private readonly Dictionary<string, Delegate> eventMap = new Dictionary<string, Delegate>();

        private readonly Dictionary<string, System.Action> actionMap = new Dictionary<string, System.Action>();

        private readonly HashSet<(EventSO, GameObject)> runningEventPairs = new HashSet<(EventSO, GameObject)>();

        private AudioSource eventAudioSource;

        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private readonly Dictionary<string, bool> boolGlobals = new Dictionary<string, bool>();
        private readonly Dictionary<string, float> floatGlobals = new Dictionary<string, float>();
        private readonly Dictionary<string, int> intGlobals = new Dictionary<string, int>();
        private readonly Dictionary<string, string> stringGlobals = new Dictionary<string, string>();
        private readonly Dictionary<string, GameObject> gameObjectGlobals = new Dictionary<string, GameObject>();

        private readonly HashSet<GameObject> openedDrawers = new HashSet<GameObject>();
        private readonly HashSet<GameObject> openedDoors = new HashSet<GameObject>();

        private readonly Dictionary<GameObject, GameObject> spawnerMap = new Dictionary<GameObject, GameObject>();

        private readonly Dictionary<(EventSO, GameObject), Coroutine> runningCoroutines = new Dictionary<(EventSO, GameObject), Coroutine>();
        private readonly Dictionary<(EventSO, GameObject), EventContext> runningContexts = new Dictionary<(EventSO, GameObject), EventContext>();

        internal bool HasRunningEvents => runningEventPairs.Count > 0;

        private int lockKeyboardCount = 0;
        private int lockMouseCount = 0;

        private readonly Dictionary<GameObject, Vector3> drawerClosedLocal = new Dictionary<GameObject, Vector3>();
        private readonly Dictionary<GameObject, Vector3> drawerLastOffset = new Dictionary<GameObject, Vector3>();

        private int eventCameraUserCount = 0;
        private float eventCameraPrevDepth = 0f;
        private bool eventCameraDepthStored = false;

        private GameObject activeTooltipObject = null;
        private TooltipData activeTooltipData = null;
        private GameObject activeTooltipCenterObject = null;
        private Camera[] tooltipCameraBuffer = Array.Empty<Camera>();
        private Camera[] speechBubbleCameraBuffer = Array.Empty<Camera>();

        private readonly Dictionary<string, List<GameObject>> pausedSceneRoots = new Dictionary<string, List<GameObject>>();

        private readonly Dictionary<string, GameObject> cachedObjectsByName = new Dictionary<string, GameObject>();

        private readonly HashSet<GameObject> cachedObjectsSet = new HashSet<GameObject>();

        private sealed class CollisionBinding
        {
            public string LocatorA;
            public string LocatorB;
            public GameObject ObjectA;
            public GameObject ObjectB;
        }

        private sealed class CameraShakeRuntimeState
        {
            public Transform Target;
            public Vector3 BasePositionAtApply;
            public Quaternion BaseRotationAtApply = Quaternion.identity;
            public Vector3 AppliedPosition;
            public Quaternion AppliedRotation = Quaternion.identity;
            public bool HasAppliedOffset;
            public bool UsesEventCamera;
            public GameObject EventCameraObject;
            public Camera EventCameraComponent;
            public bool Cleaned;
        }

        private sealed class CameraLensRuntimeState
        {
            public Camera Target;
            public bool UsesOrthographicSize;
            public float TargetValue;
            public bool UsesEventCamera;
            public GameObject EventCameraObject;
            public bool Cleaned;
        }

        private sealed class MainCameraDepthRuntimeState
        {
            public Camera Target;
            public bool Acquired;
            public bool Released;
        }

        private sealed class MainCameraDepthUsage
        {
            public int Count;
            public float PreviousDepth;
        }

        private sealed class EventCameraUsageRuntimeState
        {
            public GameObject CameraObject;
            public Camera CameraComponent;
            public bool Acquired;
            public bool Released;
        }

        private sealed class RendererFadeTarget
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public int ColorPropertyId;
            public Color StartColor;
            public MaterialPropertyBlock PropertyBlock;
        }

        private sealed class VisualFadeRuntimeState
        {
            public readonly Dictionary<CanvasGroup, float> CanvasGroups =
                new Dictionary<CanvasGroup, float>();
            public readonly Dictionary<Graphic, Color> Graphics =
                new Dictionary<Graphic, Color>();
            public readonly Dictionary<SpriteRenderer, Color> Sprites =
                new Dictionary<SpriteRenderer, Color>();
            public readonly List<RendererFadeTarget> Renderers =
                new List<RendererFadeTarget>();
            public float TargetAlpha;
            public bool Cleaned;
        }

        private sealed class SpeechBubbleTargetRuntimeState
        {
            public GameObject Target;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public Collider2D[] Colliders2D = Array.Empty<Collider2D>();
            public Collider[] Colliders = Array.Empty<Collider>();
            public int LayerMask;
            public Vector3 LocalFallbackAnchor;
        }

        private sealed class LightTweenStart
        {
            public Color Color;
            public float Intensity;
            public float Range;
        }

        private sealed class LightTweenRuntimeState
        {
            public readonly Dictionary<Light, LightTweenStart> Lights =
                new Dictionary<Light, LightTweenStart>();
            public bool AffectColor;
            public bool AffectIntensity;
            public bool AffectRange;
            public Color TargetColor;
            public float TargetIntensity;
            public float TargetRange;
            public bool Cleaned;
        }

        private readonly Dictionary<EventSO.ConditionGroupData, CollisionBinding> collisionBindings =
            new Dictionary<EventSO.ConditionGroupData, CollisionBinding>();
        private readonly HashSet<KeyCode> unsupportedConditionKeys = new HashSet<KeyCode>();
        private readonly Dictionary<Camera, MainCameraDepthUsage> mainCameraDepthUsages =
            new Dictionary<Camera, MainCameraDepthUsage>();

        private sealed class EventContext
        {
            private readonly List<System.Action> onCancel = new List<System.Action>();
            public bool IsCancelled { get; private set; }

            public int AcquiredKeyboardLocks = 0;
            public int AcquiredMouseLocks = 0;

            private readonly List<Coroutine> trackedCoroutines = new List<Coroutine>();
            public IReadOnlyList<Coroutine> TrackedCoroutines => trackedCoroutines;

            public void AddOnCancel(System.Action action)
            {
                if (action == null) return;
                if (IsCancelled)
                {
                    try { action(); } catch { }
                    return;
                }
                onCancel.Add(action);
            }

            public void RemoveOnCancel(System.Action action)
            {
                if (action == null) return;
                onCancel.Remove(action);
            }

            public void RegisterCoroutine(Coroutine c)
            {
                if (c == null) return;
                trackedCoroutines.Add(c);
            }

            public void UnregisterCoroutine(Coroutine c)
            {
                if (c == null) return;
                trackedCoroutines.Remove(c);
            }

            public void Cancel()
            {
                if (IsCancelled) return;
                IsCancelled = true;
                for (int i = 0; i < onCancel.Count; i++)
                {
                    try { onCancel[i](); } catch { }
                }
                onCancel.Clear();
            }
        }
        private GameObject choiceCanvasInstance = null;

        protected override void Awake()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation))
            {
                enabled = false;
                return;
            }

            base.Awake();
            CreateFadeCanvasIfNeeded();
            if (fadeCanvas != null) fadeCanvas.SetActive(false);

            eventAudioSource = GetComponent<AudioSource>();
            if (eventAudioSource == null)
                eventAudioSource = gameObject.AddComponent<AudioSource>();

            eventAudioSource.playOnAwake = false;
            eventAudioSource.loop = false;
            eventAudioSource.spatialBlend = 0f;
            eventAudioSource.dopplerLevel = 0f;

            DisableEventCameraIfUnused();
        }

        private void OnDisable()
        {
            // 비활성화된 MonoBehaviour의 코루틴은 다시 활성화해도 재개되지 않는다.
            // 추적 상태와 UI/Lock 정리가 영구 고착되지 않도록 즉시 취소한다.
            CancelAllRunningEvents();
        }

        private void LateUpdate()
        {
            if (activeTooltipObject == null || activeTooltipData == null ||
                !activeTooltipObject.activeInHierarchy)
            {
                ClearTooltipTracking();
                return;
            }

            if (activeTooltipData.IsRelative && activeTooltipCenterObject == null &&
                !string.IsNullOrWhiteSpace(activeTooltipData.CenterObject))
                activeTooltipCenterObject = ResolveByName(activeTooltipData.CenterObject);

            ApplyTooltipPosition(
                activeTooltipObject,
                activeTooltipData,
                activeTooltipCenterObject,
                false);
        }


        private void DisableEventCameraIfUnused()
        {
            var camGO = GetOrCreateEventCamera();
            if (camGO == null) return;

            var cam = camGO.GetComponent<Camera>();
            if (cam == null) return;

            if (eventCameraUserCount <= 0)
            {
                eventCameraUserCount = 0;
                if (eventCameraDepthStored)
                {
                    cam.depth = eventCameraPrevDepth;
                    eventCameraDepthStored = false;
                }
                if (camGO.activeSelf) camGO.SetActive(false);
            }
        }



        private void CreateFadeCanvasIfNeeded()
        {
            if (fadeCanvas != null && fadeCanvasGroup != null) return;

            fadeCanvas = new GameObject("AutoFadeCanvas");
            var canvas = fadeCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9000;

            fadeCanvas.AddComponent<CanvasScaler>();
            fadeCanvas.AddComponent<GraphicRaycaster>();
            fadeCanvasGroup = fadeCanvas.AddComponent<CanvasGroup>();
            fadeCanvasGroup.interactable = false;
            fadeCanvasGroup.blocksRaycasts = false;

            var imageGO = new GameObject("FadeImage");
            imageGO.transform.SetParent(fadeCanvas.transform, false);
            var img = imageGO.AddComponent<Image>();
            img.color = Color.black;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void SetPref(GameObject caller, float value)
        {
            PlayerPrefs.SetFloat(caller.name, value);
        }
        public float GetPref(GameObject caller)
        {
            if (caller == null || string.IsNullOrEmpty(caller.name)) return 0;
            return PlayerPrefs.GetFloat(caller.name, 0);
        }

        public void SetGlobals<T>(string name, T value)
        {
            if (string.IsNullOrEmpty(name)) return;

            var type = typeof(T);

            if (type == typeof(float)) { floatGlobals[name] = (float)(object)value; return; }
            if (type == typeof(int)) { intGlobals[name] = (int)(object)value; return; }
            if (type == typeof(bool)) { boolGlobals[name] = (bool)(object)value; return; }
            if (type == typeof(string)) { stringGlobals[name] = (string)(object)value ?? string.Empty; return; }
            if (type == typeof(GameObject)) { gameObjectGlobals[name] = (GameObject)(object)value; return; }

#if UNITY_EDITOR
            Debug.LogWarning($"[EventPlayManager] SetGlobals: 타입 '{type.Name}'은 지원되지 않습니다.");
#endif
        }


        public T GetGlobal<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) return default;

            var type = typeof(T);

            if (type == typeof(float)) return floatGlobals.TryGetValue(key, out var fv) ? (T)(object)fv : default;
            if (type == typeof(int)) return intGlobals.TryGetValue(key, out var iv) ? (T)(object)iv : default;
            if (type == typeof(bool)) return boolGlobals.TryGetValue(key, out var bv) ? (T)(object)bv : default;
            if (type == typeof(string)) return stringGlobals.TryGetValue(key, out var sv) ? (T)(object)sv : default;
            if (type == typeof(GameObject)) return gameObjectGlobals.TryGetValue(key, out var go) ? (T)(object)go : default;

#if UNITY_EDITOR
            Debug.LogWarning($"[EventPlayManager] GetGlobal: 타입 '{type.Name}'은 지원되지 않습니다.");
#endif
            return default;
        }



        public void PlayEvent(EventSO eventSO, GameObject caller, bool allowSameCaller = false)
        {
            if (eventSO == null) return;

            CancelAllRunningEvents();

            var pair = (eventSO, caller);
            if (runningEventPairs.Contains(pair))
                return;

            var context = new EventContext();

            runningEventPairs.Add(pair);
            runningContexts[pair] = context;

            float eventStartTime = Time.time;

            EventSO.EventStep chosenStep = null;

            if (eventSO != null && !eventSO.UseCondition)
            {
                if (eventSO.ConditionSteps != null && eventSO.ConditionSteps.Length > 0)
                {
                    chosenStep = eventSO.ConditionSteps[0];
                }
                else
                {
                    if (eventSO.stepsGroups != null && eventSO.stepsGroups.Length > 0)
                    {
                        var grp = eventSO.stepsGroups[0];
                        if (grp != null && grp.Steps != null && grp.Steps.Length > 0)
                            chosenStep = grp.Steps[0];
                    }
                    else if (eventSO.elseSteps != null && eventSO.elseSteps.Length > 0)
                    {
                        chosenStep = eventSO.elseSteps[0];
                    }
                }
            }
            else
            {
                int matchedIndex = -1;
                if (eventSO.Conditions != null && eventSO.Conditions.Length > 0)
                {
                    for (int i = 0; i < eventSO.Conditions.Length; i++)
                    {
                        var condGroup = eventSO.Conditions[i];
                        if (EvaluateConditionGroup(condGroup, caller))
                        {
                            matchedIndex = i;
                            break;
                        }
                    }
                }

                int condCount = eventSO.Conditions != null ? eventSO.Conditions.Length : 0;
                if (matchedIndex >= 0)
                {
                    if (eventSO.ConditionSteps != null && matchedIndex < eventSO.ConditionSteps.Length)
                    {
                        chosenStep = eventSO.ConditionSteps[matchedIndex];
                    }
                    else
                    {
                        if (eventSO.stepsGroups != null && matchedIndex < eventSO.stepsGroups.Length)
                        {
                            var grp = eventSO.stepsGroups[matchedIndex];
                            if (grp != null && grp.Steps != null && grp.Steps.Length > 0)
                                chosenStep = grp.Steps[0];
                        }
                    }
                }
                else
                {
                    if (eventSO.ConditionSteps != null && eventSO.ConditionSteps.Length > condCount)
                    {
                        chosenStep = eventSO.ConditionSteps[condCount];
                    }
                    else
                    {
                        if (eventSO.elseSteps != null && eventSO.elseSteps.Length > 0)
                            chosenStep = eventSO.elseSteps[0];
                    }
                }
            }

            if (chosenStep == null)
            {
                CleanupEventTracking(pair);
                return;
            }

            var coroutine = StartCoroutine(
                ProcessEventConcurrently(eventSO, chosenStep, eventStartTime, caller, context)
            );
            // 첫 MoveNext에서 즉시 완료된 코루틴을 완료 후 다시 등록하지 않는다.
            if (coroutine != null && runningEventPairs.Contains(pair) &&
                runningContexts.TryGetValue(pair, out var activeContext) &&
                ReferenceEquals(activeContext, context))
            {
                runningCoroutines[pair] = coroutine;
                context.RegisterCoroutine(coroutine);
            }
        }

        internal bool TryPlayEventIfIdle(EventSO eventSO, GameObject caller)
        {
            if (eventSO == null || !isActiveAndEnabled || !gameObject.activeInHierarchy || HasRunningEvents)
                return false;

            PlayEvent(eventSO, caller);
            return true;
        }

        internal bool TryPlayCapturedEventIfIdle(
            EventSO eventSO,
            GameObject caller,
            EventSO.EventStep capturedStep)
        {
            if (eventSO == null || capturedStep == null || !isActiveAndEnabled ||
                !gameObject.activeInHierarchy || HasRunningEvents)
                return false;

            var pair = (eventSO, caller);
            if (runningEventPairs.Contains(pair)) return false;

            var context = new EventContext();
            runningEventPairs.Add(pair);
            runningContexts[pair] = context;

            var coroutine = StartCoroutine(
                ProcessEventConcurrently(eventSO, capturedStep, Time.time, caller, context)
            );
            if (coroutine != null && runningEventPairs.Contains(pair) &&
                runningContexts.TryGetValue(pair, out var activeContext) &&
                ReferenceEquals(activeContext, context))
            {
                runningCoroutines[pair] = coroutine;
                context.RegisterCoroutine(coroutine);
            }

            return true;
        }

        private void CancelAllRunningEvents()
        {
            if (runningContexts.Count == 0 && runningCoroutines.Count == 0) return;

            var keys = runningContexts.Keys.ToArray();
            foreach (var pair in keys)
            {
                if (!runningContexts.TryGetValue(pair, out var ctx) || ctx == null) continue;

                try { ctx.Cancel(); } catch { }

                var tracked = ctx.TrackedCoroutines;
                if (tracked != null)
                {
                    foreach (var c in tracked.ToArray())
                    {
                        try { StopCoroutine(c); } catch { }
                    }
                }

                if (runningCoroutines.TryGetValue(pair, out var mainC) && mainC != null)
                {
                    try { StopCoroutine(mainC); } catch { }
                }

                CleanupEventTracking(pair);
            }
        }

        private bool EvaluateConditionGroup(EventSO.ConditionGroupData condGroup, GameObject caller)
        {
            if (condGroup == null)
                return false;

            EventSO.ConditionChecks checks = condGroup.Checks;
            if (checks == EventSO.ConditionChecks.None) return true;

            bool selectedChecksPass = true;

            if ((checks & EventSO.ConditionChecks.TimeCount) != 0)
            {
                double requiredSeconds = Math.Max(0d, condGroup.TimeCountSeconds);
                if (Time.timeAsDouble < requiredSeconds) selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.CollisionAB) != 0)
            {
                ResolveCollisionObjects(condGroup, out GameObject objectA, out GameObject objectB);
                if (objectA == null || objectB == null || objectA == objectB ||
                    !EventCollisionTracker.HasCollided(objectA, objectB))
                    selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.ObjectState) != 0)
            {
                string targetName = condGroup.ObjectStateTarget?.Trim() ?? string.Empty;
                GameObject target = string.IsNullOrEmpty(targetName) ? null : ResolveByName(targetName);
                bool statePass;
                switch (condGroup.ObjectState)
                {
                    case EventSO.ObjectStateCheck.Missing:
                        statePass = target == null;
                        break;
                    case EventSO.ObjectStateCheck.Active:
                        statePass = target != null && target.activeInHierarchy;
                        break;
                    case EventSO.ObjectStateCheck.Inactive:
                        statePass = target != null && !target.activeInHierarchy;
                        break;
                    default:
                        statePass = target != null;
                        break;
                }
                if (!statePass) selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.Distance) != 0)
            {
                string locatorA = condGroup.DistanceObjectA?.Trim() ?? string.Empty;
                string locatorB = condGroup.DistanceObjectB?.Trim() ?? string.Empty;
                GameObject objectA = string.IsNullOrEmpty(locatorA) ? null : ResolveByName(locatorA);
                GameObject objectB = string.IsNullOrEmpty(locatorB) ? null : ResolveByName(locatorB);
                if (objectA == null || objectB == null || objectA == objectB)
                {
                    selectedChecksPass = false;
                }
                else
                {
                    float distance = condGroup.DistanceUse2D
                        ? Vector2.Distance(
                            new Vector2(objectA.transform.position.x, objectA.transform.position.y),
                            new Vector2(objectB.transform.position.x, objectB.transform.position.y))
                        : Vector3.Distance(objectA.transform.position, objectB.transform.position);
                    float threshold = Mathf.Max(0f, condGroup.DistanceThreshold);
                    bool distancePass = condGroup.DistanceComparison == EventSO.DistanceCheck.AtLeast
                        ? distance >= threshold
                        : distance <= threshold;
                    if (!distancePass) selectedChecksPass = false;
                }
            }

            if ((checks & EventSO.ConditionChecks.InputKey) != 0)
            {
                bool readable;
                bool inputPass;
                if (condGroup.InputKeyState == EventSO.InputKeyCheck.Held)
                    readable = EventInputReader.TryIsPressed(condGroup.InputKeyCode, out inputPass);
                else
                    readable = EventInputReader.TryWasPressedThisFrame(condGroup.InputKeyCode, out inputPass);

                if (!readable)
                {
                    if (unsupportedConditionKeys.Add(condGroup.InputKeyCode))
                        Debug.LogWarning($"[EventPlayManager] 현재 입력 설정에서 Condition 키 '{condGroup.InputKeyCode}'를 읽을 수 없습니다.");
                    selectedChecksPass = false;
                }
                else if (!inputPass)
                {
                    selectedChecksPass = false;
                }
            }

            if ((checks & EventSO.ConditionChecks.AnimatorState) != 0)
            {
                string animatorLocator = condGroup.AnimatorObject?.Trim() ?? string.Empty;
                string stateName = condGroup.AnimatorStateName?.Trim() ?? string.Empty;
                GameObject animatorObject = string.IsNullOrEmpty(animatorLocator)
                    ? caller
                    : ResolveByName(animatorLocator);
                Animator animator = animatorObject != null
                    ? animatorObject.GetComponent<Animator>() ?? animatorObject.GetComponentInChildren<Animator>(true)
                    : null;
                int layer = condGroup.AnimatorLayer;
                bool animatorPass = animator != null &&
                                    layer >= 0 &&
                                    layer < animator.layerCount &&
                                    !string.IsNullOrEmpty(stateName);
                if (animatorPass)
                {
                    AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layer);
                    animatorPass = stateInfo.IsName(stateName);
                    if (animatorPass && condGroup.AnimatorState == EventSO.AnimatorStateCheck.Completed)
                    {
                        float completionTime = Mathf.Max(0f, condGroup.AnimatorCompletionTime);
                        animatorPass = stateInfo.normalizedTime >= completionTime &&
                                       !animator.IsInTransition(layer);
                    }
                }
                if (!animatorPass) selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.SceneState) != 0)
            {
                string sceneName = condGroup.SceneName?.Trim() ?? string.Empty;
                Scene scene = string.IsNullOrEmpty(sceneName)
                    ? default
                    : SceneManager.GetSceneByName(sceneName);
                bool loaded = scene.IsValid() && scene.isLoaded;
                bool scenePass;
                switch (condGroup.SceneState)
                {
                    case EventSO.SceneStateCheck.Unloaded:
                        scenePass = !loaded;
                        break;
                    case EventSO.SceneStateCheck.Active:
                        Scene activeScene = SceneManager.GetActiveScene();
                        scenePass = loaded && activeScene.IsValid() && activeScene.handle == scene.handle;
                        break;
                    default:
                        scenePass = loaded;
                        break;
                }
                if (!scenePass) selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.CameraView) != 0)
            {
                string targetName = condGroup.CameraViewTarget?.Trim() ?? string.Empty;
                GameObject target = string.IsNullOrEmpty(targetName) ? null : ResolveByName(targetName);
                bool cameraReady = TryGetObjectCameraVisibility(target, out bool visible);
                bool cameraViewPass = target != null && cameraReady &&
                                      (condGroup.CameraViewState == EventSO.CameraViewCheck.NotVisible
                                          ? !visible
                                          : visible);
                if (!cameraViewPass) selectedChecksPass = false;
            }

            if ((checks & EventSO.ConditionChecks.CheckValue) == 0)
                return selectedChecksPass;

            if (condGroup.Conditions == null || condGroup.Conditions.Length == 0)
                return false;

            bool allMatch = true;
            for (int ci = 0; ci < condGroup.Conditions.Length; ci++)
            {
                var info = condGroup.Conditions[ci];
                if (info == null) { allMatch = false; break; }

                string key = info.GlobalNames ?? string.Empty;
                bool pass = false;
                bool usePlayerPrefs = info.isPlayerPrefData;

                if (info.checkType == EventSO.CheckType.Odd || info.checkType == EventSO.CheckType.Even)
                {
                    int current = usePlayerPrefs
                        ? (string.IsNullOrEmpty(key) ? 0 : PlayerPrefs.GetInt(key, 0))
                        : (intGlobals.TryGetValue(key, out var iv) ? iv : 0);

                    pass = (info.checkType == EventSO.CheckType.Odd) ? (current % 2 != 0) : (current % 2 == 0);
                    if (!pass) { allMatch = false; break; }
                    continue;
                }

                if (string.IsNullOrEmpty(key))
                {
                    switch (info.valueType)
                    {
                        case EventSO.ValueType.Int:
                            pass = EvaluateNumericComparison(0, info.ExpectedInt, info.checkType);
                            break;
                        case EventSO.ValueType.Float:
                            pass = EvaluateNumericComparison(0f, info.ExpectedFloat, info.checkType);
                            break;
                        case EventSO.ValueType.Bool:
                            pass = EvaluateEqualityComparison(!info.ExpectedBool, info.checkType);
                            break;
                        case EventSO.ValueType.String:
                            pass = EvaluateEqualityComparison(
                                string.IsNullOrEmpty(info.ExpectedString),
                                info.checkType);
                            break;
                        case EventSO.ValueType.GameObject:
                            pass = EvaluateEqualityComparison(info.ExpectedGameObject == null, info.checkType);
                            break;
                    }
                    if (!pass) { allMatch = false; break; }
                    continue;
                }

                switch (info.valueType)
                {
                    case EventSO.ValueType.Int:
                        {
                            int current = usePlayerPrefs ? PlayerPrefs.GetInt(key, 0) : (intGlobals.TryGetValue(key, out var iv) ? iv : 0);
                            pass = EvaluateNumericComparison(current, info.ExpectedInt, info.checkType);
                            break;
                        }
                    case EventSO.ValueType.Float:
                        {
                            float current = usePlayerPrefs ? PlayerPrefs.GetFloat(key, 0f) : (floatGlobals.TryGetValue(key, out var fv) ? fv : 0f);
                            pass = EvaluateNumericComparison(current, info.ExpectedFloat, info.checkType);
                            break;
                        }
                    case EventSO.ValueType.Bool:
                        {
                            bool current = usePlayerPrefs ? (PlayerPrefs.GetInt(key, 0) != 0) : (boolGlobals.TryGetValue(key, out var bv) && bv);
                            pass = EvaluateEqualityComparison(current == info.ExpectedBool, info.checkType);
                            break;
                        }
                    case EventSO.ValueType.String:
                        {
                            string current = usePlayerPrefs ? PlayerPrefs.GetString(key, string.Empty) : (stringGlobals.TryGetValue(key, out var sv) ? sv : string.Empty);
                            bool equals = string.Equals(current ?? string.Empty, info.ExpectedString ?? string.Empty, StringComparison.Ordinal);
                            pass = EvaluateEqualityComparison(equals, info.checkType);
                            break;
                        }
                    case EventSO.ValueType.GameObject:
                        {
                            bool equals = gameObjectGlobals.TryGetValue(key, out var current) &&
                                          current == info.ExpectedGameObject;
                            pass = EvaluateEqualityComparison(equals, info.checkType);
                            break;
                        }
                }

                if (!pass) { allMatch = false; break; }
            }

            return allMatch && selectedChecksPass;
        }

        private static bool EvaluateNumericComparison(int current, int expected, EventSO.CheckType checkType)
        {
            switch (checkType)
            {
                case EventSO.CheckType.NotEqual: return current != expected;
                case EventSO.CheckType.Greater: return current > expected;
                case EventSO.CheckType.GreaterOrEqual: return current >= expected;
                case EventSO.CheckType.Less: return current < expected;
                case EventSO.CheckType.LessOrEqual: return current <= expected;
                default: return current == expected;
            }
        }

        private static bool EvaluateNumericComparison(float current, float expected, EventSO.CheckType checkType)
        {
            switch (checkType)
            {
                case EventSO.CheckType.NotEqual: return current != expected;
                case EventSO.CheckType.Greater: return current > expected;
                case EventSO.CheckType.GreaterOrEqual: return current >= expected;
                case EventSO.CheckType.Less: return current < expected;
                case EventSO.CheckType.LessOrEqual: return current <= expected;
                default: return current == expected;
            }
        }

        private static bool EvaluateEqualityComparison(bool equals, EventSO.CheckType checkType)
        {
            if (checkType == EventSO.CheckType.NotEqual) return !equals;
            return checkType == EventSO.CheckType.Value && equals;
        }

        private void ResolveCollisionObjects(
            EventSO.ConditionGroupData condition,
            out GameObject objectA,
            out GameObject objectB)
        {
            objectA = null;
            objectB = null;
            if (condition == null) return;

            string locatorA = condition.CollisionObjectA?.Trim() ?? string.Empty;
            string locatorB = condition.CollisionObjectB?.Trim() ?? string.Empty;
            if (!collisionBindings.TryGetValue(condition, out CollisionBinding binding))
            {
                binding = new CollisionBinding();
                collisionBindings.Add(condition, binding);
            }

            if (!string.Equals(binding.LocatorA, locatorA, StringComparison.Ordinal))
            {
                binding.LocatorA = locatorA;
                binding.ObjectA = null;
            }
            if (!string.Equals(binding.LocatorB, locatorB, StringComparison.Ordinal))
            {
                binding.LocatorB = locatorB;
                binding.ObjectB = null;
            }

            if (binding.ObjectA == null && !string.IsNullOrEmpty(locatorA))
                binding.ObjectA = ResolveByName(locatorA);
            if (binding.ObjectB == null && !string.IsNullOrEmpty(locatorB))
                binding.ObjectB = ResolveByName(locatorB);

            objectA = binding.ObjectA;
            objectB = binding.ObjectB;
        }

        private bool TryGetObjectCameraVisibility(GameObject target, out bool visible)
        {
            visible = false;
            if (target == null) return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Camera camera = GetHighestDepthGameCamera(target, renderers);
            if (camera == null) return false;
            if (!target.activeInHierarchy) return true;

            if (renderers.Length > 0)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                        continue;
                    if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    {
                        visible = true;
                        break;
                    }
                }
                return true;
            }

            if ((camera.cullingMask & (1 << target.layer)) == 0) return true;
            Vector3 viewportPoint = camera.WorldToViewportPoint(target.transform.position);
            visible = viewportPoint.z > 0f &&
                      viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                      viewportPoint.y >= 0f && viewportPoint.y <= 1f;
            return true;
        }

        private Camera GetHighestDepthGameCamera(GameObject target, Renderer[] renderers)
        {
            int cameraCount = Camera.allCamerasCount;
            if (cameraCount <= 0) return null;
            if (tooltipCameraBuffer.Length < cameraCount)
                tooltipCameraBuffer = new Camera[cameraCount];

            int foundCount = Camera.GetAllCameras(tooltipCameraBuffer);
            Camera bestCamera = null;
            for (int i = 0; i < foundCount; i++)
            {
                Camera candidate = tooltipCameraBuffer[i];
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    candidate.cameraType != CameraType.Game || candidate.targetTexture != null)
                    continue;

                bool seesTargetLayer = target != null &&
                                       (candidate.cullingMask & (1 << target.layer)) != 0;
                if (!seesTargetLayer && renderers != null)
                {
                    for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        Renderer renderer = renderers[rendererIndex];
                        if (renderer != null &&
                            (candidate.cullingMask & (1 << renderer.gameObject.layer)) != 0)
                        {
                            seesTargetLayer = true;
                            break;
                        }
                    }
                }
                if (!seesTargetLayer) continue;

                if (bestCamera == null || candidate.depth > bestCamera.depth)
                    bestCamera = candidate;
            }

            return bestCamera;
        }

        internal bool IsAutoConditionReady(EventSO eventSO, GameObject caller)
        {
            return TryGetAutoConditionStep(eventSO, caller, out _);
        }

        internal void ArmCollisionConditions(EventSO eventSO)
        {
            if (eventSO == null || eventSO.Conditions == null) return;
            for (int i = 0; i < eventSO.Conditions.Length; i++)
            {
                EventSO.ConditionGroupData condition = eventSO.Conditions[i];
                if (condition == null ||
                    (condition.Checks & EventSO.ConditionChecks.CollisionAB) == 0)
                    continue;

                ResolveCollisionObjects(condition, out GameObject objectA, out GameObject objectB);
                if (objectA != null && objectB != null && objectA != objectB)
                    EventCollisionTracker.HasCollided(objectA, objectB);
            }
        }

        internal bool TryGetAutoConditionStep(
            EventSO eventSO,
            GameObject caller,
            out EventSO.EventStep capturedStep)
        {
            capturedStep = null;
            if (eventSO == null || !eventSO.UseCondition ||
                eventSO.Conditions == null || eventSO.Conditions.Length == 0)
                return false;

            for (int i = 0; i < eventSO.Conditions.Length; i++)
            {
                EventSO.ConditionGroupData condition = eventSO.Conditions[i];
                if (!IsCompleteAutoCondition(condition) || !EvaluateConditionGroup(condition, caller))
                    continue;

                if (eventSO.ConditionSteps != null && i < eventSO.ConditionSteps.Length)
                {
                    capturedStep = eventSO.ConditionSteps[i];
                    return capturedStep != null;
                }

                if (eventSO.stepsGroups != null && i < eventSO.stepsGroups.Length)
                {
                    EventSO.EventStepGroup group = eventSO.stepsGroups[i];
                    if (group != null && group.Steps != null && group.Steps.Length > 0)
                    {
                        capturedStep = group.Steps[0];
                        return capturedStep != null;
                    }
                }

                return false;
            }

            return false;
        }

        private static bool IsCompleteAutoCondition(EventSO.ConditionGroupData condition)
        {
            if (condition == null)
                return false;
            if (condition.Checks == EventSO.ConditionChecks.None)
                return true;

            if ((condition.Checks & EventSO.ConditionChecks.CollisionAB) != 0)
            {
                if (string.IsNullOrWhiteSpace(condition.CollisionObjectA) ||
                    string.IsNullOrWhiteSpace(condition.CollisionObjectB) ||
                    string.Equals(
                        condition.CollisionObjectA.Trim(),
                        condition.CollisionObjectB.Trim(),
                        StringComparison.Ordinal))
                    return false;
            }

            if ((condition.Checks & EventSO.ConditionChecks.ObjectState) != 0 &&
                string.IsNullOrWhiteSpace(condition.ObjectStateTarget))
                return false;

            if ((condition.Checks & EventSO.ConditionChecks.Distance) != 0)
            {
                if (string.IsNullOrWhiteSpace(condition.DistanceObjectA) ||
                    string.IsNullOrWhiteSpace(condition.DistanceObjectB) ||
                    string.Equals(
                        condition.DistanceObjectA.Trim(),
                        condition.DistanceObjectB.Trim(),
                        StringComparison.Ordinal))
                    return false;
            }

            if ((condition.Checks & EventSO.ConditionChecks.InputKey) != 0 &&
                condition.InputKeyCode == KeyCode.None)
                return false;

            if ((condition.Checks & EventSO.ConditionChecks.AnimatorState) != 0 &&
                (string.IsNullOrWhiteSpace(condition.AnimatorObject) ||
                 string.IsNullOrWhiteSpace(condition.AnimatorStateName) ||
                 condition.AnimatorLayer < 0))
                return false;

            if ((condition.Checks & EventSO.ConditionChecks.SceneState) != 0 &&
                string.IsNullOrWhiteSpace(condition.SceneName))
                return false;

            if ((condition.Checks & EventSO.ConditionChecks.CameraView) != 0 &&
                string.IsNullOrWhiteSpace(condition.CameraViewTarget))
                return false;

            if ((condition.Checks & EventSO.ConditionChecks.CheckValue) == 0)
                return true;

            if (condition.Conditions == null || condition.Conditions.Length == 0)
                return false;

            // 새 Value 행의 기본값(빈 Key)이 0/false/empty와 우연히 일치해
            // 자동 이벤트가 즉시 발화하지 않도록, 자동 감시에서만 완성도를 검사한다.
            for (int i = 0; i < condition.Conditions.Length; i++)
            {
                EventSO.ConditionSubData value = condition.Conditions[i];
                if (value == null || string.IsNullOrWhiteSpace(value.GlobalNames))
                    return false;
            }

            return true;
        }

        // ??????????????????????????????????????????????????????????????
        //  ProcessStep: 그룹 래퍼 직접 접근 (서브 플래그는 각 그룹 내부)
        // ??????????????????????????????????????????????????????????????
        private IEnumerator ProcessStep(EventSO.EventStep step, GameObject caller, EventContext context)
        {
            if (step == null) yield break;

            float stepStartTime = Time.time;
            float stepStartRealtime = Time.realtimeSinceStartup;

            int activeRoutines = 0;

            IEnumerator RunRoutine(IEnumerator routine, Action onCancel = null)
            {
                activeRoutines++;

                bool finished = false;
                Action cancelHandler = null;

                void MarkFinished() { finished = true; }
                if (context != null)
                {
                    if (onCancel != null)
                        cancelHandler = () => { try { onCancel(); } catch { } MarkFinished(); };
                    else
                        cancelHandler = MarkFinished;
                    context.AddOnCancel(cancelHandler);
                }

                IEnumerator Wrapper()
                {
                    try
                    {
                        yield return routine;
                    }
                    finally
                    {
                        if (context != null && cancelHandler != null)
                            context.RemoveOnCancel(cancelHandler);
                        if (routine is IDisposable disposable)
                        {
                            try { disposable.Dispose(); }
                            catch (Exception exception)
                            {
                                Debug.LogWarning($"[EventPlayManager] 이벤트 루틴 정리 실패: {exception.Message}");
                            }
                        }
                        finished = true;
                    }
                }

                var cr = StartCoroutine(Wrapper());
                if (context != null) context.RegisterCoroutine(cr);

                while (!finished)
                    yield return null;

                if (context != null) context.UnregisterCoroutine(cr);
                activeRoutines--;
            }

            // SetValue
            if (step.Flags.IsSetGlobals && step.SetGlobals != null)
            {
                var values = step.SetGlobals.SetValues;
                if (values != null)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        ApplySetValue(values[i], caller);
                    }
                }
            }

            // ── Objects 그룹 ──
            if (step.Flags.IsObjects && step.Objects != null)
            {
                var objects = step.Objects;

                // Spawn
                if (objects.IsSpawnObject && objects.SpawnObject != null)
                {
                    var spawnList = objects.SpawnObject.SpawnDatas;
                    if (spawnList != null && spawnList.Length > 0)
                    {
                        foreach (var sd in spawnList)
                        {
                            if (sd == null || sd.prefab == null) continue;

                            GameObject parentGO = null;

                            Scene scene;
                            if (!String.IsNullOrEmpty(sd.sceneName))
                                scene = SceneManager.GetSceneByName(sd.sceneName);
                            else
                                scene = caller != null ? caller.scene : default;

                            if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(sd.parentName))
                            {
                                var roots = scene.GetRootGameObjects();
                                for (int ri = 0; ri < roots.Length && parentGO == null; ri++)
                                {
                                    var root = roots[ri];
                                    if (root.name == sd.parentName) { parentGO = root; break; }
                                    var t = root.transform.Find(sd.parentName);
                                    if (t != null) { parentGO = t.gameObject; break; }
                                }
                            }

                            Quaternion spawnWorldRot = Quaternion.Euler(sd.rotation);
                            GameObject go;

                            if (sd.IsUI)
                            {
                                go = Instantiate(sd.prefab);
                                go.name = string.IsNullOrEmpty(sd.naming) ? sd.prefab.name : sd.naming;

                                if (parentGO != null)
                                    go.transform.SetParent(parentGO.transform, false);
                                else if (scene.IsValid() && scene.isLoaded)
                                    try { SceneManager.MoveGameObjectToScene(go, scene); } catch { }

                                var rt = go.GetComponent<RectTransform>();
                                if (rt == null)
                                {
                                    Debug.LogWarning($"[EventPlayManager] UI 스폰: '{go.name}'에 RectTransform이 없어 Transform 배치로 폴백합니다.");
                                    if (parentGO != null) { go.transform.localPosition = sd.position; go.transform.localRotation = Quaternion.Euler(sd.rotation); }
                                    else { go.transform.position = sd.position; go.transform.rotation = Quaternion.Euler(sd.rotation); }
                                }
                                else
                                {
                                    rt.anchorMin = sd.AnchorMin; rt.anchorMax = sd.AnchorMax; rt.pivot = sd.Pivot;
                                    rt.anchoredPosition = sd.AnchoredPosition; rt.localScale = sd.LocalScale;
                                    rt.localRotation = Quaternion.Euler(sd.LocalEulerAngles);
                                }

                                if (caller != null) spawnerMap[go] = caller;
                                AddToCacheIfNeeded(go);
                            }
                            else
                            {
                                if (sd.IsLocal && !string.IsNullOrEmpty(sd.LocalCenterName))
                                {
                                    GameObject center = ResolveByName(sd.LocalCenterName);
                                    Vector3 spawnWorldPos;
                                    if (center != null)
                                        spawnWorldPos = center.transform.position + center.transform.right * sd.LocalOffset.x + center.transform.up * sd.LocalOffset.y + center.transform.forward * sd.LocalOffset.z;
                                    else { Debug.LogWarning($"[EventPlayManager] Spawn: Center '{sd.LocalCenterName}' not found. Use sd.position."); spawnWorldPos = sd.position; }

                                    go = Instantiate(sd.prefab, spawnWorldPos, spawnWorldRot);
                                    go.name = string.IsNullOrEmpty(sd.naming) ? sd.prefab.name : sd.naming;
                                    if (parentGO != null) go.transform.SetParent(parentGO.transform, true);
                                    else if (scene.IsValid() && scene.isLoaded) try { SceneManager.MoveGameObjectToScene(go, scene); } catch { }
                                }
                                else
                                {
                                    go = Instantiate(sd.prefab);
                                    go.name = string.IsNullOrEmpty(sd.naming) ? sd.prefab.name : sd.naming;
                                    var agent = go.GetComponent("NavMeshAgent") as Behaviour;
                                    if (agent != null) agent.enabled = false;
                                    if (parentGO != null) { go.transform.SetParent(parentGO.transform, false); go.transform.localPosition = sd.position; go.transform.localRotation = spawnWorldRot; }
                                    else { if (scene.IsValid() && scene.isLoaded) try { SceneManager.MoveGameObjectToScene(go, scene); } catch { } go.transform.position = sd.position; go.transform.rotation = spawnWorldRot; }
                                    if (agent != null) agent.enabled = true;
                                }

                                if (caller != null) spawnerMap[go] = caller;
                                AddToCacheIfNeeded(go);
                            }
                        }
                    }
                }

                // Destroy
                if (objects.IsDestroyObject && objects.DestroyObject != null)
                {
                    foreach (var objName in objects.DestroyObject.DestroyObjectNames)
                    {
                        GameObject target = string.IsNullOrEmpty(objName) ? caller : ResolveByName(objName);
                        if (target == null && !string.IsNullOrEmpty(objName)) { target = ResolveByName(objName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target == null) continue;
                        RemoveFromCache(target); spawnerMap.Remove(target); Destroy(target);
                    }
                }

                // EnableObject
                if (objects.IsEnableObject && objects.EnableObject != null)
                {
                    foreach (var objName in objects.EnableObject.EnableObjectNames)
                    {
                        GameObject target = string.IsNullOrEmpty(objName) ? caller : ResolveByName(objName);
                        if (target == null && !string.IsNullOrEmpty(objName)) { target = ResolveByName(objName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target == null) continue;
                        target.SetActive(true);
                    }
                }

                // DisableObject
                if (objects.IsDisableObject && objects.DisableObject != null)
                {
                    foreach (var objName in objects.DisableObject.DisableObjectNames)
                    {
                        GameObject target = string.IsNullOrEmpty(objName) ? caller : ResolveByName(objName);
                        if (target == null && !string.IsNullOrEmpty(objName)) { target = ResolveByName(objName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target == null) continue;
                        AddToCacheIfNeeded(target); target.SetActive(false);
                    }
                }

                // Rename
                if (objects.IsRename && objects.Rename != null)
                {
                    string objName = objects.Rename.ObjectName;
                    GameObject target = string.IsNullOrEmpty(objName) ? caller : ResolveByName(objName);
                    if (target == null && !string.IsNullOrEmpty(objName))
                    {
                        target = ResolveByName(objects.Rename.ObjectName);
                        if (target != null) AddToCacheIfNeeded(target);
                    }
                    if (target != null)
                    {
                        string oldName = target.name;
                        if (!string.IsNullOrEmpty(oldName) &&
                            cachedObjectsByName.TryGetValue(oldName, out var oldCached) &&
                            oldCached == target)
                            cachedObjectsByName.Remove(oldName);

                        target.name = objects.Rename.NewName;
                        if (!string.IsNullOrEmpty(target.name) &&
                            (!cachedObjectsByName.TryGetValue(target.name, out var newCached) || newCached == null || newCached == target))
                            cachedObjectsByName[target.name] = target;
                    }
                }
            }

            // ── Components 그룹 ──
            if (step.Flags.IsComponents && step.Components != null)
            {
                var comps = step.Components;

                // EnableComponent
                if (comps.IsEnableComponent && comps.EnableComponent != null && comps.EnableComponent.ComponentDatas != null && comps.EnableComponent.ComponentDatas.Length > 0)
                {
                    foreach (var ec in comps.EnableComponent.ComponentDatas)
                    {
                        if (ec == null) continue;
                        GameObject target = string.IsNullOrEmpty(ec.gameObjectName) ? caller : ResolveByName(ec.gameObjectName);
                        if (target == null && !string.IsNullOrEmpty(ec.gameObjectName)) { target = ResolveByName(ec.gameObjectName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target == null) continue;
                        if (string.IsNullOrEmpty(ec.componentName)) continue;
                        Component found = null;
                        foreach (var c in target.GetComponents<Component>()) { if (c == null) continue; var t = c.GetType(); if (t.Name == ec.componentName || t.FullName == ec.componentName) { found = c; break; } }
                        if (found == null) { Debug.LogWarning($"[EventPlayManager] EnableComponent: component '{ec.componentName}' not found on '{target.name}'"); continue; }
                        if (found is Behaviour beh) { try { beh.enabled = true; } catch { } }
                        else if (found is Collider col) { try { col.enabled = true; } catch { } }
                        else if (found is Renderer rend) { try { rend.enabled = true; } catch { } }
                        else { try { var prop = found.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite) prop.SetValue(found, true); } catch (System.Exception ex) { Debug.LogWarning($"[EventPlayManager] EnableComponent: failed - {ex.Message}"); } }
                    }
                }

                // DisableComponent
                if (comps.IsDisableComponent && comps.DisableComponent != null && comps.DisableComponent.ComponentDatas != null && comps.DisableComponent.ComponentDatas.Length > 0)
                {
                    foreach (var dc in comps.DisableComponent.ComponentDatas)
                    {
                        if (dc == null) continue;
                        GameObject target = string.IsNullOrEmpty(dc.gameObjectName) ? caller : ResolveByName(dc.gameObjectName);
                        if (target == null && !string.IsNullOrEmpty(dc.gameObjectName)) { target = ResolveByName(dc.gameObjectName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target == null) continue;
                        if (string.IsNullOrEmpty(dc.componentName)) continue;
                        Component found = null;
                        foreach (var c in target.GetComponents<Component>()) { if (c == null) continue; var t = c.GetType(); if (t.Name == dc.componentName || t.FullName == dc.componentName) { found = c; break; } }
                        if (found is Behaviour beh2) { try { beh2.enabled = false; } catch { } }
                        else if (found is Collider col2) { try { col2.enabled = false; } catch { } }
                        else if (found is Renderer rend2) { try { rend2.enabled = false; } catch { } }
                        else { try { var prop = found.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite) prop.SetValue(found, false); } catch (System.Exception ex) { Debug.LogWarning($"[EventPlayManager] DisableComponent: failed - {ex.Message}"); } }
                    }
                }

                // DisableCollider
                if (comps.IsDisableColliderObject && comps.DisableColliderObject != null)
                {
                    foreach (var objName in comps.DisableColliderObject.DisableColliderObjectNames)
                    {
                        GameObject target = null;
                        if (string.IsNullOrEmpty(objName)) target = caller;
                        else { target = ResolveByName(objName); if (target != null) AddToCacheIfNeeded(target); }
                        if (target != null)
                        {
                            var colliders3D = target.GetComponentsInChildren<Collider>(true);
                            foreach (var collider3D in colliders3D)
                                if (collider3D != null) collider3D.enabled = false;
                            var colliders2D = target.GetComponentsInChildren<Collider2D>(true);
                            foreach (var collider2D in colliders2D)
                                if (collider2D != null) collider2D.enabled = false;
                        }
                    }
                }

                // Animator Event
                if (comps.IsAnimatorEvent && comps.AnimatorEvent != null)
                    StartCoroutine(RunRoutine(AnimatorEventRoutine(comps.AnimatorEvent, caller)));

                // Visual Fade
                if (comps.IsVisualFade && comps.VisualFade != null &&
                    comps.VisualFade.Fades != null)
                {
                    VisualFadeEntryData[] fades = comps.VisualFade.Fades;
                    for (int fadeIndex = 0; fadeIndex < fades.Length; fadeIndex++)
                    {
                        VisualFadeEntryData fade = fades[fadeIndex];
                        if (fade == null) continue;
                        var fadeState = new VisualFadeRuntimeState();
                        Action onCancelFade = () => { try { CleanupVisualFade(fadeState, true); } catch { } };
                        StartCoroutine(RunRoutine(
                            VisualFadeRoutine(fade, caller, fadeState),
                            onCancelFade));
                    }
                }

                // Light Tween
                if (comps.IsLightTween && comps.LightTween != null &&
                    comps.LightTween.Lights != null)
                {
                    LightTweenEntryData[] lightTweens = comps.LightTween.Lights;
                    for (int lightIndex = 0; lightIndex < lightTweens.Length; lightIndex++)
                    {
                        LightTweenEntryData lightTween = lightTweens[lightIndex];
                        if (lightTween == null) continue;
                        var lightState = new LightTweenRuntimeState();
                        Action onCancelLightTween = () =>
                        {
                            try { CleanupLightTween(lightState, true); } catch { }
                        };
                        StartCoroutine(RunRoutine(
                            LightTweenRoutine(lightTween, caller, lightState),
                            onCancelLightTween));
                    }
                }

                // Particle Event
                if (comps.IsParticleEvent && comps.ParticleEvent != null)
                    StartCoroutine(RunRoutine(
                        ParticleEventRoutine(comps.ParticleEvent, caller)));
            }

            // ── Transforms 그룹 ──
            if (step.Flags.IsTransforms && step.Transforms != null)
            {
                var transforms = step.Transforms;

                // MoveObject
                if (transforms.IsMoveObject && transforms.MoveObject != null && transforms.MoveObject.MoveObjects != null)
                {
                    foreach (var move in transforms.MoveObject.MoveObjects)
                    {
                        if (move == null) continue;
                        GameObject go = string.IsNullOrEmpty(move.objectName) ? caller : ResolveByName(move.objectName);
                        if (go == null && !string.IsNullOrEmpty(move.objectName)) { go = ResolveByName(move.objectName); if (go != null) AddToCacheIfNeeded(go); }
                        if (go == null) continue;

                        if (move.ShowLoadingScreen && !move.isDrawer)
                        {
                            GameObject loadingInstance = loadingScreenPrefab != null ? Instantiate(loadingScreenPrefab) : null;
                            if (loadingInstance == null)
                                Debug.LogWarning("맵 이동 로딩: LoadingScreen 프리팹이 연결되지 않았습니다.", this);
                            Action cleanup = () =>
                            {
                                if (loadingInstance == null) return;
                                loadingInstance.GetComponent<LoadingScreenCanvas>()?.ReleasePause();
                                Destroy(loadingInstance);
                            };
                            StartCoroutine(RunRoutine(MapMoveWithLoading(go, move, cleanup), cleanup));
                            continue;
                        }

                        if (move.isDrawer)
                        {
                            Vector3 offset = move.drawerOffset;
                            if (!drawerClosedLocal.ContainsKey(go)) drawerClosedLocal[go] = go.transform.localPosition;
                            bool same = drawerLastOffset.TryGetValue(go, out var last) && last == offset;
                            float dur = Mathf.Max(0f, move.Duration);

                            if (same)
                            {
                                Vector3 closed = drawerClosedLocal[go];
                                if (dur <= 0f) go.transform.localPosition = closed;
                                else { Vector3 goalLocal = closed; Action onCancel = () => { try { go.transform.localPosition = goalLocal; } catch { } }; StartCoroutine(RunRoutine(MoveRoutine(go, closed - go.transform.localPosition, dur, useLocal: true), onCancel)); }
                                drawerLastOffset.Remove(go); drawerClosedLocal.Remove(go);
                            }
                            else
                            {
                                Vector3 goalWorld = go.transform.position + go.transform.right * offset.x + go.transform.up * offset.y + go.transform.forward * offset.z;
                                if (dur <= 0f) go.transform.position = goalWorld;
                                else { Vector3 computedStart = go.transform.position; Vector3 goalPos = goalWorld; Action onCancel = () => { try { go.transform.position = goalPos; } catch { } }; StartCoroutine(RunRoutine(MoveRoutineCustom(go, computedStart, goalPos, dur, move, useLocal: false), onCancel)); }
                                drawerLastOffset[go] = offset;
                            }
                            continue;
                        }

                        float dur2 = Mathf.Max(0f, move.Duration);
                        Vector3 startPos;
                        if (move.IsAnotherStartPosition)
                        {
                            if (move.startIsRelative)
                            {
                                GameObject sBasis = null;
                                if (!string.IsNullOrEmpty(move.startTargetName)) sBasis = ResolveByName(move.startTargetName);
                                Transform sbt = (sBasis != null ? sBasis.transform : go.transform);
                                startPos = sbt.position + sbt.right * move.startPosition.x + sbt.up * move.startPosition.y + sbt.forward * move.startPosition.z;
                            }
                            else { startPos = move.startPosition; }
                            go.transform.position = startPos;
                        }
                        else { startPos = go.transform.position; }

                        Vector3 targetWorld;
                        if (move.isRelative)
                        {
                            GameObject basis = null;
                            if (!string.IsNullOrEmpty(move.targetName)) basis = ResolveByName(move.targetName);
                            Transform bt = (basis != null ? basis.transform : go.transform);
                            targetWorld = bt.position + bt.right * move.targetPosition.x + bt.up * move.targetPosition.y + bt.forward * move.targetPosition.z;
                        }
                        else { targetWorld = move.targetPosition; }

                        if (dur2 <= 0f) { go.transform.position = targetWorld; }
                        else { Vector3 goalPos = targetWorld; Action onCancel = () => { try { go.transform.position = goalPos; } catch { } }; StartCoroutine(RunRoutine(MoveRoutineCustom(go, startPos, goalPos, dur2, move, useLocal: false), onCancel)); }
                    }
                }

                // RotateObject
                if (transforms.IsRotateObject && transforms.RotateObject != null)
                {
                    var rd = transforms.RotateObject;
                    GameObject obj = string.IsNullOrEmpty(rd.ObjectName) ? caller : ResolveByName(rd.ObjectName);
                    if (obj == null && !string.IsNullOrEmpty(rd.ObjectName)) { obj = ResolveByName(rd.ObjectName); if (obj != null) AddToCacheIfNeeded(obj); }

                    if (obj != null)
                    {
                        float dur = Mathf.Max(0f, rd.Duration);
                        Transform t = obj.transform;

                        if (rd.isLookAt)
                        {
                            GameObject target = null;
                            if (!string.IsNullOrEmpty(rd.LookAtName)) { target = ResolveByName(rd.LookAtName); if (target != null) AddToCacheIfNeeded(target); }
                            if (target != null)
                            {
                                Quaternion goalRot = SafeLookRotation(target.transform.position - t.position, t.rotation);
                                if (dur <= 0f) { try { t.rotation = goalRot; } catch { } }
                                else { Action onCancel = () => { try { t.rotation = goalRot; } catch { } }; StartCoroutine(RunRoutine(RotateToRoutine(t, goalRot, dur), onCancel)); }
                            }
                        }
                        else if (rd.isDoor)
                        {
                            Vector3 offset = rd.doorEulerOffset;
                            Transform basisT = t;
                            if (rd.isRelative && !string.IsNullOrEmpty(rd.TargetName)) { var basisGO = ResolveByName(rd.TargetName); if (basisGO != null) basisT = basisGO.transform; }
                            Quaternion addRot = basisT != null
                                ? Quaternion.AngleAxis(offset.x, basisT.right) * Quaternion.AngleAxis(offset.y, basisT.up) * Quaternion.AngleAxis(offset.z, basisT.forward)
                                : Quaternion.Euler(offset);
                            bool alreadyOpened = openedDoors.Contains(obj);
                            Quaternion goal;
                            if (alreadyOpened) { goal = t.rotation * Quaternion.Inverse(addRot); openedDoors.Remove(obj); }
                            else { goal = t.rotation * addRot; openedDoors.Add(obj); }
                            if (dur <= 0f) { t.rotation = goal; }
                            else { Action onCancel = () => { try { t.rotation = goal; } catch { } }; StartCoroutine(RunRoutine(RotateToRoutine(t, goal, dur), onCancel)); }
                        }
                        else
                        {
                            Transform basisT = t;
                            if (rd.isRelative && !string.IsNullOrEmpty(rd.TargetName)) { var bGO = ResolveByName(rd.TargetName); if (bGO != null) basisT = bGO.transform; }
                            Quaternion delta = basisT != null
                                ? Quaternion.AngleAxis(rd.eulerAngles.x, basisT.right) * Quaternion.AngleAxis(rd.eulerAngles.y, basisT.up) * Quaternion.AngleAxis(rd.eulerAngles.z, basisT.forward)
                                : Quaternion.Euler(rd.eulerAngles);

                            if (rd.isLocal)
                            {
                                Quaternion goal = rd.isDelta ? (t.localRotation * delta) : delta;
                                if (dur <= 0f) { try { t.localRotation = goal; } catch { } }
                                else { Action onCancel = () => { try { t.localRotation = goal; } catch { } }; StartCoroutine(RunRoutine(RotateToRoutineLocal(t, goal, dur), onCancel)); }
                            }
                            else
                            {
                                Quaternion goal = rd.isDelta ? (t.rotation * delta) : delta;
                                if (dur <= 0f) { try { t.rotation = goal; } catch { } }
                                else { Action onCancel = () => { try { t.rotation = goal; } catch { } }; StartCoroutine(RunRoutine(RotateToRoutine(t, goal, dur), onCancel)); }
                            }
                        }
                    }
                }

                // Attach Object
                if (transforms.IsAttachObject && transforms.AttachObject != null &&
                    transforms.AttachObject.AttachObjects != null)
                {
                    AttachObjectEntryData[] attachObjects = transforms.AttachObject.AttachObjects;
                    for (int attachIndex = 0; attachIndex < attachObjects.Length; attachIndex++)
                    {
                        AttachObjectEntryData attach = attachObjects[attachIndex];
                        if (attach == null) continue;

                        string objectName = attach.ObjectName?.Trim() ?? string.Empty;
                        string parentName = attach.ParentName?.Trim() ?? string.Empty;
                        GameObject target = string.IsNullOrEmpty(objectName)
                            ? caller
                            : ResolveByName(objectName);
                        if (target == null)
                        {
                            Debug.LogWarning(
                                string.IsNullOrEmpty(objectName)
                                    ? "[EventPlayManager] Attach Object: 호출 오브젝트가 없습니다."
                                    : $"[EventPlayManager] Attach Object: 오브젝트 '{objectName}'를 찾지 못했습니다.");
                            continue;
                        }

                        GameObject parent = string.IsNullOrEmpty(parentName)
                            ? null
                            : ResolveByName(parentName);
                        if (!string.IsNullOrEmpty(parentName) && parent == null)
                        {
                            Debug.LogWarning($"[EventPlayManager] Attach Object: 부모 '{parentName}'를 찾지 못했습니다.");
                            continue;
                        }
                        if (parent == target ||
                            (parent != null && parent.transform.IsChildOf(target.transform)))
                        {
                            Debug.LogWarning($"[EventPlayManager] Attach Object: '{target.name}' 자신이나 자식에게 부모로 연결할 수 없습니다.");
                            continue;
                        }

                        try
                        {
                            target.transform.SetParent(
                                parent != null ? parent.transform : null,
                                attach.WorldPositionStays);
                            if (attach.ApplyLocalTransform)
                            {
                                target.transform.localPosition = attach.LocalPosition;
                                target.transform.localRotation = Quaternion.Euler(attach.LocalEulerAngles);
                                target.transform.localScale = attach.LocalScale;
                            }
                            AddToCacheIfNeeded(target);
                            if (parent != null) AddToCacheIfNeeded(parent);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning($"[EventPlayManager] Attach Object 적용 실패: {exception.Message}");
                        }
                    }
                }
            }

            // ── Speeches 그룹 ──
            if (step.Flags.IsSpeeches && step.Speeches != null)
            {
                var speeches = step.Speeches;

                // SoftSpeech
                if (speeches.IsSoftSpeech && speeches.SoftSpeech != null)
                {
                    var uiObj = GetOrCreateSingletonUI(softSpeechPrefab);
                    Action onCancel = () => { try { var eventUI = uiObj.GetComponentInChildren<IEventUI>(true) ?? uiObj.GetComponent<IEventUI>(); if (eventUI != null) eventUI.SetText(string.Empty); } catch { } try { if (uiObj != null) uiObj.SetActive(false); } catch { } };
                    StartCoroutine(RunRoutine(SoftSpeechRoutine(uiObj, speeches.SoftSpeech.SoftSpeechTexts, stepStartTime, speeches.SoftSpeech.isTyping), onCancel));
                }

                // HardSpeech
                if (speeches.IsHardSpeech && speeches.HardSpeech != null)
                {
                    var uiObj = GetOrCreateSingletonUI(hardSpeechPrefab);
                    Action onCancel = () => { try { var eventUI = uiObj.GetComponentInChildren<IEventUI>(true) ?? uiObj.GetComponent<IEventUI>(); if (eventUI != null) eventUI.SetText(string.Empty); } catch { } try { if (uiObj != null) uiObj.SetActive(false); } catch { } };
                    StartCoroutine(RunRoutine(HardSpeechRoutine(uiObj, speeches.HardSpeech.HardSpeechTexts, speeches.HardSpeech.HardSpeechKey, speeches.HardSpeech.IsTyping), onCancel));
                }

                // Portrait Speech
                if (speeches.IsPortraitSpeech && speeches.PortraitSpeech != null)
                {
                    var uiObj = GetOrCreateSingletonUI(portraitSpeechPrefab);
                    Action onCancelPortraitSpeech = () =>
                    {
                        try
                        {
                            var portraitCanvas = uiObj != null
                                ? uiObj.GetComponentInChildren<PortraitSpeechCanvas>(true) ?? uiObj.GetComponent<PortraitSpeechCanvas>()
                                : null;
                            portraitCanvas?.Clear();
                        }
                        catch { }
                        try { if (uiObj != null) uiObj.SetActive(false); } catch { }
                    };
                    StartCoroutine(RunRoutine(
                        PortraitSpeechRoutine(uiObj, speeches.PortraitSpeech),
                        onCancelPortraitSpeech));
                }

                // Speech Bubble
                if (speeches.IsSpeechBubble && speeches.SpeechBubble != null)
                {
                    var uiObj = GetOrCreateSingletonUI(speechBubblePrefab);
                    SpeechBubbleData speechBubble = speeches.SpeechBubble;
                    string targetName = speechBubble.GameObjectName?.Trim();
                    GameObject target = string.IsNullOrEmpty(targetName)
                        ? caller
                        : ResolveByName(targetName);
                    if (target != null) AddToCacheIfNeeded(target);

                    Action onCancelSpeechBubble = () =>
                    {
                        try
                        {
                            var bubbleCanvas = uiObj != null
                                ? uiObj.GetComponentInChildren<SpeechBubbleCanvas>(true) ?? uiObj.GetComponent<SpeechBubbleCanvas>()
                                : null;
                            bubbleCanvas?.Clear();
                        }
                        catch { }
                        try { if (uiObj != null) uiObj.SetActive(false); } catch { }
                    };
                    StartCoroutine(RunRoutine(
                        SpeechBubbleRoutine(uiObj, speechBubble, target),
                        onCancelSpeechBubble));
                }
            }

            // Tooltip (독립)
            if (step.Flags.IsTooltip && step.Tooltip != null)
            {
                var tooltipObject = GetOrCreateSingletonUI(tooltipPrefab);
                Action onCancelTooltip = () =>
                {
                    try
                    {
                        var eventUI = tooltipObject != null
                            ? tooltipObject.GetComponentInChildren<IEventUI>(true) ?? tooltipObject.GetComponent<IEventUI>()
                            : null;
                        if (eventUI != null) eventUI.SetText(string.Empty);
                    }
                    catch { }
                    ClearTooltipTracking(tooltipObject);
                    try { if (tooltipObject != null) tooltipObject.SetActive(false); } catch { }
                };
                StartCoroutine(RunRoutine(TooltipRoutine(tooltipObject, step.Tooltip), onCancelTooltip));
            }

            // Black Label (독립)
            if (step.Flags.IsBlackLabel && step.BlackLabel != null)
            {
                var blackLabelObject = GetOrCreateSingletonUI(blackLabelPrefab);
                Action onCancelBlackLabel = () =>
                {
                    try { if (blackLabelObject != null) blackLabelObject.SetActive(false); } catch { }
                };
                StartCoroutine(RunRoutine(
                    BlackLabelRoutine(blackLabelObject, step.BlackLabel),
                    onCancelBlackLabel));
            }

            // Screen Flash (독립)
            if (step.Flags.IsScreenFlash && step.ScreenFlash != null)
            {
                var screenFlashObject = GetOrCreateSingletonUI(screenFlashPrefab);
                Action onCancelScreenFlash = () =>
                {
                    try
                    {
                        var screenFlashCanvas = screenFlashObject != null
                            ? screenFlashObject.GetComponentInChildren<ScreenFlashCanvas>(true) ?? screenFlashObject.GetComponent<ScreenFlashCanvas>()
                            : null;
                        screenFlashCanvas?.Clear();
                    }
                    catch { }
                    try { if (screenFlashObject != null) screenFlashObject.SetActive(false); } catch { }
                };
                StartCoroutine(RunRoutine(
                    ScreenFlashRoutine(screenFlashObject, step.ScreenFlash),
                    onCancelScreenFlash));
            }

            if (step.Flags.IsJustText && step.JustText != null)
            {
                var uiObj = GetOrCreateSingletonUI(memoPrefab);
                if (uiObj != null)
                {
                    var eventUI = uiObj.GetComponentInChildren<IEventUI>(true) ?? uiObj.GetComponent<IEventUI>();
                    if (eventUI != null) { uiObj.SetActive(true); eventUI.SetText(step.JustText.Text ?? string.Empty); }
                }
            }

            // UpdateComponent (Components 그룹)
            if (step.Flags.IsComponents && step.Components != null && step.Components.IsUpdateComponent && step.Components.UpdateComponent != null)
            {
                var ucDatas = step.Components.UpdateComponent.UpdateComponents;
                if (ucDatas != null)
                {
                    foreach (var uc in ucDatas)
                    {
                        if (uc == null || string.IsNullOrEmpty(uc.componentName) || string.IsNullOrEmpty(uc.propertyName)) continue;
                        GameObject target = string.IsNullOrEmpty(uc.gameObjectName) ? caller : ResolveByName(uc.gameObjectName);
                        if (target == null) continue;
                        Component comp = null;
                        foreach (var c in target.GetComponents<Component>())
                        {
                            if (c == null) continue;
                            var t = c.GetType();
                            if (t.Name == uc.componentName || t.FullName == uc.componentName) { comp = c; break; }
                        }
                        if (comp == null) { Debug.LogWarning($"[EventPlayManager] UpdateComponent: component '{uc.componentName}' not found on '{target.name}'"); continue; }
                        var propInfo = comp.GetType().GetProperty(uc.propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var fieldInfo = propInfo == null ? comp.GetType().GetField(uc.propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
                        if (propInfo == null && fieldInfo == null) { Debug.LogWarning($"[EventPlayManager] UpdateComponent: property/field '{uc.propertyName}' not found on '{uc.componentName}'"); continue; }
                        try
                        {
                            object val = null;
                            switch (uc.valueType)
                            {
                                case EventSO.ValueType.Int: val = uc.intValue; break;
                                case EventSO.ValueType.Float: val = uc.floatValue; break;
                                case EventSO.ValueType.Bool: val = uc.boolValue; break;
                                case EventSO.ValueType.String: val = uc.stringValue ?? string.Empty; break;
                                case EventSO.ValueType.GameObject: val = uc.gameObjectValue; break;
                            }
                            if (propInfo != null && propInfo.CanWrite) propInfo.SetValue(comp, val);
                            else if (fieldInfo != null) fieldInfo.SetValue(comp, val);
                        }
                        catch (System.Exception ex) { Debug.LogWarning($"[EventPlayManager] UpdateComponent: failed to set '{uc.propertyName}' on '{uc.componentName}' - {ex.Message}"); }
                    }
                }
            }

            // Wait (독립)
            if (step.Flags.IsWait && step.Wait != null && step.Wait.WaitTime > 0f)
                StartCoroutine(RunRoutine(WaitRoutine(step.Wait.WaitTime)));

            // Wait Until Condition (독립)
            if (step.Flags.IsWaitUntilCondition && step.WaitUntilCondition != null)
                StartCoroutine(RunRoutine(
                    WaitUntilConditionRoutine(step.WaitUntilCondition, caller)));

            // ── Cameras 그룹 ──
            if (step.Flags.IsCameras && step.Cameras != null)
            {
                var cameras = step.Cameras;

                // CameraMovement
                if (cameras.IsCameraMove && cameras.CameraMovement != null)
                {
                    bool useMainCamera = cameras.CameraMovement.IsCameraMoveUseMain;
                    var mainCameraDepthState = useMainCamera
                        ? new MainCameraDepthRuntimeState()
                        : null;
                    var eventCameraUsageState = useMainCamera
                        ? null
                        : new EventCameraUsageRuntimeState();
                    Action onCancelCam = useMainCamera
                        ? () => { try { ReleaseMainCameraDepth(mainCameraDepthState); } catch { } }
                        : () => { try { ReleaseEventCameraUsage(eventCameraUsageState); } catch { } };
                    StartCoroutine(RunRoutine(
                        CameraMoveRoutine(
                            cameras.CameraMovement,
                            stepStartTime,
                            cameras.CameraMovement.IsCameraMoveUseMain,
                            mainCameraDepthState,
                            eventCameraUsageState),
                        onCancelCam));
                }

                // CameraAim
                if (cameras.IsCameraAiming && cameras.CameraAim != null)
                {
                    bool useMainCamera = cameras.CameraAim.IsCameraAimingUseMain;
                    var mainCameraDepthState = useMainCamera
                        ? new MainCameraDepthRuntimeState()
                        : null;
                    var eventCameraUsageState = useMainCamera
                        ? null
                        : new EventCameraUsageRuntimeState();
                    Action onCancelCam = useMainCamera
                        ? () => { try { ReleaseMainCameraDepth(mainCameraDepthState); } catch { } }
                        : () => { try { ReleaseEventCameraUsage(eventCameraUsageState); } catch { } };
                    StartCoroutine(RunRoutine(
                        CameraLookAtRoutine(
                            cameras.CameraAim,
                            stepStartTime,
                            cameras.CameraAim.IsCameraAimingUseMain,
                            mainCameraDepthState,
                            eventCameraUsageState),
                        onCancelCam));
                }

                // Camera Shake
                if (cameras.IsCameraShake && cameras.CameraShake != null)
                {
                    var shakeState = new CameraShakeRuntimeState();
                    Action onCancelShake = () => { try { CleanupCameraShake(shakeState); } catch { } };
                    StartCoroutine(RunRoutine(
                        CameraShakeRoutine(cameras.CameraShake, shakeState),
                        onCancelShake));
                }

                // Camera Lens
                if (cameras.IsCameraLens && cameras.CameraLens != null)
                {
                    var lensState = new CameraLensRuntimeState();
                    Action onCancelLens = () => { try { CleanupCameraLens(lensState, true); } catch { } };
                    StartCoroutine(RunRoutine(
                        CameraLensRoutine(cameras.CameraLens, lensState),
                        onCancelLens));
                }
            }

            // Lock (독립)
            if (step.Flags.IsLock && step.Lock != null)
            {
                if (step.Lock.IsLockKeyboard) { lockKeyboardCount++; isLockKeyboard = true; context.AcquiredKeyboardLocks++; }
                if (step.Lock.IsLockMouse) { lockMouseCount++; isLockMouse = true; context.AcquiredMouseLocks++; }
            }

            // ── Scenes 그룹 ──
            if (step.Flags.IsScenes && step.Scenes != null)
            {
                var scenes = step.Scenes;

                // SceneAdd
                if (scenes.IsSceneAdd && scenes.SceneAdd != null && scenes.SceneAdd.Scenes != null)
                {
                    for (int i = 0; i < scenes.SceneAdd.Scenes.Length; i++)
                    {
                        var sceneNameToLoad = scenes.SceneAdd.Scenes[i];
                        if (string.IsNullOrEmpty(sceneNameToLoad)) continue;
                        var asyncOp = SceneManager.LoadSceneAsync(sceneNameToLoad, LoadSceneMode.Additive);
                        if (asyncOp != null) { while (!asyncOp.isDone) yield return null; }
                        else { SceneManager.LoadScene(sceneNameToLoad, LoadSceneMode.Additive); yield return null; }
                    }
                }

                // ScenePause
                if (scenes.IsScenePause && scenes.ScenePause != null && scenes.ScenePause.Scenes != null)
                {
                    for (int i = 0; i < scenes.ScenePause.Scenes.Length; i++)
                    {
                        string targetSceneName = String.IsNullOrEmpty(scenes.ScenePause.Scenes[i]) ? (caller != null ? caller.scene.name : string.Empty) : scenes.ScenePause.Scenes[i];
                        PauseSceneByNameOrCaller(targetSceneName, caller, context);
                    }
                }

                // SceneUnPause
                if (scenes.IsSceneUnPause && scenes.SceneUnPause != null && scenes.SceneUnPause.Scenes != null)
                {
                    for (int i = 0; i < scenes.SceneUnPause.Scenes.Length; i++)
                    {
                        string targetSceneName = String.IsNullOrEmpty(scenes.SceneUnPause.Scenes[i]) ? (caller != null ? caller.scene.name : string.Empty) : scenes.SceneUnPause.Scenes[i];
                        ResumeSceneByNameOrCaller(targetSceneName, caller);
                    }
                }

                // SetActiveScene
                if (scenes.IsSetActiveScene && scenes.SetActiveScene != null)
                {
                    Scene targetScene = !String.IsNullOrEmpty(scenes.SetActiveScene.Scene) ? SceneManager.GetSceneByName(scenes.SetActiveScene.Scene) : (caller != null ? caller.scene : default);
                    if (targetScene.IsValid() && targetScene.isLoaded) SceneManager.SetActiveScene(targetScene);
                }

                // SceneChange
                if (scenes.IsSceneChange && scenes.SceneChange != null)
                {
                    SceneManager.LoadScene(scenes.SceneChange.Scene);
                    yield break;
                }

                // SceneOff
                if (scenes.IsSceneOff && scenes.SceneOff != null && scenes.SceneOff.Scenes != null)
                {
                    for (int i = 0; i < scenes.SceneOff.Scenes.Length; i++)
                    {
                        string targetSceneName = !String.IsNullOrEmpty(scenes.SceneOff.Scenes[i]) ? scenes.SceneOff.Scenes[i] : (caller != null ? caller.scene.name : string.Empty);
                        if (string.IsNullOrEmpty(targetSceneName)) continue;
                        var async = SceneManager.UnloadSceneAsync(targetSceneName);
                        if (async != null) { while (!async.isDone) yield return null; }
                    }
                }
            }

            // Sound (독립)
            if (step.Flags.IsSound && step.Sound != null)
                StartCoroutine(RunRoutine(PlaySoundRoutine(step.Sound)));

            // Fade (독립)
            if (step.Flags.IsFadePlay && step.FadeInfo != null)
                StartCoroutine(RunRoutine(FadeScheduleRoutine(step.FadeInfo, stepStartRealtime)));

            // Time Scale (독립)
            if (step.Flags.IsTimeScale && step.TimeScale != null)
            {
                float previousTimeScale = Time.timeScale;
                Time.timeScale = Mathf.Clamp(step.TimeScale.TargetScale, 0f, 10f);
                Action onCancelTimeScale = step.TimeScale.RestoreAfterDuration
                    ? () => { try { Time.timeScale = previousTimeScale; } catch { } }
                    : null;
                StartCoroutine(RunRoutine(
                    TimeScaleRoutine(step.TimeScale, previousTimeScale),
                    onCancelTimeScale));
            }

            // Cursor (독립)
            if (step.Flags.IsCursorVisible && step.Cursor != null)
            {
                if (step.Cursor.isCursorVisible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }

            while (activeRoutines > 0)
                yield return null;

            if (context != null && context.IsCancelled)
                yield break;

            // Choice (독립)
            if (step.Flags.IsChoice && step.Choice != null)
            {
                Action onCancelChoice = () => { try { CleanupChoiceUIIfAny(); } catch { } try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; isLockKeyboard = false; isLockMouse = false; } catch { } };
                StartCoroutine(RunRoutine(ChoiceRoutine(step.Choice, caller), onCancelChoice));
                while (activeRoutines > 0) yield return null;
                if (context != null && context.IsCancelled) yield break;
            }

            // Action (독립)
            if (context != null && context.IsCancelled) yield break;
            if (step.Flags.IsAction && step.Action != null)
            {
                string actionName = step.Action.ActionName;
                if (!string.IsNullOrEmpty(actionName) && actionMap.TryGetValue(actionName, out var action))
                {
                    try { action(); } catch (System.Exception ex) { Debug.LogWarning($"[EventPlayManager] Action '{actionName}' failed: {ex.Message}"); }
                }
#if UNITY_EDITOR
                else if (!string.IsNullOrEmpty(actionName)) { Debug.LogWarning($"[EventPlayManager] Action '{actionName}' not found in actionMap."); }
#endif
            }

            // EventExe (독립)
            if (context != null && context.IsCancelled) yield break;
            if (step.Flags.IsEventExe && step.EventExe != null)
            {
                var exe = step.EventExe;
                EventSO.EventStep chosenExeStep = null;
                if (exe != null && !exe.UseCondition)
                {
                    if (exe.ConditionSteps != null && exe.ConditionSteps.Length > 0) chosenExeStep = exe.ConditionSteps[0];
                }
                else
                {
                    int exeMatchedIndex = -1;
                    if (exe.Conditions != null && exe.Conditions.Length > 0)
                    {
                        for (int ei = 0; ei < exe.Conditions.Length; ei++) { if (EvaluateConditionGroup(exe.Conditions[ei], caller)) { exeMatchedIndex = ei; break; } }
                    }
                    int condCountExe = exe.Conditions != null ? exe.Conditions.Length : 0;
                    if (exeMatchedIndex >= 0) { if (exe.ConditionSteps != null && exeMatchedIndex < exe.ConditionSteps.Length) chosenExeStep = exe.ConditionSteps[exeMatchedIndex]; }
                    else { if (exe.ConditionSteps != null && exe.ConditionSteps.Length > condCountExe) chosenExeStep = exe.ConditionSteps[condCountExe]; }
                }
                if (chosenExeStep != null) yield return ProcessStep(chosenExeStep, caller, context);
                yield break;
            }

            DisableEventCameraIfUnused();
            yield break;
        }

        private IEnumerator ProcessEventConcurrently(EventSO eventSO, EventSO.EventStep chosenStep, float eventStartTime, GameObject caller, EventContext context)
        {
            if (eventSO == null) { CleanupEventTracking((eventSO, caller)); yield break; }
            var pair = (eventSO, caller);
            var step = chosenStep;
            if (step == null) { CleanupEventTracking(pair); yield break; }
            try { yield return ProcessStep(step, caller, context); }
            finally { CleanupEventTracking(pair); }
        }

        private void CleanupEventTracking((EventSO, GameObject) pair)
        {
            if (runningContexts.TryGetValue(pair, out var ctx) && ctx != null)
            {
                if (ctx.AcquiredKeyboardLocks > 0) { lockKeyboardCount = Math.Max(0, lockKeyboardCount - ctx.AcquiredKeyboardLocks); isLockKeyboard = lockKeyboardCount > 0; }
                if (ctx.AcquiredMouseLocks > 0) { lockMouseCount = Math.Max(0, lockMouseCount - ctx.AcquiredMouseLocks); isLockMouse = lockMouseCount > 0; }
            }
            runningEventPairs.Remove(pair); runningCoroutines.Remove(pair); runningContexts.Remove(pair);
        }

        private void PauseSceneByNameOrCaller(string sceneName, GameObject caller, EventContext context)
        {
            Scene scene;
            if (!string.IsNullOrEmpty(sceneName)) scene = SceneManager.GetSceneByName(sceneName);
            else scene = caller != null ? caller.scene : default;
            if (!scene.IsValid() || !scene.isLoaded) return;
            var key = scene.handle.ToString();
            if (!pausedSceneRoots.TryGetValue(key, out var list) || list == null) { list = new List<GameObject>(); pausedSceneRoots[key] = list; }
            if (list.Count > 0) return;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) { var go = roots[i]; if (go == null || !go.activeSelf) continue; list.Add(go); go.SetActive(false); }
            if (context != null) { string capturedKey = key; context.AddOnCancel(() => ResumePausedSceneByKey(capturedKey)); }
        }

        private void ResumeSceneByNameOrCaller(string sceneName, GameObject caller)
        {
            Scene scene;
            if (!string.IsNullOrEmpty(sceneName)) scene = SceneManager.GetSceneByName(sceneName);
            else scene = caller != null ? caller.scene : default;
            if (!scene.IsValid()) return;
            ResumePausedSceneByKey(scene.handle.ToString());
        }

        private void ResumePausedSceneByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!pausedSceneRoots.TryGetValue(key, out var list) || list == null) return;
            for (int i = 0; i < list.Count; i++) { var go = list[i]; if (go != null) go.SetActive(true); }
            list.Clear();
        }

        private void ResumeAllPausedScenes()
        {
            foreach (var kv in pausedSceneRoots) { var list = kv.Value; if (list == null) continue; for (int i = 0; i < list.Count; i++) { var go = list[i]; if (go != null) go.SetActive(true); } list.Clear(); }
            pausedSceneRoots.Clear();
        }

        private IEnumerator WaitRoutine(float seconds) { if (seconds <= 0f) yield break; yield return new WaitForSecondsRealtime(seconds); }

        private IEnumerator WaitUntilConditionRoutine(
            WaitUntilConditionData waitData,
            GameObject caller)
        {
            if (waitData == null || waitData.Condition == null)
            {
                Debug.LogWarning("[EventPlayManager] Wait Until Condition에 Condition 데이터가 없습니다.");
                yield break;
            }

            float timeout = Mathf.Max(0f, waitData.Timeout);
            float elapsed = 0f;
            while (!EvaluateConditionGroup(waitData.Condition, caller))
            {
                if (timeout > 0f && elapsed >= timeout)
                {
                    Debug.LogWarning($"[EventPlayManager] Wait Until Condition이 {timeout:0.###}초 안에 충족되지 않아 다음 Phase로 진행합니다.");
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator TimeScaleRoutine(TimeScaleData timeScale, float previousTimeScale)
        {
            if (timeScale == null) yield break;

            try
            {
                float duration = Mathf.Max(0f, timeScale.Duration);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            finally
            {
                if (timeScale.RestoreAfterDuration)
                    Time.timeScale = previousTimeScale;
            }
        }

        private IEnumerator AnimatorEventRoutine(AnimatorEventData animatorEvent, GameObject caller)
        {
            if (animatorEvent == null) yield break;

            string locator = animatorEvent.ObjectName?.Trim() ?? string.Empty;
            GameObject target = string.IsNullOrEmpty(locator) ? caller : ResolveByName(locator);
            if (target == null)
            {
                Debug.LogWarning(
                    string.IsNullOrEmpty(locator)
                        ? "[EventPlayManager] Animator Event: 호출 오브젝트가 없습니다."
                        : $"[EventPlayManager] Animator Event: 오브젝트 '{locator}'를 찾지 못했습니다.");
                yield break;
            }

            AddToCacheIfNeeded(target);
            Animator animator = target.GetComponent<Animator>() ??
                                target.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning($"[EventPlayManager] Animator Event: '{target.name}'에 Animator가 없습니다.");
                yield break;
            }

            string stateOrParameter = animatorEvent.StateOrParameter?.Trim() ?? string.Empty;
            bool requiresName = animatorEvent.Command != AnimatorCommand.SetSpeed;
            if (requiresName && string.IsNullOrEmpty(stateOrParameter))
            {
                Debug.LogWarning($"[EventPlayManager] Animator Event: {animatorEvent.Command}에 필요한 State/Parameter 이름이 비어 있습니다.");
                yield break;
            }

            bool usesState = animatorEvent.Command == AnimatorCommand.PlayState ||
                             animatorEvent.Command == AnimatorCommand.CrossFade;
            if (usesState &&
                (animatorEvent.Layer < -1 ||
                 (animatorEvent.Layer >= 0 && animatorEvent.Layer >= animator.layerCount)))
            {
                Debug.LogWarning($"[EventPlayManager] Animator Event: Layer {animatorEvent.Layer}가 '{target.name}' Animator 범위를 벗어났습니다.");
                yield break;
            }

            if (TryGetExpectedAnimatorParameterType(animatorEvent.Command, out AnimatorControllerParameterType expectedType))
            {
                if (!TryGetAnimatorParameterType(animator, stateOrParameter, out AnimatorControllerParameterType actualType))
                {
                    Debug.LogWarning($"[EventPlayManager] Animator Event: '{target.name}'에 Parameter '{stateOrParameter}'가 없습니다.");
                    yield break;
                }

                if (actualType != expectedType)
                {
                    Debug.LogWarning($"[EventPlayManager] Animator Event: Parameter '{stateOrParameter}' 타입은 {actualType}이며 {expectedType} 명령과 맞지 않습니다.");
                    yield break;
                }
            }

            try
            {
                switch (animatorEvent.Command)
                {
                    case AnimatorCommand.PlayState:
                        animator.Play(
                            stateOrParameter,
                            animatorEvent.Layer,
                            Mathf.Clamp01(animatorEvent.NormalizedTime));
                        break;
                    case AnimatorCommand.CrossFade:
                        animator.CrossFade(
                            stateOrParameter,
                            Mathf.Max(0f, animatorEvent.TransitionDuration),
                            animatorEvent.Layer,
                            Mathf.Clamp01(animatorEvent.NormalizedTime));
                        break;
                    case AnimatorCommand.SetTrigger:
                        animator.SetTrigger(stateOrParameter);
                        break;
                    case AnimatorCommand.ResetTrigger:
                        animator.ResetTrigger(stateOrParameter);
                        break;
                    case AnimatorCommand.SetBool:
                        animator.SetBool(stateOrParameter, animatorEvent.BoolValue);
                        break;
                    case AnimatorCommand.SetInteger:
                        animator.SetInteger(stateOrParameter, animatorEvent.IntValue);
                        break;
                    case AnimatorCommand.SetFloat:
                        animator.SetFloat(stateOrParameter, animatorEvent.FloatValue);
                        break;
                    case AnimatorCommand.SetSpeed:
                        animator.speed = animatorEvent.FloatValue;
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[EventPlayManager] Animator Event 적용 실패: {exception.Message}");
                yield break;
            }

            float duration = Mathf.Max(0f, animatorEvent.Duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static bool TryGetExpectedAnimatorParameterType(
            AnimatorCommand command,
            out AnimatorControllerParameterType parameterType)
        {
            switch (command)
            {
                case AnimatorCommand.SetTrigger:
                case AnimatorCommand.ResetTrigger:
                    parameterType = AnimatorControllerParameterType.Trigger;
                    return true;
                case AnimatorCommand.SetBool:
                    parameterType = AnimatorControllerParameterType.Bool;
                    return true;
                case AnimatorCommand.SetInteger:
                    parameterType = AnimatorControllerParameterType.Int;
                    return true;
                case AnimatorCommand.SetFloat:
                    parameterType = AnimatorControllerParameterType.Float;
                    return true;
                default:
                    parameterType = default;
                    return false;
            }
        }

        private static bool TryGetAnimatorParameterType(
            Animator animator,
            string parameterName,
            out AnimatorControllerParameterType parameterType)
        {
            if (animator != null)
            {
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    AnimatorControllerParameter parameter = parameters[i];
                    if (!string.Equals(parameter.name, parameterName, StringComparison.Ordinal)) continue;
                    parameterType = parameter.type;
                    return true;
                }
            }

            parameterType = default;
            return false;
        }

        private IEnumerator VisualFadeRoutine(
            VisualFadeEntryData fade,
            GameObject caller,
            VisualFadeRuntimeState state)
        {
            if (fade == null || state == null) yield break;

            string locator = fade.ObjectName?.Trim() ?? string.Empty;
            GameObject target = string.IsNullOrEmpty(locator) ? caller : ResolveByName(locator);
            if (target == null)
            {
                Debug.LogWarning(
                    string.IsNullOrEmpty(locator)
                        ? "[EventPlayManager] Visual Fade: 호출 오브젝트가 없습니다."
                        : $"[EventPlayManager] Visual Fade: 오브젝트 '{locator}'를 찾지 못했습니다.");
                yield break;
            }

            AddToCacheIfNeeded(target);
            PrepareVisualFadeTargets(target, fade.IncludeChildren, state);
            if (!HasVisualFadeTargets(state))
            {
                Debug.LogWarning($"[EventPlayManager] Visual Fade: '{target.name}'에서 CanvasGroup, UI Graphic, SpriteRenderer 또는 Alpha Color를 지원하는 Renderer를 찾지 못했습니다.");
                yield break;
            }

            state.TargetAlpha = Mathf.Clamp01(fade.TargetAlpha);
            float duration = Mathf.Max(0f, fade.Duration);
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(elapsed / duration);
                    if (fade.EaseInOut)
                        progress = Mathf.SmoothStep(0f, 1f, progress);
                    ApplyVisualFade(state, progress);
                    yield return null;
                }
            }
            finally
            {
                CleanupVisualFade(state, true);
            }
        }

        private static void PrepareVisualFadeTargets(
            GameObject target,
            bool includeChildren,
            VisualFadeRuntimeState state)
        {
            CanvasGroup[] canvasGroups = includeChildren
                ? target.GetComponentsInChildren<CanvasGroup>(true)
                : target.GetComponents<CanvasGroup>();
            var canvasGroupTransforms = new HashSet<Transform>();
            for (int i = 0; i < canvasGroups.Length; i++)
                if (canvasGroups[i] != null)
                    canvasGroupTransforms.Add(canvasGroups[i].transform);

            var selectedCanvasGroupTransforms = new HashSet<Transform>();
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                CanvasGroup canvasGroup = canvasGroups[i];
                if (canvasGroup == null || selectedCanvasGroupTransforms.Contains(canvasGroup.transform))
                    continue;

                bool hasGroupAncestor = false;
                Transform ancestor = canvasGroup.transform.parent;
                while (ancestor != null)
                {
                    if (canvasGroupTransforms.Contains(ancestor))
                    {
                        hasGroupAncestor = true;
                        break;
                    }
                    if (ancestor == target.transform) break;
                    ancestor = ancestor.parent;
                }
                if (hasGroupAncestor) continue;

                selectedCanvasGroupTransforms.Add(canvasGroup.transform);
                state.CanvasGroups[canvasGroup] = canvasGroup.alpha;
            }

            Graphic[] graphics = includeChildren
                ? target.GetComponentsInChildren<Graphic>(true)
                : target.GetComponents<Graphic>();
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null ||
                    IsUnderSelectedCanvasGroup(graphic.transform, target.transform, selectedCanvasGroupTransforms))
                    continue;
                state.Graphics[graphic] = graphic.color;
            }

            SpriteRenderer[] sprites = includeChildren
                ? target.GetComponentsInChildren<SpriteRenderer>(true)
                : target.GetComponents<SpriteRenderer>();
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer sprite = sprites[i];
                if (sprite != null) state.Sprites[sprite] = sprite.color;
            }

            Renderer[] renderers = includeChildren
                ? target.GetComponentsInChildren<Renderer>(true)
                : target.GetComponents<Renderer>();
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || renderer is SpriteRenderer) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;

                    int propertyId;
                    if (material.HasProperty(BaseColorPropertyId))
                        propertyId = BaseColorPropertyId;
                    else if (material.HasProperty(ColorPropertyId))
                        propertyId = ColorPropertyId;
                    else
                        continue;

                    var propertyBlock = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(propertyBlock, materialIndex);
                    Color startColor = propertyBlock.HasColor(propertyId)
                        ? propertyBlock.GetColor(propertyId)
                        : material.GetColor(propertyId);
                    state.Renderers.Add(new RendererFadeTarget
                    {
                        Renderer = renderer,
                        MaterialIndex = materialIndex,
                        ColorPropertyId = propertyId,
                        StartColor = startColor,
                        PropertyBlock = propertyBlock
                    });
                }
            }
        }

        private static bool IsUnderSelectedCanvasGroup(
            Transform current,
            Transform targetRoot,
            HashSet<Transform> selectedCanvasGroupTransforms)
        {
            while (current != null)
            {
                if (selectedCanvasGroupTransforms.Contains(current)) return true;
                if (current == targetRoot) break;
                current = current.parent;
            }
            return false;
        }

        private static bool HasVisualFadeTargets(VisualFadeRuntimeState state)
        {
            return state != null &&
                   (state.CanvasGroups.Count > 0 || state.Graphics.Count > 0 ||
                    state.Sprites.Count > 0 || state.Renderers.Count > 0);
        }

        private static void ApplyVisualFade(VisualFadeRuntimeState state, float progress)
        {
            if (state == null) return;
            float targetAlpha = state.TargetAlpha;

            foreach (KeyValuePair<CanvasGroup, float> entry in state.CanvasGroups)
                if (entry.Key != null)
                    entry.Key.alpha = Mathf.Lerp(entry.Value, targetAlpha, progress);

            foreach (KeyValuePair<Graphic, Color> entry in state.Graphics)
            {
                if (entry.Key == null) continue;
                Color color = entry.Key.color;
                color.a = Mathf.Lerp(entry.Value.a, targetAlpha, progress);
                entry.Key.color = color;
            }

            foreach (KeyValuePair<SpriteRenderer, Color> entry in state.Sprites)
            {
                if (entry.Key == null) continue;
                Color color = entry.Key.color;
                color.a = Mathf.Lerp(entry.Value.a, targetAlpha, progress);
                entry.Key.color = color;
            }

            for (int i = 0; i < state.Renderers.Count; i++)
            {
                RendererFadeTarget entry = state.Renderers[i];
                if (entry == null || entry.Renderer == null || entry.PropertyBlock == null) continue;
                entry.Renderer.GetPropertyBlock(entry.PropertyBlock, entry.MaterialIndex);
                Color color = entry.PropertyBlock.HasColor(entry.ColorPropertyId)
                    ? entry.PropertyBlock.GetColor(entry.ColorPropertyId)
                    : entry.StartColor;
                color.a = Mathf.Lerp(entry.StartColor.a, targetAlpha, progress);
                entry.PropertyBlock.SetColor(entry.ColorPropertyId, color);
                entry.Renderer.SetPropertyBlock(entry.PropertyBlock, entry.MaterialIndex);
            }
        }

        private static void CleanupVisualFade(VisualFadeRuntimeState state, bool applyTarget)
        {
            if (state == null || state.Cleaned) return;
            if (applyTarget) ApplyVisualFade(state, 1f);
            state.Cleaned = true;
        }

        private IEnumerator LightTweenRoutine(
            LightTweenEntryData lightTween,
            GameObject caller,
            LightTweenRuntimeState state)
        {
            if (lightTween == null || state == null) yield break;

            string locator = lightTween.ObjectName?.Trim() ?? string.Empty;
            GameObject target = string.IsNullOrEmpty(locator) ? caller : ResolveByName(locator);
            if (target == null)
            {
                Debug.LogWarning(
                    string.IsNullOrEmpty(locator)
                        ? "[EventPlayManager] Light Tween: 호출 오브젝트가 없습니다."
                        : $"[EventPlayManager] Light Tween: 오브젝트 '{locator}'를 찾지 못했습니다.");
                yield break;
            }

            state.AffectColor = lightTween.AffectColor;
            state.AffectIntensity = lightTween.AffectIntensity;
            state.AffectRange = lightTween.AffectRange;
            state.TargetColor = lightTween.TargetColor;
            state.TargetIntensity = Mathf.Max(0f, lightTween.TargetIntensity);
            state.TargetRange = Mathf.Max(0f, lightTween.TargetRange);

            if (!state.AffectColor && !state.AffectIntensity && !state.AffectRange)
            {
                Debug.LogWarning("[EventPlayManager] Light Tween: 변경할 Color, Intensity, Range가 모두 꺼져 있습니다.");
                yield break;
            }

            Light[] lights = lightTween.IncludeChildren
                ? target.GetComponentsInChildren<Light>(true)
                : target.GetComponents<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || state.Lights.ContainsKey(light)) continue;
                state.Lights.Add(light, new LightTweenStart
                {
                    Color = light.color,
                    Intensity = light.intensity,
                    Range = light.range
                });
            }

            if (state.Lights.Count == 0)
            {
                Debug.LogWarning($"[EventPlayManager] Light Tween: '{target.name}'에서 Light를 찾지 못했습니다.");
                yield break;
            }

            AddToCacheIfNeeded(target);
            float duration = Mathf.Max(0f, lightTween.Duration);
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(elapsed / duration);
                    if (lightTween.EaseInOut)
                        progress = Mathf.SmoothStep(0f, 1f, progress);
                    ApplyLightTween(state, progress);
                    yield return null;
                }
            }
            finally
            {
                CleanupLightTween(state, true);
            }
        }

        private static void ApplyLightTween(LightTweenRuntimeState state, float progress)
        {
            if (state == null) return;
            foreach (KeyValuePair<Light, LightTweenStart> entry in state.Lights)
            {
                Light light = entry.Key;
                LightTweenStart start = entry.Value;
                if (light == null || start == null) continue;
                if (state.AffectColor)
                    light.color = Color.Lerp(start.Color, state.TargetColor, progress);
                if (state.AffectIntensity)
                    light.intensity = Mathf.Lerp(start.Intensity, state.TargetIntensity, progress);
                if (state.AffectRange)
                    light.range = Mathf.Lerp(start.Range, state.TargetRange, progress);
            }
        }

        private static void CleanupLightTween(LightTweenRuntimeState state, bool applyTarget)
        {
            if (state == null || state.Cleaned) return;
            if (applyTarget) ApplyLightTween(state, 1f);
            state.Cleaned = true;
        }

        private IEnumerator ParticleEventRoutine(ParticleEventData particleEvent, GameObject caller)
        {
            if (particleEvent == null) yield break;

            string locator = particleEvent.ObjectName?.Trim() ?? string.Empty;
            GameObject target = string.IsNullOrEmpty(locator) ? caller : ResolveByName(locator);
            if (target == null)
            {
                Debug.LogWarning(
                    string.IsNullOrEmpty(locator)
                        ? "[EventPlayManager] Particle Event: 호출 오브젝트가 없습니다."
                        : $"[EventPlayManager] Particle Event: 오브젝트 '{locator}'를 찾지 못했습니다.");
                yield break;
            }

            ParticleSystem[] particleSystems = particleEvent.IncludeChildren
                ? target.GetComponentsInChildren<ParticleSystem>(true)
                : target.GetComponents<ParticleSystem>();
            if (particleSystems.Length == 0)
            {
                Debug.LogWarning($"[EventPlayManager] Particle Event: '{target.name}'에서 ParticleSystem을 찾지 못했습니다.");
                yield break;
            }

            AddToCacheIfNeeded(target);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null) continue;
                switch (particleEvent.Command)
                {
                    case ParticleCommand.Pause:
                        particleSystem.Pause(false);
                        break;
                    case ParticleCommand.StopEmitting:
                        particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                        break;
                    case ParticleCommand.StopAndClear:
                        particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        break;
                    case ParticleCommand.Clear:
                        particleSystem.Clear(false);
                        break;
                    default:
                        particleSystem.Play(false);
                        break;
                }
            }

            float duration = Mathf.Max(0f, particleEvent.Duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator HardSpeechRoutine(GameObject uiObj, IReadOnlyList<string> texts, KeyCode advanceKey, bool isTyping)
        {
            if (uiObj == null) yield break;
            if (texts == null || texts.Count == 0) yield break;
            var eventUI = uiObj.GetComponentInChildren<IEventUI>(true) ?? uiObj.GetComponent<IEventUI>();
            if (eventUI == null) { Debug.LogWarning("[EventPlayManager] HardSpeech UI에 IEventUI가 없습니다."); yield break; }
            if (advanceKey == KeyCode.None) advanceKey = KeyCode.E;
            if (!EventInputReader.IsSupported(advanceKey))
            {
                Debug.LogWarning($"[EventPlayManager] 현재 입력 설정에서 HardSpeech 키 '{advanceKey}'를 읽을 수 없어 E 키로 진행합니다.");
                advanceKey = KeyCode.E;
            }
            if (!EventInputReader.IsSupported(advanceKey))
            {
                Debug.LogError("[EventPlayManager] HardSpeech 진행 키를 읽을 수 있는 입력 모듈이 없습니다.");
                yield break;
            }
            if (!uiObj.activeSelf) uiObj.SetActive(true);
            const float typingInterval = 0.05f;
            for (int idx = 0; idx < texts.Count; idx++)
            {
                string fullText = texts[idx] ?? string.Empty;
                if (isTyping) { while (EventInputReader.TryIsPressed(advanceKey, out bool held) && held) yield return null; eventUI.SetText(string.Empty); bool forcedShow = false; for (int i = 1; i <= fullText.Length; i++) { eventUI.SetText(fullText.Substring(0, i)); if (i == fullText.Length) break; float elapsed = 0f; while (elapsed < typingInterval) { if (EventInputReader.TryWasPressedThisFrame(advanceKey, out bool down) && down) { eventUI.SetText(fullText); forcedShow = true; break; } elapsed += Time.unscaledDeltaTime; yield return null; } if (forcedShow) break; } if (fullText.Length == 0) eventUI.SetText(string.Empty); }
                else { eventUI.SetText(fullText); }
                while (EventInputReader.TryIsPressed(advanceKey, out bool heldAfterText) && heldAfterText) yield return null;
                while (true) { if (EventInputReader.TryWasPressedThisFrame(advanceKey, out bool pressed) && pressed) break; yield return null; }
            }
            eventUI.SetText(string.Empty); uiObj.SetActive(false);
        }

        private IEnumerator SoftSpeechRoutine(GameObject uiObj, IReadOnlyList<SpeechData> speeches, float stepStartTime, bool isTyping)
        {
            if (uiObj == null) yield break;
            if (speeches == null || speeches.Count == 0) yield break;
            var eventUI = uiObj.GetComponentInChildren<IEventUI>(true) ?? uiObj.GetComponent<IEventUI>();
            if (eventUI == null) { Debug.LogWarning("[EventPlayManager] SoftSpeech UI에 IEventUI가 없습니다."); yield break; }
            if (!uiObj.activeSelf) uiObj.SetActive(true);
            const float typingInterval = 0.05f;
            for (int idx = 0; idx < speeches.Count; idx++)
            {
                var cur = speeches[idx]; if (cur == null) continue;
                float duration = Mathf.Max(0.0001f, cur.Duration); string fullText = cur.Text ?? string.Empty;
                if (isTyping) { eventUI.SetText(string.Empty); float elapsedTotal = 0f; int lastShown = 0; while (elapsedTotal < duration) { float sub = 0f; while (sub < typingInterval && elapsedTotal < duration) { sub += Time.unscaledDeltaTime; elapsedTotal += Time.unscaledDeltaTime; yield return null; } if (lastShown < fullText.Length) { lastShown = Math.Min(lastShown + 1, fullText.Length); eventUI.SetText(fullText.Substring(0, lastShown)); } } }
                else { eventUI.SetText(fullText); float elapsed = 0f; while (elapsed < duration) { elapsed += Time.unscaledDeltaTime; yield return null; } }
                eventUI.SetText(string.Empty);
            }
            uiObj.SetActive(false);
        }

        private IEnumerator SpeechBubbleRoutine(
            GameObject uiObj,
            SpeechBubbleData speechBubble,
            GameObject target)
        {
            if (uiObj == null)
            {
                Debug.LogWarning("[EventPlayManager] Speech Bubble 프리팹이 연결되지 않았습니다.");
                yield break;
            }
            if (speechBubble == null || speechBubble.SpeechBubbleTexts == null ||
                speechBubble.SpeechBubbleTexts.Count == 0)
            {
                if (uiObj.activeSelf) uiObj.SetActive(false);
                yield break;
            }

            SpeechBubbleCanvas bubbleCanvas =
                uiObj.GetComponentInChildren<SpeechBubbleCanvas>(true) ??
                uiObj.GetComponent<SpeechBubbleCanvas>();
            if (bubbleCanvas == null)
            {
                Debug.LogWarning("[EventPlayManager] Speech Bubble 프리팹에 SpeechBubbleCanvas가 없습니다.");
                if (uiObj.activeSelf) uiObj.SetActive(false);
                yield break;
            }

            if (target == null)
            {
                string targetDescription = string.IsNullOrWhiteSpace(speechBubble.GameObjectName)
                    ? "호출 오브젝트"
                    : $"'{speechBubble.GameObjectName}'";
                Debug.LogWarning($"[EventPlayManager] Speech Bubble 대상 {targetDescription}를 찾을 수 없습니다.");
                bubbleCanvas.Clear();
                if (uiObj.activeSelf) uiObj.SetActive(false);
                yield break;
            }

            Canvas rootCanvas = uiObj.GetComponent<Canvas>() ?? uiObj.GetComponentInChildren<Canvas>(true);
            SpeechBubbleTargetRuntimeState targetState = CreateSpeechBubbleTargetState(target);
            Camera projectionCamera = GetSpeechBubbleProjectionCamera(rootCanvas, targetState.LayerMask);
            if (projectionCamera == null)
            {
                Debug.LogWarning($"[EventPlayManager] Speech Bubble 대상 '{target.name}'을 표시할 Game 카메라를 찾을 수 없습니다.");
                bubbleCanvas.Clear();
                if (uiObj.activeSelf) uiObj.SetActive(false);
                yield break;
            }

            if (!uiObj.activeSelf) uiObj.SetActive(true);
            const float typingInterval = 0.05f;
            try
            {
                for (int lineIndex = 0; lineIndex < speechBubble.SpeechBubbleTexts.Count; lineIndex++)
                {
                    SpeechData line = speechBubble.SpeechBubbleTexts[lineIndex];
                    if (line == null) continue;

                    string fullText = line.Text ?? string.Empty;
                    float duration = Mathf.Max(0f, line.Duration);
                    bubbleCanvas.PrepareLine(fullText);
                    bubbleCanvas.SetText(speechBubble.isTyping ? string.Empty : fullText);

                    if (!TryUpdateSpeechBubblePosition(bubbleCanvas, targetState, ref projectionCamera))
                    {
                        Debug.LogWarning($"[EventPlayManager] Speech Bubble 대상 '{target.name}'을 화면 좌표로 변환할 수 없습니다.");
                        yield break;
                    }

                    float elapsed = 0f;
                    float typingElapsed = 0f;
                    int shownCharacters = 0;
                    while (elapsed < duration)
                    {
                        if (target == null)
                        {
                            Debug.LogWarning("[EventPlayManager] Speech Bubble 표시 중 대상 오브젝트가 사라졌습니다.");
                            yield break;
                        }

                        if (!TryUpdateSpeechBubblePosition(bubbleCanvas, targetState, ref projectionCamera))
                        {
                            bubbleCanvas.Clear();
                            yield break;
                        }
                        float delta = Time.unscaledDeltaTime;
                        elapsed += delta;

                        if (speechBubble.isTyping && shownCharacters < fullText.Length)
                        {
                            typingElapsed += delta;
                            while (typingElapsed >= typingInterval && shownCharacters < fullText.Length)
                            {
                                typingElapsed -= typingInterval;
                                shownCharacters++;
                            }
                            bubbleCanvas.SetText(fullText.Substring(0, shownCharacters));
                        }

                        yield return null;
                    }

                    bubbleCanvas.Clear();
                }
            }
            finally
            {
                bubbleCanvas.Clear();
                if (uiObj != null) uiObj.SetActive(false);
            }
        }

        private bool TryUpdateSpeechBubblePosition(
            SpeechBubbleCanvas bubbleCanvas,
            SpeechBubbleTargetRuntimeState targetState,
            ref Camera projectionCamera)
        {
            if (bubbleCanvas == null || targetState == null || targetState.Target == null) return false;

            Canvas canvas = bubbleCanvas.GetComponent<Canvas>() ??
                            bubbleCanvas.GetComponentInParent<Canvas>();
            projectionCamera = GetSpeechBubbleProjectionCamera(canvas, targetState.LayerMask);
            if (projectionCamera == null) return false;

            return bubbleCanvas.TrySetTargetWorldPosition(
                GetSpeechBubbleWorldAnchor(targetState),
                projectionCamera);
        }

        private Camera GetSpeechBubbleProjectionCamera(Canvas canvas, int targetLayerMask)
        {
            int cameraCount = Camera.allCamerasCount;
            if (cameraCount <= 0) return null;
            if (speechBubbleCameraBuffer.Length < cameraCount)
                speechBubbleCameraBuffer = new Camera[cameraCount];

            int foundCount = Camera.GetAllCameras(speechBubbleCameraBuffer);
            Camera bestCamera = null;
            for (int i = 0; i < foundCount; i++)
            {
                Camera candidate = speechBubbleCameraBuffer[i];
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    candidate.cameraType != CameraType.Game || candidate.targetTexture != null)
                    continue;
                if (canvas != null && candidate.targetDisplay != canvas.targetDisplay)
                    continue;
                if (targetLayerMask != 0 && (candidate.cullingMask & targetLayerMask) == 0)
                    continue;
                if (bestCamera == null || candidate.depth > bestCamera.depth)
                    bestCamera = candidate;
            }

            return bestCamera;
        }

        private static SpeechBubbleTargetRuntimeState CreateSpeechBubbleTargetState(GameObject target)
        {
            var state = new SpeechBubbleTargetRuntimeState { Target = target };
            if (target == null) return state;

            state.Renderers = target.GetComponentsInChildren<Renderer>(true);
            state.Colliders2D = target.GetComponentsInChildren<Collider2D>(true);
            state.Colliders = target.GetComponentsInChildren<Collider>(true);
            Transform[] transforms = target.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform child = transforms[i];
                if (child != null) state.LayerMask |= 1 << child.gameObject.layer;
            }
            state.LocalFallbackAnchor = target.transform.InverseTransformPoint(
                GetSpeechBubbleColliderAnchor(state, target.transform.position));
            return state;
        }

        private static Vector3 GetSpeechBubbleWorldAnchor(SpeechBubbleTargetRuntimeState targetState)
        {
            if (targetState == null || targetState.Target == null) return Vector3.zero;

            bool hasBounds = false;
            Bounds combinedBounds = default;
            Renderer[] renderers = targetState.Renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer ||
                    (renderer is SpriteRenderer spriteRenderer && spriteRenderer.sprite == null) ||
                    renderer.bounds.size.sqrMagnitude <= Mathf.Epsilon)
                    continue;
                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            Vector3 anchor = hasBounds
                ? new Vector3(combinedBounds.center.x, combinedBounds.max.y, combinedBounds.center.z)
                : targetState.Target.transform.TransformPoint(targetState.LocalFallbackAnchor);
            return anchor;
        }

        private static Vector3 GetSpeechBubbleColliderAnchor(
            SpeechBubbleTargetRuntimeState targetState,
            Vector3 fallback)
        {
            bool hasBounds = false;
            Bounds combinedBounds = default;
            Collider2D[] colliders2D = targetState.Colliders2D;
            for (int i = 0; i < colliders2D.Length; i++)
            {
                Collider2D collider = colliders2D[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;
                if (!hasBounds)
                {
                    combinedBounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
            {
                Collider[] colliders = targetState.Colliders;
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                        continue;
                    if (!hasBounds)
                    {
                        combinedBounds = collider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(collider.bounds);
                    }
                }
            }

            return hasBounds
                ? new Vector3(combinedBounds.center.x, combinedBounds.max.y, combinedBounds.center.z)
                : fallback;
        }

        private IEnumerator PortraitSpeechRoutine(
            GameObject uiObj,
            PortraitSpeechData portraitSpeech)
        {
            if (uiObj == null || portraitSpeech == null) yield break;

            PortraitSpeechCanvas portraitCanvas =
                uiObj.GetComponentInChildren<PortraitSpeechCanvas>(true) ??
                uiObj.GetComponent<PortraitSpeechCanvas>();
            if (portraitCanvas == null)
            {
                Debug.LogWarning("[EventPlayManager] Portrait Speech 프리팹에 PortraitSpeechCanvas가 없습니다.");
                yield break;
            }

            portraitCanvas.Clear();
            if (portraitSpeech.Lines == null || portraitSpeech.Lines.Length == 0)
            {
                Debug.LogWarning("[EventPlayManager] Portrait Speech에 표시할 Line이 없습니다.");
                if (uiObj.activeSelf) uiObj.SetActive(false);
                yield break;
            }

            KeyCode advanceKey = portraitSpeech.AdvanceKey;
            if (portraitSpeech.AdvanceMode == PortraitAdvanceMode.Input)
            {
                if (advanceKey == KeyCode.None) advanceKey = KeyCode.E;
                if (!EventInputReader.IsSupported(advanceKey))
                {
                    Debug.LogWarning($"[EventPlayManager] 현재 입력 설정에서 Portrait Speech 키 '{advanceKey}'를 읽을 수 없어 E 키로 진행합니다.");
                    advanceKey = KeyCode.E;
                }
                if (!EventInputReader.IsSupported(advanceKey))
                {
                    Debug.LogError("[EventPlayManager] Portrait Speech 진행 키를 읽을 수 있는 입력 모듈이 없습니다.");
                    if (uiObj.activeSelf) uiObj.SetActive(false);
                    yield break;
                }
            }

            if (!uiObj.activeSelf) uiObj.SetActive(true);
            try
            {
                for (int lineIndex = 0; lineIndex < portraitSpeech.Lines.Length; lineIndex++)
                {
                    PortraitSpeechLineData line = portraitSpeech.Lines[lineIndex];
                    if (line == null) continue;

                    portraitCanvas.SetLine(line.Portrait, line.SpeakerName);
                    string fullText = line.Text ?? string.Empty;

                    if (portraitSpeech.AdvanceMode == PortraitAdvanceMode.Input)
                    {
                        if (portraitSpeech.IsTyping)
                            yield return TypePortraitTextWithInput(portraitCanvas, fullText, advanceKey);
                        else
                            portraitCanvas.SetText(fullText);

                        while (EventInputReader.TryIsPressed(advanceKey, out bool held) && held)
                            yield return null;
                        while (true)
                        {
                            if (EventInputReader.TryWasPressedThisFrame(advanceKey, out bool pressed) && pressed)
                                break;
                            yield return null;
                        }
                    }
                    else
                    {
                        yield return ShowTimedPortraitText(
                            portraitCanvas,
                            fullText,
                            Mathf.Max(0f, line.Duration),
                            portraitSpeech.IsTyping);
                    }
                }
            }
            finally
            {
                portraitCanvas.Clear();
                if (uiObj != null) uiObj.SetActive(false);
            }
        }

        private IEnumerator TypePortraitTextWithInput(
            PortraitSpeechCanvas portraitCanvas,
            string fullText,
            KeyCode advanceKey)
        {
            const float typingInterval = 0.05f;
            while (EventInputReader.TryIsPressed(advanceKey, out bool held) && held)
                yield return null;

            portraitCanvas.SetText(string.Empty);
            bool forcedShow = false;
            for (int characterCount = 1; characterCount <= fullText.Length; characterCount++)
            {
                portraitCanvas.SetText(fullText.Substring(0, characterCount));
                if (characterCount == fullText.Length) break;

                float elapsed = 0f;
                while (elapsed < typingInterval)
                {
                    if (EventInputReader.TryWasPressedThisFrame(advanceKey, out bool pressed) && pressed)
                    {
                        portraitCanvas.SetText(fullText);
                        forcedShow = true;
                        break;
                    }
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (forcedShow) break;
            }

            if (fullText.Length == 0)
                portraitCanvas.SetText(string.Empty);
        }

        private IEnumerator ShowTimedPortraitText(
            PortraitSpeechCanvas portraitCanvas,
            string fullText,
            float duration,
            bool isTyping)
        {
            if (!isTyping || duration <= 0f || fullText.Length == 0)
            {
                portraitCanvas.SetText(fullText);
                float plainElapsed = 0f;
                while (plainElapsed < duration)
                {
                    plainElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                yield break;
            }

            portraitCanvas.SetText(string.Empty);
            float typingDuration = Mathf.Min(duration, fullText.Length * 0.05f);
            float elapsed = 0f;
            int shownCharacters = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                int desiredCharacters = elapsed >= typingDuration
                    ? fullText.Length
                    : Mathf.Clamp(
                        Mathf.FloorToInt(elapsed / typingDuration * fullText.Length),
                        0,
                        fullText.Length);
                if (desiredCharacters != shownCharacters)
                {
                    shownCharacters = desiredCharacters;
                    portraitCanvas.SetText(fullText.Substring(0, shownCharacters));
                }
                yield return null;
            }
            portraitCanvas.SetText(fullText);
        }

        private IEnumerator TooltipRoutine(GameObject tooltipObject, TooltipData tooltip)
        {
            if (tooltipObject == null || tooltip == null) yield break;

            var eventUI = tooltipObject.GetComponentInChildren<IEventUI>(true) ??
                          tooltipObject.GetComponent<IEventUI>();
            if (eventUI == null)
            {
                Debug.LogWarning("[EventPlayManager] Tooltip 프리팹에 IEventUI가 없습니다.");
                yield break;
            }

            eventUI.SetText(tooltip.Content ?? string.Empty);
            if (!tooltipObject.activeSelf) tooltipObject.SetActive(true);
            Canvas.ForceUpdateCanvases();

            GameObject centerObject = tooltip.IsRelative &&
                                      !string.IsNullOrWhiteSpace(tooltip.CenterObject)
                ? ResolveByName(tooltip.CenterObject)
                : null;
            activeTooltipObject = tooltipObject;
            activeTooltipData = tooltip;
            activeTooltipCenterObject = centerObject;
            ApplyTooltipPosition(tooltipObject, tooltip, centerObject, true);

            // 이벤트를 시작한 입력이 같은 프레임에 툴팁까지 닫지 않게 한다.
            yield return null;

            if (tooltip.isBlocked)
            {
                while (true)
                {
                    if (!EventInputReader.TryWasAnyKeyboardOrMousePressedThisFrame(out bool pressed))
                    {
                        Debug.LogError("[EventPlayManager] Tooltip 진행에 필요한 키보드/마우스 입력을 읽을 수 없습니다.");
                        break;
                    }
                    if (pressed) break;
                    yield return null;
                }
            }
            else
            {
                float duration = Mathf.Max(0f, tooltip.Duration);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            ClearTooltipTracking(tooltipObject);
            eventUI.SetText(string.Empty);
            tooltipObject.SetActive(false);
        }

        private IEnumerator BlackLabelRoutine(
            GameObject blackLabelObject,
            BlackLabelData blackLabel)
        {
            if (blackLabelObject == null || blackLabel == null) yield break;

            if (!blackLabelObject.activeSelf) blackLabelObject.SetActive(true);

            float duration = Mathf.Max(0f, blackLabel.Duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            blackLabelObject.SetActive(false);
        }

        private IEnumerator ScreenFlashRoutine(
            GameObject screenFlashObject,
            ScreenFlashData screenFlash)
        {
            if (screenFlashObject == null || screenFlash == null) yield break;

            ScreenFlashCanvas screenFlashCanvas =
                screenFlashObject.GetComponentInChildren<ScreenFlashCanvas>(true) ??
                screenFlashObject.GetComponent<ScreenFlashCanvas>();
            if (screenFlashCanvas == null)
            {
                Debug.LogWarning("[EventPlayManager] Screen Flash 프리팹에 ScreenFlashCanvas가 없습니다.");
                yield break;
            }

            screenFlashCanvas.Clear();
            screenFlashCanvas.SetColor(screenFlash.FlashColor);
            if (!screenFlashObject.activeSelf) screenFlashObject.SetActive(true);

            float peakAlpha = Mathf.Clamp01(screenFlash.PeakAlpha);
            try
            {
                yield return AnimateScreenFlashAlpha(
                    screenFlashCanvas,
                    0f,
                    peakAlpha,
                    Mathf.Max(0f, screenFlash.FadeInDuration));

                float holdDuration = Mathf.Max(0f, screenFlash.Duration);
                float holdElapsed = 0f;
                while (holdElapsed < holdDuration)
                {
                    holdElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                yield return AnimateScreenFlashAlpha(
                    screenFlashCanvas,
                    peakAlpha,
                    0f,
                    Mathf.Max(0f, screenFlash.FadeOutDuration));
            }
            finally
            {
                screenFlashCanvas.Clear();
                if (screenFlashObject != null) screenFlashObject.SetActive(false);
            }
        }

        private IEnumerator AnimateScreenFlashAlpha(
            ScreenFlashCanvas screenFlashCanvas,
            float from,
            float to,
            float duration)
        {
            if (screenFlashCanvas == null) yield break;
            if (duration <= 0f)
            {
                screenFlashCanvas.SetAlpha(to);
                yield break;
            }

            screenFlashCanvas.SetAlpha(from);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                screenFlashCanvas.SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            screenFlashCanvas.SetAlpha(to);
        }

        private void ApplyTooltipPosition(
            GameObject tooltipObject,
            TooltipData tooltip,
            GameObject centerObject,
            bool logFailure)
        {
            if (tooltipObject == null || tooltip == null) return;

            Canvas canvas = tooltipObject.GetComponent<Canvas>() ??
                            tooltipObject.GetComponentInChildren<Canvas>(true);
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            RectTransform contentRect = FindTooltipContentRect(tooltipObject, canvasRect);
            if (canvasRect == null || contentRect == null)
            {
                if (logFailure)
                    Debug.LogWarning("[EventPlayManager] Tooltip의 Canvas 또는 배치할 UI 패널을 찾을 수 없어 기존 위치를 유지합니다.");
                return;
            }

            Vector2 canvasLocalPosition = canvasRect.rect.center + tooltip.Position;
            if (tooltip.IsRelative)
            {
                Camera worldCamera = GetTooltipProjectionCamera(canvas, centerObject);
                if (centerObject != null && worldCamera != null)
                {
                    Transform centerTransform = centerObject.transform;
                    Vector3 worldPosition = centerTransform.position +
                                            centerTransform.right * tooltip.Position.x +
                                            centerTransform.up * tooltip.Position.y;
                    Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);
                    Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                        ? null
                        : canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
                    if (screenPosition.z > 0f &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            canvasRect,
                            screenPosition,
                            uiCamera,
                            out Vector2 targetCanvasPosition))
                    {
                        canvasLocalPosition = targetCanvasPosition;
                    }
                    else if (logFailure)
                    {
                        Debug.LogWarning($"[EventPlayManager] Tooltip 기준 오브젝트 '{tooltip.CenterObject}'를 화면 좌표로 변환할 수 없어 Position을 절대 좌표로 사용합니다.");
                    }
                }
                else if (logFailure)
                {
                    Debug.LogWarning($"[EventPlayManager] Tooltip 기준 오브젝트 '{tooltip.CenterObject}' 또는 표시 카메라를 찾을 수 없어 Position을 절대 좌표로 사용합니다.");
                }
            }

            contentRect.position = canvasRect.TransformPoint(canvasLocalPosition);
        }

        private Camera GetTooltipProjectionCamera(Canvas canvas, GameObject centerObject)
        {
            int cameraCount = Camera.allCamerasCount;
            if (cameraCount <= 0) return null;
            if (tooltipCameraBuffer.Length < cameraCount)
                tooltipCameraBuffer = new Camera[cameraCount];

            int foundCount = Camera.GetAllCameras(tooltipCameraBuffer);
            Camera bestCamera = null;
            for (int i = 0; i < foundCount; i++)
            {
                Camera candidate = tooltipCameraBuffer[i];
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    candidate.cameraType != CameraType.Game || candidate.targetTexture != null)
                    continue;
                if (canvas != null && candidate.targetDisplay != canvas.targetDisplay)
                    continue;
                if (centerObject != null &&
                    (candidate.cullingMask & (1 << centerObject.layer)) == 0)
                    continue;
                if (bestCamera == null || candidate.depth > bestCamera.depth)
                    bestCamera = candidate;
            }

            return bestCamera;
        }

        private void ClearTooltipTracking(GameObject tooltipObject = null)
        {
            if (tooltipObject != null && activeTooltipObject != tooltipObject) return;
            activeTooltipObject = null;
            activeTooltipData = null;
            activeTooltipCenterObject = null;
        }

        private static RectTransform FindTooltipContentRect(GameObject tooltipObject, RectTransform canvasRect)
        {
            if (tooltipObject == null) return null;

            Text text = tooltipObject.GetComponentInChildren<Text>(true);
            Transform current = text != null ? text.transform.parent : null;
            while (current != null && current != canvasRect)
            {
                Image image = current.GetComponent<Image>();
                if (image != null) return image.rectTransform;
                current = current.parent;
            }

            if (canvasRect != null)
            {
                for (int i = 0; i < canvasRect.childCount; i++)
                {
                    RectTransform childRect = canvasRect.GetChild(i) as RectTransform;
                    if (childRect != null) return childRect;
                }
            }

            return null;
        }

        private IEnumerator CameraShakeRoutine(
            CameraShakeData cameraShake,
            CameraShakeRuntimeState state)
        {
            if (cameraShake == null || state == null) yield break;

            Camera camera;
            if (cameraShake.UseMainCamera)
            {
                camera = Camera.main;
            }
            else
            {
                GameObject eventCameraObject = GetOrCreateEventCamera();
                camera = eventCameraObject != null ? eventCameraObject.GetComponent<Camera>() : null;
                if (camera != null)
                {
                    bool wasUnused = eventCameraUserCount == 0;
                    if (wasUnused)
                    {
                        eventCameraPrevDepth = camera.depth;
                        eventCameraDepthStored = true;
                        camera.depth = 20f;
                        Camera mainCamera = Camera.main;
                        if (mainCamera != null)
                        {
                            eventCameraObject.transform.position = mainCamera.transform.position;
                            eventCameraObject.transform.rotation = mainCamera.transform.rotation;
                        }
                    }

                    eventCameraUserCount++;
                    eventCameraObject.SetActive(true);
                    state.UsesEventCamera = true;
                    state.EventCameraObject = eventCameraObject;
                    state.EventCameraComponent = camera;
                }
            }

            if (camera == null)
            {
                Debug.LogWarning(
                    cameraShake.UseMainCamera
                        ? "[EventPlayManager] Camera Shake: Main Camera를 찾지 못했습니다."
                        : "[EventPlayManager] Camera Shake: EventCamera를 준비하지 못했습니다.");
                yield break;
            }

            state.Target = camera.transform;
            float duration = Mathf.Max(0f, cameraShake.Duration);
            float frequency = Mathf.Max(0f, cameraShake.Frequency);
            float seed = Time.realtimeSinceStartup * 0.731f + 11.17f;
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    RemoveAppliedCameraShake(state);

                    float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                    float strength = cameraShake.FadeOut ? 1f - progress : 1f;
                    float noiseTime = Time.realtimeSinceStartup * frequency;
                    Vector3 positionNoise = new Vector3(
                        SignedPerlin(seed + 1.17f, noiseTime),
                        SignedPerlin(seed + 3.41f, noiseTime),
                        SignedPerlin(seed + 5.93f, noiseTime));
                    Vector3 rotationNoise = new Vector3(
                        SignedPerlin(seed + 7.13f, noiseTime),
                        SignedPerlin(seed + 9.67f, noiseTime),
                        SignedPerlin(seed + 12.31f, noiseTime));

                    state.AppliedPosition = Vector3.Scale(positionNoise, cameraShake.PositionStrength) * strength;
                    state.AppliedRotation = Quaternion.Euler(
                        Vector3.Scale(rotationNoise, cameraShake.RotationStrength) * strength);
                    state.BasePositionAtApply = state.Target.localPosition;
                    state.BaseRotationAtApply = state.Target.localRotation;
                    state.Target.localPosition = state.BasePositionAtApply + state.AppliedPosition;
                    state.Target.localRotation = state.BaseRotationAtApply * state.AppliedRotation;
                    state.HasAppliedOffset = true;

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            finally
            {
                CleanupCameraShake(state);
            }
        }

        private static float SignedPerlin(float seed, float time)
        {
            return Mathf.PerlinNoise(seed, time) * 2f - 1f;
        }

        private static void RemoveAppliedCameraShake(CameraShakeRuntimeState state)
        {
            if (state == null || !state.HasAppliedOffset) return;
            if (state.Target != null)
            {
                Vector3 expectedPosition = state.BasePositionAtApply + state.AppliedPosition;
                Quaternion expectedRotation = state.BaseRotationAtApply * state.AppliedRotation;

                // 같은 프레임의 Move/Aim 또는 게임 카메라 스크립트가 이미 새 값을 썼다면
                // 그 값을 이전 Shake 오프셋으로 다시 보정하지 않는다.
                if ((state.Target.localPosition - expectedPosition).sqrMagnitude <= 0.00000001f)
                    state.Target.localPosition = state.BasePositionAtApply;
                if (Quaternion.Angle(state.Target.localRotation, expectedRotation) <= 0.001f)
                    state.Target.localRotation = state.BaseRotationAtApply;
            }

            state.AppliedPosition = Vector3.zero;
            state.AppliedRotation = Quaternion.identity;
            state.HasAppliedOffset = false;
        }

        private void CleanupCameraShake(CameraShakeRuntimeState state)
        {
            if (state == null || state.Cleaned) return;
            RemoveAppliedCameraShake(state);

            if (state.UsesEventCamera)
            {
                eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1);
                if (eventCameraUserCount == 0)
                {
                    if (eventCameraDepthStored && state.EventCameraComponent != null)
                        state.EventCameraComponent.depth = eventCameraPrevDepth;
                    eventCameraDepthStored = false;
                    if (state.EventCameraObject != null && state.EventCameraObject.activeSelf)
                        state.EventCameraObject.SetActive(false);
                }
            }

            state.Cleaned = true;
        }

        private IEnumerator CameraLensRoutine(
            CameraLensData cameraLens,
            CameraLensRuntimeState state)
        {
            if (cameraLens == null || state == null) yield break;

            Camera camera = null;
            if (cameraLens.UseMainCamera)
            {
                camera = Camera.main;
            }
            else
            {
                GameObject eventCameraObject = GetOrCreateEventCamera();
                camera = eventCameraObject != null ? eventCameraObject.GetComponent<Camera>() : null;
                if (camera != null)
                {
                    if (eventCameraUserCount == 0)
                    {
                        eventCameraPrevDepth = camera.depth;
                        eventCameraDepthStored = true;
                        camera.depth = 20f;

                        Camera mainCamera = Camera.main;
                        if (mainCamera != null)
                        {
                            eventCameraObject.transform.position = mainCamera.transform.position;
                            eventCameraObject.transform.rotation = mainCamera.transform.rotation;
                        }
                    }

                    eventCameraUserCount++;
                    eventCameraObject.SetActive(true);
                    state.UsesEventCamera = true;
                    state.EventCameraObject = eventCameraObject;
                }
            }

            if (camera == null)
            {
                Debug.LogWarning(
                    cameraLens.UseMainCamera
                        ? "[EventPlayManager] Camera Lens: Main Camera를 찾지 못했습니다."
                        : "[EventPlayManager] Camera Lens: EventCamera를 준비하지 못했습니다.");
                yield break;
            }

            state.Target = camera;
            state.UsesOrthographicSize = camera.orthographic;
            state.TargetValue = state.UsesOrthographicSize
                ? Mathf.Max(0.0001f, cameraLens.TargetOrthographicSize)
                : Mathf.Clamp(cameraLens.TargetFieldOfView, 1f, 179f);

            float startValue = state.UsesOrthographicSize
                ? camera.orthographicSize
                : camera.fieldOfView;
            float duration = Mathf.Max(0f, cameraLens.Duration);
            float elapsed = 0f;
            try
            {
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(elapsed / duration);
                    if (cameraLens.EaseInOut)
                        progress = Mathf.SmoothStep(0f, 1f, progress);
                    ApplyCameraLensValue(state, Mathf.Lerp(startValue, state.TargetValue, progress));
                    yield return null;
                }
            }
            finally
            {
                CleanupCameraLens(state, true);
            }
        }

        private static void ApplyCameraLensValue(CameraLensRuntimeState state, float value)
        {
            if (state == null || state.Target == null) return;
            if (state.UsesOrthographicSize)
                state.Target.orthographicSize = Mathf.Max(0.0001f, value);
            else
                state.Target.fieldOfView = Mathf.Clamp(value, 1f, 179f);
        }

        private void CleanupCameraLens(CameraLensRuntimeState state, bool applyTarget)
        {
            if (state == null || state.Cleaned) return;

            if (applyTarget)
                ApplyCameraLensValue(state, state.TargetValue);

            if (state.UsesEventCamera)
            {
                eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1);
                if (eventCameraUserCount == 0)
                {
                    if (eventCameraDepthStored && state.Target != null)
                        state.Target.depth = eventCameraPrevDepth;
                    eventCameraDepthStored = false;
                    if (state.EventCameraObject != null && state.EventCameraObject.activeSelf)
                        state.EventCameraObject.SetActive(false);
                }
            }

            state.Cleaned = true;
        }

        private void AcquireMainCameraDepth(Camera camera, MainCameraDepthRuntimeState state)
        {
            if (camera == null || state == null || state.Acquired || state.Released) return;

            MainCameraDepthUsage usage;
            if (!mainCameraDepthUsages.TryGetValue(camera, out usage))
            {
                usage = new MainCameraDepthUsage
                {
                    Count = 0,
                    PreviousDepth = camera.depth
                };
                mainCameraDepthUsages.Add(camera, usage);
            }

            usage.Count++;
            camera.depth = 20f;
            state.Target = camera;
            state.Acquired = true;
        }

        private void ReleaseMainCameraDepth(MainCameraDepthRuntimeState state)
        {
            if (state == null || state.Released) return;
            state.Released = true;
            if (!state.Acquired || ReferenceEquals(state.Target, null)) return;

            Camera target = state.Target;
            MainCameraDepthUsage usage;
            if (!mainCameraDepthUsages.TryGetValue(target, out usage)) return;

            usage.Count = Mathf.Max(0, usage.Count - 1);
            if (usage.Count > 0) return;

            if (target != null)
                target.depth = usage.PreviousDepth;
            mainCameraDepthUsages.Remove(target);
        }

        private void AcquireEventCameraUsage(
            GameObject cameraObject,
            Camera camera,
            EventCameraUsageRuntimeState state)
        {
            if (cameraObject == null || camera == null || state == null || state.Acquired || state.Released)
                return;

            if (eventCameraUserCount == 0)
            {
                eventCameraPrevDepth = camera.depth;
                eventCameraDepthStored = true;
                camera.depth = 20f;
            }

            eventCameraUserCount++;
            cameraObject.SetActive(true);
            state.CameraObject = cameraObject;
            state.CameraComponent = camera;
            state.Acquired = true;
        }

        private void ReleaseEventCameraUsage(EventCameraUsageRuntimeState state)
        {
            if (state == null || state.Released) return;
            state.Released = true;
            if (!state.Acquired) return;

            eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1);
            if (eventCameraUserCount > 0) return;

            if (eventCameraDepthStored && state.CameraComponent != null)
                state.CameraComponent.depth = eventCameraPrevDepth;
            eventCameraDepthStored = false;
            if (state.CameraObject != null && state.CameraObject.activeSelf)
                state.CameraObject.SetActive(false);
        }

        private static Quaternion SafeLookRotation(Vector3 direction, Quaternion fallback)
        {
            return direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction)
                : fallback;
        }

        private IEnumerator CameraMoveRoutine(
            CameraMovementData movement,
            float stepStartTime,
            bool useMain,
            MainCameraDepthRuntimeState mainCameraDepthState,
            EventCameraUsageRuntimeState eventCameraUsageState)
        {
            if (movement == null) yield break;
            Vector3 LocalToWorld(Transform center, Vector3 local) { return center.position + center.right * local.x + center.up * local.y + center.forward * local.z; }
            if (useMain)
            {
                Camera camComp = Camera.main; if (camComp == null) yield break;
                Transform camTransform = camComp.transform;
                AcquireMainCameraDepth(camComp, mainCameraDepthState);
                try
                {
                    while (Time.time - stepStartTime < movement.StartTime) yield return null;
                    Vector3 startWorld, goalWorld;
                    if (movement.IsRelative && !string.IsNullOrEmpty(movement.CenterObject)) { var centerGO = ResolveByName(movement.CenterObject); if (centerGO != null) { var centerT = centerGO.transform; startWorld = LocalToWorld(centerT, movement.StartPosition); goalWorld = LocalToWorld(centerT, movement.EndPosition); } else { startWorld = movement.StartPosition; goalWorld = movement.EndPosition; } }
                    else { startWorld = movement.StartPosition; goalWorld = movement.EndPosition; }
                    float duration = Mathf.Max(0.0001f, movement.EndTime - movement.StartTime);
                    camTransform.position = startWorld; float localStart = Time.time;
                    while (true) { float k = Mathf.Clamp01((Time.time - localStart) / duration); camTransform.position = Vector3.Lerp(startWorld, goalWorld, k); if (k >= 1f) break; yield return null; }
                }
                finally
                {
                    ReleaseMainCameraDepth(mainCameraDepthState);
                }
                yield break;
            }
            var camGO = GetOrCreateEventCamera(); if (camGO == null) yield break;
            Camera cam = camGO.GetComponent<Camera>(); if (cam == null) yield break;
            Transform camT = cam.transform;
            Camera mainCamera = Camera.main;
            if (mainCamera == null) yield break;
            camGO.transform.position = mainCamera.transform.position;
            camGO.transform.rotation = mainCamera.transform.rotation;
            AcquireEventCameraUsage(camGO, cam, eventCameraUsageState);
            try
            {
                while (Time.time - stepStartTime < movement.StartTime) yield return null;
                Vector3 s, g;
                if (movement.IsRelative && !string.IsNullOrEmpty(movement.CenterObject)) { var centerGO = ResolveByName(movement.CenterObject); if (centerGO != null) { var centerT = centerGO.transform; s = LocalToWorld(centerT, movement.StartPosition); g = LocalToWorld(centerT, movement.EndPosition); } else { s = movement.StartPosition; g = movement.EndPosition; } }
                else { s = movement.StartPosition; g = movement.EndPosition; }
                float dur = Mathf.Max(0.0001f, movement.EndTime - movement.StartTime);
                camT.position = s; float localStart2 = Time.time;
                while (true) { float k = Mathf.Clamp01((Time.time - localStart2) / dur); camT.position = Vector3.Lerp(s, g, k); if (k >= 1f) break; yield return null; }
            }
            finally
            {
                ReleaseEventCameraUsage(eventCameraUsageState);
            }
        }

        private IEnumerator CameraLookAtRoutine(
            CameraAimData aim,
            float stepStartTime,
            bool useMain,
            MainCameraDepthRuntimeState mainCameraDepthState,
            EventCameraUsageRuntimeState eventCameraUsageState)
        {
            if (aim == null) yield break;
            Vector3 GetTargetWorldPosWithLocalOffset(Transform target, Vector3 localOffset) { if (target == null) return Vector3.zero; return target.position + target.right * localOffset.x + target.up * localOffset.y + target.forward * localOffset.z; }
            if (useMain)
            {
                Camera camComp = Camera.main; if (camComp == null) yield break;
                Transform camTransform = camComp.transform;
                AcquireMainCameraDepth(camComp, mainCameraDepthState);
                try
                {
                    while (Time.time - stepStartTime < aim.StartTime) yield return null;
                    float endTime = Mathf.Max(aim.EndTime, aim.StartTime + 0.0001f);
                    GameObject target = ResolveByName(aim.aimedTargetName); Transform targetT = target != null ? target.transform : null;
                    if (targetT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); Vector3 dir0 = aimPos - camTransform.position; Quaternion goal = SafeLookRotation(dir0, camTransform.rotation); float initDur = Mathf.Max(0f, aim.InitDuration); if (initDur <= 0f) { camTransform.rotation = goal; } else { Quaternion startRot = camTransform.rotation; float elapsed = 0f; while (elapsed < initDur) { aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); dir0 = aimPos - camTransform.position; goal = SafeLookRotation(dir0, goal); elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / initDur); camTransform.rotation = Quaternion.Slerp(startRot, goal, k); yield return null; } camTransform.rotation = goal; } }
                    while (Time.time - stepStartTime < endTime) { if (targetT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); Vector3 dir = aimPos - camTransform.position; camTransform.rotation = SafeLookRotation(dir, camTransform.rotation); } yield return null; }
                }
                finally
                {
                    ReleaseMainCameraDepth(mainCameraDepthState);
                }
                yield break;
            }
            var camGO2 = GetOrCreateEventCamera(); if (camGO2 == null) yield break;
            Camera camComp2 = camGO2.GetComponent<Camera>(); if (camComp2 == null) yield break;
            Transform camTransform2 = camComp2.transform;
            Camera aimMainCamera = Camera.main;
            if (aimMainCamera != null) { camTransform2.position = aimMainCamera.transform.position; camTransform2.rotation = aimMainCamera.transform.rotation; }
            AcquireEventCameraUsage(camGO2, camComp2, eventCameraUsageState);
            try
            {
                while (Time.time - stepStartTime < aim.StartTime) yield return null;
                float endT = Mathf.Max(aim.EndTime, aim.StartTime + 0.0001f);
                GameObject tgt = ResolveByName(aim.aimedTargetName); Transform tgtT = tgt != null ? tgt.transform : null;
                if (tgtT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); Vector3 dir0 = aimPos - camTransform2.position; Quaternion goal = SafeLookRotation(dir0, camTransform2.rotation); float initDur = Mathf.Max(0f, aim.InitDuration); if (initDur <= 0f) { camTransform2.rotation = goal; } else { Quaternion startRot = camTransform2.rotation; float elapsed = 0f; while (elapsed < initDur) { aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); dir0 = aimPos - camTransform2.position; goal = SafeLookRotation(dir0, goal); elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / initDur); camTransform2.rotation = Quaternion.Slerp(startRot, goal, k); yield return null; } camTransform2.rotation = goal; } }
                while (Time.time - stepStartTime < endT) { if (tgtT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); Vector3 dir = aimPos - camTransform2.position; camTransform2.rotation = SafeLookRotation(dir, camTransform2.rotation); } yield return null; }
            }
            finally
            {
                ReleaseEventCameraUsage(eventCameraUsageState);
            }
        }

        private IEnumerator FadeScheduleRoutine(FadeInfo fadeInfo, float eventStartRealtime)
        {
            if (fadeInfo == null) yield break;
            CreateFadeCanvasIfNeeded();
            float span = fadeInfo.EndTime - fadeInfo.StartTime;
            if (span <= 0f) { while (Time.realtimeSinceStartup - eventStartRealtime < fadeInfo.StartTime) yield return null; if (fadeCanvas == null || fadeCanvasGroup == null) CreateFadeCanvasIfNeeded(); if (fadeInfo.FadeIn) { fadeCanvasGroup.alpha = 1f; if (!fadeCanvas.activeSelf) fadeCanvas.SetActive(true); } else { fadeCanvasGroup.alpha = 0f; if (fadeCanvas.activeSelf) fadeCanvas.SetActive(false); } yield break; }
            while (Time.realtimeSinceStartup - eventStartRealtime < fadeInfo.StartTime) yield return null;
            if (fadeCanvas == null || fadeCanvasGroup == null) CreateFadeCanvasIfNeeded();
            float startAlpha = fadeInfo.FadeIn ? 0f : 1f; float endAlpha = fadeInfo.FadeIn ? 1f : 0f;
            fadeCanvasGroup.alpha = startAlpha; if (!fadeCanvas.activeSelf) fadeCanvas.SetActive(true);
            float localStart = Time.realtimeSinceStartup;
            while (true) { if (fadeCanvasGroup == null) yield break; float k = Mathf.Clamp01((Time.realtimeSinceStartup - localStart) / span); fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, k); if (k >= 1f) break; yield return null; }
            fadeCanvasGroup.alpha = endAlpha;
            if (!fadeInfo.FadeIn) { fadeCanvasGroup.alpha = 0f; fadeCanvas.SetActive(false); }
        }

        private IEnumerator MoveRoutine(GameObject target, Vector3 distance, float duration, bool useLocal)
        {
            if (target == null) yield break;
            if (duration <= 0f) { if (useLocal) target.transform.localPosition += distance; else target.transform.position += distance; yield break; }
            if (useLocal) { Vector3 start = target.transform.localPosition; Vector3 goal = start + distance; float t = 0f; while (t < duration) { t += Time.deltaTime; float k = Mathf.Clamp01(t / duration); target.transform.localPosition = Vector3.Lerp(start, goal, k); yield return null; } target.transform.localPosition = goal; }
            else { Vector3 start = target.transform.position; Vector3 goal = start + distance; float t = 0f; while (t < duration) { t += Time.deltaTime; float k = Mathf.Clamp01(t / duration); target.transform.position = Vector3.Lerp(start, goal, k); yield return null; } target.transform.position = goal; }
        }

        private IEnumerator RotateToRoutine(Transform t, Quaternion goal, float duration)
        {
            if (t == null) yield break; if (duration <= 0f) { t.rotation = goal; yield break; }
            Quaternion start = t.rotation; float elapsed = 0f;
            while (elapsed < duration) { elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / duration); t.rotation = Quaternion.Slerp(start, goal, k); yield return null; }
            t.rotation = goal;
        }

        private IEnumerator RotateToRoutineLocal(Transform t, Quaternion goal, float duration)
        {
            if (t == null) yield break; if (duration <= 0f) { t.localRotation = goal; yield break; }
            Quaternion start = t.localRotation; float elapsed = 0f;
            while (elapsed < duration) { elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / duration); t.localRotation = Quaternion.Slerp(start, goal, k); yield return null; }
            t.localRotation = goal;
        }

        private IEnumerator ChoiceRoutine(ChoiceData choice, GameObject caller)
        {
            if (choice == null || choice.Candidates == null || choice.Candidates.Length == 0) { Debug.LogWarning("[EventPlayManager] ChoiceRoutine: choice 또는 후보가 없습니다."); yield break; }
            var prevCursorLock = Cursor.lockState; var prevCursorVisible = Cursor.visible; bool prevIsLockKeyboard = isLockKeyboard; bool prevIsLockMouse = isLockMouse;
            isLockKeyboard = true; isLockMouse = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            GameObject tempEventSystem = null;
            Component addedInputModule = null;
            var temporarilyDisabledModules = new List<Behaviour>();
            var temporarilyEnabledModules = new List<Behaviour>();
            try
            {
                UnityEngine.EventSystems.EventSystem eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem == null)
                {
                    tempEventSystem = new GameObject("AutoEventSystem");
                    eventSystem = tempEventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                }

                if (!PrepareCompatibleInputModule(
                        eventSystem.gameObject,
                        temporarilyDisabledModules,
                        temporarilyEnabledModules,
                        out addedInputModule))
                    yield break;

                if (choiceCanvasInstance == null)
                {
                    if (choiceCanvasPrefab == null) yield break;
                    choiceCanvasInstance = Instantiate(choiceCanvasPrefab);
                    try { var mgrScene = gameObject.scene; if (mgrScene.IsValid()) SceneManager.MoveGameObjectToScene(choiceCanvasInstance, mgrScene); } catch { }
                    choiceCanvasInstance.name = choiceCanvasPrefab.name; AddToCacheIfNeeded(choiceCanvasInstance);
                }
                try { if (!choiceCanvasInstance.activeSelf) choiceCanvasInstance.SetActive(true); var canvas = choiceCanvasInstance.GetComponent<Canvas>(); if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 10000; canvas.enabled = true; } var cg = choiceCanvasInstance.GetComponent<CanvasGroup>(); if (cg != null) { cg.interactable = true; cg.blocksRaycasts = true; } var gr = choiceCanvasInstance.GetComponent<GraphicRaycaster>(); if (gr == null) choiceCanvasInstance.AddComponent<GraphicRaycaster>(); } catch { }
                var grid = choiceCanvasInstance.GetComponentInChildren<GridLayoutGroup>(true);
                if (grid == null) yield break;
                var containerGO = grid.gameObject;
                for (int i = containerGO.transform.childCount - 1; i >= 0; i--) { try { Destroy(containerGO.transform.GetChild(i).gameObject); } catch { } }
                int selectedIndex = -1; Candidate selectedCandidate = null;
                int selectableButtonCount = 0;
                for (int i = 0; i < choice.Candidates.Length; i++)
                {
                    var cand = choice.Candidates[i];
                    if (choiceContentsPrefab == null) yield break;
                    var btnGO = Instantiate(choiceContentsPrefab); btnGO.name = choiceContentsPrefab.name; btnGO.transform.SetParent(containerGO.transform, false);
                    var textComp = btnGO.GetComponentInChildren<Text>(true); if (textComp != null) textComp.text = cand != null ? cand.Text : $"Choice {i + 1}";
                    var layoutElem = btnGO.GetComponent<LayoutElement>() ?? btnGO.AddComponent<LayoutElement>(); layoutElem.preferredHeight = 60f; layoutElem.flexibleWidth = 1f;
                    int closureIndex = i; var button = btnGO.GetComponent<Button>();
                    if (button != null) { button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => { if (selectedIndex != -1) return; selectedIndex = closureIndex; selectedCandidate = choice.Candidates[closureIndex]; }); button.interactable = true; selectableButtonCount++; }
                }
                if (selectableButtonCount == 0)
                {
                    Debug.LogWarning("[EventPlayManager] ChoiceContents 프리팹에 Button이 없어 선택 이벤트를 종료합니다.");
                    yield break;
                }
                yield return null;
                while (selectedIndex < 0) yield return null;
                if (selectedCandidate != null)
                {
                    string key = selectedCandidate.ValueName ?? string.Empty;
                    if (!string.IsNullOrEmpty(key))
                    {
                        switch (selectedCandidate.type)
                        {
                            case EventSO.ValueType.Int: SetGlobals<int>(key, selectedCandidate.intValue); break;
                            case EventSO.ValueType.Float: SetGlobals<float>(key, selectedCandidate.floatValue); break;
                            case EventSO.ValueType.Bool: SetGlobals<bool>(key, selectedCandidate.boolValue); break;
                            case EventSO.ValueType.String: SetGlobals<string>(key, selectedCandidate.stringValue ?? string.Empty); break;
                            case EventSO.ValueType.GameObject: SetGlobals<GameObject>(key, selectedCandidate.gameObjectValue); break;
                        }
                    }
                }
            }
            finally
            {
                CleanupChoiceUIIfAny();
                RestoreCursorAndLock(prevCursorLock, prevCursorVisible, prevIsLockKeyboard, prevIsLockMouse);
                RestorePreparedInputModules(addedInputModule, temporarilyDisabledModules, temporarilyEnabledModules);
                if (tempEventSystem != null) Destroy(tempEventSystem);
            }
        }

        private static bool PrepareCompatibleInputModule(
            GameObject eventSystemObject,
            List<Behaviour> temporarilyDisabled,
            List<Behaviour> temporarilyEnabled,
            out Component addedModule)
        {
            addedModule = null;
            if (eventSystemObject == null) return false;

            UnityEngine.EventSystems.BaseInputModule[] modules =
                eventSystemObject.GetComponents<UnityEngine.EventSystems.BaseInputModule>();
            for (int i = 0; i < modules.Length; i++)
            {
                UnityEngine.EventSystems.BaseInputModule module = modules[i];
                if (module == null || !IsCompatibleInputModule(module)) continue;
                if (!module.enabled)
                {
                    module.enabled = true;
                    temporarilyEnabled.Add(module);
                }
                return true;
            }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < modules.Length; i++)
            {
                UnityEngine.EventSystems.BaseInputModule module = modules[i];
                if (module == null || !module.enabled) continue;
                module.enabled = false;
                temporarilyDisabled.Add(module);
            }
#endif

#if ENABLE_INPUT_SYSTEM
            Type inputSystemModuleType = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModuleType != null &&
                typeof(UnityEngine.EventSystems.BaseInputModule).IsAssignableFrom(inputSystemModuleType))
            {
                addedModule = eventSystemObject.AddComponent(inputSystemModuleType);
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            addedModule = eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            return true;
#else
            Debug.LogError("[EventPlayManager] 현재 입력 설정에 맞는 UI Input Module을 찾지 못했습니다.");
            return false;
#endif
        }

        private static bool IsCompatibleInputModule(UnityEngine.EventSystems.BaseInputModule module)
        {
            if (module == null) return false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return string.Equals(
                module.GetType().FullName,
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule",
                StringComparison.Ordinal);
#elif ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
            return module is UnityEngine.EventSystems.StandaloneInputModule;
#else
            return true;
#endif
        }

        private static void RestorePreparedInputModules(
            Component addedModule,
            List<Behaviour> temporarilyDisabled,
            List<Behaviour> temporarilyEnabled)
        {
            if (addedModule != null) Destroy(addedModule);
            for (int i = 0; i < temporarilyEnabled.Count; i++)
                if (temporarilyEnabled[i] != null) temporarilyEnabled[i].enabled = false;
            for (int i = 0; i < temporarilyDisabled.Count; i++)
                if (temporarilyDisabled[i] != null) temporarilyDisabled[i].enabled = true;
        }

        private IEnumerator PlaySoundRoutine(SoundData sd)
        {
            if (sd == null) yield break;
            if (sd.time > 0f) yield return new WaitForSeconds(sd.time);
            if (sd.audioClip == null) { eventAudioSource?.Stop(); yield break; }
            if (eventAudioSource == null) { eventAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>(); eventAudioSource.playOnAwake = false; eventAudioSource.spatialBlend = 0f; eventAudioSource.dopplerLevel = 0f; }
            if (sd.isLoop)
            {
                if (eventAudioSource.clip != sd.audioClip || !eventAudioSource.isPlaying)
                {
                    eventAudioSource.clip = sd.audioClip;
                    eventAudioSource.loop = true;
                    eventAudioSource.volume = Mathf.Clamp01(sd.volume);
                    eventAudioSource.Play();
                }
                if (sd.WaitForCompletion)
                    Debug.LogWarning("[EventPlayManager] Loop Sound는 완료 시점이 없어 Wait For Completion을 적용하지 않습니다.");
            }
            else
            {
                if (eventAudioSource.loop)
                {
                    eventAudioSource.Stop();
                    eventAudioSource.loop = false;
                }
                eventAudioSource.PlayOneShot(sd.audioClip, Mathf.Clamp01(sd.volume));

                if (sd.WaitForCompletion)
                {
                    float absolutePitch = Mathf.Abs(eventAudioSource.pitch);
                    if (absolutePitch <= 0.0001f)
                    {
                        Debug.LogWarning("[EventPlayManager] Sound AudioSource의 Pitch가 0이라 완료 시간을 기다리지 않습니다.");
                    }
                    else
                    {
                        double endDspTime = AudioSettings.dspTime + sd.audioClip.length / absolutePitch;
                        while (AudioSettings.dspTime < endDspTime)
                            yield return null;
                    }
                }
            }
            yield break;
        }

        private void CleanupChoiceUIIfAny()
        {
            if (choiceCanvasInstance != null) { try { RemoveFromCache(choiceCanvasInstance); Destroy(choiceCanvasInstance); } catch { } choiceCanvasInstance = null; }
        }

        private void AddToCacheIfNeeded(GameObject go)
        {
            if (go == null) return; if (cachedObjectsSet.Contains(go)) return;
            cachedObjects.Add(go); cachedObjectsSet.Add(go);
            if (!string.IsNullOrEmpty(go.name) && !cachedObjectsByName.ContainsKey(go.name)) cachedObjectsByName[go.name] = go;
        }

        private void RemoveFromCache(GameObject go)
        {
            if (go == null) return;
            cachedObjects.Remove(go); cachedObjectsSet.Remove(go);
            if (!string.IsNullOrEmpty(go.name) && cachedObjectsByName.TryGetValue(go.name, out var cached) && cached == go) cachedObjectsByName.Remove(go.name);
        }

        private GameObject ResolveByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (cachedObjectsByName.TryGetValue(name, out var cached) && cached != null) return cached;
            for (int i = 0; i < cachedObjects.Count; i++) { var obj = cachedObjects[i]; if (obj != null && obj.name == name) { cachedObjectsByName[name] = obj; return obj; } }
            var found = GameObject.Find(name); if (found != null) { AddToCacheIfNeeded(found); return found; }
            var deep = FindInAllLoadedScenes(name); if (deep != null) { AddToCacheIfNeeded(deep); return deep; }
            return null;
        }

        private static GameObject FindInAllLoadedScenes(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            bool hasSlash = nameOrPath.IndexOf('/') >= 0;
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var scene = SceneManager.GetSceneAt(si); if (!scene.IsValid() || !scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                if (hasSlash)
                {
                    var parts = nameOrPath.Split('/');
                    for (int r = 0; r < roots.Length; r++)
                    {
                        var root = roots[r];
                        if (string.Equals(root.name, parts[0], StringComparison.Ordinal))
                        {
                            Transform qualified = root.transform;
                            for (int i = 1; i < parts.Length && qualified != null; i++) qualified = qualified.Find(parts[i]);
                            if (qualified != null) return qualified.gameObject;
                        }

                        Transform relative = root.transform.Find(nameOrPath);
                        if (relative != null) return relative.gameObject;
                    }
                }
                else { for (int r = 0; r < roots.Length; r++) { var root = roots[r]; if (root.name == nameOrPath) return root; var found = FindChildByNameRecursive(root.transform, nameOrPath); if (found != null) return found.gameObject; } }
            }
            return null;
        }

        private static Transform FindChildByNameRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++) { var child = parent.GetChild(i); if (child.name == name) return child; var found = FindChildByNameRecursive(child, name); if (found != null) return found; }
            return null;
        }

        private GameObject GetOrCreateSingletonUI(GameObject prefab)
        {
            if (prefab == null) return null;
            if (cachedObjectsByName.TryGetValue(prefab.name, out var cached) && cached != null) { EnsureVisibleUIScale(cached); return cached; }
            for (int i = 0; i < cachedObjects.Count; i++) { var obj = cachedObjects[i]; if (obj != null && obj.name == prefab.name) { cachedObjectsByName[prefab.name] = obj; EnsureVisibleUIScale(obj); return obj; } }
            var uiObj = Instantiate(prefab); uiObj.name = prefab.name; uiObj.SetActive(false);
            EnsureVisibleUIScale(uiObj);
            try { var mgrScene = gameObject.scene; if (mgrScene.IsValid()) SceneManager.MoveGameObjectToScene(uiObj, mgrScene); } catch { }
            AddToCacheIfNeeded(uiObj); return uiObj;
        }

        private static void EnsureVisibleUIScale(GameObject uiObject)
        {
            if (uiObject == null) return;
            Vector3 scale = uiObject.transform.localScale;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
                uiObject.transform.localScale = Vector3.one;
        }

        private GameObject GetOrCreateEventCamera()
        {
            if (eventCamera == null) return null;
            if (cachedObjectsByName.TryGetValue(eventCamera.name, out var cached) && cached != null) return cached;
            for (int i = 0; i < cachedObjects.Count; i++) { var obj = cachedObjects[i]; if (obj != null && obj.name == eventCamera.name) { cachedObjectsByName[eventCamera.name] = obj; return obj; } }
            var camGO = Instantiate(eventCamera);
            try { var mgrScene = gameObject.scene; if (mgrScene.IsValid()) SceneManager.MoveGameObjectToScene(camGO, mgrScene); } catch { }
            camGO.name = eventCamera.name; camGO.SetActive(false); AddToCacheIfNeeded(camGO); return camGO;
        }

        public GameObject GetSpawnCaller(GameObject go) { if (go == null) return null; return spawnerMap.TryGetValue(go, out var caller) ? caller : null; }

        private void RestoreCursorAndLock(CursorLockMode prevLock, bool prevVisible, bool prevKeyboard, bool prevMouse) { Cursor.lockState = prevLock; Cursor.visible = prevVisible; isLockKeyboard = prevKeyboard; isLockMouse = prevMouse; }

        public bool IsEventRunning => runningCoroutines.Count > 0 || runningEventPairs.Count > 0;

        public void SetAction(string actionName, System.Action action)
        {
            if (string.IsNullOrEmpty(actionName) || action == null) return;
            actionMap[actionName] = action;
        }

        public void UnSetAction(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return;
            actionMap.Remove(actionName);
        }

        private IEnumerator MapMoveWithLoading(GameObject target, MoveObjectSubData move, Action cleanup)
        {
            try
            {
                // Real time: loading is visible even while gameplay is paused. The next Phase waits for this routine.
                if (target == null) yield break;
                float began = Time.realtimeSinceStartup;
                Vector3 start = target.transform.position;
                if (move.IsAnotherStartPosition)
                {
                    var basis = move.startIsRelative ? ResolveByName(move.startTargetName) : null;
                    Transform relativeStart = basis != null ? basis.transform : target.transform;
                    start = move.startIsRelative
                        ? relativeStart.position + relativeStart.right * move.startPosition.x + relativeStart.up * move.startPosition.y + relativeStart.forward * move.startPosition.z
                        : move.startPosition;
                }
                var goalBasis = move.isRelative ? ResolveByName(move.targetName) : null;
                Transform relative = goalBasis != null ? goalBasis.transform : target.transform;
                Vector3 goal = move.isRelative
                    ? relative.position + relative.right * move.targetPosition.x + relative.up * move.targetPosition.y + relative.forward * move.targetPosition.z
                    : move.targetPosition;
                var body = target.GetComponent<Rigidbody2D>();
                if (body != null) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0f; }
                // A map transfer is performed while the screen is opaque; ordinary MoveObject is unchanged.
                if (move.Duration > 0f)
                    yield return MoveRoutineCustom(target, start, goal, move.Duration, move, false, true);
                if (target == null) yield break;
                target.transform.position = goal;
                if (body != null) body.position = goal;
                Physics2D.SyncTransforms();
                float remaining = Mathf.Max(0f, move.LoadingDuration) - (Time.realtimeSinceStartup - began);
                if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
            }
            finally { cleanup(); }
        }

        private IEnumerator MoveRoutineCustom(GameObject target, Vector3 startPos, Vector3 goalPos, float duration, MoveObjectSubData move, bool useLocal, bool unscaled = false)
        {
            if (target == null) yield break;
            duration = Mathf.Max(0f, duration);
            if (duration <= 0f) { if (useLocal) target.transform.localPosition = goalPos; else target.transform.position = goalPos; yield break; }

            var type = move.movementType;

            switch (type)
            {
                case MovementType.Linear:
                {
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k);
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.Spiral:
                {
                    float radius = Mathf.Max(0f, move.SpiralRadius);
                    int turns = Mathf.Max(1, move.SpiralTurns);
                    Vector3 forward = (goalPos - startPos).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(Vector3.forward, forward);
                    right.Normalize();
                    Vector3 up = Vector3.Cross(forward, right).normalized;
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float angle = 2f * Mathf.PI * turns * k;
                        float r = radius * (1f - k);
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k) + right * Mathf.Cos(angle) * r + up * Mathf.Sin(angle) * r;
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.ZigZag:
                {
                    float amp = move.ZigZagAmplitude;
                    float freq = Mathf.Max(0.0001f, move.ZigZagFrequency);
                    Vector3 forward = (goalPos - startPos).normalized;
                    Vector3 perp = Vector3.Cross(Vector3.up, forward);
                    if (perp.sqrMagnitude < 0.001f) perp = Vector3.Cross(Vector3.forward, forward);
                    perp.Normalize();
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float phase = 2f * Mathf.PI * freq * t;
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k) + perp * Mathf.Sin(phase) * amp;
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.Bounce:
                {
                    int bounces = Mathf.Max(1, move.BounceCount);
                    float height = Mathf.Max(0f, move.BounceHeight);
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float bouncePhase = k * bounces;
                        float frac = bouncePhase - Mathf.Floor(bouncePhase);
                        float h = 4f * frac * (1f - frac) * height * (1f - k);
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k) + Vector3.up * h;
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.EaseInOut:
                {
                    float exp = Mathf.Max(0.01f, move.EaseExponent);
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float eased = k < 0.5f
                            ? Mathf.Pow(2f * k, exp) * 0.5f
                            : 1f - Mathf.Pow(2f * (1f - k), exp) * 0.5f;
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, eased);
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.Arc:
                {
                    float arcH = Mathf.Max(0f, move.ArcHeight);
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float h = 4f * k * (1f - k) * arcH;
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k) + Vector3.up * h;
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
                case MovementType.Parabola:
                {
                    float peakH = Mathf.Max(0f, move.ParabolaPeakHeight);
                    float startY = startPos.y;
                    float endY = goalPos.y;
                    float t = 0f;
                    while (t < duration)
                    {
                        if (target == null) yield break;
                        t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime; float k = Mathf.Clamp01(t / duration);
                        float baseY = Mathf.Lerp(startY, endY, k);
                        float parabolaY = baseY + 4f * peakH * k * (1f - k);
                        Vector3 pos = Vector3.Lerp(startPos, goalPos, k);
                        pos.y = parabolaY;
                        if (useLocal) target.transform.localPosition = pos; else target.transform.position = pos;
                        yield return null;
                    }
                    break;
                }
            }

            if (useLocal) target.transform.localPosition = goalPos; else target.transform.position = goalPos;
        }

        private void ApplySetValue(SetValueSubData sv, GameObject caller)
        {
            if (sv == null) return;
            string key = sv.ValueName ?? string.Empty; if (string.IsNullOrEmpty(key)) return;
            bool isAdd = (sv.setType == EventSO.SetType.Add);
            switch (sv.valueType)
            {
                case EventSO.ValueType.Int: { int cur = sv.IsPrefData ? PlayerPrefs.GetInt(key, 0) : (intGlobals.TryGetValue(key, out var iv) ? iv : 0); int next = isAdd ? cur + sv.intValue : sv.intValue; if (sv.IsPrefData) { PlayerPrefs.SetInt(key, next); PlayerPrefs.Save(); } else { intGlobals[key] = next; } break; }
                case EventSO.ValueType.Float: { float cur = sv.IsPrefData ? PlayerPrefs.GetFloat(key, 0f) : (floatGlobals.TryGetValue(key, out var fv) ? fv : 0f); float next = isAdd ? cur + sv.floatValue : sv.floatValue; if (sv.IsPrefData) { PlayerPrefs.SetFloat(key, next); PlayerPrefs.Save(); } else { floatGlobals[key] = next; } break; }
                case EventSO.ValueType.Bool: { bool cur = sv.IsPrefData ? (PlayerPrefs.GetInt(key, 0) != 0) : (boolGlobals.TryGetValue(key, out var bv) && bv); bool next = isAdd ? !cur : (sv.boolValue == true); if (sv.IsPrefData) { PlayerPrefs.SetInt(key, next ? 1 : 0); PlayerPrefs.Save(); } else { boolGlobals[key] = next; } break; }
                case EventSO.ValueType.String: { string next = sv.stringValue ?? string.Empty; if (string.IsNullOrEmpty(next)) next = (caller != null && !string.IsNullOrEmpty(caller.name)) ? caller.name : string.Empty; if (sv.IsPrefData) { PlayerPrefs.SetString(key, next); PlayerPrefs.Save(); } else { stringGlobals[key] = next; } break; }
                case EventSO.ValueType.GameObject: { GameObject nextGO = sv.gameObjectValue ?? caller; gameObjectGlobals[key] = nextGO; break; }
            }
        }
    }
}
