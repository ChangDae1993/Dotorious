using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JYW.Game.ObjectMaker.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class AllinOneWorkflowTests
    {
        private const string Folder = "Assets/__AllinOneWorkflowTests";
        private readonly List<string> generated = new List<string>();
        private readonly List<Object> temporary = new List<Object>();
        private Scene scene;
        private string recovery;
        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        private static object Call(Type type, string name, params object[] args) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        private static object Instance(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
        private static ObjectWorkspaceDocument Current(EditorWindow window) => (ObjectWorkspaceDocument)window.GetType().GetProperty("ActiveObject", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
        [SetUp] public void SetUp()
        {
            recovery = File.Exists(ObjectWorkspaceRecovery.FilePath) ? File.ReadAllText(ObjectWorkspaceRecovery.FilePath) : null;
            if (File.Exists(ObjectWorkspaceRecovery.FilePath)) File.Delete(ObjectWorkspaceRecovery.FilePath);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__AllinOneWorkflowTests");
            scene = SceneManager.GetActiveScene();
            Call(Find("EventSceneTargetField"), "Cancel");
        }
        [TearDown] public void TearDown()
        {
            Call(Find("EventSceneTargetField"), "Cancel");
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Name == "AllinOneEditor")) w.Close();
            Selection.activeObject = null;
            foreach (var obj in temporary.Where(o => o != null)) Object.DestroyImmediate(obj);
            temporary.Clear();
            foreach (var path in generated) AssetDatabase.DeleteAsset(path);
            generated.Clear(); AssetDatabase.DeleteAsset(Folder);
            if (recovery == null) { if (File.Exists(ObjectWorkspaceRecovery.FilePath)) File.Delete(ObjectWorkspaceRecovery.FilePath); }
            else File.WriteAllText(ObjectWorkspaceRecovery.FilePath, recovery);
        }
        private GameObject SceneObject(string name, GameObject parent = null)
        {
            var go = new GameObject(name); temporary.Add(go); SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent.transform);
            return go;
        }
        private ObjectMakerBuildResult Build(ObjectKind kind = ObjectKind.Monster)
        {
            var data = ObjectDefinitionData.CreateSG001(); data.kind = kind; data.displayName = "Workflow_" + Guid.NewGuid().ToString("N");
            var result = ObjectMakerPrefabBuilder.Make(data);
            generated.AddRange(new[] { result.DefinitionPath, result.PrefabPath, result.AnimatorControllerPath });
            return result;
        }
        private ScriptableObject EventAsset()
        {
            var asset = ScriptableObject.CreateInstance(Find("JYW.Game.EventPlay.EventSO"));
            AssetDatabase.CreateAsset(asset, Folder + "/Event_" + Guid.NewGuid().ToString("N") + ".asset");
            using (var so = new SerializedObject(asset))
            {
                so.FindProperty("Conditions").arraySize = 1;
                so.FindProperty("Conditions.Array.data[0].CollisionObjectA").stringValue = "keep A";
                so.FindProperty("Conditions.Array.data[0].CollisionObjectB").stringValue = "keep B";
                so.ApplyModifiedProperties();
            }
            return asset;
        }
        private const string A = "Conditions.Array.data[0].CollisionObjectA";
        private const string B = "Conditions.Array.data[0].CollisionObjectB";
        private static string Value(Object asset, string path) { using (var so = new SerializedObject(asset)) return so.FindProperty(path).stringValue; }
        private static bool Assign(Object asset, string path, Object candidate, out string error)
        {
            object[] args = { asset, path, candidate, null };
            bool success = (bool)Call(Find("EventSceneTargetField"), "TryAssign", args); error = args[3] as string; return success;
        }

        [UnityTest] public IEnumerator SceneSelectionWritesOnlyArmedStringAndExitsMode()
        {
            var asset = EventAsset(); var first = SceneObject("Workflow_First"); var second = SceneObject("Workflow_Second");
            Call(Find("EventSceneTargetField"), "BeginPick", asset, A);
            Selection.activeGameObject = first;
            yield return null;
            Assert.AreEqual(first.name, Value(asset, A)); Assert.AreEqual("keep B", Value(asset, B));
            Assert.IsFalse((bool)Find("EventSceneTargetField").GetProperty("IsPicking").GetValue(null));
            Selection.activeGameObject = second;
            yield return null;
            Assert.AreEqual(first.name, Value(asset, A)); Assert.AreEqual("keep B", Value(asset, B));
        }
        [UnityTest] public IEnumerator ChangingArmedFieldAndCancelNeverUpdatesPreviousField()
        {
            var asset = EventAsset(); var go = SceneObject("Workflow_Target");
            Call(Find("EventSceneTargetField"), "BeginPick", asset, A);
            Call(Find("EventSceneTargetField"), "BeginPick", asset, B);
            Selection.activeGameObject = go;
            yield return null;
            Assert.AreEqual("keep A", Value(asset, A)); Assert.AreEqual(go.name, Value(asset, B));
            Call(Find("EventSceneTargetField"), "BeginPick", asset, A); Call(Find("EventSceneTargetField"), "Cancel");
            Selection.activeGameObject = SceneObject("Workflow_Ignored"); Assert.AreEqual("keep A", Value(asset, A));
        }
        [Test] public void DuplicateShortNameUsesUniquePathIncludingInactiveObjects()
        {
            var asset = EventAsset();
            var left = SceneObject("Workflow_Left"); var right = SceneObject("Workflow_Right");
            var a = SceneObject("Duplicate", left); SceneObject("Duplicate", right); a.SetActive(false);
            Assert.IsTrue(Assign(asset, A, a, out string error), error);
            Assert.AreEqual("Workflow_Left/Duplicate", Value(asset, A));
        }
        [Test] public void AmbiguousPathAndPrefabAssetAreRejectedWithoutChangingInput()
        {
            var asset = EventAsset();
            SceneObject("Same", SceneObject("Workflow_DuplicateRoot"));
            var ambiguous = SceneObject("Same", SceneObject("Workflow_DuplicateRoot"));
            Assert.IsFalse(Assign(asset, A, ambiguous, out _)); Assert.AreEqual("keep A", Value(asset, A));
            var prefab = Build();
            Assert.IsFalse(Assign(asset, A, prefab.Prefab, out _)); Assert.AreEqual("keep A", Value(asset, A));
            Assert.IsFalse(Assign(asset, A, prefab.Definition, out _));
        }
        [Test] public void TargetAssignmentSupportsUndoAndManualNamesRemainValid()
        {
            var asset = EventAsset(); var go = SceneObject("Workflow_UndoTarget");
            Assert.IsTrue(Assign(asset, A, go, out _)); Undo.FlushUndoRecordObjects();
            Undo.PerformUndo(); Assert.AreEqual("keep A", Value(asset, A));
            Undo.PerformRedo(); Assert.AreEqual(go.name, Value(asset, A));
            using (var so = new SerializedObject(asset))
            { so.FindProperty(A).stringValue = "FutureSpawn/Child"; so.ApplyModifiedProperties(); }
            AssetDatabase.SaveAssetIfDirty(asset);
            Assert.AreEqual("FutureSpawn/Child", Value(asset, A)); Assert.AreEqual("keep B", Value(asset, B));
        }
        [Test] public void NonTargetStringsCannotBeFilledByPicker()
        {
            var asset = EventAsset(); var go = SceneObject("Workflow_NoRename");
            Assert.IsFalse(Assign(asset, "m_Name", go, out _));
            Assert.AreNotEqual(go.name, asset.name);
        }
        [Test] public void PrefabCatalogIsReadOnlyAndResolvesExistingDefinition()
        {
            var result = Build();
            byte[] prefab = File.ReadAllBytes(result.PrefabPath), definition = File.ReadAllBytes(result.DefinitionPath), meta = File.ReadAllBytes(result.PrefabPath + ".meta");
            var entry = ObjectPrefabCatalog.Inspect(result.Prefab);
            Assert.IsTrue(entry.Supported, entry.Problem); Assert.AreSame(result.Definition, entry.Definition);
            Assert.AreEqual(ObjectKind.Monster, entry.Kind);
            CollectionAssert.AreEqual(prefab, File.ReadAllBytes(result.PrefabPath)); CollectionAssert.AreEqual(definition, File.ReadAllBytes(result.DefinitionPath));
            CollectionAssert.AreEqual(meta, File.ReadAllBytes(result.PrefabPath + ".meta")); Assert.IsFalse(EditorUtility.IsDirty(result.Prefab));
        }
        [Test] public void DuplicatePrefabBackReferenceAndMissingDefinitionAreExplained()
        {
            var result = Build();
            string copyPath = Folder + "/Copy.prefab"; Assert.IsTrue(AssetDatabase.CopyAsset(result.PrefabPath, copyPath));
            var copy = AssetDatabase.LoadAssetAtPath<GameObject>(copyPath);
            Assert.IsFalse(ObjectPrefabCatalog.Inspect(copy).Supported);
            StringAssert.Contains("다른 Prefab", ObjectPrefabCatalog.Inspect(copy).Problem);
            var blank = SceneObject("MissingDefinition"); blank.AddComponent<BoxCollider2D>(); blank.AddComponent<ObjectActor2D>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(blank, Folder + "/Missing.prefab");
            var entry = ObjectPrefabCatalog.Inspect(prefab);
            Assert.IsTrue(entry.HasActor); Assert.IsFalse(entry.Supported); StringAssert.Contains("Definition", entry.Problem);
        }
        [Test] public void ModifyKeepsUserAnimatorLayersStatesTransitionsReferencesAndComponents()
        {
            var result = Build();
            var controller = result.AnimatorController; var machine = controller.layers[0].stateMachine;
            var idle = machine.states.First(s => s.state.name == "Idle").state;
            var custom = machine.AddState("User_Emote"); var clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, Folder + "/Emote.anim"); custom.motion = clip;
            controller.AddLayer("User_Face"); controller.AddParameter("UserMode", AnimatorControllerParameterType.Bool);
            var transition = idle.AddTransition(custom); transition.name = "User_Idle_Emote"; transition.AddCondition(AnimatorConditionMode.If, 0, "UserMode");
            var nested = machine.AddStateMachine("User_Nested"); nested.AddState("User_Nested_Idle");
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(idle, out string _, out long beforeId);
            var root = PrefabUtility.LoadPrefabContents(result.PrefabPath);
            try { var light = root.AddComponent<Light>(); light.intensity = 2.3f; PrefabUtility.SaveAsPrefabAsset(root, result.PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var data = result.Definition.Data.Clone(); data.core.maxHealth = 61;
            var updated = ObjectMakerPrefabBuilder.Make(data, result.Definition);
            var afterMachine = updated.AnimatorController.layers[0].stateMachine;
            var afterIdle = afterMachine.states.First(s => s.state.name == "Idle").state;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(afterIdle, out string _, out long afterId);
            Assert.AreEqual(beforeId, afterId, "Generated state references must survive Modify.");
            Assert.AreEqual(2, updated.AnimatorController.layers.Length); Assert.AreEqual("User_Face", updated.AnimatorController.layers[1].name);
            Assert.AreSame(clip, afterMachine.states.First(s => s.state.name == "User_Emote").state.motion);
            Assert.IsTrue(afterIdle.transitions.Any(t => t.name == "User_Idle_Emote"));
            Assert.IsTrue(afterMachine.stateMachines.Any(m => m.stateMachine.name == "User_Nested"));
            Assert.IsTrue(updated.AnimatorController.parameters.Any(p => p.name == "UserMode"));
            Assert.AreEqual(2.3f, updated.Prefab.GetComponent<Light>().intensity);
        }
        [Test] public void ForeignAnimatorIsNeverReplacedOnModify()
        {
            var result = Build();
            var foreign = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/User.controller");
            var data = result.Definition.Data.Clone(); data.animatorController = foreign; result.Definition.ReplaceData(data);
            AssetDatabase.SaveAssetIfDirty(result.Definition);
            byte[] before = File.ReadAllBytes(result.PrefabPath), graph = File.ReadAllBytes(Folder + "/User.controller");
            Assert.IsNotEmpty(ObjectPrefabCatalog.ApplyBlocker(result.Definition));
            Assert.Throws<InvalidOperationException>(() => ObjectMakerPrefabBuilder.Make(data, result.Definition));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(result.PrefabPath)); CollectionAssert.AreEqual(graph, File.ReadAllBytes(Folder + "/User.controller"));
        }
        [Test] public void ThreeAreasScopeDocumentsAndKeepDraftSelectionAndSearch()
        {
            var type = Find("AllinOneEditor"); var window = (EditorWindow)Call(type, "OpenDocument", new object[] { null });
            var enumType = type.GetNestedType("WorkspaceArea");
            Action<string> area = value => Instance(window, "SwitchArea", Enum.Parse(enumType, value));
            Instance(window, "NewObject", ObjectKind.Monster);
            var monster = Current(window); monster.Data.displayName = "M_Draft"; monster.Data.core.maxHealth = 97; monster.Section = "rule";
            Instance(window, "NewObject", ObjectKind.Player);
            var player = Current(window); player.Data.displayName = "P_Draft"; player.Section = "controls";
            Instance(window, "SetAnyTarget", EventAsset());
            CollectionAssert.AreEquivalent(new[] { "Event", "Monster", "Player" }, Enum.GetNames(enumType));
            area("Monster"); Assert.AreSame(monster, Current(window)); Assert.AreEqual(97, monster.Data.core.maxHealth); Assert.AreEqual("rule", monster.Section);
            var names = window.rootVisualElement.Query<Button>(className: "aio-document-tab").ToList().Select(b => b.text).ToArray();
            Assert.IsTrue(names.Any(n => n.Contains("M_Draft"))); Assert.IsFalse(names.Any(n => n.Contains("P_Draft")));
            var search = window.rootVisualElement.Q<UnityEditor.UIElements.ToolbarSearchField>("asset-search"); search.value = "MonsterFilter";
            area("Player"); Assert.AreSame(player, Current(window)); Assert.AreEqual("controls", player.Section); Assert.AreEqual("", search.value);
            area("Monster"); Assert.AreEqual("MonsterFilter", search.value);
            type.GetField("activeArea", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, Enum.Parse(enumType, "Event"));
            Instance(window, "RestoreWorkspace");
            Assert.AreEqual("Monster", type.GetProperty("ActiveArea").GetValue(window).ToString(), "Existing open object windows migrate into their functional area.");
            Instance(window, "PersistWorkspace");
            window.Close();
            window = (EditorWindow)Call(type, "OpenDocument", new object[] { null });
            Assert.AreEqual("Monster", type.GetProperty("ActiveArea").GetValue(window).ToString());
            Assert.AreEqual(97, Current(window).Data.core.maxHealth);
        }
        [Test] public void WrongAreaPrefabIsRejectedWithoutReplacingUnsavedDraft()
        {
            var result = Build(ObjectKind.Player);
            var type = Find("AllinOneEditor"); var window = (EditorWindow)Call(type, "OpenDocument", new object[] { null });
            Instance(window, "NewObject", ObjectKind.Monster); var draft = Current(window); draft.Data.core.maxHealth = 123;
            Assert.IsFalse((bool)Instance(window, "TryOpenInArea", result.Prefab));
            Assert.AreSame(draft, Current(window)); Assert.AreEqual(123, Current(window).Data.core.maxHealth);
        }
        [Test] public void EmptyReloadedStatusStillCapturesCompleteUnsavedDraft()
        {
            var draft = ObjectWorkspaceDocument.Create(null, ObjectKind.Player); temporary.Add(draft);
            draft.Data.core.maxHealth = 137;
            draft.UnresolvedJson = ""; // Unity hot reload normalizes a null serialized string to empty.
            var record = ObjectWorkspaceRecovery.Capture(draft);
            var restored = ObjectWorkspaceRecovery.Restore(record); temporary.Add(restored);
            Assert.AreEqual(137, restored.Data.core.maxHealth);
            Assert.AreEqual(ObjectMakerJson.Export(draft.Data), ObjectMakerJson.Export(restored.Data));
            Assert.IsTrue(restored.IsDirty);
        }
        [Test] public void SerializedEventTabsDoNotPreventMissingDraftRecovery()
        {
            var type = Find("AllinOneEditor");
            var window = (EditorWindow)Call(type, "OpenDocument", new object[] { null });
            Instance(window, "NewObject", ObjectKind.Monster); Current(window).Data.core.maxHealth = 171;
            Instance(window, "NewObject", ObjectKind.Player); Current(window).Data.core.maxHealth = 193;
            Instance(window, "SetAnyTarget", EventAsset()); Instance(window, "PersistWorkspace");
            var docs = (List<ObjectWorkspaceDocument>)type.GetField("objectDocuments", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            var old = docs.ToArray(); docs.Clear();
            foreach (var doc in old) Object.DestroyImmediate(doc);
            var events = (IList)type.GetField("eventDocuments", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            int eventCount = events.Count; Assert.Greater(eventCount, 0);
            Instance(window, "RestoreWorkspace");
            Assert.AreEqual(eventCount, events.Count, "Recovered event tabs must not be duplicated.");
            var areaType = type.GetNestedType("WorkspaceArea");
            Instance(window, "SwitchArea", Enum.Parse(areaType, "Monster"));
            Assert.AreEqual(171, Current(window).Data.core.maxHealth); Assert.IsTrue(Current(window).IsDirty);
            Instance(window, "SwitchArea", Enum.Parse(areaType, "Player"));
            Assert.AreEqual(193, Current(window).Data.core.maxHealth); Assert.IsTrue(Current(window).IsDirty);
        }
    }
}
