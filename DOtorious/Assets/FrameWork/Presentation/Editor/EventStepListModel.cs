using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static JYW.Game.EventPlay.EventSO;

// A read-only authoring projection. Merely opening the window must never normalize saved EventSO data.
internal sealed class EventEditorPhase
{
    internal string Path, Label, Condition, ContextPath;
    internal int Depth;
    internal bool Reachable;
    internal EventStep Source;
    internal EventTimelineStep Timing;
}

internal sealed class EventEditorRow
{
    internal EventEditorPhase Phase;
    internal string FeaturePath, Title, Category, Target, Content, Start, End, TimeNote;
}

internal sealed class EventStepListModel
{
    internal readonly List<EventEditorPhase> Phases = new List<EventEditorPhase>();
    internal readonly List<EventEditorRow> Rows = new List<EventEditorRow>();
    internal readonly List<string> Warnings = new List<string>();
    private readonly Dictionary<string, EventTimelineStep> timings = new Dictionary<string, EventTimelineStep>();

    internal static EventStepListModel Build(EventSO asset)
    {
        var model = new EventStepListModel();
        if (asset == null) return model;
        var timeline = EventTimelineBuilder.Build(asset);
        foreach (var step in timeline.Steps) model.timings[step.PropertyPath] = step;
        model.Warnings.AddRange(timeline.Warnings);
        var active = new HashSet<EventStep>();
        int expected = asset.UseCondition ? (asset.Conditions?.Length ?? 0) + 1 : 1;
        int count = Math.Max(expected, asset.ConditionSteps?.Length ?? 0);
        using (var serialized = new SerializedObject(asset))
        {
            for (int i = 0; i < count; i++)
            {
                EventStep source = EventTimelineBuilder.ResolveRootStep(asset, i, out string path);
                if (source == null && serialized.FindProperty(path) == null) continue;
                string route = BranchLabel(asset.UseCondition, asset.Conditions?.Length ?? 0, i);
                string condition = BranchCondition(asset.UseCondition, asset.Conditions, i, true);
                model.Visit(source, path, route, condition, null, 0, i < expected, active, serialized);
            }
        }
        return model;
    }

