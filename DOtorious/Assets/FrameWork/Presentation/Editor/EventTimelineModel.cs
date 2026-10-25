using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using static JYW.Game.EventPlay.EventSO;

// Editor-only projection. Building this model never repairs, normalizes or serializes EventSO data.
// PropertyPath identifies the original field for future context-menu/editing commands.
internal sealed class EventTimelineItem
{
    public string PropertyPath, Title, Detail, Category, Note;
    public double Start, End;
    public bool OpenEnded, Variable;
}

internal sealed class EventTimelineStep
{
    public string PropertyPath, Label;
    public double Origin, Duration;
    public bool OriginVariable, DurationVariable;
    public readonly List<EventTimelineItem> Items = new List<EventTimelineItem>();
}

internal sealed class EventTimelineDocument
{
    public readonly List<EventTimelineStep> Steps = new List<EventTimelineStep>();
    public readonly List<string> Warnings = new List<string>();
    public bool HasBranches;
}

internal static class EventTimelineBuilder
{
    private const int MaxSteps = 256;
    private static double Seconds(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, value);
    private static string Target(string value) => string.IsNullOrWhiteSpace(value) ? "Caller" : value;
    private static string At(string path, int index) => path + ".Array.data[" + index + "]";

    public static EventTimelineDocument Build(EventSO asset)
    {
        var result = new EventTimelineDocument();
        if (asset == null) return result;
        var active = new HashSet<EventStep>();
        int count = asset.UseCondition ? (asset.Conditions?.Length ?? 0) + 1 : 1;
        result.HasBranches = asset.UseCondition;
        for (int i = 0; i < count; i++)
        {
            EventStep step = ResolveRootStep(asset, i, out string path);
            string route = !asset.UseCondition ? "Manual" : i == count - 1 ? "Else" : "Condition " + (i + 1);
            Visit(step, path, route, 1, 0, false, result, active);
        }
        return result;
    }

    // Match PlayEvent's legacy fallback exactly; an explicitly stored null is not a missing slot.
    internal static EventStep ResolveRootStep(EventSO asset, int index, out string path)
    {
        path = At("ConditionSteps", index);
        if (asset == null || index < 0) return null;
        if (index < (asset.ConditionSteps?.Length ?? 0)) return asset.ConditionSteps[index];
        int conditionCount = asset.Conditions?.Length ?? 0;
        bool useGroup = !asset.UseCondition
            ? index == 0 && (asset.stepsGroups?.Length ?? 0) > 0
            : index < conditionCount && index < (asset.stepsGroups?.Length ?? 0);
        if (useGroup)
        {
            path = At(At("stepsGroups", index) + ".Steps", 0);
            var group = asset.stepsGroups[index];
            return group?.Steps != null && group.Steps.Length > 0 ? group.Steps[0] : null;
        }
        if ((!asset.UseCondition && index == 0 || asset.UseCondition && index == conditionCount) && (asset.elseSteps?.Length ?? 0) > 0)
        {
            path = At("elseSteps", 0);
            return asset.elseSteps[0];
        }
        return null;
    }

    private static void Visit(EventStep source, string path, string route, int phase, double origin,
        bool originVariable, EventTimelineDocument document, HashSet<EventStep> active)
    {
        if (source == null) return;
        if (document.Steps.Count >= MaxSteps || !active.Add(source))
        {
            const string warning = "순환 연결 또는 256개 이상의 Step은 여기서 표시를 멈춥니다. 원본 데이터는 변경하지 않습니다.";
            if (!document.Warnings.Contains(warning)) document.Warnings.Add(warning);
            return;
        }
        var step = new EventTimelineStep { PropertyPath = path, Label = route + " / Phase " + phase,
            Origin = origin, OriginVariable = originVariable };
        document.Steps.Add(step);
        Describe(source, step);
        var f = source.Flags;
        // SceneChange exits ProcessStep before Action/EventExe; do not invent a continuation.
        bool sceneChanges = f != null && f.IsScenes && source.Scenes?.IsSceneChange == true && source.Scenes.SceneChange != null;
        if (!sceneChanges && f?.IsEventExe == true && source.EventExe != null)
        {
            var next = source.EventExe;
            if (next.UseCondition) document.HasBranches = true;
            int count = next.UseCondition ? (next.Conditions?.Length ?? 0) + 1 : 1;
            for (int i = 0; i < count && i < (next.ConditionSteps?.Length ?? 0); i++)
            {
                string childRoute = !next.UseCondition ? route : route + " → " +
                    (i == count - 1 ? "Else" : "Condition " + (i + 1));
                Visit(next.ConditionSteps[i], At(path + ".EventExe.ConditionSteps", i), childRoute, phase + 1,
                    origin + step.Duration, originVariable || step.DurationVariable, document, active);
            }
            if (next.ConditionSteps == null || next.ConditionSteps.Length == 0)
                document.Warnings.Add(step.Label + ": Next Phase 데이터가 없습니다.");
        }
        active.Remove(source);
    }

