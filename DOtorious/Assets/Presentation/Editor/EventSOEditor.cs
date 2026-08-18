using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using JYW.Game.EventPlay;

[CustomEditor(typeof(EventSO))]
[CanEditMultipleObjects]
public class EventSOEditor : Editor
{
    private static readonly Dictionary<string, bool> masterFoldouts = new Dictionary<string, bool>();
    private static readonly Dictionary<string, List<bool>> elementFoldouts = new Dictionary<string, List<bool>>();

    private static string stepClipboardJson = null;
    private static string stepClipboardLabel = "";
    private static string stepsArrayClipboardJson = null;
    private static string stepsArrayClipboardLabel = "";

    private static string dragActiveKey = null;
    private static int dragFromIndex = -1;
    private static int dragToIndex = -1;
    private static bool dragInProgress = false;
    private static readonly Dictionary<string, List<Rect>> itemHeaderRects = new Dictionary<string, List<Rect>>();

    private static readonly Dictionary<string, FieldInfo> fieldInfoCache = new Dictionary<string, FieldInfo>();

    private string cachedTargetsKey = null;
    private int cachedTargetsHash = 0;

    private static readonly StringBuilder sb = new StringBuilder(64);

    private readonly HashSet<string> drawnDataKeys = new HashSet<string>();

    [Serializable]
    private class StepArrayWrapper
    {
        public EventSO.EventStep[] Steps = new EventSO.EventStep[0];
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        UpdateTargetsKeyCache();

        var useCondProp = serializedObject.FindProperty("UseCondition");
        DrawExecutionModeSelector(useCondProp);
        bool useCondition = useCondProp != null && useCondProp.boolValue;

        DrawConditionStepsArray(
            serializedObject.FindProperty("ConditionSteps"),
            null,
            useCondition,
            null,
            serializedObject.FindProperty("Conditions")
        );

        serializedObject.ApplyModifiedProperties();

        if (dragInProgress)
            Repaint();
    }

