using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class AllinOneEditorTests
    {
        private const string Folder = "Assets/__AllinOneEditorTests";
        private readonly List<ObjectWorkspaceDocument> documents = new List<ObjectWorkspaceDocument>();
        private readonly List<string> generated = new List<string>();
        private string recoveryBefore;

        [SetUp] public void SetUp()
        {
            recoveryBefore = File.Exists(ObjectWorkspaceRecovery.FilePath) ? File.ReadAllText(ObjectWorkspaceRecovery.FilePath) : null;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__AllinOneEditorTests");
        }
        [TearDown] public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Name == "AllinOneEditor")) window.Close();
            foreach (var doc in documents.Where(d => d != null)) { Undo.ClearUndo(doc); Object.DestroyImmediate(doc); }
            documents.Clear();
            foreach (string path in generated) AssetDatabase.DeleteAsset(path);
            generated.Clear(); AssetDatabase.DeleteAsset(Folder);
            if (recoveryBefore != null) File.WriteAllText(ObjectWorkspaceRecovery.FilePath, recoveryBefore);
            else if (File.Exists(ObjectWorkspaceRecovery.FilePath)) File.Delete(ObjectWorkspaceRecovery.FilePath);
        }
        private ObjectDefinitionSO Source(string name)
        {
            var source = ScriptableObject.CreateInstance<ObjectDefinitionSO>();
            var data = ObjectDefinitionData.CreateSG001(); data.displayName = name; source.ReplaceData(data);
            AssetDatabase.CreateAsset(source, Folder + "/" + name + ".asset"); AssetDatabase.SaveAssetIfDirty(source); return source;
        }
        private ObjectWorkspaceDocument Document(ObjectDefinitionSO source)
        { var doc = ObjectWorkspaceDocument.Create(source); documents.Add(doc); return doc; }

        [Test] public void OpeningDraftDoesNotChangeSourceOrMeta()
        {
            var source = Source("ReadOnly"); string path = AssetDatabase.GetAssetPath(source);
            byte[] before = File.ReadAllBytes(path), meta = File.ReadAllBytes(path + ".meta");
            string json = EditorJsonUtility.ToJson(source);
            var doc = Document(source); Assert.IsFalse(doc.IsDirty);
            doc.Data.displayName = "Draft only";
            Assert.IsTrue(doc.IsDirty); Assert.AreEqual(json, EditorJsonUtility.ToJson(source));
            Assert.IsFalse(EditorUtility.IsDirty(source)); CollectionAssert.AreEqual(before, File.ReadAllBytes(path)); CollectionAssert.AreEqual(meta, File.ReadAllBytes(path + ".meta"));
        }

        [Test] public void UndoIsIsolatedPerDocumentAndSurvivesSwitching()
        {
            var a = Document(Source("First")); var b = Document(Source("Second"));
            Undo.RecordObject(a.Content, "Edit first draft"); a.Data.core.maxHealth = 72; Undo.FlushUndoRecordObjects();
            b.Section = "hit";
            Undo.PerformUndo(); Assert.AreEqual(a.Source.Data.core.maxHealth, a.Data.core.maxHealth); Assert.AreEqual("hit", b.Section);
            Undo.PerformRedo(); Assert.AreEqual(72, a.Data.core.maxHealth); Assert.AreEqual("Second", b.Data.displayName);
        }

        [Test] public void RecoveryRestoresMultipleDraftsAndAssetReferences()
        {
            var a = Document(Source("First")); var b = Document(Source("Second"));
            var clip = new AnimationClip { name = "Reference" }; AssetDatabase.CreateAsset(clip, Folder + "/Reference.anim");
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Reference.controller");
            a.Data.animatorController = controller; a.Data.core.maxHealth = 47; a.Section = "rule"; a.Index = 2;
            b.Data.displayName = "Unsaved second";
            var snapshot = new ObjectWorkspaceRecovery.Snapshot { activeObjectKey = b.Key, objectActive = true };
            snapshot.objects.Add(ObjectWorkspaceRecovery.Capture(a)); snapshot.objects.Add(ObjectWorkspaceRecovery.Capture(b));
            string path = Path.GetFullPath("UserSettings/AllinOneEditorTestRecovery.json");
            try
            {
                ObjectWorkspaceRecovery.Write(snapshot, path);
                var read = ObjectWorkspaceRecovery.Read(path);
                Assert.AreEqual(b.Key, read.activeObjectKey);
                var restored = ObjectWorkspaceRecovery.Restore(read.objects[0]); documents.Add(restored);
                Assert.AreEqual(47, restored.Data.core.maxHealth); Assert.AreEqual(controller, restored.Data.animatorController);
                Assert.AreEqual("rule", restored.Section); Assert.AreEqual(2, restored.Index); Assert.IsTrue(restored.IsDirty);
                Assert.AreEqual("Unsaved second", ObjectMakerJson.Import(read.objects[1].json, out _).displayName);
                ObjectWorkspaceRecovery.Write(snapshot, path); Assert.IsTrue(File.Exists(path + ".bak"));
            }
            finally { foreach (var p in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(p)) File.Delete(p); }
        }

        [Test] public void ExternalSourceChangeAndDeletionCannotBeOverwritten()
        {
            var source = Source("Conflict"); var doc = Document(source);
            var changed = source.Data.Clone(); changed.core.maxHealth = 88; source.ReplaceData(changed);
            Assert.IsTrue(doc.HasConflict); Assert.Throws<InvalidOperationException>(() => doc.Apply());
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(source));
            Assert.IsTrue(doc.SourceMissing); Assert.Throws<InvalidOperationException>(() => doc.Apply());
        }

        [Test] public void RecoveryPreservesUnfinishedInputWhilePortableImportStillValidates()
        {
            var doc = Document(null); doc.Data.displayName = ""; doc.Data.core.maxHealth = -7;
            var record = ObjectWorkspaceRecovery.Capture(doc);
            var restored = ObjectWorkspaceRecovery.Restore(record); documents.Add(restored);
            Assert.AreEqual("", restored.Data.displayName);
            Assert.AreEqual(-7, restored.Data.core.maxHealth);
            Assert.AreEqual(1, ObjectMakerJson.Import(record.json, out _).core.maxHealth);
        }

        [Test] public void TemporarilyMissingReferenceCannotEraseItsRecoveryGuid()
        {
            var doc = Document(null);
            string path = Folder + "/Temporary.controller";
            doc.Data.animatorController = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
            var saved = ObjectWorkspaceRecovery.Capture(doc);
            AssetDatabase.DeleteAsset(path);
            var restored = ObjectWorkspaceRecovery.Restore(saved); documents.Add(restored);
            Assert.IsTrue(restored.HasUnresolvedReferences);
            Assert.AreEqual(saved.json, ObjectWorkspaceRecovery.Capture(restored).json);
            Assert.Throws<InvalidOperationException>(() => restored.Apply());
        }

        [Test] public void MakeThenModifyKeepsGuidPrefabAndAnimatorMotion()
        {
            var doc = Document(null); doc.Data.displayName = "AllinOne_Test_" + Guid.NewGuid().ToString("N");
            var first = doc.Apply(); generated.AddRange(new[] { first.DefinitionPath, first.PrefabPath, first.AnimatorControllerPath });
            string guid = AssetDatabase.AssetPathToGUID(first.DefinitionPath); string key = doc.Key;
            var clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, Folder + "/Keep.anim");
            var state = first.AnimatorController.layers[0].stateMachine.states.First(s => s.state.name == ObjectCommonAnimator.Idle).state;
            state.motion = clip; EditorUtility.SetDirty(first.AnimatorController); AssetDatabase.SaveAssetIfDirty(first.AnimatorController);
            Undo.RecordObject(doc.Content, "Edit before Modify"); doc.Data.core.maxHealth = 67; Undo.FlushUndoRecordObjects();
            var second = doc.Apply(); Assert.AreEqual(key, doc.Key); Assert.IsFalse(doc.IsDirty);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(second.DefinitionPath));
            Assert.AreEqual(first.PrefabPath, second.PrefabPath); Assert.AreEqual(first.AnimatorControllerPath, second.AnimatorControllerPath);
            Assert.AreEqual(67, second.Definition.Data.core.maxHealth);
            Assert.AreSame(clip, second.AnimatorController.layers[0].stateMachine.states.First(s => s.state.name == ObjectCommonAnimator.Idle).state.motion);
            Assert.AreSame(second.Definition, doc.Source);
        }

        [Test] public void CommonHostKeepsDraftAcrossEventAndObjectNavigationAndReopen()
        {
            var type = TypeCache.GetTypesDerivedFrom<EditorWindow>().First(t => t.Name == "AllinOneEditor");
            var first = Source("First"); var second = Source("Second");
            var host = (EditorWindow)type.GetMethod("OpenDocument").Invoke(null, new object[] { first });
            var active = type.GetProperty("ActiveObject", BindingFlags.NonPublic | BindingFlags.Instance);
            var doc = (ObjectWorkspaceDocument)active.GetValue(host); doc.Data.core.maxHealth = 99;
            type.GetMethod("SetAnyTarget").Invoke(host, new object[] { second });
            var events = AssetDatabase.FindAssets("t:EventSO"); Assert.Greater(events.Length, 0);
            var eventAsset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(events[0]));
            type.GetMethod("SetAnyTarget").Invoke(host, new object[] { eventAsset });
            type.GetMethod("SetAnyTarget").Invoke(host, new object[] { first });
            Assert.AreSame(doc, active.GetValue(host)); Assert.AreEqual(99, doc.Data.core.maxHealth);
            host.Close();
            host = (EditorWindow)type.GetMethod("OpenDocument").Invoke(null, new object[] { first });
            Assert.AreEqual(99, ((ObjectWorkspaceDocument)active.GetValue(host)).Data.core.maxHealth);
            Assert.AreEqual(first.Data.core.maxHealth, AssetDatabase.LoadAssetAtPath<ObjectDefinitionSO>(AssetDatabase.GetAssetPath(first)).Data.core.maxHealth);
            Assert.AreSame(host, ObjectMakerWindow.Open(second));
        }

        [Test] public void ObjectMakerPathResolutionStillUsesOriginalScriptLocation()
        { Assert.AreEqual("Assets/FrameWork/2DObjectMaker", ObjectMakerPaths.Root); }
    }
}
