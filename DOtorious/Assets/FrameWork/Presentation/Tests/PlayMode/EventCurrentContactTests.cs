using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static JYW.Game.EventPlay.EventSO;

namespace JYW.Game.EventPlay.Tests
{
    public sealed class EventCurrentContactTests
    {
        private static readonly Vector2 CorrectPosition = new Vector2(2010f, 2000f);
        private static readonly Vector2 DecoyPosition = new Vector2(2020f, 2000f);
        private static readonly Vector2 OutsidePosition = new Vector2(2000f, 2000f);
        private static readonly MethodInfo Check = typeof(EventPlayManager).GetMethod(
            "IsAutoConditionReady", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo Arm = typeof(EventPlayManager).GetMethod(
            "ArmCollisionConditions", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<Object> owned = new List<Object>();
        private EventPlayManager manager;
        private Scene contactScene;
        private PhysicsScene2D physics;
        private bool startedWaitingEvent;
#if UNITY_EDITOR
        private string temporaryEventAssetPath;
#endif

        [UnitySetUp]
        public IEnumerator GetManager()
        {
            for (int frame = 0; frame < 120 && manager == null; frame++)
            {
                manager = EventPlayManager.Instance;
                if (manager == null)
                    yield return null;
            }

            Assert.That(manager, Is.Not.Null, "The Presentation scene must supply a manager.");
            Assert.That(manager.isActiveAndEnabled, Is.True);
            contactScene = SceneManager.CreateScene(Unique("CurrentContactPhysics"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = contactScene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True, "The fixture must have an isolated 2D physics scene.");
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (startedWaitingEvent && manager != null)
            {
                manager.enabled = false; // Cancel a waiting condition after an assertion fails.
                manager.enabled = true;
            }
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null)
                    Object.Destroy(owned[i]);

            owned.Clear();
            startedWaitingEvent = false;
            manager = null;
            if (contactScene.IsValid() && contactScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(contactScene);
                if (unload != null)
                    yield return unload;
            }
            contactScene = default;
            physics = default;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(temporaryEventAssetPath))
            {
                var tempAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<EventSO>(temporaryEventAssetPath);
                if (tempAsset != null)
                    UnityEditor.Undo.ClearUndo(tempAsset);
                UnityEditor.AssetDatabase.DeleteAsset(temporaryEventAssetPath);
                temporaryEventAssetPath = null;
            }
#endif
            yield return null;
        }

        [UnityTest]
        public IEnumerator CurrentContactRejectsWrongPairPreviousContactAndInactiveBox()
        {
            Rigidbody2D player = CreateActor("Player");
            Rigidbody2D other = CreateActor("Other", OutsidePosition + Vector2.right * 2f);
            BoxCollider2D correct = CreateRegion(CorrectPosition);
            CreateRegion(DecoyPosition);

            string prerequisite = Unique("PreviousEventFinished");
            ConditionGroupData flags = Pair(player, correct.gameObject, CollisionCheckMode.CurrentContact2D, prerequisite);
            EventSO asset = CreateConditional(flags);
            Arm.Invoke(manager, new object[] { asset });

            player.position = DecoyPosition;
            other.position = CorrectPosition;
            yield return PhysicsTicks(3);
            manager.SetGlobals(prerequisite, true);
            Assert.That(Ready(asset, correct.gameObject), Is.False, "Neither the wrong region nor a different actor on the correct box may pass.");

            manager.SetGlobals(prerequisite, false);
            other.position = OutsidePosition + Vector2.right * 2f;
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.False, "Actual contact is not enough before the previous event.");

            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            manager.SetGlobals(prerequisite, true);
            Assert.That(Ready(asset, correct.gameObject), Is.False, "Earlier contact must not pass after leaving.");

            correct.gameObject.SetActive(false);
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.False, "An inactive start box cannot be contacted.");
            Assert.That(correct.GetComponents<Collider2D>().Length, Is.EqualTo(1), "No probe or substitute collider may be created.");