    internal static EventTimelineStep DescribeStep(EventStep source, string propertyPath, string label)
    {
        var step = new EventTimelineStep { PropertyPath = propertyPath, Label = label };
        if (source != null) Describe(source, step);
        return step;
    }

    private static void Describe(EventStep s, EventTimelineStep step)
    {
        var f = s.Flags;
        if (f == null) return;
        EventTimelineItem Add(string field, string category, string title, string detail = "", double start = 0,
            double end = 0, double? waitEnd = null, bool variable = false, bool open = false, string note = "")
        {
            var item = new EventTimelineItem { PropertyPath = step.PropertyPath + "." + field,
                Category = category, Title = title, Detail = detail ?? "", Start = start, End = Math.Max(start, end),
                Variable = variable, OpenEnded = open, Note = note };
            step.Items.Add(item);
            step.Duration = Math.Max(step.Duration, waitEnd ?? item.End);
            if (variable && !waitEnd.HasValue) step.DurationVariable = true;
            return item;
        }
        void Timed(string field, string category, string title, string detail, float duration) =>
            Add(field, category, title, detail, end: Seconds(duration));
        void Names(string field, string title, string[] names)
        {
            if (names == null) return;
            for (int i = 0; i < names.Length; i++) Add(At(field, i), "Object", title, Target(names[i]));
        }
        void Speech(string field, string title, IEnumerable<SpeechData> lines, string target = "")
        {
            if (lines == null) return;
            var valid = lines.Where(line => line != null).ToArray();
            if (valid.Length == 0) return;
            Add(field, "Speech", title, target + " " + string.Join(" → ", valid.Select(line => line.Text)),
                end: valid.Sum(line => Seconds(line.Duration)));
        }

        if (f.IsSetGlobals && s.SetGlobals?.SetValues != null)
            for (int i = 0; i < s.SetGlobals.SetValues.Length; i++)
            {
                var v = s.SetGlobals.SetValues[i]; if (v == null) continue;
                Add(At("SetGlobals.SetValues", i), "State", "Set Globals", v.ValueName + " · " + v.setType + " / " + v.valueType);
            }
        if (f.IsObjects && s.Objects != null)
        {
            var g = s.Objects;
            if (g.IsSpawnObject && g.SpawnObject?.SpawnDatas != null)
                for (int i = 0; i < g.SpawnObject.SpawnDatas.Length; i++)
                {
                    var v = g.SpawnObject.SpawnDatas[i]; if (v == null) continue;
                    Add(At("Objects.SpawnObject.SpawnDatas", i), "Object", "Spawn", v.prefab != null ? v.prefab.name + " → " + v.naming : "Prefab 없음");
                }
            if (g.IsDestroyObject) Names("Objects.DestroyObject.DestroyObjectNames", "Destroy", g.DestroyObject?.DestroyObjectNames);
            if (g.IsEnableObject) Names("Objects.EnableObject.EnableObjectNames", "Enable Object", g.EnableObject?.EnableObjectNames);
            if (g.IsDisableObject) Names("Objects.DisableObject.DisableObjectNames", "Disable Object", g.DisableObject?.DisableObjectNames);
            if (g.IsRename && g.Rename != null) Add("Objects.Rename", "Object", "Rename", Target(g.Rename.ObjectName) + " → " + g.Rename.NewName);
        }
        if (f.IsTransforms && s.Transforms != null)
        {
            var g = s.Transforms;
            if (g.IsMoveObject && g.MoveObject?.MoveObjects != null)
                for (int i = 0; i < g.MoveObject.MoveObjects.Length; i++)
                {
                    var v = g.MoveObject.MoveObjects[i]; if (v == null) continue;
                    double duration = Seconds(v.Duration);
                    bool loading = v.ShowLoadingScreen && !v.isDrawer;
                    if (loading) duration = Math.Max(duration, Seconds(v.LoadingDuration));
                    Add(At("Transforms.MoveObject.MoveObjects", i), "Transform", loading ? "Move + Loading" : "Move Object",
                        Target(v.objectName) + " → " + (v.isDrawer ? v.drawerOffset : v.targetPosition) + " · " + v.movementType, end: duration);
                }
            if (g.IsRotateObject && g.RotateObject != null) Timed("Transforms.RotateObject", "Transform", "Rotate Object",
                Target(g.RotateObject.ObjectName) + " → " + (g.RotateObject.isLookAt ? g.RotateObject.LookAtName : g.RotateObject.eulerAngles.ToString()), g.RotateObject.Duration);
            if (g.IsAttachObject && g.AttachObject?.AttachObjects != null)
                for (int i = 0; i < g.AttachObject.AttachObjects.Length; i++)
                {
                    var v = g.AttachObject.AttachObjects[i]; if (v != null) Add(At("Transforms.AttachObject.AttachObjects", i), "Transform", "Attach Object", Target(v.ObjectName) + " → " + v.ParentName);
                }
        }
        if (f.IsCameras && s.Cameras != null)
        {
            var g = s.Cameras;
            if (g.IsCameraMove && g.CameraMovement != null)
            {
                var v = g.CameraMovement; double start = Seconds(v.StartTime);
                Add("Cameras.CameraMovement", "Camera", "Camera Move",
                    (v.IsCameraMoveUseMain ? "Main" : "Event Camera") + " · " + v.StartPosition + " → " + v.EndPosition,
                    start, start + Seconds(v.EndTime - v.StartTime), note: "Phase 시작 기준 · StartTime ~ EndTime (scaled seconds)");
            }
            if (g.IsCameraAiming && g.CameraAim != null)
            {
                var v = g.CameraAim; double start = Seconds(v.StartTime);
                Add("Cameras.CameraAim", "Camera", "Camera Aim", Target(v.aimedTargetName), start,
                    Math.Max(start + Seconds(v.InitDuration), Math.Max(start, Seconds(v.EndTime))));
            }
            if (g.IsCameraShake && g.CameraShake != null) Timed("Cameras.CameraShake", "Camera", "Camera Shake",
                g.CameraShake.UseMainCamera ? "Main" : "Event Camera", g.CameraShake.Duration);
            if (g.IsCameraLens && g.CameraLens != null) Timed("Cameras.CameraLens", "Camera", "Camera Lens",
                "FOV " + g.CameraLens.TargetFieldOfView + " / Size " + g.CameraLens.TargetOrthographicSize, g.CameraLens.Duration);
            if (g.IsCameraFix && g.CameraFix != null)
            {
                bool enable = g.CameraFix.Mode == CameraFixMode.Enable;
                Add("Cameras.CameraFix", "Camera", enable ? "Camera Fix · Enable" : "Camera Fix · Disable",
                    enable ? "Main · " + g.CameraFix.Position : "Main · 고정 해제", waitEnd: 0,
                    note: enable ? "즉시 고정 · Disable까지 유지 · 다음 Phase를 막지 않음" : "기존 Camera Fix 해제 · 다음 Phase를 막지 않음");
            }
        }
        if (f.IsComponents && s.Components != null)
        {
            var g = s.Components;
            if (g.IsEnableComponent && g.EnableComponent?.ComponentDatas != null)
                for (int i = 0; i < g.EnableComponent.ComponentDatas.Length; i++)
                {
                    var v = g.EnableComponent.ComponentDatas[i]; if (v != null) Add(At("Components.EnableComponent.ComponentDatas", i), "State", "Enable Component", Target(v.gameObjectName) + " / " + v.componentName);
                }
            if (g.IsDisableComponent && g.DisableComponent?.ComponentDatas != null)
                for (int i = 0; i < g.DisableComponent.ComponentDatas.Length; i++)
                {
                    var v = g.DisableComponent.ComponentDatas[i]; if (v != null) Add(At("Components.DisableComponent.ComponentDatas", i), "State", "Disable Component", Target(v.gameObjectName) + " / " + v.componentName);
                }
            if (g.IsDisableColliderObject) Names("Components.DisableColliderObject.DisableColliderObjectNames", "Disable Collider", g.DisableColliderObject?.DisableColliderObjectNames);
            if (g.IsUpdateComponent && g.UpdateComponent?.UpdateComponents != null)
                for (int i = 0; i < g.UpdateComponent.UpdateComponents.Length; i++)
                {
                    var v = g.UpdateComponent.UpdateComponents[i]; if (v != null) Add(At("Components.UpdateComponent.UpdateComponents", i), "State", "Update Component", Target(v.gameObjectName) + " / " + v.componentName + "." + v.propertyName);
                }
            if (g.IsAnimatorEvent && g.AnimatorEvent != null) Timed("Components.AnimatorEvent", "Animation", "Animator · " + g.AnimatorEvent.Command,
                Target(g.AnimatorEvent.ObjectName) + " / " + g.AnimatorEvent.StateOrParameter, g.AnimatorEvent.Duration);
            if (g.IsVisualFade && g.VisualFade?.Fades != null)
                for (int i = 0; i < g.VisualFade.Fades.Length; i++)
                {
                    var v = g.VisualFade.Fades[i]; if (v != null) Timed(At("Components.VisualFade.Fades", i), "Fade", "Visual Fade", Target(v.ObjectName) + " → Alpha " + v.TargetAlpha, v.Duration);
                }
            if (g.IsLightTween && g.LightTween?.Lights != null)
                for (int i = 0; i < g.LightTween.Lights.Length; i++)
                {
                    var v = g.LightTween.Lights[i]; if (v != null) Timed(At("Components.LightTween.Lights", i), "Light", "Light Tween", Target(v.ObjectName), v.Duration);
                }
            if (g.IsParticleEvent && g.ParticleEvent != null) Timed("Components.ParticleEvent", "Effect", "Particle · " + g.ParticleEvent.Command,
                Target(g.ParticleEvent.ObjectName), g.ParticleEvent.Duration);
        }
        if (f.IsSpeeches && s.Speeches != null)
        {
            var g = s.Speeches;
            if (g.IsSoftSpeech) Speech("Speeches.SoftSpeech", "Soft Speech", g.SoftSpeech?.SoftSpeechTexts);
            if (g.IsSpeechBubble) Speech("Speeches.SpeechBubble", "Speech Bubble", g.SpeechBubble?.SpeechBubbleTexts, Target(g.SpeechBubble?.GameObjectName));
            if (g.IsHardSpeech && g.HardSpeech?.HardSpeechTexts?.Count > 0)
                Add("Speeches.HardSpeech", "Speech", "Hard Speech", string.Join(" → ", g.HardSpeech.HardSpeechTexts),
                    variable: true, open: true, note: g.HardSpeech.HardSpeechKey + " 입력 대기");
            if (g.IsPortraitSpeech && g.PortraitSpeech?.Lines != null && g.PortraitSpeech.Lines.Length > 0)
            {
                var v = g.PortraitSpeech; bool input = v.AdvanceMode == PortraitAdvanceMode.Input;
                Add("Speeches.PortraitSpeech", "Speech", "Portrait Speech", string.Join(" → ", v.Lines.Where(l => l != null).Select(l => l.SpeakerName + ": " + l.Text)),
                    end: input ? 0 : v.Lines.Where(l => l != null).Sum(l => Seconds(l.Duration)), variable: input, open: input,
                    note: input ? v.AdvanceKey + " 입력 대기" : "대사별 Duration 합계");
            }
        }
        if (f.IsJustText && s.JustText != null) Add("JustText", "Speech", "Text", s.JustText.Text, waitEnd: 0, open: true, note: "표시 후 유지 · 다음 Phase를 막지 않음");
        if (f.IsWait && s.Wait != null) Timed("Wait", "Wait", "Wait", "다음 Phase 대기", s.Wait.WaitTime);
        if (f.IsWaitUntilCondition && s.WaitUntilCondition != null)
            Add("WaitUntilCondition", "Wait", "Wait Until Condition", "조건 충족까지", end: Seconds(s.WaitUntilCondition.Timeout),
                variable: true, open: s.WaitUntilCondition.Timeout <= 0, note: s.WaitUntilCondition.Timeout > 0 ? "최대 Timeout · 조기 완료 가능" : "제한 없는 조건 대기");
        if (f.IsTooltip && s.Tooltip != null) Add("Tooltip", "Speech", "Tooltip", s.Tooltip.Content,
            end: s.Tooltip.isBlocked ? 0 : Seconds(s.Tooltip.Duration), variable: s.Tooltip.isBlocked, open: s.Tooltip.isBlocked,
            note: s.Tooltip.isBlocked ? "키보드 / 마우스 입력 대기" : "");
        if (f.IsBlackLabel && s.BlackLabel != null) Timed("BlackLabel", "Effect", "Black Label", "상하 레터박스", s.BlackLabel.Duration);
        if (f.IsScreenFlash && s.ScreenFlash != null) Add("ScreenFlash", "Fade", "Screen Flash", "Fade In + Hold + Fade Out",
            end: Seconds(s.ScreenFlash.FadeInDuration) + Seconds(s.ScreenFlash.Duration) + Seconds(s.ScreenFlash.FadeOutDuration));
        if (f.IsLock && s.Lock != null) Add("Lock", "State", "Input Lock", "Keyboard: " + s.Lock.IsLockKeyboard + " / Mouse: " + s.Lock.IsLockMouse,
            waitEnd: 0, open: true, note: "획득한 잠금은 전체 이벤트 종료 시 해제 · 자체 대기 없음");
        bool sceneChange = false, sceneLoading = false;
        if (f.IsScenes && s.Scenes != null)
        {
            var g = s.Scenes;
            void Scenes(string field, string title, string[] names, bool loading)
            {
                if (names == null) return;
                for (int i = 0; i < names.Length; i++)
                {
                    Add(At(field + ".Scenes", i), "Scene", title, Target(names[i]), variable: loading, open: loading, note: loading ? "씬 처리 완료 시간은 실행 시 결정" : "");
                    if (loading) sceneLoading = true;
                }
            }
            if (g.IsSceneAdd) Scenes("Scenes.SceneAdd", "Scene Add", g.SceneAdd?.Scenes, true);
            if (g.IsSceneOff) Scenes("Scenes.SceneOff", "Scene Off", g.SceneOff?.Scenes, true);
            if (g.IsScenePause) Scenes("Scenes.ScenePause", "Scene Pause", g.ScenePause?.Scenes, false);
            if (g.IsSceneUnPause) Scenes("Scenes.SceneUnPause", "Scene Unpause", g.SceneUnPause?.Scenes, false);
            if (g.IsSetActiveScene && g.SetActiveScene != null) Add("Scenes.SetActiveScene", "Scene", "Set Active Scene", Target(g.SetActiveScene.Scene));
            if (g.IsSceneChange && g.SceneChange != null)
            {
                sceneChange = true;
                Add("Scenes.SceneChange", "Scene", "Scene Change", g.SceneChange.Scene, variable: true, open: true, note: "현재 Phase 종료 · 뒤의 이벤트는 실행하지 않음");
            }
        }
        if (sceneChange) return;
        if (f.IsSound && s.Sound != null)
        {
            var v = s.Sound; double start = Seconds(v.time), length = v.audioClip != null ? Seconds(v.audioClip.length) : 0;
            bool loop = v.audioClip != null && v.isLoop;
            Add("Sound", "Sound", v.audioClip == null ? "Stop Sound" : "Sound", v.audioClip != null ? v.audioClip.name : "재생 중인 소리 정지",
                start, start + (loop ? 0 : length), v.WaitForCompletion && !loop ? (double?)null : start,
                variable: sceneLoading, open: loop, note: (loop ? "Loop · 종료 시점 미정 · 다음 Phase를 막지 않음" : v.WaitForCompletion ? "완료 대기 · Pitch=1 기준" : "지연 후 재생 · 다음 Phase를 막지 않음"));
        }
        if (f.IsFadePlay && s.FadeInfo != null)
        {
            var v = s.FadeInfo; double start = Seconds(v.StartTime);
            Add("FadeInfo", "Fade", v.FadeIn ? "Fade In" : "Fade Out", v.FadeIn ? "화면 Alpha 0 → 1" : "화면 Alpha 1 → 0",
                start, start + Seconds(v.EndTime - v.StartTime), variable: sceneLoading, note: "실시간 기준 · 기존 FadeIn 값과 동작을 그대로 표시");
        }
        if (f.IsTimeScale && s.TimeScale != null)
        {
            Timed("TimeScale", "State", "Time Scale", "×" + s.TimeScale.TargetScale + (s.TimeScale.RestoreAfterDuration ? " · 종료 후 복구" : " · 유지"), s.TimeScale.Duration);
            step.DurationVariable = true;
        }
        if (f.IsCursorVisible && s.Cursor != null) Add("Cursor", "State", "Cursor", s.Cursor.isCursorVisible ? "Visible" : "Hidden", variable: sceneLoading);
        // Choice and Action run AFTER all concurrent routines finish in ProcessStep.
        if (f.IsChoice && s.Choice != null) Add("Choice", "Speech", "Choice", s.Choice.Candidates?.Length + "개 선택지",
            step.Duration, step.Duration, variable: true, open: true, note: "앞의 연출 완료 후 선택 입력 대기");
        if (f.IsAction && s.Action != null) Add("Action", "State", "Action", s.Action.ActionName,
            step.Duration, step.Duration, variable: step.DurationVariable, note: "앞의 연출 / 선택 완료 후 호출");
    }
}
