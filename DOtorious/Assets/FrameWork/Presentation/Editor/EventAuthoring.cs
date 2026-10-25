using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using JYW.Game.EventPlay;
using UnityEditor;

/// <summary>Editor commands only. Reading the catalogue or resolving a path never changes an asset.</summary>
internal static class EventAuthoring
{
    internal sealed class Feature
    {
        internal readonly string Category, Title, Description, TopFlag, Group, SubFlag, DataPath, EntriesPath;
        internal Feature(string category, string title, string description, string topFlag, string group,
            string subFlag, string dataPath, string entriesPath = null)
        {
            Category = category; Title = title; Description = description; TopFlag = topFlag;
            Group = group; SubFlag = subFlag; DataPath = dataPath; EntriesPath = entriesPath;
        }
    }

    private static Feature Single(string category, string title, string description, string flag, string data,
        string entries = null) => new Feature(category, title, description, flag, null, null, data, entries);
    private static Feature Grouped(string category, string title, string description, string group, string flag,
        string data, string entries = null) => new Feature(category, title, description, "Is" + group, group,
            flag, group + "." + data, entries == null ? null : group + "." + data + "." + entries);

    internal static readonly IReadOnlyList<Feature> Features = new[]
    {
        Single("상태와 흐름", "전역 값 변경", "전역 변수 또는 저장 값을 설정하거나 더합니다. Set Globals", "IsSetGlobals", "SetGlobals", "SetGlobals.SetValues"),
        Single("상태와 흐름", "시간 대기", "이 Phase가 끝나기 전에 지정한 시간만큼 기다립니다. Wait", "IsWait", "Wait"),
        Single("상태와 흐름", "조건 대기", "조건이 충족되거나 제한 시간이 끝날 때까지 기다립니다. Wait Until Condition", "IsWaitUntilCondition", "WaitUntilCondition"),
        Single("상태와 흐름", "입력 잠금", "키보드 또는 마우스 입력을 잠급니다. Input Lock", "IsLock", "Lock"),
        Single("상태와 흐름", "게임 속도", "Time Scale을 변경하고 필요하면 원래 속도로 되돌립니다.", "IsTimeScale", "TimeScale"),
        Single("상태와 흐름", "마우스 커서", "마우스 커서 표시 상태를 변경합니다. Cursor", "IsCursorVisible", "Cursor"),
        Single("상태와 흐름", "액션 호출", "이 Phase의 연출과 선택이 끝난 뒤 등록된 이름의 Action을 호출합니다.", "IsAction", "Action"),
        Grouped("오브젝트", "오브젝트 생성", "프리팹으로 새 오브젝트를 만듭니다. Spawn Object", "Objects", "IsSpawnObject", "SpawnObject", "SpawnDatas"),
        Grouped("오브젝트", "오브젝트 제거", "이름으로 찾은 오브젝트를 제거합니다. Destroy Object", "Objects", "IsDestroyObject", "DestroyObject", "DestroyObjectNames"),
        Grouped("오브젝트", "오브젝트 켜기", "오브젝트를 활성화합니다. Enable Object", "Objects", "IsEnableObject", "EnableObject", "EnableObjectNames"),
        Grouped("오브젝트", "오브젝트 끄기", "오브젝트를 비활성화합니다. Disable Object", "Objects", "IsDisableObject", "DisableObject", "DisableObjectNames"),
        Grouped("오브젝트", "이름 변경", "오브젝트의 이름을 바꿉니다. Rename", "Objects", "IsRename", "Rename"),
        Grouped("이동과 배치", "오브젝트 이동", "목적지와 이동 방식을 지정합니다. Move Object", "Transforms", "IsMoveObject", "MoveObject", "MoveObjects"),
        Grouped("이동과 배치", "오브젝트 회전", "회전 또는 바라볼 대상을 지정합니다. Rotate Object", "Transforms", "IsRotateObject", "RotateObject"),
        Grouped("이동과 배치", "부모 연결과 분리", "부모를 바꾸거나 부모에서 분리합니다. Attach Object", "Transforms", "IsAttachObject", "AttachObject", "AttachObjects"),
        Grouped("카메라", "카메라 이동", "Main 또는 Event Camera의 시작점과 도착점을 지정합니다. Camera Move", "Cameras", "IsCameraMove", "CameraMovement"),
        Grouped("카메라", "카메라 시선", "카메라가 바라볼 대상을 지정합니다. Camera Aim", "Cameras", "IsCameraAiming", "CameraAim"),
        Grouped("카메라", "카메라 흔들기", "진동 세기와 시간을 조절합니다. Camera Shake", "Cameras", "IsCameraShake", "CameraShake"),
        Grouped("카메라", "카메라 렌즈", "시야각 또는 Orthographic Size를 전환합니다. Camera Lens", "Cameras", "IsCameraLens", "CameraLens"),
        Grouped("카메라", "카메라 고정", "Main Camera를 월드 위치에 고정하거나 기존 고정을 해제합니다. Camera Fix", "Cameras", "IsCameraFix", "CameraFix"),
        Grouped("컴포넌트와 효과", "컴포넌트 켜기", "지정한 컴포넌트를 활성화합니다. Enable Component", "Components", "IsEnableComponent", "EnableComponent", "ComponentDatas"),
        Grouped("컴포넌트와 효과", "컴포넌트 끄기", "지정한 컴포넌트를 비활성화합니다. Disable Component", "Components", "IsDisableComponent", "DisableComponent", "ComponentDatas"),
        Grouped("컴포넌트와 효과", "콜라이더 끄기", "지정한 오브젝트의 콜라이더를 끕니다. Disable Collider", "Components", "IsDisableColliderObject", "DisableColliderObject", "DisableColliderObjectNames"),
        Grouped("컴포넌트와 효과", "컴포넌트 값 변경", "컴포넌트의 속성 또는 필드 값을 변경합니다. Update Component", "Components", "IsUpdateComponent", "UpdateComponent", "UpdateComponents"),
        Grouped("컴포넌트와 효과", "애니메이터 명령", "애니메이션 상태 또는 파라미터를 변경합니다. Animator", "Components", "IsAnimatorEvent", "AnimatorEvent"),
        Grouped("컴포넌트와 효과", "오브젝트 페이드", "렌더러나 UI의 투명도를 전환합니다. Visual Fade", "Components", "IsVisualFade", "VisualFade", "Fades"),
        Grouped("컴포넌트와 효과", "조명 전환", "조명의 색상, 밝기 또는 범위를 전환합니다. Light Tween", "Components", "IsLightTween", "LightTween", "Lights"),
        Grouped("컴포넌트와 효과", "파티클 명령", "파티클을 재생하거나 정지하고 비웁니다. Particle", "Components", "IsParticleEvent", "ParticleEvent"),
        Grouped("대사와 안내", "시간제 대사", "각 줄의 표시 시간에 따라 대사를 진행합니다. Soft Speech", "Speeches", "IsSoftSpeech", "SoftSpeech", "SoftSpeechTexts"),
        Grouped("대사와 안내", "입력 대기 대사", "지정한 키를 누르면 다음 대사로 넘어갑니다. Hard Speech", "Speeches", "IsHardSpeech", "HardSpeech", "HardSpeechTexts"),
        Grouped("대사와 안내", "초상화 대사", "화자, 초상화와 진행 방식을 지정합니다. Portrait Speech", "Speeches", "IsPortraitSpeech", "PortraitSpeech", "Lines"),
        Grouped("대사와 안내", "말풍선", "대상 오브젝트에 말풍선 대사를 표시합니다. Speech Bubble", "Speeches", "IsSpeechBubble", "SpeechBubble", "SpeechBubbleTexts"),
        Single("대사와 안내", "화면 텍스트", "화면에 텍스트를 표시하고 유지합니다. Text", "IsJustText", "JustText"),
        Single("대사와 안내", "도움말", "위치와 입력 대기 여부를 지정하여 안내를 표시합니다. Tooltip", "IsTooltip", "Tooltip"),
        Single("대사와 안내", "선택지", "다른 연출이 끝난 뒤 선택 입력을 기다립니다. Choice", "IsChoice", "Choice", "Choice.Candidates"),
        Single("화면과 사운드", "화면 페이드", "화면을 서서히 가리거나 드러냅니다. Fade", "IsFadePlay", "FadeInfo"),
        Single("화면과 사운드", "화면 플래시", "화면 색상의 나타남, 유지, 사라짐을 조절합니다. Screen Flash", "IsScreenFlash", "ScreenFlash"),
        Single("화면과 사운드", "레터박스", "화면 위아래에 검은 띠를 표시합니다. Black Label", "IsBlackLabel", "BlackLabel"),
        Single("화면과 사운드", "사운드", "오디오를 재생하거나 재생 중인 소리를 정지합니다. Sound", "IsSound", "Sound"),
        Grouped("씬", "활성 씬 지정", "이미 로드한 씬을 활성 씬으로 지정합니다. Set Active Scene", "Scenes", "IsSetActiveScene", "SetActiveScene"),
        Grouped("씬", "씬 전환", "현재 Phase를 끝내고 다른 씬으로 전환합니다. Scene Change", "Scenes", "IsSceneChange", "SceneChange"),
        Grouped("씬", "씬 추가", "추가 방식으로 씬을 로드합니다. Scene Add", "Scenes", "IsSceneAdd", "SceneAdd", "Scenes"),
        Grouped("씬", "씬 내리기", "로드한 씬을 해제합니다. Scene Off", "Scenes", "IsSceneOff", "SceneOff", "Scenes"),
        Grouped("씬", "씬 일시 정지", "지정한 씬의 동작을 일시 정지합니다. Scene Pause", "Scenes", "IsScenePause", "ScenePause", "Scenes"),
        Grouped("씬", "씬 정지 해제", "일시 정지한 씬의 동작을 재개합니다. Scene Unpause", "Scenes", "IsSceneUnPause", "SceneUnPause", "Scenes")
    };

