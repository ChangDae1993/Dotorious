using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker.Editor
{
    [InitializeOnLoad]
    public sealed class ObjectMakerAutoSetup : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string SessionKey = "JYW.2DObjectMaker.AutoSetup.v1";
        private static bool queued;
        private static bool running;

        static ObjectMakerAutoSetup()
        {
            EditorApplication.delayCall += RunOnce;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker) ||
                running || importedAssets == null ||
                !importedAssets.Any(path =>
                    path.IndexOf("/2DObjectMaker/", StringComparison.Ordinal) >= 0 ||
                    path.IndexOf("/ObjectMaker/", StringComparison.Ordinal) >= 0))
                return;
            Queue();
        }

        public static void RunFromCommandLine()
        {
            bool success = RunSetup(true);
            if (Application.isBatchMode)
                EditorApplication.Exit(success ? 0 : 1);
        }

        private static void RunOnce()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                return;

            if (SessionState.GetBool(SessionKey, false))
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += RunOnce;
                return;
            }

            if (RunSetup(false))
                SessionState.SetBool(SessionKey, true);
        }

        private static void Queue()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                return;

            if (queued)
                return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                    EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Queue();
                    return;
                }
                RunSetup(false);
            };
        }

        private static bool RunSetup(bool verbose)
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                return true;

            if (running)
                return false;

            running = true;
            try
            {
                ObjectMakerPaths.EnsureStructure();
                if (verbose)
                    Debug.Log("[2DObjectMaker] 폴더 구조와 자동 멀티씬 런타임을 확인했습니다.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[2DObjectMaker] 자동 설정에 실패했습니다.\n" + exception);
                return false;
            }
            finally
            {
                running = false;
            }
        }

        public int callbackOrder => -9000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                return;

            if (!RunSetup(false))
                throw new BuildFailedException("2DObjectMaker 자동 설정에 실패했습니다.");

            string[] definitionGuids = AssetDatabase.FindAssets("t:ObjectDefinitionSO");
            for (int i = 0; i < definitionGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
                ObjectDefinitionSO definition = AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(path);
                if (definition != null && definition.Data.sceneSpawns.Any(rule =>
                        rule != null && rule.enabled && !string.IsNullOrWhiteSpace(rule.sceneName)) &&
                    definition.GeneratedPrefab == null)
                {
                    throw new BuildFailedException(
                        "자동 씬 배치가 설정된 ObjectDefinitionSO에 생성 프리팹이 없습니다: " + path);
                }
            }
        }
    }
}
