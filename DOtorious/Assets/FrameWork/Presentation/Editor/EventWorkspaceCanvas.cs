using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Retained, read-only flow projection. All authoring belongs to the host's explicit commands.</summary>
internal sealed class EventWorkspaceCanvas : VisualElement
{
    private readonly Action<string, string> select;
    private readonly Action<string, Rect> addFeature;
    private readonly Action<string> addPhase;
    private readonly ScrollView scroll;
    private readonly VisualElement flow;
    private readonly Dictionary<string, VisualElement> phaseElements = new Dictionary<string, VisualElement>();
    private readonly Dictionary<string, VisualElement> featureElements = new Dictionary<string, VisualElement>();
    private EventSO asset;
    private EventStepListModel model;
    private string selectedPhase, selectedFeature;
    private int revision;

    /// <param name="addFeature">Receives the phase path and anchor's panel-space worldBound.</param>
    internal EventWorkspaceCanvas(Action<string, string> select, Action<string, Rect> addFeature, Action<string> addPhase)
    {
        this.select = select; this.addFeature = addFeature; this.addPhase = addPhase;
        name = "event-workspace-canvas";
        AddToClassList("ew-flow-canvas");
        style.flexGrow = 1;
        style.minWidth = 0;
        style.minHeight = 0;

        scroll = new ScrollView(ScrollViewMode.Vertical) { name = "event-flow-scroll" };
        scroll.AddToClassList("ew-flow-scroll");
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        Add(scroll);

        flow = new VisualElement { name = "event-flow-content" };
        flow.AddToClassList("ew-flow-content");
        flow.style.paddingLeft = 20;
        flow.style.paddingRight = 20;
        flow.style.paddingTop = 22;
        flow.style.paddingBottom = 34;
        flow.style.flexShrink = 0;
        scroll.Add(flow);
        Refresh(null, null, null, null);
    }

    internal void Refresh(EventSO currentAsset, EventStepListModel currentModel, string phasePath, string featurePath)
    {
        bool newAsset = asset != currentAsset;
        bool rebuild = newAsset || !ReferenceEquals(model, currentModel) || flow.childCount == 0;
        bool changedSelection = selectedPhase != phasePath || selectedFeature != featurePath;
        selectedPhase = phasePath; selectedFeature = featurePath;
        if (!rebuild)
        {
            UpdateSelection();
            if (changedSelection) RevealSelection();
            return;
        }
        Vector2 offset = newAsset ? Vector2.zero : scroll.scrollOffset;
        asset = currentAsset; model = currentModel;
        int version = ++revision;
        flow.Clear(); phaseElements.Clear(); featureElements.Clear();

        if (asset == null)
        {
            flow.Add(EmptyState("이벤트를 선택하세요", "왼쪽 라이브러리에서 이벤트를 열면 시작 조건과 Phase 흐름이 여기에 표시됩니다."));
        }
        else
        {
            flow.Add(BuildEntry());
            if (model == null || model.Phases.Count == 0)
            {
                flow.Add(Connector(null, null));
                var empty = EmptyState("첫 번째 Phase부터 시작하세요", "Phase 안에 기능을 추가하고, 연출이 끝난 뒤 실행할 다음 Phase를 연결하세요.");
                var actions = Row("ew-flow-empty-actions");
                actions.style.justifyContent = Justify.Center;
                actions.Add(Command("+ 첫 Phase", () => addPhase?.Invoke(null), "ew-flow-primary"));
                Button feature = null;
                feature = Command("+ 기능으로 시작", () => addFeature?.Invoke(null, feature.worldBound), "ew-flow-secondary");
                actions.Add(feature); empty.Add(actions); flow.Add(empty);
            }
            else
            {
                var rows = model.Rows.GroupBy(row => row.Phase.Path).ToDictionary(group => group.Key, group => group.ToList());
                EventEditorPhase previous = null;
                foreach (EventEditorPhase phase in model.Phases)
                {
                    flow.Add(Connector(previous, phase));
                    rows.TryGetValue(phase.Path, out var features);
                    flow.Add(BuildPhase(phase, features ?? new List<EventEditorRow>()));
                    previous = phase;
                }
                var tail = Row("ew-flow-end");
                tail.style.justifyContent = Justify.Center;
                tail.Add(Text("●", "ew-flow-end-dot"));
                tail.Add(Text("각 실행 경로의 끝", "ew-flow-muted"));
                tail.style.marginTop = 20;
                flow.Add(tail);
            }
            if (model != null && model.Warnings.Count > 0) flow.Add(BuildWarnings());
        }
        UpdateSelection();
        schedule.Execute(() =>
        {
            if (version != revision) return;
            scroll.scrollOffset = offset;
            if (changedSelection && !newAsset) RevealSelection();
        });
    }

