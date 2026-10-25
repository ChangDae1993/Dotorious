using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using JYW.Game.EventPlay;

/// <summary>IMGUI timeline with explicit, undoable authoring commands. Drawing never repairs source data.</summary>
[Serializable]
internal sealed class EventTimelineView
{
    [SerializeField] private int stepIndex;
    [SerializeField] private float zoom = 1;
    [SerializeField] private Vector2 scroll;
    [SerializeField] private string selectedPath;
    [SerializeField] private string selectedPhasePath;
    [SerializeField] private double cursorTime;
    [NonSerialized] private EventTimelineDocument document;
    [NonSerialized] private EventSO documentAsset;
    [NonSerialized] internal Action<string, string> SelectionRequested;
    [NonSerialized] internal Action<string, string> EditRequested;

    private const float RulerHeight = 32, RowHeight = 56, HeaderHeight = 25;
    private static readonly Color Background = new Color(.075f, .09f, .12f);
    private static readonly Color Grid = new Color(.23f, .27f, .33f);
    private static readonly Dictionary<int, GUIStyle> TextStyles = new Dictionary<int, GUIStyle>();

    internal void Reset()
    {
        stepIndex = 0; scroll = Vector2.zero; selectedPath = null; selectedPhasePath = null; cursorTime = 0;
        document = null;
    }

    internal void SelectPhase(string phasePath, string featurePropertyPath = null)
    {
        selectedPhasePath = phasePath; selectedPath = featurePropertyPath;
        if (document != null)
        {
            int index = document.Steps.FindIndex(step => step.PropertyPath == phasePath);
            if (index >= 0) stepIndex = index;
        }
    }

    internal void Draw(EventSO asset, bool result, float availableHeight)
    {
        if (document == null || documentAsset != asset || Event.current.type == EventType.Layout)
        {
            document = EventTimelineBuilder.Build(asset);
            documentAsset = asset;
        }
        int selectedIndex = document.Steps.FindIndex(step => step.PropertyPath == selectedPhasePath);
        if (selectedIndex >= 0) stepIndex = selectedIndex;
        stepIndex = Mathf.Clamp(stepIndex, 0, Math.Max(0, document.Steps.Count - 1));
        if (document.Steps.Count > 0 && selectedIndex < 0) selectedPhasePath = document.Steps[stepIndex].PropertyPath;
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(result || stepIndex == 0))
            if (GUILayout.Button("←", EditorStyles.toolbarButton, GUILayout.Width(30))) ChangeStep(stepIndex - 1);
        GUILayout.Label(result ? "전체 Phase" : "Phase " + (document.Steps.Count == 0 ? 0 : stepIndex + 1) + " / " + document.Steps.Count,
            EditorStyles.centeredGreyMiniLabel, GUILayout.Width(100));
        using (new EditorGUI.DisabledScope(result || stepIndex + 1 >= document.Steps.Count))
            if (GUILayout.Button("→", EditorStyles.toolbarButton, GUILayout.Width(30))) ChangeStep(stepIndex + 1);
        if (GUILayout.Button("+ 기능", EditorStyles.toolbarButton, GUILayout.Width(57)))
            OpenPicker(GUILayoutUtility.GetLastRect(), selectedPhasePath);
        if (GUILayout.Button("+ 다음 Phase", EditorStyles.toolbarButton, GUILayout.Width(93))) QueueNextPhase(selectedPhasePath);
        GUILayout.FlexibleSpace();
        GUILayout.Label("확대", EditorStyles.miniLabel, GUILayout.Width(26));
        zoom = GUILayout.HorizontalSlider(zoom, .5f, 8, GUILayout.Width(72));
        if (GUILayout.Button("맞춤", EditorStyles.toolbarButton, GUILayout.Width(36))) { zoom = 1; scroll.x = 0; }
        EditorGUILayout.EndHorizontal();

        string caption = result ? "전체 타임라인 · Phase 시간 누적" : document.Steps.Count > 0 ? document.Steps[stepIndex].Label : "등록된 Phase 없음";
        if (result && document.HasBranches) caption += " · Condition / Else는 서로 다른 실행 경로";
        EditorGUILayout.LabelField(caption, EditorStyles.wordWrappedMiniLabel);

