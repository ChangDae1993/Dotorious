using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using JYW.Game.ObjectMaker.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A dedicated authoring workspace over the original EventSO serialized data.</summary>
public sealed partial class AllinOneEditor : ObjectMakerWindow
{
    [SerializeField] private EventSO currentEventSO;
    [SerializeField] private bool workspaceFollowSelection = false;
    [SerializeField] private int viewMode, inspectorMode;
    [SerializeField] private string selectedPhasePath, selectedFeaturePath, assetSearch = "";
    [SerializeField] private EventTimelineView timelineView = new EventTimelineView();
    private EventSOEditor embeddedEditor;
    private EventStepListModel model;
    private VisualElement center, canvasHost, library, contextBar, inspectorActions;
    private Label documentTitle, documentState, status, inspectorTitle, inspectorCaption;
    private ObjectField assetField;
    private ToolbarSearchField browserSearch;
    private ScrollView propertyScroll, libraryScroll;
    private EventWorkspaceCanvas flow;
    private EventWorkspaceOverview overview;
    private IMGUIContainer propertyGUI, timelineGUI;
    private readonly List<Button> viewButtons = new List<Button>();
    private readonly List<Button> inspectorButtons = new List<Button>();
    private int lastDirtyCount = -1;
    private bool refreshPending;
    private string SessionKey => "AllinOneEditor.WorkspaceTab." + Application.dataPath;

    [MenuItem("Window/FrameWork/AllinOneEditor", false, 2299)]
    public static new void OpenWindow() => OpenDocument(Selection.activeObject);

    public static AllinOneEditor OpenDocument(UnityEngine.Object source = null)
    {
        var window = GetWindow<AllinOneEditor>();
        if (source != null) window.SetAnyTarget(source);
        window.Show(); window.Focus();
        return window;
    }

    [InitializeOnLoadMethod]
    private static void RegisterWorkspace()
    {
        LegacyDraftImporter = (source, data) => OpenDocument(source).ImportLegacyDraft(source, data);
        WorkspaceOpener = source =>
        {
            var window = OpenDocument(source);
            if (source == null) window.NewObject(ObjectKind.Monster);
            return window;
        };
    }

