using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using JYW.Game.ObjectMaker.Editor;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class ObjectMakerEditorTests
    {
        [Test]
        public void SG001DefaultMatchesDesignValues()
        {
            ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
            Assert.AreEqual("SG-001", data.displayName);
            Assert.AreEqual(1, data.core.maxHealth);
            Assert.AreEqual(0.5f, data.core.moveSpeed);
            Assert.AreEqual(5f, data.detection.frontDistance);
            Assert.AreEqual(1.5f, data.detection.rearDistance);
            Assert.AreEqual(2f, data.detection.verticalRange);
            Assert.AreEqual(8f, data.chase.maxDistanceFromSpawn);
            Assert.AreEqual(1.4f, data.chase.speedMultiplier);
            Assert.AreEqual(1.2f, data.attack.attackRange);
            Assert.AreEqual(1f, data.attack.targetInvulnerabilitySeconds);
            Assert.AreEqual(0.5f, data.attack.prepareSeconds);
            Assert.AreEqual(0.15f, data.attack.activeSeconds);
            Assert.AreEqual(0.7f, data.attack.recoverySeconds);
            Assert.AreEqual(0.8f, data.attack.cooldownSeconds);
            Assert.AreEqual(0.05f, data.hit.hitStopSeconds);
            Assert.AreEqual(6, data.ai.rules.Count);
            Assert.AreEqual(ObjectAICondition.Died, data.ai.rules[0].when);
            Assert.AreEqual(ObjectAIAction.Attack, data.ai.rules[2].action);
            Assert.AreEqual(ObjectAIAction.FollowTarget, data.ai.rules[4].action);
            Assert.AreEqual(ObjectAIAction.Patrol, data.ai.rules[5].action);
            Assert.AreEqual(1, data.player.attacks.Count);
            Assert.AreEqual(1, data.player.attacks[0].comboSteps);
            Assert.GreaterOrEqual(ObjectBehaviorCatalog.ConditionCount, 20);
            Assert.GreaterOrEqual(ObjectBehaviorCatalog.ActionCount, 25);
        }

        [Test]
        public void BuilderCreatesLinkedDefinitionAndPrefab()
        {
            ObjectMakerBuildResult result = default;
            try
            {
                ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
                data.displayName = "ObjectMaker_EditorTest";
                result = ObjectMakerPrefabBuilder.Make(data);

                Assert.AreEqual("Assets/FrameWork/2DObjectMaker", ObjectMakerPaths.Root);
                Assert.AreEqual("Assets/2DObjectMaker", ObjectMakerPaths.GeneratedRoot);
                Assert.IsNotNull(result.Definition);
                Assert.IsNotNull(result.Prefab);
                Assert.IsNotNull(result.AnimatorController);
                Assert.AreSame(result.Prefab, result.Definition.GeneratedPrefab);
                Assert.AreSame(result.AnimatorController, result.Definition.Data.animatorController);
                StringAssert.StartsWith(ObjectMakerPaths.GeneratedDefinitions + "/", result.DefinitionPath);
                StringAssert.StartsWith(ObjectMakerPaths.GeneratedPrefabs + "/", result.PrefabPath);
                StringAssert.StartsWith(ObjectMakerPaths.GeneratedAnimators + "/",
                    result.AnimatorControllerPath);
                Assert.IsNotNull(result.Prefab.GetComponent<ObjectActor2D>());
                Assert.IsNotNull(result.Prefab.GetComponent<ObjectMonsterBrain2D>());
                Assert.IsNotNull(result.Prefab.GetComponent<ObjectFxController2D>());
                Assert.IsNotNull(result.Prefab.GetComponent<Rigidbody2D>());
                Assert.IsNotNull(result.Prefab.GetComponent<Collider2D>());
                Animator prefabAnimator = result.Prefab.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(prefabAnimator);
                Assert.AreSame(result.AnimatorController, prefabAnimator.runtimeAnimatorController);
                Assert.IsNull(result.Definition.Data.previewSprite);
                Assert.IsNotNull(FindState(result.AnimatorController, ObjectCommonAnimator.Idle));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Walk));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Run));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Jump));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Attack));
                Assert.IsNotNull(FindState(result.AnimatorController, ObjectCommonAnimator.Hit));
                Assert.IsNotNull(FindState(result.AnimatorController, ObjectCommonAnimator.Dead));
                Assert.AreEqual(10, result.AnimatorController.layers[0].stateMachine.states.Length,
                    "Enemy 공용 그래프에 활성 AI 규칙과 무관한 State가 남아 있습니다.");
                Assert.IsTrue(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleParameter,
                    AnimatorControllerParameterType.Int));
                Assert.IsTrue(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyAttackPhaseParameter,
                    AnimatorControllerParameterType.Int));
                AssertAllMotionBindingsExist(result.Definition, result.AnimatorController);
                AnimatorState patrol = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleState(5, ObjectAIAction.Patrol));
                Assert.IsNotNull(patrol);
                Assert.IsTrue(HasAnyStateTransitionTo(result.AnimatorController, patrol));
                AnimatorState enemyPrepare = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleAttackState(
                        2,
                        ObjectAIAction.Attack,
                        ObjectMotionCondition.AttackPrepare));
                AnimatorState enemyActive = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleAttackState(
                        2,
                        ObjectAIAction.Attack,
                        ObjectMotionCondition.Attack));
                AnimatorState enemyRecover = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleAttackState(
                        2,
                        ObjectAIAction.Attack,
                        ObjectMotionCondition.AttackRecover));
                Assert.IsNotNull(enemyPrepare);
                Assert.IsNotNull(enemyActive);
                Assert.IsNotNull(enemyRecover);
                Assert.IsTrue(HasAnyStateTransitionTo(result.AnimatorController, enemyPrepare));
                Assert.IsTrue(HasTransitionTo(enemyPrepare, enemyActive));
                Assert.IsTrue(HasTransitionTo(enemyActive, enemyRecover));

                ObjectDefinitionData modified = result.Definition.Data.Clone();
                modified.ai.rules[5].enabled = false;
                result = ObjectMakerPrefabBuilder.Make(modified, result.Definition);
                Assert.IsNull(FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleState(5, ObjectAIAction.Patrol)),
                    "비활성화한 Enemy AI 규칙의 노드가 Modify 뒤 남아 있습니다.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(result.PrefabPath))
                    AssetDatabase.DeleteAsset(result.PrefabPath);
                if (!string.IsNullOrEmpty(result.DefinitionPath))
                    AssetDatabase.DeleteAsset(result.DefinitionPath);
                if (!string.IsNullOrEmpty(result.AnimatorControllerPath))
                    AssetDatabase.DeleteAsset(result.AnimatorControllerPath);
            }
        }

        [Test]
        public void PlayerBuilderCreatesControllerAndAttackComboGraph()
        {
            ObjectMakerBuildResult result = default;
            try
            {
                ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
                data.displayName = "ObjectMaker_PlayerGraphTest";
                data.kind = ObjectKind.Player;
                data.movement.groundWalker = false;
                data.player.attacks = new List<ObjectPlayerAttack>
                {
                    new ObjectPlayerAttack
                    {
                        displayName = "Sword",
                        key = Key.J,
                        comboSteps = 3,
                        damage = 2
                    },
                    new ObjectPlayerAttack
                    {
                        displayName = "Skill",
                        key = Key.K,
                        comboSteps = 2,
                        damage = 5
                    }
                };

                result = ObjectMakerPrefabBuilder.Make(data);

                Assert.IsNotNull(result.Prefab.GetComponent<ObjectPlayerController2D>());
                Assert.IsNull(result.Prefab.GetComponent<ObjectMonsterBrain2D>());
                Assert.AreEqual(1f, result.Prefab.GetComponent<Rigidbody2D>().gravityScale,
                    "Player는 이전 Enemy 지상 이동 설정과 관계없이 점프용 중력을 사용해야 합니다.");
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Walk));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Attack));
                Assert.IsNotNull(FindState(result.AnimatorController, ObjectCommonAnimator.Run));
                Assert.IsNotNull(FindState(result.AnimatorController, ObjectCommonAnimator.Jump));
                Assert.AreEqual(10, result.AnimatorController.layers[0].stateMachine.states.Length);
                Assert.IsTrue(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.AttackingParameter,
                    AnimatorControllerParameterType.Bool));
                Assert.IsTrue(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackParameter,
                    AnimatorControllerParameterType.Int));
                Assert.IsTrue(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerComboParameter,
                    AnimatorControllerParameterType.Int));
                AssertAllMotionBindingsExist(result.Definition, result.AnimatorController);
                for (int attack = 0; attack < 2; attack++)
                {
                    int steps = attack == 0 ? 3 : 2;
                    for (int combo = 0; combo < steps; combo++)
                    {
                        string stateName = ObjectAnimatorGraph2D.PlayerAttackState(attack, combo);
                        Assert.IsNotNull(FindState(result.AnimatorController, stateName));
                    }
                }

                AnimatorState first = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackState(0, 0));
                AnimatorState second = FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackState(0, 1));
                Assert.IsTrue(HasTransitionTo(first, second),
                    "첫 콤보 State에서 두 번째 콤보 State로 전이가 연결되지 않았습니다.");
                Assert.IsTrue(HasAnyStateTransitionTo(result.AnimatorController, first),
                    "Any State에서 첫 공격 State로 진입하는 전이가 없습니다.");
                Assert.IsFalse(HasParameter(
                    result.AnimatorController,
                    "Do_" + ObjectAnimatorGraph2D.PlayerAttackState(0, 0),
                    AnimatorControllerParameterType.Trigger),
                    "구형 공격별 Trigger가 남아 공용 변수 그래프와 중복됩니다.");
                string yaml = File.ReadAllText(Path.GetFullPath(result.DefinitionPath));
                StringAssert.Contains("player:", yaml);
                StringAssert.Contains("comboSteps: 3", yaml);

                ObjectDefinitionData remapped = result.Definition.Data.Clone();
                remapped.player.moveLeftKey = Key.None;
                remapped.player.moveRightKey = Key.None;
                remapped.player.jumpKey = Key.None;
                remapped.player.attacks[1].key = Key.None;
                result = ObjectMakerPrefabBuilder.Make(remapped, result.Definition);
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Run));
                Assert.IsNull(FindState(result.AnimatorController, ObjectCommonAnimator.Jump));
                Assert.IsNull(FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackState(1, 0)));
                Assert.IsNotNull(FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackState(0, 0)));
                Assert.IsFalse(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.SpeedParameter,
                    AnimatorControllerParameterType.Float));
                Assert.IsFalse(HasParameter(result.AnimatorController,
                    ObjectAnimatorGraph2D.JumpParameter,
                    AnimatorControllerParameterType.Trigger));

                ObjectDefinitionData switched = result.Definition.Data.Clone();
                switched.kind = ObjectKind.Monster;
                result = ObjectMakerPrefabBuilder.Make(switched, result.Definition);
                Assert.IsNull(result.Prefab.GetComponent<ObjectPlayerController2D>());
                Assert.IsNotNull(result.Prefab.GetComponent<ObjectMonsterBrain2D>());
                Assert.AreEqual(0f, result.Prefab.GetComponent<Rigidbody2D>().gravityScale);
                Assert.IsNull(FindState(
                    result.AnimatorController,
                    ObjectAnimatorGraph2D.PlayerAttackState(0, 0)));
                Assert.IsFalse(HasNamedTransition(
                    FindState(result.AnimatorController, ObjectCommonAnimator.Idle),
                    "Auto_Idle_Run"),
                    "Player에서 Enemy로 바꾼 뒤 Player 이동 전이가 남아 있습니다.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(result.PrefabPath))
                    AssetDatabase.DeleteAsset(result.PrefabPath);
                if (!string.IsNullOrEmpty(result.DefinitionPath))
                    AssetDatabase.DeleteAsset(result.DefinitionPath);
                if (!string.IsNullOrEmpty(result.AnimatorControllerPath))
                    AssetDatabase.DeleteAsset(result.AnimatorControllerPath);
            }
        }

        [Test]
        public void UpdateKeepsPrefabSpriteAndAnimatorStateMotions()
        {
            ObjectMakerBuildResult created = default;
            ObjectMakerBuildResult updated = default;
            string spritePath = string.Empty;
            string clipPath = string.Empty;
            Texture2D texture = null;
            GameObject contents = null;
            try
            {
                ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
                data.displayName = "ObjectMaker_PreservedVisualTest";
                created = ObjectMakerPrefabBuilder.Make(data);

                spritePath = AssetDatabase.GenerateUniqueAssetPath(
                    ObjectMakerPaths.GeneratedRoot + "/PreservedSprite.png");
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                File.WriteAllBytes(Path.GetFullPath(spritePath), texture.EncodeToPNG());
                AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(
                    spritePath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Sprite assignedSprite = null;
                UnityEngine.Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(spritePath);
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    assignedSprite = importedAssets[i] as Sprite;
                    if (assignedSprite != null)
                        break;
                }
                Assert.IsNotNull(assignedSprite);

                contents = PrefabUtility.LoadPrefabContents(created.PrefabPath);
                var replacementVisual = new GameObject("UserReplacementVisual");
                replacementVisual.transform.SetParent(contents.transform, false);
                replacementVisual.AddComponent<SpriteRenderer>().sprite = assignedSprite;
                replacementVisual.AddComponent<Animator>();
                PrefabUtility.SaveAsPrefabAsset(contents, created.PrefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
                contents = null;

                clipPath = AssetDatabase.GenerateUniqueAssetPath(
                    ObjectMakerPaths.GeneratedAnimators + "/PreservedAttack.anim");
                var clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
                AnimatorState attackState = FindState(
                    created.AnimatorController,
                    ObjectAnimatorGraph2D.EnemyRuleAttackState(
                        2,
                        ObjectAIAction.Attack,
                        ObjectMotionCondition.Attack));
                attackState.motion = clip;
                EditorUtility.SetDirty(attackState);
                AssetDatabase.SaveAssets();

                updated = ObjectMakerPrefabBuilder.Make(
                    created.Definition.Data.Clone(),
                    created.Definition);

                Assert.AreEqual(created.AnimatorControllerPath, updated.AnimatorControllerPath);
                Assert.AreSame(clip,
                    FindState(
                        updated.AnimatorController,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            2,
                            ObjectAIAction.Attack,
                            ObjectMotionCondition.Attack)).motion);
                Transform savedVisual = updated.Prefab.transform.Find("UserReplacementVisual");
                Assert.IsNotNull(savedVisual,
                    "Modify가 사용자가 Prefab에 추가한 표시용 자식 오브젝트를 삭제했습니다.");
                SpriteRenderer savedRenderer = savedVisual.GetComponent<SpriteRenderer>();
                Assert.AreSame(assignedSprite, savedRenderer.sprite);
                ObjectActor2D savedActor = updated.Prefab.GetComponent<ObjectActor2D>();
                Assert.AreSame(updated.Definition, savedActor.Definition);
                Assert.AreSame(savedRenderer, savedActor.SpriteRenderer,
                    "Modify 뒤 ObjectActor2D가 실제 표시용 SpriteRenderer를 사용하지 않습니다.");
                Assert.AreSame(updated.AnimatorController,
                    savedVisual.GetComponent<Animator>().runtimeAnimatorController,
                    "교체한 표시용 오브젝트에 공용 Animator가 연결되지 않았습니다.");
                Assert.IsNull(updated.Definition.Data.previewSprite,
                    "Prefab에서 바꾼 이미지는 Definition 입력값으로 되돌아가면 안 됩니다.");
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (!string.IsNullOrEmpty(created.PrefabPath))
                    AssetDatabase.DeleteAsset(created.PrefabPath);
                if (!string.IsNullOrEmpty(created.DefinitionPath))
                    AssetDatabase.DeleteAsset(created.DefinitionPath);
                if (!string.IsNullOrEmpty(created.AnimatorControllerPath))
                    AssetDatabase.DeleteAsset(created.AnimatorControllerPath);
                if (!string.IsNullOrEmpty(clipPath))
                    AssetDatabase.DeleteAsset(clipPath);
                if (!string.IsNullOrEmpty(spritePath))
                    AssetDatabase.DeleteAsset(spritePath);
            }
        }

        [Test]
        public void GeneratedPrefabResolvesItsDefinitionForModify()
        {
            ObjectMakerBuildResult result = default;
            try
            {
                ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
                data.displayName = "ObjectMaker_ModifySourceTest";
                result = ObjectMakerPrefabBuilder.Make(data);

                ObjectActor2D actor = result.Prefab.GetComponent<ObjectActor2D>();
                Assert.IsNotNull(actor);
                Assert.AreSame(result.Definition, actor.Definition,
                    "생성 Prefab이 자신을 만든 Object Definition SO를 가지고 있지 않습니다.");
                Assert.AreSame(result.Definition,
                    ObjectMakerWindow.ResolveDefinition(result.Prefab),
                    "생성 Prefab을 2DObjectMaker에 넣었을 때 Definition을 찾지 못합니다.");
            }
            finally
            {
                if (!string.IsNullOrEmpty(result.PrefabPath))
                    AssetDatabase.DeleteAsset(result.PrefabPath);
                if (!string.IsNullOrEmpty(result.DefinitionPath))
                    AssetDatabase.DeleteAsset(result.DefinitionPath);
                if (!string.IsNullOrEmpty(result.AnimatorControllerPath))
                    AssetDatabase.DeleteAsset(result.AnimatorControllerPath);
            }
        }

        [UnityTest]
        public IEnumerator WindowAcceptsTextInputAndMakeSoClick()
        {
            if (Application.isBatchMode)
                Assert.Ignore("EditorWindow 입력 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            ObjectMakerWindow window = null;
            ObjectDefinitionSO generated = null;
            string prefabPath = string.Empty;
            string definitionPath = string.Empty;
            string animatorControllerPath = string.Empty;
            const string typedSuffix = "_UI";

            try
            {
                Selection.activeObject = null;
                window = ObjectMakerWindow.Open(null);
                window.position = new Rect(100f, 100f, 900f, 760f);

                var windowData = new SerializedObject(window);
                string[] foldouts =
                {
                    "showCore", "showRules", "showContactDamage", "showMovement", "showDetection",
                    "showChase", "showAttack", "showHit", "showDestruction", "showMotions",
                    "showSceneSpawns"
                };
                for (int i = 0; i < foldouts.Length; i++)
                    windowData.FindProperty(foldouts[i]).boolValue = false;
                windowData.FindProperty("scrollPosition").vector2Value = Vector2.zero;
                windowData.ApplyModifiedPropertiesWithoutUndo();

                window.Repaint();
                yield return null;
                yield return null;
                StringAssert.StartsWith("Make", ObjectMakerWindow.LastBuildButtonLabelForTests);

                Assert.Greater(ObjectMakerWindow.LastDisplayNameRectForTests.width, 0f);
                window.Focus();
                Click(window, ObjectMakerWindow.LastDisplayNameRectForTests.center);
                yield return null;
                SendKey(window, KeyCode.End);
                for (int i = 0; i < typedSuffix.Length; i++)
                    SendCharacter(window, typedSuffix[i]);
                yield return null;

                windowData.Update();
                string typedName = windowData.FindProperty("draft")
                    .FindPropertyRelative("displayName").stringValue;
                Assert.IsTrue(typedName.EndsWith(typedSuffix, StringComparison.Ordinal),
                    "2DObjectMaker 텍스트 필드가 실제 키 입력을 받지 못했습니다: " + typedName);

                Assert.Greater(ObjectMakerWindow.LastMakeButtonRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastMakeButtonRectForTests.center);
                yield return null;
                yield return null;

                generated = window.CurrentDefinitionForTests;
                Assert.IsNotNull(generated, "Make SO 실제 클릭으로 Definition이 생성되지 않았습니다.");
                Assert.AreEqual(typedName, generated.Data.displayName);
                Assert.IsNotNull(generated.GeneratedPrefab);
                Assert.IsInstanceOf<AnimatorController>(generated.Data.animatorController);
                Assert.IsNull(generated.Data.previewSprite);
                Assert.IsFalse(generated.Data.ai.useLegacyTuning);
                Assert.AreEqual(6, generated.Data.ai.rules.Count);
                StringAssert.StartsWith("Modify", ObjectMakerWindow.LastBuildButtonLabelForTests,
                    "생성 뒤 버튼이 Modify 모드로 바뀌지 않았습니다.");

                definitionPath = AssetDatabase.GetAssetPath(generated);
                prefabPath = AssetDatabase.GetAssetPath(generated.GeneratedPrefab);
                animatorControllerPath = AssetDatabase.GetAssetPath(
                    generated.Data.animatorController);
                Assert.IsTrue(File.Exists(Path.GetFullPath(definitionPath)));
                StringAssert.StartsWith(ObjectMakerPaths.GeneratedPrefabs + "/", prefabPath);
                StringAssert.StartsWith(ObjectMakerPaths.GeneratedAnimators + "/",
                    animatorControllerPath);
                StringAssert.Contains(typedSuffix, File.ReadAllText(Path.GetFullPath(definitionPath)));

                window.Close();
                window = null;
                yield return null;

                Selection.activeObject = null;
                window = ObjectMakerWindow.Open(null);
                window.position = new Rect(100f, 100f, 900f, 760f);
                windowData = new SerializedObject(window);
                windowData.FindProperty("followSelection").boolValue = false;
                string[] modifyFoldouts =
                {
                    "showCore", "showRules", "showContactDamage", "showMovement",
                    "showDetection", "showChase", "showAttack", "showHit",
                    "showDestruction", "showMotions", "showSceneSpawns"
                };
                for (int i = 0; i < modifyFoldouts.Length; i++)
                    windowData.FindProperty(modifyFoldouts[i]).boolValue = false;
                windowData.FindProperty("scrollPosition").vector2Value = Vector2.zero;
                windowData.ApplyModifiedPropertiesWithoutUndo();
                window.Repaint();
                yield return null;
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastDefinitionDropRectForTests.width, 0f);

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences =
                    new UnityEngine.Object[] { generated.GeneratedPrefab };
                window.SendEvent(new Event
                {
                    type = EventType.DragUpdated,
                    mousePosition = ObjectMakerWindow.LastDefinitionDropRectForTests.center
                });
                window.SendEvent(new Event
                {
                    type = EventType.DragPerform,
                    mousePosition = ObjectMakerWindow.LastDefinitionDropRectForTests.center
                });
                yield return null;
                yield return null;
                Assert.AreSame(generated, window.CurrentDefinitionForTests,
                    "생성 Prefab을 드롭했을 때 Prefab이 가진 Definition을 읽지 못했습니다.");
                StringAssert.StartsWith("Modify", ObjectMakerWindow.LastBuildButtonLabelForTests);

                windowData.Update();
                windowData.FindProperty("draft")
                    .FindPropertyRelative("core")
                    .FindPropertyRelative("maxHealth").intValue = 9;
                windowData.ApplyModifiedPropertiesWithoutUndo();
                Click(window, ObjectMakerWindow.LastMakeButtonRectForTests.center);
                yield return null;
                yield return null;
                Assert.AreSame(generated, window.CurrentDefinitionForTests);
                Assert.AreEqual(9, generated.Data.core.maxHealth,
                    "Modify 실제 클릭이 기존 Definition을 재정의하지 못했습니다.");
                Assert.AreEqual(definitionPath, AssetDatabase.GetAssetPath(generated));
                Assert.AreEqual(prefabPath, AssetDatabase.GetAssetPath(generated.GeneratedPrefab));
                Assert.AreEqual(animatorControllerPath,
                    AssetDatabase.GetAssetPath(generated.Data.animatorController));
                StringAssert.Contains("maxHealth: 9",
                    File.ReadAllText(Path.GetFullPath(definitionPath)));

                window.Close();
                window = null;
                yield return null;
                window = ObjectMakerWindow.Open(generated.GeneratedPrefab);
                yield return null;
                Assert.AreSame(generated, window.CurrentDefinitionForTests,
                    "Modify 뒤 Prefab으로 창을 다시 열었을 때 Definition이 유지되지 않았습니다.");
                Assert.AreEqual(typedName, window.CurrentDefinitionForTests.Data.displayName);
            }
            finally
            {
                if (window != null)
                    window.Close();
                Selection.activeObject = null;
                if (!string.IsNullOrEmpty(prefabPath))
                    AssetDatabase.DeleteAsset(prefabPath);
                if (!string.IsNullOrEmpty(definitionPath))
                    AssetDatabase.DeleteAsset(definitionPath);
                if (!string.IsNullOrEmpty(animatorControllerPath))
                    AssetDatabase.DeleteAsset(animatorControllerPath);
            }
        }

        [UnityTest]
        public IEnumerator RuleListAddButtonCreatesReusableRuleCard()
        {
            if (Application.isBatchMode)
                Assert.Ignore("EditorWindow 입력 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            ObjectMakerWindow window = null;
            try
            {
                Selection.activeObject = null;
                window = ObjectMakerWindow.Open(null);
                window.position = new Rect(120f, 120f, 900f, 760f);
                var serialized = new SerializedObject(window);
                string[] closedFoldouts =
                {
                    "showCore", "showContactDamage", "showHit", "showDestruction",
                    "showMotions", "showSceneSpawns"
                };
                for (int i = 0; i < closedFoldouts.Length; i++)
                    serialized.FindProperty(closedFoldouts[i]).boolValue = false;
                serialized.FindProperty("showRules").boolValue = true;
                SerializedProperty rules = serialized.FindProperty("draft")
                    .FindPropertyRelative("ai")
                    .FindPropertyRelative("rules");
                rules.arraySize = 0;
                serialized.FindProperty("scrollPosition").vector2Value = Vector2.zero;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                window.Repaint();
                yield return null;
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastAddRuleButtonRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastAddRuleButtonRectForTests.center);
                yield return null;

                serialized.Update();
                rules = serialized.FindProperty("draft")
                    .FindPropertyRelative("ai")
                    .FindPropertyRelative("rules");
                Assert.AreEqual(1, rules.arraySize,
                    "실제 '+ ~할 때 규칙 추가' 클릭으로 규칙 카드가 생성되지 않았습니다.");
                SerializedProperty added = rules.GetArrayElementAtIndex(0);
                Assert.AreEqual((int)ObjectAICondition.NoTarget,
                    added.FindPropertyRelative("when").enumValueIndex);
                Assert.AreEqual((int)ObjectAIAction.Stop,
                    added.FindPropertyRelative("action").enumValueIndex);
            }
            finally
            {
                if (window != null)
                    window.Close();
                Selection.activeObject = null;
            }
        }

        [UnityTest]
        public IEnumerator RuleDropdownsAcceptActualOptionClicks()
        {
            if (Application.isBatchMode)
                Assert.Ignore("EditorWindow 입력 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            ObjectMakerWindow window = null;
            try
            {
                Selection.activeObject = null;
                window = ObjectMakerWindow.Open(null);
                window.position = new Rect(140f, 140f, 900f, 760f);
                var serialized = new SerializedObject(window);
                string[] closedFoldouts =
                {
                    "showCore", "showContactDamage", "showHit", "showDestruction",
                    "showMotions", "showSceneSpawns"
                };
                for (int i = 0; i < closedFoldouts.Length; i++)
                    serialized.FindProperty(closedFoldouts[i]).boolValue = false;
                serialized.FindProperty("showRules").boolValue = true;
                SerializedProperty rules = serialized.FindProperty("draft")
                    .FindPropertyRelative("ai")
                    .FindPropertyRelative("rules");
                rules.arraySize = 1;
                SerializedProperty first = rules.GetArrayElementAtIndex(0);
                first.FindPropertyRelative("when").enumValueIndex = (int)ObjectAICondition.NoTarget;
                first.FindPropertyRelative("action").enumValueIndex = (int)ObjectAIAction.Stop;
                serialized.FindProperty("scrollPosition").vector2Value = Vector2.zero;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                window.Repaint();
                yield return null;
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastConditionPopupRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastConditionPopupRectForTests.center);
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastConditionChoiceRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastConditionChoiceRectForTests.center);
                yield return null;

                serialized.Update();
                first = serialized.FindProperty("draft")
                    .FindPropertyRelative("ai")
                    .FindPropertyRelative("rules")
                    .GetArrayElementAtIndex(0);
                Assert.AreEqual((int)ObjectAICondition.HasTarget,
                    first.FindPropertyRelative("when").enumValueIndex,
                    "~할 때 드롭다운의 실제 항목 클릭이 반영되지 않았습니다.");

                Click(window, ObjectMakerWindow.LastActionPopupRectForTests.center);
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastActionChoiceRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastActionChoiceRectForTests.center);
                yield return null;
                serialized.Update();
                first = serialized.FindProperty("draft")
                    .FindPropertyRelative("ai")
                    .FindPropertyRelative("rules")
                    .GetArrayElementAtIndex(0);
                Assert.AreEqual((int)ObjectAIAction.Wait,
                    first.FindPropertyRelative("action").enumValueIndex,
                    "공용 AI 행동 드롭다운의 실제 항목 클릭이 반영되지 않았습니다.");
            }
            finally
            {
                if (window != null)
                    window.Close();
                Selection.activeObject = null;
            }
        }

        [UnityTest]
        public IEnumerator PlayerTargetAndAddAttackButtonsChangeActualDraft()
        {
            if (Application.isBatchMode)
                Assert.Ignore("EditorWindow 입력 검증은 그래픽 장치가 있는 Editor에서 실행합니다.");

            ObjectMakerWindow window = null;
            try
            {
                Selection.activeObject = null;
                window = ObjectMakerWindow.Open(null);
                window.position = new Rect(160f, 160f, 900f, 820f);
                var serialized = new SerializedObject(window);
                serialized.FindProperty("showCore").boolValue = false;
                serialized.FindProperty("showPlayerControls").boolValue = false;
                serialized.FindProperty("showPlayerAttacks").boolValue = true;
                serialized.FindProperty("showHit").boolValue = false;
                serialized.FindProperty("showDestruction").boolValue = false;
                serialized.FindProperty("showSceneSpawns").boolValue = false;
                serialized.FindProperty("draft")
                    .FindPropertyRelative("player")
                    .FindPropertyRelative("attacks").arraySize = 0;
                serialized.FindProperty("scrollPosition").vector2Value = Vector2.zero;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                window.Repaint();
                yield return null;
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastPlayerTargetButtonRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastPlayerTargetButtonRectForTests.center);
                yield return null;

                serialized.Update();
                Assert.AreEqual(
                    (int)ObjectKind.Player,
                    serialized.FindProperty("draft")
                        .FindPropertyRelative("kind").enumValueIndex,
                    "실제 Player 버튼 클릭이 제작 대상에 반영되지 않았습니다.");

                window.Repaint();
                yield return null;
                Assert.Greater(ObjectMakerWindow.LastAddPlayerAttackButtonRectForTests.width, 0f);
                Click(window, ObjectMakerWindow.LastAddPlayerAttackButtonRectForTests.center);
                yield return null;

                serialized.Update();
                SerializedProperty attacks = serialized.FindProperty("draft")
                    .FindPropertyRelative("player")
                    .FindPropertyRelative("attacks");
                Assert.AreEqual(1, attacks.arraySize,
                    "실제 '+ 공격 추가' 클릭으로 공격 카드가 생성되지 않았습니다.");
                Assert.AreEqual(1,
                    attacks.GetArrayElementAtIndex(0)
                        .FindPropertyRelative("comboSteps").intValue);
            }
            finally
            {
                if (window != null)
                    window.Close();
                Selection.activeObject = null;
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

        private static AnimatorState FindState(AnimatorController controller, string name)
        {
            ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state != null && string.Equals(state.name, name, StringComparison.Ordinal))
                    return state;
            }
            return null;
        }

        private static bool HasParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == type &&
                    string.Equals(parameters[i].name, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void AssertAllMotionBindingsExist(
            ObjectDefinitionSO definition,
            AnimatorController controller)
        {
            Assert.IsNotNull(definition.Data.motions);
            for (int i = 0; i < definition.Data.motions.Count; i++)
            {
                ObjectMotionBinding binding = definition.Data.motions[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.animatorState))
                    continue;
                Assert.IsNotNull(FindState(controller, binding.animatorState),
                    binding.condition + " Motion 변수가 없는 Animator State를 내보냈습니다: " +
                    binding.animatorState);
            }
        }

        private static bool HasTransitionTo(AnimatorState source, AnimatorState destination)
        {
            AnimatorStateTransition[] transitions = source.transitions;
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null && transitions[i].destinationState == destination)
                    return true;
            }
            return false;
        }

        private static bool HasNamedTransition(AnimatorState source, string transitionName)
        {
            AnimatorStateTransition[] transitions = source.transitions;
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null && string.Equals(
                        transitions[i].name,
                        transitionName,
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool HasAnyStateTransitionTo(
            AnimatorController controller,
            AnimatorState destination)
        {
            AnimatorStateTransition[] transitions =
                controller.layers[0].stateMachine.anyStateTransitions;
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i] != null && transitions[i].destinationState == destination)
                    return true;
            }
            return false;
        }

        private static void SendKey(EditorWindow window, KeyCode keyCode)
        {
            window.SendEvent(new Event { type = EventType.KeyDown, keyCode = keyCode });
            window.SendEvent(new Event { type = EventType.KeyUp, keyCode = keyCode });
        }

        private static void SendCharacter(EditorWindow window, char character)
        {
            window.SendEvent(new Event
            {
                type = EventType.KeyDown,
                character = character,
                keyCode = KeyCode.None
            });
            window.SendEvent(new Event
            {
                type = EventType.KeyUp,
                character = character,
                keyCode = KeyCode.None
            });
        }
    }
}