    internal static EventSO.EventStep GetStep(EventSO asset, string propertyPath)
        => GetValue(asset, propertyPath) as EventSO.EventStep;

    private static object GetValue(EventSO asset, string propertyPath)
    {
        if (asset == null || string.IsNullOrEmpty(propertyPath)) return null;
        object value = asset;
        string[] parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length && value != null; i++)
        {
            if (parts[i] == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
            {
                string indexText = parts[++i];
                if (!int.TryParse(indexText.Substring(5, indexText.Length - 6), out int index) ||
                    !(value is IList list) || index < 0 || index >= list.Count) return null;
                value = list[index];
            }
            else
            {
                FieldInfo field = value.GetType().GetField(parts[i], BindingFlags.Public | BindingFlags.Instance);
                if (field == null) return null;
                value = field.GetValue(value);
            }
        }
        return value;
    }

    internal static bool EnsureFirstPhase(EventSO asset, out string phasePath)
    {
        phasePath = null;
        if (asset == null) return false;
        var document = EventTimelineBuilder.Build(asset);
        if (document.Steps.Count > 0) { phasePath = document.Steps[0].PropertyPath; return true; }
        BeginUndo(asset, "첫 Phase 추가");
        phasePath = EnsureFirstPhaseCore(asset);
        EditorUtility.SetDirty(asset);
        return true;
    }