        IList<EventTimelineStep> steps = result ? document.Steps :
            document.Steps.Count > 0 ? new[] { document.Steps[stepIndex] } : Array.Empty<EventTimelineStep>();
        Rect area = GUILayoutUtility.GetRect(10, Mathf.Max(110, availableHeight - 150), GUILayout.ExpandWidth(true));
        DrawCanvas(area, steps, result);
        var selectedStep = document.Steps.FirstOrDefault(s => s.Items.Any(item => item.PropertyPath == selectedPath));
        var selected = selectedStep?.Items.FirstOrDefault(item => item.PropertyPath == selectedPath);
        string details = selected == null ? "항목을 선택하면 설정을 편집합니다. 빈 곳이나 항목을 우클릭하여 기능을 추가하세요." :
            selected.Title + " · " + selected.Detail.Replace('\n', ' ') + "\n" +
            TimeLabel(selected, result ? selectedStep.Origin : 0, result && selectedStep.OriginVariable) + " · " + selected.Note;
        EditorGUILayout.SelectableLabel(details, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(34));
        using (new EditorGUI.DisabledScope(selected == null))
            if (GUILayout.Button("선택 항목 편집", GUILayout.Height(22)))
            {
                EditRequested?.Invoke(selectedPhasePath, selectedPath);
                GUIUtility.ExitGUI();
            }
        EditorGUILayout.LabelField("일반 연출은 같은 Phase에서 함께 시작합니다. 선택지와 액션은 연출 뒤에 진행합니다.\n우클릭한 시간은 시작 지연으로 저장되지 않습니다. ◆ 즉시 · ? 종료 미정 · ≈ 실행에 따라 변동", EditorStyles.wordWrappedMiniLabel);
        if (document.Warnings.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", document.Warnings.Distinct()), MessageType.Warning);
    }

    private void ChangeStep(int next)
    {
        stepIndex = next; scroll = Vector2.zero; selectedPath = null; cursorTime = 0;
        selectedPhasePath = document.Steps[next].PropertyPath;
        SelectionRequested?.Invoke(selectedPhasePath, null);
        // The old Layout tree belongs to the previous Step, so finish this GUI event before repainting.
        GUIUtility.ExitGUI();
    }

