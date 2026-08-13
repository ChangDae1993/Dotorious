using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    /// <summary>
    /// 모든 Resources 폴더의 EventSO를 이름순으로 읽어 하나씩 완료한 뒤 다음 이벤트를 실행한다.
    /// Presentation 씬은 EventPlayManager.Instance가 자동으로 Additive 로드한다.
    /// </summary>
    internal sealed class PresentationAutoRunner : MonoBehaviour
    {
        private const float ManagerAcquireTimeout = 30f;

        private static PresentationAutoRunner instance;
        private bool sequenceRunning;
        private bool sequenceCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var host = new GameObject("PresentationAutoRunner");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PresentationAutoRunner>();
        }

        private IEnumerator Start()
        {
            if (sequenceRunning || sequenceCompleted) yield break;

            sequenceRunning = true;
            try
            {
                // 사용자 씬의 Awake/OnEnable/Start와 명시적 PlayEvent가 먼저 실행될 기회를 보장한다.
                yield return null;

                EventSO[] eventAssets = Resources.LoadAll<EventSO>(string.Empty)
                    .Where(asset => asset != null)
                    .OrderBy(asset => asset.name, StringComparer.Ordinal)
                    .ToArray();

                if (eventAssets.Length == 0)
                {
                    Debug.Log("[Presentation] Resources에서 자동 실행할 EventSO를 찾지 못했습니다.");
                    yield break;
                }

                string[] duplicateNames = eventAssets
                    .GroupBy(asset => asset.name, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToArray();
                if (duplicateNames.Length > 0)
                    Debug.LogWarning($"[Presentation] 이름이 같은 EventSO도 모두 실행합니다: {string.Join(", ", duplicateNames)}");

                Debug.Log($"[Presentation] Resources EventSO {eventAssets.Length}개를 이름순으로 자동 실행합니다.");

                for (int index = 0; index < eventAssets.Length; index++)
                {
                    EventSO eventAsset = eventAssets[index];
                    EventPlayManager manager = null;
                    float unavailableSince = -1f;

                    // 수동 이벤트가 실행 중이면 건드리지 않고 끝날 때까지 기다린다.
                    // SceneChange로 manager가 파괴되면 다음 프레임에 Presentation 씬을 다시 Additive 로드한다.
                    while (true)
                    {
                        manager = EventPlayManager.Instance;
                        bool managerAvailable = manager != null && manager.isActiveAndEnabled &&
                                                manager.gameObject.activeInHierarchy;
                        if (!managerAvailable)
                        {
                            if (unavailableSince < 0f) unavailableSince = Time.realtimeSinceStartup;
                            if (Time.realtimeSinceStartup - unavailableSince >= ManagerAcquireTimeout)
                            {
                                Debug.LogError("[Presentation] EventPlayManager를 준비하지 못해 EventSO 자동 실행을 중단합니다.");
                                yield break;
                            }

                            yield return null;
                            continue;
                        }

                        unavailableSince = -1f;
                        if (manager.HasRunningEvents)
                        {
                            yield return null;
                            continue;
                        }

                        // 같은 프레임의 다른 호출이 시작한 이벤트를 취소하지 않도록 한 프레임 재확인한다.
                        yield return null;
                        if (manager != null && manager.TryPlayEventIfIdle(eventAsset, gameObject))
                            break;
                    }

                    Debug.Log($"[Presentation] EventSO 자동 실행 ({index + 1}/{eventAssets.Length}): {eventAsset.name}");

                    while (manager != null && manager.HasRunningEvents)
                        yield return null;

                    // 완료 정리와 다음 이벤트 사이에 한 프레임을 둔다.
                    yield return null;
                }

                sequenceCompleted = true;
                Debug.Log("[Presentation] Resources EventSO 자동 실행을 모두 완료했습니다.");
            }
            finally
            {
                sequenceRunning = false;
            }
        }
    }
}