    private void Visit(EventStep source, string path, string route, string condition, string context,
        int depth, bool reachable, HashSet<EventStep> active, SerializedObject serialized)
    {
        if (Phases.Count >= 512)
        { AddWarning("512개 이후 Phase는 생략했습니다. 데이터는 변경하지 않습니다."); return; }
        if (source != null && !active.Add(source))
        { AddWarning(route + ": 순환 연결이 있어 탐색을 멈췄습니다."); return; }
        timings.TryGetValue(path, out var timing);
        if (timing == null && source != null) timing = EventTimelineBuilder.DescribeStep(source, path, route);
        var phase = new EventEditorPhase { Path = path, Label = route + " / Phase " + (depth + 1),
            Condition = condition, ContextPath = context, Depth = depth, Reachable = reachable,
            Source = source, Timing = timing };
        Phases.Add(phase);
        if (timing != null)
        {
            foreach (var item in timing.Items)
            {
                string relative = item.PropertyPath.StartsWith(path + ".", StringComparison.Ordinal)
                    ? item.PropertyPath.Substring(path.Length + 1) : "";
                var feature = EventAuthoring.Features.FirstOrDefault(f => relative == f.DataPath ||
                    relative.StartsWith(f.DataPath + ".", StringComparison.Ordinal));
                bool variableStart = timing.OriginVariable;
                double origin = timing.Origin;
                string start = !reachable ? "미실행" : variableStart ? "가변" : Seconds(origin + item.Start);
                bool persistent = item.Title == "Camera Fix · Enable";
                string openEnd = item.Title.Contains("Speech") || item.Title == "Tooltip" || item.Title == "Choice" ? "입력 후" :
                    item.Title == "Wait Until Condition" ? "조건 충족 시" : item.Title == "Input Lock" ? "이벤트 종료" : "별도 종료";
                string end = !reachable ? "미실행" : persistent ? "Disable까지" : item.OpenEnded ? openEnd :
                    item.Variable || variableStart ? "가변" : Seconds(origin + item.End);
                Rows.Add(new EventEditorRow { Phase = phase, FeaturePath = item.PropertyPath,
                    Title = feature?.Title ?? item.Title, Category = feature?.Category ?? item.Category,
                    Target = TargetOf(serialized.FindProperty(item.PropertyPath)), Content = item.Detail,
                    Start = start, End = end,
                    TimeNote = "시작/종료는 각 실행 경로의 호출 시점을 0초로 본 표시입니다. " +
                        "가변 값은 실행 중 달라지며 확정 시간이 아닙니다.\n" +
                        "시간 참고: " + Seconds(origin + item.Start) + " ~ " + Seconds(origin + item.End) + "\n" + item.Note });
            }
        }
        if (timing == null || timing.Items.Count == 0)
            Rows.Add(new EventEditorRow { Phase = phase, FeaturePath = path, Title = source == null ? "빈 Phase" :
                reachable ? "활성 기능 없음" : "비활성 경로", Category = "흐름", Target = "—",
                Content = source == null ? "기능 추가로 이 단계를 작성하세요." :
                    reachable ? "다음 Phase 또는 분기 설정을 확인하세요." : "현재 실행 경로에 연결되지 않은 저장 데이터입니다.",
                Start = "—", End = "—", TimeNote = "실행 시간 없음" });
        if (source?.EventExe?.eventSO != null)
            AddWarning(phase.Label + ": EventExe.eventSO에 저장된 참조는 현재 런타임에서 자동 호출되지 않습니다. 인라인 Next Phase가 실행됩니다.");
        var next = source?.EventExe;
        if (next != null && next.ConditionSteps != null)
        {
            bool sceneChange = source.Flags?.IsScenes == true && source.Scenes?.IsSceneChange == true && source.Scenes.SceneChange != null;
            bool continues = reachable && source.Flags?.IsEventExe == true && !sceneChange;
            int expected = next.UseCondition ? (next.Conditions?.Length ?? 0) + 1 : 1;
            for (int i = 0; i < next.ConditionSteps.Length; i++)
            {
                if (next.ConditionSteps[i] == null) continue;
                string childRoute = next.UseCondition ? route + " → " + BranchLabel(true, next.Conditions?.Length ?? 0, i) : route;
                string childCondition = condition + "\n→ " + BranchCondition(next.UseCondition, next.Conditions, i, false);
                if (!continues) childCondition += sceneChange ? " (씬 전환으로 연결 종료)" : " (Next Phase 꺼짐/미연결)";
                Visit(next.ConditionSteps[i], At(path + ".EventExe.ConditionSteps", i), childRoute, childCondition,
                    path + ".EventExe", depth + 1, continues && i < expected, active, serialized);
            }
        }
        if (source != null) active.Remove(source);
    }

