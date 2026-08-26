using System;
using UnityEditor;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    [CustomEditor(typeof(ObjectDefinitionSO))]
    public sealed class ObjectDefinitionSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Open 2DObjectMaker (Modify)", GUILayout.Height(28f)))
                ObjectMakerWindow.Open((ObjectDefinitionSO)target);

            if (GUILayout.Button("Modify Generated Prefab", GUILayout.Height(28f)))
            {
                var definition = (ObjectDefinitionSO)target;
                try
                {
                    ObjectMakerBuildResult result = ObjectMakerPrefabBuilder.Make(
                        definition.Data,
                        definition);
                    EditorGUIUtility.PingObject(result.Prefab);
                    Debug.Log("[2DObjectMaker] Modify 완료: " + result.PrefabPath);
                }
                catch (Exception exception)
                {
                    Debug.LogError("[2DObjectMaker] Modify 실패\n" + exception);
                }
            }
        }
    }
}
