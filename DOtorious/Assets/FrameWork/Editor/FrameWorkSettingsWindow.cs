using System;
using UnityEditor;
using UnityEngine;

namespace JYW.FrameWork.Editor
{
    public sealed class FrameWorkSettingsWindow : EditorWindow
    {
        private const string WindowScriptSuffix = "/Editor/FrameWorkSettingsWindow.cs";

        [SerializeField] private FrameWorkSettings settings;
        [SerializeField] private string statusMessage = "체크한 모듈이 다음 Play부터 자동 적용됩니다.";

        private SerializedObject serializedSettings;

        public static Rect LastPresentationToggleRectForTests { get; private set; }
        public static Rect LastObjectMakerToggleRectForTests { get; private set; }
        public static int MouseDownCountForTests { get; private set; }
        public static int PresentationClickCountForTests { get; private set; }
        public static int ObjectMakerClickCountForTests { get; private set; }
        public static Vector2 LastMousePositionForTests { get; private set; }
        public FrameWorkSettings CurrentSettingsForTests => settings;

        [MenuItem("Window/FrameWork/Settings", false, 2302)]
        public static void OpenWindow()
        {
            Open();
        }

        public static FrameWorkSettingsWindow Open()
        {
            FrameWorkSettingsWindow window = GetWindow<FrameWorkSettingsWindow>();
            window.titleContent = new GUIContent("FrameWork Settings");
            window.minSize = new Vector2(390f, 230f);
            window.ReloadSettings();
            window.Show();
            window.Focus();
            return window;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("FrameWork Settings");
            minSize = new Vector2(390f, 230f);
            ReloadSettings();
        }

        private void OnGUI()
        {
            if (Event.current != null && Event.current.type == EventType.MouseDown)
            {
                MouseDownCountForTests++;
                LastMousePositionForTests = Event.current.mousePosition;
            }

            if (settings == null || serializedSettings == null)
                ReloadSettings();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("현재 게임에 적용할 FrameWork", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "체크를 끈 모듈은 Play와 빌드에서 런타임 컴포넌트, 자동 부트스트랩 및 자동 설정을 실행하지 않습니다. " +
                "편집 창과 데이터는 그대로 유지됩니다.",
                MessageType.Info);

            if (settings == null || serializedSettings == null)
            {
                EditorGUILayout.HelpBox("FrameWork Settings 에셋을 만들지 못했습니다.", MessageType.Error);
                return;
            }

            serializedSettings.Update();
            SerializedProperty presentation = serializedSettings.FindProperty("presentationEnabled");
            SerializedProperty objectMaker = serializedSettings.FindProperty("objectMakerEnabled");

            Rect presentationRect = EditorGUILayout.GetControlRect(false, 24f);
            LastPresentationToggleRectForTests = presentationRect;
            bool nextPresentation = presentation.boolValue;
            if (GUI.Button(
                presentationRect,
                new GUIContent(
                    (presentation.boolValue ? "☑  " : "☐  ") + "Presentation",
                    "EventSO 편집 및 Presentation 자동 실행을 적용합니다."),
                EditorStyles.label))
            {
                PresentationClickCountForTests++;
                nextPresentation = !presentation.boolValue;
            }

            Rect objectMakerRect = EditorGUILayout.GetControlRect(false, 24f);
            LastObjectMakerToggleRectForTests = objectMakerRect;
            bool nextObjectMaker = objectMaker.boolValue;
            if (GUI.Button(
                objectMakerRect,
                new GUIContent(
                    (objectMaker.boolValue ? "☑  " : "☐  ") + "2DObjectMaker",
                    "2D ObjectDefinition과 멀티씬 자동 배치를 적용합니다."),
                EditorStyles.label))
            {
                ObjectMakerClickCountForTests++;
                nextObjectMaker = !objectMaker.boolValue;
            }

            if (nextPresentation != presentation.boolValue || nextObjectMaker != objectMaker.boolValue)
            {
                Undo.RecordObject(settings, "Change FrameWork Settings");
                presentation.boolValue = nextPresentation;
                objectMaker.boolValue = nextObjectMaker;
                serializedSettings.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                FrameWorkFeatureGate.InvalidateCache();
                statusMessage = "저장됨 - 다음 Play부터 적용됩니다.";
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);

            string assetPath = AssetDatabase.GetAssetPath(settings);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Settings Asset", assetPath);
        }

        private void ReloadSettings()
        {
            settings = LoadOrCreateSettings();
            serializedSettings = settings != null ? new SerializedObject(settings) : null;
            Repaint();
        }

        private static FrameWorkSettings LoadOrCreateSettings()
        {
            string root = FindRoot();
            string resources = root + "/Resources";
            string resourceFolder = resources + "/FrameWork";
            string assetPath = resourceFolder + "/Settings.asset";

            EnsureFolder(resources);
            EnsureFolder(resourceFolder);

            FrameWorkSettings result = AssetDatabase.LoadAssetAtPath<FrameWorkSettings>(assetPath);
            if (result != null)
                return result;

            result = CreateInstance<FrameWorkSettings>();
            result.name = "Settings";
            AssetDatabase.CreateAsset(result, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return result;
        }

        private static string FindRoot()
        {
            string[] guids = AssetDatabase.FindAssets("FrameWorkSettingsWindow t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (path.EndsWith(WindowScriptSuffix, StringComparison.Ordinal))
                    return path.Substring(0, path.Length - WindowScriptSuffix.Length);
            }

            return "Assets/FrameWork";
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            int slash = assetPath.LastIndexOf('/');
            if (slash <= 0)
                throw new InvalidOperationException("Assets 아래의 FrameWork 폴더만 만들 수 있습니다: " + assetPath);

            string parent = assetPath.Substring(0, slash);
            string folderName = assetPath.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