    private void AddWarning(string message) { if (!Warnings.Contains(message)) Warnings.Add(message); }
    private static string At(string path, int index) => path + ".Array.data[" + index + "]";
    private static string Seconds(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "초";
    private static string Name(string value) => string.IsNullOrWhiteSpace(value) ? "미지정" : value;

    internal static string BranchLabel(bool useCondition, int count, int index) =>
        !useCondition ? index == 0 ? "직접 호출" : "미연결 " + (index + 1) :
        index < count ? "조건 " + (index + 1) : index == count ? "Else" : "미연결 " + (index + 1);

    internal static string BranchCondition(bool useCondition, ConditionGroupData[] groups, int index, bool root)
    {
        int count = groups?.Length ?? 0;
        if (!useCondition) return index == 0 ? root ? "직접 호출 시 실행 (None)" : "이전 Phase 완료 후 실행" : "실행에 매핑되지 않은 저장 단계";
        if (index > count) return "실행에 매핑되지 않은 저장 단계";
        if (index == count) return root ? "Else · 직접 호출 시 앞선 모든 조건이 불일치 (자동감시로 시작하지 않음)" :
            "Else · 이전 Phase 완료 후 앞선 다음 단계 조건이 모두 불일치";
        string priority = index == 0 ? "첫 조건: " : "앞선 조건 불일치 AND ";
        string issue = root ? AutoConditionIssue(groups[index]) : null;
        return priority + DescribeCondition(groups[index]) + (string.IsNullOrEmpty(issue) ? "" : "\n자동감시 제외: " + issue);
    }

    internal static string DescribeCondition(ConditionGroupData group)
    {
        if (group == null) return "조건 데이터 없음 (불충족)";
        var checks = group.Checks;
        if (checks == ConditionChecks.None) return "항상 참 (Checks=None)";
        var parts = new List<string>();
        if ((checks & ConditionChecks.CheckValue) != 0)
            parts.Add(group.Conditions == null || group.Conditions.Length == 0 ? "값 조건 없음 (불충족)" :
                "값 [" + string.Join(" AND ", group.Conditions.Select(DescribeValue)) + "]");
        if ((checks & ConditionChecks.TimeCount) != 0) parts.Add("게임 경과 ≥ " + group.TimeCountSeconds.ToString("0.##") + "초");
        if ((checks & ConditionChecks.CollisionAB) != 0) parts.Add(Name(group.CollisionObjectA) + " ↔ " + Name(group.CollisionObjectB) + " 충돌 이력");
        if ((checks & ConditionChecks.ObjectState) != 0) parts.Add(Name(group.ObjectStateTarget) + " " + ObjectState(group.ObjectState));
        if ((checks & ConditionChecks.Distance) != 0) parts.Add(Name(group.DistanceObjectA) + " ↔ " + Name(group.DistanceObjectB) +
            " 거리 " + (group.DistanceComparison == DistanceCheck.AtMost ? "≤ " : "≥ ") + group.DistanceThreshold.ToString("0.##") + (group.DistanceUse2D ? " (2D)" : " (3D)"));
        if ((checks & ConditionChecks.InputKey) != 0) parts.Add("키 " + group.InputKeyCode + (group.InputKeyState == InputKeyCheck.Held ? " 누르는 중" : " 누른 프레임"));
        if ((checks & ConditionChecks.AnimatorState) != 0) parts.Add((string.IsNullOrWhiteSpace(group.AnimatorObject) ? "Caller (빈 대상)" : group.AnimatorObject) + " 애니메이션 " + group.AnimatorStateName +
            (group.AnimatorState == AnimatorStateCheck.Completed ? " 완료 ≥ " + group.AnimatorCompletionTime.ToString("0.##") : " 재생 중") + " / Layer " + group.AnimatorLayer);
        if ((checks & ConditionChecks.SceneState) != 0) parts.Add("씬 " + group.SceneName + " " +
            (group.SceneState == SceneStateCheck.Active ? "활성" : group.SceneState == SceneStateCheck.Loaded ? "로드됨" : "로드 안 됨"));
        if ((checks & ConditionChecks.CameraView) != 0) parts.Add(Name(group.CameraViewTarget) +
            (group.CameraViewState == CameraViewCheck.Visible ? " 카메라 화면 안" : " 카메라 화면 밖"));
        return string.Join(" AND ", parts);
    }

    // Keep these diagnostics in sync with EventPlayManager.IsCompleteAutoCondition. They describe
    // authoring completeness, not whether a complete condition is currently true in the running game.
    internal static string AutoConditionIssue(ConditionGroupData group)
    {
        if (group == null) return "조건 데이터 없음";
        var checks = group.Checks;
        if (checks == ConditionChecks.None) return null;
        var issues = new List<string>();
        if ((checks & ConditionChecks.CollisionAB) != 0)
        {
            if (string.IsNullOrWhiteSpace(group.CollisionObjectA) || string.IsNullOrWhiteSpace(group.CollisionObjectB)) issues.Add("충돌 A/B 미지정");
            else if (string.Equals(group.CollisionObjectA.Trim(), group.CollisionObjectB.Trim(), StringComparison.Ordinal)) issues.Add("충돌 A/B가 같음");
        }
        if ((checks & ConditionChecks.ObjectState) != 0 && string.IsNullOrWhiteSpace(group.ObjectStateTarget)) issues.Add("오브젝트 상태 대상 미지정");
        if ((checks & ConditionChecks.Distance) != 0)
        {
            if (string.IsNullOrWhiteSpace(group.DistanceObjectA) || string.IsNullOrWhiteSpace(group.DistanceObjectB)) issues.Add("거리 A/B 미지정");
            else if (string.Equals(group.DistanceObjectA.Trim(), group.DistanceObjectB.Trim(), StringComparison.Ordinal)) issues.Add("거리 A/B가 같음");
        }
        if ((checks & ConditionChecks.InputKey) != 0 && group.InputKeyCode == KeyCode.None) issues.Add("입력 키가 None");
        if ((checks & ConditionChecks.AnimatorState) != 0)
        {
            if (string.IsNullOrWhiteSpace(group.AnimatorObject)) issues.Add("Animator 대상 미지정");
            if (string.IsNullOrWhiteSpace(group.AnimatorStateName)) issues.Add("Animator 상태 이름 미지정");
            if (group.AnimatorLayer < 0) issues.Add("Animator Layer가 음수");
        }
        if ((checks & ConditionChecks.SceneState) != 0 && string.IsNullOrWhiteSpace(group.SceneName)) issues.Add("씬 이름 미지정");
        if ((checks & ConditionChecks.CameraView) != 0 && string.IsNullOrWhiteSpace(group.CameraViewTarget)) issues.Add("카메라 화면 판정 대상 미지정");
        if ((checks & ConditionChecks.CheckValue) != 0)
        {
            if (group.Conditions == null || group.Conditions.Length == 0) issues.Add("값 비교 행 없음");
            else
                for (int i = 0; i < group.Conditions.Length; i++)
                    if (group.Conditions[i] == null || string.IsNullOrWhiteSpace(group.Conditions[i].GlobalNames)) issues.Add("값 비교 " + (i + 1) + " Key 미지정");
        }
        return issues.Count == 0 ? null : string.Join(", ", issues);
    }

    private static string ObjectState(ObjectStateCheck value) => value == ObjectStateCheck.Exists ? "존재" :
        value == ObjectStateCheck.Missing ? "없음" : value == ObjectStateCheck.Active ? "활성" : "비활성";

    private static string DescribeValue(ConditionSubData value)
    {
        if (value == null) return "빈 값 (불충족)";
        string op = value.checkType == CheckType.Value ? "=" : value.checkType == CheckType.NotEqual ? "≠" :
            value.checkType == CheckType.Greater ? ">" : value.checkType == CheckType.GreaterOrEqual ? "≥" :
            value.checkType == CheckType.Less ? "<" : value.checkType == CheckType.LessOrEqual ? "≤" :
            value.checkType == CheckType.Odd ? "홀수" : "짝수";
        string expected = value.valueType == EventSO.ValueType.Int ? value.ExpectedInt.ToString() :
            value.valueType == EventSO.ValueType.Float ? value.ExpectedFloat.ToString("0.###") :
            value.valueType == EventSO.ValueType.Bool ? value.ExpectedBool.ToString() :
            value.valueType == EventSO.ValueType.String ? "\"" + value.ExpectedString + "\"" : value.ExpectedGameObject != null ? value.ExpectedGameObject.name : "null";
        return (value.isPlayerPrefData ? "저장 " : "전역 ") + value.GlobalNames + " " + op +
            (value.checkType == CheckType.Odd || value.checkType == CheckType.Even ? "" : " " + expected);
    }

    private static string TargetOf(SerializedProperty property)
    {
        if (property == null) return "—";
        if (property.propertyPath.Contains(".Cameras."))
        {
            if (property.propertyPath.EndsWith(".CameraFix", StringComparison.Ordinal)) return "Main Camera";
            var useMain = property.FindPropertyRelative("UseMainCamera") ??
                property.FindPropertyRelative("IsCameraMoveUseMain") ?? property.FindPropertyRelative("IsCameraAimingUseMain");
            return useMain != null ? useMain.boolValue ? "Main Camera" : "Event Camera" : "카메라";
        }
        if (property.propertyType == SerializedPropertyType.String) return string.IsNullOrWhiteSpace(property.stringValue) ? "Caller" : property.stringValue;
        if (property.propertyType == SerializedPropertyType.ObjectReference) return property.objectReferenceValue != null ? property.objectReferenceValue.name : "없음";
        string[] names = { "ObjectName", "objectName", "gameObjectName", "GameObjectName", "CenterObject", "CameraName", "ValueName", "Scene", "SceneName", "prefab" };
        foreach (string name in names)
        {
            var child = property.FindPropertyRelative(name);
            if (child == null) continue;
            if (child.propertyType == SerializedPropertyType.String) return string.IsNullOrWhiteSpace(child.stringValue) ? "Caller" : child.stringValue;
            if (child.propertyType == SerializedPropertyType.ObjectReference) return child.objectReferenceValue != null ? child.objectReferenceValue.name : "없음";
        }
        if (property.propertyPath.Contains(".Speeches.") || property.propertyPath.Contains(".FadeInfo")) return "화면 UI";
        return "—";
    }
}

internal sealed class EventInvocationReference
{
    internal UnityEngine.Object Source;
    internal string Location, Description;
}

/// <summary>Projects can describe verified event entry points without coupling this editor to game scripts.</summary>
public static class PresentationEventSourceDescriptions
{
    private static readonly Dictionary<string, Func<Component, string, EventSO, string>> Describers =
        new Dictionary<string, Func<Component, string, EventSO, string>>(StringComparer.Ordinal);

