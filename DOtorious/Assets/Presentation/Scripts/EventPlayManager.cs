using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static JYW.Game.EventPlay.EventSO;

namespace JYW.Game.EventPlay
{
    public class EventPlayManager : Singleton<EventPlayManager>
    {
        public static string OwningSceneName => "Presentation";

        [SerializeField] private GameObject softSpeechPrefab;
        [SerializeField] private GameObject hardSpeechPrefab;
        [SerializeField] private GameObject memoPrefab;
        [SerializeField] private GameObject eventCamera;
        [SerializeField] private GameObject choiceCanvasPrefab;
        [SerializeField] private GameObject choiceContentsPrefab;

        [HideInInspector] public bool isLockCamera = false;
        [HideInInspector] public bool isLockMove = false;

        [HideInInspector] public List<GameObject> cachedObjects = new List<GameObject>();

        private GameObject fadeCanvas = null;
        private CanvasGroup fadeCanvasGroup = null;

        private readonly Dictionary<string, Delegate> eventMap = new Dictionary<string, Delegate>();

        private readonly Dictionary<string, System.Action> actionMap = new Dictionary<string, System.Action>();

        private readonly HashSet<(EventSO, GameObject)> runningEventPairs = new HashSet<(EventSO, GameObject)>();

        private AudioSource eventAudioSource;

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

        private int lockMoveCount = 0;
        private int lockCameraCount = 0;

        private readonly Dictionary<GameObject, Vector3> drawerClosedLocal = new Dictionary<GameObject, Vector3>();
        private readonly Dictionary<GameObject, Vector3> drawerLastOffset = new Dictionary<GameObject, Vector3>();

        private int eventCameraUserCount = 0;
        private float eventCameraPrevDepth = 0f;
        private bool eventCameraDepthStored = false;

        private readonly Dictionary<string, List<GameObject>> pausedSceneRoots = new Dictionary<string, List<GameObject>>();

        private readonly Dictionary<string, GameObject> cachedObjectsByName = new Dictionary<string, GameObject>();

        private readonly HashSet<GameObject> cachedObjectsSet = new HashSet<GameObject>();

        private sealed class EventContext
        {
            private readonly List<System.Action> onCancel = new List<System.Action>();
            public bool IsCancelled { get; private set; }

            public int AcquiredMoveLocks = 0;
            public int AcquiredCameraLocks = 0;

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
            if (condGroup == null || condGroup.Conditions == null || condGroup.Conditions.Length == 0)
                return false;

            bool allMatch = true;
            for (int ci = 0; ci < condGroup.Conditions.Length; ci++)
            {
                var info = condGroup.Conditions[ci];
                if (info == null) continue;

                string key = info.GlobalNames ?? string.Empty;
                bool pass = false;
                bool usePlayerPrefs = info.isPlayerPrefData;

                if (string.IsNullOrEmpty(key))
                {
                    switch (info.valueType)
                    {
                        case EventSO.ValueType.Int: pass = (0 == info.ExpectedInt); break;
                        case EventSO.ValueType.Float: pass = (0f == info.ExpectedFloat); break;
                        case EventSO.ValueType.Bool: pass = (false == info.ExpectedBool); break;
                        case EventSO.ValueType.String: pass = (string.Empty == (info.ExpectedString ?? "")); break;
                        case EventSO.ValueType.GameObject: pass = (info.ExpectedGameObject == null); break;
                    }
                    if (!pass) { allMatch = false; break; }
                    continue;
                }

                if (info.checkType == EventSO.CheckType.Odd || info.checkType == EventSO.CheckType.Even)
                {
                    int current = usePlayerPrefs
                        ? PlayerPrefs.GetInt(key, 0)
                        : (intGlobals.TryGetValue(key, out var iv) ? iv : 0);

                    pass = (info.checkType == EventSO.CheckType.Odd) ? (current % 2 != 0) : (current % 2 == 0);
                    if (!pass) { allMatch = false; break; }
                    continue;
                }

                switch (info.valueType)
                {
                    case EventSO.ValueType.Int:
                        {
                            int current = usePlayerPrefs ? PlayerPrefs.GetInt(key, 0) : (intGlobals.TryGetValue(key, out var iv) ? iv : 0);
                            pass = (current == info.ExpectedInt);
                            break;
                        }
                    case EventSO.ValueType.Float:
                        {
                            float current = usePlayerPrefs ? PlayerPrefs.GetFloat(key, 0f) : (floatGlobals.TryGetValue(key, out var fv) ? fv : 0f);
                            pass = (current == info.ExpectedFloat);
                            break;
                        }
                    case EventSO.ValueType.Bool:
                        {
                            bool current = usePlayerPrefs ? (PlayerPrefs.GetInt(key, 0) != 0) : (boolGlobals.TryGetValue(key, out var bv) && bv);
                            pass = (current == info.ExpectedBool);
                            break;
                        }
                    case EventSO.ValueType.String:
                        {
                            string current = usePlayerPrefs ? PlayerPrefs.GetString(key, string.Empty) : (stringGlobals.TryGetValue(key, out var sv) ? sv : string.Empty);
                            pass = string.Equals(current ?? string.Empty, info.ExpectedString ?? string.Empty, StringComparison.Ordinal);
                            break;
                        }
                    case EventSO.ValueType.GameObject:
                        {
                            pass = gameObjectGlobals.TryGetValue(key, out var current) &&
                                   current == info.ExpectedGameObject;
                            break;
                        }
                }

                if (!pass) { allMatch = false; break; }
            }