    private static string EnsureFirstPhaseCore(EventSO asset)
    {
        int size = asset.UseCondition ? (asset.Conditions?.Length ?? 0) + 1 : 1;
        var values = asset.ConditionSteps ?? Array.Empty<EventSO.EventStep>();
        if (values.Length < size) Array.Resize(ref values, size);
        if (values[0] == null) values[0] = new EventSO.EventStep();
        asset.ConditionSteps = values;
        return "ConditionSteps.Array.data[0]";
    }

    internal static bool AddFeature(EventSO asset, string phasePath, string featureKey, out string featurePropertyPath)
    {
        featurePropertyPath = null;
        Feature feature = null;
        foreach (Feature candidate in Features) if (candidate.DataPath == featureKey) { feature = candidate; break; }
        if (asset == null || feature == null) return false;
        EventSO.EventStep step = GetStep(asset, phasePath);
        if (step == null && !string.IsNullOrEmpty(phasePath)) return false;
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(feature.Title + " 추가");
        Undo.RegisterCompleteObjectUndo(asset, feature.Title + " 추가");
        if (step == null)
        {
            var existing = EventTimelineBuilder.Build(asset);
            phasePath = existing.Steps.Count > 0 ? existing.Steps[0].PropertyPath : EnsureFirstPhaseCore(asset);
            step = GetStep(asset, phasePath);
        }
        if (step.Flags == null) step.Flags = new EventSO.EventStepFlags();
        typeof(EventSO.EventStepFlags).GetField(feature.TopFlag).SetValue(step.Flags, true);
        if (feature.Group != null)
        {
            object group = EnsurePath(step, feature.Group);
            group.GetType().GetField(feature.SubFlag).SetValue(group, true);
        }
        EnsurePath(step, feature.DataPath);
        featurePropertyPath = phasePath + "." + feature.DataPath;
        if (feature.EntriesPath != null)
        {
            int dot = feature.EntriesPath.LastIndexOf('.');
            object owner = EnsurePath(step, feature.EntriesPath.Substring(0, dot));
            FieldInfo field = owner.GetType().GetField(feature.EntriesPath.Substring(dot + 1));
            Type itemType = field.FieldType.IsArray ? field.FieldType.GetElementType() : field.FieldType.GetGenericArguments()[0];
            object item = itemType == typeof(string) ? string.Empty : Activator.CreateInstance(itemType);
            IList old = field.GetValue(owner) as IList;
            int index = old?.Count ?? 0;
            if (field.FieldType.IsArray)
            {
                Array items = Array.CreateInstance(itemType, index + 1);
                if (old != null) old.CopyTo(items, 0);
                items.SetValue(item, index);
                field.SetValue(owner, items);
            }
            else
            {
                if (old == null) { old = (IList)Activator.CreateInstance(field.FieldType); field.SetValue(owner, old); }
                old.Add(item);
            }
            featurePropertyPath = phasePath + "." + feature.EntriesPath + ".Array.data[" + index + "]";
        }
        EditorUtility.SetDirty(asset);
        Undo.CollapseUndoOperations(undo);
        return true;
    }

