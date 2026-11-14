using System;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors real EventSO collision conditions; never invokes an event or adds a dispatcher.</summary>
public static class EventConditionRegion2DPlacement
{
    public static EventConditionRegion2D Assign(EventSO asset, BoxCollider2D region, Collider2D actor, int conditionIndex = 0)
    {
        Validate(asset, region, actor, conditionIndex);
        string a = Locator(actor), b = Locator(region);
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        const string label = "Author EventSO 2D Contact Condition";
        Undo.SetCurrentGroupName(label);
        try
        {
            Undo.RegisterCompleteObjectUndo(asset, label);
            Undo.RegisterCompleteObjectUndo(region, label);
            // Set before AddComponent so its whole-object redo snapshot includes the trigger state.
            region.isTrigger = true;
            var binding = region.GetComponent<EventConditionRegion2D>() ?? Undo.AddComponent<EventConditionRegion2D>(region.gameObject);
            Undo.RegisterCompleteObjectUndo(binding, label);
            if (asset.Conditions == null) asset.Conditions = Array.Empty<EventSO.ConditionGroupData>();
            if (conditionIndex == asset.Conditions.Length) Array.Resize(ref asset.Conditions, conditionIndex + 1);
            var condition = asset.Conditions[conditionIndex] ?? (asset.Conditions[conditionIndex] = new EventSO.ConditionGroupData());
            condition.Checks |= EventSO.ConditionChecks.CollisionAB;
            condition.CollisionObjectA = a;
            condition.CollisionObjectB = b;
            condition.CollisionMode = EventSO.CollisionCheckMode.CurrentContact2D;
            asset.UseCondition = true;
            region.isTrigger = true;
            binding.Event = asset;
            binding.Actor = actor;
            binding.ConditionIndex = conditionIndex;
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(region);
            EditorUtility.SetDirty(binding);
            PrefabUtility.RecordPrefabInstancePropertyModifications(region);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binding);
            EditorSceneManager.MarkSceneDirty(region.gameObject.scene);
            Undo.CollapseUndoOperations(undo);
            return binding;
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    public static EventConditionRegion2D Create(EventSO asset, Collider2D actor, Vector3 position, Vector2 size, int conditionIndex = 0)
    {
        if (actor == null) throw new ArgumentException("같은 씬의 플레이어 Collider2D를 선택하세요.");
        if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(size.x) || !Finite(size.y) || size.x <= 0 || size.y <= 0)
            throw new ArgumentException("위치와 크기는 유한해야 하며 크기는 0보다 커야 합니다.");
        ValidateScene(actor.gameObject.scene);
        if (asset == null || !AssetDatabase.Contains(asset)) throw new ArgumentException("저장된 EventSO를 선택하세요.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        GameObject created = null;
        try
        {
            created = new GameObject(GameObjectUtility.GetUniqueNameForSibling(null, "Start_" + asset.name));
            SceneManager.MoveGameObjectToScene(created, actor.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(created, "Create EventSO Start Region");
            Undo.RegisterCompleteObjectUndo(created.transform, "Position EventSO Start Region");
            created.transform.position = position;
            var region = Undo.AddComponent<BoxCollider2D>(created);
            Undo.RegisterCompleteObjectUndo(region, "Size EventSO Start Region");
            region.size = size;
            var result = Assign(asset, region, actor, conditionIndex);
            Undo.CollapseUndoOperations(group);
            return result;
        }
        catch { Undo.RevertAllDownToGroup(group); if (created != null) UnityEngine.Object.DestroyImmediate(created); throw; }
    }

    private static void Validate(EventSO asset, BoxCollider2D region, Collider2D actor, int index)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("플레이 모드 밖에서 조건을 작성하세요.");
        if (asset == null || !AssetDatabase.Contains(asset)) throw new ArgumentException("저장된 EventSO를 선택하세요.");
        if (region == null || actor == null || region == actor) throw new ArgumentException("서로 다른 시작 구역과 플레이어 Collider2D를 선택하세요.");
        ValidateScene(region.gameObject.scene);
        ValidateScene(actor.gameObject.scene);
        if (region.gameObject.scene != actor.gameObject.scene) throw new ArgumentException("구역과 플레이어는 같은 씬이어야 합니다.");
        if (index < 0 || index > (asset.Conditions?.Length ?? 0)) throw new ArgumentOutOfRangeException(nameof(index), "기존 분기 또는 바로 다음 분기를 선택하세요.");
        if (region.GetComponentsInChildren<Collider2D>(true).Length != 1 || actor.GetComponentsInChildren<Collider2D>(true).Length != 1)
            throw new ArgumentException("대상이 모호하지 않도록 Collider2D 하나만 가진 오브젝트를 선택하세요.");
    }

    private static void ValidateScene(Scene scene)
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null || !scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
            throw new ArgumentException("Prefab 모드를 나가 일반 씬의 오브젝트를 선택하세요.");
    }

    private static string Locator(Component target)
    {
        if (!EventSceneTargetField.TryLocator(target, out _, out string error)) throw new ArgumentException(error);
        string path = EventSceneTargetField.HierarchyPath(target.gameObject);
        foreach (Transform t in target.GetComponentsInParent<Transform>(true))
            if (t.name.Contains("/")) throw new ArgumentException("대상 계층의 이름에 / 문자를 사용할 수 없습니다.");
        if (EventSceneTargetField.FindMatches(path).Length != 1) throw new ArgumentException("대상 계층 경로가 중복됩니다.");
        return path;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    [InitializeOnLoadMethod]
    private static void Describe()
    {
        PresentationEventSourceDescriptions.Register(typeof(EventConditionRegion2D).FullName, (source, path, asset) =>
        {
            if (path != "eventSO") return null;
            var binding = (EventConditionRegion2D)source;
            return "시작 Collider2D · 조건 " + (binding.ConditionIndex + 1) + ": EventSO의 A↔B 현재 2D 접촉 조건으로 실행합니다. 직접 호출/Action 우회 없음.";
        });
    }
}
