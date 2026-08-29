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
    private static string phaseClipboardJson = null;

    private static string dragActiveKey = null;
    private static int dragFromIndex = -1;
    private static int dragToIndex = -1;
    private static bool dragInProgress = false;
    private static int dragControlId = 0;
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
        if (selected == 1)
        {
            EditorGUILayout.HelpBox(
                "자동 Condition EventSO의 Caller는 Presentation 자동 감시 오브젝트입니다. " +
                "Object/Scene 항목에 'Empty = Caller'가 표시된 기능은 자동 실행에서 대상 이름이나 Root/Child 경로를 직접 입력해 주세요.",
                MessageType.Warning);
        }
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
            "IsTooltip",
            "IsBlackLabel",
            "IsTimeScale",
            "IsScreenFlash",
            "IsWaitUntilCondition"
        };
        string[] topFlagLabels = {
            "Set Globals",
            "Objects (Spawn/Destroy/Enable/Disable/Rename)",
            "Components (Enable/Disable/Animator/Fade/Light/Particle)",
            "Transforms (Move/Rotate/Attach)",
            "Wait","Lock",
            "Speeches (Soft/Hard/Portrait)",
            "Scenes (Active/Change/Add/Off/Pause/UnPause)",
            "Sound","Fade Play",
            "Cameras (Move/Aim/Shake/Lens)",
            "Cursor","Choice","Action",
            "Tooltip",
            "Black Label",
            "Time Scale",
            "Screen Flash",
            "Wait Until Condition"
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
        DrawSoundCustom(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsFadePlay", "FadeInfo");
        DrawCamerasGroup(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsCursorVisible", "Cursor");
        DrawChoiceCustom(flagsProp, stepProp, "IsChoice", "Choice");
        DrawFlagAndData(flagsProp, stepProp, "IsAction", "Action");
        DrawTooltipCustom(flagsProp, stepProp);
        DrawFlagAndData(flagsProp, stepProp, "IsBlackLabel", "BlackLabel");
        DrawTimeScaleCustom(flagsProp, stepProp);
        DrawScreenFlashCustom(flagsProp, stepProp);
        DrawWaitUntilConditionCustom(flagsProp, stepProp);
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

    private void DrawSoundCustom(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        SerializedProperty flag = flagsProp.FindPropertyRelative("IsSound");
        if (flag == null || !flag.boolValue) return;

        const string dataName = "Sound";
        string dataKey = stepProp.propertyPath + "." + dataName;
        if (drawnDataKeys.Contains(dataKey)) return;
        drawnDataKeys.Add(dataKey);

        SerializedProperty dataProp = stepProp.FindPropertyRelative(dataName);
        if (dataProp == null) return;

        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], dataName, true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty delayProp = dataProp.FindPropertyRelative("time");
        SerializedProperty audioClipProp = dataProp.FindPropertyRelative("audioClip");
        SerializedProperty volumeProp = dataProp.FindPropertyRelative("volume");
        SerializedProperty loopProp = dataProp.FindPropertyRelative("isLoop");
        SerializedProperty waitProp = dataProp.FindPropertyRelative("WaitForCompletion");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SOUND", EditorStyles.boldLabel);
        if (delayProp != null)
            EditorGUILayout.PropertyField(delayProp, new GUIContent("Start Delay (Scaled Seconds)"));
        if (audioClipProp != null)
            EditorGUILayout.PropertyField(audioClipProp, new GUIContent("Audio Clip", "비우면 현재 Event AudioSource를 정지합니다."));
        if (volumeProp != null)
        {
            EditorGUILayout.PropertyField(volumeProp, new GUIContent("Volume"));
            if (volumeProp.floatValue < 0f || volumeProp.floatValue > 1f)
                EditorGUILayout.HelpBox("Volume은 런타임에서 0~1 범위로 제한됩니다.", MessageType.Info);
        }
        if (loopProp != null)
            EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop"));
        if (waitProp != null)
            EditorGUILayout.PropertyField(waitProp, new GUIContent("Wait For Completion"));

        bool isLoop = loopProp != null && loopProp.boolValue;
        bool waitsForCompletion = waitProp != null && waitProp.boolValue;
        if (isLoop && waitsForCompletion)
            EditorGUILayout.HelpBox("Loop Sound에는 끝이 없어 Wait For Completion을 적용하지 않습니다.", MessageType.Warning);
        else if (waitsForCompletion)
            EditorGUILayout.HelpBox("Start Delay와 비루프 클립 재생이 끝날 때까지 다음 Phase로 넘어가지 않습니다.", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Start Delay 후 재생을 시작하면 다음 Phase로 진행합니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void DrawTimeScaleCustom(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        SerializedProperty flag = flagsProp.FindPropertyRelative("IsTimeScale");
        if (flag == null || !flag.boolValue) return;

        const string dataName = "TimeScale";
        string dataKey = stepProp.propertyPath + "." + dataName;
        if (drawnDataKeys.Contains(dataKey)) return;
        drawnDataKeys.Add(dataKey);

        SerializedProperty dataProp = stepProp.FindPropertyRelative(dataName);
        if (dataProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Time Scale", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty targetScaleProp = dataProp.FindPropertyRelative("TargetScale");
        SerializedProperty durationProp = dataProp.FindPropertyRelative("Duration");
        SerializedProperty restoreProp = dataProp.FindPropertyRelative("RestoreAfterDuration");
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("TIME SCALE", EditorStyles.boldLabel);
        if (targetScaleProp != null)
            EditorGUILayout.PropertyField(targetScaleProp, new GUIContent("Target Scale"));
        if (durationProp != null)
            EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (Unscaled Seconds)"));
        if (restoreProp != null)
            EditorGUILayout.PropertyField(restoreProp, new GUIContent("Restore After Duration"));

        bool restores = restoreProp == null || restoreProp.boolValue;
        float targetScale = targetScaleProp != null ? targetScaleProp.floatValue : 1f;
        if (Mathf.Approximately(targetScale, 0f) && !restores)
            EditorGUILayout.HelpBox("Target Scale 0을 복구하지 않으면 게임의 scaled time이 계속 멈춥니다.", MessageType.Warning);
        else if (restores && durationProp != null && durationProp.floatValue <= 0f)
            EditorGUILayout.HelpBox("Duration이 0이고 복구가 켜져 있어 Time Scale 변화가 같은 프레임에 끝납니다.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox(
                restores
                    ? "즉시 Time Scale을 적용하고, unscaled Duration 뒤 이벤트 시작 전 값으로 복구합니다."
                    : "즉시 Time Scale을 적용하고, unscaled Duration 뒤에도 설정값을 유지합니다.",
                MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawScreenFlashCustom(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        SerializedProperty flag = flagsProp.FindPropertyRelative("IsScreenFlash");
        if (flag == null || !flag.boolValue) return;

        const string dataName = "ScreenFlash";
        string dataKey = stepProp.propertyPath + "." + dataName;
        if (drawnDataKeys.Contains(dataKey)) return;
        drawnDataKeys.Add(dataKey);

        SerializedProperty dataProp = stepProp.FindPropertyRelative(dataName);
        if (dataProp == null) return;
        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Screen Flash", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty colorProp = dataProp.FindPropertyRelative("FlashColor");
        SerializedProperty peakAlphaProp = dataProp.FindPropertyRelative("PeakAlpha");
        SerializedProperty fadeInProp = dataProp.FindPropertyRelative("FadeInDuration");
        SerializedProperty holdProp = dataProp.FindPropertyRelative("Duration");
        SerializedProperty fadeOutProp = dataProp.FindPropertyRelative("FadeOutDuration");
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SCREEN FLASH", EditorStyles.boldLabel);
        if (colorProp != null) EditorGUILayout.PropertyField(colorProp, new GUIContent("Flash Color"));
        if (peakAlphaProp != null) EditorGUILayout.PropertyField(peakAlphaProp, new GUIContent("Peak Alpha"));
        if (fadeInProp != null) EditorGUILayout.PropertyField(fadeInProp, new GUIContent("Fade In"));
        if (holdProp != null) EditorGUILayout.PropertyField(holdProp, new GUIContent("Hold Duration"));
        if (fadeOutProp != null) EditorGUILayout.PropertyField(fadeOutProp, new GUIContent("Fade Out"));

        float totalDuration =
            Mathf.Max(0f, fadeInProp != null ? fadeInProp.floatValue : 0f) +
            Mathf.Max(0f, holdProp != null ? holdProp.floatValue : 0f) +
            Mathf.Max(0f, fadeOutProp != null ? fadeOutProp.floatValue : 0f);
        EditorGUILayout.LabelField("Total Phase Block", $"{totalDuration:0.###} unscaled seconds");
        if (colorProp != null && colorProp.colorValue.a <= 0f)
            EditorGUILayout.HelpBox("Flash Color의 Alpha가 0이면 화면에 보이지 않지만 설정한 시간만큼 Phase는 유지됩니다.", MessageType.Warning);
        else if (peakAlphaProp != null && peakAlphaProp.floatValue <= 0f)
            EditorGUILayout.HelpBox("Peak Alpha가 0이면 화면에 보이지 않지만 설정한 시간만큼 Phase는 유지됩니다.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Fade In → 최대 알파 유지 → Fade Out 전체가 끝날 때까지 다음 Phase로 진행하지 않습니다.", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawWaitUntilConditionCustom(SerializedProperty flagsProp, SerializedProperty stepProp)
    {
        SerializedProperty flag = flagsProp.FindPropertyRelative("IsWaitUntilCondition");
        if (flag == null || !flag.boolValue) return;

        const string dataName = "WaitUntilCondition";
        string dataKey = stepProp.propertyPath + "." + dataName;
        if (drawnDataKeys.Contains(dataKey)) return;
        drawnDataKeys.Add(dataKey);

        SerializedProperty dataProp = stepProp.FindPropertyRelative(dataName);
        if (dataProp == null) return;

        string foldKey = BuildKey(stepProp.propertyPath, "." + dataName);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Wait Until Condition", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty conditionProp = dataProp.FindPropertyRelative("Condition");
        SerializedProperty timeoutProp = dataProp.FindPropertyRelative("Timeout");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("WAIT UNTIL CONDITION", EditorStyles.boldLabel);
        if (timeoutProp != null)
            EditorGUILayout.PropertyField(timeoutProp, new GUIContent("Timeout (Unscaled Seconds)"));
        EditorGUILayout.HelpBox(
            timeoutProp == null || timeoutProp.floatValue <= 0f
                ? "조건이 충족될 때까지 현재 Phase를 제한 없이 유지합니다."
                : "조건이 먼저 충족되면 즉시 진행하고, Timeout에 도달하면 경고 후 다음 Phase로 진행합니다.",
            timeoutProp == null || timeoutProp.floatValue <= 0f ? MessageType.Warning : MessageType.Info);

        if (conditionProp != null)
            DrawConditionGroupElement(conditionProp, conditionProp.propertyPath);
        else
            EditorGUILayout.HelpBox("Condition 데이터를 찾을 수 없습니다.", MessageType.Error);
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
                DrawPhaseList(stepProp, keySuffix, null, stepsArrayProp, 0);
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

        int conditionCount = conditionsArrayProp != null ? conditionsArrayProp.arraySize : 0;
        int maxStepCount = conditionCount + 1;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Event Steps", EditorStyles.boldLabel, GUILayout.Width(90));
        int oldSize = stepsArrayProp.arraySize;
        int newSize = EditorGUILayout.IntField(oldSize, GUILayout.Width(60));
        if (newSize != oldSize)
        {
            int requestedSize = Mathf.Max(0, newSize);
            int limitedSize = requestedSize > oldSize
                ? (oldSize >= maxStepCount ? oldSize : Mathf.Min(requestedSize, maxStepCount))
                : requestedSize;
            stepsArrayProp.arraySize = limitedSize;
            for (int i = oldSize; i < stepsArrayProp.arraySize; i++)
                EnsureEventStepElement(stepsArrayProp, i);
        }
        bool canAddStep = stepsArrayProp.arraySize < maxStepCount;
        EditorGUI.BeginDisabledGroup(!canAddStep);
        if (GUILayout.Button("+", GUILayout.Width(22)))
        {
            int index = stepsArrayProp.arraySize++;
            EnsureEventStepElement(stepsArrayProp, index);
        }
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button("-", GUILayout.Width(22))) { if (stepsArrayProp.arraySize > 0) stepsArrayProp.arraySize--; }
        GUILayout.Space(8);
        EditorGUI.BeginDisabledGroup(!canAddStep);
        if (GUILayout.Button("Add Event Step", GUILayout.Width(110)))
        {
            int index = stepsArrayProp.arraySize++;
            EnsureEventStepElement(stepsArrayProp, index);
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (stepsArrayProp.arraySize == maxStepCount)
            EditorGUILayout.HelpBox($"Condition {conditionCount}개: Event Step은 최대 {maxStepCount}개까지 만들 수 있으며, 마지막은 Else Event입니다.", MessageType.Info);
        else if (stepsArrayProp.arraySize > maxStepCount)
            EditorGUILayout.HelpBox($"Condition {conditionCount}개에는 Event Step이 최대 {maxStepCount}개만 필요합니다. 기존 초과 데이터는 자동 삭제하지 않으므로 X 버튼으로 정리해 주세요.", MessageType.Warning);

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
            GUI.enabled = canUseElemClipboard && stepsArrayProp.arraySize < maxStepCount;
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
                DrawPhaseList(stepProp, $"{keySuffix}_Step{i}", null, stepsArrayProp, i);
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
    private void DrawPhaseList(
        SerializedProperty firstStepProp,
        string keySuffix,
        SerializedProperty topUseCondToggleProp,
        SerializedProperty firstContainerArrayProp,
        int firstContainerIndex)
    {
        var steps = new List<PhaseNode>();
        CollectPhaseChain(firstStepProp, firstContainerArrayProp, firstContainerIndex, steps);

        string phaseDragKey = BuildKey(
            firstStepProp.propertyPath,
            $".PhaseDrag_{keySuffix ?? string.Empty}");
        if (!itemHeaderRects.TryGetValue(phaseDragKey, out _))
            itemHeaderRects[phaseDragKey] = new List<Rect>();
        itemHeaderRects[phaseDragKey].Clear();
        int reorderablePhaseCount = GetReorderablePhaseCount(steps);
        bool canReorderPhases = !serializedObject.isEditingMultipleObjects &&
                                reorderablePhaseCount > 1;

        for (int pi = 0; pi < steps.Count; pi++)
        {
            var node = steps[pi];
            bool isFirst = (pi == 0);

            if (pi > 0) EditorGUILayout.Space(4);

            string phaseKey = BuildKey(node.StepProp.propertyPath, $".PhaseList{pi}_{keySuffix}");
            if (!masterFoldouts.TryGetValue(phaseKey, out _)) masterFoldouts[phaseKey] = true;

            EditorGUILayout.BeginVertical("box");
            Rect headerRect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight);
            const float handleWidth = 18f;
            const float buttonWidth = 22f;
            const float headerPadding = 4f;
            Rect handleRect = new Rect(headerRect.x, headerRect.y, handleWidth, headerRect.height);
            float removeWidth = !isFirst ? buttonWidth : 0f;
            Rect labelRect = new Rect(
                handleRect.xMax + headerPadding,
                headerRect.y,
                Mathf.Max(0f, headerRect.width - handleWidth - headerPadding - removeWidth),
                headerRect.height);
            Rect removeRect = new Rect(
                headerRect.xMax - buttonWidth,
                headerRect.y,
                buttonWidth,
                headerRect.height);

            bool canDragThisPhase = canReorderPhases && pi < reorderablePhaseCount;
            int phaseDragControlId = GUIUtility.GetControlID(FocusType.Passive, handleRect);
            GUI.Label(handleRect, canDragThisPhase ? "=" : "·");
            if (canDragThisPhase)
            {
                EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    handleRect.Contains(Event.current.mousePosition))
                {
                    GUIUtility.hotControl = phaseDragControlId;
                    dragInProgress = true;
                    dragActiveKey = phaseDragKey;
                    dragFromIndex = pi;
                    dragToIndex = pi;
                    dragControlId = phaseDragControlId;
                    Event.current.Use();
                }
            }

            masterFoldouts[phaseKey] = EditorGUI.Foldout(
                labelRect,
                masterFoldouts[phaseKey],
                $"Phase {pi + 1}",
                true);
            itemHeaderRects[phaseDragKey].Add(headerRect);

            // X 버튼 (Phase 1 제외)
            if (!isFirst && node.IsEventExeProp != null)
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f, 1f);
                if (GUI.Button(removeRect, "X"))
                {
                    node.IsEventExeProp.boolValue = false;
                    node.IsEventExeProp.serializedObject.ApplyModifiedProperties();
                    GUI.backgroundColor = prevBg;
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = prevBg;
            }

            bool isConditionRouter = !isFirst && node.UseCondProp != null && node.UseCondProp.boolValue;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var assetInstance = node.StepProp.serializedObject.targetObject as EventSO;
            bool canUsePhaseClipboard = assetInstance != null &&
                                        node.ContainerArrayProp != null &&
                                        !isConditionRouter;
            EditorGUI.BeginDisabledGroup(!canUsePhaseClipboard);
            if (GUILayout.Button(
                    new GUIContent("Copy", "현재 Phase의 연출만 복사합니다. Next Phase 연결은 포함하지 않습니다."),
                    GUILayout.Width(60)))
            {
                CopyPhase(assetInstance, node.ContainerArrayProp, node.ContainerIndex);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!canUsePhaseClipboard || string.IsNullOrEmpty(phaseClipboardJson));
            if (GUILayout.Button(
                    new GUIContent("Paste", "현재 Phase의 연출만 교체하며, 이 위치의 Next Phase 연결은 그대로 보존합니다."),
                    GUILayout.Width(60)))
            {
                if (PastePhase(assetInstance, node.ContainerArrayProp, node.ContainerIndex))
                    GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (isConditionRouter)
                EditorGUILayout.LabelField("Condition Phase는 아래 각 Event Step의 Phase에서 복사하거나 붙여넣으세요.", EditorStyles.wordWrappedMiniLabel);

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

        HandlePhaseReorderDrag(steps, phaseDragKey, reorderablePhaseCount);
        if (reorderablePhaseCount < steps.Count)
            EditorGUILayout.HelpBox(
                "Condition 분기 카드는 분기 구조 보호를 위해 드래그 대상에서 제외됩니다. " +
                "그 앞의 일반 Phase와 분기 안 각 Event Step의 Phase는 해당 목록에서 드래그할 수 있습니다.",
                MessageType.Info);
        if (serializedObject.isEditingMultipleObjects && reorderablePhaseCount > 1)
            EditorGUILayout.HelpBox(
                "Phase 순서 변경은 한 번에 하나의 EventSO를 선택했을 때 사용할 수 있습니다.",
                MessageType.Info);

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
        public SerializedProperty ContainerArrayProp;
        public int ContainerIndex;
    }

    private void CollectPhaseChain(
        SerializedProperty firstStep,
        SerializedProperty firstContainerArray,
        int firstContainerIndex,
        List<PhaseNode> result)
    {
        result.Clear();
        result.Add(new PhaseNode
        {
            StepProp = firstStep,
            ContainerArrayProp = firstContainerArray,
            ContainerIndex = firstContainerIndex,
        });

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
                ContainerArrayProp = condSteps,
                ContainerIndex = 0,
            });

            bool nextUseCond = useCond != null && useCond.boolValue;
            if (nextUseCond) break;

            current = nextStep;
        }
    }

    private static int GetReorderablePhaseCount(List<PhaseNode> steps)
    {
        if (steps == null || steps.Count == 0) return 0;
        PhaseNode last = steps[steps.Count - 1];
        SerializedProperty useCondition = last.UseCondProp;
        return useCondition != null && useCondition.boolValue
            ? steps.Count - 1
            : steps.Count;
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

        string[] subFlags = { "IsSoftSpeech", "IsHardSpeech", "IsPortraitSpeech" };
        string[] subLabels = { "Soft Speech", "Hard Speech", "Portrait Speech" };
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
        if (GetBoolProp(speechesProp, "IsPortraitSpeech"))
        {
            var p = speechesProp.FindPropertyRelative("PortraitSpeech");
            if (p != null) DrawPortraitSpeechDirect(p, stepProp.propertyPath + ".Speeches.PortraitSpeech");
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

        string[] subFlags = { "IsCameraMove", "IsCameraAiming", "IsCameraShake", "IsCameraLens" };
        string[] subLabels = { "Camera Move", "Camera Aim", "Screen Shake", "Camera Lens" };
        DrawSubFlagTogglesOnProp(camerasProp, subFlags, subLabels);

        if (GetBoolProp(camerasProp, "IsCameraMove")) { var p = camerasProp.FindPropertyRelative("CameraMovement"); if (p != null) DrawCameraMovementDirect(p, stepProp.propertyPath + ".Cameras.CameraMovement"); }
        if (GetBoolProp(camerasProp, "IsCameraAiming")) { var p = camerasProp.FindPropertyRelative("CameraAim"); if (p != null) DrawGenericDataDirect(p, "CameraAim", stepProp.propertyPath + ".Cameras.CameraAim"); }
        if (GetBoolProp(camerasProp, "IsCameraShake")) { var p = camerasProp.FindPropertyRelative("CameraShake"); if (p != null) DrawCameraShakeDirect(p, stepProp.propertyPath + ".Cameras.CameraShake"); }
        if (GetBoolProp(camerasProp, "IsCameraLens")) { var p = camerasProp.FindPropertyRelative("CameraLens"); if (p != null) DrawCameraLensDirect(p, stepProp.propertyPath + ".Cameras.CameraLens"); }

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

        string[] subFlags = { "IsEnableComponent", "IsDisableComponent", "IsDisableColliderObject", "IsUpdateComponent", "IsAnimatorEvent", "IsVisualFade", "IsLightTween", "IsParticleEvent" };
        string[] subLabels = { "Enable Component", "Disable Component", "Disable Collider", "Update Component", "Animator", "Visual Fade", "Light Tween", "Particle" };
        DrawSubFlagTogglesOnProp(compsProp, subFlags, subLabels);

        if (GetBoolProp(compsProp, "IsEnableComponent")) { var p = compsProp.FindPropertyRelative("EnableComponent"); if (p != null) DrawComponentDataDirect(p, "EnableComponent", "ComponentDatas", stepProp.propertyPath + ".Components.EnableComponent"); }
        if (GetBoolProp(compsProp, "IsDisableComponent")) { var p = compsProp.FindPropertyRelative("DisableComponent"); if (p != null) DrawComponentDataDirect(p, "DisableComponent", "ComponentDatas", stepProp.propertyPath + ".Components.DisableComponent"); }
        if (GetBoolProp(compsProp, "IsDisableColliderObject")) { var p = compsProp.FindPropertyRelative("DisableColliderObject"); if (p != null) DrawStringArrayDirect(p, "DisableColliderObjectNames", "DisableCollider", "Object Names (Empty = Caller)", stepProp.propertyPath + ".Components.DisableCollider"); }
        if (GetBoolProp(compsProp, "IsUpdateComponent")) { var p = compsProp.FindPropertyRelative("UpdateComponent"); if (p != null) DrawUpdateComponentDirect(p, stepProp.propertyPath + ".Components.UpdateComponent"); }
        if (GetBoolProp(compsProp, "IsAnimatorEvent")) { var p = compsProp.FindPropertyRelative("AnimatorEvent"); if (p != null) DrawAnimatorEventDirect(p, stepProp.propertyPath + ".Components.AnimatorEvent"); }
        if (GetBoolProp(compsProp, "IsVisualFade")) { var p = compsProp.FindPropertyRelative("VisualFade"); if (p != null) DrawVisualFadeDirect(p, stepProp.propertyPath + ".Components.VisualFade"); }
        if (GetBoolProp(compsProp, "IsLightTween")) { var p = compsProp.FindPropertyRelative("LightTween"); if (p != null) DrawLightTweenDirect(p, stepProp.propertyPath + ".Components.LightTween"); }
        if (GetBoolProp(compsProp, "IsParticleEvent")) { var p = compsProp.FindPropertyRelative("ParticleEvent"); if (p != null) DrawParticleEventDirect(p, stepProp.propertyPath + ".Components.ParticleEvent"); }

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

        string[] subFlags = { "IsMoveObject", "IsRotateObject", "IsAttachObject" };
        string[] subLabels = { "Move Object", "Rotate Object", "Attach Object" };
        DrawSubFlagTogglesOnProp(transformsProp, subFlags, subLabels);

        if (GetBoolProp(transformsProp, "IsMoveObject")) { var p = transformsProp.FindPropertyRelative("MoveObject"); if (p != null) DrawMoveObjectDirect(p, stepProp.propertyPath + ".Transforms.MoveObject"); }
        if (GetBoolProp(transformsProp, "IsRotateObject")) { var p = transformsProp.FindPropertyRelative("RotateObject"); if (p != null) DrawRotateObjectDirect(p, stepProp.propertyPath + ".Transforms.RotateObject"); }
        if (GetBoolProp(transformsProp, "IsAttachObject")) { var p = transformsProp.FindPropertyRelative("AttachObject"); if (p != null) DrawAttachObjectDirect(p, stepProp.propertyPath + ".Transforms.AttachObject"); }

        EditorGUILayout.EndVertical();
    }

    // ??????????????????????????????????????????
    //  헬퍼: 그룹 래퍼 프로퍼티에서 서브 플래그 토글
    // ??????????????????????????????????????????
    private void DrawSubFlagTogglesOnProp(SerializedProperty groupProp, string[] subFlagNames, string[] subLabels)
    {
        const int columns = 2;
        for (int i = 0; i < subFlagNames.Length; i++)
        {
            if (i % columns == 0) EditorGUILayout.BeginHorizontal();
            var p = groupProp.FindPropertyRelative(subFlagNames[i]);
            if (p != null)
                p.boolValue = GUILayout.Toggle(
                    p.boolValue,
                    subLabels[i],
                    "Button",
                    GUILayout.ExpandWidth(true));
            else
                GUILayout.Space(1f);
            if (i % columns == columns - 1 || i == subFlagNames.Length - 1)
                EditorGUILayout.EndHorizontal();
        }
    }

    private bool GetBoolProp(SerializedProperty parent, string name)
    {
        var p = parent.FindPropertyRelative(name);
        return p != null && p.boolValue;
    }

    private static void SetStringRelative(SerializedProperty parent, string name, string value)
    {
        SerializedProperty property = parent?.FindPropertyRelative(name);
        if (property != null) property.stringValue = value;
    }

    private static void SetIntRelative(SerializedProperty parent, string name, int value)
    {
        SerializedProperty property = parent?.FindPropertyRelative(name);
        if (property != null) property.intValue = value;
    }

    private static void SetFloatRelative(SerializedProperty parent, string name, float value)
    {
        SerializedProperty property = parent?.FindPropertyRelative(name);
        if (property != null) property.floatValue = value;
    }

    private static void SetBoolRelative(SerializedProperty parent, string name, bool value)
    {
        SerializedProperty property = parent?.FindPropertyRelative(name);
        if (property != null) property.boolValue = value;
    }

    private static void SetEnumRelative(SerializedProperty parent, string name, int enumIndex)
    {
        SerializedProperty property = parent?.FindPropertyRelative(name);
        if (property != null) property.enumValueIndex = enumIndex;
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

    private void DrawPortraitSpeechDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Portrait Speech", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty advanceModeProp = dataProp.FindPropertyRelative("AdvanceMode");
        SerializedProperty advanceKeyProp = dataProp.FindPropertyRelative("AdvanceKey");
        SerializedProperty isTypingProp = dataProp.FindPropertyRelative("IsTyping");
        SerializedProperty linesProp = dataProp.FindPropertyRelative("Lines");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("PORTRAIT SPEECH", EditorStyles.boldLabel);
        if (advanceModeProp != null)
            EditorGUILayout.PropertyField(advanceModeProp, new GUIContent("Advance Mode"));

        bool usesInput = advanceModeProp != null &&
                         advanceModeProp.enumValueIndex == (int)EventSO.PortraitAdvanceMode.Input;
        if (usesInput && advanceKeyProp != null)
        {
            EditorGUILayout.PropertyField(advanceKeyProp, new GUIContent("Advance Key"));
            if (advanceKeyProp.intValue == (int)KeyCode.None)
                EditorGUILayout.HelpBox("Advance Key가 None이면 런타임에서 E 키를 사용합니다.", MessageType.Info);
        }

        if (isTypingProp != null)
            EditorGUILayout.PropertyField(isTypingProp, new GUIContent("Is Typing"));

        EditorGUILayout.HelpBox(
            usesInput
                ? "각 Line은 Advance Key 입력까지 현재 Phase를 막습니다. 타이핑 중 첫 입력은 전체 문장을 표시하고, 다음 입력이 다음 Line으로 넘깁니다."
                : "각 Line은 해당 Duration 동안 표시되며, 모든 Line이 끝날 때까지 다음 Phase로 넘어가지 않습니다.",
            MessageType.Info);

        if (linesProp == null)
        {
            EditorGUILayout.HelpBox("Lines 데이터를 찾을 수 없습니다.", MessageType.Error);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Lines ({linesProp.arraySize})", EditorStyles.boldLabel);
        if (GUILayout.Button("Add Line", GUILayout.Width(80)))
        {
            int newIndex = linesProp.arraySize;
            linesProp.arraySize++;
            SerializedProperty newLine = linesProp.GetArrayElementAtIndex(newIndex);
            SerializedProperty portraitProp = newLine?.FindPropertyRelative("Portrait");
            SerializedProperty speakerProp = newLine?.FindPropertyRelative("SpeakerName");
            SerializedProperty textProp = newLine?.FindPropertyRelative("Text");
            SerializedProperty durationProp = newLine?.FindPropertyRelative("Duration");
            if (portraitProp != null) portraitProp.objectReferenceValue = null;
            if (speakerProp != null) speakerProp.stringValue = string.Empty;
            if (textProp != null) textProp.stringValue = string.Empty;
            if (durationProp != null) durationProp.floatValue = 2f;
        }
        EditorGUI.BeginDisabledGroup(linesProp.arraySize == 0);
        if (GUILayout.Button("-", GUILayout.Width(24)))
            linesProp.DeleteArrayElementAtIndex(linesProp.arraySize - 1);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (linesProp.arraySize == 0)
            EditorGUILayout.HelpBox("표시할 Line을 하나 이상 추가해 주세요.", MessageType.Warning);

        int removeIndex = -1;
        for (int i = 0; i < linesProp.arraySize; i++)
        {
            SerializedProperty lineProp = linesProp.GetArrayElementAtIndex(i);
            if (lineProp == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Line {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("X", GUILayout.Width(24))) removeIndex = i;
            EditorGUILayout.EndHorizontal();

            SerializedProperty portraitProp = lineProp.FindPropertyRelative("Portrait");
            SerializedProperty speakerProp = lineProp.FindPropertyRelative("SpeakerName");
            SerializedProperty textProp = lineProp.FindPropertyRelative("Text");
            SerializedProperty durationProp = lineProp.FindPropertyRelative("Duration");
            if (portraitProp != null)
                EditorGUILayout.PropertyField(portraitProp, new GUIContent("Portrait"));
            if (speakerProp != null)
                EditorGUILayout.PropertyField(speakerProp, new GUIContent("Speaker Name"));
            if (textProp != null)
                EditorGUILayout.PropertyField(textProp, new GUIContent("Text"));
            if (!usesInput && durationProp != null)
            {
                EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (Unscaled Seconds)"));
                if (durationProp.floatValue <= 0f)
                    EditorGUILayout.HelpBox("Duration이 0이면 이 Line은 같은 프레임에 다음 Line으로 넘어갑니다.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }
        if (removeIndex >= 0)
            linesProp.DeleteArrayElementAtIndex(removeIndex);

        EditorGUILayout.EndVertical();
    }

    private void DrawAnimatorEventDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Animator Event", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty objectNameProp = dataProp.FindPropertyRelative("ObjectName");
        SerializedProperty commandProp = dataProp.FindPropertyRelative("Command");
        SerializedProperty stateOrParameterProp = dataProp.FindPropertyRelative("StateOrParameter");
        SerializedProperty layerProp = dataProp.FindPropertyRelative("Layer");
        SerializedProperty normalizedTimeProp = dataProp.FindPropertyRelative("NormalizedTime");
        SerializedProperty transitionDurationProp = dataProp.FindPropertyRelative("TransitionDuration");
        SerializedProperty boolValueProp = dataProp.FindPropertyRelative("BoolValue");
        SerializedProperty intValueProp = dataProp.FindPropertyRelative("IntValue");
        SerializedProperty floatValueProp = dataProp.FindPropertyRelative("FloatValue");
        SerializedProperty durationProp = dataProp.FindPropertyRelative("Duration");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("ANIMATOR EVENT", EditorStyles.boldLabel);
        if (objectNameProp != null)
            EditorGUILayout.PropertyField(objectNameProp, new GUIContent("Object Name", "비우면 이 EventSO를 호출한 오브젝트를 사용합니다."));
        if (commandProp != null)
            EditorGUILayout.PropertyField(commandProp, new GUIContent("Command"));

        EventSO.AnimatorCommand command = commandProp != null
            ? (EventSO.AnimatorCommand)commandProp.enumValueIndex
            : EventSO.AnimatorCommand.PlayState;
        bool usesState = command == EventSO.AnimatorCommand.PlayState ||
                         command == EventSO.AnimatorCommand.CrossFade;
        bool usesName = command != EventSO.AnimatorCommand.SetSpeed;

        if (usesName && stateOrParameterProp != null)
        {
            EditorGUILayout.PropertyField(
                stateOrParameterProp,
                new GUIContent(usesState ? "State Name" : "Parameter Name"));
            if (string.IsNullOrWhiteSpace(stateOrParameterProp.stringValue))
                EditorGUILayout.HelpBox(
                    usesState ? "실행할 Animator State 이름을 입력해 주세요." : "변경할 Animator Parameter 이름을 입력해 주세요.",
                    MessageType.Warning);
        }

        if (usesState)
        {
            if (layerProp != null)
                EditorGUILayout.PropertyField(layerProp, new GUIContent("Layer", "-1이면 Animator 기본 레이어 해석을 사용합니다."));
            if (normalizedTimeProp != null)
                EditorGUILayout.PropertyField(normalizedTimeProp, new GUIContent("Start Normalized Time"));
            if (command == EventSO.AnimatorCommand.CrossFade && transitionDurationProp != null)
                EditorGUILayout.PropertyField(
                    transitionDurationProp,
                    new GUIContent("Transition (Normalized)", "전환 대상 상태 길이에 대한 정규화된 전환 시간입니다."));
        }
        else
        {
            switch (command)
            {
                case EventSO.AnimatorCommand.SetBool:
                    if (boolValueProp != null) EditorGUILayout.PropertyField(boolValueProp, new GUIContent("Value"));
                    break;
                case EventSO.AnimatorCommand.SetInteger:
                    if (intValueProp != null) EditorGUILayout.PropertyField(intValueProp, new GUIContent("Value"));
                    break;
                case EventSO.AnimatorCommand.SetFloat:
                    if (floatValueProp != null) EditorGUILayout.PropertyField(floatValueProp, new GUIContent("Value"));
                    break;
                case EventSO.AnimatorCommand.SetSpeed:
                    if (floatValueProp != null) EditorGUILayout.PropertyField(floatValueProp, new GUIContent("Animator Speed"));
                    break;
            }
        }

        if (durationProp != null)
            EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (Unscaled Seconds)", "명령 적용 후 다음 Phase로 넘어가기 전에 기다릴 시간입니다."));
        EditorGUILayout.HelpBox(
            "Object Name이 비어 있으면 Caller의 Animator를 사용하고, 없으면 자식 Animator까지 찾습니다. Duration은 애니메이션 길이를 자동 추정하지 않으며 설정한 시간만큼 Phase 진행을 막습니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawCameraShakeDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Screen Shake", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty useMainProp = dataProp.FindPropertyRelative("UseMainCamera");
        SerializedProperty durationProp = dataProp.FindPropertyRelative("Duration");
        SerializedProperty positionProp = dataProp.FindPropertyRelative("PositionStrength");
        SerializedProperty rotationProp = dataProp.FindPropertyRelative("RotationStrength");
        SerializedProperty frequencyProp = dataProp.FindPropertyRelative("Frequency");
        SerializedProperty fadeOutProp = dataProp.FindPropertyRelative("FadeOut");
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SCREEN SHAKE", EditorStyles.boldLabel);
        if (durationProp != null) EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (Unscaled Seconds)"));

        string advancedKey = BuildKey(uniqueKey, ".ScreenShakeAdvanced");
        if (!masterFoldouts.TryGetValue(advancedKey, out _)) masterFoldouts[advancedKey] = false;
        masterFoldouts[advancedKey] = EditorGUILayout.Foldout(
            masterFoldouts[advancedKey],
            "Advanced Settings (Optional)",
            true);
        if (masterFoldouts[advancedKey])
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (useMainProp != null) EditorGUILayout.PropertyField(useMainProp, new GUIContent("Use Main Camera"));
            if (positionProp != null) EditorGUILayout.PropertyField(positionProp, new GUIContent("Position Strength"));
            if (rotationProp != null) EditorGUILayout.PropertyField(rotationProp, new GUIContent("Rotation Strength"));
            if (frequencyProp != null) EditorGUILayout.PropertyField(frequencyProp, new GUIContent("Frequency"));
            if (fadeOutProp != null) EditorGUILayout.PropertyField(fadeOutProp, new GUIContent("Fade Out"));
            EditorGUILayout.EndVertical();
        }

        float shakeDuration = durationProp != null ? durationProp.floatValue : 0f;
        Vector3 positionStrength = positionProp != null ? positionProp.vector3Value : Vector3.zero;
        Vector3 rotationStrength = rotationProp != null ? rotationProp.vector3Value : Vector3.zero;
        if (shakeDuration <= 0f)
            EditorGUILayout.HelpBox("Duration이 0이면 화면 흔들림이 재생되지 않습니다.", MessageType.Warning);
        else if (positionStrength.sqrMagnitude <= 0.000001f && rotationStrength.sqrMagnitude <= 0.000001f)
            EditorGUILayout.HelpBox("Position Strength와 Rotation Strength가 모두 0이라 화면은 흔들리지 않지만 Duration 동안 Phase는 유지됩니다.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox(
                useMainProp != null && !useMainProp.boolValue
                    ? "Duration만 설정하면 기본 세기로 Presentation EventCamera를 흔듭니다. 종료나 취소 시 이 연출이 더한 위치·회전만 제거합니다."
                    : "Duration만 설정하면 기본 세기로 Main Camera를 흔듭니다. 종료나 취소 시 이 연출이 더한 위치·회전만 제거합니다.",
                MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawCameraLensDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Camera Lens", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty useMainProp = dataProp.FindPropertyRelative("UseMainCamera");
        SerializedProperty fieldOfViewProp = dataProp.FindPropertyRelative("TargetFieldOfView");
        SerializedProperty orthographicSizeProp = dataProp.FindPropertyRelative("TargetOrthographicSize");
        SerializedProperty durationProp = dataProp.FindPropertyRelative("Duration");
        SerializedProperty easeProp = dataProp.FindPropertyRelative("EaseInOut");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("CAMERA LENS", EditorStyles.boldLabel);
        if (useMainProp != null) EditorGUILayout.PropertyField(useMainProp, new GUIContent("Use Main Camera"));
        if (fieldOfViewProp != null) EditorGUILayout.PropertyField(fieldOfViewProp, new GUIContent("Target FOV (Perspective)"));
        if (orthographicSizeProp != null) EditorGUILayout.PropertyField(orthographicSizeProp, new GUIContent("Target Size (Orthographic)"));
        if (durationProp != null) EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (Unscaled Seconds)"));
        if (easeProp != null) EditorGUILayout.PropertyField(easeProp, new GUIContent("Ease In Out"));

        if (durationProp != null && durationProp.floatValue <= 0f)
            EditorGUILayout.HelpBox("Duration이 0이면 해당 카메라 투영 방식의 목표 렌즈 값으로 즉시 전환합니다.", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Perspective Camera는 Target FOV, Orthographic Camera는 Target Size를 사용하며 전환이 끝날 때까지 Phase를 유지합니다.", MessageType.Info);

        if (useMainProp != null && !useMainProp.boolValue)
            EditorGUILayout.HelpBox("Presentation EventCamera 인스턴스를 재사용합니다. 단독 실행 시 전환 동안만 활성화되고, 설정한 렌즈 값은 다음 EventCamera 연출에도 유지됩니다.", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawVisualFadeDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Visual Fade", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty arrayProp = dataProp.FindPropertyRelative("Fades");
        if (arrayProp == null)
        {
            EditorGUILayout.HelpBox("Visual Fade의 Fades 데이터를 찾을 수 없습니다.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("VISUAL FADE", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "CanvasGroup, CanvasGroup 밖의 UI Graphic, SpriteRenderer, 3D Renderer를 함께 지원합니다. 여러 Entry는 동시에 실행되고 가장 긴 Duration이 끝날 때까지 Phase를 유지합니다.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Entries ({arrayProp.arraySize})", EditorStyles.boldLabel);
        if (GUILayout.Button("Add", GUILayout.Width(52)))
        {
            int newIndex = arrayProp.arraySize;
            arrayProp.arraySize++;
            SerializedProperty added = arrayProp.GetArrayElementAtIndex(newIndex);
            SetStringRelative(added, "ObjectName", string.Empty);
            SetBoolRelative(added, "IncludeChildren", true);
            SetFloatRelative(added, "TargetAlpha", 0f);
            SetFloatRelative(added, "Duration", 0.5f);
            SetBoolRelative(added, "EaseInOut", true);
        }
        EditorGUI.BeginDisabledGroup(arrayProp.arraySize == 0);
        if (GUILayout.Button("-", GUILayout.Width(24)))
            arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (arrayProp.arraySize == 0)
            EditorGUILayout.HelpBox("Fade할 오브젝트를 한 개 이상 추가해 주세요.", MessageType.Warning);

        int removeIndex = -1;
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            SerializedProperty entry = arrayProp.GetArrayElementAtIndex(i);
            if (entry == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Fade {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("X", GUILayout.Width(24))) removeIndex = i;
            EditorGUILayout.EndHorizontal();

            SerializedProperty objectName = entry.FindPropertyRelative("ObjectName");
            SerializedProperty includeChildren = entry.FindPropertyRelative("IncludeChildren");
            SerializedProperty targetAlpha = entry.FindPropertyRelative("TargetAlpha");
            SerializedProperty duration = entry.FindPropertyRelative("Duration");
            SerializedProperty easeInOut = entry.FindPropertyRelative("EaseInOut");
            if (objectName != null)
                EditorGUILayout.PropertyField(objectName, new GUIContent("Object (Name / Path, Empty = Caller)"));
            if (includeChildren != null)
                EditorGUILayout.PropertyField(includeChildren, new GUIContent("Include Children"));
            if (targetAlpha != null)
                EditorGUILayout.PropertyField(targetAlpha, new GUIContent("Target Alpha"));
            if (duration != null)
                EditorGUILayout.PropertyField(duration, new GUIContent("Duration (Unscaled Seconds)"));
            if (easeInOut != null)
                EditorGUILayout.PropertyField(easeInOut, new GUIContent("Ease In Out"));

            if (duration != null && duration.floatValue <= 0f)
                EditorGUILayout.HelpBox("Duration이 0이면 Target Alpha를 즉시 적용합니다.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            arrayProp.DeleteArrayElementAtIndex(removeIndex);

        EditorGUILayout.HelpBox(
            "3D Renderer는 _BaseColor 또는 _Color Alpha를 사용합니다. 실제 투명 표현을 위해 해당 Material의 Surface/Rendering Mode가 Transparent 계열이어야 합니다.",
            MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawLightTweenDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Light Tween", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty arrayProp = dataProp.FindPropertyRelative("Lights");
        if (arrayProp == null)
        {
            EditorGUILayout.HelpBox("Light Tween의 Lights 데이터를 찾을 수 없습니다.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("LIGHT TWEEN", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "지정 오브젝트의 Light 색·밝기·범위를 동시에 보간합니다. 여러 Entry는 동시에 실행되고 가장 긴 Duration이 끝날 때까지 Phase를 유지합니다.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Entries ({arrayProp.arraySize})", EditorStyles.boldLabel);
        if (GUILayout.Button("Add", GUILayout.Width(52)))
        {
            int newIndex = arrayProp.arraySize;
            arrayProp.arraySize++;
            SerializedProperty added = arrayProp.GetArrayElementAtIndex(newIndex);
            SetStringRelative(added, "ObjectName", string.Empty);
            SetBoolRelative(added, "IncludeChildren", true);
            SetBoolRelative(added, "AffectColor", false);
            SerializedProperty targetColor = added?.FindPropertyRelative("TargetColor");
            if (targetColor != null) targetColor.colorValue = Color.white;
            SetBoolRelative(added, "AffectIntensity", true);
            SetFloatRelative(added, "TargetIntensity", 1f);
            SetBoolRelative(added, "AffectRange", false);
            SetFloatRelative(added, "TargetRange", 10f);
            SetFloatRelative(added, "Duration", 0.5f);
            SetBoolRelative(added, "EaseInOut", true);
        }
        EditorGUI.BeginDisabledGroup(arrayProp.arraySize == 0);
        if (GUILayout.Button("-", GUILayout.Width(24)))
            arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (arrayProp.arraySize == 0)
            EditorGUILayout.HelpBox("조절할 Light를 한 개 이상 추가해 주세요.", MessageType.Warning);

        int removeIndex = -1;
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            SerializedProperty entry = arrayProp.GetArrayElementAtIndex(i);
            if (entry == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Light {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("X", GUILayout.Width(24))) removeIndex = i;
            EditorGUILayout.EndHorizontal();

            SerializedProperty objectName = entry.FindPropertyRelative("ObjectName");
            SerializedProperty includeChildren = entry.FindPropertyRelative("IncludeChildren");
            SerializedProperty affectColor = entry.FindPropertyRelative("AffectColor");
            SerializedProperty targetColor = entry.FindPropertyRelative("TargetColor");
            SerializedProperty affectIntensity = entry.FindPropertyRelative("AffectIntensity");
            SerializedProperty targetIntensity = entry.FindPropertyRelative("TargetIntensity");
            SerializedProperty affectRange = entry.FindPropertyRelative("AffectRange");
            SerializedProperty targetRange = entry.FindPropertyRelative("TargetRange");
            SerializedProperty duration = entry.FindPropertyRelative("Duration");
            SerializedProperty easeInOut = entry.FindPropertyRelative("EaseInOut");

            if (objectName != null)
                EditorGUILayout.PropertyField(objectName, new GUIContent("Object (Name / Path, Empty = Caller)"));
            if (includeChildren != null)
                EditorGUILayout.PropertyField(includeChildren, new GUIContent("Include Children"));
            if (affectColor != null)
                EditorGUILayout.PropertyField(affectColor, new GUIContent("Affect Color"));
            if (affectColor != null && affectColor.boolValue && targetColor != null)
                EditorGUILayout.PropertyField(targetColor, new GUIContent("Target Color"));
            if (affectIntensity != null)
                EditorGUILayout.PropertyField(affectIntensity, new GUIContent("Affect Intensity"));
            if (affectIntensity != null && affectIntensity.boolValue && targetIntensity != null)
                EditorGUILayout.PropertyField(targetIntensity, new GUIContent("Target Intensity"));
            if (affectRange != null)
                EditorGUILayout.PropertyField(affectRange, new GUIContent("Affect Range"));
            if (affectRange != null && affectRange.boolValue && targetRange != null)
                EditorGUILayout.PropertyField(targetRange, new GUIContent("Target Range"));
            if (duration != null)
                EditorGUILayout.PropertyField(duration, new GUIContent("Duration (Unscaled Seconds)"));
            if (easeInOut != null)
                EditorGUILayout.PropertyField(easeInOut, new GUIContent("Ease In Out"));

            bool changesAnything = (affectColor != null && affectColor.boolValue) ||
                                   (affectIntensity != null && affectIntensity.boolValue) ||
                                   (affectRange != null && affectRange.boolValue);
            if (!changesAnything)
                EditorGUILayout.HelpBox("Color, Intensity, Range 중 하나 이상을 켜야 합니다.", MessageType.Warning);
            else if (duration != null && duration.floatValue <= 0f)
                EditorGUILayout.HelpBox("Duration이 0이면 선택한 목표값을 즉시 적용합니다.", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            arrayProp.DeleteArrayElementAtIndex(removeIndex);
        EditorGUILayout.EndVertical();
    }

    private void DrawParticleEventDirect(SerializedProperty dataProp, string uniqueKey)
    {
        if (dataProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "Particle Event", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty objectName = dataProp.FindPropertyRelative("ObjectName");
        SerializedProperty includeChildren = dataProp.FindPropertyRelative("IncludeChildren");
        SerializedProperty command = dataProp.FindPropertyRelative("Command");
        SerializedProperty duration = dataProp.FindPropertyRelative("Duration");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("PARTICLE EVENT", EditorStyles.boldLabel);
        if (objectName != null)
            EditorGUILayout.PropertyField(objectName, new GUIContent("Object (Name / Path, Empty = Caller)"));
        if (includeChildren != null)
            EditorGUILayout.PropertyField(includeChildren, new GUIContent("Include Children"));
        if (command != null)
            EditorGUILayout.PropertyField(command, new GUIContent("Command"));
        if (duration != null)
            EditorGUILayout.PropertyField(duration, new GUIContent("Duration (Unscaled Seconds)"));

        EditorGUILayout.HelpBox(
            "대상과 선택한 자식의 ParticleSystem에 명령을 한 번 적용합니다. Duration은 파티클 수명을 추정하지 않고 설정한 시간만큼만 Phase 진행을 막습니다.",
            MessageType.Info);
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

    private void DrawAttachObjectDirect(SerializedProperty attachProp, string uniqueKey)
    {
        if (attachProp == null || drawnDataKeys.Contains(uniqueKey)) return;
        drawnDataKeys.Add(uniqueKey);

        string foldKey = BuildKey(uniqueKey);
        if (!masterFoldouts.TryGetValue(foldKey, out _)) masterFoldouts[foldKey] = true;
        masterFoldouts[foldKey] = EditorGUILayout.Foldout(masterFoldouts[foldKey], "AttachObject", true);
        if (!masterFoldouts[foldKey]) return;

        SerializedProperty arrayProp = attachProp.FindPropertyRelative("AttachObjects");
        if (arrayProp == null)
        {
            EditorGUILayout.HelpBox("AttachObjects 데이터를 찾을 수 없습니다.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("ATTACH OBJECT", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Parent Name이 비어 있으면 부모에서 분리합니다. Apply Local Transform을 켜면 연결 직후 지정한 로컬 위치·회전·스케일을 적용합니다.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Entries ({arrayProp.arraySize})", EditorStyles.boldLabel);
        if (GUILayout.Button("Add", GUILayout.Width(52)))
        {
            int newIndex = arrayProp.arraySize;
            arrayProp.arraySize++;
            SerializedProperty added = arrayProp.GetArrayElementAtIndex(newIndex);
            SetStringRelative(added, "ObjectName", string.Empty);
            SetStringRelative(added, "ParentName", string.Empty);
            SetBoolRelative(added, "WorldPositionStays", true);
            SetBoolRelative(added, "ApplyLocalTransform", false);
            SerializedProperty localPosition = added?.FindPropertyRelative("LocalPosition");
            SerializedProperty localEulerAngles = added?.FindPropertyRelative("LocalEulerAngles");
            SerializedProperty localScale = added?.FindPropertyRelative("LocalScale");
            if (localPosition != null) localPosition.vector3Value = Vector3.zero;
            if (localEulerAngles != null) localEulerAngles.vector3Value = Vector3.zero;
            if (localScale != null) localScale.vector3Value = Vector3.one;
        }
        EditorGUI.BeginDisabledGroup(arrayProp.arraySize == 0);
        if (GUILayout.Button("-", GUILayout.Width(24)))
            arrayProp.DeleteArrayElementAtIndex(arrayProp.arraySize - 1);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        if (arrayProp.arraySize == 0)
            EditorGUILayout.HelpBox("연결하거나 분리할 오브젝트를 추가해 주세요.", MessageType.Warning);

        int removeIndex = -1;
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            SerializedProperty entry = arrayProp.GetArrayElementAtIndex(i);
            if (entry == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Attach {i + 1}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("X", GUILayout.Width(24))) removeIndex = i;
            EditorGUILayout.EndHorizontal();

            SerializedProperty objectName = entry.FindPropertyRelative("ObjectName");
            SerializedProperty parentName = entry.FindPropertyRelative("ParentName");
            SerializedProperty worldPositionStays = entry.FindPropertyRelative("WorldPositionStays");
            SerializedProperty applyLocalTransform = entry.FindPropertyRelative("ApplyLocalTransform");
            if (objectName != null)
                EditorGUILayout.PropertyField(objectName, new GUIContent("Object (Name / Path, Empty = Caller)"));
            if (parentName != null)
                EditorGUILayout.PropertyField(parentName, new GUIContent("Parent (Name / Path, Empty = Detach)"));
            if (worldPositionStays != null)
                EditorGUILayout.PropertyField(worldPositionStays, new GUIContent("World Position Stays"));
            if (applyLocalTransform != null)
                EditorGUILayout.PropertyField(applyLocalTransform, new GUIContent("Apply Local Transform"));
            if (applyLocalTransform != null && applyLocalTransform.boolValue)
            {
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("LocalPosition"), new GUIContent("Local Position"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("LocalEulerAngles"), new GUIContent("Local Euler Angles"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("LocalScale"), new GUIContent("Local Scale"));
            }
            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
            arrayProp.DeleteArrayElementAtIndex(removeIndex);
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

            SetStringRelative(added, "CollisionObjectA", string.Empty);
            SetStringRelative(added, "CollisionObjectB", string.Empty);
            SetStringRelative(added, "ObjectStateTarget", string.Empty);
            SetEnumRelative(added, "ObjectState", (int)EventSO.ObjectStateCheck.Exists);
            SetStringRelative(added, "DistanceObjectA", string.Empty);
            SetStringRelative(added, "DistanceObjectB", string.Empty);
            SetFloatRelative(added, "DistanceThreshold", 1f);
            SetEnumRelative(added, "DistanceComparison", (int)EventSO.DistanceCheck.AtMost);
            SetBoolRelative(added, "DistanceUse2D", false);
            SetIntRelative(added, "InputKeyCode", (int)KeyCode.E);
            SetEnumRelative(added, "InputKeyState", (int)EventSO.InputKeyCheck.PressedThisFrame);
            SetStringRelative(added, "AnimatorObject", string.Empty);
            SetStringRelative(added, "AnimatorStateName", string.Empty);
            SetIntRelative(added, "AnimatorLayer", 0);
            SetEnumRelative(added, "AnimatorState", (int)EventSO.AnimatorStateCheck.Current);
            SetFloatRelative(added, "AnimatorCompletionTime", 1f);
            SetStringRelative(added, "SceneName", string.Empty);
            SetEnumRelative(added, "SceneState", (int)EventSO.SceneStateCheck.Loaded);
            SetStringRelative(added, "CameraViewTarget", string.Empty);
            SetEnumRelative(added, "CameraViewState", (int)EventSO.CameraViewCheck.Visible);
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
        SerializedProperty objectStateTargetProp = condGroupProp.FindPropertyRelative("ObjectStateTarget");
        SerializedProperty objectStateProp = condGroupProp.FindPropertyRelative("ObjectState");
        SerializedProperty distanceAProp = condGroupProp.FindPropertyRelative("DistanceObjectA");
        SerializedProperty distanceBProp = condGroupProp.FindPropertyRelative("DistanceObjectB");
        SerializedProperty distanceThresholdProp = condGroupProp.FindPropertyRelative("DistanceThreshold");
        SerializedProperty distanceComparisonProp = condGroupProp.FindPropertyRelative("DistanceComparison");
        SerializedProperty distanceUse2DProp = condGroupProp.FindPropertyRelative("DistanceUse2D");
        SerializedProperty inputKeyCodeProp = condGroupProp.FindPropertyRelative("InputKeyCode");
        SerializedProperty inputKeyStateProp = condGroupProp.FindPropertyRelative("InputKeyState");
        SerializedProperty animatorObjectProp = condGroupProp.FindPropertyRelative("AnimatorObject");
        SerializedProperty animatorStateNameProp = condGroupProp.FindPropertyRelative("AnimatorStateName");
        SerializedProperty animatorLayerProp = condGroupProp.FindPropertyRelative("AnimatorLayer");
        SerializedProperty animatorStateProp = condGroupProp.FindPropertyRelative("AnimatorState");
        SerializedProperty animatorCompletionTimeProp = condGroupProp.FindPropertyRelative("AnimatorCompletionTime");
        SerializedProperty sceneNameProp = condGroupProp.FindPropertyRelative("SceneName");
        SerializedProperty sceneStateProp = condGroupProp.FindPropertyRelative("SceneState");
        SerializedProperty cameraViewTargetProp = condGroupProp.FindPropertyRelative("CameraViewTarget");
        SerializedProperty cameraViewStateProp = condGroupProp.FindPropertyRelative("CameraViewState");
        var condsProp = condGroupProp.FindPropertyRelative("Conditions");
        if (checksProp == null || condsProp == null)
        {
            EditorGUILayout.HelpBox("Condition 데이터를 읽을 수 없습니다.", MessageType.Error);
            return;
        }

        const int knownChecksMask = (int)(
            EventSO.ConditionChecks.CheckValue |
            EventSO.ConditionChecks.TimeCount |
            EventSO.ConditionChecks.CollisionAB |
            EventSO.ConditionChecks.ObjectState |
            EventSO.ConditionChecks.Distance |
            EventSO.ConditionChecks.InputKey |
            EventSO.ConditionChecks.AnimatorState |
            EventSO.ConditionChecks.SceneState |
            EventSO.ConditionChecks.CameraView);
        int checksMask = checksProp.intValue & knownChecksMask;
        EditorGUI.BeginChangeCheck();
        int selectedMask = EditorGUILayout.MaskField(
            "Condition Checks",
            checksMask,
            new[]
            {
                "Check Value",
                "Time Count",
                "A ↔ B Collision",
                "Object State",
                "Distance",
                "Input Key",
                "Animator State",
                "Scene State",
                "Camera View"
            });
        if (EditorGUI.EndChangeCheck())
        {
            int updatedMask = selectedMask < 0
                ? knownChecksMask
                : selectedMask & knownChecksMask;
            checksProp.intValue = updatedMask;
            checksProp.serializedObject.ApplyModifiedProperties();
            Repaint();
            GUIUtility.ExitGUI();
            return;
        }

        int newMask = checksMask;

        bool checkValue = (newMask & (int)EventSO.ConditionChecks.CheckValue) != 0;
        bool timeCount = (newMask & (int)EventSO.ConditionChecks.TimeCount) != 0;
        bool collisionAB = (newMask & (int)EventSO.ConditionChecks.CollisionAB) != 0;
        bool objectState = (newMask & (int)EventSO.ConditionChecks.ObjectState) != 0;
        bool distance = (newMask & (int)EventSO.ConditionChecks.Distance) != 0;
        bool inputKey = (newMask & (int)EventSO.ConditionChecks.InputKey) != 0;
        bool animatorState = (newMask & (int)EventSO.ConditionChecks.AnimatorState) != 0;
        bool sceneState = (newMask & (int)EventSO.ConditionChecks.SceneState) != 0;
        bool cameraView = (newMask & (int)EventSO.ConditionChecks.CameraView) != 0;
        if (!checkValue && !timeCount && !collisionAB && !objectState && !distance && !inputKey && !animatorState && !sceneState && !cameraView)
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
                "필요한 Collider가 없으면 오브젝트 크기에 맞는 런타임 Trigger Collider를 자동으로 추가합니다. " +
                "기존 static Collider만 있거나 Rigidbody의 충돌 감지가 꺼진 조합도 원본을 변경하지 않고 Collider별 보조 Trigger/숨김 kinematic probe를 런타임에 준비합니다. " +
                "짧게 접촉해도 이번 Play의 해당 오브젝트 생명주기 동안 기억합니다.",
                MessageType.Info);

            string objectA = collisionAProp?.stringValue?.Trim() ?? string.Empty;
            string objectB = collisionBProp?.stringValue?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(objectA) || string.IsNullOrEmpty(objectB))
                EditorGUILayout.HelpBox("Object A와 Object B를 모두 입력해야 합니다.", MessageType.Warning);
            else if (string.Equals(objectA, objectB, StringComparison.Ordinal))
                EditorGUILayout.HelpBox("Object A와 Object B는 서로 다른 오브젝트여야 합니다.", MessageType.Warning);
        }

        if (objectState)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("OBJECT STATE", EditorStyles.miniBoldLabel);
            if (objectStateTargetProp != null)
                EditorGUILayout.PropertyField(objectStateTargetProp, new GUIContent("Object (Name / Path)"));
            if (objectStateProp != null)
                EditorGUILayout.PropertyField(objectStateProp, new GUIContent("Required State"));
            if (string.IsNullOrWhiteSpace(objectStateTargetProp?.stringValue))
                EditorGUILayout.HelpBox("상태를 확인할 Object 이름 또는 Root/Child 경로를 입력해야 합니다.", MessageType.Warning);
        }

        if (distance)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("DISTANCE", EditorStyles.miniBoldLabel);
            if (distanceAProp != null)
                EditorGUILayout.PropertyField(distanceAProp, new GUIContent("Object A (Name / Path)"));
            if (distanceBProp != null)
                EditorGUILayout.PropertyField(distanceBProp, new GUIContent("Object B (Name / Path)"));
            if (distanceComparisonProp != null)
                EditorGUILayout.PropertyField(distanceComparisonProp, new GUIContent("Comparison"));
            if (distanceThresholdProp != null)
                EditorGUILayout.PropertyField(distanceThresholdProp, new GUIContent("Distance"));
            if (distanceUse2DProp != null)
                EditorGUILayout.PropertyField(distanceUse2DProp, new GUIContent("Use 2D Distance (X/Y)"));

            string distanceA = distanceAProp?.stringValue?.Trim() ?? string.Empty;
            string distanceB = distanceBProp?.stringValue?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(distanceA) || string.IsNullOrEmpty(distanceB))
                EditorGUILayout.HelpBox("Object A와 Object B를 모두 입력해야 합니다.", MessageType.Warning);
            else if (string.Equals(distanceA, distanceB, StringComparison.Ordinal))
                EditorGUILayout.HelpBox("Object A와 Object B는 서로 다른 오브젝트여야 합니다.", MessageType.Warning);
        }

        if (inputKey)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("INPUT KEY", EditorStyles.miniBoldLabel);
            if (inputKeyCodeProp != null)
                EditorGUILayout.PropertyField(inputKeyCodeProp, new GUIContent("Key"));
            if (inputKeyStateProp != null)
                EditorGUILayout.PropertyField(inputKeyStateProp, new GUIContent("Input State"));
            EditorGUILayout.HelpBox(
                "키보드와 Mouse0~Mouse4는 Legacy Input Manager와 새 Input System 전용 설정에서 동일하게 읽습니다.",
                MessageType.Info);
            if (inputKeyCodeProp != null && inputKeyCodeProp.intValue == (int)KeyCode.None)
                EditorGUILayout.HelpBox("Key는 None이 아닌 값으로 지정해야 합니다.", MessageType.Warning);
        }

        if (animatorState)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("ANIMATOR STATE", EditorStyles.miniBoldLabel);
            if (animatorObjectProp != null)
                EditorGUILayout.PropertyField(animatorObjectProp, new GUIContent("Object (Name / Path, Empty = Caller)"));
            if (animatorStateNameProp != null)
                EditorGUILayout.PropertyField(animatorStateNameProp, new GUIContent("State Name"));
            if (animatorLayerProp != null)
                EditorGUILayout.PropertyField(animatorLayerProp, new GUIContent("Layer"));
            if (animatorStateProp != null)
                EditorGUILayout.PropertyField(animatorStateProp, new GUIContent("Check"));

            bool checksCompletion = animatorStateProp != null &&
                                    animatorStateProp.enumValueIndex == (int)EventSO.AnimatorStateCheck.Completed;
            if (checksCompletion && animatorCompletionTimeProp != null)
                EditorGUILayout.PropertyField(
                    animatorCompletionTimeProp,
                    new GUIContent("Completion Normalized Time"));

            EditorGUILayout.HelpBox(
                "지정 오브젝트에 Animator가 없으면 자식까지 찾습니다. Current는 해당 State가 현재 재생 중일 때, Completed는 normalizedTime 기준을 넘고 전환 중이 아닐 때 참입니다.",
                MessageType.Info);
            if (string.IsNullOrWhiteSpace(animatorStateNameProp?.stringValue))
                EditorGUILayout.HelpBox("State Name을 입력해야 합니다.", MessageType.Warning);
            if (string.IsNullOrWhiteSpace(animatorObjectProp?.stringValue))
                EditorGUILayout.HelpBox("Object가 비어 있으면 수동/중첩 이벤트에서는 Caller를 사용합니다. 자동 Condition Event에는 Object를 입력해야 합니다.", MessageType.Info);
            if (animatorLayerProp != null && animatorLayerProp.intValue < 0)
                EditorGUILayout.HelpBox("Animator State 조건의 Layer는 0 이상이어야 합니다.", MessageType.Warning);
        }

        if (sceneState)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("SCENE STATE", EditorStyles.miniBoldLabel);
            if (sceneNameProp != null)
                EditorGUILayout.PropertyField(sceneNameProp, new GUIContent("Scene Name"));
            if (sceneStateProp != null)
                EditorGUILayout.PropertyField(sceneStateProp, new GUIContent("Required State"));
            EditorGUILayout.HelpBox(
                "Loaded/Unloaded는 씬의 로드 여부를, Active는 SceneManager의 현재 Active Scene인지 확인합니다.",
                MessageType.Info);
            if (string.IsNullOrWhiteSpace(sceneNameProp?.stringValue))
                EditorGUILayout.HelpBox("확인할 Scene Name을 입력해야 합니다.", MessageType.Warning);
        }

        if (cameraView)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("CAMERA VIEW", EditorStyles.miniBoldLabel);
            if (cameraViewTargetProp != null)
                EditorGUILayout.PropertyField(cameraViewTargetProp, new GUIContent("Object (Name / Path)"));
            if (cameraViewStateProp != null)
                EditorGUILayout.PropertyField(cameraViewStateProp, new GUIContent("Required State"));
            EditorGUILayout.HelpBox(
                "현재 활성화된 Game Camera 중 대상 Layer를 렌더링하는 가장 높은 Depth 카메라를 사용합니다. Renderer가 있으면 Bounds와 카메라 Frustum을, 없으면 Transform 위치를 확인합니다.",
                MessageType.Info);
            if (string.IsNullOrWhiteSpace(cameraViewTargetProp?.stringValue))
                EditorGUILayout.HelpBox("화면 안/밖을 확인할 Object 이름 또는 Root/Child 경로를 입력해야 합니다.", MessageType.Warning);
        }

        int selectedCheckCount =
            (checkValue ? 1 : 0) +
            (timeCount ? 1 : 0) +
            (collisionAB ? 1 : 0) +
            (objectState ? 1 : 0) +
            (distance ? 1 : 0) +
            (inputKey ? 1 : 0) +
            (animatorState ? 1 : 0) +
            (sceneState ? 1 : 0) +
            (cameraView ? 1 : 0);
        if (selectedCheckCount > 1)
            EditorGUILayout.HelpBox("선택한 조건을 모두 만족해야 이 Condition이 실행됩니다. (AND)", MessageType.Info);
    }

    private void DrawConditionsArrayInline(SerializedProperty condsProp, string baseKey)
    {
        if (condsProp == null) return;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Values ({condsProp.arraySize})", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ Add Value", GUILayout.Width(90)))
        {
            int newIndex = condsProp.arraySize;
            condsProp.arraySize++;
            SerializedProperty newValue = condsProp.GetArrayElementAtIndex(newIndex);
            SetStringRelative(newValue, "GlobalNames", string.Empty);
            SetEnumRelative(newValue, "valueType", (int)EventSO.ValueType.Float);
            SetEnumRelative(newValue, "checkType", (int)EventSO.CheckType.Value);
            SetBoolRelative(newValue, "isPlayerPrefData", false);
            SetIntRelative(newValue, "ExpectedInt", 0);
            SetFloatRelative(newValue, "ExpectedFloat", 0f);
            SetBoolRelative(newValue, "ExpectedBool", false);
            SetStringRelative(newValue, "ExpectedString", string.Empty);
            SerializedProperty expectedObject = newValue?.FindPropertyRelative("ExpectedGameObject");
            if (expectedObject != null) expectedObject.objectReferenceValue = null;
        }
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
            var nameProp = condElem.FindPropertyRelative("GlobalNames");
            var valueTypeProp = condElem.FindPropertyRelative("valueType");
            var prefProp = condElem.FindPropertyRelative("isPlayerPrefData");
            var checkTypeProp = condElem.FindPropertyRelative("checkType");
            if (nameProp != null) EditorGUILayout.PropertyField(nameProp, new GUIContent("Global Key"));
            if (checkTypeProp != null) EditorGUILayout.PropertyField(checkTypeProp, new GUIContent("Check Type"));

            EventSO.CheckType checkType = checkTypeProp != null
                ? (EventSO.CheckType)checkTypeProp.enumValueIndex
                : EventSO.CheckType.Value;
            bool isParity = checkType == EventSO.CheckType.Odd || checkType == EventSO.CheckType.Even;
            bool isOrderedComparison =
                checkType == EventSO.CheckType.Greater ||
                checkType == EventSO.CheckType.GreaterOrEqual ||
                checkType == EventSO.CheckType.Less ||
                checkType == EventSO.CheckType.LessOrEqual;
            int valueTypeIndex = valueTypeProp != null
                ? valueTypeProp.enumValueIndex
                : (int)EventSO.ValueType.Float;

            if (isParity)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Popup("Value Type", 0, new[] { "Int" });
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.HelpBox("Odd/Even은 Int 전용이며 기대값 입력이 필요 없습니다.", MessageType.Info);
            }
            else
            {
                if (valueTypeProp != null)
                    EditorGUILayout.PropertyField(valueTypeProp, new GUIContent("Value Type"));
                valueTypeIndex = valueTypeProp != null
                    ? valueTypeProp.enumValueIndex
                    : (int)EventSO.ValueType.Float;
                switch (valueTypeIndex)
                {
                    case (int)EventSO.ValueType.Int:
                        var intExpected = condElem.FindPropertyRelative("ExpectedInt");
                        if (intExpected != null) EditorGUILayout.PropertyField(intExpected, new GUIContent("Expected Int"));
                        break;
                    case (int)EventSO.ValueType.Float:
                        var floatExpected = condElem.FindPropertyRelative("ExpectedFloat");
                        if (floatExpected != null) EditorGUILayout.PropertyField(floatExpected, new GUIContent("Expected Float"));
                        break;
                    case (int)EventSO.ValueType.Bool:
                        var boolExpected = condElem.FindPropertyRelative("ExpectedBool");
                        if (boolExpected != null) EditorGUILayout.PropertyField(boolExpected, new GUIContent("Expected Bool"));
                        break;
                    case (int)EventSO.ValueType.String:
                        var stringExpected = condElem.FindPropertyRelative("ExpectedString");
                        if (stringExpected != null) EditorGUILayout.PropertyField(stringExpected, new GUIContent("Expected String"));
                        break;
                    case (int)EventSO.ValueType.GameObject:
                        var objectExpected = condElem.FindPropertyRelative("ExpectedGameObject");
                        if (objectExpected != null) EditorGUILayout.PropertyField(objectExpected, new GUIContent("Expected GameObject"));
                        break;
                }

                if (isOrderedComparison &&
                    valueTypeIndex != (int)EventSO.ValueType.Int &&
                    valueTypeIndex != (int)EventSO.ValueType.Float)
                    EditorGUILayout.HelpBox("크기 비교(>, ≥, <, ≤)는 Int 또는 Float에서만 사용할 수 있습니다.", MessageType.Warning);
            }

            bool isGameObjectCondition = !isParity &&
                                         valueTypeIndex == (int)EventSO.ValueType.GameObject;
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
    private static EventSO.EventStep CreatePhaseSnapshot(EventSO.EventStep source)
    {
        if (source == null) return null;

        var snapshot = new EventSO.EventStep();
        FieldInfo[] fields = typeof(EventSO.EventStep).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field.Name == nameof(EventSO.EventStep.EventExe)) continue;
            field.SetValue(snapshot, field.GetValue(source));
        }
        snapshot.EventExe = null;
        return snapshot;
    }

    private void CopyPhase(EventSO asset, SerializedProperty arrayProp, int index)
    {
        if (asset == null || arrayProp == null) return;
        try
        {
            arrayProp.serializedObject.ApplyModifiedProperties();
            EventSO.EventStep source = GetEventStep(asset, arrayProp, index);
            EventSO.EventStep snapshot = CreatePhaseSnapshot(source);
            phaseClipboardJson = snapshot != null
                ? EditorJsonUtility.ToJson(snapshot, true)
                : null;
        }
        catch
        {
            phaseClipboardJson = null;
        }
    }

    private bool PastePhase(EventSO asset, SerializedProperty arrayProp, int index)
    {
        if (asset == null || arrayProp == null || string.IsNullOrEmpty(phaseClipboardJson)) return false;
        try
        {
            arrayProp.serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(asset, $"Paste Phase {index + 1}");

            EventSO.EventStep destinationStep = GetEventStep(asset, arrayProp, index);
            if (destinationStep == null) return false;

            bool keepNextPhase = destinationStep.Flags != null && destinationStep.Flags.IsEventExe;
            EventSO.EventExeData keepEventExe = destinationStep.EventExe;

            var clone = new EventSO.EventStep();
            EditorJsonUtility.FromJsonOverwrite(phaseClipboardJson, clone);
            if (clone.Flags == null) clone.Flags = new EventSO.EventStepFlags();
            clone.Flags.IsEventExe = keepNextPhase;
            clone.EventExe = keepEventExe;

            SerializedProperty element = EnsureEventStepElement(arrayProp, index);
            if (element == null) return false;
            if (element.propertyType == SerializedPropertyType.ManagedReference)
            {
                element.managedReferenceValue = clone;
                arrayProp.serializedObject.ApplyModifiedProperties();
            }
            else if (string.Equals(arrayProp.propertyPath, "ConditionSteps", StringComparison.Ordinal) &&
                     asset.ConditionSteps != null && index >= 0 && index < asset.ConditionSteps.Length)
            {
                asset.ConditionSteps[index] = clone;
                arrayProp.serializedObject.Update();
            }
            else
            {
                return false;
            }

            EditorUtility.SetDirty(asset);
            return true;
        }
        catch
        {
            return false;
        }
    }

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

    private void HandlePhaseReorderDrag(
        List<PhaseNode> steps,
        string key,
        int reorderablePhaseCount)
    {
        if (!dragInProgress || dragActiveKey != key) return;
        if (steps == null || reorderablePhaseCount < 2 ||
            reorderablePhaseCount > steps.Count ||
            dragFromIndex < 0 || dragFromIndex >= reorderablePhaseCount ||
            !itemHeaderRects.TryGetValue(key, out var rects) ||
            rects == null || rects.Count != steps.Count)
        {
            ResetPhaseDragState();
            return;
        }

        if ((Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) ||
            Event.current.type == EventType.MouseLeaveWindow ||
            Event.current.type == EventType.Ignore)
        {
            ResetPhaseDragState();
            Event.current.Use();
            return;
        }

        Vector2 mouse = Event.current.mousePosition;
        int target = reorderablePhaseCount - 1;
        for (int i = 0; i < reorderablePhaseCount; i++)
        {
            if (mouse.y < rects[i].center.y)
            {
                target = i;
                break;
            }
        }
        dragToIndex = Mathf.Clamp(target, 0, reorderablePhaseCount - 1);

        if (Event.current.type == EventType.MouseUp)
        {
            bool changed = dragFromIndex >= 0 && dragToIndex >= 0 &&
                           dragFromIndex != dragToIndex &&
                           ReorderPhaseContents(
                               steps,
                               dragFromIndex,
                               dragToIndex,
                               reorderablePhaseCount);
            ResetPhaseDragState();
            Event.current.Use();
            if (changed)
                GUIUtility.ExitGUI();
            return;
        }

        Rect targetRect = rects[dragToIndex];
        EditorGUI.DrawRect(
            new Rect(targetRect.x, targetRect.y - 1f, targetRect.width, 2f),
            new Color(0f, 0.7f, 1f, 0.9f));
    }

    private static void ResetPhaseDragState()
    {
        if (dragControlId != 0 && GUIUtility.hotControl == dragControlId)
            GUIUtility.hotControl = 0;
        dragControlId = 0;
        dragInProgress = false;
        dragActiveKey = null;
        dragFromIndex = -1;
        dragToIndex = -1;
    }

    private bool ReorderPhaseContents(
        List<PhaseNode> steps,
        int fromIndex,
        int toIndex,
        int reorderablePhaseCount)
    {
        if (steps == null || reorderablePhaseCount < 2 ||
            reorderablePhaseCount > steps.Count ||
            fromIndex < 0 || fromIndex >= reorderablePhaseCount ||
            toIndex < 0 || toIndex >= reorderablePhaseCount || fromIndex == toIndex)
            return false;

        EventSO asset = serializedObject.targetObject as EventSO;
        if (asset == null) return false;

        try
        {
            serializedObject.ApplyModifiedProperties();
            var destinations = new List<EventSO.EventStep>(reorderablePhaseCount);
            var reordered = new List<EventSO.EventStep>(reorderablePhaseCount);
            for (int i = 0; i < reorderablePhaseCount; i++)
            {
                PhaseNode node = steps[i];
                EventSO.EventStep destination = GetEventStep(
                    asset,
                    node.ContainerArrayProp,
                    node.ContainerIndex);
                if (destination == null) return false;

                EventSO.EventStep snapshot = CreatePhaseSnapshot(destination);
                if (snapshot == null) return false;
                string json = EditorJsonUtility.ToJson(snapshot, false);
                var clone = new EventSO.EventStep();
                EditorJsonUtility.FromJsonOverwrite(json, clone);
                clone.EventExe = null;
                destinations.Add(destination);
                reordered.Add(clone);
            }

            EventSO.EventStep moved = reordered[fromIndex];
            reordered.RemoveAt(fromIndex);
            reordered.Insert(toIndex, moved);

            Undo.RecordObject(asset, "Reorder Phases");
            FieldInfo[] fields = typeof(EventSO.EventStep).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < destinations.Count; i++)
            {
                EventSO.EventStep destination = destinations[i];
                EventSO.EventStep source = reordered[i];
                bool keepNextPhase = destination.Flags != null &&
                                     destination.Flags.IsEventExe;
                EventSO.EventExeData keepEventExe = destination.EventExe;

                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    FieldInfo field = fields[fieldIndex];
                    if (field.Name == nameof(EventSO.EventStep.EventExe)) continue;
                    field.SetValue(destination, field.GetValue(source));
                }

                if (destination.Flags == null)
                    destination.Flags = new EventSO.EventStepFlags();
                destination.Flags.IsEventExe = keepNextPhase;
                destination.EventExe = keepEventExe;
            }

            EditorUtility.SetDirty(asset);
            serializedObject.Update();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[EventSOEditor] Phase 순서 변경 실패: {exception.Message}");
            serializedObject.Update();
            return false;
        }
    }
}