    private VisualElement BuildEntry()
    {
        var entry = new VisualElement { name = "event-entry" };
        entry.AddToClassList("ew-flow-entry");
        entry.style.paddingLeft = 17; entry.style.paddingRight = 17;
        entry.style.paddingTop = 14; entry.style.paddingBottom = 14;
        entry.style.flexShrink = 0;
        var top = Row("ew-flow-entry-header");
        top.Add(Text("시작", "ew-flow-entry-badge"));
        var title = Text(asset.name, "ew-flow-entry-title");
        title.style.flexGrow = 1; title.style.minWidth = 0;
        title.style.overflow = Overflow.Hidden; title.style.textOverflow = TextOverflow.Ellipsis;
        title.tooltip = asset.name;
        top.Add(title);
        top.Add(Text(asset.UseCondition ? "조건 분기" : "직접 호출", "ew-flow-chip"));
        entry.Add(top);
        string detail = asset.UseCondition
            ? "조건 " + (asset.Conditions?.Length ?? 0) + "개를 위에서부터 확인하고, 처음 맞는 경로로 진입합니다."
            : "씬의 트리거나 외부 호출로 시작합니다.";
        var description = Text(detail, "ew-flow-description");
        description.style.whiteSpace = WhiteSpace.Normal;
        description.style.marginTop = 8;
        entry.Add(description);
        var hint = Text("일반 연출은 Phase 안에서 함께 시작 · 선택지와 Action은 연출 뒤에 진행", "ew-flow-muted");
        hint.style.whiteSpace = WhiteSpace.Normal; hint.style.marginTop = 5;
        entry.Add(hint);
        var edit = Command("시작 조건 설정", () => select?.Invoke(null, null), "ew-flow-link");
        edit.style.alignSelf = Align.FlexStart; edit.style.marginTop = 9;
        entry.Add(edit);
        return entry;
    }

