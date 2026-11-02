using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class AllinOneUsabilityTests
    {
        private const string Folder = "Assets/__AllinOneUsabilityTests";
        private string recovery;
        private readonly List<string> generated = new List<string>();
        private static Type Host => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AllinOneEditor")).First(t => t != null);
        private static object Call(object target, string name, params object[] args) => Host.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
        private static ObjectWorkspaceDocument Current(EditorWindow window) => (ObjectWorkspaceDocument)Host.GetProperty("ActiveObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        private static EditorWindow Window()
        {
            var window = EditorWindow.GetWindow(Host); Call(window, "CreateGUI"); Call(window, "SetDetailed", false); return window;
        }
        [SetUp] public void SetUp()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType() == Host)) w.Close();
            recovery = File.Exists(ObjectWorkspaceRecovery.FilePath) ? File.ReadAllText(ObjectWorkspaceRecovery.FilePath) : null;
            if (File.Exists(ObjectWorkspaceRecovery.FilePath)) File.Delete(ObjectWorkspaceRecovery.FilePath);
            AssetDatabase.CreateFolder("Assets", "__AllinOneUsabilityTests");
        }
        [TearDown] public void TearDown()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType() == Host)) w.Close();
            foreach (var path in generated) AssetDatabase.DeleteAsset(path);
            generated.Clear(); AssetDatabase.DeleteAsset(Folder);
            if (recovery == null) { if (File.Exists(ObjectWorkspaceRecovery.FilePath)) File.Delete(ObjectWorkspaceRecovery.FilePath); }
            else File.WriteAllText(ObjectWorkspaceRecovery.FilePath, recovery);
        }
        private ObjectMakerBuildResult Build(ObjectKind kind)
        {
            var data = ObjectDefinitionData.CreateSG001(); data.kind = kind; data.displayName = "Usability_" + Guid.NewGuid().ToString("N");
            data.player.attacks[0].animationSpeed = 2.75f; data.player.attacks[0].comboSteps = 3;
            var result = ObjectMakerPrefabBuilder.Make(data);
            generated.AddRange(new[] { result.DefinitionPath, result.PrefabPath, result.AnimatorControllerPath }); return result;
        }
        private static Object Event(string name, string folder = Folder) => (Object)Host.GetMethod("CreateNamedEvent", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { name, folder });

        [Test] public void InlineEditingIsDraftOnlyAndUndoWorksWithoutSelectingAnInspectorSection()
        {
            var source = Build(ObjectKind.Monster); var window = Window(); Call(window, "SetAnyTarget", source.Prefab);
            var doc = Current(window); int health = source.Definition.Data.core.maxHealth;
            Assert.AreEqual(DisplayStyle.None, window.rootVisualElement.Q<ScrollView>("documents").style.display.value);
            var field = window.rootVisualElement.Q<IntegerField>("basic-health"); Assert.NotNull(field);
            field.value = 127; Undo.FlushUndoRecordObjects();
            Assert.AreEqual(127, doc.Data.core.maxHealth); Assert.AreEqual(health, source.Definition.Data.core.maxHealth); Assert.IsTrue(doc.IsDirty);
            Undo.PerformUndo(); Assert.AreEqual(health, doc.Data.core.maxHealth);
            Undo.PerformRedo(); Assert.AreEqual(127, doc.Data.core.maxHealth);
            Call(window, "Save"); Assert.AreEqual(127, source.Definition.Data.core.maxHealth); Assert.IsFalse(doc.IsDirty);
        }
        [Test] public void BasicAndDetailedEditorsShareChangesAndFoldedAttackSettingsSurviveApply()
        {
            var source = Build(ObjectKind.Player); var window = Window();
            Call(window, "NewObject", ObjectKind.Monster);
            Assert.IsFalse((bool)Call(window, "TryOpenInArea", source.Prefab));
            Assert.IsTrue(window.rootVisualElement.Q<Label>(className: "aio-browser-message").ClassListContains("is-error"));
            Call(window, "SwitchArea", Enum.Parse(Host.GetNestedType("WorkspaceArea"), "Player"));
            Assert.IsFalse(window.rootVisualElement.Q<Label>(className: "aio-browser-message").ClassListContains("is-error"));
            Call(window, "SetAnyTarget", source.Prefab);
            var doc = Current(window);
            window.rootVisualElement.Q<FloatField>("basic-jump-force").value = 13;
            window.rootVisualElement.Q<IntegerField>("basic-attack-damage-0").value = 14;
            Call(window, "SetDetailed", true);
            Assert.AreEqual(13, doc.Data.player.jumpForce); Assert.AreEqual(14, doc.Data.player.attacks[0].damage);
            doc.Data.core.maxHealth = 83;
            Call(window, "SetDetailed", false);
            Assert.AreEqual(83, window.rootVisualElement.Q<IntegerField>("basic-health").value);
            Call(window, "Save");
            Assert.AreEqual(13, source.Definition.Data.player.jumpForce);
            Assert.AreEqual(14, source.Definition.Data.player.attacks[0].damage);
            Assert.AreEqual(2.75f, source.Definition.Data.player.attacks[0].animationSpeed);
            Assert.AreEqual(3, source.Definition.Data.player.attacks[0].comboSteps);
            Assert.NotNull(source.Definition.GeneratedPrefab);
        }
        [Test] public void InlineDraftSurvivesAreaSwitchAndCloseReopen()
        {
            var window = Window(); Call(window, "NewObject", ObjectKind.Player); var doc = Current(window); string key = doc.Key;
            window.rootVisualElement.Q<TextField>("basic-name").value = "Recovery basic player";
            window.rootVisualElement.Q<IntegerField>("basic-health").value = 79;
            Call(window, "NewObject", ObjectKind.Monster); window.Close();
            window = Window(); Call(window, "SwitchArea", Enum.Parse(Host.GetNestedType("WorkspaceArea"), "Player"));
            doc = Current(window); Assert.AreEqual(key, doc.Key); Assert.AreEqual(79, doc.Data.core.maxHealth);
            Assert.AreEqual("Recovery basic player", window.rootVisualElement.Q<TextField>("basic-name").value); Assert.IsTrue(doc.IsDirty);
        }
        [Test] public void NamedEventCreationIsUniqueAndInvalidLocationsCreateNothing()
        {
            var first = Event("SameName"); string path = AssetDatabase.GetAssetPath(first); byte[] bytes = File.ReadAllBytes(path);
            var second = Event("SameName"); Assert.AreNotEqual(path, AssetDatabase.GetAssetPath(second)); CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            foreach (string invalid in new[] { "../Outside", "Assets/../Outside", "Assets//Bad", "Packages/Bad" })
                Assert.Throws<TargetInvocationException>(() => Event("Invalid", invalid));
            using (var serialized = new SerializedObject(first))
            { Assert.IsFalse(serialized.FindProperty("UseCondition").boolValue); Assert.AreEqual(1, serialized.FindProperty("ConditionSteps").arraySize); }
            Assert.AreEqual(2, AssetDatabase.FindAssets("t:EventSO", new[] { Folder }).Length);
        }
        [Test] public void CancellingNewEventRestoresPreviousDocumentAndDoesNotCreateAnAsset()
        {
            var asset = Event("Existing"); var window = Window(); Call(window, "SetAnyTarget", asset);
            Call(window, "CreateEvent"); Assert.NotNull(window.rootVisualElement.Q<TextField>("new-event-name"));
            Call(window, "CancelNewEvent");
            Assert.AreSame(asset, Host.GetField("currentEventSO", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window));
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:EventSO", new[] { Folder }).Length);
            Assert.NotNull(window.rootVisualElement.Q<Foldout>("basic-event-conditions"));
        }
        [Test] public void OpeningBasicAndDetailedViewsNeverRewritesExistingEventAssets()
        {
            var window = Window();
            foreach (var guid in AssetDatabase.FindAssets("t:EventSO", new[] { "Assets/Resources/EventSO" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); var asset = AssetDatabase.LoadMainAssetAtPath(path);
                byte[] bytes = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta"); bool dirty = EditorUtility.IsDirty(asset);
                string json = EditorJsonUtility.ToJson(asset);
                Call(window, "SetAnyTarget", asset); Call(window, "SetDetailed", true); Call(window, "SetDetailed", false);
                Assert.AreEqual(json, EditorJsonUtility.ToJson(asset), path); Assert.AreEqual(dirty, EditorUtility.IsDirty(asset), path);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path)); CollectionAssert.AreEqual(meta, File.ReadAllBytes(path + ".meta"));
            }
        }
    }
}
