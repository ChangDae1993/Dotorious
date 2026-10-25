using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEngine;

/// <summary>Searchable execution inventory; selecting an item opens the same original serialized Phase.</summary>
[Serializable]
internal sealed class EventStepListView
{
    [SerializeField] private string search = "";
    [SerializeField] private int category;
    [SerializeField] private Vector2 scroll, sourceScroll;
    [SerializeField] private bool showSources = true;
    [SerializeField] private bool showRules;
    [SerializeField] private string selectedPhasePath, selectedFeaturePath;
    [NonSerialized] internal Action<string, string> SelectionRequested;
    [NonSerialized] internal Action<string, string> EditRequested;
    [NonSerialized] private EventSO indexedAsset;
    [NonSerialized] private List<EventInvocationReference> references;
    [NonSerialized] private bool referencesDirty = true;

    internal void InvalidateSources() { referencesDirty = true; }
    internal void Reset() { scroll = Vector2.zero; search = ""; category = 0; selectedPhasePath = selectedFeaturePath = null; referencesDirty = true; }
    internal void SelectPhase(string phasePath, string featurePath = null)
    { selectedPhasePath = phasePath; selectedFeaturePath = featurePath; }

    internal void Draw(EventSO asset, EventStepListModel model, float height)
    {
        if (indexedAsset != asset || referencesDirty || references == null)
        {
            references = EventInvocationIndex.ScanLoadedScenes(asset);
            indexedAsset = asset;
            referencesDirty = false;
        }
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("검색", GUILayout.Width(30));
        search = GUILayout.TextField(search ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
        var categories = new[] { "모든 기능" }.Concat(model.Rows.Select(r => r.Category).Distinct().OrderBy(c => c)).ToArray();
        category = Mathf.Clamp(category, 0, categories.Length - 1);
        category = EditorGUILayout.Popup(category, categories, EditorStyles.toolbarPopup, GUILayout.Width(135));
        if (GUILayout.Button("초기화", EditorStyles.toolbarButton, GUILayout.Width(48))) { search = ""; category = 0; }
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedPhasePath)))
            if (GUILayout.Button("선택 항목 편집", EditorStyles.toolbarButton, GUILayout.Width(96)))
            { EditRequested?.Invoke(selectedPhasePath, selectedFeaturePath); GUIUtility.ExitGUI(); }
        EditorGUILayout.EndHorizontal();

        showSources = EditorGUILayout.Foldout(showSources, "실행 진입점 · 자동감시 / 현재 열린 씬 참조 " + references.Count + "개", true);
        if (showSources)
        {
            EditorGUILayout.HelpBox(EventInvocationIndex.AutomaticRegistration(asset), MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("현재 로드된 씬만 검사합니다. 저장 참조만으로 실제 실행을 보장하지 않습니다.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("참조 새로고침", GUILayout.Width(95))) referencesDirty = true;
            EditorGUILayout.EndHorizontal();
            if (references.Count > 0) sourceScroll = EditorGUILayout.BeginScrollView(sourceScroll, GUILayout.Height(Mathf.Min(180, Mathf.Max(80, references.Count * 85))));
            foreach (var reference in references)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(new GUIContent(reference.Description + "\n" + reference.Location, reference.Location),
                    EditorStyles.wordWrappedMiniLabel, GUILayout.MinHeight(31));
                if (GUILayout.Button("찾기", GUILayout.Width(42), GUILayout.Height(27)))
                { Selection.activeObject = reference.Source; EditorGUIUtility.PingObject(reference.Source); }
                EditorGUILayout.EndHorizontal();
            }
            if (references.Count > 0) EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("코드의 PlayEvent 호출·동적 연결은 자동 추적하지 않습니다. 스킬 성공 전용 연결은 별도 런타임 바인딩이 필요하며, 키 조건은 입력 자체를 뜻합니다.", EditorStyles.wordWrappedMiniLabel);
        }
        showRules = EditorGUILayout.Foldout(showRules, "조건과 시간 읽는 법", true);
        if (showRules)
            EditorGUILayout.HelpBox("한 조건 안의 체크 9종과 값 비교들은 AND입니다. 조건 분기는 위에서부터 검사하고 처음 맞은 하나만 실행합니다.\n" +
                "root Else는 직접 호출 때만 선택됩니다. 다음 Phase의 Else는 이전 단계가 끝난 뒤 선택될 수 있습니다.\n" +
                "Time Count는 게임 시작 후 경과 시간, Collision A/B는 충돌 이력입니다. 일반 연출은 Phase 안에서 함께 시작하고 선택지·Action은 연출 뒤에 진행합니다.\n" +
                "시간은 경로별 호출 시점을 기준으로 표시합니다. 가변·별도 종료는 확정 시각이 아닙니다.", MessageType.None);

        string query = (search ?? "").Trim();
        var visible = model.Rows.Where(row => (category == 0 || row.Category == categories[category]) &&
            (query.Length == 0 || Contains(row.Phase.Label + " " + row.Phase.Condition + " " + row.Title + " " + row.Target + " " + row.Content + " " + row.Category, query))).ToList();
        EditorGUILayout.LabelField(visible.Count + " / " + model.Rows.Count + "개 기능 · " + model.Phases.Count + "개 Phase · 선택 후 ‘선택 항목 편집’ 또는 더블클릭으로 편집합니다.", EditorStyles.miniLabel);
        float available = Mathf.Max(110, height - (showSources ? 126 + (references.Count > 0 ? Mathf.Min(180, Mathf.Max(80, references.Count * 85)) : 0) : 20) - (showRules ? 115 : 20) - 55);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(available));
        const float width = 1370;
        Rect header = GUILayoutUtility.GetRect(width, 25, GUILayout.MinWidth(width));
        EditorGUI.DrawRect(header, EditorGUIUtility.isProSkin ? new Color(.18f, .21f, .25f) : new Color(.80f, .83f, .87f));
        DrawCells(header, new[] { "순서 / Phase", "실행 조건", "시작", "종료", "대상", "기능과 내용" }, null, true);
        for (int i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            Rect rect = GUILayoutUtility.GetRect(width, 66, GUILayout.MinWidth(width));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, row.FeaturePath == selectedFeaturePath ? new Color(.2f, .5f, .85f, .25f) : i % 2 == 0 ? new Color(.5f, .5f, .5f, .07f) : Color.clear);
            string number = (model.Rows.IndexOf(row) + 1).ToString("00") + " · " + row.Phase.Label;
            if (!row.Phase.Reachable) number += "\n[미실행 저장 단계]";
            string[] text = { number, row.Phase.Condition, row.Start, row.End, row.Target, row.Title + "\n" + (row.Content ?? "").Replace('\n', ' ') };
            DrawCells(rect, text, row, false);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                selectedPhasePath = row.Phase.Path; selectedFeaturePath = row.FeaturePath;
                SelectionRequested?.Invoke(row.Phase.Path, row.FeaturePath);
                if (Event.current.clickCount > 1) EditRequested?.Invoke(row.Phase.Path, row.FeaturePath);
                Event.current.Use();
                GUIUtility.ExitGUI();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }
        if (visible.Count == 0)
            EditorGUILayout.HelpBox(query.Length > 0 ? "검색과 일치하는 항목이 없습니다." : "표시할 기능이 없습니다. 스텝 편집에서 Phase와 기능을 추가하세요.", MessageType.Info);
        EditorGUILayout.EndScrollView();
        if (model.Warnings.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", model.Warnings.Distinct()), MessageType.Warning);
    }

    private static bool Contains(string text, string query) => text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void DrawCells(Rect rect, string[] values, EventEditorRow row, bool header)
    {
        float[] widths = { 190, 300, 86, 100, 195, 499 };
        float x = rect.x;
        for (int i = 0; i < widths.Length; i++)
        {
            Rect cell = new Rect(x + 6, rect.y + 5, widths[i] - 12, rect.height - 9);
            string tooltip = row == null ? values[i] : i == 2 || i == 3 ? row.TimeNote : values[i] + "\n" + row.FeaturePath;
            GUI.Label(cell, new GUIContent(values[i], tooltip), header ? EditorStyles.boldLabel : EditorStyles.wordWrappedMiniLabel);
            if (i > 0) EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), new Color(.5f, .5f, .5f, .13f));
            x += widths[i];
        }
    }
}