    private void DrawExecutionModeSelector(SerializedProperty useConditionProp)
    {
        if (useConditionProp == null) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("EVENT EXECUTION", EditorStyles.boldLabel);
        EditorGUI.showMixedValue = useConditionProp.hasMultipleDifferentValues;
        int current = useConditionProp.boolValue ? 1 : 0;
        int selected = GUILayout.Toolbar(current, new[] { "None", "Condition" }, GUILayout.Height(24));
        EditorGUI.showMixedValue = false;
        if (selected != current)
            useConditionProp.boolValue = selected == 1;

        EditorGUILayout.HelpBox(
            selected == 1
                ? "Condition: Resources에서 자동 감시하며, 설정한 조건이 충족되면 이 EventSO를 한 번 실행합니다."
                : "None: 자동 실행하지 않습니다. 기존처럼 EventPlayManager.PlayEvent(...)로 호출해야 합니다.",
            selected == 1 ? MessageType.Info : MessageType.None);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    private static void DrawSectionBanner(string title, string subtitle, Color color)
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 25f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, color);
        var titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.normal.textColor = Color.white;
        titleStyle.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height), title, titleStyle);
        if (!string.IsNullOrEmpty(subtitle))
            EditorGUILayout.LabelField(subtitle, EditorStyles.wordWrappedMiniLabel);
    }
    private void UpdateTargetsKeyCache()
    {
        // Unity 6.5에서는 InstanceID 대신 EntityId를 사용한다.
        int hash = 0;
        if (targets != null)
            for (int t = 0; t < targets.Length; t++)
                if (targets[t] != null)
#if UNITY_6000_5_OR_NEWER
                    hash ^= targets[t].GetEntityId().GetHashCode();
#else
                    hash ^= targets[t].GetInstanceID();
#endif

        if (hash != cachedTargetsHash)
        {
            cachedTargetsHash = hash;
            sb.Clear();
            if (targets != null)
                for (int t = 0; t < targets.Length; t++)
                    if (targets[t] != null)
                    {
#if UNITY_6000_5_OR_NEWER
                        sb.Append(targets[t].GetEntityId().ToString());
#else
                        sb.Append(targets[t].GetInstanceID());
#endif
                        sb.Append('_');
                    }
            cachedTargetsKey = sb.ToString();
        }
    }

    private static FieldInfo GetCachedFieldInfo(Type type, string fieldName)
    {
        string key = type.FullName + "." + fieldName;
        if (!fieldInfoCache.TryGetValue(key, out var fi))
        {
            fi = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fieldInfoCache[key] = fi;
        }
        return fi;
    }

    private static SerializedProperty EnsureEventStepElement(SerializedProperty arrayProp, int index)
    {
        if (arrayProp == null || !arrayProp.isArray || index < 0 || index >= arrayProp.arraySize) return null;
        SerializedProperty element = arrayProp.GetArrayElementAtIndex(index);
        if (element != null &&
            element.propertyType == SerializedPropertyType.ManagedReference &&
            element.managedReferenceValue == null)
        {
            element.managedReferenceValue = new EventSO.EventStep();
            element = arrayProp.GetArrayElementAtIndex(index);
        }
        return element;
    }

    private T GetFieldValue<T>(object obj, string fieldName) where T : class
    {
        if (obj == null) return null;
        var fi = GetCachedFieldInfo(obj.GetType(), fieldName);
        return fi?.GetValue(obj) as T;
    }

    private string BuildKey(string suffix) { sb.Clear(); sb.Append(cachedTargetsKey); sb.Append(suffix); return sb.ToString(); }
    private string BuildKey(string s1, string s2) { sb.Clear(); sb.Append(cachedTargetsKey); sb.Append(s1); sb.Append(s2); return sb.ToString(); }


    private void DrawStepContent(SerializedProperty stepProp)
    {
        var flagsProp = stepProp.FindPropertyRelative("Flags");
        if (flagsProp == null) return;
        drawnDataKeys.Clear();

        // 최상위 플래그 (그룹 + 독립) ? IsEventExe는 Phase 체인 UI로 별도 관리
        string[] topFlagNames = {
            "IsSetGlobals",
            "IsObjects",
            "IsComponents",
            "IsTransforms",
            "IsWait","IsLock",
            "IsSpeeches",
            "IsScenes",
            "IsSound","IsFadePlay",
            "IsCameras",
            "IsCursorVisible","IsChoice","IsAction",
            "IsTooltip"
        };
        string[] topFlagLabels = {
            "Set Globals",
            "Objects (Spawn/Destroy/Enable/Disable/Rename)",
            "Components (Enable/Disable/Collider/Update)",
            "Transforms (Move/Rotate)",
            "Wait","Lock",
            "Speeches (Soft/Hard)",
            "Scenes (Active/Change/Add/Off/Pause/UnPause)",
            "Sound","Fade Play",
            "Cameras (Move/Aim)",
            "Cursor","Choice","Action",
            "Tooltip"
        };

        int mask = 0;
        for (int fi = 0; fi < topFlagNames.Length; fi++)
        {
            var p = flagsProp.FindPropertyRelative(topFlagNames[fi]);
            if (p != null && p.boolValue) mask |= (1 << fi);
        }
        int newMask = EditorGUILayout.MaskField("Step Flags", mask, topFlagLabels);
        if (newMask != mask)
        {
            for (int fi = 0; fi < topFlagNames.Length; fi++)
            {
                var p = flagsProp.FindPropertyRelative(topFlagNames[fi]);
                if (p == null) continue;
                p.boolValue = (newMask & (1 << fi)) != 0;
            }
            flagsProp.serializedObject.ApplyModifiedProperties();
            Repaint();
            GUIUtility.ExitGUI();
            return;
        }

        DrawSetGlobalsCustom(flagsProp, stepProp, "IsSetGlobals", "SetGlobals");
        DrawObjectsGroup(flagsProp, stepProp);
        DrawComponentsGroup(flagsProp, stepProp);
        DrawTransformsGroup(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsWait", "Wait");
        DrawLockCustom(flagsProp, stepProp, "IsLock", "Lock");
        DrawSpeechesGroup(flagsProp, stepProp);
        DrawScenesGroup(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsSound", "Sound");
        DrawFlagAndData(flagsProp, stepProp, "IsFadePlay", "FadeInfo");
        DrawCamerasGroup(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsCursorVisible", "Cursor");
        DrawChoiceCustom(flagsProp, stepProp, "IsChoice", "Choice");
        DrawFlagAndData(flagsProp, stepProp, "IsAction", "Action");
        DrawTooltipCustom(flagsProp, stepProp);
        // IsEventExe는 DrawStepContent에서 직접 그리지 않음 ? Phase 체인 UI가 처리
    }

    private void DrawTooltipCustom(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        SerializedProperty flag = flagsProp.FindPropertyRelative("IsTooltip");
        if (flag == null || !flag.boolValue) return;

        const string dataName = "Tooltip";
        string dataKey = stepProp.propertyPath + "." + dataName;
        if (drawnDataKeys.Contains(dataKey)) return;
        drawnDataKeys.Add(dataKey);

        SerializedProperty dataProp = stepProp.FindPropertyRelative(dataName);
        if (dataProp == null) return;

        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], dataName, true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty contentProp = dataProp.FindPropertyRelative("Content");
        SerializedProperty durationProp = dataProp.FindPropertyRelative("Duration");
        SerializedProperty blockedProp = dataProp.FindPropertyRelative("isBlocked");
        SerializedProperty isRelativeProp = dataProp.FindPropertyRelative("IsRelative");
        SerializedProperty centerObjectProp = dataProp.FindPropertyRelative("CenterObject");
        SerializedProperty positionProp = dataProp.FindPropertyRelative("Position");

        EditorGUILayout.BeginVertical("box");
        if (contentProp != null)
            EditorGUILayout.PropertyField(contentProp, new GUIContent("Content"));

        if (isRelativeProp != null)
            EditorGUILayout.PropertyField(isRelativeProp, new GUIContent("Is Relative"));

        bool isRelative = isRelativeProp != null && isRelativeProp.boolValue;
        if (isRelative && centerObjectProp != null)
        {
            EditorGUILayout.LabelField("Center Object Name (기준 오브젝트)");
            EditorGUILayout.PropertyField(centerObjectProp, GUIContent.none);
        }

        if (positionProp != null)
        {
            string positionLabel = isRelative
                ? "Offset (World Right=X, Up=Y)"
                : "Position (Canvas X/Y, Center = 0,0)";
            EditorGUILayout.PropertyField(positionProp, new GUIContent(positionLabel));
        }

        EditorGUILayout.HelpBox(
            isRelative
                ? "기준 오브젝트 위치에 Right×X + Up×Y 월드 오프셋을 더한 뒤, 매 프레임 실제 표시 카메라 기준의 UI 위치로 변환합니다. 이름이나 경로를 찾지 못하면 Position을 화면 중앙 기준 절대 좌표로 사용합니다."
                : "화면 중앙을 (0,0)으로 하는 Canvas X/Y 절대 좌표입니다.",
            MessageType.Info);

        if (blockedProp != null)
            EditorGUILayout.PropertyField(
                blockedProp,
                new GUIContent("Is Blocked", "켜면 시간 제한 없이 다음 키보드 키 또는 마우스 버튼 입력까지 기다립니다."));

        bool isBlocked = blockedProp != null && blockedProp.boolValue;
        EditorGUI.BeginDisabledGroup(isBlocked);
        if (durationProp != null)
            EditorGUILayout.PropertyField(
                durationProp,
                new GUIContent("Duration", "Is Blocked가 꺼져 있을 때 Tooltip을 표시할 시간입니다."));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            isBlocked
                ? "키보드 키 또는 마우스 버튼 입력이 들어오면 다음 Phase로 진행합니다. Duration 값은 변경하지 않고 보존합니다."
                : "Duration이 끝나면 자동으로 다음 Phase로 진행합니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawConditionStepsArray(SerializedProperty stepsArrayProp, string keySuffix, bool useCondition, SerializedProperty useCondToggleProp, SerializedProperty conditionsArrayProp)
    {
        if (stepsArrayProp == null) return;

        if (!useCondition)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionBanner("EVENT", "수동 PlayEvent 호출 시 실행할 연출입니다.", new Color(0.18f, 0.46f, 0.29f, 1f));
            if (stepsArrayProp.arraySize < 1)
            {
                EditorGUILayout.HelpBox("실행할 Event Step이 없습니다.", MessageType.Warning);
                if (GUILayout.Button("+ Add Manual Event"))
                {
                    stepsArrayProp.arraySize = 1;
                    EnsureEventStepElement(stepsArrayProp, 0);
                }
                EditorGUILayout.EndVertical();
                return;
            }
            var stepProp = stepsArrayProp.GetArrayElementAtIndex(0);
            if (stepProp != null &&
                stepProp.propertyType == SerializedPropertyType.ManagedReference &&
                stepProp.managedReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Manual Event 데이터가 비어 있습니다. 표시만으로는 데이터를 변경하지 않습니다.", MessageType.Warning);
                if (GUILayout.Button("Repair Manual Event"))
                    stepProp.managedReferenceValue = new EventSO.EventStep();
            }
            else if (stepProp != null)
            {
                DrawPhaseList(stepProp, keySuffix, null);
            }
            EditorGUILayout.EndVertical();
            return;
        }

        // Condition과 Event Step을 서로 다른 카드로 분리하여 매핑 관계를 명확히 표시한다.
        string key = BuildKey(string.IsNullOrEmpty(keySuffix) ? "ConditionStepsArray" : $"ConditionStepsArray_{keySuffix}");

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawSectionBanner("WHEN · CONDITIONS", "어떤 조건에서 실행할지 설정합니다. 각 Condition은 같은 번호의 Event Step과 연결됩니다.", new Color(0.18f, 0.39f, 0.68f, 1f));
        if (conditionsArrayProp != null)
            DrawConditionsArrayTop(conditionsArrayProp, keySuffix);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawSectionBanner("THEN · EVENT STEPS", "조건이 참일 때 실행할 연출입니다. 이 영역은 위 Condition 카드의 자식이 아닙니다.", new Color(0.18f, 0.50f, 0.30f, 1f));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Event Steps", EditorStyles.boldLabel, GUILayout.Width(90));
        int oldSize = stepsArrayProp.arraySize;
        int newSize = EditorGUILayout.IntField(oldSize, GUILayout.Width(60));
        if (newSize != oldSize)
        {
            stepsArrayProp.arraySize = Mathf.Max(0, newSize);
            for (int i = oldSize; i < stepsArrayProp.arraySize; i++)
                EnsureEventStepElement(stepsArrayProp, i);
        }
        if (GUILayout.Button("+", GUILayout.Width(22)))
        {
            int index = stepsArrayProp.arraySize++;
            EnsureEventStepElement(stepsArrayProp, index);
        }
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (stepsArrayProp.arraySize > 0) stepsArrayProp.arraySize--; }
        GUILayout.Space(8);
        if (GUILayout.Button("Add Event Step", GUILayout.Width(110)))
        {
            int index = stepsArrayProp.arraySize++;
            EnsureEventStepElement(stepsArrayProp, index);
        }
        EditorGUILayout.EndHorizontal();

        int conditionCount = conditionsArrayProp != null ? conditionsArrayProp.arraySize : 0;
        if (stepsArrayProp.arraySize < conditionCount)
            EditorGUILayout.HelpBox("일부 Condition에 연결된 Event Step이 없습니다. 같은 번호의 Event Step을 추가해 주세요.", MessageType.Warning);

        if (!elementFoldouts.TryGetValue(key, out _)) elementFoldouts[key] = new List<bool>();
        if (!itemHeaderRects.TryGetValue(key, out _)) itemHeaderRects[key] = new List<Rect>();
        itemHeaderRects[key].Clear();
        var folds = elementFoldouts[key];
        while (folds.Count < stepsArrayProp.arraySize) folds.Add(true);
        while (folds.Count > stepsArrayProp.arraySize) folds.RemoveAt(folds.Count - 1);

        for (int i = 0; i < stepsArrayProp.arraySize; i++)
        {
            var stepProp = stepsArrayProp.GetArrayElementAtIndex(i); if (stepProp == null) continue;
            Color oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = i < conditionCount
                ? new Color(0.72f, 1f, 0.78f, 1f)
                : new Color(1f, 0.88f, 0.62f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBackground;
            Rect headerRect = GUILayoutUtility.GetRect(1, EditorGUIUtility.singleLineHeight);
            float rightButtonsWidth = 170f; float handleW = 18f; float pad = 6f;
            Rect handleRect = new Rect(headerRect.x, headerRect.y, handleW, headerRect.height);
            GUI.Label(handleRect, "=");
            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition)) { dragInProgress = true; dragActiveKey = key; dragFromIndex = i; dragToIndex = i; Event.current.Use(); }
            Rect labelRect = new Rect(handleRect.xMax + pad, headerRect.y, headerRect.width - (handleW + pad + rightButtonsWidth), headerRect.height);
            string stepLabel = i < conditionCount
                ? $"Event Step {i + 1}  ←  Condition {i + 1}"
                : i == conditionCount
                    ? "Else Event  ·  수동 PlayEvent의 조건 불일치 때만 실행"
                    : $"Unmapped Event Step {i + 1}";
            folds[i] = EditorGUI.Foldout(labelRect, folds[i], stepLabel, true);
            EditorGUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            var assetInstance = stepsArrayProp.serializedObject.targetObject as EventSO;
            bool canUseElemClipboard = assetInstance != null;
            GUI.enabled = canUseElemClipboard;
            if (GUILayout.Button("Copy", GUILayout.Width(60))) CopyElseStep(assetInstance, stepsArrayProp, i);
            GUI.enabled = canUseElemClipboard && !string.IsNullOrEmpty(stepClipboardJson);
            if (GUILayout.Button("Paste", GUILayout.Width(60))) PasteElseStep(assetInstance, stepsArrayProp, i);
            GUI.enabled = canUseElemClipboard;
            if (GUILayout.Button("Duplicate", GUILayout.Width(80))) DuplicateElseStep(assetInstance, stepsArrayProp, i);
            GUI.enabled = true;
            if (GUILayout.Button("X", GUILayout.Width(22))) { stepsArrayProp.DeleteArrayElementAtIndex(i); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break; }
            EditorGUILayout.EndHorizontal();
            itemHeaderRects[key].Add(headerRect);
            bool missingManagedStep = stepProp.propertyType == SerializedPropertyType.ManagedReference &&
                                      stepProp.managedReferenceValue == null;
            if (folds[i] && missingManagedStep)
            {
                EditorGUILayout.HelpBox("Event Step 데이터가 비어 있습니다. 표시만으로는 데이터를 변경하지 않습니다.", MessageType.Warning);
                if (GUILayout.Button("Repair Event Step"))
                    stepProp.managedReferenceValue = new EventSO.EventStep();
            }
            else if (folds[i])
            {
                DrawPhaseList(stepProp, $"{keySuffix}_Step{i}", null);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        HandleReorderDrag(stepsArrayProp, key);
        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  Phase List: EventExe 체인을 iterative하게 순회하며 동일 형식으로 표시
    //  Phase 1 = firstStepProp 자체, Phase 2+ = EventExe.ConditionSteps[0] 체인
    //  모든 Phase가 동일한 형식: Foldout + [Condition] + [X](Phase1 제외)
    //  Condition OFF → DrawStepContent (현재 Phase 내용)
    //  Condition ON  → Conditions[] + ConditionSteps[] (재귀)
    // ??????????????????????????????????????????
    private void DrawPhaseList(SerializedProperty firstStepProp, string keySuffix, SerializedProperty topUseCondToggleProp)
    {
        var steps = new List<PhaseNode>();
        CollectPhaseChain(firstStepProp, steps);

        for (int pi = 0; pi < steps.Count; pi++)
        {
            var node = steps[pi];
            bool isFirst = (pi == 0);

            if (pi > 0) EditorGUILayout.Space(4);

            string phaseKey = BuildKey(node.StepProp.propertyPath, $".PhaseList{pi}_{keySuffix}");
            if (!masterFoldouts.TryGetValue(phaseKey, out _)) masterFoldouts[phaseKey] = true;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            masterFoldouts[phaseKey] = EditorGUILayout.Foldout(masterFoldouts[phaseKey], $"Phase {pi + 1}", true);
            GUILayout.FlexibleSpace();

            // Phase 실행 방식: 클릭할 때 None ↔ Condition으로 명확하게 전환한다.
            var useCondProp = isFirst ? topUseCondToggleProp : node.UseCondProp;
            if (useCondProp != null)
            {
                bool curCond = useCondProp.boolValue;
                int newMode = GUILayout.Toolbar(curCond ? 1 : 0, new[] { "None", "Condition" }, GUILayout.Width(150));
                bool newCond = newMode == 1;
                if (newCond != curCond)
                {
                    useCondProp.boolValue = newCond;
                    useCondProp.serializedObject.ApplyModifiedProperties();
                }
            }

            // X 버튼 (Phase 1 제외)
            if (!isFirst && node.IsEventExeProp != null)
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f, 1f);
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    node.IsEventExeProp.boolValue = false;
                    node.IsEventExeProp.serializedObject.ApplyModifiedProperties();
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = prevBg;
            }

            EditorGUILayout.EndHorizontal();

            if (masterFoldouts[phaseKey])
            {
                bool useCond = isFirst
                    ? (topUseCondToggleProp != null && topUseCondToggleProp.boolValue)
                    : (node.UseCondProp != null && node.UseCondProp.boolValue);

                if (useCond && isFirst)
                {
                    // Phase 1 Condition ON → OnInspectorGUI가 재호출로 처리
                }
                else if (useCond && !isFirst)
                {
                    // Phase N (N>1) Condition ON → Conditions + ConditionSteps 재귀
                    DrawConditionStepsArray(
                        node.CondStepsProp,
                        $"{keySuffix}_P{pi}",
                        true,
                        node.UseCondProp,
                        node.CondsProp
                    );
                }
                else
                {
                    // Condition OFF → 이 Phase의 내용을 직접 그림
                    drawnDataKeys.Clear();
                    DrawStepContent(node.StepProp);
                }
            }

            EditorGUILayout.EndVertical();
        }

        DrawBrokenNextPhaseRepair(steps[steps.Count - 1]);

        // "+ Next Phase" 버튼
        var lastNode = steps[steps.Count - 1];
        var lastFlags = lastNode.StepProp.FindPropertyRelative("Flags");
        var lastIsExe = lastFlags?.FindPropertyRelative("IsEventExe");
        if (lastIsExe != null && !lastIsExe.boolValue)
        {
            EditorGUILayout.Space(2);
            if (GUILayout.Button($"+ Next Phase (Phase {steps.Count + 1})", GUILayout.Height(22)))
            {
                lastIsExe.boolValue = true;
                var exeProp = lastNode.StepProp.FindPropertyRelative("EventExe");
                if (exeProp != null)
                {
                    var uc = exeProp.FindPropertyRelative("UseCondition");
                    if (uc != null) uc.boolValue = false;
                    var cs = exeProp.FindPropertyRelative("ConditionSteps");
                    if (cs != null)
                    {
                        if (cs.arraySize < 1) cs.arraySize = 1;
                        EnsureEventStepElement(cs, 0);
                    }
                }
                lastNode.StepProp.serializedObject.ApplyModifiedProperties();
            }
        }
    }

    private struct PhaseNode
    {
        public SerializedProperty StepProp;
        public SerializedProperty IsEventExeProp;   // 이전 Phase의 Flags.IsEventExe (Phase1이면 null)
        public SerializedProperty UseCondProp;       // EventExeData.UseCondition (Phase1이면 null)
        public SerializedProperty CondsProp;         // EventExeData.Conditions (Phase1이면 null)
        public SerializedProperty CondStepsProp;     // EventExeData.ConditionSteps (Phase1이면 null)
    }

    private void CollectPhaseChain(SerializedProperty firstStep, List<PhaseNode> result)
    {
        result.Clear();
        result.Add(new PhaseNode { StepProp = firstStep });

        var current = firstStep;
        for (int safety = 0; safety < 50; safety++)
        {
            var flags = current.FindPropertyRelative("Flags");
            if (flags == null) break;
            var isExe = flags.FindPropertyRelative("IsEventExe");
            if (isExe == null || !isExe.boolValue) break;

            var exe = current.FindPropertyRelative("EventExe");
            if (exe == null) break;
            var useCond = exe.FindPropertyRelative("UseCondition");
            var conds = exe.FindPropertyRelative("Conditions");
            var condSteps = exe.FindPropertyRelative("ConditionSteps");
            if (condSteps == null || condSteps.arraySize < 1) break;

            var nextStep = condSteps.GetArrayElementAtIndex(0);
            if (nextStep == null ||
                (nextStep.propertyType == SerializedPropertyType.ManagedReference &&
                 nextStep.managedReferenceValue == null)) break;

            result.Add(new PhaseNode
            {
                StepProp = nextStep,
                IsEventExeProp = isExe,
                UseCondProp = useCond,
                CondsProp = conds,
                CondStepsProp = condSteps,
            });

            bool nextUseCond = useCond != null && useCond.boolValue;
            if (nextUseCond) break;

            current = nextStep;
        }
    }

    private void DrawObjectsGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsObjects");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".ObjectsGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Objects", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var objectsProp = stepProp.FindPropertyRelative("Objects");
        if (objectsProp == null) { EditorGUILayout.LabelField("Objects is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsSpawnObject", "IsDestroyObject", "IsEnableObject", "IsDisableObject", "IsRename" };
        string[] subLabels = { "Spawn Object", "Destroy Object", "Enable Object", "Disable Object", "Rename" };
        DrawSubFlagTogglesOnProp(objectsProp, subFlags, subLabels);

        if (GetBoolProp(objectsProp, "IsSpawnObject"))
        {
            var spawnProp = objectsProp.FindPropertyRelative("SpawnObject");
            if (spawnProp != null) DrawSpawnObjectDirect(spawnProp, stepProp.propertyPath + ".Objects.SpawnObject");
        }
        if (GetBoolProp(objectsProp, "IsDestroyObject"))
        {
            var p = objectsProp.FindPropertyRelative("DestroyObject");
            if (p != null) DrawStringArrayDirect(p, "DestroyObjectNames", "DestroyObject", "Object Names (Empty = Caller)", stepProp.propertyPath + ".Objects.DestroyObject");
        }
        if (GetBoolProp(objectsProp, "IsEnableObject"))
        {
            var p = objectsProp.FindPropertyRelative("EnableObject");
            if (p != null) DrawStringArrayDirect(p, "EnableObjectNames", "EnableObject", "Object Names (Empty = Caller)", stepProp.propertyPath + ".Objects.EnableObject");
        }
        if (GetBoolProp(objectsProp, "IsDisableObject"))
        {
            var p = objectsProp.FindPropertyRelative("DisableObject");
            if (p != null) DrawStringArrayDirect(p, "DisableObjectNames", "DisableObject", "Object Names (Empty = Caller)", stepProp.propertyPath + ".Objects.DisableObject");
        }
        if (GetBoolProp(objectsProp, "IsRename"))
        {
            var p = objectsProp.FindPropertyRelative("Rename");
            if (p != null) DrawGenericDataDirect(p, "Rename", stepProp.propertyPath + ".Objects.Rename");
        }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  그룹 Drawer: Speeches
    // ??????????????????????????????????????????
    private void DrawSpeechesGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsSpeeches");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".SpeechesGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Speeches", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var speechesProp = stepProp.FindPropertyRelative("Speeches");
        if (speechesProp == null) { EditorGUILayout.LabelField("Speeches is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsSoftSpeech", "IsHardSpeech" };
        string[] subLabels = { "Soft Speech", "Hard Speech" };
        DrawSubFlagTogglesOnProp(speechesProp, subFlags, subLabels);

        if (GetBoolProp(speechesProp, "IsSoftSpeech"))
        {
            var p = speechesProp.FindPropertyRelative("SoftSpeech");
            if (p != null) DrawGenericDataDirect(p, "SoftSpeech", stepProp.propertyPath + ".Speeches.SoftSpeech");
        }
        if (GetBoolProp(speechesProp, "IsHardSpeech"))
        {
            var p = speechesProp.FindPropertyRelative("HardSpeech");
            if (p != null) DrawGenericDataDirect(p, "HardSpeech", stepProp.propertyPath + ".Speeches.HardSpeech");
        }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  그룹 Drawer: Scenes
    // ??????????????????????????????????????????
    private void DrawScenesGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsScenes");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".ScenesGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Scenes", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var scenesProp = stepProp.FindPropertyRelative("Scenes");
        if (scenesProp == null) { EditorGUILayout.LabelField("Scenes is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsSetActiveScene", "IsSceneChange", "IsSceneAdd", "IsSceneOff", "IsScenePause", "IsSceneUnPause" };
        string[] subLabels = { "Set Active Scene", "Scene Change", "Scene Add", "Scene Off", "Scene Pause", "Scene UnPause" };
        DrawSubFlagTogglesOnProp(scenesProp, subFlags, subLabels);

        if (GetBoolProp(scenesProp, "IsSetActiveScene")) { var p = scenesProp.FindPropertyRelative("SetActiveScene"); if (p != null) DrawGenericDataDirect(p, "SetActiveScene", stepProp.propertyPath + ".Scenes.SetActiveScene"); }
        if (GetBoolProp(scenesProp, "IsSceneChange")) { var p = scenesProp.FindPropertyRelative("SceneChange"); if (p != null) DrawGenericDataDirect(p, "SceneChange", stepProp.propertyPath + ".Scenes.SceneChange"); }
        if (GetBoolProp(scenesProp, "IsSceneAdd")) { var p = scenesProp.FindPropertyRelative("SceneAdd"); if (p != null) DrawGenericDataDirect(p, "SceneAdd", stepProp.propertyPath + ".Scenes.SceneAdd"); }
        if (GetBoolProp(scenesProp, "IsSceneOff")) { var p = scenesProp.FindPropertyRelative("SceneOff"); if (p != null) DrawGenericDataDirect(p, "SceneOff", stepProp.propertyPath + ".Scenes.SceneOff"); }
        if (GetBoolProp(scenesProp, "IsScenePause")) { var p = scenesProp.FindPropertyRelative("ScenePause"); if (p != null) DrawGenericDataDirect(p, "ScenePause", stepProp.propertyPath + ".Scenes.ScenePause"); }
        if (GetBoolProp(scenesProp, "IsSceneUnPause")) { var p = scenesProp.FindPropertyRelative("SceneUnPause"); if (p != null) DrawGenericDataDirect(p, "SceneUnPause", stepProp.propertyPath + ".Scenes.SceneUnPause"); }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  그룹 Drawer: Cameras
    // ??????????????????????????????????????????
    private void DrawCamerasGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsCameras");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".CamerasGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Cameras", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var camerasProp = stepProp.FindPropertyRelative("Cameras");
        if (camerasProp == null) { EditorGUILayout.LabelField("Cameras is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsCameraMove", "IsCameraAiming" };
        string[] subLabels = { "Camera Move", "Camera Aim" };
        DrawSubFlagTogglesOnProp(camerasProp, subFlags, subLabels);

        if (GetBoolProp(camerasProp, "IsCameraMove")) { var p = camerasProp.FindPropertyRelative("CameraMovement"); if (p != null) DrawCameraMovementDirect(p, stepProp.propertyPath + ".Cameras.CameraMovement"); }
        if (GetBoolProp(camerasProp, "IsCameraAiming")) { var p = camerasProp.FindPropertyRelative("CameraAim"); if (p != null) DrawGenericDataDirect(p, "CameraAim", stepProp.propertyPath + ".Cameras.CameraAim"); }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  그룹 Drawer: Components
    // ??????????????????????????????????????????
    private void DrawComponentsGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsComponents");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".ComponentsGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Components", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var compsProp = stepProp.FindPropertyRelative("Components");
        if (compsProp == null) { EditorGUILayout.LabelField("Components is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsEnableComponent", "IsDisableComponent", "IsDisableColliderObject", "IsUpdateComponent" };
        string[] subLabels = { "Enable Component", "Disable Component", "Disable Collider", "Update Component" };
        DrawSubFlagTogglesOnProp(compsProp, subFlags, subLabels);

        if (GetBoolProp(compsProp, "IsEnableComponent")) { var p = compsProp.FindPropertyRelative("EnableComponent"); if (p != null) DrawComponentDataDirect(p, "EnableComponent", "ComponentDatas", stepProp.propertyPath + ".Components.EnableComponent"); }
        if (GetBoolProp(compsProp, "IsDisableComponent")) { var p = compsProp.FindPropertyRelative("DisableComponent"); if (p != null) DrawComponentDataDirect(p, "DisableComponent", "ComponentDatas", stepProp.propertyPath + ".Components.DisableComponent"); }
        if (GetBoolProp(compsProp, "IsDisableColliderObject")) { var p = compsProp.FindPropertyRelative("DisableColliderObject"); if (p != null) DrawStringArrayDirect(p, "DisableColliderObjectNames", "DisableCollider", "Object Names (Empty = Caller)", stepProp.propertyPath + ".Components.DisableCollider"); }
        if (GetBoolProp(compsProp, "IsUpdateComponent")) { var p = compsProp.FindPropertyRelative("UpdateComponent"); if (p != null) DrawUpdateComponentDirect(p, stepProp.propertyPath + ".Components.UpdateComponent"); }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  그룹 Drawer: Transforms
    // ??????????????????????????????????????????
    private void DrawTransformsGroup(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        var groupFlag = flagsProp.FindPropertyRelative("IsTransforms");
        if (groupFlag == null || !groupFlag.boolValue) return;

        string foldKey = BuildKey(stepProp.propertyPath, ".TransformsGroup");
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "▼ Transforms", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");

        var transformsProp = stepProp.FindPropertyRelative("Transforms");
        if (transformsProp == null) { EditorGUILayout.LabelField("Transforms is null"); EditorGUILayout.EndVertical(); return; }

        string[] subFlags = { "IsMoveObject", "IsRotateObject" };
        string[] subLabels = { "Move Object", "Rotate Object" };
        DrawSubFlagTogglesOnProp(transformsProp, subFlags, subLabels);

        if (GetBoolProp(transformsProp, "IsMoveObject")) { var p = transformsProp.FindPropertyRelative("MoveObject"); if (p != null) DrawMoveObjectDirect(p, stepProp.propertyPath + ".Transforms.MoveObject"); }
        if (GetBoolProp(transformsProp, "IsRotateObject")) { var p = transformsProp.FindPropertyRelative("RotateObject"); if (p != null) DrawRotateObjectDirect(p, stepProp.propertyPath + ".Transforms.RotateObject"); }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  헬퍼: 그룹 래퍼 프로퍼티에서 서브 플래그 토글
    // ??????????????????????????????????????????
    private void DrawSubFlagTogglesOnProp(SerializedProperty groupProp, string[] subFlagNames, string[] subLabels)
    {
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < subFlagNames.Length; i++)
        {
            var p = groupProp.FindPropertyRelative(subFlagNames[i]);
            if (p == null) continue;
            p.boolValue = GUILayout.Toggle(p.boolValue, subLabels[i], "Button", GUILayout.MinWidth(80));
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool GetBoolProp(SerializedProperty parent, string name)
    {
        var p = parent.FindPropertyRelative(name);
        return p != null && p.boolValue;
    }

    // ??????????????????????????????????????????
    //  Direct Drawer 헬퍼들
    // ??????????????????????????????????????????
    private void DrawGenericDataDirect(SerializedProperty dataProp, string label, string uniqueKey)
    {
        if (dataProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], label, true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");
        var iterator = dataProp.Copy();
        var end = iterator.GetEndProperty();
        if (iterator.NextVisible(true))
        {
            while (!SerializedProperty.EqualContents(iterator, end))
            {
                EditorGUILayout.PropertyField(iterator, true);
                if (!iterator.NextVisible(false)) break;
            }
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawStringArrayDirect(SerializedProperty dataProp, string arrayFieldName, string label, string hint, string uniqueKey)
    {
        if (dataProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], label, true);
        if (!masterFoldouts[foldKey]) return;

        var arrProp = dataProp.FindPropertyRelative(arrayFieldName);
        if (arrProp == null) { EditorGUILayout.BeginVertical("box"); EditorGUILayout.PropertyField(dataProp, true); EditorGUILayout.EndVertical(); return; }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(hint, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();
        EditorGUI.indentLevel++;
        for (int i = 0; i < arrProp.arraySize; i++) { var elem = arrProp.GetArrayElementAtIndex(i); if (elem != null) EditorGUILayout.PropertyField(elem, new GUIContent($"[{i}]")); }
        EditorGUI.indentLevel--;
        EditorGUILayout.EndVertical();
    }

    private void DrawComponentDataDirect(SerializedProperty dataProp, string label, string arrayFieldName, string uniqueKey)
    {
        if (dataProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        var arrProp = dataProp.FindPropertyRelative(arrayFieldName);
        if (arrProp == null) { EditorGUILayout.PropertyField(dataProp, true); return; }

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], label, true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"[{i}]", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            var goNameProp = elem.FindPropertyRelative("gameObjectName");
            if (goNameProp != null) { EditorGUILayout.LabelField("GameObject Name (Empty = Caller)"); EditorGUILayout.PropertyField(goNameProp, GUIContent.none); }
            var compNameProp = elem.FindPropertyRelative("componentName");
            if (compNameProp != null) EditorGUILayout.PropertyField(compNameProp, new GUIContent("Component Name"));
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawUpdateComponentDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        var arrProp = dataProp.FindPropertyRelative("UpdateComponents");
        if (arrProp == null) { EditorGUILayout.PropertyField(dataProp, true); return; }

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "UpdateComponent", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("UpdateComponents", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"[{i}]", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            var goNameProp = elem.FindPropertyRelative("gameObjectName");
            if (goNameProp != null) { EditorGUILayout.LabelField("GameObject Name (Empty = Caller)"); EditorGUILayout.PropertyField(goNameProp, GUIContent.none); }
            var compNameProp = elem.FindPropertyRelative("componentName");
            if (compNameProp != null) EditorGUILayout.PropertyField(compNameProp, new GUIContent("Component Name"));
            var propNameProp = elem.FindPropertyRelative("propertyName");
            if (propNameProp != null) EditorGUILayout.PropertyField(propNameProp, new GUIContent("Property/Field Name"));
            var valueTypeProp = elem.FindPropertyRelative("valueType");
            if (valueTypeProp != null) EditorGUILayout.PropertyField(valueTypeProp, new GUIContent("Value Type"));
            int vt = valueTypeProp != null ? valueTypeProp.enumValueIndex : 0;
            switch (vt)
            {
                case 0: var ip = elem.FindPropertyRelative("intValue"); if (ip != null) EditorGUILayout.PropertyField(ip, new GUIContent("Value (Int)")); break;
                case 1: var fp = elem.FindPropertyRelative("floatValue"); if (fp != null) EditorGUILayout.PropertyField(fp, new GUIContent("Value (Float)")); break;
                case 2: var bp = elem.FindPropertyRelative("boolValue"); if (bp != null) EditorGUILayout.PropertyField(bp, new GUIContent("Value (Bool)")); break;
                case 3: var sp = elem.FindPropertyRelative("stringValue"); if (sp != null) EditorGUILayout.PropertyField(sp, new GUIContent("Value (String)")); break;
                case 4: var gp = elem.FindPropertyRelative("gameObjectValue"); if (gp != null) EditorGUILayout.PropertyField(gp, new GUIContent("Value (GameObject)")); break;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawSpawnObjectDirect(SerializedProperty spawnProp, string uniqueKey)
    {
        if (spawnProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "SpawnObject", true);
        if (!masterFoldouts[foldKey]) return;

        var arrProp = spawnProp.FindPropertyRelative("SpawnDatas");
        if (arrProp == null) return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SpawnDatas", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Spawn [{i}]", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("prefab"));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("naming"));
            var parentNm = elem.FindPropertyRelative("parentName");
            if (parentNm != null) { EditorGUILayout.LabelField("Parent Name (Empty = Root)"); EditorGUILayout.PropertyField(parentNm, GUIContent.none); }
            var sceneNm = elem.FindPropertyRelative("sceneName");
            if (sceneNm != null) { EditorGUILayout.LabelField("Scene Name (Empty = Caller's Scene)"); EditorGUILayout.PropertyField(sceneNm, GUIContent.none); }
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("rotation"));
            var isUIProp = elem.FindPropertyRelative("IsUI");
            EditorGUILayout.PropertyField(isUIProp, new GUIContent("Is UI"));
            if (isUIProp != null && isUIProp.boolValue)
            {
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("AnchoredPosition"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("AnchorMin"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("AnchorMax"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("Pivot"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("LocalScale"));
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("LocalEulerAngles"));
            }
            else
            {
                var isLocalProp = elem.FindPropertyRelative("IsLocal");
                EditorGUILayout.PropertyField(isLocalProp, new GUIContent("Is Local"));
                if (isLocalProp != null && isLocalProp.boolValue)
                {
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("LocalCenterName"));
                    var localOff = elem.FindPropertyRelative("LocalOffset");
                    if (localOff != null) { EditorGUILayout.LabelField("Offset (Right=X, Up=Y, Front=Z)"); EditorGUILayout.PropertyField(localOff, GUIContent.none); }
                }
                else { EditorGUILayout.PropertyField(elem.FindPropertyRelative("position"), new GUIContent("World Position")); }
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawMoveObjectDirect(SerializedProperty moveProp, string uniqueKey)
    {
        if (moveProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "MoveObject", true);
        if (!masterFoldouts[foldKey]) return;

        var arrProp = moveProp.FindPropertyRelative("MoveObjects");
        if (arrProp == null) return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("MoveObjects", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Move [{i}]", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            var objNameProp = elem.FindPropertyRelative("objectName");
            if (objNameProp != null) { EditorGUILayout.LabelField("Object Name (Empty = Caller)"); EditorGUILayout.PropertyField(objNameProp, GUIContent.none); }
            var isRelativeProp = elem.FindPropertyRelative("isRelative");
            bool isRelativePrecheck = isRelativeProp != null && isRelativeProp.boolValue;
            if (!isRelativePrecheck)
            {
                var isDrawerProp = elem.FindPropertyRelative("isDrawer");
                if (isDrawerProp != null) EditorGUILayout.PropertyField(isDrawerProp, new GUIContent("Is Drawer"));
                if (isDrawerProp != null && isDrawerProp.boolValue)
                {
                    var dOff = elem.FindPropertyRelative("drawerOffset");
                    if (dOff != null) { EditorGUILayout.LabelField("Drawer Offset (Right=X, Up=Y, Front=Z)"); EditorGUILayout.PropertyField(dOff, GUIContent.none); }
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("Duration"), new GUIContent("Duration"));
                    EditorGUI.indentLevel--; EditorGUILayout.EndVertical(); continue;
                }
            }
            // ── Start Section ──
            var isAnotherStartProp = elem.FindPropertyRelative("IsAnotherStartPosition");
            if (isAnotherStartProp != null) EditorGUILayout.PropertyField(isAnotherStartProp, new GUIContent("Use Custom Start Position"));
            if (isAnotherStartProp != null && isAnotherStartProp.boolValue)
            {
                EditorGUILayout.BeginVertical("helpbox");
                EditorGUILayout.LabelField("── Start ──", EditorStyles.miniBoldLabel);
                var startIsRelProp = elem.FindPropertyRelative("startIsRelative");
                if (startIsRelProp != null) EditorGUILayout.PropertyField(startIsRelProp, new GUIContent("Start Is Relative"));
                bool startIsRel = startIsRelProp != null && startIsRelProp.boolValue;
                if (startIsRel)
                    EditorGUILayout.PropertyField(elem.FindPropertyRelative("startTargetName"), new GUIContent("Start Target Name (Empty = Self)"));
                string startLabel = startIsRel ? "Start Offset (Relative)" : "Start World Position";
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("startPosition"), new GUIContent(startLabel));
                EditorGUILayout.EndVertical();
            }
            // ── Goal Section ──
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("── Goal ──", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(isRelativeProp, new GUIContent("Goal Is Relative"));
            bool isRelative = isRelativeProp != null && isRelativeProp.boolValue;
            if (isRelative)
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetName"), new GUIContent("Goal Target Name (Empty = Self)"));
            string posLabel = isRelative ? "Goal Offset (Relative)" : "Goal World Position";
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("targetPosition"), new GUIContent(posLabel));
            EditorGUILayout.EndVertical();
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("Duration"), new GUIContent("Duration"));
            var typeProp = elem.FindPropertyRelative("movementType");
            EditorGUILayout.PropertyField(typeProp, new GUIContent("Movement Type"));
            if (typeProp != null)
            {
                int mt = typeProp.enumValueIndex;
                switch (mt)
                {
                    case 1: // Spiral
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("SpiralRadius"));
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("SpiralTurns"));
                        break;
                    case 2: // ZigZag
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("ZigZagAmplitude"));
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("ZigZagFrequency"));
                        break;
                    case 3: // Bounce
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("BounceCount"));
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("BounceHeight"));
                        break;
                    case 4: // EaseInOut
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("EaseExponent"), new GUIContent("Ease Exponent (2=Quad, 3=Cubic...)"));
                        break;
                    case 5: // Arc
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("ArcHeight"));
                        break;
                    case 6: // Parabola
                        EditorGUILayout.PropertyField(elem.FindPropertyRelative("ParabolaPeakHeight"), new GUIContent("Peak Height"));
                        break;
                }
            }
            EditorGUI.indentLevel--; EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawRotateObjectDirect(SerializedProperty rotateProp, string uniqueKey)
    {
        if (rotateProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "RotateObject", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");
        var objNameProp = rotateProp.FindPropertyRelative("ObjectName");
        if (objNameProp != null) { EditorGUILayout.LabelField("Object Name (Empty = Caller)"); EditorGUILayout.PropertyField(objNameProp, GUIContent.none); }
        var isLookAtProp = rotateProp.FindPropertyRelative("isLookAt");
        if (isLookAtProp != null) EditorGUILayout.PropertyField(isLookAtProp, new GUIContent("Is LookAt"));
        bool isLookAt = (isLookAtProp != null && isLookAtProp.boolValue);
        if (isLookAt) { EditorGUILayout.PropertyField(rotateProp.FindPropertyRelative("LookAtName")); }
        else
        {
            var isDoorProp = rotateProp.FindPropertyRelative("isDoor");
            if (isDoorProp != null) EditorGUILayout.PropertyField(isDoorProp, new GUIContent("Is Door"));
            if (isDoorProp != null && isDoorProp.boolValue)
            {
                var offsetProp = rotateProp.FindPropertyRelative("doorEulerOffset");
                if (offsetProp != null) { EditorGUILayout.LabelField("Door Offset (Right=X, Up=Y, Front=Z)"); EditorGUILayout.PropertyField(offsetProp, GUIContent.none); }
                var isDeltaPropDoor = rotateProp.FindPropertyRelative("isDelta");
                if (isDeltaPropDoor != null) EditorGUILayout.PropertyField(isDeltaPropDoor, new GUIContent("Is Delta"));
            }
            else
            {
                var isRelativeProp = rotateProp.FindPropertyRelative("isRelative");
                if (isRelativeProp != null) EditorGUILayout.PropertyField(isRelativeProp, new GUIContent("Is Relative"));
                bool isRelativeVal = isRelativeProp != null && isRelativeProp.boolValue;
                if (isRelativeVal) { EditorGUILayout.PropertyField(rotateProp.FindPropertyRelative("TargetName"), new GUIContent("Target Name")); }
                else { var isLocalProp = rotateProp.FindPropertyRelative("isLocal"); if (isLocalProp != null) EditorGUILayout.PropertyField(isLocalProp, new GUIContent("Is Local")); }
                var isDeltaProp = rotateProp.FindPropertyRelative("isDelta");
                if (isDeltaProp != null) EditorGUILayout.PropertyField(isDeltaProp, new GUIContent("Is Delta"));
                var eulerProp = rotateProp.FindPropertyRelative("eulerAngles");
                if (eulerProp != null) { string l = (isDeltaProp != null && isDeltaProp.boolValue) ? "Relative Euler" : "Absolute Euler"; EditorGUILayout.PropertyField(eulerProp, new GUIContent(l)); }
            }
        }
        EditorGUILayout.PropertyField(rotateProp.FindPropertyRelative("Duration"));
        EditorGUILayout.EndVertical();
    }

    private void DrawCameraMovementDirect(SerializedProperty camProp, string uniqueKey)
    {
        if (camProp == null) return;
        if (drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "CameraMovement", true);
        if (!masterFoldouts[foldKey]) return;

        EditorGUILayout.BeginVertical("box");
        var useMainProp = camProp.FindPropertyRelative("IsCameraMoveUseMain");
        if (useMainProp != null) EditorGUILayout.PropertyField(useMainProp, new GUIContent("Use Main Camera"));
        var isRelativeProp = camProp.FindPropertyRelative("IsRelative");
        if (isRelativeProp != null) EditorGUILayout.PropertyField(isRelativeProp, new GUIContent("Is Relative"));
        bool isRelative = isRelativeProp != null && isRelativeProp.boolValue;
        if (isRelative) { var centerProp = camProp.FindPropertyRelative("CenterObject"); if (centerProp != null) { EditorGUILayout.LabelField("Center Object Name (기준 오브젝트)"); EditorGUILayout.PropertyField(centerProp, GUIContent.none); } }
        string startLabel = isRelative ? "Start Offset (Right=X, Up=Y, Front=Z)" : "Start Position (World)";
        string endLabel = isRelative ? "End Offset (Right=X, Up=Y, Front=Z)" : "End Position (World)";
        var startProp = camProp.FindPropertyRelative("StartPosition"); if (startProp != null) EditorGUILayout.PropertyField(startProp, new GUIContent(startLabel));
        var endProp = camProp.FindPropertyRelative("EndPosition"); if (endProp != null) EditorGUILayout.PropertyField(endProp, new GUIContent(endLabel));
        var startTimeProp = camProp.FindPropertyRelative("StartTime"); if (startTimeProp != null) EditorGUILayout.PropertyField(startTimeProp, new GUIContent("Start Time"));
        var endTimeProp = camProp.FindPropertyRelative("EndTime"); if (endTimeProp != null) EditorGUILayout.PropertyField(endTimeProp, new GUIContent("End Time"));
        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  독립 Drawer들
    // ??????????????????????????????????????????
    private void DrawLockCustom(SerializedProperty flagsProp, SerializedProperty stepProp, string lockFlagName, string lockDataName)
    {
        var lockFlag = flagsProp.FindPropertyRelative(lockFlagName); if (lockFlag == null || !lockFlag.boolValue) return;
        string dataKey = stepProp.propertyPath + "." + lockDataName; if (drawnDataKeys.Contains(dataKey)) return; drawnDataKeys.Add(dataKey);
        var lockProp = stepProp.FindPropertyRelative(lockDataName); if (lockProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + lockDataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], lockDataName, true);
        if (!masterFoldouts[foldKey]) return;
        EditorGUILayout.BeginVertical("box");
        var moveProp = lockProp.FindPropertyRelative("IsLockMove"); var camProp = lockProp.FindPropertyRelative("IsLockCamera");
        if (moveProp != null) EditorGUILayout.PropertyField(moveProp, new GUIContent("Lock Move"));
        if (camProp != null) EditorGUILayout.PropertyField(camProp, new GUIContent("Lock Camera"));
        EditorGUILayout.EndVertical();
    }

    private void DrawChoiceCustom(SerializedProperty flagsProp, SerializedProperty stepProp, string flagName, string dataName)
    {
        var flag = flagsProp.FindPropertyRelative(flagName); if (flag == null || !flag.boolValue) return;
        string dataKey = stepProp.propertyPath + "." + dataName; if (drawnDataKeys.Contains(dataKey)) return; drawnDataKeys.Add(dataKey);
        var dataProp = stepProp.FindPropertyRelative(dataName); if (dataProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], dataName, true);
        if (!masterFoldouts[foldKey]) return;
        var arrProp = dataProp.FindPropertyRelative("Candidates"); if (arrProp == null) { EditorGUILayout.PropertyField(dataProp, true); return; }
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Candidates", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box"); EditorGUILayout.LabelField($"Candidate [{i}]", EditorStyles.miniBoldLabel); EditorGUI.indentLevel++;
            var textProp = elem.FindPropertyRelative("Text"); var valueNameProp = elem.FindPropertyRelative("ValueName"); var typeProp = elem.FindPropertyRelative("type");
            if (textProp != null) EditorGUILayout.PropertyField(textProp, new GUIContent("Text"));
            if (valueNameProp != null) EditorGUILayout.PropertyField(valueNameProp, new GUIContent("Value Name"));
            if (typeProp != null) EditorGUILayout.PropertyField(typeProp, new GUIContent("Value Type"));
            int vt = typeProp != null ? typeProp.enumValueIndex : 0;
            switch (vt) { case 0: var intP = elem.FindPropertyRelative("intValue"); if (intP != null) EditorGUILayout.PropertyField(intP, new GUIContent("Int Value")); break; case 1: var floatP = elem.FindPropertyRelative("floatValue"); if (floatP != null) EditorGUILayout.PropertyField(floatP, new GUIContent("Float Value")); break; case 2: var boolP = elem.FindPropertyRelative("boolValue"); if (boolP != null) EditorGUILayout.PropertyField(boolP, new GUIContent("Bool Value")); break; case 3: var strP = elem.FindPropertyRelative("stringValue"); if (strP != null) EditorGUILayout.PropertyField(strP, new GUIContent("String Value")); break; case 4: var goP = elem.FindPropertyRelative("gameObjectValue"); if (goP != null) EditorGUILayout.PropertyField(goP, new GUIContent("GameObject Value")); break; }
            EditorGUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(90))) { arrProp.DeleteArrayElementAtIndex(i); EditorGUILayout.EndHorizontal(); EditorGUI.indentLevel--; EditorGUILayout.EndVertical(); break; }
            EditorGUILayout.EndHorizontal(); EditorGUI.indentLevel--; EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawSetGlobalsCustom(SerializedProperty flagsProp, SerializedProperty stepProp, string flagName, string dataName)
    {
        var flag = flagsProp.FindPropertyRelative(flagName); if (flag == null || !flag.boolValue) return;
        string dataKey = stepProp.propertyPath + "." + dataName; if (drawnDataKeys.Contains(dataKey)) return; drawnDataKeys.Add(dataKey);
        var setProp = stepProp.FindPropertyRelative(dataName); if (setProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], dataName, true);
        if (!masterFoldouts[foldKey]) return;
        var arrProp = setProp.FindPropertyRelative("SetValues"); if (arrProp == null) { EditorGUILayout.PropertyField(setProp, true); return; }
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SetValues", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Size", GUILayout.Width(40));
        int newSize = EditorGUILayout.IntField(arrProp.arraySize, GUILayout.Width(60));
        if (newSize != arrProp.arraySize) arrProp.arraySize = Mathf.Max(0, newSize);
        if (GUILayout.Button("+", GUILayout.Width(22))) arrProp.arraySize++;
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (arrProp.arraySize > 0) arrProp.arraySize--; }
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < arrProp.arraySize; i++)
        {
            var elem = arrProp.GetArrayElementAtIndex(i); if (elem == null) continue;
            EditorGUILayout.BeginVertical("box"); EditorGUILayout.LabelField($"SetValue [{i}]", EditorStyles.miniBoldLabel); EditorGUI.indentLevel++;
            var prefProp = elem.FindPropertyRelative("IsPrefData"); var setTypeProp = elem.FindPropertyRelative("setType"); var valueTypeProp = elem.FindPropertyRelative("valueType"); var nameProp = elem.FindPropertyRelative("ValueName");
            int currentValueType = valueTypeProp != null ? valueTypeProp.enumValueIndex : 0;
            bool isGameObjectValue = currentValueType == (int)EventSO.ValueType.GameObject;
            bool setOnlyValue = currentValueType == (int)EventSO.ValueType.String || isGameObjectValue;
            if (prefProp != null)
            {
                EditorGUI.BeginDisabledGroup(isGameObjectValue);
                EditorGUILayout.PropertyField(prefProp, new GUIContent("Use PlayerPrefs"));
                EditorGUI.EndDisabledGroup();
            }
            if (setTypeProp != null)
            {
                EditorGUI.BeginDisabledGroup(setOnlyValue);
                EditorGUILayout.PropertyField(setTypeProp, new GUIContent("Operation"));
                EditorGUI.EndDisabledGroup();
            }
            if (nameProp != null) EditorGUILayout.PropertyField(nameProp, new GUIContent("Key"));
            if (valueTypeProp != null) EditorGUILayout.PropertyField(valueTypeProp, new GUIContent("Value Type"));
            int vt = valueTypeProp != null ? valueTypeProp.enumValueIndex : 0;
            switch (vt) { case 0: EditorGUILayout.PropertyField(elem.FindPropertyRelative("intValue"), new GUIContent("Value (Int)")); break; case 1: EditorGUILayout.PropertyField(elem.FindPropertyRelative("floatValue"), new GUIContent("Value (Float)")); break; case 2: EditorGUILayout.PropertyField(elem.FindPropertyRelative("boolValue"), new GUIContent("Value (Bool)")); break; case 3: EditorGUILayout.PropertyField(elem.FindPropertyRelative("stringValue"), new GUIContent("Value (String)")); break; case 4: EditorGUILayout.PropertyField(elem.FindPropertyRelative("gameObjectValue"), new GUIContent("Value (GameObject)")); if (prefProp != null && prefProp.boolValue) EditorGUILayout.HelpBox("GameObject 값은 PlayerPrefs에 저장되지 않습니다.", MessageType.Info); break; }
            if (setOnlyValue && setTypeProp != null && setTypeProp.enumValueIndex == (int)EventSO.SetType.Add)
                EditorGUILayout.HelpBox("String/GameObject의 기존 Add 값은 호환성을 위해 Set으로 실행됩니다. 저장된 값은 자동 변경하지 않습니다.", MessageType.Info);
            EditorGUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(90))) { arrProp.DeleteArrayElementAtIndex(i); EditorGUILayout.EndHorizontal(); EditorGUI.indentLevel--; EditorGUILayout.EndVertical(); break; }
            EditorGUILayout.EndHorizontal(); EditorGUI.indentLevel--; EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawFlagAndData(SerializedProperty flagsProp, SerializedProperty stepProp, string flagName, string dataName)
    {
        var flag = flagsProp.FindPropertyRelative(flagName); if (flag == null || !flag.boolValue) return;
        string dataKey = stepProp.propertyPath + "." + dataName; if (drawnDataKeys.Contains(dataKey)) return; drawnDataKeys.Add(dataKey);
        var dataProp = stepProp.FindPropertyRelative(dataName); if (dataProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], dataName, true);
        if (!masterFoldouts[foldKey]) return;
        EditorGUILayout.BeginVertical("box");
        var iterator = dataProp.Copy(); var end = iterator.GetEndProperty();
        if (iterator.NextVisible(true)) { while (!SerializedProperty.EqualContents(iterator, end)) { EditorGUILayout.PropertyField(iterator, true); if (!iterator.NextVisible(false)) break; } }
        EditorGUILayout.EndVertical();
    }

    private void DrawConditionsArrayTop(SerializedProperty condsArrayProp, string keySuffix = null)
    {
        if (condsArrayProp == null) return;
        string key = BuildKey(string.IsNullOrEmpty(keySuffix) ? "ConditionsArray" : $"ConditionsArray_{keySuffix}");
        if (!masterFoldouts.TryGetValue(key, out _)) masterFoldouts[key] = true;
        masterFoldouts[key] = EditorGUILayout.Foldout(masterFoldouts[key], $"Condition Branches ({condsArrayProp.arraySize})", true);
        if (!masterFoldouts[key]) return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Add Condition", GUILayout.Width(120)))
        {
            int index = condsArrayProp.arraySize;
            condsArrayProp.arraySize++;
            SerializedProperty added = condsArrayProp.GetArrayElementAtIndex(index);
            SerializedProperty checks = added?.FindPropertyRelative("Checks");
            SerializedProperty seconds = added?.FindPropertyRelative("TimeCountSeconds");
            SerializedProperty values = added?.FindPropertyRelative("Conditions");
            if (checks != null) checks.intValue = (int)EventSO.ConditionChecks.CheckValue;
            if (seconds != null) seconds.floatValue = 0f;
            if (values != null) values.arraySize = 0;
        }
        EditorGUILayout.EndHorizontal();

        if (condsArrayProp.arraySize == 0)
            EditorGUILayout.HelpBox("Condition이 없습니다. 자동 실행하려면 Condition을 추가하세요.", MessageType.Warning);

        for (int i = 0; i < condsArrayProp.arraySize; i++)
        {
            SerializedProperty elem = condsArrayProp.GetArrayElementAtIndex(i);
            if (elem == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Condition {i + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                condsArrayProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();
            DrawConditionGroupElement(elem, elem.propertyPath);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
    }

    private void DrawBrokenNextPhaseRepair(PhaseNode lastNode)
    {
        SerializedProperty flags = lastNode.StepProp?.FindPropertyRelative("Flags");
        SerializedProperty isEventExe = flags?.FindPropertyRelative("IsEventExe");
        if (isEventExe == null || !isEventExe.boolValue) return;

        SerializedProperty eventExe = lastNode.StepProp.FindPropertyRelative("EventExe");
        SerializedProperty conditionSteps = eventExe?.FindPropertyRelative("ConditionSteps");
        bool needsRepair = conditionSteps == null || conditionSteps.arraySize == 0;
        if (!needsRepair && conditionSteps != null)
        {
            SerializedProperty next = conditionSteps.GetArrayElementAtIndex(0);
            needsRepair = next != null &&
                          next.propertyType == SerializedPropertyType.ManagedReference &&
                          next.managedReferenceValue == null;
        }

        if (!needsRepair) return;

        EditorGUILayout.HelpBox("Next Phase가 활성화되어 있지만 Phase 데이터가 비어 있습니다. 표시만으로는 데이터를 변경하지 않습니다.", MessageType.Warning);
        if (conditionSteps != null && GUILayout.Button("Repair Next Phase"))
        {
            if (conditionSteps.arraySize < 1) conditionSteps.arraySize = 1;
            EnsureEventStepElement(conditionSteps, 0);
            conditionSteps.serializedObject.ApplyModifiedProperties();
        }
    }

    private void DrawConditionGroupElement(SerializedProperty condGroupProp, string label)
    {
        if (condGroupProp == null) return;
        string key = BuildKey(condGroupProp.propertyPath);
        if (!masterFoldouts.TryGetValue(key, out _)) masterFoldouts[key] = true;
        masterFoldouts[key] = EditorGUILayout.Foldout(masterFoldouts[key], "Condition Settings", true);
        if (!masterFoldouts[key]) return;

        SerializedProperty checksProp = condGroupProp.FindPropertyRelative("Checks");
        SerializedProperty secondsProp = condGroupProp.FindPropertyRelative("TimeCountSeconds");
        SerializedProperty collisionAProp = condGroupProp.FindPropertyRelative("CollisionObjectA");
        SerializedProperty collisionBProp = condGroupProp.FindPropertyRelative("CollisionObjectB");
        var condsProp = condGroupProp.FindPropertyRelative("Conditions");
        if (checksProp == null || condsProp == null)
        {
            EditorGUILayout.HelpBox("Condition 데이터를 읽을 수 없습니다.", MessageType.Error);
            return;
        }

        int checksMask = checksProp.intValue;
        int newMask = EditorGUILayout.MaskField(
            "Condition Checks",
            checksMask,
            new[] { "Check Value", "Time Count", "A ↔ B Collision" });
        if (newMask != checksMask)
        {
            checksProp.intValue = newMask;
            checksProp.serializedObject.ApplyModifiedProperties();
            Repaint();
            GUIUtility.ExitGUI();
            return;
        }

        bool checkValue = (newMask & (int)EventSO.ConditionChecks.CheckValue) != 0;
        bool timeCount = (newMask & (int)EventSO.ConditionChecks.TimeCount) != 0;
        bool collisionAB = (newMask & (int)EventSO.ConditionChecks.CollisionAB) != 0;
        if (!checkValue && !timeCount && !collisionAB)
            EditorGUILayout.HelpBox(
                "Nothing: 별도 조건을 검사하지 않고 이 Condition의 Event Step을 즉시 실행합니다.",
                MessageType.Info);

        if (checkValue)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("CHECK VALUE", EditorStyles.miniBoldLabel);
            DrawConditionsArrayInline(condsProp, condGroupProp.propertyPath + ".Conditions");
        }

        if (timeCount && secondsProp != null)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("TIME COUNT", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(secondsProp, new GUIContent("Seconds After Game Start"));
            if (secondsProp.floatValue < 0f)
                EditorGUILayout.HelpBox("시간은 0초 이상이어야 합니다. 런타임에서는 음수를 0초로 처리합니다.", MessageType.Warning);
        }

        if (collisionAB)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("A ↔ B COLLISION", EditorStyles.miniBoldLabel);
            if (collisionAProp != null)
                EditorGUILayout.PropertyField(collisionAProp, new GUIContent("Object A (Name / Path)"));
            if (collisionBProp != null)
                EditorGUILayout.PropertyField(collisionBProp, new GUIContent("Object B (Name / Path)"));
            EditorGUILayout.HelpBox(
                "3D/2D의 Collision 또는 Trigger에서 A와 B가 접촉하면 참이 됩니다. " +
                "짧게 접촉해도 이번 Play의 해당 오브젝트 생명주기 동안 기억합니다.",
                MessageType.Info);

            string objectA = collisionAProp?.stringValue?.Trim() ?? string.Empty;
            string objectB = collisionBProp?.stringValue?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(objectA) || string.IsNullOrEmpty(objectB))
                EditorGUILayout.HelpBox("Object A와 Object B를 모두 입력해야 합니다.", MessageType.Warning);
            else if (string.Equals(objectA, objectB, StringComparison.Ordinal))
                EditorGUILayout.HelpBox("Object A와 Object B는 서로 다른 오브젝트여야 합니다.", MessageType.Warning);
        }

        int selectedCheckCount = (checkValue ? 1 : 0) + (timeCount ? 1 : 0) + (collisionAB ? 1 : 0);
        if (selectedCheckCount > 1)
            EditorGUILayout.HelpBox("선택한 조건을 모두 만족해야 이 Condition이 실행됩니다. (AND)", MessageType.Info);
    }

    private void DrawConditionsArrayInline(SerializedProperty condsProp, string baseKey)
    {
        if (condsProp == null) return;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Values ({condsProp.arraySize})", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Add Value", GUILayout.Width(90))) condsProp.arraySize++;
        EditorGUILayout.EndHorizontal();

        if (condsProp.arraySize == 0)
            EditorGUILayout.HelpBox("Check Value가 선택됐지만 비교할 값이 없습니다.", MessageType.Warning);

        for (int ci = 0; ci < condsProp.arraySize; ci++)
        {
            var condElem = condsProp.GetArrayElementAtIndex(ci); if (condElem == null) continue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Value {ci + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                condsProp.DeleteArrayElementAtIndex(ci);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();
            var nameProp = condElem.FindPropertyRelative("GlobalNames"); var valueTypeProp = condElem.FindPropertyRelative("valueType"); var prefProp = condElem.FindPropertyRelative("isPlayerPrefData"); var checkTypeProp = condElem.FindPropertyRelative("checkType");
            if (nameProp != null) EditorGUILayout.PropertyField(nameProp, new GUIContent("Global Key"));
            if (checkTypeProp != null) EditorGUILayout.PropertyField(checkTypeProp, new GUIContent("Check Type"));
            int ct = checkTypeProp != null ? checkTypeProp.enumValueIndex : 0;
            if (ct != 0) { EditorGUI.BeginDisabledGroup(true); EditorGUILayout.Popup("Value Type", 0, new[] { "Int" }); EditorGUI.EndDisabledGroup(); EditorGUILayout.HelpBox("Odd/Even은 Int 전용이며 기대값 입력이 필요 없습니다.", MessageType.Info); }
            else { if (valueTypeProp != null) EditorGUILayout.PropertyField(valueTypeProp, new GUIContent("Value Type")); int vt = valueTypeProp != null ? valueTypeProp.enumValueIndex : 1; switch (vt) { case 0: var ep = condElem.FindPropertyRelative("ExpectedInt"); if (ep != null) EditorGUILayout.PropertyField(ep, new GUIContent("Expected Int")); break; case 1: var fp = condElem.FindPropertyRelative("ExpectedFloat"); if (fp != null) EditorGUILayout.PropertyField(fp, new GUIContent("Expected Float")); break; case 2: var bp = condElem.FindPropertyRelative("ExpectedBool"); if (bp != null) EditorGUILayout.PropertyField(bp, new GUIContent("Expected Bool")); break; case 3: var sp = condElem.FindPropertyRelative("ExpectedString"); if (sp != null) EditorGUILayout.PropertyField(sp, new GUIContent("Expected String")); break; case 4: var gp = condElem.FindPropertyRelative("ExpectedGameObject"); if (gp != null) EditorGUILayout.PropertyField(gp, new GUIContent("Expected GameObject")); break; } }
            bool isGameObjectCondition = valueTypeProp != null && valueTypeProp.enumValueIndex == (int)EventSO.ValueType.GameObject;
            if (prefProp != null)
            {
                EditorGUI.BeginDisabledGroup(isGameObjectCondition);
                EditorGUILayout.PropertyField(prefProp, new GUIContent("Use PlayerPrefs"));
                EditorGUI.EndDisabledGroup();
                if (isGameObjectCondition && prefProp.boolValue)
                    EditorGUILayout.HelpBox("GameObject 조건은 PlayerPrefs에 저장할 수 없어 메모리 Global 값을 사용합니다. 기존 값은 자동 변경하지 않습니다.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
    }

    // ??????????????????????????????????????????
    //  Copy/Paste/Duplicate/Drag 헬퍼
    // ??????????????????????????????????????????
    private static EventSO.EventStep GetEventStep(EventSO asset, SerializedProperty arrayProp, int index)
    {
        SerializedProperty element = EnsureEventStepElement(arrayProp, index);
        if (element == null) return null;
        if (element.propertyType == SerializedPropertyType.ManagedReference)
            return element.managedReferenceValue as EventSO.EventStep;

        if (asset != null && string.Equals(arrayProp.propertyPath, "ConditionSteps", StringComparison.Ordinal))
        {
            EventSO.EventStep[] values = asset.ConditionSteps;
            if (values != null && index >= 0 && index < values.Length)
            {
                if (values[index] == null) values[index] = new EventSO.EventStep();
                return values[index];
            }
        }
        return null;
    }

    private void CopyElseStep(EventSO asset, SerializedProperty arrayProp, int index)
    {
        try
        {
            arrayProp.serializedObject.ApplyModifiedProperties();
            EventSO.EventStep step = GetEventStep(asset, arrayProp, index);
            if (step == null) return;
            stepClipboardJson = EditorJsonUtility.ToJson(step, true);
            stepClipboardLabel = $"[{asset.name}] {arrayProp.propertyPath} Step {index}";
        }
        catch { stepClipboardJson = null; stepClipboardLabel = ""; }
    }

    private void PasteElseStep(EventSO asset, SerializedProperty arrayProp, int index)
    {
        if (string.IsNullOrEmpty(stepClipboardJson)) return;
        Undo.RecordObject(asset, $"Paste {arrayProp.name}[{index}]");
        try
        {
            var clone = new EventSO.EventStep();
            EditorJsonUtility.FromJsonOverwrite(stepClipboardJson, clone);
            SerializedProperty element = EnsureEventStepElement(arrayProp, index);
            if (element == null) return;

            if (element.propertyType == SerializedPropertyType.ManagedReference)
            {
                element.managedReferenceValue = clone;
                arrayProp.serializedObject.ApplyModifiedProperties();
            }
            else if (string.Equals(arrayProp.propertyPath, "ConditionSteps", StringComparison.Ordinal) &&
                     asset.ConditionSteps != null && index >= 0 && index < asset.ConditionSteps.Length)
            {
                arrayProp.serializedObject.ApplyModifiedProperties();
                asset.ConditionSteps[index] = clone;
                arrayProp.serializedObject.Update();
            }
            EditorUtility.SetDirty(asset);
        }
        catch { }
    }

    private void DuplicateElseStep(EventSO asset, SerializedProperty arrayProp, int index)
    {
        try
        {
            arrayProp.serializedObject.ApplyModifiedProperties();
            EventSO.EventStep source = GetEventStep(asset, arrayProp, index);
            if (source == null) return;
            string json = EditorJsonUtility.ToJson(source, true);
            var clone = new EventSO.EventStep();
            EditorJsonUtility.FromJsonOverwrite(json, clone);
            Undo.RecordObject(asset, $"Duplicate {arrayProp.name}[{index}]");

            SerializedProperty sourceElement = EnsureEventStepElement(arrayProp, index);
            if (sourceElement != null && sourceElement.propertyType == SerializedPropertyType.ManagedReference)
            {
                arrayProp.InsertArrayElementAtIndex(index + 1);
                SerializedProperty destination = arrayProp.GetArrayElementAtIndex(index + 1);
                destination.managedReferenceValue = clone;
                arrayProp.serializedObject.ApplyModifiedProperties();
            }
            else if (string.Equals(arrayProp.propertyPath, "ConditionSteps", StringComparison.Ordinal))
            {
                EventSO.EventStep[] sourceArray = asset.ConditionSteps ?? Array.Empty<EventSO.EventStep>();
                var newArray = new EventSO.EventStep[sourceArray.Length + 1];
                Array.Copy(sourceArray, 0, newArray, 0, index + 1);
                newArray[index + 1] = clone;
                Array.Copy(sourceArray, index + 1, newArray, index + 2, sourceArray.Length - index - 1);
                asset.ConditionSteps = newArray;
                arrayProp.serializedObject.Update();
            }
            EditorUtility.SetDirty(asset);
        }
        catch { }
    }

    private void HandleReorderDrag(SerializedProperty arrayProp, string key)
    {
        if (!dragInProgress || dragActiveKey != key) return;
        if (!itemHeaderRects.TryGetValue(key, out var rects) || rects == null || rects.Count == 0) return;
        Vector2 mouse = Event.current.mousePosition;
        int target = rects.Count - 1;
        for (int i = 0; i < rects.Count; i++) { if (mouse.y < rects[i].center.y) { target = i; break; } }
        dragToIndex = Mathf.Clamp(target, 0, arrayProp.arraySize - 1);
        if (Event.current.type == EventType.MouseUp)
        {
            if (dragFromIndex >= 0 && dragToIndex >= 0 && dragFromIndex != dragToIndex) { Undo.RecordObject(arrayProp.serializedObject.targetObject, "Reorder Steps"); arrayProp.MoveArrayElement(dragFromIndex, dragToIndex); arrayProp.serializedObject.ApplyModifiedProperties(); EditorUtility.SetDirty(arrayProp.serializedObject.targetObject); }
            dragInProgress = false; dragActiveKey = null; dragFromIndex = -1; dragToIndex = -1; Event.current.Use();
        }
        else { Rect r = rects[dragToIndex]; EditorGUI.DrawRect(new Rect(r.x, r.y - 1, r.width, 2), new Color(0f, 0.7f, 1f, 0.9f)); }
    }
}
