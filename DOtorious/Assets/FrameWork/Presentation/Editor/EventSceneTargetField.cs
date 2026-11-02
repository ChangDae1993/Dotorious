using System;
using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Editor-only locator input. Existing EventSO strings and runtime resolution stay unchanged.</summary>
[InitializeOnLoad]
public static class EventSceneTargetField
{
    private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "objectName", "ObjectName", "gameObjectName", "GameObjectName", "targetName", "TargetName",
        "startTargetName", "LookAtName", "parentName", "ParentName", "LocalCenterName", "CenterObject",
        "aimedTargetName", "CollisionObjectA", "CollisionObjectB", "ObjectStateTarget",
        "DistanceObjectA", "DistanceObjectB", "AnimatorObject", "CameraViewTarget"
    };
    private static readonly string[] Arrays = { "DestroyObjectNames", "EnableObjectNames", "DisableObjectNames", "DisableColliderObjectNames" };
    private static Object owner;
    private static string path;
    private static string feedbackPath, feedback;
    private static Object feedbackOwner;
    public static bool IsPicking => owner != null && !string.IsNullOrEmpty(path);
    public static bool SelectionConsumedThisTick { get; private set; }
    public static string ActivePath => path;
    public static event Action Changed;
    public static readonly Dictionary<string, Rect> LastFieldRects = new Dictionary<string, Rect>();
    public static readonly Dictionary<string, Rect> LastPickRects = new Dictionary<string, Rect>();
    public static readonly Dictionary<string, Rect> LastListRects = new Dictionary<string, Rect>();
    public static readonly Dictionary<string, Rect> LastSceneRows = new Dictionary<string, Rect>();

    static EventSceneTargetField()
    {
        Selection.selectionChanged += OnSelection;
        Undo.undoRedoPerformed += Cancel;
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        SceneView.duringSceneGui += _ => HandleEscape();
#if UNITY_6000_5_OR_NEWER
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += (_, __) => HandleEscape();
#else
        EditorApplication.hierarchyWindowItemOnGUI += (_, __) => HandleEscape();
#endif
    }
    public static bool IsTarget(SerializedProperty property) =>
        property != null && property.serializedObject.targetObject is EventSO &&
        property.propertyType == SerializedPropertyType.String &&
        (Names.Contains(property.name) || Arrays.Any(a => property.propertyPath.Contains("." + a + ".Array.data[")));

    public static GameObject[] LoadedObjects()
    {
        var result = new List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if ((t.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0) result.Add(t.gameObject);
        }
        return result.ToArray();
    }
    public static string HierarchyPath(GameObject go)
    {
        if (go == null) return "";
        var names = new List<string>();
        for (var t = go.transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse(); return string.Join("/", names);
    }
    public static GameObject[] FindMatches(string locator)
    {
        if (string.IsNullOrEmpty(locator)) return Array.Empty<GameObject>();
        var all = LoadedObjects();
        if (!locator.Contains("/")) return all.Where(g => g.name == locator).ToArray();
        // Runtime accepts root-qualified and relative hierarchy paths, with an optional leading slash in GameObject.Find.
        string normalized = locator.TrimStart('/');
        return all.Where(g => HierarchyPath(g) == normalized ||
            (!locator.StartsWith("/", StringComparison.Ordinal) && HierarchyPath(g).EndsWith("/" + normalized, StringComparison.Ordinal))).ToArray();
    }
    public static bool TryLocator(Object candidate, out string locator, out string error)
    {
        locator = null; error = null;
        var go = candidate as GameObject ?? (candidate as Component)?.gameObject;
        var all = LoadedObjects();
        if (go == null || EditorUtility.IsPersistent(go) || !all.Contains(go))
        { error = "현재 로드된 일반 씬의 오브젝트만 선택할 수 있습니다. Prefab 에셋과 Prefab Mode는 대상이 아닙니다."; return false; }
        if (string.IsNullOrWhiteSpace(go.name))
        { error = "이름이 비어 있습니다. Hierarchy에서 이름을 지정하세요."; return false; }
        if (all.Count(g => g.name == go.name) == 1 && !go.name.Contains("/")) { locator = go.name; return true; }
        string qualified = HierarchyPath(go);
        for (var t = go.transform; t != null; t = t.parent)
            if (t.name.Contains("/")) { error = "이름에 / 문자가 있어 경로를 안전하게 만들 수 없습니다. 직접 입력하거나 이름을 구분하세요."; return false; }
        if (FindMatches(qualified).Length != 1)
        { error = "로드된 씬에 이름과 전체 경로가 중복됩니다. 이름을 구분한 뒤 다시 선택하세요. 기존 입력은 유지됩니다."; return false; }
        locator = qualified; return true;
    }
    public static bool TryAssign(Object target, string propertyPath, Object candidate, out string error)
    {
        error = null;
        if (target == null) { error = "편집 문서가 닫혔습니다."; return false; }
        using (var serialized = new SerializedObject(target))
        {
            var property = serialized.FindProperty(propertyPath);
            if (!IsTarget(property)) { error = "대상 이름 필드가 더 이상 존재하지 않습니다."; return false; }
            if (!TryLocator(candidate, out string locator, out error)) return false;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("씬 대상 선택");
            property.stringValue = locator;
            serialized.ApplyModifiedProperties();
            Undo.IncrementCurrentGroup();
        }
        Changed?.Invoke();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return true;
    }
    public static void BeginPick(Object target, string propertyPath)
    {
        Cancel();
        if (target == null) return;
        using (var serialized = new SerializedObject(target))
            if (!IsTarget(serialized.FindProperty(propertyPath))) return;
        owner = target; path = propertyPath; feedback = null;
        Changed?.Invoke();
    }
    public static void Cancel()
    {
        owner = null; path = null;
        Changed?.Invoke();
    }
    public static void HandleEscape()
    {
        if (IsPicking && Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        { Cancel(); Event.current.Use(); }
    }
    private static void OnSelection()
    {
        if (!IsPicking) return;
        SelectionConsumedThisTick = true;
        EditorApplication.delayCall += () => SelectionConsumedThisTick = false;
        var selected = Selection.objects;
        if (selected.Length != 1) { SetFeedback(owner, path, "오브젝트 하나를 선택하세요. Esc로 취소합니다."); return; }
        Object target = owner; string propertyPath = path;
        if (TryAssign(target, propertyPath, selected[0], out string error)) Cancel();
        else SetFeedback(target, propertyPath, error);
    }
    private static void SetFeedback(Object target, string propertyPath, string message)
    { feedbackOwner = target; feedbackPath = propertyPath; feedback = message; Changed?.Invoke(); }

    public static bool PropertyField(SerializedProperty property, params GUILayoutOption[] options) => PropertyField(property, null, false, options);
    public static bool PropertyField(SerializedProperty property, bool includeChildren, params GUILayoutOption[] options) => PropertyField(property, null, includeChildren, options);
    public static bool PropertyField(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) => PropertyField(property, label, false, options);
    public static bool PropertyField(SerializedProperty property, GUIContent label, bool includeChildren, params GUILayoutOption[] options)
    {
        if (property != null && property.serializedObject.isEditingMultipleObjects)
            return EditorGUILayout.PropertyField(property, label, includeChildren, options);
        if (!IsTarget(property))
        {
            // Generic array drawers otherwise hide nested target fields behind Unity's default recursion.
            if (includeChildren && property != null && property.hasVisibleChildren && ContainsTarget(property))
            {
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label ?? new GUIContent(property.displayName), true);
                if (property.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    var it = property.Copy(); var end = it.GetEndProperty();
                    if (it.NextVisible(true))
                        do { PropertyField(it, null, true); } while (it.NextVisible(false) && !SerializedProperty.EqualContents(it, end));
                    EditorGUI.indentLevel--;
                }
                return property.isExpanded;
            }
            return EditorGUILayout.PropertyField(property, label, includeChildren, options);
        }
        var target = property.serializedObject.targetObject;
        string key = property.propertyPath;
        bool armed = owner == target && path == key;
        HandleEscape();
        if (label == null) label = new GUIContent(property.displayName);
        if (!string.IsNullOrEmpty(label.text)) EditorGUILayout.LabelField(label);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.SetNextControlName("EventTarget." + key);
            string value = EditorGUILayout.TextField(property.stringValue, options);
            Rect fieldRect = GUILayoutUtility.GetLastRect(); LastFieldRects[key] = ScreenRect(fieldRect);
            if (value != property.stringValue)
            { Cancel(); property.stringValue = value; feedback = null; }
            var color = GUI.backgroundColor;
            if (armed) GUI.backgroundColor = new Color(.45f, .8f, 1f);
            if (GUILayout.Button(new GUIContent(armed ? "×" : "◎", armed ? "선택 모드 취소 · Esc" : "이 필드의 선택 모드: Scene / Hierarchy에서 오브젝트를 클릭하세요."), GUILayout.Width(24)))
            {
                property.serializedObject.ApplyModifiedProperties();
                if (armed) Cancel(); else BeginPick(target, key);
            }
            LastPickRects[key] = ScreenRect(GUILayoutUtility.GetLastRect()); GUI.backgroundColor = color;
            if (GUILayout.Button(new GUIContent("≡", "로드된 씬 오브젝트 목록에서 찾기"), GUILayout.Width(24)))
            {
                property.serializedObject.ApplyModifiedProperties(); Cancel();
                PopupWindow.Show(GUILayoutUtility.GetLastRect(), new SceneList(target, key));
            }
            LastListRects[key] = ScreenRect(GUILayoutUtility.GetLastRect());
            HandleDrop(fieldRect, property);
            if (Event.current.type == EventType.MouseDown && fieldRect.Contains(Event.current.mousePosition) && IsPicking && !armed) Cancel();
        }
        if (armed) EditorGUILayout.HelpBox("선택 중 · Scene / Hierarchy에서 하나를 클릭하세요. 이 필드만 적용됩니다. Esc 취소", MessageType.Info);
        if (feedbackOwner == target && feedbackPath == key && !string.IsNullOrEmpty(feedback))
            EditorGUILayout.HelpBox(feedback, MessageType.Warning);
        else if (!string.IsNullOrEmpty(property.stringValue))
        {
            var matches = FindMatches(property.stringValue);
            if (matches.Length == 0) EditorGUILayout.HelpBox("현재 로드된 씬에서 찾지 못했습니다. 실행 중 생성될 대상이라면 이름을 유지할 수 있습니다.", MessageType.Warning);
            else if (matches.Length > 1) EditorGUILayout.HelpBox("같은 이름 / 경로가 " + matches.Length + "개입니다. 선택기로 고유한 경로를 지정하세요.", MessageType.Warning);
            else
            {
                var match = matches[0];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(match.scene.name + " / " + HierarchyPath(match) + (match.activeInHierarchy ? "" : " · 비활성"), EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button(new GUIContent("↗", "Hierarchy에서 위치 표시"), GUILayout.Width(24))) EditorGUIUtility.PingObject(match);
                }
            }
        }
        return false;
    }
    private static bool ContainsTarget(SerializedProperty property)
    {
        var it = property.Copy(); var end = it.GetEndProperty();
        while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
            if (IsTarget(it)) return true;
        return false;
    }
    private static Rect ScreenRect(Rect rect) => new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
    private static void HandleDrop(Rect rect, SerializedProperty property)
    {
        if (!GUI.enabled) return;
        var e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
        string error = "오브젝트 하나만 드래그하세요.";
        bool valid = DragAndDrop.objectReferences.Length == 1 && TryLocator(DragAndDrop.objectReferences[0], out _, out error);
        DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
        if (e.type == EventType.DragPerform)
        {
            property.serializedObject.ApplyModifiedProperties();
            if (valid && TryAssign(property.serializedObject.targetObject, property.propertyPath, DragAndDrop.objectReferences[0], out error))
            { DragAndDrop.AcceptDrag(); Cancel(); property.serializedObject.Update(); feedback = null; GUI.changed = true; }
            else SetFeedback(property.serializedObject.targetObject, property.propertyPath, error);
        }
        e.Use();
    }

    private sealed class SceneList : PopupWindowContent
    {
        private readonly Object target;
        private readonly string propertyPath;
        private string search = "", error;
        private Vector2 scroll;
        private bool focus = true;
        public SceneList(Object target, string propertyPath) { this.target = target; this.propertyPath = propertyPath; }
        public override Vector2 GetWindowSize() => new Vector2(540, 440);
        public override void OnGUI(Rect rect)
        {
            LastSceneRows.Clear();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { editorWindow.Close(); return; }
            GUILayout.Label("씬 오브젝트 선택", EditorStyles.boldLabel);
            GUILayout.Label("현재 로드된 씬 · 비활성 포함 · 이름 / 경로 / 컴포넌트 검색", EditorStyles.wordWrappedMiniLabel);
            GUI.SetNextControlName("EventSceneSearch");
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (focus) { EditorGUI.FocusTextInControl("EventSceneSearch"); focus = false; }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int count = 0;
            foreach (var go in LoadedObjects().OrderBy(g => g.scene.name).ThenBy(HierarchyPath))
            {
                string hierarchy = HierarchyPath(go);
                string components = string.Join(" ", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
                if (!string.IsNullOrEmpty(search) && (go.scene.name + "/" + hierarchy + " " + components).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(go.scene.name + " / " + hierarchy + (go.activeInHierarchy ? "" : " [비활성]"), components), EditorStyles.miniButton, GUILayout.Height(26)))
                    {
                        if (TryAssign(target, propertyPath, go, out error)) { editorWindow.Close(); GUIUtility.ExitGUI(); }
                    }
                    LastSceneRows[go.scene.name + "/" + hierarchy] = ScreenRect(GUILayoutUtility.GetLastRect());
                    if (GUILayout.Button(new GUIContent("↗", "Hierarchy에서 위치 표시"), GUILayout.Width(25))) EditorGUIUtility.PingObject(go);
                    if (GUILayout.Button(new GUIContent("F", "Scene View에서 보기"), GUILayout.Width(25))) SceneView.lastActiveSceneView?.Frame(new Bounds(go.transform.position, Vector3.one * 2), false);
                }
            }
            if (count == 0) GUILayout.Label("일치하는 씬 오브젝트가 없습니다.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("취소 · Esc")) editorWindow.Close();
        }
    }
}