    private VisualElement BuildPhase(EventEditorPhase phase, List<EventEditorRow> allRows)
    {
        EventSO owner = asset;
        EventSO.EventStep originalStep = phase.Source;
        bool IsCurrent() => IsCurrentPhase(owner, phase.Path, originalStep);
        var card = new VisualElement { name = "phase-" + phaseElements.Count, focusable = true };
        card.AddToClassList("ew-flow-phase");
        card.EnableInClassList("ew-flow-phase--inactive", !phase.Reachable);
        card.style.marginLeft = Indent(phase);
        card.style.flexShrink = 0;
        card.tooltip = phase.Condition;
        phaseElements[phase.Path] = card;
        var features = allRows.Where(row => row.FeaturePath != phase.Path).ToList();

        var header = Row("ew-flow-phase-header");
        header.style.paddingLeft = 13; header.style.paddingRight = 10;
        header.style.paddingTop = 11; header.style.paddingBottom = 10;
        header.Add(Text("PHASE " + (phase.Depth + 1).ToString("00"), "ew-flow-phase-badge"));
        var route = Text(Route(phase), "ew-flow-route");
        route.style.flexGrow = 1; route.style.minWidth = 0;
        route.style.overflow = Overflow.Hidden; route.style.textOverflow = TextOverflow.Ellipsis;
        route.tooltip = phase.Label;
        header.Add(route);
        header.Add(Text(PhaseDuration(phase), "ew-flow-duration"));
        if (!phase.Reachable) header.Add(Text("미실행", "ew-flow-inactive-badge"));
        card.Add(header);

        var condition = new VisualElement();
        condition.AddToClassList("ew-flow-condition");
        condition.style.paddingLeft = 14; condition.style.paddingRight = 14;
        condition.style.paddingTop = 8; condition.style.paddingBottom = 9;
        var conditionText = Text(LocalCondition(phase), "ew-flow-condition-text");
        conditionText.style.whiteSpace = WhiteSpace.Normal;
        conditionText.tooltip = phase.Condition;
        condition.Add(conditionText);
        card.Add(condition);

        var body = new VisualElement(); body.AddToClassList("ew-flow-features");
        foreach (EventEditorRow feature in features) body.Add(BuildFeature(feature));
        if (features.Count == 0)
        {
            var empty = Text(phase.Source == null ? "비어 있는 단계입니다. 기능을 추가해 구성하세요." : "등록된 실행 기능이 없습니다.", "ew-flow-phase-empty");
            empty.style.paddingLeft = 14; empty.style.paddingRight = 14;
            empty.style.paddingTop = 19; empty.style.paddingBottom = 19;
            empty.style.whiteSpace = WhiteSpace.Normal;
            body.Add(empty);
        }
        card.Add(body);

        var footer = Row("ew-flow-phase-footer");
        footer.style.paddingLeft = 10; footer.style.paddingRight = 10;
        footer.style.paddingTop = 8; footer.style.paddingBottom = 8;
        footer.Add(Text(features.Count + "개 기능", "ew-flow-muted"));
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; footer.Add(spacer);
        Button add = null;
        add = Command("+ 기능", () => { if (IsCurrent()) addFeature?.Invoke(phase.Path, add.worldBound); }, "ew-flow-add-feature");
        footer.Add(add);
        footer.Add(Command("+ 다음 Phase", () => { if (IsCurrent()) addPhase?.Invoke(phase.Path); }, "ew-flow-add-phase"));
        card.Add(footer);
        BindSelection(card, phase.Path, null, IsCurrent);
        card.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            if (!IsCurrent()) return;
            evt.menu.AppendAction("기능 추가…", _ => { if (IsCurrent()) addFeature?.Invoke(phase.Path, card.worldBound); });
            evt.menu.AppendAction("다음 Phase 삽입", _ => { if (IsCurrent()) addPhase?.Invoke(phase.Path); });
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("이 Phase 선택", _ => { if (IsCurrent()) Choose(phase.Path, null); });
            evt.StopPropagation();
        }));
        return card;
    }

    private VisualElement BuildFeature(EventEditorRow feature)
    {
        EventSO owner = asset;
        EventSO.EventStep originalStep = feature.Phase.Source;
        bool IsCurrent() => IsCurrentPhase(owner, feature.Phase.Path, originalStep);
        var row = Row("ew-flow-feature");
        row.focusable = true;
        row.style.minHeight = 58;
        row.style.paddingLeft = 13; row.style.paddingRight = 12;
        row.style.paddingTop = 9; row.style.paddingBottom = 9;
        row.tooltip = feature.Title + "\n대상: " + feature.Target + "\n" + feature.Content + "\n" + feature.TimeNote;
        featureElements[feature.FeaturePath] = row;
        var marker = new VisualElement(); marker.AddToClassList("ew-flow-category-marker");
        marker.AddToClassList(CategoryClass(feature.Category));
        marker.style.backgroundColor = CategoryColor(feature.Category);
        marker.style.width = 3; marker.style.alignSelf = Align.Stretch;
        marker.style.marginRight = 10;
        row.Add(marker);
        var content = new VisualElement(); content.style.flexGrow = 1; content.style.minWidth = 0;
        var title = Text(feature.Title, "ew-flow-feature-title");
        title.style.overflow = Overflow.Hidden; title.style.textOverflow = TextOverflow.Ellipsis;
        content.Add(title);
        string target = string.IsNullOrWhiteSpace(feature.Target) || feature.Target == "—" ? "" : feature.Target;
        string summary = string.IsNullOrWhiteSpace(feature.Content) ? target :
            target.Length == 0 ? feature.Content : target + " · " + feature.Content;
        var detail = Text(summary.Replace('\n', ' '), "ew-flow-feature-detail");
        detail.style.marginTop = 4; detail.style.overflow = Overflow.Hidden;
        detail.style.textOverflow = TextOverflow.Ellipsis;
        content.Add(detail); row.Add(content);
        var time = Text(FeatureDuration(feature), "ew-flow-feature-time");
        time.style.marginLeft = 10; time.style.minWidth = 52;
        time.style.unityTextAlign = TextAnchor.MiddleRight;
        time.tooltip = feature.TimeNote;
        row.Add(time);
        BindSelection(row, feature.Phase.Path, feature.FeaturePath, IsCurrent);
        row.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            if (!IsCurrent()) return;
            evt.menu.AppendAction("이 기능 선택", _ => { if (IsCurrent()) Choose(feature.Phase.Path, feature.FeaturePath); });
            evt.menu.AppendAction("같은 Phase에 기능 추가…", _ => { if (IsCurrent()) addFeature?.Invoke(feature.Phase.Path, row.worldBound); });
            evt.menu.AppendAction("뒤에 다음 Phase 삽입", _ => { if (IsCurrent()) addPhase?.Invoke(feature.Phase.Path); });
            evt.StopPropagation();
        }));
        return row;
    }

    private VisualElement Connector(EventEditorPhase previous, EventEditorPhase next)
    {
        var connector = Row("ew-flow-connector");
        connector.style.height = previous != null && next != null && next.Depth <= previous.Depth ? 48 : 35;
        connector.style.flexShrink = 0;
        connector.style.marginLeft = next == null ? 15 : Indent(next) + 15;
        bool alternative = previous != null && next != null && next.ContextPath != previous.Path + ".EventExe";
        bool inactive = next != null && !next.Reachable;
        connector.EnableInClassList("ew-flow-connector--alternative", alternative);
        connector.EnableInClassList("ew-flow-connector--inactive", inactive);
        var rail = new VisualElement(); rail.AddToClassList("ew-flow-rail");
        rail.style.width = 20; rail.style.height = Length.Percent(100); rail.style.alignItems = Align.Center;
        var line = new VisualElement(); line.AddToClassList("ew-flow-line");
        line.style.width = 2; line.style.flexGrow = 1;
        if (!alternative) rail.Add(line);
        rail.Add(Text(alternative ? "↳" : "↓", "ew-flow-arrow"));
        connector.Add(rail);
        string caption = alternative ? inactive ? "미연결 저장 경로" : "다른 분기 · 한 경로만 실행" :
            inactive ? "저장된 연결 · 현재는 실행하지 않음" : previous == null ? "이벤트 진입" :
            previous.Source?.EventExe?.UseCondition == true ? "조건을 확인해 다음 경로 선택" : "이 Phase가 끝나면";
        var label = Text(caption, "ew-flow-connector-label");
        label.style.marginLeft = 9;
        connector.Add(label);
        return connector;
    }

    private VisualElement BuildWarnings()
    {
        var warning = new VisualElement(); warning.AddToClassList("ew-flow-warning");
        warning.style.marginTop = 22;
        warning.style.paddingLeft = 13; warning.style.paddingRight = 13;
        warning.style.paddingTop = 11; warning.style.paddingBottom = 11;
        warning.Add(Text("연결 확인 · " + model.Warnings.Count, "ew-flow-warning-title"));
        foreach (string message in model.Warnings)
        {
            var text = Text(message, "ew-flow-warning-text");
            text.style.whiteSpace = WhiteSpace.Normal; text.style.marginTop = 5;
            warning.Add(text);
        }
        return warning;
    }

    private bool IsCurrentPhase(EventSO owner, string phasePath, EventSO.EventStep originalStep)
        => owner != null && owner == asset &&
            ReferenceEquals(EventAuthoring.GetStep(owner, phasePath), originalStep);

    private void BindSelection(VisualElement element, string phasePath, string featurePath, Func<bool> isCurrent)
    {
        element.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || !isCurrent() || HasButtonTarget(evt.target as VisualElement, element)) return;
            Choose(phasePath, featurePath); element.Focus(); evt.StopPropagation();
        });
        element.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.target != element || !isCurrent() || evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
            Choose(phasePath, featurePath); evt.StopPropagation();
        });
    }

    private static bool HasButtonTarget(VisualElement target, VisualElement stop)
    {
        while (target != null && target != stop)
        {
            if (target is Button) return true;
            target = target.parent;
        }
        return false;
    }

    private void Choose(string phasePath, string featurePath)
    {
        selectedPhase = phasePath; selectedFeature = featurePath;
        UpdateSelection(); select?.Invoke(phasePath, featurePath);
    }

    private void UpdateSelection()
    {
        foreach (var pair in phaseElements) pair.Value.EnableInClassList("ew-flow-phase--selected", pair.Key == selectedPhase);
        foreach (var pair in featureElements) pair.Value.EnableInClassList("ew-flow-feature--selected", pair.Key == selectedFeature);
    }

    internal void RevealSelection()
    {
        VisualElement target = null;
        if (!string.IsNullOrEmpty(selectedFeature)) featureElements.TryGetValue(selectedFeature, out target);
        if (target == null && !string.IsNullOrEmpty(selectedPhase)) phaseElements.TryGetValue(selectedPhase, out target);
        if (target != null && target.panel != null) scroll.ScrollTo(target);
    }

    private static VisualElement EmptyState(string title, string description)
    {
        var empty = new VisualElement(); empty.AddToClassList("ew-flow-empty");
        empty.style.paddingLeft = 24; empty.style.paddingRight = 24;
        empty.style.paddingTop = 30; empty.style.paddingBottom = 30;
        var heading = Text(title, "ew-flow-empty-title"); heading.style.unityTextAlign = TextAnchor.MiddleCenter;
        heading.style.whiteSpace = WhiteSpace.Normal; empty.Add(heading);
        var detail = Text(description, "ew-flow-empty-detail"); detail.style.marginTop = 10;
        detail.style.whiteSpace = WhiteSpace.Normal; detail.style.unityTextAlign = TextAnchor.MiddleCenter;
        empty.Add(detail); return empty;
    }

    private static VisualElement Row(string className)
    {
        var row = new VisualElement(); row.AddToClassList(className);
        row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
        return row;
    }

    private static Label Text(string value, string className)
    {
        var label = new Label(value ?? string.Empty); label.AddToClassList(className); return label;
    }

    private static Button Command(string title, Action action, string className)
    {
        var button = new Button(action) { text = title };
        button.AddToClassList("ew-flow-command"); button.AddToClassList(className);
        return button;
    }

    private static float Indent(EventEditorPhase phase) => Math.Min(phase.Depth, 3) * 12;
    private static string Route(EventEditorPhase phase)
    {
        int suffix = phase.Label.LastIndexOf(" / Phase ", StringComparison.Ordinal);
        return suffix >= 0 ? phase.Label.Substring(0, suffix) : phase.Label;
    }

    private static string LocalCondition(EventEditorPhase phase)
    {
        string condition = phase.Condition ?? "";
        int arrow = condition.LastIndexOf("\n→ ", StringComparison.Ordinal);
        string local = arrow >= 0 ? condition.Substring(arrow + 3) : condition;
        return phase.Reachable ? local : "미실행 · " + local;
    }

    private static string PhaseDuration(EventEditorPhase phase)
    {
        if (!phase.Reachable) return "저장됨";
        if (phase.Timing == null) return "대기 없음";
        if (phase.Timing.DurationVariable) return "가변 시간";
        return phase.Timing.Duration <= .000001 ? "즉시" : Seconds(phase.Timing.Duration);
    }

    private static string FeatureDuration(EventEditorRow row)
    {
        if (!row.Phase.Reachable) return "미실행";
        var item = row.Phase.Timing?.Items.FirstOrDefault(value => value.PropertyPath == row.FeaturePath);
        if (item == null) return row.End ?? "—";
        if (row.End == "Disable까지") return "해제까지";
        if (item.OpenEnded) return row.End ?? "별도 종료";
        if (item.Variable) return "가변";
        return item.End - item.Start <= .000001 ? "즉시" : Seconds(item.End - item.Start);
    }

    private static string Seconds(double seconds) => seconds.ToString("0.##", CultureInfo.InvariantCulture) + "초";
    private static string CategoryClass(string category)
    {
        switch (category)
        {
            case "카메라": return "ew-flow-category-camera";
            case "대사와 안내": return "ew-flow-category-speech";
            case "이동과 배치": return "ew-flow-category-transform";
            case "오브젝트": return "ew-flow-category-object";
            case "씬": return "ew-flow-category-scene";
            case "화면과 사운드": return "ew-flow-category-screen";
            case "컴포넌트와 효과": return "ew-flow-category-effect";
            default: return "ew-flow-category-state";
        }
    }

    private static Color CategoryColor(string category)
    {
        switch (category)
        {
            case "카메라": return new Color(.35f, .69f, .96f);
            case "대사와 안내": return new Color(.85f, .64f, .34f);
            case "이동과 배치": return new Color(.35f, .76f, .61f);
            case "오브젝트": return new Color(.62f, .68f, .84f);
            case "씬": return new Color(.65f, .57f, .93f);
            case "화면과 사운드": return new Color(.91f, .55f, .45f);
            case "컴포넌트와 효과": return new Color(.79f, .58f, .80f);
            default: return new Color(.57f, .65f, .75f);
        }
    }
}
