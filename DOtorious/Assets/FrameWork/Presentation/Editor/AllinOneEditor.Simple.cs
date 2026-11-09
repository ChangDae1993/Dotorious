using System;
using System.IO;
using System.Linq;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using JYW.Game.ObjectMaker.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public sealed partial class AllinOneEditor
{
    // The basic page edits the same documents as the detailed workspace. No second data model.
    [SerializeField] private bool detailedMode;
    private ScrollView simplePage;
    private VisualElement advancedWorkspace;
    private Button modeButton;
    private string simpleFingerprint;
    private bool creatingEvent;
    private Label simpleNotice, simplePreview;
    private string AreaName => IsEventArea ? "이벤트" : activeArea == WorkspaceArea.Player ? "플레이어" : "몬스터";

    private void ClearBrowserSearch()
    { assetSearch = ""; RememberSearch(); browserSearch?.SetValueWithoutNotify(""); RebuildBrowser(); PersistWorkspace(); }

    private void SetDetailed(bool value)
    {
        EventSceneTargetField.Cancel(); detailedMode = value; creatingEvent = false; simpleFingerprint = null;
        RefreshModeVisibility(); RefreshSimpleWorkspace(); Repaint();
    }
    private void RefreshModeVisibility()
    {
        if (simplePage == null) return;
        simplePage.style.display = detailedMode ? DisplayStyle.None : DisplayStyle.Flex;
        advancedWorkspace.style.display = detailedMode ? DisplayStyle.Flex : DisplayStyle.None;
        modeButton.text = detailedMode ? "← 기본 편집" : "상세 편집";
        modeButton.tooltip = detailedMode ? "필수 설정으로 돌아가기" : "타임라인 · 전체 구조 · 세부 설정";
        rootVisualElement.EnableInClassList("aio-basic", !detailedMode);
    }
    private void AddDraftsToBrowser()
    {
        if (IsEventArea) return;
        foreach (var doc in objectDocuments.Where(d => d != null && d.Data.kind == AreaKind))
        {
            if (!Matches(doc.Title + " " + AssetDatabase.GetAssetPath(doc.Source), assetSearch)) continue;
            var captured = doc;
            var item = ActionButton(doc.Title + (doc.IsDirty ? "  ●" : ""), () => ActivateObject(captured), "ew-asset-item");
            item.name = "draft-" + doc.Key;
            item.EnableInClassList("is-selected", IsObjectDocument && ActiveObject == doc);
            item.tooltip = doc.Source == null ? "새 프리팹 · 아직 만들기 전입니다. 초안은 자동 복구됩니다." : AssetDatabase.GetAssetPath(doc.Source);
            AddAssetContext(item, doc.Source?.GeneratedPrefab ?? (UnityEngine.Object)doc.Source); library.Add(item);
        }
    }
    private VisualElement SimpleSection(string title)
    {
        var box = new VisualElement(); box.AddToClassList("aio-simple-section"); simplePage.Add(box);
        if (!string.IsNullOrEmpty(title)) box.Add(Text(title, "aio-simple-heading"));
        return box;
    }
    private Foldout SimpleFold(string title, bool open = false)
    {
        var fold = new Foldout { text = title, value = open }; fold.AddToClassList("aio-simple-section");
        simplePage.Add(fold); return fold;
    }
    private void RefreshSimpleWorkspace()
    {
        if (IsMapArea || simplePage == null || detailedMode || creatingEvent) return;
        string key = IsObjectDocument ? ActiveObject.Key + ":" + ActiveObject.Data.player.attacks.Count + ":" + ActiveObject.Data.ai.rules.Count :
            (currentEventSO == null ? "none" : currentEventSO.GetEntityId().ToString()) + ":" + selectedPhasePath + ":" + selectedFeaturePath + ":" +
            (model == null ? "" : string.Join("|", model.Rows.Select(r => r.FeaturePath))) + ":" + currentEventSO?.UseCondition + ":" + currentEventSO?.Conditions?.Length;
        key = activeArea + ":" + key;
        if (simpleFingerprint == key) { UpdateSimpleNotice(); RefreshSimpleEventSummaries(); return; }
        simpleFingerprint = key;
        var scroll = simplePage.scrollOffset;
        simplePage.Clear(); simpleNotice = simplePreview = null;
        if (IsObjectDocument) DrawSimpleObject();
        else if (IsEventArea && currentEventSO != null) DrawSimpleEvent();
        else DrawSimpleEmpty();
        simplePage.scrollOffset = scroll;
        UpdateSimpleNotice();
    }
    private void DrawSimpleEmpty()
    {
        var box = SimpleSection(AreaName + " 만들기");
        box.Add(Text(IsEventArea ? "언제 실행할지와 어떤 동작을 할지 설정합니다." : "기본 설정부터 입력하고 게임에서 사용할 프리팹으로 저장합니다.", "aio-simple-hint"));
        box.Add(ActionButton("새 " + AreaName + " 만들기", () => { if (IsEventArea) CreateEvent(); else NewObject(AreaKind); }, "ew-primary-button"));
        box.Add(Text("기존 파일은 왼쪽 목록에서 선택하거나 불러오기 칸으로 드래그하세요.", "aio-simple-hint"));
        if (!string.IsNullOrEmpty(recoveryError)) box.Add(new HelpBox(recoveryError, HelpBoxMessageType.Error));
    }
    private void OpenObjectDetail(string section, int index = 0)
    { SelectObjectProperty(section, index); SetDetailed(true); }

    private TField DraftField<TField, TValue>(VisualElement parent, TField field, TValue value, Action<TValue> assign, string name)
        where TField : BaseField<TValue>
    {
        var doc = ActiveObject;
        field.name = name; field.SetValueWithoutNotify(value); field.AddToClassList("aio-simple-field");
        field.SetEnabled(!doc.HasUnresolvedReferences);
        field.RegisterValueChangedCallback(e =>
        {
            if (doc == null || doc.HasUnresolvedReferences) return;
            Undo.RecordObject(doc.Content, field.label + " 편집"); assign(e.newValue); EditorUtility.SetDirty(doc.Content);
            PersistWorkspace(); RefreshObjectStatus(); RebuildBrowser(); UpdateSimpleNotice();
        });
        parent.Add(field); return field;
    }
    private void DrawSimpleObject()
    {
        var doc = ActiveObject; bool player = doc.Data.kind == ObjectKind.Player;
        var basic = SimpleSection("기본 설정");
        DraftField(basic, new TextField("이름"), doc.Data.displayName, v => doc.Data.displayName = v, "basic-name");
        var stats = new VisualElement(); stats.AddToClassList("aio-simple-grid"); basic.Add(stats);
        DraftField(stats, new IntegerField("체력"), doc.Data.core.maxHealth, v => doc.Data.core.maxHealth = v, "basic-health");
        DraftField(stats, new FloatField("이동 속도"), doc.Data.core.moveSpeed, v => doc.Data.core.moveSpeed = v, "basic-speed");
        if (!player) DraftField(stats, new IntegerField("공격력"), doc.Data.core.attackPower, v => doc.Data.core.attackPower = v, "basic-damage");
        DraftField(stats, new IntegerField("방어력"), doc.Data.core.defense, v => doc.Data.core.defense = v, "basic-defense");
        if (player) DrawSimplePlayer(doc);
        else
        {
            var ai = SimpleFold("AI 행동 · " + doc.Data.ai.rules.Count + "개 규칙");
            ai.name = "basic-ai";
            ai.Add(Text("위에서 처음 맞는 조건의 행동을 실행합니다.", "aio-simple-hint"));
            for (int i = 0; i < doc.Data.ai.rules.Count; i++)
            {
                int index = i; var rule = doc.Data.ai.rules[i];
                ai.Add(ActionButton((i + 1) + ". " + (rule.enabled ? "" : "[꺼짐] ") + WorkspaceEnumLabel(rule.when) + " → " + WorkspaceEnumLabel(rule.action), () => OpenObjectDetail("rule", index), "aio-simple-action"));
            }
            ai.Add(ActionButton("+ 행동 추가", () => { MutateObject("AI 규칙 추가", d => { d.Data.ai.rules.Add(new ObjectAIRule()); d.Section = "rule"; d.Index = d.Data.ai.rules.Count - 1; }); OpenObjectDetail("rule", doc.Data.ai.rules.Count - 1); }));
        }
        var extra = SimpleFold("추가 설정 · 모션 / 피격 / 효과 / 배치"); extra.name = "basic-extra";
        var links = new VisualElement(); links.AddToClassList("aio-simple-links"); extra.Add(links);
        for (int i = 0; i < SectionIds.Length; i++)
        {
            string section = SectionIds[i];
            if (section == "stats" || section == "controls" || section == "contact" && player) continue;
            string label = section == "identity" ? "식별자 · 방향 · 설명" : SectionNames[i];
            links.Add(ActionButton(label, () => OpenObjectDetail(section)));
        }
        var check = SimpleSection("저장 전 확인");
        var renderer = doc.Source?.GeneratedPrefab?.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.sprite != null);
        if (renderer != null)
        {
            var preview = new Image { sprite = renderer.sprite, scaleMode = ScaleMode.ScaleToFit, tooltip = "저장된 프리팹 이미지" };
            preview.style.width = 80; preview.style.height = 80; check.Add(preview);
        }
        simplePreview = Text("", "aio-simple-hint"); check.Add(simplePreview);
        simpleNotice = Text("", "aio-simple-notice"); check.Add(simpleNotice);
        if (doc.Source?.GeneratedPrefab != null)
        {
            var actions = new VisualElement(); actions.AddToClassList("aio-simple-links"); check.Add(actions);
            actions.Add(ActionButton("프리팹 열기", () => AssetDatabase.OpenAsset(doc.Source.GeneratedPrefab)));
            actions.Add(ActionButton("Project에서 찾기", () => EditorGUIUtility.PingObject(doc.Source.GeneratedPrefab)));
        }
    }
    private void DrawSimplePlayer(ObjectWorkspaceDocument doc)
    {
        var move = SimpleFold("이동 · 점프", true); move.name = "basic-controls";
        var grid = new VisualElement(); grid.AddToClassList("aio-simple-grid"); move.Add(grid);
        DraftField(grid, new EnumField("왼쪽 키", doc.Data.player.moveLeftKey), (Enum)doc.Data.player.moveLeftKey, v => doc.Data.player.moveLeftKey = (Key)v, "basic-left-key");
        DraftField(grid, new EnumField("오른쪽 키", doc.Data.player.moveRightKey), (Enum)doc.Data.player.moveRightKey, v => doc.Data.player.moveRightKey = (Key)v, "basic-right-key");
        DraftField(grid, new EnumField("달리기 키", doc.Data.player.runKey), (Enum)doc.Data.player.runKey, v => doc.Data.player.runKey = (Key)v, "basic-run-key");
        DraftField(grid, new FloatField("달리기 배속"), doc.Data.player.runSpeedMultiplier, v => doc.Data.player.runSpeedMultiplier = v, "basic-run-speed");
        DraftField(grid, new EnumField("점프 키", doc.Data.player.jumpKey), (Enum)doc.Data.player.jumpKey, v => doc.Data.player.jumpKey = (Key)v, "basic-jump-key");
        DraftField(grid, new FloatField("점프 힘"), doc.Data.player.jumpForce, v => doc.Data.player.jumpForce = v, "basic-jump-force");
        DraftField(move, new Toggle("공격 중 이동 허용"), doc.Data.player.allowMovementDuringAttack, v => doc.Data.player.allowMovementDuringAttack = v, "basic-attack-movement");
        move.Add(ActionButton("지면 판정 · 이동 소리…", () => OpenObjectDetail("controls")));
        var attacks = SimpleFold("공격 · " + doc.Data.player.attacks.Count + "개", true); attacks.name = "basic-attacks";
        for (int i = 0; i < doc.Data.player.attacks.Count; i++)
        {
            int index = i;
            var row = new Foldout { text = doc.Data.player.attacks[i].displayName + "  ·  " + doc.Data.player.attacks[i].key, value = false, name = "basic-attack-" + i };
            row.AddToClassList("aio-simple-attack"); attacks.Add(row);
            DraftField(row, new TextField("공격 이름"), doc.Data.player.attacks[i].displayName, v => { doc.Data.player.attacks[index].displayName = v; row.text = v + "  ·  " + doc.Data.player.attacks[index].key; }, "basic-attack-name-" + i);
            DraftField(row, new EnumField("공격 키", doc.Data.player.attacks[i].key), (Enum)doc.Data.player.attacks[i].key, v => { doc.Data.player.attacks[index].key = (Key)v; row.text = doc.Data.player.attacks[index].displayName + "  ·  " + v; }, "basic-attack-key-" + i);
            DraftField(row, new IntegerField("피해량"), doc.Data.player.attacks[i].damage, v => doc.Data.player.attacks[index].damage = v, "basic-attack-damage-" + i);
            DraftField(row, new FloatField("모션 배속"), doc.Data.player.attacks[i].animationSpeed, v => doc.Data.player.attacks[index].animationSpeed = v, "basic-attack-speed-" + i);
            row.Add(ActionButton("판정 · 타이밍 · 콤보 · 효과…", () => OpenObjectDetail("attack", index)));
        }
        attacks.Add(ActionButton("+ 공격 추가", () => { MutateObject("공격 추가", d => d.Data.player.attacks.Add(new ObjectPlayerAttack { displayName = "새 공격" })); var row = simplePage.Q<Foldout>("basic-attack-" + (doc.Data.player.attacks.Count - 1)); if (row != null) row.value = true; }));
    }
    private void UpdateSimpleNotice()
    {
        if (!IsObjectDocument || simpleNotice == null) return;
        var doc = ActiveObject;
        string warning = recoveryError ?? (doc.HasUnresolvedReferences ? doc.Notice : doc.SourceMissing ? "원본 파일이 없습니다. 더 보기에서 JSON으로 초안을 보관하고 원본을 복구하세요." : doc.HasConflict ? "원본이 바뀌었습니다. 더 보기에서 초안을 내보낸 뒤 원본을 다시 불러오세요." : ObjectPrefabCatalog.ApplyBlocker(doc.Source));
        if (string.IsNullOrEmpty(warning) && doc.Notice != null && doc.Notice.StartsWith("적용 실패:", StringComparison.Ordinal)) warning = doc.Notice;
        simpleNotice.text = warning ?? (doc.IsDirty ? "아직 프리팹에 저장하지 않았습니다. 위의 ‘" + saveButton.text + "’를 누르세요. 초안은 자동 복구됩니다." : "프리팹에 저장되었습니다.");
        simpleNotice.EnableInClassList("is-error", !string.IsNullOrEmpty(warning));
        simplePreview.text = doc.Title + "  ·  체력 " + doc.Data.core.maxHealth + "  ·  이동 " + doc.Data.core.moveSpeed.ToString("0.##") +
            (doc.Source == null ? "\n새 프리팹과 연결 설정을 만듭니다. 이미지는 생성 후 프리팹에서 지정하세요." : "\n기존 이미지·애니메이션을 유지하며 프리팹의 설정을 갱신합니다.");
    }
    private IMGUIContainer SimpleEventGUI(Action draw)
    {
        var gui = new IMGUIContainer(); gui.AddToClassList("aio-simple-gui");
        gui.onGUIHandler = () =>
        {
            if (embeddedEditor == null) return;
            float old = EditorGUIUtility.labelWidth; bool wide = EditorGUIUtility.wideMode;
            try { EditorGUIUtility.labelWidth = 170; EditorGUIUtility.wideMode = true; draw(); }
            finally { EditorGUIUtility.labelWidth = old; EditorGUIUtility.wideMode = wide; }
        };
        return gui;
    }
    private void RefreshSimpleEventSummaries()
    {
        if (!IsEventArea || model == null) return;
        var rows = model.Rows.Where(r => r.Phase.Path == selectedPhasePath && r.FeaturePath != r.Phase.Path).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            var button = simplePage.Q<Button>("basic-event-action-" + i);
            if (button != null) button.text = rows[i].Title + (string.IsNullOrEmpty(rows[i].Target) || rows[i].Target == "—" ? "" : "  ·  " + rows[i].Target);
        }
        var summary = simplePage.Q<Label>("basic-phase-summary");
        if (summary != null) summary.text = model.Phases.FirstOrDefault(p => p.Path == selectedPhasePath)?.Condition.Replace("Phase", "단계");
    }
    private void DrawBasicEventFeature()
    {
        // Common move authoring stays compact; every original movement option remains in detail editing.
        using (var serialized = new SerializedObject(currentEventSO))
        {
            var item = serialized.FindProperty(selectedFeaturePath);
            if (item != null && selectedFeaturePath.Contains(".Transforms.MoveObject.MoveObjects.Array.data[") && item.FindPropertyRelative("objectName") != null)
            {
                EventSceneTargetField.PropertyField(item.FindPropertyRelative("objectName"), new GUIContent("움직일 대상"));
                bool drawer = item.FindPropertyRelative("isDrawer")?.boolValue == true;
                if (drawer) EditorGUILayout.PropertyField(item.FindPropertyRelative("drawerOffset"), new GUIContent("이동 거리"));
                else
                {
                    var relative = item.FindPropertyRelative("isRelative");
                    EditorGUILayout.PropertyField(relative, new GUIContent("다른 대상 기준으로 이동"));
                    if (relative.boolValue) EventSceneTargetField.PropertyField(item.FindPropertyRelative("targetName"), new GUIContent("기준 대상"));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("targetPosition"), new GUIContent(relative.boolValue ? "기준에서 떨어진 거리" : "도착 위치"));
                }
                EditorGUILayout.PropertyField(item.FindPropertyRelative("Duration"), new GUIContent("이동 시간 (초)"));
                bool custom = drawer || item.FindPropertyRelative("IsAnotherStartPosition")?.boolValue == true || item.FindPropertyRelative("ShowLoadingScreen")?.boolValue == true || item.FindPropertyRelative("movementType")?.enumValueIndex != 0;
                if (custom) EditorGUILayout.HelpBox("추가 이동 설정을 사용 중입니다. 전체 동작 설정에서 확인할 수 있습니다.", MessageType.None);
                serialized.ApplyModifiedProperties(); return;
            }
        }
        embeddedEditor.DrawWorkspaceFeature(selectedPhasePath, selectedFeaturePath);
    }
    private void DrawSimpleEvent()
    {
        var conditions = SimpleSection("1  시작 조건");
        conditions.Add(Text(currentEventSO.UseCondition ? "조건에 맞는 분기를 실행합니다. 자동 감시는 저장 위치와 조건 구성을 함께 확인하세요." : "트리거나 코드에서 이 이벤트를 호출하면 시작합니다.", "aio-simple-hint"));
        var edit = new Foldout { text = "조건 설정", value = false, name = "basic-event-conditions" };
        edit.Add(SimpleEventGUI(() => embeddedEditor.DrawExecutionSettings())); conditions.Add(edit);
        var actions = SimpleSection("2  실행할 동작");
        if (model.Phases.Count > 1)
        {
            var phases = model.Phases.Select(p => p.Label.Replace("Phase", "단계")).ToList();
            var picker = new DropdownField("편집할 단계", phases, Mathf.Max(0, model.Phases.FindIndex(p => p.Path == selectedPhasePath))) { name = "basic-event-phase" };
            picker.RegisterValueChangedCallback(e => SelectOnly(model.Phases[picker.index].Path, null)); actions.Add(picker);
        }
        var phase = model.Phases.FirstOrDefault(p => p.Path == selectedPhasePath);
        if (phase != null) { var summary = Text(phase.Condition.Replace("Phase", "단계"), "aio-simple-hint"); summary.name = "basic-phase-summary"; actions.Add(summary); }
        var rows = model.Rows.Where(r => r.Phase.Path == selectedPhasePath && r.FeaturePath != r.Phase.Path).ToList();
        if (rows.Count > 0)
        {
            actions.Add(Text("같은 단계의 일반 연출은 함께 시작합니다. 완료 후 선택지 → 액션 → 다음 단계로 이어집니다.", "aio-simple-hint"));
            foreach (var row in rows)
            {
                var captured = row;
                var button = ActionButton(row.Title + (string.IsNullOrEmpty(row.Target) || row.Target == "—" ? "" : "  ·  " + row.Target), () => SelectOnly(captured.Phase.Path, captured.FeaturePath), "aio-simple-action");
                button.name = "basic-event-action-" + rows.IndexOf(row);
                button.EnableInClassList("is-selected", row.FeaturePath == selectedFeaturePath); actions.Add(button);
            }
        }
        else actions.Add(Text("아직 동작이 없습니다. 동작 추가에서 이동·대사·카메라 등을 고르세요.", "aio-simple-hint"));
        var add = ActionButton("+ 동작 추가", () =>
        {
            if (string.IsNullOrEmpty(selectedPhasePath)) EventAuthoring.EnsureFirstPhase(currentEventSO, out selectedPhasePath);
            OpenFeaturePicker(selectedPhasePath, actions.worldBound);
        }, "ew-primary-button"); add.name = "basic-event-add"; actions.Add(add);
        if (!string.IsNullOrEmpty(selectedFeaturePath) && selectedFeaturePath != selectedPhasePath &&
            embeddedEditor.TryGetWorkspaceFeatureInfo(selectedPhasePath, selectedFeaturePath, out var title, out bool enabled, out bool entry))
        {
            var selected = SimpleSection(title); selected.name = "basic-event-selected";
            var use = new Toggle("동작 사용") { value = enabled };
            use.RegisterValueChangedCallback(e => { embeddedEditor.SetWorkspaceFeatureEnabled(selectedPhasePath, selectedFeaturePath, e.newValue); RequestRefresh(); }); selected.Add(use);
            selected.Add(SimpleEventGUI(DrawBasicEventFeature));
            selected.Add(ActionButton("전체 동작 설정…", () => { SetDetailed(true); SetInspectorMode(0); RefreshSelection(); }));
            if (entry) selected.Add(ActionButton("이 항목 삭제", () => { embeddedEditor.RemoveWorkspaceFeature(selectedPhasePath, selectedFeaturePath); selectedFeaturePath = null; RequestRefresh(); }));
        }
        var detail = SimpleFold("단계 연결 · 실행 위치 · 타임라인"); detail.name = "basic-event-more";
        var links = new VisualElement(); links.AddToClassList("aio-simple-links"); detail.Add(links);
        links.Add(ActionButton("다음 단계 추가", () => AddNextPhase(selectedPhasePath)));
        links.Add(ActionButton("단계 조건 · 연결", () => { SetDetailed(true); SetInspectorMode(0); selectedFeaturePath = null; RefreshSelection(); }));
        links.Add(ActionButton("실행 위치 확인", () => { SetDetailed(true); SetView(1); }));
        links.Add(ActionButton("타임라인", () => { SetDetailed(true); SetView(2); }));
        links.Add(ActionButton("전체 구조", () => { SetDetailed(true); SetInspectorMode(2); }));
        if (model.Warnings.Count > 0) detail.Add(new HelpBox(string.Join("\n", model.Warnings), HelpBoxMessageType.Warning));
    }
    private void BeginNewEvent()
    {
        ShowBrowserMessage("파일을 여기에 놓아 불러오기");
        EventSceneTargetField.Cancel(); ActivateEventArea(); detailedMode = false; creatingEvent = true; RefreshModeVisibility(); RebuildBrowser(); RefreshAreaChrome();
        simplePage.Clear(); simplePage.scrollOffset = Vector2.zero;
        var box = SimpleSection("새 이벤트 만들기");
        var name = new TextField("이름") { value = "새 이벤트", name = "new-event-name" }; box.Add(name);
        string folder = currentEventSO == null ? "Assets/Resources/EventSO" : Path.GetDirectoryName(AssetDatabase.GetAssetPath(currentEventSO)).Replace('\\', '/');
        var location = new Foldout { text = "저장 위치", value = false }; box.Add(location);
        var path = new TextField("폴더") { value = folder, name = "new-event-folder" }; location.Add(path);
        location.Add(Text("Assets 아래에 저장합니다. 이름이 같으면 새 번호를 붙여 기존 파일을 보존합니다.", "aio-simple-hint"));
        box.Add(Text("만든 뒤 시작 조건과 동작을 설정하세요.", "aio-simple-hint"));
        var error = Text("", "aio-simple-notice"); error.AddToClassList("is-error"); box.Add(error);
        var row = new VisualElement(); row.AddToClassList("aio-simple-links"); box.Add(row);
        var create = ActionButton("만들기", () =>
        {
            try { var asset = CreateNamedEvent(name.value, path.value); creatingEvent = false; simpleFingerprint = null; SetTarget(asset); ClearBrowserSearch(); }
            catch (Exception exception) { error.text = exception.Message; }
        }, "ew-primary-button"); create.name = "new-event-create"; row.Add(create);
        row.Add(ActionButton("취소", CancelNewEvent));
        saveButton.SetEnabled(false); documentTitle.text = "새 이벤트"; documentState.text = "";
        name.schedule.Execute(() => { name.Focus(); name.SelectAll(); });
    }
    private void CancelNewEvent() { creatingEvent = false; simpleFingerprint = null; RefreshDocument(); }
    internal static EventSO CreateNamedEvent(string title, string folder)
    {
        title = (title ?? "").Trim(); folder = (folder ?? "").Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(title) || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || title == "." || title == "..") throw new ArgumentException("파일에 사용할 이름을 입력하세요. / 또는 : 같은 문자는 사용할 수 없습니다.");
        if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Split('/').Any(p => p == "." || p == ".." || string.IsNullOrWhiteSpace(p) || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new ArgumentException("저장 폴더는 Assets 아래 경로를 입력하세요.");
        string current = "Assets";
        foreach (var part in folder.Split('/').Skip(1)) { string next = current + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part); current = next; }
        var asset = CreateInstance<EventSO>(); asset.UseCondition = false;
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + title + ".asset"));
        EventAuthoring.EnsureFirstPhase(asset, out _); AssetDatabase.SaveAssetIfDirty(asset); return asset;
    }
}
