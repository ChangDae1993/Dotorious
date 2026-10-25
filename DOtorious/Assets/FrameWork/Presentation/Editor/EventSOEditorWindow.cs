using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A dedicated authoring workspace over the original EventSO serialized data.</summary>
public sealed class EventSOEditorWindow : EditorWindow
{
    [SerializeField] private EventSO currentEventSO;
    [SerializeField] private bool followSelection = true;
    [SerializeField] private int viewMode, inspectorMode, browserMode;
    [SerializeField] private string selectedPhasePath, selectedFeaturePath, phaseSearch = "", assetSearch = "";
    [SerializeField] private EventTimelineView timelineView = new EventTimelineView();
    private EventSOEditor embeddedEditor;
    private EventStepListModel model;
    private List<string> assetPaths;
    private VisualElement center, canvasHost, outline, library, contextBar, inspectorActions;
    private Label documentTitle, documentState, status, inspectorTitle, inspectorCaption;
    private ObjectField assetField;
    private ToolbarSearchField browserSearch;
    private ScrollView propertyScroll, outlineScroll, libraryScroll;
    private EventWorkspaceCanvas flow;
    private EventWorkspaceOverview overview;
    private IMGUIContainer propertyGUI, timelineGUI;
    private readonly List<Button> viewButtons = new List<Button>();
    private readonly List<Button> inspectorButtons = new List<Button>();
    private readonly List<Button> browserButtons = new List<Button>();
    private int lastDirtyCount = -1;
    private bool refreshPending;
    private string SessionKey => "EventSOEditor.WorkspaceTab." + Application.dataPath;

