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
    [SerializeField] private List<ObjectWorkspaceDocument> objectDocuments = new List<ObjectWorkspaceDocument>();
    [SerializeField] private List<EventSO> eventDocuments = new List<EventSO>();
    [SerializeField] private string activeObjectKey;
    [SerializeField] private bool objectActive;
    [SerializeField] private int assetFilter;
    private ScrollView documentsBar, objectCanvas;
    private VisualElement eventViewBar, eventInspectorTabs;
    private Label inspectorTip;
    private Button saveButton;
    private string recoveryError;
    private bool recoveryBlocked, restoring;
    private readonly List<Button> assetFilterButtons = new List<Button>();
    private ObjectWorkspaceDocument ActiveObject => objectDocuments.FirstOrDefault(d => d != null && d.Key == activeObjectKey);
    private bool IsObjectDocument => !IsEventArea && !IsMapArea && objectActive && ActiveObject != null && ActiveObject.Data.kind == AreaKind;

    private void RestoreWorkspace()
    {
        if (objectDocuments == null) objectDocuments = new List<ObjectWorkspaceDocument>();
        if (eventDocuments == null) eventDocuments = new List<EventSO>();
        objectDocuments.RemoveAll(d => d == null); eventDocuments.RemoveAll(e => e == null);
        // A window already open before areas were introduced has the default Event value.
        if (objectActive && activeArea == WorkspaceArea.Event && ActiveObject != null)
        {
            activeArea = ActiveObject.Data.kind == ObjectKind.Player ? WorkspaceArea.Player : WorkspaceArea.Monster;
            if (activeArea == WorkspaceArea.Player) playerKey = activeObjectKey; else monsterKey = activeObjectKey;
        }
        // Unity's saved window layout restores persistent EventSO references, but not
        // HideAndDontSave draft objects. Event tabs must not prevent disk recovery.
        if (objectDocuments.Count > 0) return;
        restoring = true;
        try
        {
            var snapshot = ObjectWorkspaceRecovery.Read();
            var recovered = new List<ObjectWorkspaceDocument>();
            try { foreach (var record in snapshot.objects) recovered.Add(ObjectWorkspaceRecovery.Restore(record)); }
            catch { foreach (var doc in recovered) DestroyImmediate(doc); throw; }
            objectDocuments = recovered;
            foreach (string guid in snapshot.eventGuids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<EventSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && !eventDocuments.Contains(asset)) eventDocuments.Add(asset);
            }
            activeObjectKey = snapshot.activeObjectKey; objectActive = snapshot.objectActive;
            activeArea = snapshot.workspaceArea > 0 ? (WorkspaceArea)(snapshot.workspaceArea - 1) : objectActive && ActiveObject != null ? (ActiveObject.Data.kind == ObjectKind.Player ? WorkspaceArea.Player : WorkspaceArea.Monster) : WorkspaceArea.Event;
            monsterKey = snapshot.monsterKey; playerKey = snapshot.playerKey;
            eventSearch = snapshot.eventSearch ?? ""; monsterSearch = snapshot.monsterSearch ?? ""; playerSearch = snapshot.playerSearch ?? "";
            assetSearch = IsEventArea ? eventSearch : activeArea == WorkspaceArea.Player ? playerSearch : monsterSearch;
            currentEventSO = AssetDatabase.LoadAssetAtPath<EventSO>(AssetDatabase.GUIDToAssetPath(snapshot.activeEventGuid ?? ""));
        }
        catch (Exception exception) { recoveryError = exception.Message; recoveryBlocked = true; Debug.LogWarning("[AllinOneEditor] " + recoveryError); }
        finally { restoring = false; }
    }

    private void PersistWorkspace()
    {
        if (restoring || recoveryBlocked) return;
        try
        {
            var snapshot = new ObjectWorkspaceRecovery.Snapshot
            {
                activeObjectKey = activeObjectKey, objectActive = objectActive,
                workspaceArea = (int)activeArea + 1, monsterKey = monsterKey, playerKey = playerKey,
                eventSearch = eventSearch, monsterSearch = monsterSearch, playerSearch = playerSearch,
                activeEventGuid = currentEventSO == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(currentEventSO))
            };
            foreach (var doc in objectDocuments.Where(d => d != null)) snapshot.objects.Add(ObjectWorkspaceRecovery.Capture(doc));
            foreach (var asset in eventDocuments.Where(e => e != null)) snapshot.eventGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
            ObjectWorkspaceRecovery.Write(snapshot);
            recoveryError = null;
        }
        catch (Exception exception) { recoveryError = "초안 복구 저장 실패: " + exception.Message; }
    }

    private void OnDestroy()
    {
        DisposeMapDraft();
        PersistWorkspace();
        foreach (var doc in objectDocuments.Where(d => d != null)) { Undo.ClearUndo(doc); DestroyImmediate(doc); }
    }

    public bool SetAnyTarget(UnityEngine.Object asset)
    {
        if (asset is EventSO eventAsset) { SetTarget(eventAsset); PersistWorkspace(); return true; }
        var entry = ObjectPrefabCatalog.Inspect(asset);
        if (!entry.Supported) { ShowBrowserMessage(entry.Problem, true); return false; }
        var definition = entry.Definition;
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition));
        var doc = objectDocuments.FirstOrDefault(d => d != null && d.SourceGuid == guid);
        if (doc == null) { doc = ObjectWorkspaceDocument.Create(definition); objectDocuments.Add(doc); }
        ActivateObject(doc); return true;
    }

    public void NewObject(ObjectKind kind)
    {
        var doc = ObjectWorkspaceDocument.Create(null, kind); objectDocuments.Add(doc); ActivateObject(doc);
        ClearBrowserSearch();
    }

    private void ImportLegacyDraft(ObjectDefinitionSO source, ObjectDefinitionData data)
    {
        var doc = source == null ? null : objectDocuments.FirstOrDefault(d => d != null && d.Source == source);
        if (doc == null || doc.IsDirty && ObjectMakerJson.Export(doc.Data) != ObjectMakerJson.Export(data))
        {
            doc = ObjectWorkspaceDocument.Create(source);
            doc.Key = "legacy:" + Guid.NewGuid().ToString("N"); objectDocuments.Add(doc);
        }
        doc.Data = data.Clone(); doc.Data.Sanitize();
        doc.Notice = "기존 2DObjectMaker 창의 초안을 복구했습니다.";
        ActivateObject(doc);
    }

    private void ActivateObject(ObjectWorkspaceDocument doc)
    {
        ShowBrowserMessage("파일을 여기에 놓아 불러오기");
        creatingEvent = false; simpleFingerprint = null;
        if (simplePage != null) simplePage.scrollOffset = Vector2.zero;
        EventSceneTargetField.Cancel(); ActivateAreaForObject(doc);
        activeObjectKey = doc.Key; objectActive = true;
        rootVisualElement.Focus(); PersistWorkspace(); RefreshDocument(); Repaint();
    }













    private void SelectObjectProperty(string section, int index = 0)
    {
        if (!IsObjectDocument) return;
        ActiveObject.Section = section; ActiveObject.Index = index;
        propertyScroll.scrollOffset = Vector2.zero;
        RefreshObjectWorkspace(); PersistWorkspace();
    }

    private static readonly string[] SectionIds = { "identity", "stats", "controls", "contact", "hit", "destruction", "motions", "spawns" };
    private static readonly string[] SectionNames = { "이름 · 기본 정보", "기본 스탯", "이동 · 점프", "접촉 피해", "피격", "제거 · 효과", "모션", "씬 배치" };

    private void RefreshObjectWorkspace()
    {
        if (objectCanvas == null || !IsObjectDocument) return;
        var doc = ActiveObject; bool player = doc.Data.kind == ObjectKind.Player;
        objectCanvas.Clear(); contextBar.Clear();
        contextBar.Add(Text(player ? "PLAYER / 입력 · 공격 구성" : "MONSTER / 조건 → 행동", "ew-context-title"));
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; contextBar.Add(spacer);
        var addItem = ActionButton(player ? "+ 공격" : "+ AI 규칙", () => MutateObject("항목 추가", d =>
        {
            if (player) { d.Data.player.attacks.Add(new ObjectPlayerAttack { displayName = "Attack " + (d.Data.player.attacks.Count + 1) }); d.Section = "attack"; d.Index = d.Data.player.attacks.Count - 1; }
            else { d.Data.ai.rules.Add(new ObjectAIRule()); d.Section = "rule"; d.Index = d.Data.ai.rules.Count - 1; }
        }), "ew-primary-button");
        addItem.SetEnabled(!doc.HasUnresolvedReferences); contextBar.Add(addItem);

        var intro = new VisualElement(); intro.AddToClassList("aio-object-intro"); objectCanvas.Add(intro);
        intro.Add(Text(player ? "PLAYER SETTINGS" : "MONSTER PREFAB", "ew-eyebrow"));
        intro.Add(Text(doc.Title, "aio-object-title"));
        intro.Add(Text(player ? "이동·점프 입력과 공격 키를 함께 확인하고, 공격별 모션 배속·콤보를 편집합니다." : "우선순위가 높은 규칙부터 검사합니다. 처음 맞는 조건 하나의 행동을 실행합니다.", "ew-flow-muted"));
        var facts = new VisualElement(); facts.AddToClassList("aio-facts"); intro.Add(facts);
        facts.Add(Text("HP  " + doc.Data.core.maxHealth, "ew-flow-chip"));
        facts.Add(Text("이동  " + doc.Data.core.moveSpeed.ToString("0.##"), "ew-flow-chip"));
        facts.Add(Text(player ? "공격 중 이동  " + (doc.Data.player.allowMovementDuringAttack ? "허용" : "제한") : doc.Data.ai.rules.Count + " 규칙", "ew-flow-chip"));
        var sections = new VisualElement(); sections.AddToClassList("aio-sections"); objectCanvas.Add(sections);
        for (int i = 0; i < SectionIds.Length; i++)
        {
            string section = SectionIds[i];
            if (i == 0 || i == 3 || i == 6) sections.Add(Text(i == 0 ? "기본 · 조작" : i == 3 ? "전투 · 반응" : "리소스 · 배치", "aio-section-group"));
            if (section == "controls" && !player || section == "contact" && player) continue;
            var button = ActionButton(SectionNames[i], () => SelectObjectProperty(section), "aio-section");
            button.EnableInClassList("is-active", doc.Section == section); sections.Add(button);
        }
        if (player)
        {
            var controls = ActionButton(doc.Data.player.moveLeftKey + " / " + doc.Data.player.moveRightKey + "  이동     " + doc.Data.player.runKey + "  달리기     " + doc.Data.player.jumpKey + "  점프", () => SelectObjectProperty("controls"), "aio-input-strip");
            objectCanvas.Add(controls);
        }
        objectCanvas.Add(Text(player ? "ATTACKS  /  키별 공격 · 콤보" : "RULES  /  위에서 아래로 우선 검사", "aio-list-title"));
        int count = player ? doc.Data.player.attacks.Count : doc.Data.ai.rules.Count;
        for (int i = 0; i < count; i++)
        {
            int index = i; string section = player ? "attack" : "rule";
            var row = new VisualElement { name = section + "-" + i }; row.AddToClassList("aio-rule");
            row.EnableInClassList("is-selected", doc.Section == section && doc.Index == i);
            row.RegisterCallback<ClickEvent>(e => SelectObjectProperty(section, index));
            var heading = new VisualElement(); heading.AddToClassList("aio-rule-head"); row.Add(heading);
            heading.Add(Text((i+1).ToString("00"), "aio-rule-number"));
            string label = player ? doc.Data.player.attacks[i].displayName : doc.Data.ai.rules[i].label;
            heading.Add(Text(string.IsNullOrWhiteSpace(label) ? "규칙 " + (i+1) : label, "aio-rule-title"));
            var gap = new VisualElement(); gap.style.flexGrow = 1; heading.Add(gap);
            AddRowAction(heading, "↑", () => MoveObjectItem(index, -1, player), i > 0);
            AddRowAction(heading, "↓", () => MoveObjectItem(index, 1, player), i < count - 1);
            AddRowAction(heading, "복제", () => DuplicateObjectItem(index, player), true);
            AddRowAction(heading, "×", () => RemoveObjectItem(index, player), true);
            var route = new VisualElement(); route.AddToClassList("aio-route"); row.Add(route);
            if (player)
            {
                var attack = doc.Data.player.attacks[i];
                route.Add(Text(attack.key.ToString(), "aio-condition")); route.Add(Text("→", "aio-arrow"));
                route.Add(Text(WorkspaceEnumLabel(attack.pattern) + "  ·  피해 " + attack.damage + "  ·  " + attack.comboSteps + "단 콤보", "aio-action"));
                row.Add(Text("전딜 " + attack.inputDelaySeconds.ToString("0.##") + "s   →   판정 " + attack.activeSeconds.ToString("0.##") + "s   →   후딜 " + attack.recoverySeconds.ToString("0.##") + "s     |     모션 ×" + attack.animationSpeed.ToString("0.##") + "     |     재사용 " + attack.cooldownSeconds.ToString("0.##") + "s", "aio-rule-detail"));
            }
            else
            {
                var rule = doc.Data.ai.rules[i];
                row.EnableInClassList("is-disabled", !rule.enabled);
                route.Add(Text(WorkspaceEnumLabel(rule.when), "aio-condition")); route.Add(Text("→", "aio-arrow")); route.Add(Text(WorkspaceEnumLabel(rule.action), "aio-action"));
                row.Add(Text(ObjectBehaviorCatalog.DescribeCondition(rule.when), "aio-rule-detail"));
                row.Add(Text(ObjectBehaviorCatalog.DescribeAction(rule.action), "aio-rule-detail"));
                if (!rule.enabled) row.Add(Text("사용 안 함 · 값은 보존됩니다", "ew-flow-inactive-badge"));
            }
            objectCanvas.Add(row);
        }
        if (count == 0) objectCanvas.Add(Text(player ? "아직 공격이 없습니다. + 공격으로 첫 입력을 만드세요." : "아직 규칙이 없습니다. + AI 규칙으로 조건과 행동을 연결하세요.", "ew-empty-text"));
        var outputs = new VisualElement(); outputs.AddToClassList("aio-outputs"); objectCanvas.Add(outputs);
        outputs.Add(Text(player ? "PLAYER PREFAB / 생성 결과" : "MONSTER PREFAB / 생성 결과", "ew-eyebrow"));
        outputs.Add(Text(doc.Source == null ? "만들기를 누르면 프리팹과 연결된 설정·애니메이터가 생성됩니다." : "프리팹에 적용하면 기존 설정과 애니메이터를 갱신합니다.", "ew-flow-muted"));
        if (doc.Source != null)
        {
            outputs.Add(Text(doc.Source.GeneratedPrefab != null ? AssetDatabase.GetAssetPath(doc.Source.GeneratedPrefab) : "아직 연결된 Prefab이 없습니다.", "ew-flow-muted"));
            var openPrefab = ActionButton("Prefab Mode에서 열기", () => AssetDatabase.OpenAsset(doc.Source.GeneratedPrefab)); openPrefab.SetEnabled(doc.Source.GeneratedPrefab != null); outputs.Add(openPrefab);
            var pingPrefab = ActionButton("Project에서 Prefab 찾기", () => EditorGUIUtility.PingObject(doc.Source.GeneratedPrefab)); pingPrefab.SetEnabled(doc.Source.GeneratedPrefab != null); outputs.Add(pingPrefab);
            outputs.Add(ActionButton("Animator 열기", () => { if (doc.Source.Data.animatorController != null) AssetDatabase.OpenAsset(doc.Source.Data.animatorController); }));
        }
        RefreshObjectInspectorHeader(); RefreshObjectStatus(); propertyGUI?.MarkDirtyRepaint();
    }

    private void AddRowAction(VisualElement row, string label, Action action, bool enabled)
    {
        var button = ActionButton(label, action, "aio-row-action"); button.SetEnabled(enabled && !ActiveObject.HasUnresolvedReferences);
        button.RegisterCallback<ClickEvent>(e => e.StopPropagation()); row.Add(button);
    }

    private void MutateObject(string undoName, Action<ObjectWorkspaceDocument> action, bool resolveMissing = false)
    {
        var doc = ActiveObject; if (doc == null) return;
        if (doc.HasUnresolvedReferences && !resolveMissing) return;
        simpleFingerprint = null; Undo.RecordObject(doc.Content, undoName); action(doc); EditorUtility.SetDirty(doc.Content);
        PersistWorkspace(); RefreshDocument();
    }
    private void MoveObjectItem(int index, int delta, bool player) => MutateObject("항목 우선순위 변경", doc =>
    {
        if (player) { var item = doc.Data.player.attacks[index]; doc.Data.player.attacks.RemoveAt(index); doc.Data.player.attacks.Insert(index + delta, item); }
        else { var item = doc.Data.ai.rules[index]; doc.Data.ai.rules.RemoveAt(index); doc.Data.ai.rules.Insert(index + delta, item); }
        doc.Section = player ? "attack" : "rule"; doc.Index = index + delta;
    });
    private void DuplicateObjectItem(int index, bool player) => MutateObject("항목 복제", doc =>
    {
        if (player) doc.Data.player.attacks.Insert(index + 1, doc.Data.player.attacks[index].Clone());
        else doc.Data.ai.rules.Insert(index + 1, doc.Data.ai.rules[index].Clone());
        doc.Section = player ? "attack" : "rule"; doc.Index = index + 1;
    });
    private void RemoveObjectItem(int index, bool player) => MutateObject("항목 삭제", doc =>
    {
        if (player) doc.Data.player.attacks.RemoveAt(index); else doc.Data.ai.rules.RemoveAt(index);
        doc.Index = Mathf.Max(0, index - 1); doc.Section = player ? "attack" : "rule";
    });

    private void RefreshObjectInspectorHeader()
    {
        if (!IsObjectDocument || inspectorTitle == null) return;
        var doc = ActiveObject; inspectorActions.Clear();
        int section = Array.IndexOf(SectionIds, doc.Section);
        inspectorTitle.text = section >= 0 ? SectionNames[section] : doc.Section == "attack" ? "공격 " + (doc.Index + 1) : "AI 규칙 " + (doc.Index + 1);
        inspectorCaption.text = doc.Title + "  /  초안 편집";
        inspectorTip.text = "오브젝트 초안 · 원본은 프리팹에 적용할 때 저장\n선택 전환·닫기·재시작 후에도 초안을 복구합니다.";
    }

    private void DrawObjectInspector()
    {
        var doc = ActiveObject; if (doc == null) return;
        float width = EditorGUIUtility.labelWidth; bool wide = EditorGUIUtility.wideMode;
        try
        {
            EditorGUIUtility.labelWidth = Mathf.Clamp(propertyGUI.contentRect.width * .43f, 115, 155);
            EditorGUIUtility.wideMode = propertyGUI.contentRect.width >= 300;
            if (!string.IsNullOrEmpty(recoveryError)) EditorGUILayout.HelpBox(recoveryError, MessageType.Error);
            if (doc.HasUnresolvedReferences) EditorGUILayout.HelpBox(doc.Notice + "\n원래 JSON은 보존됩니다. 에셋을 복구한 뒤 창을 다시 열거나 JSON으로 내보내세요.", MessageType.Warning);
            string blocker = ObjectPrefabCatalog.ApplyBlocker(doc.Source);
            if (!string.IsNullOrEmpty(blocker)) EditorGUILayout.HelpBox(blocker, MessageType.Warning);
            if (doc.SourceMissing || doc.HasConflict) EditorGUILayout.HelpBox(doc.SourceMissing ? "원본 에셋이 없습니다. 초안은 보존되어 있습니다." : "원본이 외부에서 변경되었습니다. 적용 전에 원본과 초안을 확인하세요.", MessageType.Warning);
            Undo.RecordObject(doc.Content, "오브젝트 속성 편집");
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(doc.HasUnresolvedReferences)) DrawObjectProperties(doc);
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(doc.Content); PersistWorkspace(); RequestRefresh(); }
        }
        finally { EditorGUIUtility.labelWidth = width; EditorGUIUtility.wideMode = wide; }
    }

    private void RefreshObjectStatus()
    {
        if (documentTitle == null || !IsObjectDocument) return;
        var doc = ActiveObject; bool dirty = doc.IsDirty;
        documentTitle.text = doc.Title; documentTitle.tooltip = doc.Source == null ? "새 오브젝트 초안" : AssetDatabase.GetAssetPath(doc.Source);
        documentState.text = dirty ? "● 초안 · 적용 전" : "✓ 적용됨"; documentState.EnableInClassList("is-dirty", dirty);
        saveButton.text = doc.Source == null ? (doc.Data.kind == ObjectKind.Player ? "플레이어 만들기" : "몬스터 만들기") : "프리팹에 적용";
        saveButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode && !doc.SourceMissing && !doc.HasConflict && !doc.HasUnresolvedReferences && !recoveryBlocked && string.IsNullOrEmpty(ObjectPrefabCatalog.ApplyBlocker(doc.Source)));
        status.text = !string.IsNullOrEmpty(recoveryError) ? "⚠ " + recoveryError : "OBJECT DRAFT    |    " + (doc.Notice ?? "로컬 복구 저장 · Make/Modify로 프로젝트 에셋 적용") + "    |    " + (doc.Source == null ? "새 문서" : AssetDatabase.GetAssetPath(doc.Source));
        status.tooltip = ObjectWorkspaceRecovery.FilePath;
    }

    private void BuildObject()
    {
        var doc = ActiveObject; if (doc == null || EditorApplication.isPlayingOrWillChangePlaymode || doc.SourceMissing || doc.HasConflict || doc.HasUnresolvedReferences || recoveryBlocked || !string.IsNullOrEmpty(ObjectPrefabCatalog.ApplyBlocker(doc.Source))) return;
        try { doc.Apply(); simpleFingerprint = null; PersistWorkspace(); prefabEntries = null; RefreshDocument(); }
        catch (Exception exception) { doc.Notice = "적용 실패: " + exception.Message; RefreshObjectStatus(); UpdateSimpleNotice(); Debug.LogError("[AllinOneEditor] " + exception); }
    }

    private void RevertObject()
    {
        var doc = ActiveObject; if (doc == null || doc.Source == null) return;
        if (!EditorUtility.DisplayDialog("초안 되돌리기", "현재 문서의 초안을 원본 Definition으로 되돌립니다. 다른 문서의 초안은 유지됩니다.", "되돌리기", "취소")) return;
        MutateObject("초안 되돌리기", d =>
        {
            d.Data = d.Source.Data.Clone(); d.Data.Sanitize(); ObjectBehaviorCatalog.ImportLegacyTuning(d.Data);
            d.Baseline = ObjectMakerJson.Export(d.Data); d.SourceBaseline = ObjectMakerJson.Export(d.Source.Data); d.UnresolvedJson = null; d.Notice = "원본을 다시 읽었습니다.";
        }, true);
    }
    private void ImportObjectJson()
    {
        string path = EditorUtility.OpenFilePanel("오브젝트 초안 JSON 가져오기", Application.dataPath, "json"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            var imported = ObjectMakerJson.ImportFile(path, out var warnings);
            if (!IsObjectDocument || ActiveObject.Data.kind != imported.kind) NewObject(imported.kind);
            MutateObject("JSON 가져오기", d => { d.Data = imported; d.UnresolvedJson = warnings.Length > 0 ? File.ReadAllText(path) : null; d.Section = "identity"; d.Notice = warnings.Length == 0 ? "JSON을 초안으로 가져왔습니다. 프리팹에 적용해 저장하세요." : string.Join("\n", warnings); }, true);
        }
        catch (Exception exception) { EditorUtility.DisplayDialog("가져오기 실패", exception.Message, "확인"); }
    }
    private void ExportObjectJson()
    {
        var doc = ActiveObject; if (!IsObjectDocument || doc == null) return;
        string path = EditorUtility.SaveFilePanel("오브젝트 초안 JSON 내보내기", Application.dataPath, ObjectMakerPaths.SafeFileName(doc.Title), "json");
        if (string.IsNullOrEmpty(path)) return;
        try { if (doc.HasUnresolvedReferences) File.WriteAllText(path, doc.UnresolvedJson); else ObjectMakerJson.ExportFile(path, doc.Data); doc.Notice = "JSON 내보냄: " + path; RefreshObjectStatus(); }
        catch (Exception exception) { EditorUtility.DisplayDialog("내보내기 실패", exception.Message, "확인"); }
    }
}