            return allMatch;
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

                void MarkFinished() { finished = true; }
                if (context != null)
                {
                    if (onCancel != null)
                        context.AddOnCancel(() => { try { onCancel(); } catch { } MarkFinished(); });
                    else
                        context.AddOnCancel(MarkFinished);
                }

                IEnumerator Wrapper()
                {
                    try
                    {
                        yield return routine;
                    }
                    finally
                    {
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
                        if (target != null) { var colliders = target.GetComponentsInChildren<Collider>(true); foreach (var col in colliders) col.enabled = false; }
                    }
                }
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
                                Quaternion goalRot = Quaternion.LookRotation(target.transform.position - t.position);
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

            // ── Cameras 그룹 ──
            if (step.Flags.IsCameras && step.Cameras != null)
            {
                var cameras = step.Cameras;

                // CameraMovement
                if (cameras.IsCameraMove && cameras.CameraMovement != null)
                {
                    Action onCancelCam = () => { try { eventCameraUserCount = Math.Max(0, eventCameraUserCount - 1); DisableEventCameraIfUnused(); } catch { } };
                    StartCoroutine(RunRoutine(CameraMoveRoutine(cameras.CameraMovement, stepStartTime, cameras.CameraMovement.IsCameraMoveUseMain), onCancelCam));
                }

                // CameraAim
                if (cameras.IsCameraAiming && cameras.CameraAim != null)
                {
                    Action onCancelCam = () => { try { eventCameraUserCount = Math.Max(0, eventCameraUserCount - 1); DisableEventCameraIfUnused(); } catch { } };
                    StartCoroutine(RunRoutine(CameraLookAtRoutine(cameras.CameraAim, stepStartTime, cameras.CameraAim.IsCameraAimingUseMain), onCancelCam));
                }
            }

            // Lock (독립)
            if (step.Flags.IsLock && step.Lock != null)
            {
                if (step.Lock.IsLockMove) { lockMoveCount++; isLockMove = true; context.AcquiredMoveLocks++; }
                if (step.Lock.IsLockCamera) { lockCameraCount++; isLockCamera = true; context.AcquiredCameraLocks++; }
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
                Action onCancelChoice = () => { try { CleanupChoiceUIIfAny(); } catch { } try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; isLockMove = false; isLockCamera = false; } catch { } };
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
                if (ctx.AcquiredMoveLocks > 0) { lockMoveCount = Math.Max(0, lockMoveCount - ctx.AcquiredMoveLocks); isLockMove = lockMoveCount > 0; }
                if (ctx.AcquiredCameraLocks > 0) { lockCameraCount = Math.Max(0, lockCameraCount - ctx.AcquiredCameraLocks); isLockCamera = lockCameraCount > 0; }
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