    private void DrawCanvas(Rect area, IList<EventTimelineStep> steps, bool result)
    {
        EditorGUI.DrawRect(area, Background);
        if (steps.Count == 0)
        {
            GUI.Label(area, "아직 등록된 Phase가 없습니다.\n우클릭 또는 + 기능으로 첫 이벤트를 만드세요.", TextStyle(12, TextAnchor.MiddleCenter));
            if (Event.current.type == EventType.ContextClick && area.Contains(Event.current.mousePosition))
            {
                OpenPicker(new Rect(Event.current.mousePosition, Vector2.zero), null);
                Event.current.Use();
            }
            return;
        }
        float labelWidth = Mathf.Clamp(area.width * .23f, 132, 205);
        double extent = 1;
        float height = 4;
        foreach (var step in steps)
        {
            double origin = result ? step.Origin : 0;
            extent = Math.Max(extent, origin + step.Duration);
            foreach (var item in step.Items)
                extent = Math.Max(extent, origin + (item.OpenEnded ? item.Start + 1 : item.End));
            height += HeaderHeight + Math.Max(1, step.Items.Count) * RowHeight;
        }
        float timeWidth = Math.Max(100, area.width - labelWidth - 32) * zoom;
        double pixelsPerSecond = timeWidth / extent;
        double tick = TickSize(pixelsPerSecond);
        Rect body = new Rect(area.x, area.y + RulerHeight, area.width, Math.Max(40, area.height - RulerHeight));
        float contentWidth = labelWidth + timeWidth + 24;
        scroll = GUI.BeginScrollView(body, scroll, new Rect(0, 0, contentWidth, height), false, false);
        bool contextRequested = false;
        string contextPhase = null;
        Vector2 contextScreenPoint = Vector2.zero;
        string editPhase = null, editFeature = null;
        float y = 0;
        int rowNumber = 0;
        foreach (var step in steps)
        {
            double origin = result ? step.Origin : 0;
            EditorGUI.DrawRect(new Rect(scroll.x, y, area.width, HeaderHeight), new Color(.13f, .16f, .21f));
            int index = document.Steps.IndexOf(step) + 1;
            string label = "STEP " + index.ToString("00") + "  ·  " + step.Label;
            if (result) label += "  ·  " + (step.OriginVariable ? "≈ " : "") + Format(origin) + "s 시작";
            if (step.Items.Count == 0) label += "  ·  이벤트 없음";
            GUI.Label(new Rect(scroll.x + 8, y, area.width - 20, HeaderHeight), label, TextStyle(11));
            Rect phaseArea = new Rect(scroll.x, y, area.width - 18, HeaderHeight + Math.Max(1, step.Items.Count) * RowHeight);
            if (Event.current.type == EventType.ContextClick && phaseArea.Contains(Event.current.mousePosition))
            {
                contextRequested = true; contextPhase = step.PropertyPath;
                contextScreenPoint = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Select(step, null); Event.current.Use();
            }
            Rect header = new Rect(scroll.x, y, area.width - 18, HeaderHeight);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && header.Contains(Event.current.mousePosition))
            { Select(step, null); Event.current.Use(); }
            y += HeaderHeight;
            if (step.Items.Count == 0)
            {
                GUI.Label(new Rect(scroll.x + 15, y, area.width - 30, RowHeight),
                    "이 Phase는 비어 있습니다. 우클릭으로 기능을 추가하세요.", TextStyle(11));
                y += RowHeight;
            }
            foreach (var item in step.Items)
            {
                if (y + RowHeight < scroll.y || y > scroll.y + body.height)
                {
                    rowNumber++; y += RowHeight; continue;
                }
                Rect row = new Rect(0, y, contentWidth, RowHeight);
                Color rowColor = rowNumber++ % 2 == 0 ? new Color(.105f, .125f, .16f) : new Color(.12f, .14f, .18f);
                EditorGUI.DrawRect(row, rowColor);
                for (double t = Math.Max(0, Math.Floor(scroll.x / pixelsPerSecond / tick) * tick); t <= extent + tick; t += tick)
                {
                    float gx = labelWidth + (float)(t * pixelsPerSecond);
                    if (gx > scroll.x + area.width) break;
                    EditorGUI.DrawRect(new Rect(gx, y, 1, RowHeight), Grid);
                }
                double start = origin + item.Start, end = origin + item.End;
                float x = labelWidth + (float)(start * pixelsPerSecond);
                bool instant = !item.OpenEnded && end - start < .000001;
                float width = item.OpenEnded ? Math.Max(24, labelWidth + timeWidth - x) : Math.Max(3, (float)((end - start) * pixelsPerSecond));
                Rect bar = new Rect(x, y + 7, width, RowHeight - 14);
                bool uncertain = item.Variable || result && step.OriginVariable;
                Color color = CategoryColor(item.Category);
                EditorGUI.DrawRect(bar, color);
                if (uncertain || item.OpenEnded)
                    for (float dash = bar.x; dash < bar.xMax; dash += 10)
                        EditorGUI.DrawRect(new Rect(dash, bar.yMax - 2, Math.Min(5, bar.xMax - dash), 2), Color.white);
                if (selectedPath == item.PropertyPath) Outline(bar, new Color(.85f, .95f, 1));
                string timing = TimeLabel(item, origin, result && step.OriginVariable);
                string tooltip = item.Title + "\n" + item.Detail + "\n" + timing + "\n" + item.Note + "\n" + step.Label;
                if (instant)
                {
                    GUI.Label(new Rect(x - 5, y + 17, 16, 20), "◆", TextStyle(12));
                    GUI.Label(new Rect(x + 12, y + 6, Math.Max(80, contentWidth - x - 12), RowHeight - 10),
                        new GUIContent(item.Title + " · " + item.Detail + "\n" + timing, tooltip), TextStyle(11));
                }
                else GUI.Label(new Rect(bar.x + 7, bar.y + 2, Math.Max(0, bar.width - 12), bar.height - 4),
                    new GUIContent(item.Title + " · " + item.Detail.Replace('\n', ' ') + "\n" + timing, tooltip), TextStyle(11));

                Rect hit = new Rect(scroll.x, y, area.width - 18, RowHeight);
                if (HandleItemInput(hit, step, item)) { editPhase = step.PropertyPath; editFeature = item.PropertyPath; }
                // Draw labels last, at scroll.x, so the name column stays fixed during horizontal scrolling.
                EditorGUI.DrawRect(new Rect(scroll.x, y, labelWidth, RowHeight), rowColor);
                EditorGUI.DrawRect(new Rect(scroll.x, y + 8, 3, RowHeight - 16), color);
                GUI.Label(new Rect(scroll.x + 10, y + 6, labelWidth - 18, RowHeight - 8),
                    new GUIContent(rowNumber.ToString("00") + "  " + item.Title + "\n" +
                        (uncertain ? "≈ " : "") + Format(start) + "s → " + (item.OpenEnded ? "?" : Format(end) + "s"), tooltip), TextStyle(11));
                y += RowHeight;
            }
        }
        Rect unused = new Rect(scroll.x, y, area.width - 18, Math.Max(0, body.height + scroll.y - y));
        if (Event.current.type == EventType.ContextClick && unused.Contains(Event.current.mousePosition))
        {
            contextRequested = true; contextPhase = steps[steps.Count - 1].PropertyPath;
            contextScreenPoint = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            Event.current.Use();
        }
        float cursorX = labelWidth + (float)(cursorTime * pixelsPerSecond);
        if (cursorX >= scroll.x + labelWidth)
            EditorGUI.DrawRect(new Rect(cursorX, 0, 1, height), new Color(1, .8f, .35f, .65f));
        GUI.EndScrollView();

