using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using JYW.Game.EventPlay;

/// <summary>
/// EventSO 전용 편집 창입니다.
/// 실제 편집 UI는 기존 EventSOEditor를 그대로 사용하므로 Inspector와 항상 같은 기능을 제공합니다.
/// </summary>
public sealed class EventSOEditorWindow : EditorWindow
{
    private const int ObjectPickerControlId = 0x4556534f;

    [SerializeField] private EventSO currentEventSO;
    [SerializeField] private bool followSelection = true;
    [SerializeField] private Vector2 scrollPosition;

    private Editor embeddedEditor;

    [MenuItem("Window/Presentation/EventSO Editor", false, 2100)]
    public static void OpenWindow()
    {
        Open(Selection.activeObject as EventSO);
    }

    public static void Open(EventSO eventSO)
    {
        EventSOEditorWindow window = GetWindow<EventSOEditorWindow>();
        window.titleContent = new GUIContent("EventSO Editor");
        window.minSize = new Vector2(460f, 320f);

        if (eventSO != null)
            window.SetTarget(eventSO);
        else
            window.TryUseSelection();

        window.Show();
        window.Focus();
    }

    [OnOpenAsset]
#if UNITY_6000_5_OR_NEWER
    private static bool OpenEventSOAsset(EntityId entityId, int line)
    {
        EventSO eventSO = EditorUtility.EntityIdToObject(entityId) as EventSO;
#else
    private static bool OpenEventSOAsset(int instanceId, int line)
    {
        EventSO eventSO = EditorUtility.InstanceIDToObject(instanceId) as EventSO;
#endif
        if (eventSO == null)
            return false;

        Open(eventSO);
        return true;
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("EventSO Editor");
        minSize = new Vector2(460f, 320f);

        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;

        if (!TryUseSelection())
            RebuildEmbeddedEditor();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        DisposeEmbeddedEditor();
    }

    private void OnProjectChange()
    {
        if (currentEventSO == null)
            DisposeEmbeddedEditor();
        else
            RebuildEmbeddedEditor();

        Repaint();
    }

    private void OnSelectionChanged()
    {
        if (followSelection)
            TryUseSelection();

        Repaint();
    }

    private bool TryUseSelection()
    {
        if (!followSelection)
            return false;

        EventSO selected = Selection.activeObject as EventSO;
        if (selected == null)
            return false;

        SetTarget(selected);
        return true;
    }

    private void SetTarget(EventSO eventSO)
    {
        if (currentEventSO == eventSO && embeddedEditor != null && embeddedEditor.target == eventSO)
            return;

        currentEventSO = eventSO;
        scrollPosition = Vector2.zero;
        RebuildEmbeddedEditor();
        Repaint();
    }

    private void RebuildEmbeddedEditor()
    {
        DisposeEmbeddedEditor();

        if (currentEventSO != null)
            embeddedEditor = Editor.CreateEditor(currentEventSO, typeof(EventSOEditor));
    }

    private void DisposeEmbeddedEditor()
    {
        if (embeddedEditor == null)
            return;

        DestroyImmediate(embeddedEditor);
        embeddedEditor = null;
    }

    private void OnGUI()
    {
        HandleObjectPicker();
        DrawToolbar();

        if (currentEventSO == null)
        {
            DrawEmptyState();
            return;
        }

        if (embeddedEditor == null || embeddedEditor.target != currentEventSO)
            RebuildEmbeddedEditor();

        DrawAssetPath();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        if (embeddedEditor != null)
            embeddedEditor.OnInspectorGUI();
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("EventSO", GUILayout.Width(52f));
        EventSO nextTarget = (EventSO)EditorGUILayout.ObjectField(
            currentEventSO,
            typeof(EventSO),
            false,
            GUILayout.MinWidth(100f));
        if (nextTarget != currentEventSO)
            SetTarget(nextTarget);

        if (GUILayout.Button(new GUIContent("Open File", "프로젝트의 EventSO 에셋을 선택합니다."), EditorStyles.toolbarButton, GUILayout.Width(70f)))
        {
            EditorGUIUtility.ShowObjectPicker<EventSO>(
                currentEventSO,
                false,
                string.Empty,
                ObjectPickerControlId);
        }

        bool nextFollowSelection = GUILayout.Toggle(
            followSelection,
            new GUIContent("Follow Selection", "Project 창에서 선택한 EventSO를 자동으로 표시합니다."),
            EditorStyles.toolbarButton,
            GUILayout.Width(104f));
        if (nextFollowSelection != followSelection)
        {
            followSelection = nextFollowSelection;
            if (followSelection)
                TryUseSelection();
        }

        EditorGUI.BeginDisabledGroup(currentEventSO == null);
        if (GUILayout.Button(new GUIContent("Ping", "Project 창에서 현재 EventSO의 위치를 표시합니다."), EditorStyles.toolbarButton, GUILayout.Width(40f)))
            EditorGUIUtility.PingObject(currentEventSO);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    private void HandleObjectPicker()
    {
        Event current = Event.current;
        if (current == null ||
            EditorGUIUtility.GetObjectPickerControlID() != ObjectPickerControlId ||
            (current.commandName != "ObjectSelectorUpdated" && current.commandName != "ObjectSelectorClosed"))
            return;

        EventSO picked = EditorGUIUtility.GetObjectPickerObject() as EventSO;
        if (picked != null)
            SetTarget(picked);
    }

    private void DrawAssetPath()
    {
        string assetPath = AssetDatabase.GetAssetPath(currentEventSO);
        string dirtyMark = EditorUtility.IsDirty(currentEventSO) ? " *" : string.Empty;

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField(currentEventSO.name + dirtyMark, EditorStyles.boldLabel, GUILayout.Width(160f));
        EditorGUILayout.SelectableLabel(assetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEmptyState()
    {
        GUILayout.Space(18f);
        EditorGUILayout.HelpBox(
            "Project 창에서 EventSO를 선택하거나 위의 Open File 버튼으로 에셋을 여세요.\n" +
            "Follow Selection이 켜져 있으면 이후 선택하는 EventSO가 자동으로 이 창에 표시됩니다.",
            MessageType.Info);

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Open EventSO File", GUILayout.Width(150f), GUILayout.Height(28f)))
        {
            EditorGUIUtility.ShowObjectPicker<EventSO>(
                null,
                false,
                string.Empty,
                ObjectPickerControlId);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }
}
