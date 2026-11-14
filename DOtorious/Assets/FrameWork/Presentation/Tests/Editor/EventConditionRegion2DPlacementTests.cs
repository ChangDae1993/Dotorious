using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace JYW.Game.EventPlay.Tests
{
    public sealed class EventConditionRegion2DPlacementTests
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        string folder;
        Scene scene, previous;
        bool replacedUntitledScene;
        NewSceneSetup previousUntitledSetup;
        EventSO asset;
        EditorWindow window;
        string recoveryBackupDirectory;
        readonly Dictionary<string, string> recoveryBackups = new Dictionary<string, string>();
        static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        static EventConditionRegion2D Place(string name, params object[] args) => (EventConditionRegion2D)Find("EventConditionRegion2DPlacement").GetMethod(name, All).Invoke(null, args);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, All).Invoke(target, args);
        GameObject New(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
        }

        [SetUp] public void SetUp()
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                Assert.Ignore("Close Prefab Mode before starting scene placement tests.");

            previous = SceneManager.GetActiveScene();
            bool hasUntitled = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(index => SceneManager.GetSceneAt(index))
                .Any(candidate => candidate.isLoaded && string.IsNullOrEmpty(candidate.path));

            if (hasUntitled)
            {
                if (SceneManager.sceneCount != 1 || !previous.IsValid() || !previous.isLoaded ||
                    previous.isDirty || !string.IsNullOrEmpty(previous.path) ||
                    !TryReadUntitledTemplate(previous, out previousUntitledSetup))
                    Assert.Ignore("Save the current scene setup before starting scene placement tests.");
            }

            folder = "Assets/__EventPlacementTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                if (hasUntitled)
                {
                    // Unity cannot create anything additively while an untitled
                    // scene is open. Preserve a copy before replacing a clean stock scene.
                    string backupPath = folder + "/UntitledSceneBackup.unity";
                    Assert.IsTrue(EditorSceneManager.SaveScene(previous, backupPath, true),
                        "Could not save a copy of the untitled startup scene at " + backupPath);
                    Assert.IsTrue(File.Exists(backupPath), "Untitled scene backup was not written at " + backupPath);
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    replacedUntitledScene = true;
                }
                else
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Unity did not create a loaded placement test scene.");
                if (SceneManager.GetActiveScene() != scene)
                    SceneManager.SetActiveScene(scene);
                Assert.AreEqual(scene, SceneManager.GetActiveScene(), "Could not activate the placement test scene.");

                string testScenePath = folder + "/TestScene.unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, testScenePath),
                    "Could not save the placement test scene to " + testScenePath);
                asset = ScriptableObject.CreateInstance<EventSO>();
                asset.UseCondition = true;
                AssetDatabase.CreateAsset(asset, folder + "/Existing.asset");
            }
            catch
            {
                RestoreScenesAndDeleteAssets();
                throw;
            }
        }

        private static bool TryReadUntitledTemplate(Scene candidate, out NewSceneSetup template)
        {
            template = NewSceneSetup.EmptyScene;
            GameObject[] roots = candidate.GetRootGameObjects();
            if (roots.Length == 0) return true;

            // The untouched, default startup scene can also contain a camera
            // and a directional light. Do not replace a custom unnamed scene.
            if (roots.Any(root => root.transform.childCount != 0) ||
                roots.Count(root => root.name == "Main Camera" && root.GetComponent<Camera>() != null) != 1 ||
                roots.Count(root => root.name == "Directional Light" &&
                    root.TryGetComponent(out Light light) && light.type == LightType.Directional) != roots.Length - 1 ||
                roots.Length > 2)
                return false;

            template = NewSceneSetup.DefaultGameObjects;
            return true;
        }

        [TearDown] public void TearDown()
        {
            try
            {
                if (window != null) window.Close();
            }
            finally
            {
                window = null;
                try { RestoreRecoveryFiles(); }
                finally { RestoreScenesAndDeleteAssets(); }
            }
        }

        private void RestoreScenesAndDeleteAssets()
        {
            if (replacedUntitledScene)
            {
                // Retain the saved original in the test folder if clean restoration fails.
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    string teardownPath = folder + "/TemporaryTeardown.unity";
                    Assert.IsTrue(EditorSceneManager.SaveScene(scene, teardownPath),
                        "Could not save the dirty test scene before restoring the untitled startup scene: " + teardownPath);
                }
                var restored = EditorSceneManager.NewScene(previousUntitledSetup, NewSceneMode.Single);
                Assert.IsTrue(restored.IsValid() && restored.isLoaded, "Could not restore the untitled startup scene.");
            }
            else
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }

            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            folder = null;
            scene = default;
            previous = default;
            replacedUntitledScene = false;
        }

        [Test] public void AssignAuthorsNativeConditionPreservesOtherChecksAndSupportsUndoRedo()
        {
            var parent = New("World");
            var player = New("Actor").AddComponent<BoxCollider2D>(); player.transform.SetParent(parent.transform);
            var region = New("Start").AddComponent<BoxCollider2D>(); region.transform.SetParent(parent.transform);
            region.size = new Vector2(7, 4); region.offset = Vector2.one;
            asset.UseCondition = false;
            asset.Conditions = new[] { new EventSO.ConditionGroupData { Checks = EventSO.ConditionChecks.InputKey } };
            asset.ConditionSteps = new[] { new EventSO.EventStep { Flags = new EventSO.EventStepFlags { IsWait = true }, Wait = new EventSO.WaitData { WaitTime = 1.234f } } };
            string before = EditorJsonUtility.ToJson(asset);
            var binding = Place("Assign", asset, region, player, 0);
            Assert.AreSame(asset, binding.Event); Assert.AreSame(player, binding.Actor);
            Assert.IsTrue(asset.UseCondition); Assert.IsTrue(region.isTrigger);
            Assert.AreEqual("World/Actor", asset.Conditions[0].CollisionObjectA);
            Assert.AreEqual("World/Start", asset.Conditions[0].CollisionObjectB);
            Assert.AreEqual(EventSO.CollisionCheckMode.CurrentContact2D, asset.Conditions[0].CollisionMode);
            Assert.IsTrue((asset.Conditions[0].Checks & EventSO.ConditionChecks.InputKey) != 0);
            Assert.AreEqual(1.234f, asset.ConditionSteps[0].Wait.WaitTime);
            Assert.AreEqual(new Vector2(7, 4), region.size); Assert.AreEqual(Vector2.one, region.offset);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.IsNull(region.GetComponent<EventConditionRegion2D>()); Assert.IsFalse(region.isTrigger);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(asset));
            Undo.PerformRedo();
            Assert.AreSame(asset, region.GetComponent<EventConditionRegion2D>().Event);
            Assert.IsTrue(region.isTrigger);
            Place("Assign", asset, region, player, 0);
            Assert.AreEqual(1, region.GetComponents<EventConditionRegion2D>().Length);
        }

        [Test] public void SavedSceneAndAssetRetainTheExactNativeConditionAndReferences()
        {
            var actor = New("Actor").AddComponent<BoxCollider2D>();
            var binding = Place("Create", asset, actor, new Vector3(5, 6, 0), new Vector2(2, 3), 0);
            Assert.AreEqual(new Vector3(5, 6, 0), binding.transform.position);
            AssetDatabase.SaveAssets();
            string path = folder + "/Placement.unity"; Assert.IsTrue(EditorSceneManager.SaveScene(scene, path));
            var preview = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var saved = preview.GetRootGameObjects().Select(g => g.GetComponent<EventConditionRegion2D>()).Single(t => t != null);
                Assert.AreSame(asset, saved.Event); Assert.IsNotNull(saved.Actor);
                Assert.AreEqual(preview, saved.Actor.gameObject.scene);
                Assert.AreEqual(saved.name, asset.Conditions[0].CollisionObjectB);
                Assert.AreEqual(saved.Actor.name, asset.Conditions[0].CollisionObjectA);
                Assert.IsTrue(saved.GetComponent<BoxCollider2D>().isTrigger);
                Assert.AreEqual(0, saved.ConditionIndex);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Assert.IsTrue(binding == null);
            Undo.PerformRedo(); Assert.AreEqual(1, scene.GetRootGameObjects().Count(g => g.GetComponent<EventConditionRegion2D>() != null));
        }

        [Test] public void InvalidOrAmbiguousTargetsNeverMutateAssetOrScene()
        {
            var actor = New("Actor").AddComponent<BoxCollider2D>();
            var region = New("Start").AddComponent<BoxCollider2D>();
            int count = scene.rootCount; string before = EditorJsonUtility.ToJson(asset);
            Assert.Throws<TargetInvocationException>(() => Place("Assign", asset, region, actor, 9));
            Assert.Throws<TargetInvocationException>(() => Place("Create", asset, actor, Vector3.zero, Vector2.zero, 0));
            Assert.Throws<TargetInvocationException>(() => Place("Create", asset, actor, Vector3.zero, new Vector2(float.NaN, 2), 0));
            Assert.AreEqual(count, scene.rootCount);
            var child = New("Duplicate collider"); child.transform.SetParent(region.transform); child.AddComponent<BoxCollider2D>();
            Assert.Throws<TargetInvocationException>(() => Place("Assign", asset, region, actor, 0));
            Object.DestroyImmediate(child);
            New("Start");
            Assert.Throws<TargetInvocationException>(() => Place("Assign", asset, region, actor, 0));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(asset));
            Assert.IsNull(region.GetComponent<EventConditionRegion2D>()); Assert.IsFalse(region.isTrigger);
        }
        private void PreserveRecoveryFiles(IEnumerable<string> roots)
        {
            string[] paths = roots.SelectMany(root => new[] { root, root + ".bak", root + ".tmp" }).Distinct().ToArray();
            recoveryBackupDirectory = Path.Combine(Path.GetTempPath(), "DotoriousEventPlacement_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recoveryBackupDirectory);
            var verifiedCopies = new Dictionary<string, string>();
            using (var manifest = new StreamWriter(Path.Combine(recoveryBackupDirectory, "restore-manifest.txt")))
            {
                foreach (string original in paths)
                {
                    string copy = null;
                    if (File.Exists(original))
                    {
                        copy = Path.Combine(recoveryBackupDirectory, Path.GetFileName(original) + "." + verifiedCopies.Count);
                        File.Copy(original, copy);
                    }
                    verifiedCopies.Add(original, copy);
                    manifest.WriteLine((copy ?? "(original absent)") + " -> " + original);
                }
            }

            // The originals are touched only after every copy and the recovery
            // manifest are safely closed on disk. A crash leaves those copies intact.
            foreach (var entry in verifiedCopies)
                recoveryBackups.Add(entry.Key, entry.Value);
            foreach (var entry in recoveryBackups)
                if (entry.Value != null) File.Delete(entry.Key);
        }

        private void RestoreRecoveryFiles()
        {
            foreach (var entry in recoveryBackups)
            {
                if (entry.Value == null)
                {
                    if (File.Exists(entry.Key)) File.Delete(entry.Key);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Key));
                    File.Copy(entry.Value, entry.Key, true);
                }
            }

            // If restoration above throws, leave the disk copies and manifest for recovery.
            recoveryBackups.Clear();
            if (recoveryBackupDirectory == null) return;
            foreach (string file in Directory.GetFiles(recoveryBackupDirectory)) File.Delete(file);
            Directory.Delete(recoveryBackupDirectory);
            recoveryBackupDirectory = null;
        }

        [UnityTest] public IEnumerator ActualEventWorkspaceAssignButtonBindsChosenRegionAndPlayer()
        {
            var type = Find("AllinOneEditor");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("A graphics-enabled editor is required for real UI input.");
            if (Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w => w.GetType() == type))
                Assert.Ignore("Existing user AllinOneEditor windows are never closed by this test.");
            string recovery = (string)type.GetProperty("MapRecoveryPath", All).GetValue(null);
            PreserveRecoveryFiles(new[] { recovery, Path.GetFullPath(ObjectWorkspaceRecovery.FilePath) });
            window = (EditorWindow)ScriptableObject.CreateInstance(type);
            window.position = new Rect(80, 80, 1200, 850); window.Show();
            Call(window, "CreateGUI"); Call(window, "SetTarget", asset); Call(window, "SetDetailed", false);
            for (int i = 0; i < 8; i++) { window.Repaint(); yield return null; }
            var region = New("GUI region").AddComponent<BoxCollider2D>(); var player = New("GUI player").AddComponent<BoxCollider2D>();
            window.rootVisualElement.Q<ObjectField>("condition-start-region").value = region;
            window.rootVisualElement.Q<ObjectField>("condition-start-actor").value = player;
            var button = window.rootVisualElement.Q<Button>("condition-start-assign"); Assert.NotNull(button);
            window.rootVisualElement.Q<ScrollView>("simple-page").ScrollTo(button);
            for (int i = 0; i < 8; i++) { window.Repaint(); yield return null; }
            window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = button.worldBound.center, button = 0, clickCount = 1 });
            window.SendEvent(new Event { type = EventType.MouseUp, mousePosition = button.worldBound.center, button = 0, clickCount = 1 });
            yield return null;
            var trigger = region.GetComponent<EventConditionRegion2D>(); Assert.NotNull(trigger);
            Assert.AreSame(asset, trigger.Event); Assert.AreSame(player, trigger.Actor);
            Assert.IsFalse(window.rootVisualElement.Q<Label>("condition-start-notice").ClassListContains("is-error"));
        }
    }
}