        Rect ruler = new Rect(area.x + labelWidth, area.y, Math.Max(1, area.width - labelWidth - 16), RulerHeight);
        GUI.BeginGroup(ruler);
        EditorGUI.DrawRect(new Rect(0, 0, ruler.width, ruler.height), new Color(.075f, .09f, .12f));
        for (double t = Math.Max(0, Math.Floor(scroll.x / pixelsPerSecond / tick) * tick); t <= extent + tick; t += tick)
        {
            float x = (float)(t * pixelsPerSecond) - scroll.x;
            if (x > ruler.width) break;
            EditorGUI.DrawRect(new Rect(x, 23, 1, 9), new Color(.5f, .57f, .66f));
            GUI.Label(new Rect(x + 3, 1, 85, 22), Format(t) + "s", TextStyle(10));
        }
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && new Rect(0, 0, ruler.width, ruler.height).Contains(Event.current.mousePosition))
        {
            cursorTime = Math.Max(0, Math.Min(extent, (Event.current.mousePosition.x + scroll.x) / pixelsPerSecond));
            Event.current.Use();
        }
        GUI.EndGroup();
        GUI.Label(new Rect(area.x + 10, area.y, labelWidth - 16, RulerHeight), "이벤트  ·  " + Format(cursorTime) + "s", TextStyle(10));
        if (!contextRequested && Event.current.type == EventType.ContextClick && area.Contains(Event.current.mousePosition))
        {
            contextRequested = true; contextPhase = selectedPhasePath;
            contextScreenPoint = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            Event.current.Use();
        }
        if (contextRequested) OpenPicker(new Rect(GUIUtility.ScreenToGUIPoint(contextScreenPoint), Vector2.zero), contextPhase);
        if (editPhase != null)
        {
            EditRequested?.Invoke(editPhase, editFeature);
            GUIUtility.ExitGUI();
        }
    }

    private bool HandleItemInput(Rect hit, EventTimelineStep step, EventTimelineItem item)
    {
        Event input = Event.current;
        if (!hit.Contains(input.mousePosition)) return false;
        if (input.type == EventType.MouseDown && input.button == 0)
        {
            bool edit = input.clickCount > 1;
            Select(step, item); input.Use(); return edit;
        }
        return false;
    }

    private void Select(EventTimelineStep step, EventTimelineItem item)
    {
        selectedPhasePath = step.PropertyPath; selectedPath = item?.PropertyPath;
        stepIndex = document.Steps.IndexOf(step);
        SelectionRequested?.Invoke(selectedPhasePath, selectedPath);
    }

    private void OpenPicker(Rect rect, string phasePath)
    {
        EventFeaturePicker.Show(rect, documentAsset, phasePath, (phase, feature) =>
        {
            document = null; SelectPhase(phase, feature); SelectionRequested?.Invoke(phase, feature);
        });
    }

    private void QueueNextPhase(string phasePath)
    {
        EventSO asset = documentAsset;
        EventSO.EventStep step = EventAuthoring.GetStep(asset, phasePath);
        EditorApplication.delayCall += () =>
        {
            if (asset == null || (!string.IsNullOrEmpty(phasePath) && !ReferenceEquals(step, EventAuthoring.GetStep(asset, phasePath)))) return;
            if (!EventAuthoring.AddNextPhase(asset, phasePath, out string path)) return;
            document = null; SelectPhase(path); SelectionRequested?.Invoke(path, null);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        };
    }

    internal static double TickSize(double pixelsPerSecond)
    {
        double rough = 76 / Math.Max(.000001, pixelsPerSecond);
        double power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double scaled = rough / power;
        return (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * power;
    }

    private static string Format(double seconds) => seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string TimeLabel(EventTimelineItem item, double origin, bool uncertainOrigin)
    {
        string prefix = uncertainOrigin || item.Variable ? "≈ " : "";
        return prefix + Format(origin + item.Start) + "s → " + (item.OpenEnded ? "?" : Format(origin + item.End) + "s") +
            (item.OpenEnded ? " · 종료 미정" : item.End == item.Start ? " · 즉시" : " · " + Format(item.End - item.Start) + "s");
    }

    private static GUIStyle TextStyle(int size, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        int key = size * 16 + (int)alignment;
        if (!TextStyles.TryGetValue(key, out var style))
        {
            style = new GUIStyle(EditorStyles.label) { fontSize = size, alignment = alignment,
                clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = new Color(.94f, .96f, 1) } };
            TextStyles[key] = style;
        }
        return style;
    }

    private static void Outline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    private static Color CategoryColor(string category)
    {
        switch (category)
        {
            case "Camera": return new Color(.13f, .42f, .62f);
            case "Fade": return new Color(.40f, .25f, .56f);
            case "Transform": return new Color(.17f, .43f, .35f);
            case "Speech": return new Color(.50f, .36f, .14f);
            case "Sound": return new Color(.57f, .28f, .16f);
            case "Scene": return new Color(.30f, .35f, .59f);
            default: return new Color(.29f, .34f, .42f);
        }
    }
}