            correct.gameObject.SetActive(true);
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.True, "The player child shape is now touching the authored box.");
            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.False, "Exit clears the opt-in condition.");
        }

        [UnityTest]
        public IEnumerator DefaultHistoricalModeStillLatchesAndCurrentModeDoesNotCreateColliders()
        {
            Rigidbody2D player = CreateActor("Player");
            var empty = Own(NewInContactScene(Unique("NoCollider")));
            EventSO current = CreateConditional(Pair(player, empty, CollisionCheckMode.CurrentContact2D));
            Arm.Invoke(manager, new object[] { current });
            Assert.That(Ready(current, empty), Is.False);
            Assert.That(empty.GetComponents<Component>().Length, Is.EqualTo(1),
                "The current mode must not create colliders, relays, or an auxiliary body.");

            BoxCollider2D correct = CreateRegion(CorrectPosition);
            ConditionGroupData historical = Pair(player, correct.gameObject);
            Assert.That(historical.CollisionMode, Is.EqualTo(CollisionCheckMode.ContactHistory));
            EventSO asset = CreateConditional(historical);
            Arm.Invoke(manager, new object[] { asset });
            Assert.That(Ready(asset, correct.gameObject), Is.False);
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.True);
            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.True, "Existing historical assets must still latch.");
        }

        [UnityTest]
        public IEnumerator WaitUntilConditionRequiresActualContactWhenPrerequisiteChanges()
        {
            Rigidbody2D player = CreateActor("Player");
            BoxCollider2D region = CreateRegion(CorrectPosition);
            string prerequisite = Unique("Prerequisite"), completed = Unique("WaitCompleted");
            ConditionGroupData condition = Pair(player, region.gameObject, CollisionCheckMode.CurrentContact2D, prerequisite);
            EventSO asset = Own(ScriptableObject.CreateInstance<EventSO>());
            asset.UseCondition = false;
            asset.ConditionSteps = new[]
            {
                new EventStep
                {
                    Flags = new EventStepFlags { IsWaitUntilCondition = true, IsEventExe = true },
                    WaitUntilCondition = new WaitUntilConditionData { Condition = condition },
                    EventExe = new EventExeData
                    {
                        UseCondition = false,
                        ConditionSteps = new[]
                        {
                            new EventStep
                            {
                                Flags = new EventStepFlags { IsSetGlobals = true },
                                SetGlobals = new SetValueData
                                {
                                    SetValues = new[]
                                    {
                                        new SetValueSubData
                                        {
                                            valueType = EventSO.ValueType.Bool, ValueName = completed, boolValue = true
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            startedWaitingEvent = true;
            manager.PlayEvent(asset, region.gameObject);

            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(manager.GetGlobal<bool>(completed), Is.False);
            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            manager.SetGlobals(prerequisite, true);
            yield return PhysicsTicks(3);
            Assert.That(manager.GetGlobal<bool>(completed), Is.False);

            player.position = CorrectPosition;
            yield return PhysicsTicks(6);
            Assert.That(manager.GetGlobal<bool>(completed), Is.True, "The next authored phase runs on eligible contact.");
        }

        [UnityTest]
        public IEnumerator CurrentContactRequiresAnUnambiguousExactColliderHost()
        {
            Rigidbody2D player = CreateActor("Player");
            BoxCollider2D region = CreateRegion(CorrectPosition);
            ConditionGroupData condition = Pair(player, region.gameObject, CollisionCheckMode.CurrentContact2D);
            EventSO asset = CreateConditional(condition);
            string shapePath = condition.CollisionObjectA;
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, region.gameObject), Is.True);

            condition.CollisionObjectA = player.gameObject.name;
            Assert.That(Ready(asset, region.gameObject), Is.False,
                "A touching child collider must not satisfy its parent's locator.");
            condition.CollisionObjectA = shapePath;
            Assert.That(Ready(asset, region.gameObject), Is.True);

            var duplicate = Own(NewInContactScene(player.gameObject.name));
            duplicate.transform.position = OutsidePosition;
            var duplicateShape = NewInContactScene("Actual Collider2D");
            duplicateShape.transform.SetParent(duplicate.transform, false);
            duplicateShape.AddComponent<BoxCollider2D>();
            Assert.That(Ready(asset, region.gameObject), Is.False,
                "A duplicated full hierarchy path must fail closed even if one result is physically touching.");

            Object.Destroy(duplicate);
            yield return null;
            Assert.That(Ready(asset, region.gameObject), Is.True,
                "A formerly ambiguous locator should resolve anew after the duplicate is removed.");

            var duplicateRegion = Own(NewInContactScene(region.gameObject.name));
            duplicateRegion.transform.position = OutsidePosition;
            duplicateRegion.AddComponent<BoxCollider2D>();
            Assert.That(Ready(asset, region.gameObject), Is.False, "A bare duplicate name must also fail closed.");
            Object.Destroy(duplicateRegion);
            yield return null;

            var shape = player.transform.Find("Actual Collider2D");
            var second = shape.gameObject.AddComponent<CircleCollider2D>();
            second.enabled = false;
            Assert.That(Ready(asset, region.gameObject), Is.False,
                "Multiple Collider2Ds on one named host are ambiguous even when only one is enabled.");
        }
        [UnityTest]
        public IEnumerator AutoRunnerDiscardsLiveContactWhileBusyAndBetweenSelectionAndPlayback()
        {
            Rigidbody2D player = CreateActor("Player");
            BoxCollider2D correct = CreateRegion(CorrectPosition);
            CreateRegion(DecoyPosition);
            string prerequisite = Unique("Prerequisite"), startCount = Unique("StartCount");
            EventSO asset = CreateConditional(Pair(player, correct.gameObject, CollisionCheckMode.CurrentContact2D, prerequisite));
            asset.ConditionSteps[0].Flags.IsSetGlobals = true;
            asset.ConditionSteps[0].SetGlobals = new SetValueData
            {
                SetValues = new[]
                {
                    new SetValueSubData
                    {
                        valueType = EventSO.ValueType.Int, ValueName = startCount,
                        setType = SetType.Add, intValue = 1
                    }
                }
            };

            IEnumerator monitor = CreateControlledMonitor(asset, correct.gameObject, out IList pending);
            Assert.That(monitor.MoveNext(), Is.True, "The real runner begins with its normal initial yield.");

            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True); // Right pair, too early.
            player.position = DecoyPosition;
            manager.SetGlobals(prerequisite, true);
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True); // Wrong region, even though the prerequisite now passes.
            Assert.That(manager.GetGlobal<int>(startCount), Is.Zero);
            Assert.That(pending.Count, Is.EqualTo(1));

            string releaseBusy = Unique("ReleaseBusyManager");
            EventSO blocker = Own(ScriptableObject.CreateInstance<EventSO>());
            blocker.UseCondition = false;
            blocker.ConditionSteps = new[]
            {
                new EventStep
                {
                    Flags = new EventStepFlags { IsWaitUntilCondition = true, IsLock = true },
                    Lock = new LockData { IsLockMove = true },
                    WaitUntilCondition = new WaitUntilConditionData
                    {
                        Condition = new ConditionGroupData
                        {
                            Checks = ConditionChecks.CheckValue,
                            Conditions = new[]
                            {
                                new ConditionSubData
                                {
                                    GlobalNames = releaseBusy, valueType = EventSO.ValueType.Bool, ExpectedBool = true
                                }
                            }
                        }
                    }
                }
            };
            startedWaitingEvent = true;
            manager.PlayEvent(blocker, correct.gameObject);
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(manager.isLockMove, Is.True, "The manager must remain occupied by its own event.");
            Assert.That(monitor.MoveNext(), Is.True); // Eligible contact while busy must not queue.

            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            manager.SetGlobals(releaseBusy, true);
            yield return PhysicsTicks(6);
            Assert.That(manager.isLockMove, Is.False);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(manager.GetGlobal<int>(startCount), Is.Zero, "A touch while busy cannot start after exit.");
            Assert.That(pending.Count, Is.EqualTo(1));

            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(Ready(asset, correct.gameObject), Is.True);
            Assert.That(monitor.MoveNext(), Is.True); // Now select the event, then yield before starting it.
            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True); // When the runner resumes, contact is already gone.
            Assert.That(manager.GetGlobal<int>(startCount), Is.Zero);
            Assert.That(pending.Count, Is.EqualTo(1), "Losing contact in the handoff must not consume the asset.");

            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True); // Select again.
            Assert.That(monitor.MoveNext(), Is.True); // Keep touching through the handoff.
            yield return PhysicsTicks(6);
            Assert.That(manager.GetGlobal<int>(startCount), Is.EqualTo(1));
            Assert.That(pending.Count, Is.Zero);

            player.position = OutsidePosition;
            yield return PhysicsTicks(3);
            player.position = CorrectPosition;
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(manager.GetGlobal<int>(startCount), Is.EqualTo(1), "A successfully started asset runs only once.");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ReassignedEditorColliderBindingSurvivesReloadAndStartsSameAssetOnlyAtNewBox()
        {
            const string collisionField = "Conditions.Array.data[0].CollisionObjectB";
            Rigidbody2D player = CreateActor("Player");
            BoxCollider2D oldRegion = CreateRegion(CorrectPosition);
            BoxCollider2D newRegion = CreateRegion(CorrectPosition + Vector2.right * 3f);
            string counter = Unique("SavedRebindStarts");
            EventSO created = CreateConditional(Pair(player, oldRegion.gameObject, CollisionCheckMode.CurrentContact2D));
            created.ConditionSteps[0].Flags.IsSetGlobals = true;
            created.ConditionSteps[0].SetGlobals = new SetValueData
            {
                SetValues = new[]
                {
                    new SetValueSubData
                    {
                        ValueName = counter, valueType = EventSO.ValueType.Int, setType = SetType.Add, intValue = 1
                    }
                }
            };

            temporaryEventAssetPath = "Assets/" + Unique("__CurrentContactRebind") + ".asset";
            UnityEditor.AssetDatabase.CreateAsset(created, temporaryEventAssetPath);
            owned.Remove(created); // Only this unique persistent path is deleted at teardown.
            UnityEditor.AssetDatabase.SaveAssetIfDirty(created);
            string originalGuid = UnityEditor.AssetDatabase.AssetPathToGUID(temporaryEventAssetPath);
            Assert.That(originalGuid, Is.Not.Empty);

            player.position = oldRegion.transform.position;
            yield return PhysicsTicks(3);
            Assert.That(Ready(created, oldRegion.gameObject), Is.True, "The original saved binding selected the first box.");
            player.position = OutsidePosition;
            yield return PhysicsTicks(3);

            // This is the actual AllinOne Condition scene-object picker path,
            // not a runtime reassignment or a direct invocation of the event.
            System.Type picker = null;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                picker = assembly.GetType("EventSceneTargetField");
                if (picker != null) break;
            }
            Assert.That(picker, Is.Not.Null, "The AllinOne scene target editor must be loaded.");
            var assign = picker.GetMethod("TryAssign", BindingFlags.Public | BindingFlags.Static);
            Assert.That(assign, Is.Not.Null);
            object[] binding = { created, collisionField, newRegion, null };
            Assert.That((bool)assign.Invoke(null, binding), Is.True, binding[3] as string);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(created);
            UnityEditor.Undo.ClearUndo(created);
            Resources.UnloadAsset(created);
            UnityEditor.AssetDatabase.ImportAsset(temporaryEventAssetPath,
                UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);

            var reloaded = UnityEditor.AssetDatabase.LoadAssetAtPath<EventSO>(temporaryEventAssetPath);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(temporaryEventAssetPath), Is.EqualTo(originalGuid));
            using (var serialized = new UnityEditor.SerializedObject(reloaded))
                Assert.That(serialized.FindProperty(collisionField).stringValue, Is.EqualTo(newRegion.gameObject.name));

            IEnumerator monitor = CreateControlledMonitor(reloaded, newRegion.gameObject, out IList pending);
            Assert.That(monitor.MoveNext(), Is.True);
            player.position = oldRegion.transform.position;
            yield return PhysicsTicks(3);
            Assert.That(oldRegion.IsTouching(player.transform.Find("Actual Collider2D").GetComponent<BoxCollider2D>()), Is.True);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(manager.GetGlobal<int>(counter), Is.Zero, "The old box must not start the saved EventSO.");
            Assert.That(pending.Count, Is.EqualTo(1));

            player.position = newRegion.transform.position;
            yield return PhysicsTicks(3);
            Assert.That(monitor.MoveNext(), Is.True);
            Assert.That(monitor.MoveNext(), Is.True);
            yield return PhysicsTicks(6);
            Assert.That(manager.GetGlobal<int>(counter), Is.EqualTo(1), "The new box starts the same reloaded EventSO.");
            Assert.That(pending.Count, Is.Zero);
        }
#endif
        private IEnumerator CreateControlledMonitor(EventSO asset, GameObject region, out IList pending)
        {
            // Feed one in-memory asset into the actual runner, and step only its
            // real coroutine around the Physics2D update boundaries we control.
            // Keeping this component inactive prevents competing OnEnable scans.
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var runnerType = typeof(EventPlayManager).Assembly.GetType("JYW.Game.EventPlay.PresentationAutoRunner", true);
            var pendingType = runnerType.GetNestedType("PendingSceneEvent", BindingFlags.NonPublic);
            Assert.That(pendingType, Is.Not.Null);
            var entry = System.Activator.CreateInstance(pendingType, true);
            pendingType.GetField("Asset").SetValue(entry, asset);
            pendingType.GetField("Scene").SetValue(entry, region.scene);
            pendingType.GetField("SceneName").SetValue(entry, region.scene.name);

            var host = Own(NewInContactScene(Unique("ControlledAutoRunner")));
            host.SetActive(false);
            var runner = host.AddComponent(runnerType);
            pending = (IList)runnerType.GetField("pendingEvents", members).GetValue(runner);
            pending.Add(entry);
            return (IEnumerator)runnerType.GetMethod("MonitorPendingEvents", members).Invoke(runner, null);
        }
        private static ConditionGroupData Pair(Rigidbody2D player, GameObject region,
            CollisionCheckMode mode = CollisionCheckMode.ContactHistory, string prerequisite = null)
        {
            var condition = new ConditionGroupData
            {
                Checks = ConditionChecks.CollisionAB,
                CollisionMode = mode,
                CollisionObjectA = mode == CollisionCheckMode.CurrentContact2D
                    ? player.gameObject.name + "/Actual Collider2D"
                    : player.gameObject.name,
                CollisionObjectB = region.name
            };
            if (prerequisite != null)
            {
                condition.Checks |= ConditionChecks.CheckValue;
                condition.Conditions = new[]
                {
                    new ConditionSubData
                    {
                        GlobalNames = prerequisite, valueType = EventSO.ValueType.Bool, ExpectedBool = true
                    }
                };
            }
            return condition;
        }

        private EventSO CreateConditional(ConditionGroupData condition)
        {
            var asset = Own(ScriptableObject.CreateInstance<EventSO>());
            asset.UseCondition = true;
            asset.Conditions = new[] { condition };
            asset.ConditionSteps = new[] { new EventStep() };
            return asset;
        }

        private Rigidbody2D CreateActor(string name, Vector2? position = null)
        {
            var actor = Own(NewInContactScene(Unique(name)));
            actor.transform.position = position ?? OutsidePosition;
            var body = actor.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            var child = NewInContactScene("Actual Collider2D");
            child.transform.SetParent(actor.transform, false);
            child.AddComponent<BoxCollider2D>().size = Vector2.one;
            return body;
        }

        private BoxCollider2D CreateRegion(Vector2 position)
        {
            var region = Own(NewInContactScene(Unique("Region")));
            region.transform.position = position;
            var box = region.AddComponent<BoxCollider2D>();
            box.size = Vector2.one * 2f;
            box.isTrigger = true;
            return box;
        }

        private bool Ready(EventSO asset, GameObject caller) =>
            (bool)Check.Invoke(manager, new object[] { asset, caller });

        private T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }

        private GameObject NewInContactScene(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, contactScene);
            return go;
        }

        private IEnumerator PhysicsTicks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                physics.Simulate(.02f);
                yield return null;
            }
        }

        private static string Unique(string prefix) => prefix + "_" + System.Guid.NewGuid().ToString("N");
    }
}
