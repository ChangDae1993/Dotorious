using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class ObjectMakerPlayModeTests
    {
        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                Assert.Ignore("2DObjectMaker가 Settings에서 꺼져 있어 런타임 행동 테스트를 생략합니다.");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                    UnityEngine.Object.DestroyImmediate(cleanup[i]);
            }
            cleanup.Clear();

            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < allObjects.Length; i++)
            {
                GameObject candidate = allObjects[i];
                if (candidate != null && candidate.scene.IsValid() &&
                    (candidate.name.StartsWith("2DObjectMakerFx_", StringComparison.Ordinal) ||
                     candidate.name.EndsWith("_Projectile", StringComparison.Ordinal)))
                    UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [UnityTest]
        public IEnumerator DamageUsesDefenseAndSharedInvulnerability()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.maxHealth = 5;
                data.core.defense = 1;
                data.hit.knockback = 0.2f;
            });
            ObjectActor2D actor = CreateActor("ObjectMakerTestPlayer", definition, Vector2.zero);

            var request = new ObjectDamageRequest(
                2,
                null,
                Vector2.left,
                1f,
                0f,
                ObjectDamageCause.Attack);
            Assert.IsTrue(actor.TryReceiveDamage(request));
            Assert.AreEqual(4, actor.CurrentHealth);
            Assert.AreEqual(0.2f, actor.Body.position.x, 0.001f);
            Assert.IsFalse(actor.TryReceiveDamage(request));
            Assert.AreEqual(4, actor.CurrentHealth);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DetectsPlayerAcrossAdditiveSceneAndUsesChaseSpeed()
        {
            CreateGround(new Vector2(0f, -1f), new Vector2(20f, 1f));
            ObjectDefinitionSO monsterDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.attack.attackRange = 0.5f;
                data.attack.contactDamage = 0;
            });
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.maxHealth = 5;
                data.hit.knockback = 0f;
            });

            ObjectActor2D monster = CreateActor("ObjectMakerTestMonster", monsterDefinition, Vector2.zero, true);
            ObjectActor2D player = CreateActor("ObjectMakerTestPlayer", playerDefinition, new Vector2(3f, 0f));
            Scene additiveScene = SceneManager.CreateScene("ObjectMaker_Additive_Test");
            SceneManager.MoveGameObjectToScene(player.gameObject, additiveScene);
            ObjectMonsterBrain2D brain = monster.GetComponent<ObjectMonsterBrain2D>();

            float timeout = Time.realtimeSinceStartup + 1f;
            while (brain.State != ObjectBrainState.Chase && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(ObjectBrainState.Chase, brain.State);
            Assert.AreSame(player, brain.Target);
            Assert.AreEqual(FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker),
                GameObject.Find("2DObjectMakerAutoRunner") != null);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0.7f, monster.Body.linearVelocity.x, 0.12f);

            AsyncOperation unload = SceneManager.UnloadSceneAsync(additiveScene);
            while (unload != null && !unload.isDone)
                yield return null;
        }

        [UnityTest]
        public IEnumerator AttackHonorsPrepareActiveRecoveryAndInvulnerability()
        {
            ObjectDefinitionSO monsterDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.movement.groundWalker = false;
                data.attack.prepareSeconds = 0.08f;
                data.attack.activeSeconds = 0.04f;
                data.attack.recoverySeconds = 0.08f;
                data.attack.cooldownSeconds = 0.08f;
                data.attack.contactDamage = 0;
                data.attack.targetInvulnerabilitySeconds = 1f;
            });
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.maxHealth = 5;
                data.hit.knockback = 0f;
                data.hit.blinkSeconds = 0f;
            });
            ObjectActor2D monster = CreateActor("ObjectMakerAttackMonster", monsterDefinition, Vector2.zero, true);
            ObjectActor2D player = CreateActor("ObjectMakerAttackPlayer", playerDefinition, new Vector2(1f, 0f));
            ObjectMonsterBrain2D brain = monster.GetComponent<ObjectMonsterBrain2D>();

            float timeout = Time.realtimeSinceStartup + 1f;
            while (brain.State != ObjectBrainState.AttackPrepare && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(ObjectBrainState.AttackPrepare, brain.State);
            Assert.AreEqual(5, player.CurrentHealth);
            yield return new WaitForSeconds(0.04f);
            Assert.AreEqual(5, player.CurrentHealth, "공격 준비 중에는 판정이 발생하면 안 됩니다.");

            timeout = Time.realtimeSinceStartup + 1f;
            while (player.CurrentHealth == 5 && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(4, player.CurrentHealth);
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(4, player.CurrentHealth,
                "한 번의 피격 무적 시간 동안 재공격 피해가 중첩되었습니다.");
        }

        [UnityTest]
        public IEnumerator SustainedContactDamagesAgainOnlyAfterInvulnerability()
        {
            ObjectDefinitionSO monsterDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.movement.groundWalker = false;
                data.core.moveSpeed = 0f;
                data.attack.attackRange = 0.01f;
                data.attack.contactDamage = 1;
                data.attack.contactKnockback = 0f;
                data.attack.targetInvulnerabilitySeconds = 0.12f;
            });
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.maxHealth = 5;
                data.hit.knockback = 0f;
                data.hit.blinkSeconds = 0f;
            });
            ObjectActor2D monster = CreateActor("ObjectMakerContactMonster", monsterDefinition, Vector2.zero, true);
            ObjectActor2D player = CreateActor("ObjectMakerContactPlayer", playerDefinition, new Vector2(0.75f, 0f));
            monster.BodyCollider.isTrigger = true;
            player.BodyCollider.isTrigger = true;

            float timeout = Time.realtimeSinceStartup + 1f;
            while (player.CurrentHealth == 5 && Time.realtimeSinceStartup < timeout)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(4, player.CurrentHealth);

            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(4, player.CurrentHealth);
            timeout = Time.realtimeSinceStartup + 1f;
            while (player.CurrentHealth == 4 && Time.realtimeSinceStartup < timeout)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(3, player.CurrentHealth);
        }

        [UnityTest]
        public IEnumerator CliffStopsChaseAndLocksSameVisibleTargetUntilItLeaves()
        {
            CreateGround(new Vector2(0f, -1f), new Vector2(1f, 1f));
            ObjectDefinitionSO monsterDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.attack.attackRange = 0.5f;
                data.attack.contactDamage = 0;
                data.chase.minimumChaseSeconds = 0f;
                data.chase.unreachableLookSeconds = 0.06f;
            });
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.hit.knockback = 0f;
            });
            ObjectActor2D monster = CreateActor("ObjectMakerCliffMonster", monsterDefinition, Vector2.zero, true);
            CreateActor("ObjectMakerCliffPlayer", playerDefinition, new Vector2(3f, 0f));
            ObjectMonsterBrain2D brain = monster.GetComponent<ObjectMonsterBrain2D>();

            float timeout = Time.realtimeSinceStartup + 1f;
            while (brain.State != ObjectBrainState.Chase && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(ObjectBrainState.Chase, brain.State);

            yield return new WaitForSeconds(0.25f);
            Assert.Less(Mathf.Abs(monster.Body.position.x), 0.05f,
                "낭떠러지 앞에서 몬스터가 발판 밖으로 이동했습니다.");
            Assert.AreNotEqual(ObjectBrainState.Chase, brain.State,
                "포기한 다른 발판의 같은 대상을 즉시 다시 추격했습니다.");
            Assert.AreNotEqual(ObjectBrainState.AttackPrepare, brain.State);
        }

        [UnityTest]
        public IEnumerator DeathDisablesCollisionAndRemovesActor()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.maxHealth = 1;
                data.destruction.destroyDelaySeconds = 10f;
                data.destruction.disableCollisionsImmediately = true;
            });
            ObjectActor2D actor = CreateActor("ObjectMakerDeathMonster", definition, Vector2.zero, true);

            Assert.IsTrue(actor.TryReceiveDamage(new ObjectDamageRequest(
                1, null, Vector2.left, 0f, 0f, ObjectDamageCause.Script)));
            Assert.IsTrue(actor.IsDead);
            Assert.IsFalse(actor.BodyCollider.enabled);
            Assert.IsFalse(actor.gameObject.activeSelf,
                "HP 0이 된 호출 안에서 본체가 즉시 사라지지 않았습니다.");
            yield return null;
            Assert.IsTrue(actor == null,
                "HP 0 본체가 프레임 종료 뒤에도 제거되지 않았습니다.");
        }

        [UnityTest]
        public IEnumerator HealthReachedDestroySelfRuleRemovesActorImmediatelyAndKeepsEffect()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.maxHealth = 1;
                data.destruction.destroyDelaySeconds = 10f;
                data.ai.useLegacyTuning = false;
                data.ai.rules = new List<ObjectAIRule>
                {
                    new ObjectAIRule
                    {
                        label = "HP 0 도달",
                        enabled = true,
                        when = ObjectAICondition.HealthReached,
                        action = ObjectAIAction.DestroySelf,
                        condition = new ObjectAIConditionParameters { healthValue = 0 },
                        effect = ObjectRuleEffect.Explosion,
                        effectSettings = new ObjectRuleEffectParameters
                        {
                            duration = 0.5f,
                            amount = 4
                        }
                    }
                };
            });
            ObjectActor2D actor = CreateActor(
                "ObjectMakerImmediateDeathMonster", definition, Vector2.zero, true);
            yield return null;

            Assert.IsTrue(actor.TryReceiveDamage(new ObjectDamageRequest(
                1, null, Vector2.left, 0f, 0f, ObjectDamageCause.Script)));
            Assert.IsTrue(actor.IsDead);
            Assert.IsFalse(actor.gameObject.activeSelf,
                "HP 0 본체가 연출과 함께 남아 있습니다.");
            ObjectFxController2D effects = actor.GetComponent<ObjectFxController2D>();
            Assert.IsNotNull(effects);
            Assert.AreEqual(1, effects.SpawnedEffectCount,
                "HP 도달 연출이 정확히 한 번 재생되지 않았습니다.");
            Assert.IsNotNull(GameObject.Find("2DObjectMakerFx_Circle"),
                "본체를 숨기기 전에 독립 연출이 생성되지 않았습니다.");
            yield return null;

            Assert.IsTrue(actor == null,
                "HP 도달 → 즉시 파괴 규칙인데 본체가 제거되지 않았습니다.");
            Assert.IsNotNull(GameObject.Find("2DObjectMakerFx_Circle"),
                "본체 제거와 함께 독립 파괴 연출까지 사라졌습니다.");
        }

        [UnityTest]
        public IEnumerator HealthZeroRemovalDoesNotDependOnMonsterBrainSubscription()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.maxHealth = 1;
                data.destruction.destroyDelaySeconds = 10f;
                data.ai.useLegacyTuning = false;
                data.ai.rules = new List<ObjectAIRule>
                {
                    new ObjectAIRule
                    {
                        label = "HP 0 도달",
                        enabled = true,
                        when = ObjectAICondition.HealthReached,
                        action = ObjectAIAction.DestroySelf,
                        condition = new ObjectAIConditionParameters { healthValue = 0 }
                    }
                };
            });
            ObjectActor2D actor = CreateActor(
                "ObjectMakerImmediateDeathWithoutBrain", definition, Vector2.zero);
            yield return null;

            Assert.IsTrue(actor.TryReceiveDamage(new ObjectDamageRequest(
                1, null, Vector2.left, 0f, 0f, ObjectDamageCause.Attack)));
            Assert.IsTrue(actor.IsDead);
            Assert.IsFalse(actor.gameObject.activeSelf);
            yield return null;

            Assert.IsTrue(actor == null,
                "HP 0 제거가 Monster Brain 이벤트 구독 여부에 의존하고 있습니다.");
        }

        [UnityTest]
        public IEnumerator HealthReachedRuleUsesConfiguredAbsoluteHpAndCommonAction()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.maxHealth = 3;
                data.hit.blinkSeconds = 0f;
                data.hit.interruptCurrentAction = false;
                data.ai.useLegacyTuning = false;
                data.ai.rules = new List<ObjectAIRule>
                {
                    new ObjectAIRule
                    {
                        label = "HP 2 도달",
                        when = ObjectAICondition.HealthReached,
                        action = ObjectAIAction.Stop,
                        condition = new ObjectAIConditionParameters { healthValue = 2 },
                        settings = new ObjectAIActionParameters
                        {
                            durationSeconds = Vector2.zero,
                            groundMovement = false
                        }
                    }
                };
            });
            ObjectActor2D actor = CreateActor(
                "ObjectMakerHealthReachedMonster", definition, Vector2.zero, true);
            ObjectMonsterBrain2D brain = actor.GetComponent<ObjectMonsterBrain2D>();
            yield return null;

            Assert.AreEqual(-1, brain.ActiveRuleIndex,
                "설정 HP보다 높을 때 HP 도달 규칙이 먼저 실행됐습니다.");
            Assert.IsTrue(actor.TryReceiveDamage(new ObjectDamageRequest(
                1, null, Vector2.left, 0f, 0f, ObjectDamageCause.Script)));
            float timeout = Time.realtimeSinceStartup + 0.5f;
            while (brain.ActiveRuleIndex != 0 && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(2, actor.CurrentHealth);
            Assert.AreEqual(0, brain.ActiveRuleIndex);
            Assert.AreEqual(ObjectAIAction.Stop, brain.ActiveAction,
                "HP 도달 조건에서 선택한 공용 AI 행동이 실행되지 않았습니다.");
        }

        [UnityTest]
        public IEnumerator SameStopActionCanBeReusedByDifferentWhenRules()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.movement.groundWalker = false;
                data.core.maxHealth = 2;
                data.hit.blinkSeconds = 0f;
                data.hit.interruptCurrentAction = false;
                data.ai.useLegacyTuning = false;
                data.ai.rules = new List<ObjectAIRule>
                {
                    new ObjectAIRule
                    {
                        label = "저체력일 때 정지",
                        when = ObjectAICondition.LowHealth,
                        action = ObjectAIAction.Stop,
                        condition = new ObjectAIConditionParameters { healthRatio = 0.5f },
                        settings = new ObjectAIActionParameters
                        {
                            durationSeconds = Vector2.zero,
                            groundMovement = false
                        }
                    },
                    new ObjectAIRule
                    {
                        label = "타겟이 없을 때도 정지",
                        when = ObjectAICondition.NoTarget,
                        action = ObjectAIAction.Stop,
                        settings = new ObjectAIActionParameters
                        {
                            durationSeconds = Vector2.zero,
                            groundMovement = false
                        }
                    }
                };
            });
            ObjectActor2D monster = CreateActor(
                "ObjectMakerSharedStopMonster", definition, Vector2.zero, true);
            ObjectMonsterBrain2D brain = monster.GetComponent<ObjectMonsterBrain2D>();

            yield return null;
            yield return null;
            Assert.AreEqual(1, brain.ActiveRuleIndex);
            Assert.AreEqual(ObjectAIAction.Stop, brain.ActiveAction);

            Assert.IsTrue(monster.TryReceiveDamage(new ObjectDamageRequest(
                1, null, Vector2.left, 0f, 0f, ObjectDamageCause.Script)));
            float timeout = Time.realtimeSinceStartup + 0.5f;
            while (brain.ActiveRuleIndex != 0 && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(0, brain.ActiveRuleIndex);
            Assert.AreEqual(ObjectAIAction.Stop, brain.ActiveAction,
                "서로 다른 '~할 때' 규칙에서 동일한 공용 정지 AI를 재사용하지 못했습니다.");
        }

        [UnityTest]
        public IEnumerator ShootRuleCreatesProjectileAndDamagesPlayer()
        {
            ObjectDefinitionSO monsterDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.movement.groundWalker = false;
                data.attack.contactDamage = 0;
                data.ai.useLegacyTuning = false;
                ObjectAIRule attack = new ObjectAIRule
                {
                    label = "가까우면 사격",
                    when = ObjectAICondition.TargetWithinDistance,
                    action = ObjectAIAction.Shoot
                };
                attack.condition.distance = 2f;
                attack.settings.damage = 1;
                attack.settings.attackRange = 2f;
                attack.settings.prepareSeconds = 0.02f;
                attack.settings.activeSeconds = 0.02f;
                attack.settings.recoverySeconds = 0.02f;
                attack.settings.cooldownSeconds = 1f;
                attack.settings.projectileSpeed = 8f;
                attack.settings.projectileLifetime = 1f;
                attack.settings.targetInvulnerabilitySeconds = 0.2f;
                ObjectAIRule detect = new ObjectAIRule
                {
                    label = "발견하면 따라가기",
                    when = ObjectAICondition.TargetDetected,
                    action = ObjectAIAction.FollowTarget
                };
                detect.condition.frontDistance = 4f;
                detect.condition.rearDistance = 4f;
                detect.settings.groundMovement = false;
                data.ai.rules = new List<ObjectAIRule> { attack, detect };
            });
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.maxHealth = 3;
                data.hit.blinkSeconds = 0f;
                data.hit.knockback = 0f;
            });
            CreateActor("ObjectMakerRuleShooter", monsterDefinition, Vector2.zero, true);
            ObjectActor2D player = CreateActor(
                "ObjectMakerRuleShootTarget", playerDefinition, new Vector2(1.25f, 0f));

            float timeout = Time.realtimeSinceStartup + 1.5f;
            while (player.CurrentHealth == 3 && Time.realtimeSinceStartup < timeout)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(2, player.CurrentHealth,
                "Shoot 공용 행동이 실제 투사체를 만들고 플레이어에게 피해를 주지 못했습니다.");
        }

        [UnityTest]
        public IEnumerator ProceduralRuleEffectSpawnsAndCleansVisuals()
        {
            ObjectDefinitionSO definition = CreateDefinition(ObjectKind.Monster);
            ObjectActor2D actor = CreateActor(
                "ObjectMakerFxActor", definition, Vector2.zero);
            ObjectFxController2D effects = actor.gameObject.AddComponent<ObjectFxController2D>();
            effects.Configure(actor, null);
            var settings = new ObjectRuleEffectParameters
            {
                amount = 4,
                duration = 0.1f,
                scale = 0.5f,
                primaryColor = Color.cyan,
                secondaryColor = Color.white
            };

            effects.Play(ObjectRuleEffect.PixelScatter, settings);
            Assert.AreEqual(1, effects.SpawnedEffectCount);
            Assert.IsNotNull(GameObject.Find("2DObjectMakerFx_Square"));
            yield return new WaitForSeconds(0.25f);
            Assert.IsNull(GameObject.Find("2DObjectMakerFx_Square"),
                "절차형 연출 입자가 수명 뒤 정리되지 않았습니다.");
        }

        [UnityTest]
        public IEnumerator PlayerControllerMovesJumpsAndAppliesQueuedComboDamage()
        {
            CreateGround(new Vector2(0f, -1f), new Vector2(20f, 1f));
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.moveSpeed = 3f;
                data.player.runKey = UnityEngine.InputSystem.Key.LeftShift;
                data.player.runSpeedMultiplier = 2f;
                data.player.jumpForce = 5f;
                data.player.groundCheckRadius = 0.15f;
                data.player.attacks = new List<ObjectPlayerAttack>
                {
                    new ObjectPlayerAttack
                    {
                        displayName = "Test Combo",
                        damage = 1,
                        inputDelaySeconds = 0.01f,
                        activeSeconds = 0.02f,
                        recoverySeconds = 0.04f,
                        cooldownSeconds = 0f,
                        range = 1.2f,
                        hitboxHeight = 1.2f,
                        knockback = 0f,
                        targetInvulnerabilitySeconds = 0f,
                        comboSteps = 2,
                        comboInputWindowSeconds = 1f,
                        comboResetSeconds = 1f
                    }
                };
            });
            ObjectDefinitionSO enemyDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.maxHealth = 3;
                data.hit.blinkSeconds = 0f;
                data.hit.knockback = 0f;
            });

            ObjectActor2D player = CreateActor(
                "ObjectMakerControlledPlayer", playerDefinition, Vector2.zero);
            player.Body.bodyType = RigidbodyType2D.Dynamic;
            player.Body.gravityScale = 1f;
            var probe = new GameObject("GroundProbe");
            probe.transform.SetParent(player.transform, false);
            probe.transform.localPosition = Vector3.down * 0.5f;
            ObjectPlayerController2D controller =
                player.gameObject.AddComponent<ObjectPlayerController2D>();
            controller.Configure(player, player.Body, player.BodyCollider, probe.transform);

            ObjectActor2D enemy = CreateActor(
                "ObjectMakerPlayerAttackTarget", enemyDefinition, new Vector2(0.8f, 0f));
            enemy.Body.bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
            Assert.IsTrue(controller.RefreshGrounded(), "GroundProbe가 지면을 확인하지 못했습니다.");

            Assert.IsTrue(controller.TryStartAttack(0));
            yield return new WaitForSeconds(0.02f);
            Assert.AreEqual(2, enemy.CurrentHealth,
                "첫 번째 콤보 공격의 데미지가 적용되지 않았습니다.");
            Assert.IsTrue(controller.TryStartAttack(0),
                "콤보 입력 가능시간 안의 두 번째 입력이 예약되지 않았습니다.");

            float timeout = Time.realtimeSinceStartup + 0.5f;
            while (enemy.CurrentHealth > 1 && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.AreEqual(1, enemy.CurrentHealth,
                "예약된 두 번째 콤보 공격이 다음 State/판정으로 진행되지 않았습니다.");

            timeout = Time.realtimeSinceStartup + 0.5f;
            while (controller.IsAttacking && Time.realtimeSinceStartup < timeout)
                yield return null;
            enemy.transform.position = new Vector2(5f, 0f);
            Physics2D.SyncTransforms();
            controller.SetMoveInput(1f);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(3f, player.Body.linearVelocity.x, 0.15f,
                "오른쪽 이동 입력이 설정된 이동 속도로 적용되지 않았습니다.");

            controller.SetRunInput(true);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(controller.IsRunning);
            Assert.AreEqual(6f, player.Body.linearVelocity.x, 0.15f,
                "달리기 키 입력이 설정된 속도 배율로 적용되지 않았습니다.");
            controller.SetRunInput(false);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(controller.IsRunning);
            Assert.AreEqual(3f, player.Body.linearVelocity.x, 0.15f,
                "달리기 키를 놓은 뒤 걷기 속도로 복구되지 않았습니다.");

            controller.SetMoveInput(0f);
            Physics2D.SyncTransforms();
            Assert.IsTrue(controller.TryJump(), "지면 위 점프 입력이 실행되지 않았습니다.");
            Assert.Greater(player.Body.linearVelocity.y, 0f);
            timeout = Time.realtimeSinceStartup + 0.3f;
            while (controller.RefreshGrounded() && Time.realtimeSinceStartup < timeout)
                yield return new WaitForFixedUpdate();
            Assert.IsFalse(controller.IsGrounded, "점프 후에도 계속 지면 상태로 남아 있습니다.");
            Assert.IsFalse(controller.TryJump(),
                "공중에서 점프를 다시 실행해 Jump 모션이 반복될 수 있습니다.");
        }

        [UnityTest]
        public IEnumerator PlayerAttackStartsAtOutermostColliderFrontAndDestroysTarget()
        {
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.player.attacks = new List<ObjectPlayerAttack>
                {
                    new ObjectPlayerAttack
                    {
                        key = UnityEngine.InputSystem.Key.A,
                        damage = 1,
                        inputDelaySeconds = 0.01f,
                        activeSeconds = 0.01f,
                        recoverySeconds = 0.01f,
                        cooldownSeconds = 0f,
                        range = 1.2f,
                        hitboxHeight = 1.2f,
                        knockback = 0f,
                        targetInvulnerabilitySeconds = 0f
                    }
                };
            });
            ObjectDefinitionSO enemyDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.objectId = "SG-001-AttackRangeRegression";
                data.core.maxHealth = 1;
                data.core.defense = 0;
                data.hit.blinkSeconds = 0f;
                data.hit.knockback = 0f;
                data.destruction.destroyDelaySeconds = 0f;
            });

            ObjectActor2D player = CreateActor(
                "ObjectMakerLargeColliderPlayer", playerDefinition, Vector2.zero);
            player.Body.bodyType = RigidbodyType2D.Dynamic;
            player.Body.gravityScale = 0f;
            player.Body.freezeRotation = true;
            BoxCollider2D addedSceneCollider = player.gameObject.AddComponent<BoxCollider2D>();
            addedSceneCollider.size = new Vector2(3.94f, 2f);
            var probe = new GameObject("GroundProbe");
            probe.transform.SetParent(player.transform, false);
            ObjectPlayerController2D controller =
                player.gameObject.AddComponent<ObjectPlayerController2D>();
            controller.Configure(player, player.Body, player.BodyCollider, probe.transform);

            ObjectActor2D enemy = CreateActor(
                "ObjectMakerLargeColliderAttackTarget", enemyDefinition, new Vector2(2.47f, 0f));
            player.Body.bodyType = RigidbodyType2D.Kinematic;
            enemy.Body.bodyType = RigidbodyType2D.Kinematic;
            enemy.Body.gravityScale = 0f;
            enemy.Body.freezeRotation = true;
            ObjectMonsterBrain2D brain = enemy.GetComponent<ObjectMonsterBrain2D>();
            if (brain != null)
                brain.enabled = false;
            enemy.Data.attack.contactDamage = 0;
            enemy.Data.attack.attackDamage = 0;
            Physics2D.SyncTransforms();

            Assert.Greater(
                Mathf.Abs(enemy.Body.position.x - player.Body.position.x),
                1.7f,
                "큰 Player Collider가 실제로 두 오브젝트를 기존 중심 기준 공격 범위 밖까지 밀어내지 않았습니다.");

            player.SetFacing(1f);
            Assert.IsTrue(controller.TryStartAttack(0));
            float timeout = Time.realtimeSinceStartup + 0.5f;
            while (enemy != null && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.IsTrue(enemy == null,
                "큰 Player Collider의 앞면부터 공격 범위를 계산하지 않아 체력 1인 대상이 제거되지 않았습니다.");
        }

        [UnityTest]
        public IEnumerator PlayerJumpUsesLowestAttachedColliderForGrounding()
        {
            CreateGround(new Vector2(0f, -2.67f), new Vector2(20f, 1f));
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.player.jumpForce = 5f;
                data.player.groundCheckRadius = 0.15f;
            });
            ObjectActor2D player = CreateActor(
                "ObjectMakerMultiColliderPlayer", playerDefinition, Vector2.zero);
            player.Body.bodyType = RigidbodyType2D.Dynamic;
            player.Body.gravityScale = 1f;
            ((BoxCollider2D)player.BodyCollider).size = new Vector2(1f, 1.8f);
            BoxCollider2D addedSceneCollider = player.gameObject.AddComponent<BoxCollider2D>();
            addedSceneCollider.size = new Vector2(2.47f, 4.34f);

            var probe = new GameObject("GroundProbe");
            probe.transform.SetParent(player.transform, false);
            probe.transform.localPosition = Vector3.down * 0.9f;
            ObjectPlayerController2D controller =
                player.gameObject.AddComponent<ObjectPlayerController2D>();
            controller.Configure(player, player.Body, player.BodyCollider, probe.transform);
            Physics2D.SyncTransforms();

            Assert.IsTrue(controller.RefreshGrounded(),
                "추가 Collider가 실제 발이 됐지만 기존 GroundProbe만 검사해 지면을 놓쳤습니다.");
            Assert.IsTrue(controller.TryJump(),
                "Space로 매핑된 점프가 추가 Collider의 지면 접촉을 인식하지 못했습니다.");
            Assert.Greater(player.Body.linearVelocity.y, 0f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerAndEnemyMovementFlipAllVisibleSpriteRenderers()
        {
            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player, data =>
            {
                data.core.moveSpeed = 3f;
                data.facesRightByDefault = true;
            });
            ObjectActor2D player = CreateActor(
                "ObjectMakerFacingPlayer", playerDefinition, Vector2.zero);
            SpriteRenderer[] playerRenderers = AddGeneratedAndReplacementVisuals(player);
            var playerProbe = new GameObject("GroundProbe");
            playerProbe.transform.SetParent(player.transform, false);
            ObjectPlayerController2D playerController =
                player.gameObject.AddComponent<ObjectPlayerController2D>();
            playerController.Configure(
                player, player.Body, player.BodyCollider, playerProbe.transform);

            playerController.SetMoveInput(-1f);
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(player.IsFacingRight,
                "Player 왼쪽 이동이 방향 상태에 반영되지 않았습니다.");
            Assert.IsTrue(playerRenderers[0].flipX,
                "Player의 생성된 Visual SpriteRenderer가 왼쪽으로 반전되지 않았습니다.");
            Assert.IsTrue(playerRenderers[1].flipX,
                "Player Prefab에 추가한 실제 표시용 SpriteRenderer가 왼쪽으로 반전되지 않았습니다.");

            playerController.SetMoveInput(1f);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(player.IsFacingRight,
                "Player 오른쪽 이동이 방향 상태에 반영되지 않았습니다.");
            Assert.IsFalse(playerRenderers[0].flipX,
                "Player의 생성된 Visual SpriteRenderer가 오른쪽 방향으로 복구되지 않았습니다.");
            Assert.IsFalse(playerRenderers[1].flipX,
                "Player Prefab에 추가한 실제 표시용 SpriteRenderer가 오른쪽 방향으로 복구되지 않았습니다.");
            playerController.SetMoveInput(0f);

            ObjectDefinitionSO enemyDefinition = CreateDefinition(ObjectKind.Monster, data =>
            {
                data.core.moveSpeed = 1f;
                data.facesRightByDefault = true;
                data.movement.groundWalker = false;
                data.attack.attackRange = 0.1f;
                data.attack.contactDamage = 0;
                data.detection.frontDistance = 10f;
                data.detection.rearDistance = 10f;
            });
            ObjectActor2D enemy = CreateActor(
                "ObjectMakerFacingEnemy", enemyDefinition, new Vector2(3f, 0f), true);
            SpriteRenderer[] enemyRenderers = AddGeneratedAndReplacementVisuals(enemy);
            ObjectMonsterBrain2D enemyBrain = enemy.GetComponent<ObjectMonsterBrain2D>();
            enemyBrain.ForceTarget(player);

            float timeout = Time.realtimeSinceStartup + 1f;
            while (enemy.Body.linearVelocity.x >= -0.01f &&
                   Time.realtimeSinceStartup < timeout)
                yield return new WaitForFixedUpdate();

            Assert.Less(enemy.Body.linearVelocity.x, -0.01f,
                "Enemy가 왼쪽의 Player를 향해 이동하지 않았습니다.");
            Assert.IsFalse(enemy.IsFacingRight,
                "Enemy 왼쪽 이동이 방향 상태에 반영되지 않았습니다.");
            Assert.IsTrue(enemyRenderers[0].flipX,
                "Enemy의 생성된 Visual SpriteRenderer가 왼쪽으로 반전되지 않았습니다.");
            Assert.IsTrue(enemyRenderers[1].flipX,
                "Enemy Prefab에 추가한 실제 표시용 SpriteRenderer가 왼쪽으로 반전되지 않았습니다.");
        }

        private ObjectDefinitionSO CreateDefinition(
            ObjectKind kind,
            Action<ObjectDefinitionData> configure = null)
        {
            ObjectDefinitionSO definition = ScriptableObject.CreateInstance<ObjectDefinitionSO>();
            ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
            data.kind = kind;
            configure?.Invoke(data);
            definition.ReplaceData(data);
            cleanup.Add(definition);
            return definition;
        }

        private ObjectActor2D CreateActor(
            string name,
            ObjectDefinitionSO definition,
            Vector2 position,
            bool addMonsterBrain = false)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = position;
            var body = gameObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            if (definition.Data.kind == ObjectKind.Player)
                body.bodyType = RigidbodyType2D.Kinematic;
            var collider = gameObject.AddComponent<BoxCollider2D>();
            var actor = gameObject.AddComponent<ObjectActor2D>();
            actor.Configure(definition, body, collider, gameObject.transform, null, null);
            actor.RefreshFromDefinition(true);

            if (addMonsterBrain)
            {
                var probe = new GameObject("GroundProbe");
                probe.transform.SetParent(gameObject.transform, false);
                probe.transform.localPosition = Vector3.down * 0.5f;
                ObjectFxController2D effects = gameObject.AddComponent<ObjectFxController2D>();
                effects.Configure(actor, null);
                var brain = gameObject.AddComponent<ObjectMonsterBrain2D>();
                brain.Configure(actor, body, collider, probe.transform);
            }

            cleanup.Add(gameObject);
            return actor;
        }

        private static SpriteRenderer[] AddGeneratedAndReplacementVisuals(ObjectActor2D actor)
        {
            var generatedVisual = new GameObject("Visual");
            generatedVisual.transform.SetParent(actor.transform, false);
            SpriteRenderer generatedRenderer =
                generatedVisual.AddComponent<SpriteRenderer>();

            var replacementVisual = new GameObject("Popi");
            replacementVisual.transform.SetParent(actor.transform, false);
            SpriteRenderer replacementRenderer =
                replacementVisual.AddComponent<SpriteRenderer>();

            actor.Configure(
                actor.Definition,
                actor.Body,
                actor.BodyCollider,
                generatedVisual.transform,
                generatedRenderer,
                null);
            actor.RefreshFromDefinition(false);
            return new[] { generatedRenderer, replacementRenderer };
        }

        private void CreateGround(Vector2 position, Vector2 size)
        {
            var ground = new GameObject("ObjectMakerTestGround");
            ground.transform.position = position;
            var collider = ground.AddComponent<BoxCollider2D>();
            collider.size = size;
            cleanup.Add(ground);
        }
    }

    public sealed class FrameWorkFeatureGatePlayModeTests
    {
        [UnityTest]
        public IEnumerator AutomaticRunnersAndComponentsRespectCurrentSettings()
        {
            bool presentationEnabled =
                FrameWorkFeatureGate.IsEnabled(FrameWorkModule.Presentation);
            bool objectMakerEnabled =
                FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker);

            var actorObject = new GameObject("FrameWorkGateActor");
            actorObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
            actorObject.AddComponent<BoxCollider2D>();
            ObjectActor2D actor = actorObject.AddComponent<ObjectActor2D>();

            yield return null;

            Assert.AreEqual(presentationEnabled,
                GameObject.Find("PresentationAutoRunner") != null);
            Assert.AreEqual(objectMakerEnabled,
                GameObject.Find("2DObjectMakerAutoRunner") != null);
            Assert.AreEqual(objectMakerEnabled, actor.enabled);

            if (!presentationEnabled)
                Assert.IsFalse(SceneManager.GetSceneByName("Presentation").isLoaded);

            UnityEngine.Object.DestroyImmediate(actorObject);
        }
    }
}
