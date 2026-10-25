using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>Responsive, read-only execution inventory for the Event workspace.</summary>
internal sealed class EventWorkspaceOverview : VisualElement
{
    static readonly Color Surface = new Color(.078f, .094f, .125f);
    static readonly Color Raised = new Color(.108f, .129f, .165f);
    static readonly Color Line = new Color(.18f, .215f, .27f);
    static readonly Color Ink = new Color(.87f, .90f, .95f);
    static readonly Color Muted = new Color(.56f, .63f, .74f);
    static readonly Color Accent = new Color(.39f, .68f, 1f);
    static readonly Color Selected = new Color(.13f, .24f, .37f);
    readonly Action<string, string> select;
    readonly Label title, mode, totals, invocation, shownCount;
    readonly TextField search;
    readonly DropdownField categories;
    readonly Foldout sourceFoldout, warningsFoldout;
    readonly ScrollView sourceList, inventory;
    readonly VisualElement tableHeader, warningList;
    readonly ScrollView page;
    readonly Button clearSearch;
    readonly List<VisualElement> responsiveRows = new List<VisualElement>();
    readonly List<Label> compactCaptions = new List<Label>();
    readonly List<VisualElement> cells = new List<VisualElement>();
    EventSO asset, indexedAsset;
    EventStepListModel model;
    List<EventInvocationReference> sources = new List<EventInvocationReference>();
    string selectedPhase, selectedFeature;
    int indexedSceneStamp = int.MinValue;
    bool compact, changingFilter;