        private IEnumerator CameraMoveRoutine(CameraMovementData movement, float stepStartTime, bool useMain)
        {
            if (movement == null) yield break;
            Vector3 LocalToWorld(Transform center, Vector3 local) { return center.position + center.right * local.x + center.up * local.y + center.forward * local.z; }
            if (useMain)
            {
                Camera camComp = Camera.main; if (camComp == null) yield break;
                float prevDepth = camComp.depth; Transform camTransform = camComp.transform; camComp.depth = 20f;
                while (Time.time - stepStartTime < movement.StartTime) yield return null;
                Vector3 startWorld, goalWorld;
                if (movement.IsRelative && !string.IsNullOrEmpty(movement.CenterObject)) { var centerGO = ResolveByName(movement.CenterObject); if (centerGO != null) { var centerT = centerGO.transform; startWorld = LocalToWorld(centerT, movement.StartPosition); goalWorld = LocalToWorld(centerT, movement.EndPosition); } else { startWorld = movement.StartPosition; goalWorld = movement.EndPosition; } }
                else { startWorld = movement.StartPosition; goalWorld = movement.EndPosition; }
                float duration = Mathf.Max(0.0001f, movement.EndTime - movement.StartTime);
                camTransform.position = startWorld; float localStart = Time.time;
                while (true) { float k = Mathf.Clamp01((Time.time - localStart) / duration); camTransform.position = Vector3.Lerp(startWorld, goalWorld, k); if (k >= 1f) break; yield return null; }
                camComp.depth = prevDepth; yield break;
            }
            var camGO = GetOrCreateEventCamera(); if (camGO == null) yield break;
            Camera cam = camGO.GetComponent<Camera>(); if (cam == null) yield break;
            Transform camT = cam.transform;
            if (eventCameraUserCount == 0) { eventCameraPrevDepth = cam.depth; eventCameraDepthStored = true; cam.depth = 20; }
            Camera mainCamera = Camera.main;
            if (mainCamera == null) { eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1); yield break; }
            eventCameraUserCount++; camGO.transform.position = mainCamera.transform.position; camGO.transform.rotation = mainCamera.transform.rotation; camGO.SetActive(true);
            while (Time.time - stepStartTime < movement.StartTime) yield return null;
            Vector3 s, g;
            if (movement.IsRelative && !string.IsNullOrEmpty(movement.CenterObject)) { var centerGO = ResolveByName(movement.CenterObject); if (centerGO != null) { var centerT = centerGO.transform; s = LocalToWorld(centerT, movement.StartPosition); g = LocalToWorld(centerT, movement.EndPosition); } else { s = movement.StartPosition; g = movement.EndPosition; } }
            else { s = movement.StartPosition; g = movement.EndPosition; }
            float dur = Mathf.Max(0.0001f, movement.EndTime - movement.StartTime);
            camT.position = s; float localStart2 = Time.time;
            while (true) { float k = Mathf.Clamp01((Time.time - localStart2) / dur); camT.position = Vector3.Lerp(s, g, k); if (k >= 1f) break; yield return null; }
            eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1);
            if (eventCameraUserCount == 0) { if (eventCameraDepthStored) cam.depth = eventCameraPrevDepth; camGO.SetActive(false); eventCameraDepthStored = false; }
        }