    protected override void OnGUI() { }
    protected override void OnEnable()
    {
        titleContent = new GUIContent("AllinOneEditor"); minSize = new Vector2(960, 600);
        RestoreWorkspace();
        RestoreMapDraft(); wantsMouseMove = true;
        viewMode = Mathf.Clamp(SessionState.GetInt(SessionKey, viewMode), 0, 3);
        if (timelineView == null) timelineView = new EventTimelineView();
        timelineView.SelectionRequested = SelectOnly; timelineView.EditRequested = SelectForEditing;
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EventSceneTargetField.Changed += RequestRefresh;
        RebuildEmbeddedEditor();
    }
    protected override void OnDisable()
    {
        FinishMapStroke(); SaveMapRecovery(); ReleaseMapTextures();
        if (mapSourcePreview) DestroyImmediate(mapSourcePreview); mapSourcePreview = null;
        EventSceneTargetField.Cancel();
        EventSceneTargetField.Changed -= RequestRefresh;
        PersistWorkspace();
        SessionState.SetInt(SessionKey, viewMode);
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        timelineView.SelectionRequested = null; timelineView.EditRequested = null;
        DisposeEmbeddedEditor();
    }
    public void CreateGUI()
    {
        var root = rootVisualElement; root.Clear(); root.focusable = true; root.AddToClassList("ew-root");
        string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(System.IO.Path.GetDirectoryName(script).Replace('\\', '/') + "/EventWorkspace.uss");
        if (theme != null) root.styleSheets.Add(theme);
        viewButtons.Clear(); inspectorButtons.Clear();
        DrawAreas(root);
        DrawCommandBar(root);
        DrawDocuments(root); documentsBar.style.display = DisplayStyle.None;
        var workspace = new TwoPaneSplitView(0, 238, TwoPaneSplitViewOrientation.Horizontal); documentWorkspace = workspace;
        workspace.viewDataKey = "AllinOneEditor.ExplorerWidth";
        workspace.AddToClassList("ew-workspace"); root.Add(workspace);
        var left = new VisualElement(); left.AddToClassList("ew-browser"); left.style.minWidth = 175; workspace.Add(left); DrawBrowser(left);
        var body = new VisualElement(); body.style.flexGrow = 1; body.style.minWidth = 540; workspace.Add(body);
        simplePage = new ScrollView { name = "simple-page" }; simplePage.AddToClassList("aio-simple-page"); body.Add(simplePage);
        var work = new TwoPaneSplitView(1, 350, TwoPaneSplitViewOrientation.Horizontal); advancedWorkspace = work;
        work.viewDataKey = "AllinOneEditor.PropertiesWidth";
        work.style.flexGrow = 1; body.Add(work);
        center = new VisualElement(); center.AddToClassList("ew-center"); center.style.minWidth = 380; work.Add(center); DrawCenter(center);
        var right = new VisualElement(); right.AddToClassList("ew-inspector"); right.style.minWidth = 280; work.Add(right); DrawInspector(right);

        DrawMapWorkspace(root);
        status = new Label(); status.AddToClassList("ew-status"); root.Add(status); status.style.display = DisplayStyle.None;
        root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (IsMapArea && !evt.actionKey && !evt.altKey && MapShortcutTargetAllowsTyping() && HandleMapShortcut(evt.keyCode)) { evt.StopImmediatePropagation(); return; }
            if (evt.keyCode == KeyCode.Escape) { FinishMapStroke(); if (creatingEvent) CancelNewEvent(); else EventSceneTargetField.Cancel(); evt.StopPropagation(); }
            if (evt.actionKey && evt.keyCode == KeyCode.F) { browserSearch?.Focus(); evt.StopPropagation(); }
            if (evt.actionKey && evt.keyCode >= KeyCode.Alpha1 && evt.keyCode <= KeyCode.Alpha4) { SwitchArea((WorkspaceArea)((int)evt.keyCode - (int)KeyCode.Alpha1)); evt.StopPropagation(); }
            if (evt.actionKey && evt.keyCode == KeyCode.S) { Save(); evt.StopPropagation(); }
        }, TrickleDown.TrickleDown);
        root.schedule.Execute(() =>
        {
            if (IsMapArea) { RefreshMapStatus(); return; } if (creatingEvent) return;
            if (IsObjectDocument) { RefreshObjectStatus(); return; }
            if (!IsEventArea || currentEventSO == null) return;
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
        
        var identity = new VisualElement(); identity.AddToClassList("ew-document"); bar.Add(identity);
        
        documentTitle = Text("이벤트를 선택하세요", "ew-document-title"); identity.Add(documentTitle);
        documentState = Text("", "ew-save-state"); bar.Add(documentState);
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; bar.Add(spacer);
        var undo = ActionButton("↶", () => Undo.PerformUndo(), "ew-icon-button"); undo.tooltip = "실행 취소 · Ctrl+Z"; bar.Add(undo);
        var redo = ActionButton("↷", () => Undo.PerformRedo(), "ew-icon-button"); redo.tooltip = "다시 실행 · Ctrl+Y"; bar.Add(redo);
        modeButton = ActionButton("상세 편집", () => SetDetailed(!detailedMode)); modeButton.name = "editor-detail-mode"; bar.Add(modeButton);
        var options = new ToolbarMenu { text = "더 보기" }; options.AddToClassList("ew-file-menu");
        options.menu.AppendAction("새 이벤트 만들기…", _ => CreateEvent());
        options.menu.AppendAction("새 Monster 프리팹 초안", _ => NewObject(ObjectKind.Monster));
        options.menu.AppendAction("새 Player 초안", _ => NewObject(ObjectKind.Player));
        options.menu.AppendAction("Prefab 가져오기 / 목록", _ => { assetFilter = 0; RebuildBrowser(); assetField?.Focus(); ShowBrowserMessage("Prefab을 가져오기 칸에 놓거나 아래 목록에서 선택하세요."); }, _ => IsEventArea ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        options.menu.AppendAction("초안 JSON 가져오기…", _ => ImportObjectJson());
        options.menu.AppendAction("초안 JSON 내보내기…", _ => ExportObjectJson(), _ => IsObjectDocument ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        options.menu.AppendAction("현재 초안 되돌리기…", _ => RevertObject(), _ => IsObjectDocument && ActiveObject.Source != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        options.menu.AppendAction("Project에서 위치 찾기", _ => { var asset = IsObjectDocument ? ActiveObject.Source : (UnityEngine.Object)currentEventSO; if (asset != null) EditorGUIUtility.PingObject(asset); });
        options.menu.AppendAction("이벤트 전체 구조 편집", _ => { SetDetailed(true); SetInspectorMode(2); }, _ => !IsObjectDocument && currentEventSO != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled); bar.Add(options);
        saveButton = ActionButton("이벤트 저장", () => { if (IsMapArea) MakeMap(); else Save(); }, "ew-primary-button"); saveButton.name = "document-save"; saveButton.tooltip = "Ctrl+S"; bar.Add(saveButton);
    }
    private void DrawBrowser(VisualElement left) => DrawUnifiedBrowser(left);
    private void DrawCenter(VisualElement parent)
    {
        var tabs = new VisualElement(); eventViewBar = tabs; tabs.AddToClassList("ew-viewbar"); parent.Add(tabs);
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
        emptyArea = new VisualElement { name = "empty-area" }; emptyArea.AddToClassList("ew-flow-empty"); canvasHost.Add(emptyArea);
        objectCanvas = new ScrollView { name = "object-canvas" }; objectCanvas.AddToClassList("aio-object-canvas"); canvasHost.Add(objectCanvas);
    }
    private void DrawInspector(VisualElement right)
    {
        right.Add(Text("PROPERTIES", "ew-inspector-eyebrow")); inspectorTitle = Text("선택 속성", "ew-inspector-title"); right.Add(inspectorTitle);
        inspectorCaption = Text("흐름에서 기능을 선택하세요.", "ew-inspector-caption"); right.Add(inspectorCaption);
        inspectorActions = new VisualElement(); inspectorActions.AddToClassList("ew-property-actions"); right.Add(inspectorActions);
        var tabs = new VisualElement(); eventInspectorTabs = tabs; tabs.AddToClassList("ew-small-tabs"); right.Add(tabs);
        string[] labels = { "선택 항목", "시작 조건", "전체 구조" };
        for (int i = 0; i < labels.Length; i++)
        { int index = i; var b = ActionButton(labels[i], () => SetInspectorMode(index), "ew-small-tab"); inspectorButtons.Add(b); tabs.Add(b); }
        var inspectorBody = new VisualElement(); inspectorBody.AddToClassList("ew-property-body"); right.Add(inspectorBody);
        propertyScroll = new ScrollView(); propertyScroll.AddToClassList("ew-property-scroll"); inspectorBody.Add(propertyScroll);
        propertyGUI = new IMGUIContainer(DrawProperties); propertyGUI.AddToClassList("ew-property-gui"); propertyScroll.Add(propertyGUI);
        inspectorTip = Text("", "ew-inspector-tip"); right.Add(inspectorTip);
    }
    private void DrawProperties()
    {
        if (IsObjectDocument) { DrawObjectInspector(); return; }
        if (!IsEventArea || currentEventSO == null || embeddedEditor == null) return;
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
        documentWorkspace.style.display = IsMapArea ? DisplayStyle.None : DisplayStyle.Flex;
        mapWorkspace.style.display = IsMapArea ? DisplayStyle.Flex : DisplayStyle.None;
        modeButton.style.display = IsMapArea ? DisplayStyle.None : DisplayStyle.Flex;
        if (IsMapArea) { RefreshMapWorkspace(); return; }
        rootVisualElement.Q<ToolbarMenu>(className: "ew-file-menu")?.SetEnabled(true);
        saveButton.tooltip = "Ctrl+S";
        SetDocumentVisibility();
        RefreshDocuments(); RefreshModeVisibility();
        if (IsObjectDocument) { RefreshObjectWorkspace(); RebuildBrowser(); RefreshSimpleWorkspace(); return; }
        if (!IsEventArea) { RefreshEmptyArea(); RefreshSimpleWorkspace(); return; }
        if (currentEventSO != null && (embeddedEditor == null || embeddedEditor.target != currentEventSO)) RebuildEmbeddedEditor();
        model = EventStepListModel.Build(currentEventSO);
        if (!model.Phases.Any(p => p.Path == selectedPhasePath)) { selectedPhasePath = model.Phases.FirstOrDefault()?.Path; selectedFeaturePath = null; }
        if (selectedFeaturePath != null && embeddedEditor != null && !embeddedEditor.TryGetWorkspaceFeatureInfo(selectedPhasePath, selectedFeaturePath, out _, out _, out _)) selectedFeaturePath = null;
        lastDirtyCount = currentEventSO != null ? EditorUtility.GetDirtyCount(currentEventSO) : -1;
        assetField?.SetValueWithoutNotify(currentEventSO); RefreshSelection(); RebuildBrowser(); RefreshStatus(); RefreshSimpleWorkspace();
    }
    private void RefreshSelection()
    {
        if (IsObjectDocument) { RefreshObjectWorkspace(); return; }
        if (!IsEventArea || canvasHost == null || model == null) return;
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
        contextBar.Add(ActionButton("시작 조건", () => SetInspectorMode(1)));
        var add = ActionButton("+ 기능 추가", () => OpenFeaturePicker(selectedPhasePath, contextBar.worldBound), "ew-primary-button"); add.SetEnabled(currentEventSO != null && phase != null); contextBar.Add(add);
        var next = ActionButton(phase == null ? "+ 첫 Phase" : "+ 다음 Phase", () => AddNextPhase(selectedPhasePath)); next.SetEnabled(currentEventSO != null); contextBar.Add(next);
    }
    private void RefreshInspectorHeader()
    {
        if (IsObjectDocument) { RefreshObjectInspectorHeader(); return; }
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
    private void RebuildBrowser() => RebuildUnifiedBrowser();
    private static bool Matches(string source, string search) => string.IsNullOrWhiteSpace(search) || source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    private void SetView(int index) { viewMode = index; SessionState.SetInt(SessionKey, viewMode); RefreshSelection(); }
    private void SetInspectorMode(int index) { EventSceneTargetField.Cancel(); inspectorMode = index; if (propertyScroll != null) propertyScroll.scrollOffset = Vector2.zero; RefreshInspectorHeader(); propertyGUI?.MarkDirtyRepaint(); }
    private void SelectOnly(string phase, string feature)
    {
        if (phase == null && feature == null) { SetInspectorMode(1); return; }
        EventSceneTargetField.Cancel();
        selectedPhasePath = phase; selectedFeaturePath = feature; inspectorMode = 0; if (propertyScroll != null) propertyScroll.scrollOffset = Vector2.zero;
        RefreshSelection(); RebuildBrowser(); RefreshSimpleWorkspace(); Repaint();
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
    private void Save()
    {
        if (IsMapArea) { SaveMapDraftFile(); return; }
        if (creatingEvent) return;
        // End active field interaction before applying the current draft.
        rootVisualElement.Focus();
        if (IsObjectDocument) { BuildObject(); return; }
        if (!IsEventArea) return;
        if (currentEventSO != null) AssetDatabase.SaveAssetIfDirty(currentEventSO);
        RefreshStatus(); RefreshDocuments();
    }
    private void CreateEvent()
    {
        BeginNewEvent();
    }
    private void RefreshStatus()
    {
        if (IsObjectDocument) { RefreshObjectStatus(); return; }
        if (documentTitle == null) return;
        saveButton.text = "이벤트 저장"; saveButton.SetEnabled(currentEventSO != null);
        inspectorTip.text = "EventSO 직접 편집 · Ctrl+S로 에셋 저장\nCtrl+Z / Ctrl+Y로 변경 취소·다시 실행";
        bool dirty = currentEventSO != null && EditorUtility.IsDirty(currentEventSO);
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
    private void OnProjectChange() { prefabEntries = null; overview?.InvalidateSources(); RequestRefresh(); }
    private void OnHierarchyChanged() { overview?.InvalidateSources(); RequestRefresh(); }
    private void OnUndoRedo() { OnMapUndoRedo(); simpleFingerprint = null; PersistWorkspace(); RequestRefresh(); }
    private void OnSelectionChanged() { if (!EventSceneTargetField.IsPicking && !EventSceneTargetField.SelectionConsumedThisTick && workspaceFollowSelection) TryUseSelection(); }
    private bool TryUseSelection() { return workspaceFollowSelection && EditorUtility.IsPersistent(Selection.activeObject) && TryOpenInArea(Selection.activeObject); }
    private void SetTarget(EventSO asset)
    {
        ShowBrowserMessage("파일을 여기에 놓아 불러오기");
        creatingEvent = false; simpleFingerprint = null;
        if (simplePage != null) simplePage.scrollOffset = Vector2.zero;
        EventSceneTargetField.Cancel(); ActivateEventArea();
        objectActive = false;
        if (asset != null && !eventDocuments.Contains(asset)) eventDocuments.Add(asset);
        PersistWorkspace();
        if (currentEventSO == asset)
        {
            if (asset != null && (embeddedEditor == null || embeddedEditor.target != asset)) RebuildEmbeddedEditor();
            RefreshDocument(); return;
        }
        currentEventSO = asset; PersistWorkspace(); selectedPhasePath = selectedFeaturePath = null; inspectorMode = 0; model = null; timelineView?.Reset(); RebuildEmbeddedEditor(); RefreshDocument();
    }
    private void RebuildEmbeddedEditor() { DisposeEmbeddedEditor(); if (currentEventSO != null) embeddedEditor = Editor.CreateEditor(currentEventSO, typeof(EventSOEditor)) as EventSOEditor; }
    private void DisposeEmbeddedEditor() { if (embeddedEditor != null) DestroyImmediate(embeddedEditor); embeddedEditor = null; }
}