    /// <param name="componentTypeName">Full type name preferred; a simple type name is also supported.</param>
    /// <param name="describe">Receives the scene component, serialized reference path and referenced asset. Return null for the generic fallback.</param>
    public static void Register(string componentTypeName, Func<Component, string, EventSO, string> describe)
    {
        if (string.IsNullOrWhiteSpace(componentTypeName)) throw new ArgumentException("Component type name is required.", nameof(componentTypeName));
        if (describe == null) Describers.Remove(componentTypeName);
        else Describers[componentTypeName] = describe;
    }

    internal static string Describe(Component source, string propertyPath, EventSO asset)
    {
        var type = source.GetType();
        if (!Describers.TryGetValue(type.FullName ?? type.Name, out var describe)) Describers.TryGetValue(type.Name, out describe);
        if (describe != null)
        {
            try { string result = describe(source, propertyPath, asset); if (!string.IsNullOrWhiteSpace(result)) return result; }
            catch (Exception exception) { return "설명 조회 실패: " + exception.Message + " · 저장 참조는 유지됩니다."; }
        }
        if (type.Name == "EventCollider" && propertyPath == "eventSO") return "Player 태그의 3D Trigger 진입 → PlayEvent";
        return "저장 참조 · 호출 동작은 해당 컴포넌트에서 확인";
    }
}

internal static class EventInvocationIndex
{
    internal static List<EventInvocationReference> ScanLoadedScenes(EventSO asset)
    {
        var result = new List<EventInvocationReference>();
        if (asset == null) return result;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var iterator = serialized.GetIterator();
                    while (iterator.Next(true))
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.objectReferenceValue != asset) continue;
                        string type = component.GetType().Name;
                        string description = PresentationEventSourceDescriptions.Describe(component, iterator.propertyPath, asset);
                        result.Add(new EventInvocationReference { Source = component,
                            Location = scene.name + " / " + Hierarchy(component.transform) + " / " + type + "." + iterator.propertyPath,
                            Description = description + (component.gameObject.activeInHierarchy && component.enabled ? "" : " · 현재 비활성") });
                    }
                }
            }
        }
        return result;
    }

    internal static string AutomaticRegistration(EventSO asset)
    {
        if (asset == null) return "";
        string path = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
        const string marker = "/Resources/Presentation/";
        int index = path.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return "자동감시 경로 밖 · 직접 호출 또는 씬 트리거가 필요합니다.\n자동감시 등록 경로: Assets/.../Resources/Presentation/{씬 이름}/하위 폴더/EventSO.asset";
        string relative = path.Substring(index + marker.Length);
        int slash = relative.IndexOf('/');
        if (slash < 0) return "씬 이름 폴더가 없습니다. Resources/Presentation/{씬 이름}/ 아래에 등록하세요.";
        string scene = relative.Substring(0, slash);
        if (!asset.UseCondition) return "등록 폴더: " + scene + " · None은 자동감시 대상이 아니며 직접 호출로 실행합니다.";
        if ((asset.Conditions?.Length ?? 0) == 0) return "등록 폴더: " + scene + " · Condition이 비어 있어 자동감시로 시작하지 않습니다.";
        var issues = new List<string>();
        int ready = 0;
        for (int i = 0; i < asset.Conditions.Length; i++)
        {
            string issue = EventStepListModel.AutoConditionIssue(asset.Conditions[i]);
            if (issue == null) ready++; else issues.Add("조건 " + (i + 1) + ": " + issue);
        }
        return "자동감시 등록 폴더: " + scene + " · 감시 가능 조건 " + ready + "/" + asset.Conditions.Length + "개\n" +
            (ready == 0 ? "모든 조건이 미완성이므로 자동감시로 시작하지 않습니다." :
                "해당 씬 로드당 1회, 먼저 맞은 완성된 분기를 기억하고 매니저가 비면 실행합니다. Else는 자동 시작하지 않습니다.") +
            (issues.Count == 0 ? "" : "\n감시 제외 — " + string.Join(" / ", issues));
    }

    private static string Hierarchy(Transform target)
    {
        string result = target.name;
        while (target.parent != null) { target = target.parent; result = target.name + "/" + result; }
        return result;
    }
}