    /// <summary>Insert a phase without replacing the existing continuation or conditional branches.</summary>
    internal static bool AddNextPhase(EventSO asset, string phasePath, out string newPhasePath)
    {
        newPhasePath = null;
        if (asset == null) return false;
        if (string.IsNullOrEmpty(phasePath)) return EnsureFirstPhase(asset, out newPhasePath);
        EventSO.EventStep step = GetStep(asset, phasePath);
        if (step == null) return false;
        BeginUndo(asset, "다음 Phase 추가");
        if (step.Flags == null) step.Flags = new EventSO.EventStepFlags();
        var next = new EventSO.EventStep { EventExe = step.EventExe };
        next.Flags.IsEventExe = step.Flags.IsEventExe;
        step.EventExe = new EventSO.EventExeData
        {
            UseCondition = false, Conditions = Array.Empty<EventSO.ConditionGroupData>(),
            ConditionSteps = new[] { next }
        };
        step.Flags.IsEventExe = true;
        newPhasePath = phasePath + ".EventExe.ConditionSteps.Array.data[0]";
        EditorUtility.SetDirty(asset);
        return true;
    }

    internal static bool AddConditionBranch(EventSO asset, string contextPath, out string newPhasePath)
    {
        newPhasePath = null;
        if (!GetBranchContext(asset, contextPath, out object context, out EventSO.ConditionGroupData[] conditions,
                out EventSO.EventStep[] steps)) return false;
        int index = conditions.Length;
        BeginUndo(asset, "조건 분기 추가");
        var newConditions = new EventSO.ConditionGroupData[index + 1];
        Array.Copy(conditions, newConditions, index);
        newConditions[index] = new EventSO.ConditionGroupData();
        var newSteps = new EventSO.EventStep[index + 2];
        if (index > 0) Array.Copy(steps, newSteps, index);
        newSteps[index] = new EventSO.EventStep();
        newSteps[index + 1] = steps.Length == 0 ? new EventSO.EventStep() : steps[index];
        SetBranchFields(context, true, newConditions, newSteps);
        newPhasePath = (string.IsNullOrEmpty(contextPath) ? "" : contextPath + ".") +
                       "ConditionSteps.Array.data[" + index + "]";
        EditorUtility.SetDirty(asset);
        return true;
    }

