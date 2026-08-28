using System.Collections;
using System.Collections.Generic;
using JYW.FrameWork;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class ObjectMakerKeyboardPlayModeTests : InputTestFixture
    {
        private readonly List<UnityEngine.Object> cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void CleanupObjects()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                    UnityEngine.Object.DestroyImmediate(cleanup[i]);
            }
            cleanup.Clear();
        }

        [UnityTest]
        public IEnumerator ConfiguredRunAndAttackKeysDriveRuntimeAndDestroyTarget()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
                Assert.Ignore("2DObjectMaker가 Settings에서 꺼져 있어 키 입력 테스트를 생략합니다.");

            ObjectDefinitionSO playerDefinition = CreateDefinition(ObjectKind.Player);
            ObjectDefinitionData playerData = playerDefinition.Data.Clone();
            playerData.core.moveSpeed = 3f;
            playerData.player.moveLeftKey = Key.LeftArrow;
            playerData.player.moveRightKey = Key.RightArrow;
            playerData.player.runKey = Key.LeftShift;
            playerData.player.runSpeedMultiplier = 2f;
            playerData.player.attacks = new List<ObjectPlayerAttack>
            {
                new ObjectPlayerAttack
                {
                    key = Key.A,
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
            playerDefinition.ReplaceData(playerData);

            ObjectDefinitionSO enemyDefinition = CreateDefinition(ObjectKind.Monster);
            ObjectDefinitionData enemyData = enemyDefinition.Data.Clone();
            enemyData.objectId = "SG-001-KeyboardRegression";
            enemyData.core.maxHealth = 1;
            enemyData.core.defense = 0;
            enemyData.hit.blinkSeconds = 0f;
            enemyData.hit.knockback = 0f;
            enemyData.destruction.destroyDelaySeconds = 0f;
            enemyDefinition.ReplaceData(enemyData);

            ObjectActor2D player = CreateActor(
                "ObjectMakerKeyboardPlayer", playerDefinition, Vector2.zero);
            BoxCollider2D addedSceneCollider = player.gameObject.AddComponent<BoxCollider2D>();
            addedSceneCollider.size = new Vector2(3.94f, 2f);
            var probe = new GameObject("GroundProbe");
            probe.transform.SetParent(player.transform, false);
            ObjectPlayerController2D controller =
                player.gameObject.AddComponent<ObjectPlayerController2D>();
            controller.Configure(player, player.Body, player.BodyCollider, probe.transform);

            ObjectActor2D enemy = CreateActor(
                "ObjectMakerKeyboardTarget", enemyDefinition, new Vector2(10f, 0f));

            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            yield return null;

            Press(keyboard.rightArrowKey);
            yield return null;
            Assert.AreEqual(3f, player.Body.linearVelocity.x, 0.15f,
                "설정한 오른쪽 이동 키가 걷기 속도를 적용하지 않았습니다.");

            Press(keyboard.leftShiftKey);
            yield return null;
            Assert.IsTrue(controller.IsRunning,
                "설정한 Left Shift 달리기 키가 Controller에 들어오지 않았습니다.");
            Assert.AreEqual(6f, player.Body.linearVelocity.x, 0.15f,
                "설정한 달리기 속도 배율이 적용되지 않았습니다.");

            Release(keyboard.leftShiftKey);
            yield return null;
            Assert.IsFalse(controller.IsRunning);
            Assert.AreEqual(3f, player.Body.linearVelocity.x, 0.15f,
                "달리기 키를 놓은 뒤 걷기 속도로 돌아오지 않았습니다.");
            Release(keyboard.rightArrowKey);
            yield return null;

            player.Body.bodyType = RigidbodyType2D.Kinematic;
            enemy.Body.bodyType = RigidbodyType2D.Kinematic;
            player.Body.position = Vector2.zero;
            enemy.Body.position = new Vector2(2.47f, 0f);
            player.Body.linearVelocity = Vector2.zero;
            enemy.Body.linearVelocity = Vector2.zero;
            player.SetFacing(1f);
            Physics2D.SyncTransforms();
            Assert.Greater(
                Mathf.Abs(enemy.Body.position.x - player.Body.position.x),
                1.7f,
                "큰 Player Collider 재현 조건이 만들어지지 않았습니다.");

            Press(keyboard.aKey);
            yield return null;
            Assert.IsTrue(controller.IsAttacking,
                "실제 A 키 입력이 Player 공격 시작까지 도달하지 않았습니다.");
            Release(keyboard.aKey);
            yield return null;

            float timeout = Time.realtimeSinceStartup + 0.5f;
            while (enemy != null && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.IsTrue(enemy == null,
                "실제 A 키 공격으로 체력 1인 대상이 사망·제거되지 않았습니다.");
        }

        private ObjectDefinitionSO CreateDefinition(ObjectKind kind)
        {
            ObjectDefinitionSO definition = ScriptableObject.CreateInstance<ObjectDefinitionSO>();
            ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
            data.kind = kind;
            definition.ReplaceData(data);
            cleanup.Add(definition);
            return definition;
        }

        private ObjectActor2D CreateActor(
            string name,
            ObjectDefinitionSO definition,
            Vector2 position)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = position;
            Rigidbody2D body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.freezeRotation = true;
            BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();
            ObjectActor2D actor = gameObject.AddComponent<ObjectActor2D>();
            actor.Configure(definition, body, collider, gameObject.transform, null, null);
            actor.RefreshFromDefinition(true);
            cleanup.Add(gameObject);
            return actor;
        }
    }
}
