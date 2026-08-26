using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using JYW.FrameWork;
using JYW.FrameWork.Editor;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class FrameWorkSettingsEditorTests
    {
        [Test]
        [Order(100)]
        public void FrameWorkMenuItemsAreRegistered()
        {
            if (Application.isBatchMode)
                Assert.Ignore("메뉴 창 실행 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            string[] menuPaths =
            {
                "Window/FrameWork/Presentation",
                "Window/FrameWork/2DObjectMaker",
                "Window/FrameWork/Settings"
            };

            try
            {
                for (int i = 0; i < menuPaths.Length; i++)
                    Assert.IsTrue(EditorApplication.ExecuteMenuItem(menuPaths[i]),
                        "메뉴를 실행하지 못했습니다: " + menuPaths[i]);
                LogAssert.Expect(
                    LogType.Error,
                    "ExecuteMenuItem failed because there is no menu named 'Window/FrameWork/ObjectMaker'");
                Assert.IsFalse(EditorApplication.ExecuteMenuItem("Window/FrameWork/ObjectMaker"),
                    "이전 ObjectMaker 메뉴가 남아 있습니다.");
            }
            finally
            {
                EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                for (int i = 0; i < windows.Length; i++)
                {
                    string typeName = windows[i].GetType().Name;
                    if (typeName == "EventSOEditorWindow" ||
                        typeName == "ObjectMakerWindow" ||
                        typeName == "FrameWorkSettingsWindow")
                        windows[i].Close();
                }
            }
        }

        [Test]
        public void NewSettingsEnableBothModules()
        {
            FrameWorkSettings settings = ScriptableObject.CreateInstance<FrameWorkSettings>();
            try
            {
                Assert.IsTrue(settings.IsEnabled(FrameWorkModule.Presentation));
                Assert.IsTrue(settings.IsEnabled(FrameWorkModule.ObjectMaker));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ProjectSettingsAssetDrivesFeatureGate()
        {
            FrameWorkSettings settings = Resources.Load<FrameWorkSettings>(FrameWorkFeatureGate.ResourcePath);
            Assert.IsNotNull(settings);

            FrameWorkFeatureGate.InvalidateCache();
            Assert.AreEqual(settings.PresentationEnabled,
                FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation));
            Assert.AreEqual(settings.ObjectMakerEnabled,
                FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker));
        }

        [UnityTest]
        [Order(0)]
        public IEnumerator SettingsWindowCheckboxesPersistThroughReopen()
        {
            if (Application.isBatchMode)
                Assert.Ignore("EditorWindow 입력 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            FrameWorkSettingsWindow window = null;
            FrameWorkSettings settings = null;
            bool originalPresentation = true;
            bool originalObjectMaker = true;

            try
            {
                window = FrameWorkSettingsWindow.Open();
                window.position = new Rect(120f, 120f, 560f, 360f);
                window.Repaint();
                yield return null;
                yield return null;

                settings = window.CurrentSettingsForTests;
                Assert.IsNotNull(settings);

                var serialized = new SerializedObject(settings);
                SerializedProperty presentation = serialized.FindProperty("presentationEnabled");
                SerializedProperty objectMaker = serialized.FindProperty("objectMakerEnabled");
                originalPresentation = presentation.boolValue;
                originalObjectMaker = objectMaker.boolValue;

                Assert.Greater(FrameWorkSettingsWindow.LastPresentationToggleRectForTests.width, 0f);
                Assert.Greater(FrameWorkSettingsWindow.LastObjectMakerToggleRectForTests.width, 0f);

                window.Focus();
                yield return null;
                Rect presentationRect = FrameWorkSettingsWindow.LastPresentationToggleRectForTests;
                int presentationClicks = FrameWorkSettingsWindow.PresentationClickCountForTests;
                int mouseDowns = FrameWorkSettingsWindow.MouseDownCountForTests;
                Click(window, presentationRect.center + Vector2.up * 26f);
                yield return null;

                Assert.Greater(FrameWorkSettingsWindow.MouseDownCountForTests, mouseDowns,
                    "Settings 창이 MouseDown을 받지 못했습니다. rect=" + presentationRect);
                Assert.Greater(FrameWorkSettingsWindow.PresentationClickCountForTests, presentationClicks,
                    "Presentation 행 클릭이 처리되지 않았습니다. mouse=" +
                    FrameWorkSettingsWindow.LastMousePositionForTests + ", rect=" + presentationRect);

                serialized.Update();
                Assert.AreEqual(!originalPresentation, presentation.boolValue);

                window.Close();
                window = null;
                yield return null;

                window = FrameWorkSettingsWindow.Open();
                window.position = new Rect(120f, 120f, 560f, 360f);
                window.Repaint();
                yield return null;
                yield return null;

                settings = window.CurrentSettingsForTests;
                serialized = new SerializedObject(settings);
                presentation = serialized.FindProperty("presentationEnabled");
                objectMaker = serialized.FindProperty("objectMakerEnabled");
                window.Focus();
                yield return null;
                Rect objectMakerRect = FrameWorkSettingsWindow.LastObjectMakerToggleRectForTests;
                Click(window, objectMakerRect.center + Vector2.up * 26f);
                yield return null;

                serialized.Update();
                Assert.AreEqual(!originalPresentation, presentation.boolValue);
                Assert.AreEqual(!originalObjectMaker, objectMaker.boolValue);
                FrameWorkFeatureGate.InvalidateCache();
                Assert.AreEqual(!originalPresentation,
                    FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation));
                Assert.AreEqual(!originalObjectMaker,
                    FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker));

                string assetPath = AssetDatabase.GetAssetPath(settings);
                string yaml = File.ReadAllText(Path.GetFullPath(assetPath));
                StringAssert.Contains("presentationEnabled: " + (originalPresentation ? "0" : "1"), yaml);
                StringAssert.Contains("objectMakerEnabled: " + (originalObjectMaker ? "0" : "1"), yaml);

                window.Close();
                window = null;
                yield return null;

                window = FrameWorkSettingsWindow.Open();
                yield return null;
                settings = window.CurrentSettingsForTests;
                serialized = new SerializedObject(settings);
                Assert.AreEqual(!originalPresentation,
                    serialized.FindProperty("presentationEnabled").boolValue);
                Assert.AreEqual(!originalObjectMaker,
                    serialized.FindProperty("objectMakerEnabled").boolValue);
            }
            finally
            {
                if (window != null)
                    window.Close();

                if (settings != null)
                {
                    var serialized = new SerializedObject(settings);
                    serialized.FindProperty("presentationEnabled").boolValue = originalPresentation;
                    serialized.FindProperty("objectMakerEnabled").boolValue = originalObjectMaker;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(settings);
                    FrameWorkFeatureGate.InvalidateCache();
                }
            }
        }

        private static void Click(EditorWindow window, Vector2 position)
        {
            window.SendEvent(new Event
            {
                type = EventType.MouseDown,
                button = 0,
                mousePosition = position
            });
            window.SendEvent(new Event
            {
                type = EventType.MouseUp,
                button = 0,
                mousePosition = position
            });
        }
    }
}
