using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Editor
{
    public sealed class ObjectPrefabEntry
    {
        public GameObject Prefab;
        public ObjectDefinitionSO Definition;
        public ObjectKind Kind;
        public string Path, Problem, Note;
        public bool HasActor;
        public bool Supported => Definition != null && string.IsNullOrEmpty(Problem);
    }

    /// <summary>Read-only inspection. Import opens a linked draft; it never converts or saves a Prefab.</summary>
    public static class ObjectPrefabCatalog
    {
        public static ObjectPrefabEntry Inspect(Object source)
        {
            var entry = new ObjectPrefabEntry();
            if (source is ObjectDefinitionSO definition)
            {
                entry.Definition = definition;
                entry.Prefab = definition.GeneratedPrefab;
                entry.Kind = definition.Data.kind;
                entry.Path = AssetDatabase.GetAssetPath(definition);
                if (string.IsNullOrEmpty(entry.Path)) entry.Problem = "프로젝트에 저장된 Definition이 아닙니다.";
                else if (entry.Kind != ObjectKind.Monster && entry.Kind != ObjectKind.Player) entry.Problem = "Monster / Player Definition만 지원합니다. 원본 종류는 변경하지 않습니다.";
                return entry;
            }
            var go = source as GameObject ?? (source as Component)?.gameObject;
            if (go == null) { entry.Problem = "ObjectActor2D가 연결된 Prefab 또는 Definition을 넣어주세요."; return entry; }
            bool scene = !EditorUtility.IsPersistent(go);
            ObjectActor2D actor;
            if (scene)
            {
                actor = go.GetComponentInParent<ObjectActor2D>(true);
                if (actor == null)
                {
                    var children = go.GetComponentsInChildren<ObjectActor2D>(true);
                    if (children.Length == 1) actor = children[0];
                    else if (children.Length > 1) { entry.HasActor = true; entry.Problem = "ObjectActor2D가 여러 개입니다. 편집할 오브젝트 하나를 선택하세요."; return entry; }
                }
                if (actor != null)
                {
                    entry.HasActor = true;
                    var linked = actor.Definition;
                    if (linked == null) { entry.Problem = "ObjectActor2D의 Definition 연결이 없습니다."; return entry; }
                    entry = Inspect(linked); entry.HasActor = true;
                    entry.Note = "씬 인스턴스의 연결 Definition을 엽니다. 씬의 컴포넌트 Override는 가져오지 않습니다.";
                    return entry;
                }
            }
            string path = AssetDatabase.GetAssetPath(go);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            entry.Path = path; entry.Prefab = prefab;
            if (prefab == null) { entry.Problem = "연결된 ObjectActor2D 또는 Prefab 에셋이 없습니다."; return entry; }
            var actors = prefab.GetComponentsInChildren<ObjectActor2D>(true);
            entry.HasActor = actors.Length > 0;
            if (actors.Length != 1) { entry.Problem = actors.Length == 0 ? "ObjectActor2D 컴포넌트가 없는 Prefab입니다." : "ObjectActor2D가 여러 개여서 편집 대상을 정할 수 없습니다."; return entry; }
            actor = actors[0]; entry.Definition = actor.Definition;
            if (entry.Definition == null) { entry.Problem = "ObjectActor2D의 Definition 연결이 없습니다."; return entry; }
            entry.Kind = entry.Definition.Data.kind;
            if (actor.gameObject != prefab) entry.Problem = "ObjectActor2D가 루트가 아닌 자식에 있습니다. 이 구조는 자동 Modify를 지원하지 않습니다.";
            else if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Model) entry.Problem = "Model Prefab은 자동 Modify를 지원하지 않습니다.";
            else if (entry.Kind != ObjectKind.Monster && entry.Kind != ObjectKind.Player) entry.Problem = "Monster / Player가 아닌 종류입니다.";
            else if (entry.Definition.GeneratedPrefab != prefab) entry.Problem = "Definition이 다른 Prefab을 가리킵니다. 복제본 / Variant를 원본으로 덮어쓰지 않습니다. 연결 Definition을 확인하세요.";
            return entry;
        }

        public static List<ObjectPrefabEntry> Scan() => AssetDatabase.FindAssets("t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath).Select(p => Inspect(AssetDatabase.LoadAssetAtPath<GameObject>(p)))
            .Where(e => e.HasActor).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

        public static string ApplyBlocker(ObjectDefinitionSO source)
        {
            if (source == null) return null;
            var controller = source.Data.animatorController as AnimatorController;
            string controllerPath = AssetDatabase.GetAssetPath(source.Data.animatorController).Replace('\\', '/');
            if (source.Data.animatorController != null &&
                (controller == null || !(controllerPath.StartsWith(ObjectMakerPaths.GeneratedAnimators + "/", StringComparison.Ordinal) ||
                                        controllerPath.StartsWith("Assets/2DObjectMaker/", StringComparison.Ordinal))))
                return "사용자 Animator Controller를 유지하기 위해 자동 Modify를 중단합니다. 공용 생성 Animator 연결을 확인하세요. 초안과 원본은 보존됩니다.";
            var prefab = source.GeneratedPrefab;
            if (prefab == null) return null;
            var entry = Inspect(prefab);
            if (!entry.Supported) return entry.Problem;
            if (entry.Definition != source) return "Definition과 Prefab의 연결이 서로 다릅니다. 다른 설정을 덮어쓰지 않습니다.";
            var actor = prefab.GetComponent<ObjectActor2D>();
            if (actor != null && actor.Animator != null && actor.Animator.runtimeAnimatorController != source.Data.animatorController)
                return "Prefab의 Animator와 Definition의 Animator 연결이 다릅니다. 사용자 Animator를 덮어쓰지 않습니다. 연결을 확인하세요.";
            return null;
        }
    }
}
