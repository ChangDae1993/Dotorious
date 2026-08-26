using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker
{
    public static class ObjectMakerRegistry
    {
        private static readonly List<ObjectActor2D> ActorsInternal = new List<ObjectActor2D>();

        public static IReadOnlyList<ObjectActor2D> Actors => ActorsInternal;

        public static void Register(ObjectActor2D actor)
        {
            if (actor != null && !ActorsInternal.Contains(actor))
                ActorsInternal.Add(actor);
        }

        public static void Unregister(ObjectActor2D actor)
        {
            ActorsInternal.Remove(actor);
        }

        public static ObjectActor2D FindClosestPlayer(Vector2 position, LayerMask targetMask)
        {
            ObjectActor2D closest = null;
            float closestSqrDistance = float.PositiveInfinity;
            for (int i = ActorsInternal.Count - 1; i >= 0; i--)
            {
                ObjectActor2D candidate = ActorsInternal[i];
                if (candidate == null)
                {
                    ActorsInternal.RemoveAt(i);
                    continue;
                }

                if (!candidate.isActiveAndEnabled || candidate.IsDead ||
                    candidate.Kind != ObjectKind.Player ||
                    (targetMask.value & (1 << candidate.gameObject.layer)) == 0)
                    continue;

                float sqrDistance = ((Vector2)candidate.transform.position - position).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance)
                    continue;

                closest = candidate;
                closestSqrDistance = sqrDistance;
            }

            return closest;
        }

        internal static void Reset()
        {
            ActorsInternal.Clear();
        }
    }

    /// <summary>
    /// 모든 로드 씬과 Additive 씬을 자동 등록하고 Definition의 씬 배치 규칙을 실행한다.
    /// 별도 매니저 씬이나 Build Settings 등록이 필요하지 않다.
    /// </summary>
    internal sealed class ObjectMakerAutoRunner : MonoBehaviour
    {
        private const string DefinitionResourcePath = "2DObjectMaker/Definitions";
        private const string LegacyDefinitionResourcePath = "ObjectMaker/Definitions";

        private readonly HashSet<string> spawnedRuleKeys = new HashSet<string>(StringComparer.Ordinal);
        private static ObjectMakerAutoRunner instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            ObjectMakerRegistry.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                return;

            if (instance != null)
                return;

            var host = new GameObject("2DObjectMakerAutoRunner");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ObjectMakerAutoRunner>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                RegisterScene(SceneManager.GetSceneAt(i));
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
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
            string prefix = scene.handle + ":";
            spawnedRuleKeys.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        private void RegisterScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(scene.name))
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ObjectActor2D[] actors = roots[i].GetComponentsInChildren<ObjectActor2D>(true);
                for (int j = 0; j < actors.Length; j++)
                    if (actors[j].isActiveAndEnabled)
                        ObjectMakerRegistry.Register(actors[j]);
            }

            SpawnDefinitionsForScene(scene);
        }

        private void SpawnDefinitionsForScene(Scene scene)
        {
            ObjectDefinitionSO[] definitions = LoadDefinitions();
            Array.Sort(definitions, (left, right) =>
                string.CompareOrdinal(left != null ? left.name : string.Empty,
                    right != null ? right.name : string.Empty));

            for (int definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
            {
                ObjectDefinitionSO definition = definitions[definitionIndex];
                if (definition == null || definition.GeneratedPrefab == null ||
                    definition.Data.sceneSpawns == null)
                    continue;

                for (int ruleIndex = 0; ruleIndex < definition.Data.sceneSpawns.Count; ruleIndex++)
                {
                    ObjectSceneSpawnRule rule = definition.Data.sceneSpawns[ruleIndex];
                    if (rule == null || !rule.enabled ||
                        !string.Equals(rule.sceneName, scene.name, StringComparison.Ordinal))
                        continue;

#if UNITY_6000_5_OR_NEWER
                    string definitionId = definition.GetEntityId().ToString();
#else
                    string definitionId = definition.GetInstanceID().ToString();
#endif
                    string key = scene.handle + ":" + definitionId + ":" + ruleIndex;
                    if (!spawnedRuleKeys.Add(key))
                        continue;

                    for (int spawnIndex = 0; spawnIndex < Mathf.Max(1, rule.count); spawnIndex++)
                    {
                        Vector3 position = rule.position + rule.spacing * spawnIndex;
                        Quaternion rotation = Quaternion.Euler(rule.eulerAngles);
                        GameObject spawned = Instantiate(definition.GeneratedPrefab, position, rotation);
                        spawned.name = definition.Data.displayName +
                                       (rule.count > 1 ? "_" + (spawnIndex + 1) : string.Empty);
                        SceneManager.MoveGameObjectToScene(spawned, scene);
                    }
                }
            }
        }

        private static ObjectDefinitionSO[] LoadDefinitions()
        {
            var results = new List<ObjectDefinitionSO>();
            var seen = new HashSet<ObjectDefinitionSO>();
            AddDefinitions(DefinitionResourcePath, results, seen);
            AddDefinitions(LegacyDefinitionResourcePath, results, seen);
            return results.ToArray();
        }

        private static void AddDefinitions(
            string resourcePath,
            List<ObjectDefinitionSO> results,
            HashSet<ObjectDefinitionSO> seen)
        {
            ObjectDefinitionSO[] loaded = Resources.LoadAll<ObjectDefinitionSO>(resourcePath);
            for (int i = 0; i < loaded.Length; i++)
            {
                ObjectDefinitionSO definition = loaded[i];
                if (definition != null && seen.Add(definition))
                    results.Add(definition);
            }
        }
    }
}
