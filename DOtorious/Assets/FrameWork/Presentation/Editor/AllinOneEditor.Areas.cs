using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using JYW.Game.ObjectMaker.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AllinOneEditor
{
    public enum WorkspaceArea { Event, Monster, Player }
    [SerializeField] private WorkspaceArea activeArea;
    [SerializeField] private string monsterKey, playerKey;
    [SerializeField] private string eventSearch = "", monsterSearch = "", playerSearch = "";
    [SerializeField] private bool showUnsupported;
    private readonly List<Button> areaButtons = new List<Button>();
    private VisualElement browserPane, browserFilters, emptyArea;
    private Label browserTitle, browserCaption, browserMessage;
    private Button newDocumentButton;
    private Toggle unsupportedToggle;
    private List<ObjectPrefabEntry> prefabEntries;
    public WorkspaceArea ActiveArea => activeArea;
    private bool IsEventArea => activeArea == WorkspaceArea.Event;
    private ObjectKind AreaKind => activeArea == WorkspaceArea.Player ? ObjectKind.Player : ObjectKind.Monster;

    private void DrawAreas(VisualElement root)
    {
        var bar = new VisualElement { name = "workspace-areas" }; bar.AddToClassList("aio-areas"); root.Add(bar);
        areaButtons.Clear();
        string[] captions = { "조건 · 이벤트", "몬스터 Prefab", "플레이어 설정" };
        for (int i = 0; i < 3; i++)
        {
            var area = (WorkspaceArea)i;
            var button = ActionButton("", () => SwitchArea(area), "aio-area");
            button.name = "area-" + area;
            button.Add(Text(area.ToString(), "aio-area-title")); button.Add(Text(captions[i], "aio-area-caption"));
            button.tooltip = "Ctrl+" + (i + 1); areaButtons.Add(button); bar.Add(button);
        }
    }
    private void RememberSearch()
    {
        if (IsEventArea) eventSearch = assetSearch;
        else if (activeArea == WorkspaceArea.Monster) monsterSearch = assetSearch;
        else playerSearch = assetSearch;
    }
    public void SwitchArea(WorkspaceArea area)
    {
        if (area == activeArea) return;
        ShowBrowserMessage("파일을 여기에 놓아 불러오기");
        creatingEvent = false; simpleFingerprint = null;
        EventSceneTargetField.Cancel();
        RememberSearch(); PersistWorkspace();
        activeArea = area;
        assetSearch = IsEventArea ? eventSearch : area == WorkspaceArea.Monster ? monsterSearch : playerSearch;
        objectActive = !IsEventArea;
        if (!IsEventArea)
        {
            string key = area == WorkspaceArea.Monster ? monsterKey : playerKey;
            var doc = objectDocuments.FirstOrDefault(d => d != null && d.Key == key && d.Data.kind == AreaKind)
                ?? objectDocuments.LastOrDefault(d => d != null && d.Data.kind == AreaKind);
            activeObjectKey = doc?.Key;
        }
        browserSearch?.SetValueWithoutNotify(assetSearch);
        rootVisualElement.Focus(); PersistWorkspace(); RefreshDocument();
    }
    private void ActivateAreaForObject(ObjectWorkspaceDocument document)
    {
        RememberSearch();
        activeArea = document.Data.kind == ObjectKind.Player ? WorkspaceArea.Player : WorkspaceArea.Monster;
        if (activeArea == WorkspaceArea.Player) playerKey = document.Key; else monsterKey = document.Key;
        assetSearch = activeArea == WorkspaceArea.Player ? playerSearch : monsterSearch;
        browserSearch?.SetValueWithoutNotify(assetSearch);
    }
    private void ActivateEventArea()
    {
        if (IsEventArea) return;
        RememberSearch(); activeArea = WorkspaceArea.Event; assetSearch = eventSearch;
        browserSearch?.SetValueWithoutNotify(assetSearch);
    }
    private void RefreshAreaChrome()
    {
        for (int i = 0; i < areaButtons.Count; i++) areaButtons[i].EnableInClassList("is-active", (int)activeArea == i);
        if (browserTitle == null) return;
        browserTitle.text = IsEventArea ? "이벤트 목록" : activeArea == WorkspaceArea.Player ? "플레이어 목록" : "몬스터 목록";
        browserCaption.text = IsEventArea ? "선택해서 편집" : "프리팹을 선택해서 편집";
        newDocumentButton.text = "+ 새로 만들기";
        assetField.tooltip = IsEventArea ? "EventSO를 놓으면 이 Event 영역에서 엽니다." : "해당 종류의 Prefab / Definition / 연결된 씬 오브젝트를 가져옵니다. 원본은 자동 저장하지 않습니다.";
        browserFilters.style.display = IsEventArea ? DisplayStyle.None : DisplayStyle.Flex;
        unsupportedToggle.style.display = IsEventArea ? DisplayStyle.None : DisplayStyle.Flex;
        for (int i = 0; i < assetFilterButtons.Count; i++) assetFilterButtons[i].EnableInClassList("is-active", assetFilter == i);
    }
    private void DrawDocuments(VisualElement root)
    {
        documentsBar = new ScrollView(ScrollViewMode.Horizontal) { name = "documents" };
        documentsBar.AddToClassList("aio-documents"); root.Add(documentsBar);
    }
    private void RefreshDocuments()
    {
        if (documentsBar == null) return;
        documentsBar.Clear(); RefreshAreaChrome();
        documentsBar.Add(Text(IsEventArea ? "열린 이벤트" : "열린 " + activeArea + " 초안", "aio-document-scope"));
        int count = 0;
        if (IsEventArea)
            foreach (var asset in eventDocuments.Where(e => e != null))
            {
                var captured = asset;
                var tab = ActionButton(asset.name + (EditorUtility.IsDirty(asset) ? "  ●" : ""), () => SetTarget(captured), "aio-document-tab");
                tab.EnableInClassList("is-active", currentEventSO == asset);
                tab.tooltip = AssetDatabase.GetAssetPath(asset); AddAssetContext(tab, asset); documentsBar.Add(tab); count++;
            }
        else
            foreach (var doc in objectDocuments.Where(d => d != null && d.Data.kind == AreaKind))
            {
                var captured = doc;
                var tab = ActionButton(doc.Title + (doc.IsDirty ? "  ●" : ""), () => ActivateObject(captured), "aio-document-tab");
                tab.EnableInClassList("is-active", IsObjectDocument && ActiveObject == doc);
                tab.tooltip = doc.Source == null ? "새 Prefab 초안 · Make 전" : AssetDatabase.GetAssetPath(doc.Source);
                AddAssetContext(tab, doc.Source); documentsBar.Add(tab); count++;
            }
        if (count == 0) documentsBar.Add(Text("왼쪽에서 열거나 새로 만드세요.", "aio-document-hint"));
    }
    private void DrawUnifiedBrowser(VisualElement left)
    {
        browserPane = left;
        var head = new VisualElement(); head.AddToClassList("ew-section-head");
        browserTitle = Text("", "ew-eyebrow"); head.Add(browserTitle);
        newDocumentButton = ActionButton("", () => { if (IsEventArea) CreateEvent(); else NewObject(AreaKind); }, "aio-new-document");
        head.Add(newDocumentButton); left.Add(head);
        browserCaption = Text("", "aio-browser-caption"); left.Add(browserCaption);
        assetField = new ObjectField { objectType = typeof(UnityEngine.Object), allowSceneObjects = true, name = "asset-drop" };
        assetField.AddToClassList("ew-asset-field");
        assetField.RegisterValueChangedCallback(e =>
        {
            if (!TryOpenInArea(e.newValue)) assetField.SetValueWithoutNotify(IsObjectDocument ? (UnityEngine.Object)ActiveObject.Source?.GeneratedPrefab ?? ActiveObject.Source : IsEventArea ? currentEventSO : null);
        });
        left.Add(assetField);
        browserMessage = Text("파일을 여기에 놓아 불러오기", "aio-browser-message"); left.Add(browserMessage);
        var browserOptions = new Foldout { text = "불러오기 옵션", value = false, name = "browser-options" }; browserOptions.AddToClassList("aio-import-options"); left.Add(browserOptions);
        var follow = new Toggle("Project 선택으로 문서 열기") { value = workspaceFollowSelection };
        follow.AddToClassList("ew-follow"); follow.RegisterValueChangedCallback(e => { workspaceFollowSelection = e.newValue; if (e.newValue) TryUseSelection(); }); browserOptions.Add(follow);
        browserFilters = new VisualElement(); browserFilters.AddToClassList("ew-small-tabs"); browserOptions.Add(browserFilters); assetFilterButtons.Clear();
        string[] filters = { "Prefab", "Definition" };
        for (int i = 0; i < filters.Length; i++)
        { int index = i; var b = ActionButton(filters[i], () => { assetFilter = index; RebuildBrowser(); }, "ew-small-tab"); assetFilterButtons.Add(b); browserFilters.Add(b); }
        unsupportedToggle = new Toggle("연결 문제도 표시") { value = showUnsupported };
        unsupportedToggle.AddToClassList("ew-follow"); unsupportedToggle.RegisterValueChangedCallback(e => { showUnsupported = e.newValue; RebuildBrowser(); }); browserOptions.Add(unsupportedToggle);
        browserSearch = new ToolbarSearchField { name = "asset-search" }; browserSearch.AddToClassList("ew-browser-search");
        browserSearch.SetValueWithoutNotify(assetSearch); browserSearch.tooltip = "이름 / 경로 검색 · Ctrl+F";
        browserSearch.RegisterValueChangedCallback(e => { assetSearch = e.newValue; RememberSearch(); RebuildBrowser(); }); left.Add(browserSearch);
        libraryScroll = new ScrollView { name = "asset-list" }; libraryScroll.AddToClassList("ew-browser-scroll"); library = libraryScroll.contentContainer; left.Add(libraryScroll);
        var footer = new VisualElement(); footer.AddToClassList("ew-browser-foot");
        
        footer.Add(Text("● 표시는 저장 전 변경입니다.", "ew-muted")); left.Add(footer);
        RegisterAssetDrop(left);
    }
    private void ShowBrowserMessage(string message, bool error = false)
    {
        if (browserMessage == null) return;
        browserMessage.text = message; browserMessage.EnableInClassList("is-error", error);
    }
    private bool TryOpenInArea(UnityEngine.Object asset)
    {
        if (asset == null) return false;
        if (IsEventArea)
        {
            if (asset is EventSO eventAsset) { SetTarget(eventAsset); ShowBrowserMessage("이벤트를 열었습니다."); return true; }
            ShowBrowserMessage("Event 영역에는 EventSO를 넣어주세요. Prefab은 Monster / Player에서 엽니다.", true); return false;
        }
        var entry = ObjectPrefabCatalog.Inspect(asset);
        if (!entry.Supported) { ShowBrowserMessage(entry.Problem, true); return false; }
        if (entry.Kind != AreaKind) { ShowBrowserMessage("이 대상은 " + (entry.Kind == ObjectKind.Player ? "Player" : "Monster") + "입니다. 해당 영역에서 열어주세요.", true); return false; }
        bool opened = SetAnyTarget(entry.Definition);
        ShowBrowserMessage(entry.Note ?? (entry.Prefab == null ? "설정을 불러왔습니다. 만들기를 누르면 프리팹이 생성됩니다." : "불러왔습니다. 수정 후 프리팹에 적용하세요."));
        return opened;
    }
    private void RebuildUnifiedBrowser()
    {
        if (library == null) return;
        RefreshAreaChrome(); library.Clear();
        AddDraftsToBrowser();
        if (IsEventArea || assetFilter == 1)
        {
            var paths = AssetDatabase.FindAssets(IsEventArea ? "t:EventSO" : "t:ObjectDefinitionSO").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p);
            foreach (var path in paths)
            {
                if (!Matches(path, assetSearch)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (!IsEventArea && objectDocuments.Any(d => d != null && d.Data.kind == AreaKind && d.Source == asset)) continue;
                if (!IsEventArea && (!(asset is ObjectDefinitionSO d) || d.Data.kind != AreaKind)) continue;
                AddBrowserAsset(asset, path, IsEventArea ? "EventSO" : "Definition", null, true);
            }
        }
        else
        {
            if (prefabEntries == null) prefabEntries = ObjectPrefabCatalog.Scan();
            foreach (var entry in prefabEntries)
            {
                if (entry.Supported && objectDocuments.Any(d => d != null && d.Data.kind == AreaKind && d.Source == entry.Definition)) continue;
                if (entry.Definition != null && entry.Kind != AreaKind || !entry.Supported && !showUnsupported) continue;
                if (!Matches(entry.Path + " " + (entry.Definition != null ? entry.Definition.name : "") + " " + entry.Problem, assetSearch)) continue;
                AddBrowserAsset(entry.Prefab, entry.Path, entry.Supported ? "Prefab → " + entry.Definition.name : "연결 확인", entry.Problem, entry.Supported);
            }
        }
        if (library.childCount == 0)
            library.Add(Text(string.IsNullOrEmpty(assetSearch) ? (IsEventArea ? "이벤트가 없습니다. 새 이벤트를 만드세요." : "연결된 Prefab이 없습니다. 새로 만들거나 Definition 보기를 선택하세요.") : "검색 결과가 없습니다. 검색어와 필터를 확인하세요.", "ew-empty-text"));
    }
    private void AddBrowserAsset(UnityEngine.Object asset, string path, string detail, string problem, bool supported)
    {
        var item = ActionButton("", () => { if (supported) TryOpenInArea(asset); else ShowBrowserMessage(problem, true); }, "ew-asset-item");
        item.Add(Text(asset != null ? asset.name + (EditorUtility.IsDirty(asset) ? "  ●" : "") : "연결 없음", "ew-outline-title"));
        
        
        item.EnableInClassList("is-selected", IsObjectDocument ? asset == ActiveObject.Source || asset == ActiveObject.Source?.GeneratedPrefab : IsEventArea && currentEventSO == asset);
        item.EnableInClassList("is-unsupported", !supported); item.tooltip = string.IsNullOrEmpty(problem) ? path : problem + "\n" + path;
        AddAssetContext(item, asset); library.Add(item);
    }
    private static void AddAssetContext(VisualElement element, UnityEngine.Object asset)
    {
        element.AddManipulator(new ContextualMenuManipulator(e =>
        {
            e.menu.AppendAction("Project에서 위치 찾기", _ => { if (asset != null) EditorGUIUtility.PingObject(asset); }, _ => asset == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            e.menu.AppendAction("에셋 경로 복사", _ => EditorGUIUtility.systemCopyBuffer = AssetDatabase.GetAssetPath(asset), _ => asset == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            if (asset is GameObject prefab) e.menu.AppendAction("Prefab Mode에서 열기", _ => AssetDatabase.OpenAsset(prefab));
        }));
    }
    private void RegisterAssetDrop(VisualElement panel)
    {
        panel.RegisterCallback<DragUpdatedEvent>(e =>
        {
            bool valid = DragAndDrop.objectReferences.Length == 1 &&
                (IsEventArea ? DragAndDrop.objectReferences[0] is EventSO : ObjectPrefabCatalog.Inspect(DragAndDrop.objectReferences[0]).Supported &&
                 ObjectPrefabCatalog.Inspect(DragAndDrop.objectReferences[0]).Kind == AreaKind);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            e.StopPropagation();
        });
        panel.RegisterCallback<DragPerformEvent>(e =>
        {
            if (DragAndDrop.objectReferences.Length == 1)
            { if (TryOpenInArea(DragAndDrop.objectReferences[0])) DragAndDrop.AcceptDrag(); }
            else ShowBrowserMessage("한 번에 하나의 에셋을 가져오세요.", true);
            e.StopPropagation();
        });
    }
    private void SetDocumentVisibility()
    {
        bool empty = !IsEventArea && !IsObjectDocument;
        eventViewBar.style.display = IsEventArea ? DisplayStyle.Flex : DisplayStyle.None;
        eventInspectorTabs.style.display = IsEventArea ? DisplayStyle.Flex : DisplayStyle.None;
        objectCanvas.style.display = IsObjectDocument ? DisplayStyle.Flex : DisplayStyle.None;
        emptyArea.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
        propertyScroll.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
        if (!IsEventArea) flow.style.display = overview.style.display = timelineGUI.style.display = DisplayStyle.None;
        assetField?.SetValueWithoutNotify(IsObjectDocument ? (UnityEngine.Object)ActiveObject.Source?.GeneratedPrefab ?? ActiveObject.Source : IsEventArea ? currentEventSO : null);
    }
    private void RefreshEmptyArea()
    {
        emptyArea.Clear(); contextBar.Clear(); inspectorActions.Clear();
        emptyArea.Add(Text(activeArea + " 제작", "ew-flow-empty-title"));
        emptyArea.Add(Text("왼쪽에서 Prefab을 가져오거나 새 " + activeArea + "를 만드세요.\n설정 후 만들기 또는 프리팹에 적용을 눌러 저장하세요.", "ew-flow-empty-detail"));
        emptyArea.Add(ActionButton("+ 새 " + activeArea + " 만들기", () => NewObject(AreaKind), "ew-primary-button"));
        documentTitle.text = activeArea + " / Prefab 선택"; documentState.text = "";
        inspectorTitle.text = activeArea + " 속성"; inspectorCaption.text = "선택한 항목의 설정이 표시됩니다."; inspectorTip.text = "기존 초안은 다른 영역에 그대로 유지됩니다.";
        saveButton.text = "프리팹 만들기"; saveButton.SetEnabled(false);
        status.text = activeArea.ToString().ToUpperInvariant() + "  ·  Prefab 단위 가져오기 / 생성";
        RebuildBrowser();
    }
}