    internal EventWorkspaceOverview(Action<string, string> select)
    {
        this.select = select;
        name = "ew-overview";
        AddToClassList("ew-overview");
        style.flexGrow = 1;
        style.flexShrink = 1;
        style.minWidth = 0;
        style.minHeight = 0;
        style.backgroundColor = Surface;
        style.color = Ink;
        style.paddingLeft = 20;
        style.paddingRight = 20;
        style.paddingTop = 12;
        style.paddingBottom = 8;

        var heading = Box("ew-overview-heading");
        heading.style.flexDirection = FlexDirection.Row;
        heading.style.alignItems = Align.Center;
        heading.style.flexWrap = Wrap.Wrap;
        heading.style.flexShrink = 0;
        heading.style.marginBottom = 8;
        title = Text("실행 목록", 16, Ink, true);
        title.style.flexGrow = 0;
        title.style.marginRight = 10;
        mode = Text("", 10, Accent, true);
        mode.AddToClassList("ew-overview-mode");
        mode.style.backgroundColor = new Color(.12f, .20f, .30f);
        Pad(mode, 7, 3);
        Round(mode, 4);
        heading.Add(title);
        heading.Add(mode);
        Add(heading);

        totals = Text("", 11, Muted);
        totals.style.marginTop = 0;
        totals.style.marginBottom = 0;
        totals.style.marginRight = 8;
        totals.style.flexGrow = 1;
        totals.name = "ew-overview-totals";
        totals.style.flexShrink = 0;
        heading.Insert(1, totals);

        var invocationBox = Box("ew-overview-invocation");
        invocationBox.style.backgroundColor = Raised;
        invocationBox.style.borderLeftWidth = 3;
        invocationBox.style.borderLeftColor = Accent;
        invocationBox.style.flexShrink = 0;
        Pad(invocationBox, 10, 6);
        Round(invocationBox, 5);
        invocationBox.Add(Text("실행 방식", 10, Accent, true));
        invocation = Text("EventSO를 선택하면 호출 방식과 실행 경로를 표시합니다.", 12, Ink);
        invocation.name = "ew-overview-invocation-summary";
        invocation.style.marginTop = 3;
        invocation.style.flexShrink = 0;
        invocation.style.height = StyleKeyword.Auto;
        invocationBox.Add(invocation);
        Add(invocationBox);

        sourceFoldout = new Foldout { text = "현재 열린 씬의 진입점", value = false };
        sourceFoldout.name = "ew-overview-sources";
        sourceFoldout.AddToClassList("ew-overview-sources");
        sourceFoldout.style.marginTop = 4;
        sourceFoldout.style.marginBottom = 4;
        sourceFoldout.style.color = Muted;
        sourceFoldout.style.flexShrink = 0;
        var sourceTools = Box("ew-overview-source-tools");
        sourceTools.style.flexDirection = FlexDirection.Row;
        sourceTools.style.alignItems = Align.Center;
        var sourceHint = Text("열린 씬의 저장 참조입니다. 실제 호출 여부는 각 연결의 설명을 확인하세요.", 11, Muted);
        sourceHint.style.flexGrow = 1;
        sourceHint.style.marginRight = 8;
        sourceTools.Add(sourceHint);
        var rescan = SmallButton("다시 검색", () => { ReadSources(); RenderSources(); UpdateSummary(); });
        rescan.name = "ew-overview-rescan-sources";
        sourceTools.Add(rescan);
        sourceFoldout.Add(sourceTools);
        sourceList = Scroll("ew-overview-source-list");
        sourceList.style.maxHeight = 176;
        sourceList.style.marginTop = 6;
        sourceFoldout.Add(sourceList);
        sourceFoldout.Add(Text("동적 PlayEvent 호출은 자동 추적하지 않습니다. 키 조건은 입력 자체를 뜻합니다.", 10, Muted));
        Add(sourceFoldout);

        var tools = Box("ew-overview-tools");
        tools.style.flexDirection = FlexDirection.Row;
        tools.style.alignItems = Align.Center;
        tools.style.flexWrap = Wrap.Wrap;
        tools.style.marginBottom = 6;
        tools.style.flexShrink = 0;
        var searchBox = Box("ew-overview-search-box");
        searchBox.style.flexDirection = FlexDirection.Row;
        searchBox.style.alignItems = Align.Center;
        searchBox.style.backgroundColor = Raised;
        searchBox.style.flexGrow = 1;
        searchBox.style.flexShrink = 1;
        searchBox.style.minWidth = 140;
        searchBox.style.height = 30;
        searchBox.style.marginRight = 8;
        Round(searchBox, 4);
        var searchCaption = Text("검색", 11, Muted);
        searchCaption.style.marginLeft = 10;
        searchCaption.style.marginRight = 6;
        searchBox.Add(searchCaption);
        search = new TextField { name = "ew-overview-search", tooltip = "Phase, 실행 조건, 기능, 대상 또는 내용으로 검색" };
        search.AddToClassList("ew-overview-search");
        search.style.flexGrow = 1;
        search.style.flexShrink = 1;
        search.style.minWidth = 0;
        search.style.marginLeft = 0;
        search.style.marginRight = 4;
        StyleInput(search);
        search.RegisterValueChangedCallback(_ => { clearSearch.SetEnabled(!string.IsNullOrEmpty(search.value)); RenderInventory(); });
        searchBox.Add(search);
        clearSearch = SmallButton("×", () => search.value = "");
        clearSearch.name = "ew-overview-clear-search";
        clearSearch.tooltip = "검색어 지우기";
        clearSearch.style.minWidth = 24;
        clearSearch.style.paddingLeft = clearSearch.style.paddingRight = 3;
        clearSearch.SetEnabled(false);
        searchBox.Add(clearSearch);
        tools.Add(searchBox);
        categories = new DropdownField(new List<string> { "모든 기능" }, 0) { name = "ew-overview-category" };
        categories.AddToClassList("ew-overview-category");
        categories.style.width = 120;
        categories.style.height = 30;
        categories.style.marginLeft = categories.style.marginRight = 0;
        StyleInput(categories);
        categories.RegisterValueChangedCallback(_ => { if (!changingFilter) RenderInventory(); });
        tools.Add(categories);
        shownCount = Text("", 10, Muted);
        shownCount.name = "ew-overview-visible-count";
        shownCount.style.marginLeft = 8;
        shownCount.style.flexShrink = 0;
        tools.Add(shownCount);
        Add(tools);

        tableHeader = Box("ew-overview-table-header");
        tableHeader.style.flexDirection = FlexDirection.Row;
        tableHeader.style.flexShrink = 0;
        tableHeader.style.backgroundColor = Raised;
        tableHeader.style.borderTopWidth = tableHeader.style.borderBottomWidth = 1;
        tableHeader.style.borderTopColor = tableHeader.style.borderBottomColor = Line;
        string[] headings = { "PHASE", "실행 조건", "기능 · 대상", "시작 → 종료" };
        for (int i = 0; i < headings.Length; i++)
        {
            var cell = HeaderCell(i);
            cell.Add(Text(headings[i], 10, Muted, true));
            tableHeader.Add(cell);
        }
        Add(tableHeader);
        inventory = Scroll("ew-overview-inventory");
        inventory.style.flexGrow = 1;
        inventory.style.flexShrink = 1;
        inventory.style.minHeight = 0;
        inventory.style.minWidth = 0;
        Add(inventory);

        warningsFoldout = new Foldout { text = "실행 경로 확인", value = false };
        warningsFoldout.name = "ew-overview-warnings";
        warningsFoldout.style.color = new Color(.94f, .72f, .40f);
        warningsFoldout.style.marginTop = 8;
        warningsFoldout.style.flexShrink = 0;
        var warningScroll = Scroll("ew-overview-warning-scroll");
        warningScroll.style.maxHeight = 112;
        warningList = Box("ew-overview-warning-list");
        warningScroll.Add(warningList);
        warningsFoldout.Add(warningScroll);
        Add(warningsFoldout);
        var timingHint = Text("경로 호출 시점 기준 · 가변/입력 대기는 확정 시간이 아닙니다.", 10, Muted);
        timingHint.tooltip = "시간은 각 경로의 호출 시점을 0초로 표시합니다. 가변·입력 대기·Disable까지는 확정된 종료 시각이 아닙니다.";
        timingHint.style.marginTop = 5;
        timingHint.style.flexShrink = 0;
        Add(timingHint);

        // Headers retain their natural wrapped height. On short windows the page
        // itself scrolls, rather than compressing labels or taking the entire list.
        var sections = Children().ToArray();
        page = Scroll("ew-overview-page");
        page.style.flexGrow = 1;
        page.style.flexShrink = 1;
        page.style.minHeight = 0;
        page.contentContainer.style.flexGrow = 1;
        page.contentContainer.style.flexShrink = 0;
        page.contentContainer.style.minHeight = new Length(100, LengthUnit.Percent);
        foreach (var section in sections)
        {
            section.RemoveFromHierarchy(); page.Add(section);
            if (section != inventory) section.RegisterCallback<GeometryChangedEvent>(_ => FitInventoryHeight());
        }
        Add(page);
        page.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => FitInventoryHeight());
        RegisterCallback<GeometryChangedEvent>(evt => {
            SetCompact(evt.newRect.width < 660);
            FitInventoryHeight();
        });
    }

    internal void Refresh(EventSO asset, EventStepListModel model, string selectedPhase, string selectedFeature)
    {
        bool different = this.asset != asset;
        this.asset = asset;
        this.model = model;
        this.selectedPhase = selectedPhase;
        this.selectedFeature = selectedFeature;
        if (different || indexedAsset != asset || indexedSceneStamp != SceneStamp()) ReadSources();
        string previous = categories.value;
        var choices = new List<string> { "모든 기능" };
        if (model != null) choices.AddRange(model.Rows.Select(row => row.Category ?? "흐름").Distinct().OrderBy(value => value, StringComparer.Ordinal));
        changingFilter = true;
        categories.choices = choices;
        categories.SetValueWithoutNotify(choices.Contains(previous) ? previous : choices[0]);
        changingFilter = false;
        UpdateSummary();
        RenderSources();
        RenderInventory();
        warningList.Clear();
        var warnings = model?.Warnings.Distinct().ToArray() ?? Array.Empty<string>();
        warningsFoldout.text = "실행 경로 확인 · " + warnings.Length;
        warningsFoldout.style.display = warnings.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        foreach (string warning in warnings)
        {
            var label = Text("• " + warning, 11, new Color(.94f, .76f, .51f));
            label.style.marginBottom = 5;
            warningList.Add(label);
        }
    }

    internal void InvalidateSources() { indexedSceneStamp = int.MinValue; }

    void FitInventoryHeight()
    {
        if (page == null || page.panel == null) return;
        float height = page.contentViewport.resolvedStyle.height;
        if (float.IsNaN(height) || height <= 0) return;
        foreach (var section in page.contentContainer.Children())
        {
            if (section == inventory || section.resolvedStyle.display == DisplayStyle.None) continue;
            var resolved = section.resolvedStyle;
            if (!float.IsNaN(resolved.height)) height -= resolved.height + resolved.marginTop + resolved.marginBottom;
        }
        // A small viewport scrolls the page, with a bounded inner list still usable.
        float listHeight = Mathf.Max(200, height);
        if (Mathf.Abs(inventory.resolvedStyle.height - listHeight) > .5f)
            inventory.style.height = listHeight;
    }

    void UpdateSummary()
    {
        title.text = "실행 목록";
        mode.text = asset == null ? "EVENT WORKSPACE" : asset.UseCondition ? "CONDITION" : "DIRECT CALL";
        int phaseCount = model?.Phases.Count ?? 0;
        int actionCount = model?.Rows.Count(row => row.FeaturePath != row.Phase.Path) ?? 0;
        int branches = model?.Phases.Count(phase => phase.Depth == 0 && phase.Reachable) ?? 0;
        totals.text = branches + " 경로 · " + phaseCount + " Phase · " + actionCount + " 기능";
        invocation.text = InvocationSummary();
        invocation.tooltip = asset != null ? EventInvocationIndex.AutomaticRegistration(asset) : "";
        sourceFoldout.text = "호출·등록 상세 · 열린 씬 " + sources.Count + "개 참조";
        sourceFoldout.style.display = asset != null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void ReadSources()
    {
        sources = asset != null ? EventInvocationIndex.ScanLoadedScenes(asset) : new List<EventInvocationReference>();
        indexedAsset = asset;
        indexedSceneStamp = SceneStamp();
    }

    string InvocationSummary()
    {
        if (asset == null) return "EventSO를 선택하면 호출 방식과 실행 경로를 표시합니다.";
        if (!asset.UseCondition) return "직접 호출 · 씬 트리거 또는 PlayEvent 호출로 시작합니다.";
        int count = asset.Conditions?.Length ?? 0;
        if (count == 0) return "조건 분기 · 조건이 비어 있어 자동감시로 시작하지 않습니다. 직접 호출 경로를 확인하세요.";
        string registration = EventInvocationIndex.AutomaticRegistration(asset);
        if (!registration.StartsWith("자동감시 등록 폴더:", StringComparison.Ordinal))
            return "조건 분기 · 현재 자동감시 등록 대상이 아닙니다.\n씬 트리거 또는 PlayEvent 호출 시 처음 맞는 분기로 실행됩니다.";
        int ready = asset.Conditions.Count(condition => EventStepListModel.AutoConditionIssue(condition) == null);
        if (ready == 0) return "조건 분기 · 모든 조건이 미완성이므로 자동감시로 시작하지 않습니다. 상세에서 제외 사유를 확인하세요.";
        return "씬 조건 감시 · 완성된 조건 " + ready + "/" + count + "개를 감시합니다.\n씬 로드당 1회 · Else는 직접 호출에서만 시작합니다.";
    }

    void RenderSources()
    {
        sourceList.Clear();
        if (asset != null)
        {
            var registration = Text(EventInvocationIndex.AutomaticRegistration(asset), 11, Ink);
            registration.style.marginBottom = 9;
            registration.style.paddingBottom = 8;
            registration.style.borderBottomWidth = 1;
            registration.style.borderBottomColor = Line;
            sourceList.Add(registration);
        }
        if (sources.Count == 0)
        {
            var empty = Text("현재 열린 씬에서 이 EventSO를 참조하는 컴포넌트를 찾지 못했습니다. 다른 씬이나 코드에서 호출하는지는 별도로 확인하세요.", 11, Muted);
            Pad(empty, 4, 7);
            sourceList.Add(empty);
            return;
        }
        foreach (var source in sources)
        {
            var row = Box("ew-overview-source-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Line;
            Pad(row, 4, 9);
            var copy = Box("ew-overview-source-copy");
            copy.style.flexGrow = 1;
            copy.style.flexShrink = 1;
            copy.style.minWidth = 0;
            copy.Add(Text(source.Description ?? "저장 참조", 12, Ink, true));
            var location = Text(source.Location ?? "", 10, Muted);
            location.style.marginTop = 3;
            copy.Add(location);
            row.Add(copy);
            var find = SmallButton("씬에서 찾기", () => {
                if (source.Source == null) return;
                Selection.activeObject = source.Source;
                EditorGUIUtility.PingObject(source.Source);
            });
            find.SetEnabled(source.Source != null);
            find.style.marginLeft = 10;
            row.Add(find);
            sourceList.Add(row);
        }
    }

    void RenderInventory()
    {
        float previousY = inventory.scrollOffset.y;
        inventory.Clear();
        responsiveRows.Clear();
        compactCaptions.Clear();
        cells.Clear();
        string query = (search.value ?? "").Trim();
        string category = categories.value;
        var all = model?.Rows ?? new List<EventEditorRow>();
        var visible = all.Where(row => (category == "모든 기능" || (row.Category ?? "흐름") == category) &&
            (query.Length == 0 || Matches(row, query))).ToArray();
        shownCount.text = visible.Length + " / " + all.Count;
        for (int index = 0; index < visible.Length; index++)
        {
            var data = visible[index];
            bool exact = !string.IsNullOrEmpty(selectedFeature) && data.FeaturePath == selectedFeature;
            bool inPhase = data.Phase.Path == selectedPhase;
            Color normal = exact ? Selected : inPhase ? new Color(.105f, .16f, .225f) :
                index % 2 == 0 ? new Color(.085f, .105f, .14f) : Surface;
            var row = Box("ew-overview-row");
            row.name = "ew-overview-row-" + index;
            row.focusable = true;
            row.tabIndex = 0;
            row.style.flexShrink = 0;
            row.style.minWidth = 0;
            row.style.backgroundColor = normal;
            row.style.borderLeftWidth = 3;
            row.style.borderLeftColor = exact ? Accent : Color.clear;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Line;
            if (exact) row.AddToClassList("ew-overview-row-selected");
            if (!data.Phase.Reachable) row.AddToClassList("ew-overview-row-unreachable");
            row.tooltip = data.FeaturePath;
            void Pick()
            {
                selectedPhase = data.Phase.Path;
                selectedFeature = data.FeaturePath;
                select?.Invoke(selectedPhase, selectedFeature);
            }
            row.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0) { Pick(); evt.StopPropagation(); } });
            row.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;
                Pick(); evt.StopPropagation();
            });
            row.RegisterCallback<MouseEnterEvent>(_ => { if (!exact) row.style.backgroundColor = new Color(.12f, .17f, .23f); });
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = normal);
            row.RegisterCallback<FocusInEvent>(_ => row.style.borderLeftColor = Accent);
            row.RegisterCallback<FocusOutEvent>(_ => row.style.borderLeftColor = exact ? Accent : Color.clear);
            var phase = Cell("PHASE", 0);
            var number = Text((index + 1).ToString("00"), 10, Muted);
            number.style.marginBottom = 4;
            phase.Add(number);
            phase.Add(Text(data.Phase.Label ?? "Phase", 12, data.Phase.Reachable ? Ink : Muted, true));
            if (!data.Phase.Reachable)
            {
                var inactive = Text("미실행 저장 단계", 10, new Color(.85f, .62f, .39f));
                inactive.style.marginTop = 5;
                phase.Add(inactive);
            }
            row.Add(phase);
            var when = Cell("실행 조건", 1);
            when.Add(Text(data.Phase.Condition ?? "—", 11, data.Phase.Reachable ? Ink : Muted));
            row.Add(when);
            var action = Cell("기능 · 대상", 2);
            action.Add(Text(data.Title ?? "기능", 13, Ink, true));
            if (!string.IsNullOrEmpty(data.Target) && data.Target != "—")
            {
                var target = Text("→ " + data.Target, 11, Accent);
                target.style.marginTop = 5;
                action.Add(target);
            }
            if (!string.IsNullOrWhiteSpace(data.Content))
            {
                var content = Text(data.Content.Replace('\n', ' '), 11, Muted);
                content.style.marginTop = 5;
                action.Add(content);
            }
            row.Add(action);
            var timing = Cell("시작 → 종료", 3);
            timing.tooltip = data.TimeNote ?? "";
            timing.Add(Text(data.Start ?? "—", 12, Ink, true));
            var end = Text("→ " + (data.End ?? "—"), 11, Muted);
            end.style.marginTop = 5;
            timing.Add(end);
            row.Add(timing);
            responsiveRows.Add(row);
            inventory.Add(row);
        }
        if (visible.Length == 0)
        {
            var empty = Box("ew-overview-empty");
            Pad(empty, 20, 32);
            empty.Add(Text(query.Length > 0 || category != "모든 기능" ? "일치하는 기능이 없습니다" : "표시할 실행 단계가 없습니다", 16, Ink, true));
            var note = Text(query.Length > 0 || category != "모든 기능" ? "검색어 또는 기능 필터를 바꾸면 다른 항목을 볼 수 있습니다." :
                asset == null ? "EventSO를 선택해 실행 경로를 살펴보세요." : "단계 편집에서 Phase와 기능을 추가하세요.", 12, Muted);
            note.style.marginTop = 8;
            empty.Add(note);
            inventory.Add(empty);
        }
        ApplyResponsiveLayout();
        inventory.scrollOffset = new Vector2(0, previousY);
    }

    VisualElement Cell(string caption, int column)
    {
        var cell = Box("ew-overview-cell");
        cell.userData = column;
        Pad(cell, 10, 12);
        cell.style.minWidth = 0;
        cells.Add(cell);
        var label = Text(caption, 9, Muted, true);
        label.style.marginBottom = 5;
        label.AddToClassList("ew-overview-compact-caption");
        compactCaptions.Add(label);
        cell.Add(label);
        return cell;
    }

    static VisualElement HeaderCell(int column)
    {
        var cell = Box("ew-overview-header-cell");
        Pad(cell, 10, 7);
        ColumnWidth(cell, column);
        return cell;
    }

    void SetCompact(bool value)
    {
        if (compact == value) return;
        compact = value;
        EnableInClassList("ew-overview-compact", compact);
        ApplyResponsiveLayout();
    }

    void ApplyResponsiveLayout()
    {
        tableHeader.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
        foreach (var row in responsiveRows) row.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
        foreach (var caption in compactCaptions) caption.style.display = compact ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var cell in cells)
        {
            if (compact)
            {
                cell.style.flexBasis = StyleKeyword.Auto;
                cell.style.flexGrow = 0;
                cell.style.flexShrink = 0;
                cell.style.paddingTop = cell.style.paddingBottom = 7;
            }
            else
            {
                ColumnWidth(cell, (int)cell.userData);
                cell.style.paddingTop = cell.style.paddingBottom = 12;
            }
        }
    }

    static void ColumnWidth(VisualElement cell, int column)
    {
        float[] widths = { 17, 29, 37, 17 };
        cell.style.flexBasis = new Length(widths[column], LengthUnit.Percent);
        cell.style.flexGrow = 0;
        cell.style.flexShrink = 1;
        cell.style.minWidth = 0;
    }

    static bool Matches(EventEditorRow row, string query)
    {
        string text = (row.Phase.Label ?? "") + " " + (row.Phase.Condition ?? "") + " " + row.Title + " " + row.Target + " " + row.Content + " " + row.Category;
        return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int SceneStamp()
    {
        unchecked
        {
            int stamp = SceneManager.sceneCount;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                stamp = stamp * 397 ^ scene.handle.GetHashCode();
                stamp = stamp * 397 ^ (scene.isLoaded ? 1 : 0);
            }
            return stamp;
        }
    }

    static ScrollView Scroll(string name)
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = name };
        scroll.AddToClassList(name);
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        scroll.contentContainer.style.minWidth = 0;
        scroll.contentContainer.style.flexGrow = 0;
        scroll.contentContainer.style.flexShrink = 1;
        return scroll;
    }

    static VisualElement Box(string className)
    {
        var box = new VisualElement();
        box.AddToClassList(className);
        box.style.minWidth = 0;
        return box;
    }

    static Label Text(string value, int size, Color color, bool bold = false)
    {
        var label = new Label(value);
        label.AddToClassList("ew-overview-text");
        label.style.fontSize = size;
        label.style.color = color;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.overflow = Overflow.Hidden;
        label.style.minWidth = 0;
        label.style.flexShrink = 1;
        if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.pickingMode = PickingMode.Ignore;
        return label;
    }

    static Button SmallButton(string label, Action click)
    {
        var button = new Button(click) { text = label };
        button.AddToClassList("ew-overview-button");
        button.style.backgroundColor = Raised;
        button.style.color = Ink;
        button.style.fontSize = 11;
        button.style.minHeight = 26;
        button.style.flexShrink = 0;
        button.style.borderTopWidth = button.style.borderRightWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = 1;
        button.style.borderTopColor = button.style.borderRightColor = button.style.borderBottomColor = button.style.borderLeftColor = Line;
        Pad(button, 9, 4);
        Round(button, 4);
        return button;
    }

    static void StyleInput(VisualElement field)
    {
        var input = field.Q(className: "unity-base-field__input");
        if (input == null) return;
        input.style.backgroundColor = Raised;
        input.style.color = Ink;
        input.style.fontSize = 12;
        input.style.borderTopWidth = input.style.borderRightWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = 0;
        input.style.minWidth = 0;
        Round(input, 4);
    }

    static void Pad(VisualElement element, float horizontal, float vertical)
    {
        element.style.paddingLeft = element.style.paddingRight = horizontal;
        element.style.paddingTop = element.style.paddingBottom = vertical;
    }

    static void Round(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