        private IEnumerator CameraLookAtRoutine(CameraAimData aim, float stepStartTime, bool useMain)
        {
            if (aim == null) yield break;
            Vector3 GetTargetWorldPosWithLocalOffset(Transform target, Vector3 localOffset) { if (target == null) return Vector3.zero; return target.position + target.right * localOffset.x + target.up * localOffset.y + target.forward * localOffset.z; }
            if (useMain)
            {
                Camera camComp = Camera.main; if (camComp == null) yield break;
                float prevDepth = camComp.depth; Transform camTransform = camComp.transform; camComp.depth = 20f;
                while (Time.time - stepStartTime < aim.StartTime) yield return null;
                float endTime = Mathf.Max(aim.EndTime, aim.StartTime + 0.0001f);
                GameObject target = ResolveByName(aim.aimedTargetName); Transform targetT = target != null ? target.transform : null;
                if (targetT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); Vector3 dir0 = aimPos - camTransform.position; Quaternion goal = Quaternion.LookRotation(dir0); float initDur = Mathf.Max(0f, aim.InitDuration); if (initDur <= 0f) { camTransform.rotation = goal; } else { Quaternion startRot = camTransform.rotation; float elapsed = 0f; while (elapsed < initDur) { aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); dir0 = aimPos - camTransform.position; goal = Quaternion.LookRotation(dir0); elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / initDur); camTransform.rotation = Quaternion.Slerp(startRot, goal, k); yield return null; } camTransform.rotation = goal; } }
                while (Time.time - stepStartTime < endTime) { if (targetT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(targetT, aim.TargetLocalOffset); Vector3 dir = aimPos - camTransform.position; camTransform.rotation = Quaternion.LookRotation(dir); } yield return null; }
                camComp.depth = prevDepth; yield break;
            }
            var camGO2 = GetOrCreateEventCamera(); if (camGO2 == null) yield break;
            Camera camComp2 = camGO2.GetComponent<Camera>(); if (camComp2 == null) yield break;
            Transform camTransform2 = camComp2.transform;
            if (eventCameraUserCount == 0) { eventCameraPrevDepth = camComp2.depth; eventCameraDepthStored = true; camComp2.depth = 20; }
            Camera aimMainCamera = Camera.main;
            if (aimMainCamera != null) { camTransform2.position = aimMainCamera.transform.position; camTransform2.rotation = aimMainCamera.transform.rotation; }
            eventCameraUserCount++; camGO2.SetActive(true);
            while (Time.time - stepStartTime < aim.StartTime) yield return null;
            float endT = Mathf.Max(aim.EndTime, aim.StartTime + 0.0001f);
            GameObject tgt = ResolveByName(aim.aimedTargetName); Transform tgtT = tgt != null ? tgt.transform : null;
            if (tgtT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); Vector3 dir0 = aimPos - camTransform2.position; Quaternion goal = Quaternion.LookRotation(dir0); float initDur = Mathf.Max(0f, aim.InitDuration); if (initDur <= 0f) { camTransform2.rotation = goal; } else { Quaternion startRot = camTransform2.rotation; float elapsed = 0f; while (elapsed < initDur) { aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); dir0 = aimPos - camTransform2.position; goal = Quaternion.LookRotation(dir0); elapsed += Time.deltaTime; float k = Mathf.Clamp01(elapsed / initDur); camTransform2.rotation = Quaternion.Slerp(startRot, goal, k); yield return null; } camTransform2.rotation = goal; } }
            while (Time.time - stepStartTime < endT) { if (tgtT != null) { Vector3 aimPos = GetTargetWorldPosWithLocalOffset(tgtT, aim.TargetLocalOffset); Vector3 dir = aimPos - camTransform2.position; camTransform2.rotation = Quaternion.LookRotation(dir); } yield return null; }
            eventCameraUserCount = Mathf.Max(0, eventCameraUserCount - 1);
            if (eventCameraUserCount == 0) { if (eventCameraDepthStored) camComp2.depth = eventCameraPrevDepth; camGO2.SetActive(false); eventCameraDepthStored = false; }
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
            var prevCursorLock = Cursor.lockState; var prevCursorVisible = Cursor.visible; bool prevIsLockMove = isLockMove; bool prevIsLockCamera = isLockCamera;
            isLockMove = true; isLockCamera = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
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
                RestoreCursorAndLock(prevCursorLock, prevCursorVisible, prevIsLockMove, prevIsLockCamera);
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
            if (sd.isLoop) { if (eventAudioSource.clip != sd.audioClip || !eventAudioSource.isPlaying) { eventAudioSource.clip = sd.audioClip; eventAudioSource.loop = true; eventAudioSource.volume = Mathf.Clamp01(sd.volume); eventAudioSource.Play(); } }
            else { if (eventAudioSource.loop) { eventAudioSource.Stop(); eventAudioSource.loop = false; } eventAudioSource.PlayOneShot(sd.audioClip, Mathf.Clamp01(sd.volume)); }
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

        private void RestoreCursorAndLock(CursorLockMode prevLock, bool prevVisible, bool prevMove, bool prevCam) { Cursor.lockState = prevLock; Cursor.visible = prevVisible; isLockMove = prevMove; isLockCamera = prevCam; }

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

        private IEnumerator MoveRoutineCustom(GameObject target, Vector3 startPos, Vector3 goalPos, float duration, MoveObjectSubData move, bool useLocal)
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
                        t += Time.deltaTime; float k = Mathf.Clamp01(t / duration);
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
