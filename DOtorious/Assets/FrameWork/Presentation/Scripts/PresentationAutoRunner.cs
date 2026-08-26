using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using JYW.FrameWork;

namespace JYW.Game.EventPlay
{
    /// <summary>
    /// 로드된 게임 씬마다 Resources/Presentation/{씬 이름}의 Condition EventSO를 감시하고,
    /// 조건을 만족한 에셋을 씬 로드 순서와 이름순으로 한 번 실행한다.
    /// None EventSO는 자동 실행하지 않으며 기존 EventPlayManager.PlayEvent 호출로만 실행한다.
    /// Presentation 씬은 EventPlayManager.Instance가 자동으로 Additive 로드한다.
    /// </summary>
    internal sealed class PresentationAutoRunner : MonoBehaviour
    {
        private const float ManagerAcquireTimeout = 30f;
        private const string ResourceRoot = "Presentation";

        private sealed class PendingSceneEvent
        {
            public Scene Scene;
            public string SceneName;
            public EventSO Asset;
            public EventSO.EventStep CapturedStep;
            public EventPlayManager ArmedManager;
        }

        private static PresentationAutoRunner instance;
        private readonly HashSet<Scene> registeredScenes = new HashSet<Scene>();
        private readonly List<PendingSceneEvent> pendingEvents = new List<PendingSceneEvent>();
        private Coroutine monitorCoroutine;
        private int completedCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation)) return;
            if (instance != null) return;

            var host = new GameObject("PresentationAutoRunner");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PresentationAutoRunner>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            // AfterSceneLoad에서 생성되므로 최초 씬의 sceneLoaded 콜백은 이미 끝났을 수 있다.
            // 현재 로드 순서를 그대로 사용해 최초 씬과 에디터의 다중 씬 시작을 등록한다.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                RegisterScene(SceneManager.GetSceneAt(i));

            if (monitorCoroutine == null)
                monitorCoroutine = StartCoroutine(MonitorPendingEvents());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            if (monitorCoroutine == null) return;
            StopCoroutine(monitorCoroutine);
            monitorCoroutine = null;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RegisterScene(scene);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            registeredScenes.Remove(scene);
            pendingEvents.RemoveAll(item => item.Scene == scene);
        }

        private void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.name) ||
                string.Equals(scene.name, EventPlayManager.OwningSceneName, StringComparison.Ordinal) ||
                !registeredScenes.Add(scene))
                return;

            string resourcePath = ResourceRoot + "/" + scene.name;
            List<EventSO> sceneEvents = Resources.LoadAll<EventSO>(resourcePath)
                .Where(asset => asset != null && asset.UseCondition)
                .OrderBy(asset => asset.name, StringComparer.Ordinal)
                .ToList();

            if (sceneEvents.Count == 0)
            {
                Debug.Log($"[Presentation] Resources/{resourcePath}에서 자동 감시할 Condition EventSO를 찾지 못했습니다. None EventSO는 PlayEvent로 호출해야 합니다.");
                return;
            }

            string[] duplicateNames = sceneEvents
                .GroupBy(asset => asset.name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateNames.Length > 0)
                Debug.LogWarning($"[Presentation] 씬 '{scene.name}'에서 이름이 같은 EventSO도 모두 실행합니다: {string.Join(", ", duplicateNames)}");

            for (int i = 0; i < sceneEvents.Count; i++)
            {
                pendingEvents.Add(new PendingSceneEvent
                {
                    Scene = scene,
                    SceneName = scene.name,
                    Asset = sceneEvents[i]
                });
            }

            Debug.Log($"[Presentation] 씬 '{scene.name}'의 Condition EventSO {sceneEvents.Count}개를 자동 감시합니다. (Resources/{resourcePath})");

            EventPlayManager manager = EventPlayManager.Instance;
            if (IsManagerAvailable(manager))
                ArmPendingEvents(manager);
        }

        private IEnumerator MonitorPendingEvents()
        {
            float unavailableSince = -1f;
            bool unavailableReported = false;

            // 사용자 씬의 Start와 명시적 PlayEvent가 실행될 기회를 보장한다.
            yield return null;

            while (true)
            {
                if (pendingEvents.Count == 0)
                {
                    unavailableSince = -1f;
                    unavailableReported = false;
                    yield return null;
                    continue;
                }

                EventPlayManager manager = EventPlayManager.Instance;
                if (!IsManagerAvailable(manager))
                {
                    if (unavailableSince < 0f)
                        unavailableSince = Time.realtimeSinceStartup;
                    if (!unavailableReported &&
                        Time.realtimeSinceStartup - unavailableSince >= ManagerAcquireTimeout)
                    {
                        unavailableReported = true;
                        Debug.LogError("[Presentation] EventPlayManager를 준비하지 못했습니다. 씬별 Condition EventSO 감시는 복구될 때까지 대기합니다.");
                    }

                    yield return null;
                    continue;
                }

                unavailableSince = -1f;
                unavailableReported = false;
                ArmPendingEvents(manager);

                // 실행 중인 다른 이벤트가 있어도 조건 충족 순간은 기억한다.
                for (int i = 0; i < pendingEvents.Count; i++)
                {
                    PendingSceneEvent candidate = pendingEvents[i];
                    if (candidate.CapturedStep == null &&
                        manager.TryGetAutoConditionStep(candidate.Asset, gameObject, out EventSO.EventStep step))
                        candidate.CapturedStep = step;
                }

                PendingSceneEvent readyEvent = pendingEvents.FirstOrDefault(item => item.CapturedStep != null);
                if (readyEvent == null || manager.HasRunningEvents)
                {
                    yield return null;
                    continue;
                }

                // 같은 프레임에 시작된 수동 이벤트를 취소하지 않도록 한 프레임 양보한다.
                yield return null;
                if (!pendingEvents.Contains(readyEvent) ||
                    !manager.TryPlayCapturedEventIfIdle(readyEvent.Asset, gameObject, readyEvent.CapturedStep))
                    continue;

                pendingEvents.Remove(readyEvent);
                completedCount++;
                Debug.Log($"[Presentation] Condition EventSO 실행 ({completedCount}, 씬 '{readyEvent.SceneName}'): {readyEvent.Asset.name}");

                while (manager != null && manager.HasRunningEvents)
                    yield return null;

                yield return null;
            }
        }

        private void ArmPendingEvents(EventPlayManager manager)
        {
            for (int i = 0; i < pendingEvents.Count; i++)
            {
                PendingSceneEvent pending = pendingEvents[i];
                if (pending.ArmedManager == manager) continue;
                manager.ArmCollisionConditions(pending.Asset);
                pending.ArmedManager = manager;
            }
        }

        private static bool IsManagerAvailable(EventPlayManager manager)
        {
            return manager != null && manager.isActiveAndEnabled && manager.gameObject.activeInHierarchy;
        }
    }
}