    internal static bool RemoveConditionBranch(EventSO asset, string contextPath, int index)
    {
        if (!GetBranchContext(asset, contextPath, out object context, out EventSO.ConditionGroupData[] conditions,
                out EventSO.EventStep[] steps) || index < 0 || index >= conditions.Length) return false;
        BeginUndo(asset, "조건 분기 삭제");
        var newConditions = new EventSO.ConditionGroupData[conditions.Length - 1];
        var newSteps = new EventSO.EventStep[steps.Length - 1];
        for (int source = 0, target = 0; source < conditions.Length; source++)
            if (source != index) newConditions[target++] = conditions[source];
        for (int source = 0, target = 0; source < steps.Length; source++)
            if (source != index) newSteps[target++] = steps[source];
        bool useCondition = (bool)context.GetType().GetField("UseCondition").GetValue(context);
        SetBranchFields(context, useCondition, newConditions, newSteps);
        EditorUtility.SetDirty(asset);
        return true;
    }

    private static bool GetBranchContext(EventSO asset, string contextPath, out object context,
        out EventSO.ConditionGroupData[] conditions, out EventSO.EventStep[] steps)
    {
        context = string.IsNullOrEmpty(contextPath) ? asset : GetValue(asset, contextPath);
        conditions = null; steps = null;
        if (asset == null || !(context is EventSO || context is EventSO.EventExeData)) return false;
        conditions = (EventSO.ConditionGroupData[])context.GetType().GetField("Conditions").GetValue(context) ??
                     Array.Empty<EventSO.ConditionGroupData>();
        steps = (EventSO.EventStep[])context.GetType().GetField("ConditionSteps").GetValue(context) ??
                Array.Empty<EventSO.EventStep>();
        if (steps.Length == conditions.Length + 1 || (steps.Length == 0 && conditions.Length == 0)) return true;
        UnityEngine.Debug.LogWarning("조건과 Phase의 연결 수가 맞지 않아 변경을 취소했습니다. 기존 자료를 고급 보기에서 확인하세요. (조건 " +
            conditions.Length + " / Phase " + steps.Length + ")", asset);
        return false;
    }

    private static void SetBranchFields(object context, bool useCondition, EventSO.ConditionGroupData[] conditions,
        EventSO.EventStep[] steps)
    {
        Type type = context.GetType();
        type.GetField("UseCondition").SetValue(context, useCondition);
        type.GetField("Conditions").SetValue(context, conditions);
        type.GetField("ConditionSteps").SetValue(context, steps);
    }

    private static void BeginUndo(EventSO asset, string label)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(label);
        Undo.RegisterCompleteObjectUndo(asset, label);
    }

    private static object EnsurePath(object root, string path)
    {
        foreach (string name in path.Split('.'))
        {
            FieldInfo field = root.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            object value = field.GetValue(root);
            if (value == null) { value = Activator.CreateInstance(field.FieldType); field.SetValue(root, value); }
            root = value;
        }
        return root;
    }
}