    [MenuItem("Window/FrameWork/Presentation", false, 2300)]
    public static void OpenWindow() { Open(Selection.activeObject as EventSO); }
    public static void Open(EventSO eventSO)
    {
        var window = GetWindow<EventSOEditorWindow>();
        window.titleContent = new GUIContent("Presentation");
        window.minSize = new Vector2(1040, 600);
        if (eventSO != null) window.SetTarget(eventSO); else window.TryUseSelection();
        window.Show(); window.Focus();
    }
    [OnOpenAsset]
#if UNITY_6000_5_OR_NEWER
    private static bool OpenEventSOAsset(EntityId entityId, int line)
    {
        var eventSO = EditorUtility.EntityIdToObject(entityId) as EventSO;
#else
    private static bool OpenEventSOAsset(int instanceId, int line)
    {
        var eventSO = EditorUtility.InstanceIDToObject(instanceId) as EventSO;
#endif
        if (eventSO == null) return false;
        Open(eventSO); return true;
    }
    private void OnEnable()
    {
        titleContent = new GUIContent("Presentation"); minSize = new Vector2(1040, 600);
        viewMode = Mathf.Clamp(SessionState.GetInt(SessionKey, viewMode), 0, 3);
        if (timelineView == null) timelineView = new EventTimelineView();
        timelineView.SelectionRequested = SelectOnly; timelineView.EditRequested = SelectForEditing;
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        if (!TryUseSelection()) RebuildEmbeddedEditor();
    }
    private void OnDisable()
    {
        SessionState.SetInt(SessionKey, viewMode);
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        timelineView.SelectionRequested = null; timelineView.EditRequested = null;
        DisposeEmbeddedEditor();
    }
    public void CreateGUI()
    {
        var root = rootVisualElement; root.Clear(); root.AddToClassList("ew-root");
        string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(System.IO.Path.GetDirectoryName(script).Replace('\\', '/') + "/EventWorkspace.uss");
        if (theme != null) root.styleSheets.Add(theme);
        viewButtons.Clear(); inspectorButtons.Clear(); browserButtons.Clear();
        DrawCommandBar(root);
        var workspace = new TwoPaneSplitView(0, 210, TwoPaneSplitViewOrientation.Horizontal);
        workspace.viewDataKey = "Presentation.ExplorerWidth";
        workspace.AddToClassList("ew-workspace"); root.Add(workspace);
        var left = new VisualElement(); left.AddToClassList("ew-browser"); left.style.minWidth = 175; workspace.Add(left); DrawBrowser(left);
        var work = new TwoPaneSplitView(1, 320, TwoPaneSplitViewOrientation.Horizontal);
        work.viewDataKey = "Presentation.PropertiesWidth";
        work.style.flexGrow = 1; workspace.Add(work);
        center = new VisualElement(); center.AddToClassList("ew-center"); center.style.minWidth = 380; work.Add(center); DrawCenter(center);
        var right = new VisualElement(); right.AddToClassList("ew-inspector"); right.style.minWidth = 280; work.Add(right); DrawInspector(right);
        status = new Label(); status.AddToClassList("ew-status"); root.Add(status);
        root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.actionKey && evt.keyCode == KeyCode.S) { Save(); evt.StopPropagation(); }
        });
        root.schedule.Execute(() =>
        {
            if (currentEventSO == null) return;
            int dirty = EditorUtility.GetDirtyCount(currentEventSO);
            if (lastDirtyCount != dirty) RefreshDocument(); else RefreshStatus();
        }).Every(350);
        RefreshDocument();
    }
    private static Label Text(string text, string className)
    { var label = new Label(text); label.AddToClassList(className); return label; }
    private static Button ActionButton(string text, Action action, string className = "ew-button")
    { var button = new Button(action) { text = text }; button.AddToClassList(className); return button; }
    private void DrawCommandBar(VisualElement root)
    {
        var bar = new VisualElement(); bar.AddToClassList("ew-commandbar"); root.Add(bar);
        var brand = Text("P", "ew-brand"); brand.tooltip = "Presentation · Event authoring"; bar.Add(brand);
        var identity = new VisualElement(); identity.AddToClassList("ew-document"); bar.Add(identity);
        identity.Add(Text("PRESENTATION  /  EVENT EDITOR", "ew-eyebrow"));
        documentTitle = Text("이벤트를 선택하세요", "ew-document-title"); identity.Add(documentTitle);
        documentState = Text("", "ew-save-state"); bar.Add(documentState);
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; bar.Add(spacer);
        var undo = ActionButton("↶", () => Undo.PerformUndo(), "ew-icon-button"); undo.tooltip = "실행 취소 · Ctrl+Z"; bar.Add(undo);
        var redo = ActionButton("↷", () => Undo.PerformRedo(), "ew-icon-button"); redo.tooltip = "다시 실행 · Ctrl+Y"; bar.Add(redo);
        var options = new ToolbarMenu { text = "파일  ▾" }; options.AddToClassList("ew-file-menu");
        options.menu.AppendAction("새 이벤트 만들기…", _ => CreateEvent());
        options.menu.AppendAction("Project에서 위치 찾기", _ => { if (currentEventSO != null) EditorGUIUtility.PingObject(currentEventSO); });
        options.menu.AppendAction("원본 전체 구조 편집", _ => SetInspectorMode(2)); bar.Add(options);
        bar.Add(ActionButton("저장  Ctrl+S", Save, "ew-primary-button"));
    }
    private void DrawBrowser(VisualElement left)
    {
        var head = new VisualElement(); head.AddToClassList("ew-section-head"); head.Add(Text("EXPLORER", "ew-eyebrow"));
        var plus = ActionButton("+", CreateEvent, "ew-icon-button"); plus.tooltip = "새 EventSO 만들기"; head.Add(plus); left.Add(head);
        assetField = new ObjectField { objectType = typeof(EventSO), allowSceneObjects = false }; assetField.AddToClassList("ew-asset-field");
        assetField.SetValueWithoutNotify(currentEventSO); assetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as EventSO)); left.Add(assetField);
        var follow = new Toggle("Project 선택 따라가기") { value = followSelection }; follow.AddToClassList("ew-follow");
        follow.RegisterValueChangedCallback(evt => { followSelection = evt.newValue; if (followSelection) TryUseSelection(); }); left.Add(follow);
        var tabs = new VisualElement(); tabs.AddToClassList("ew-small-tabs"); left.Add(tabs);
        for (int i = 0; i < 2; i++)
        {
            int index = i; var b = ActionButton(i == 0 ? "실행 구조" : "이벤트 파일", () => SetBrowserMode(index), "ew-small-tab"); browserButtons.Add(b); tabs.Add(b);
        }
        browserSearch = new ToolbarSearchField(); browserSearch.AddToClassList("ew-browser-search"); left.Add(browserSearch);
        browserSearch.SetValueWithoutNotify(browserMode == 0 ? phaseSearch : assetSearch);
        browserSearch.RegisterValueChangedCallback(evt => { if (browserMode == 0) phaseSearch = evt.newValue; else assetSearch = evt.newValue; RebuildBrowser(); });
        outlineScroll = new ScrollView(); outlineScroll.AddToClassList("ew-browser-scroll"); outline = outlineScroll.contentContainer; left.Add(outlineScroll);
        libraryScroll = new ScrollView(); libraryScroll.AddToClassList("ew-browser-scroll"); library = libraryScroll.contentContainer; left.Add(libraryScroll);
        var foot = new VisualElement(); foot.AddToClassList("ew-browser-foot"); foot.Add(Text("동시 실행 → 완료 후 다음 Phase", "ew-muted"));
        foot.Add(ActionButton("실행 시작 조건 설정", () => SetInspectorMode(1), "ew-button")); left.Add(foot);
    }
    private void DrawCenter(VisualElement parent)
    {
        var tabs = new VisualElement(); tabs.AddToClassList("ew-viewbar"); parent.Add(tabs);
        string[] titles = { "실행 흐름", "이벤트 목록", "타임라인", "전체 타임라인" };
        for (int i = 0; i < titles.Length; i++)
        { int index = i; var b = ActionButton(titles[i], () => SetView(index), "ew-view-tab"); viewButtons.Add(b); tabs.Add(b); }
        contextBar = new VisualElement(); contextBar.AddToClassList("ew-contextbar"); parent.Add(contextBar);
        canvasHost = new VisualElement(); canvasHost.AddToClassList("ew-canvas-host"); parent.Add(canvasHost);
        flow = new EventWorkspaceCanvas(SelectOnly, OpenFeaturePicker, AddNextPhase); overview = new EventWorkspaceOverview(SelectOnly);
        timelineGUI = new IMGUIContainer(() =>
        {
            if (currentEventSO == null) return;
            timelineView.Draw(currentEventSO, viewMode == 3, Mathf.Max(180, timelineGUI.contentRect.height - 5));
        });
        timelineGUI.AddToClassList("ew-timeline"); canvasHost.Add(flow); canvasHost.Add(overview); canvasHost.Add(timelineGUI);
    }
    private void DrawInspector(VisualElement right)
    {
        right.Add(Text("PROPERTIES", "ew-inspector-eyebrow")); inspectorTitle = Text("선택 속성", "ew-inspector-title"); right.Add(inspectorTitle);
        inspectorCaption = Text("흐름에서 기능을 선택하세요.", "ew-inspector-caption"); right.Add(inspectorCaption);
        inspectorActions = new VisualElement(); inspectorActions.AddToClassList("ew-property-actions"); right.Add(inspectorActions);
        var tabs = new VisualElement(); tabs.AddToClassList("ew-small-tabs"); right.Add(tabs);
        string[] labels = { "선택 항목", "시작 조건", "전체 구조" };
        for (int i = 0; i < labels.Length; i++)
        { int index = i; var b = ActionButton(labels[i], () => SetInspectorMode(index), "ew-small-tab"); inspectorButtons.Add(b); tabs.Add(b); }
        var inspectorBody = new VisualElement(); inspectorBody.AddToClassList("ew-property-body"); right.Add(inspectorBody);
        propertyScroll = new ScrollView(); propertyScroll.AddToClassList("ew-property-scroll"); inspectorBody.Add(propertyScroll);
        propertyGUI = new IMGUIContainer(DrawProperties); propertyGUI.AddToClassList("ew-property-gui"); propertyScroll.Add(propertyGUI);
        right.Add(Text("속성은 원본 EventSO에 반영됩니다.\n저장 전에도 Ctrl+Z로 되돌릴 수 있습니다.", "ew-inspector-tip"));
    }
    private void DrawProperties()
    {
        if (currentEventSO == null || embeddedEditor == null) return;
        float previousWidth = EditorGUIUtility.labelWidth;
        bool previousWide = EditorGUIUtility.wideMode;
        try
        {
            EditorGUIUtility.labelWidth = Mathf.Clamp(propertyGUI.contentRect.width * .40f, 100, 145);
            EditorGUIUtility.wideMode = propertyGUI.contentRect.width >= 300;
            EditorGUI.BeginChangeCheck();
            if (inspectorMode == 2) embeddedEditor.OnInspectorGUI();
            else if (inspectorMode == 1) embeddedEditor.DrawExecutionSettings();
            else if (!string.IsNullOrEmpty(selectedFeaturePath) && selectedFeaturePath != selectedPhasePath) embeddedEditor.DrawWorkspaceFeature(selectedPhasePath, selectedFeaturePath);
            else if (!string.IsNullOrEmpty(selectedPhasePath)) embeddedEditor.DrawWorkspacePhaseSettings(selectedPhasePath);
            else EditorGUILayout.HelpBox("가운데에서 Phase 또는 기능을 선택하세요.", MessageType.None);
            if (EditorGUI.EndChangeCheck()) RequestRefresh();
        }
        finally { EditorGUIUtility.labelWidth = previousWidth; EditorGUIUtility.wideMode = previousWide; }
    }
    private void RefreshDocument()
    {
        if (canvasHost == null) return;
        if (currentEventSO != null && (embeddedEditor == null || embeddedEditor.target != currentEventSO)) RebuildEmbeddedEditor();
        model = EventStepListModel.Build(currentEventSO);
        if (!model.Phases.Any(p => p.Path == selectedPhasePath)) { selectedPhasePath = model.Phases.FirstOrDefault()?.Path; selectedFeaturePath = null; }
        if (selectedFeaturePath != null && embeddedEditor != null && !embeddedEditor.TryGetWorkspaceFeatureInfo(selectedPhasePath, selectedFeaturePath, out _, out _, out _)) selectedFeaturePath = null;
        lastDirtyCount = currentEventSO != null ? EditorUtility.GetDirtyCount(currentEventSO) : -1;
        assetField?.SetValueWithoutNotify(currentEventSO); RefreshSelection(); RebuildBrowser(); RefreshStatus();
    }
    private void RefreshSelection()
    {
        if (canvasHost == null || model == null) return;
        for (int i = 0; i < viewButtons.Count; i++) viewButtons[i].EnableInClassList("is-active", i == viewMode);
        flow.style.display = viewMode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        overview.style.display = viewMode == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        timelineGUI.style.display = viewMode >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
        flow.Refresh(currentEventSO, model, selectedPhasePath, selectedFeaturePath); overview.Refresh(currentEventSO, model, selectedPhasePath, selectedFeaturePath);
        timelineView.SelectPhase(selectedPhasePath, selectedFeaturePath); RebuildContext(); RefreshInspectorHeader(); propertyGUI?.MarkDirtyRepaint(); timelineGUI?.MarkDirtyRepaint();
    }
    private void RebuildContext()
    {
        contextBar.Clear(); var phase = model.Phases.FirstOrDefault(p => p.Path == selectedPhasePath);
        var text = Text(phase?.Label ?? "이벤트 제작", "ew-context-title"); text.tooltip = phase?.Condition; contextBar.Add(text);
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; contextBar.Add(spacer);
        var add = ActionButton("+ 기능 추가", () => OpenFeaturePicker(selectedPhasePath, contextBar.worldBound), "ew-primary-button"); add.SetEnabled(currentEventSO != null && phase != null); contextBar.Add(add);
        var next = ActionButton(phase == null ? "+ 첫 Phase" : "+ 다음 Phase", () => AddNextPhase(selectedPhasePath)); next.SetEnabled(currentEventSO != null); contextBar.Add(next);
    }
    private void RefreshInspectorHeader()
    {
        for (int i = 0; i < inspectorButtons.Count; i++) inspectorButtons[i].EnableInClassList("is-active", i == inspectorMode);
        var phase = model?.Phases.FirstOrDefault(p => p.Path == selectedPhasePath); var row = model?.Rows.FirstOrDefault(r => r.FeaturePath == selectedFeaturePath);
        string featureTitle = row?.Title;
        inspectorActions.Clear();
        if (inspectorMode == 0 && embeddedEditor != null && embeddedEditor.TryGetWorkspaceFeatureInfo(selectedPhasePath, selectedFeaturePath, out string title, out bool enabled, out bool entry))
        {
            featureTitle = title;
            var toggle = new Toggle(entry ? "이 기능 전체 사용" : "기능 사용") { value = enabled };
            toggle.AddToClassList("ew-feature-toggle");
            toggle.RegisterValueChangedCallback(evt => { embeddedEditor.SetWorkspaceFeatureEnabled(selectedPhasePath, selectedFeaturePath, evt.newValue); RequestRefresh(); });
            inspectorActions.Add(toggle);
            if (entry) inspectorActions.Add(ActionButton("항목 삭제", () =>
            {
                if (!EditorUtility.DisplayDialog("항목 삭제", "선택한 항목을 삭제할까요? 실행 취소로 복구할 수 있습니다.", "삭제", "취소")) return;
                embeddedEditor.RemoveWorkspaceFeature(selectedPhasePath, selectedFeaturePath); selectedFeaturePath = null; RequestRefresh();
            }));
        }
        inspectorTitle.text = inspectorMode == 1 ? "이벤트 시작 조건" : inspectorMode == 2 ? "전체 구조 편집" : featureTitle ?? "Phase 설정";
        inspectorCaption.text = inspectorMode == 1 ? "직접 호출 · 조건 감시 · 분기 우선순위" : inspectorMode == 2 ? "순서 변경 · 복사 · 붙여넣기 · 삭제" : phase?.Label ?? "기능을 선택하면 해당 설정만 표시합니다.";
    }
    private void RebuildBrowser()
    {
        if (outline == null) return;
        for (int i = 0; i < browserButtons.Count; i++) browserButtons[i].EnableInClassList("is-active", i == browserMode);
        outlineScroll.style.display = browserMode == 0 ? DisplayStyle.Flex : DisplayStyle.None; libraryScroll.style.display = browserMode == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        if (browserMode == 0)
        {
            outline.Clear();
            if (model == null || model.Phases.Count == 0) outline.Add(Text("Phase가 없습니다.\n첫 Phase를 만들어 시작하세요.", "ew-empty-text"));
            else foreach (var phase in model.Phases)
            {
                if (!Matches(phase.Label + " " + phase.Condition, phaseSearch)) continue;
                var item = ActionButton("", () => SelectOnly(phase.Path, null), "ew-outline-item"); item.EnableInClassList("is-selected", phase.Path == selectedPhasePath); item.style.marginLeft = Mathf.Min(phase.Depth, 4) * 9;
                item.Add(Text((phase.Reachable ? "●  " : "○  ") + phase.Label, "ew-outline-title"));
                int count = model.Rows.Count(r => r.Phase == phase && r.FeaturePath != phase.Path); item.Add(Text(count + "개 기능" + (phase.Reachable ? "" : " · 미연결"), "ew-muted")); item.tooltip = phase.Condition; outline.Add(item);
            }
        }
        else
        {
            library.Clear(); if (assetPaths == null) assetPaths = AssetDatabase.FindAssets("t:EventSO").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string path in assetPaths)
            {
                if (!Matches(path, assetSearch)) continue;
                var item = ActionButton("", () => SetTarget(AssetDatabase.LoadAssetAtPath<EventSO>(path)), "ew-asset-item"); item.EnableInClassList("is-selected", currentEventSO != null && AssetDatabase.GetAssetPath(currentEventSO) == path);
                item.Add(Text(System.IO.Path.GetFileNameWithoutExtension(path), "ew-outline-title")); item.Add(Text(System.IO.Path.GetDirectoryName(path).Replace('\\', '/').Replace("Assets/", ""), "ew-asset-path")); item.tooltip = path; library.Add(item);
            }
        }
    }
    private static bool Matches(string source, string search) => string.IsNullOrWhiteSpace(search) || source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    private void SetBrowserMode(int index) { browserMode = index; browserSearch.SetValueWithoutNotify(index == 0 ? phaseSearch : assetSearch); RebuildBrowser(); }
    private void SetView(int index) { viewMode = index; SessionState.SetInt(SessionKey, viewMode); RefreshSelection(); }
    private void SetInspectorMode(int index) { inspectorMode = index; if (propertyScroll != null) propertyScroll.scrollOffset = Vector2.zero; RefreshInspectorHeader(); propertyGUI?.MarkDirtyRepaint(); }
    private void SelectOnly(string phase, string feature)
    {
        if (phase == null && feature == null) { SetInspectorMode(1); return; }
        selectedPhasePath = phase; selectedFeaturePath = feature; inspectorMode = 0; if (propertyScroll != null) propertyScroll.scrollOffset = Vector2.zero;
        RefreshSelection(); RebuildBrowser(); Repaint();
    }
    private void SelectForEditing(string phase, string feature) { SelectOnly(phase, feature); }
    private void OpenFeaturePicker(string phase, Rect rect)
    {
        if (currentEventSO == null) return;
        EventSO owner = currentEventSO;
        EventFeaturePicker.Show(rect, owner, phase, (p, feature) =>
        {
            if (this == null || currentEventSO != owner) return;
            selectedPhasePath = p; selectedFeaturePath = feature; inspectorMode = 0; RequestRefresh();
        });
    }
    private void AddNextPhase(string phase)
    {
        if (currentEventSO == null) return; string path;
        bool added = string.IsNullOrEmpty(phase) ? EventAuthoring.EnsureFirstPhase(currentEventSO, out path) : EventAuthoring.AddNextPhase(currentEventSO, phase, out path);
        if (added) { selectedPhasePath = path; selectedFeaturePath = null; inspectorMode = 0; RefreshDocument(); }
    }
    private void Save() { if (currentEventSO != null) AssetDatabase.SaveAssetIfDirty(currentEventSO); RefreshStatus(); }
    private void CreateEvent()
    {
        string path = EditorUtility.SaveFilePanelInProject("새 이벤트", "NewEvent", "asset", "EventSO를 저장할 위치를 선택하세요."); if (string.IsNullOrEmpty(path)) return;
        var asset = CreateInstance<EventSO>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssetIfDirty(asset); assetPaths = null; SetTarget(asset); Selection.activeObject = asset;
    }
    private void RefreshStatus()
    {
        if (documentTitle == null) return; bool dirty = currentEventSO != null && EditorUtility.IsDirty(currentEventSO);
        documentTitle.text = currentEventSO != null ? currentEventSO.name : "이벤트를 선택하세요"; documentTitle.tooltip = currentEventSO != null ? AssetDatabase.GetAssetPath(currentEventSO) : "";
        documentState.text = currentEventSO == null ? "" : dirty ? "● 저장 전" : "✓ 저장됨"; documentState.EnableInClassList("is-dirty", dirty);
        status.text = currentEventSO == null ? "READY   ·   왼쪽 이벤트 파일에서 열거나 새 이벤트를 만드세요." : "●  " + (EditorApplication.isPlaying ? "PLAY MODE" : "EDIT MODE") + "    |    " + model.Phases.Count + " Phase    ·    " + model.Rows.Count(r => r.FeaturePath != r.Phase.Path) + " 기능" + (model.Warnings.Count > 0 ? "    |    ⚠ " + model.Warnings.Count + "개 확인 사항" : "") + "    |    " + AssetDatabase.GetAssetPath(currentEventSO);
        status.tooltip = model != null ? string.Join("\n", model.Warnings) : "";
    }
    private void RequestRefresh()
    {
        if (refreshPending || rootVisualElement == null) return; refreshPending = true;
        rootVisualElement.schedule.Execute(() => { refreshPending = false; RefreshDocument(); }).ExecuteLater(25);
    }
    private void OnProjectChange() { assetPaths = null; overview?.InvalidateSources(); RequestRefresh(); }
    private void OnHierarchyChanged() { overview?.InvalidateSources(); RequestRefresh(); }
    private void OnUndoRedo() { RequestRefresh(); }
    private void OnSelectionChanged() { if (followSelection) TryUseSelection(); }
    private bool TryUseSelection() { if (!followSelection || !(Selection.activeObject is EventSO selected)) return false; SetTarget(selected); return true; }
    private void SetTarget(EventSO asset)
    {
        if (currentEventSO == asset)
        {
            if (asset != null && (embeddedEditor == null || embeddedEditor.target != asset)) RebuildEmbeddedEditor();
            RefreshDocument(); return;
        }
        currentEventSO = asset; selectedPhasePath = selectedFeaturePath = null; inspectorMode = 0; model = null; timelineView?.Reset(); RebuildEmbeddedEditor(); RefreshDocument();
    }
    private void RebuildEmbeddedEditor() { DisposeEmbeddedEditor(); if (currentEventSO != null) embeddedEditor = Editor.CreateEditor(currentEventSO, typeof(EventSOEditor)) as EventSOEditor; }
    private void DisposeEmbeddedEditor() { if (embeddedEditor != null) DestroyImmediate(embeddedEditor); embeddedEditor = null; }
}
