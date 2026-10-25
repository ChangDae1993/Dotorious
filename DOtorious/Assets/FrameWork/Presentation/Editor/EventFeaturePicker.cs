using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEngine;

/// <summary>A cancelled picker never creates a phase or touches the EventSO.</summary>
internal sealed class EventFeaturePicker : PopupWindowContent
{
    private readonly EventSO asset;
    private readonly string phasePath;
    private readonly EventSO.EventStep openedStep;
    private readonly string phaseLabel;
    private readonly Action<string, string> onAdded;
    private string search = string.Empty;
    private Vector2 scroll;
    private int selected;
    private bool focusSearch = true;
    private GUIStyle entryStyle, descriptionStyle, headingStyle, categoryStyle, hintStyle;

    private EventFeaturePicker(EventSO asset, string phasePath, Action<string, string> onAdded)
    {
        this.asset = asset; this.phasePath = phasePath; this.onAdded = onAdded;
        openedStep = EventAuthoring.GetStep(asset, phasePath);
        phaseLabel = EventTimelineBuilder.Build(asset).Steps.FirstOrDefault(step => step.PropertyPath == phasePath)?.Label;
        phaseLabel = string.IsNullOrEmpty(phaseLabel) ? (string.IsNullOrEmpty(phasePath) ? "첫 Phase" : "선택한 Phase") :
            phaseLabel.Replace("Manual", "수동 실행").Replace("Condition", "조건").Replace("Else", "그 외");
    }

    internal static void Show(Rect activatorRect, EventSO asset, string phasePath, Action<string, string> onAdded)
    {
        if (asset == null) return;
        PopupWindow.Show(activatorRect, new EventFeaturePicker(asset, phasePath, onAdded));
    }

    public override Vector2 GetWindowSize() => new Vector2(470, 550);
    public override void OnOpen() { editorWindow.wantsMouseMove = true; }

    public override void OnGUI(Rect rect)
    {
        if (entryStyle == null)
        {
            entryStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, padding = new RectOffset(12, 12, 0, 0) };
            entryStyle.normal.textColor = new Color(.88f, .93f, 1f);
            descriptionStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { padding = new RectOffset(12, 12, 0, 0), fontSize = 11 };
            descriptionStyle.normal.textColor = new Color(.62f, .70f, .82f);
            headingStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17, padding = new RectOffset(12, 12, 0, 0) };
            headingStyle.normal.textColor = new Color(.94f, .97f, 1);
            categoryStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, padding = new RectOffset(12, 12, 0, 0) };
            categoryStyle.normal.textColor = new Color(.48f, .66f, .95f);
            hintStyle = new GUIStyle(descriptionStyle) { fontSize = 10 };
        }
        EditorGUI.DrawRect(rect, new Color(.105f, .135f, .18f));
        Event input = Event.current;
        bool keyDown = input.type == EventType.KeyDown;
        KeyCode key = input.keyCode;
        if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
        {
            input.Use(); editorWindow.Close(); return;
        }
        GUILayout.Space(14);
        GUILayout.Label("기능 추가", headingStyle, GUILayout.Height(24));
        GUILayout.Label(phaseLabel, categoryStyle, GUILayout.Height(19));
        GUILayout.Label("같은 Phase의 연출은 함께 시작합니다. 순서가 필요하면 다음 Phase를 추가하세요.",
            descriptionStyle);
        GUILayout.Space(5);
        GUI.SetNextControlName("EventFeatureSearch");
        EditorGUI.BeginChangeCheck();
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck()) { selected = 0; scroll = Vector2.zero; }
        if (focusSearch && input.type == EventType.Repaint)
        {
            EditorGUI.FocusTextInControl("EventFeatureSearch"); focusSearch = false;
        }
        List<EventAuthoring.Feature> matches = EventAuthoring.Features.Where(Matches).ToList();
        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, matches.Count - 1));
        if (keyDown && (key == KeyCode.DownArrow || key == KeyCode.UpArrow))
        {
            selected = Mathf.Clamp(selected + (key == KeyCode.DownArrow ? 1 : -1), 0, Mathf.Max(0, matches.Count - 1));
            scroll.y = Mathf.Max(0, selected * 58 - 100);
            input.Use(); editorWindow.Repaint();
        }
        else if (keyDown && (key == KeyCode.Return || key == KeyCode.KeypadEnter) && matches.Count > 0)
        {
            input.Use(); Choose(matches[selected]); return;
        }
        GUILayout.Space(5);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EventAuthoring.Feature chosen = null;
        string category = null;
        for (int i = 0; i < matches.Count; i++)
        {
            var feature = matches[i];
            if (category != feature.Category)
            {
                category = feature.Category;
                GUILayout.Space(6);
                GUILayout.Label(category, categoryStyle);
            }
            Rect row = GUILayoutUtility.GetRect(10, 55, GUILayout.ExpandWidth(true));
            bool hover = row.Contains(input.mousePosition);
            if (i == selected || hover)
            {
                EditorGUI.DrawRect(row, new Color(.19f, .29f, .45f));
                EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), new Color(.45f, .65f, 1));
            }
            if (GUI.Button(row, new GUIContent("",
                    feature.EntriesPath == null ? "기존 설정을 유지하며 활성화합니다." : "기존 항목 뒤에 새 항목을 추가합니다."), GUIStyle.none))
                chosen = feature;
            GUI.Label(new Rect(row.x, row.y + 7, row.width, 19), feature.Title, entryStyle);
            GUI.Label(new Rect(row.x, row.y + 27, row.width, 27), feature.Description, descriptionStyle);
            if (hover && input.type == EventType.MouseMove) { selected = i; editorWindow.Repaint(); }
        }
        if (matches.Count == 0) EditorGUILayout.HelpBox("검색 결과가 없습니다. 기능 이름이나 카테고리로 검색하세요.", MessageType.Info);
        EditorGUILayout.EndScrollView();
        GUILayout.Space(5);
        GUILayout.Label("Enter 선택 · Esc 취소 · 단일 기능은 기존 값 유지 · 목록 기능은 새 항목 추가", hintStyle);
        GUILayout.Space(8);
        if (chosen != null) Choose(chosen);
    }

    private bool Matches(EventAuthoring.Feature feature)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string text = feature.Title + " " + feature.Category + " " + feature.Description + " " + feature.DataPath;
        return search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .All(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void Choose(EventAuthoring.Feature feature)
    {
        // Finish the popup GUI before mutating its host's layout or serialized arrays.
        editorWindow.Close();
        EditorApplication.delayCall += () =>
        {
            if (asset == null) return;
            if (!string.IsNullOrEmpty(phasePath) && !ReferenceEquals(openedStep, EventAuthoring.GetStep(asset, phasePath)))
            {
                Debug.LogWarning("선택 중 Phase 구조가 바뀌어 기능 추가를 취소했습니다. 원하는 Phase에서 다시 추가하세요.", asset);
                return;
            }
            if (!EventAuthoring.AddFeature(asset, phasePath, feature.DataPath, out string path)) return;
            int featureStart = path.IndexOf("." + feature.DataPath, StringComparison.Ordinal);
            string actualPhase = featureStart >= 0 ? path.Substring(0, featureStart) : phasePath;
            onAdded?.Invoke(actualPhase, path);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        };
    }
}
