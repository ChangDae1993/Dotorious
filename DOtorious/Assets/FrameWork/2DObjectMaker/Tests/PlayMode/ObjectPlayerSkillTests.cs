using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace JYW.Game.ObjectMaker.Tests
{
    public sealed class ObjectPlayerSkillTests : InputTestFixture
    {
        readonly List<Object> cleanup = new List<Object>();
        Keyboard keyboard;
        ObjectActor2D player;
        ObjectPlayerController2D controller;

        [SetUp] public void CreateWorld()
        {
            Time.timeScale = 1;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            var d = ObjectDefinitionData.CreateSG001(); d.kind = ObjectKind.Player;
            d.player.moveLeftKey=Key.LeftArrow;d.player.moveRightKey=Key.RightArrow;d.player.jumpKey=Key.C;
            d.player.attacks=new List<ObjectPlayerAttack>();
            var keys=new[]{Key.Z,Key.X,Key.A,Key.S,Key.D};
            var patterns=new[]{ObjectPlayerAttackPattern.Melee,ObjectPlayerAttackPattern.Melee,ObjectPlayerAttackPattern.GrowingThrust,ObjectPlayerAttackPattern.Projectile,ObjectPlayerAttackPattern.Dash};
            for(int i=0;i<5;i++)d.player.attacks.Add(new ObjectPlayerAttack{key=keys[i],pattern=patterns[i],damage=2,inputDelaySeconds=.04f,activeSeconds=.2f,recoverySeconds=.1f,cooldownSeconds=.2f,range=i==2?5:1.2f,hitboxHeight=1f,knockback=0,targetInvulnerabilitySeconds=0,projectileSpeed=15,projectileLifetime=1,dashSpeed=15});
            player=Actor(d,Vector2.zero);
            var probe=new GameObject("GroundProbe");probe.transform.SetParent(player.transform);probe.transform.localPosition=Vector3.down*.5f;
            controller=player.gameObject.AddComponent<ObjectPlayerController2D>();controller.Configure(player,player.Body,player.BodyCollider,probe.transform);
        }
        [TearDown] public void Cleanup()
        {
            foreach(var projectile in Object.FindObjectsByType<ObjectPlayerProjectile2D>(FindObjectsSortMode.None))Object.DestroyImmediate(projectile.gameObject);
            for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i]!=null)Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();Time.timeScale=1;
        }
        ObjectActor2D Actor(ObjectDefinitionData data,Vector2 position)
        {
            var definition=ScriptableObject.CreateInstance<ObjectDefinitionSO>();definition.ReplaceData(data);cleanup.Add(definition);
            var go=new GameObject("SkillTest_"+data.kind);go.transform.position=position;cleanup.Add(go);
            var body=go.AddComponent<Rigidbody2D>();body.gravityScale=0;body.bodyType=RigidbodyType2D.Kinematic;
            var collider=go.AddComponent<BoxCollider2D>();
            var actor=go.AddComponent<ObjectActor2D>();actor.Configure(definition,body,collider,go.transform,null,null);actor.RefreshFromDefinition(true);
            return actor;
        }
        ObjectActor2D Enemy(float x,int hp=20)
        {
            var data=ObjectDefinitionData.CreateSG001();data.kind=ObjectKind.Monster;data.core.maxHealth=hp;data.core.defense=0;data.hit.hitStopSeconds=0;data.hit.blinkSeconds=0;
            return Actor(data,new Vector2(x,0));
        }
        [UnityTest] public IEnumerator AllFiveMappedKeysDealExactlyOneHit()
        {
            var controls=new[]{keyboard.zKey,keyboard.xKey,keyboard.aKey,keyboard.sKey,keyboard.dKey};
            for(int i=0;i<5;i++)
            {
                player.Body.position=Vector2.zero;player.Body.linearVelocity=Vector2.zero;player.SetFacing(1);
                var enemy=Enemy(i==2?4:i==3?3:1.1f);Physics2D.SyncTransforms();yield return null;
                Press(controls[i]);yield return null;Release(controls[i]);
                Assert.AreEqual(i,controller.CurrentAttackIndex,"Mapped key did not choose its attack.");
                yield return new WaitForSeconds(.45f);
                Assert.AreEqual(18,enemy.CurrentHealth,"Damage must occur exactly once for skill "+i);
                Object.DestroyImmediate(enemy.gameObject);
                yield return new WaitForSeconds(.25f);
            }
        }
        [UnityTest] public IEnumerator ThrustPiercesMultipleEnemiesAndFacesLeft()
        {
            player.SetFacing(-1);var a=Enemy(-2);var b=Enemy(-4);
            Physics2D.SyncTransforms();yield return null;
            Assert.IsTrue(controller.TryStartAttack(2));
            yield return new WaitForSeconds(.4f);
            Assert.AreEqual(18,a.CurrentHealth);Assert.AreEqual(18,b.CurrentHealth);Assert.IsFalse(player.IsFacingRight);
        }
        [UnityTest] public IEnumerator ProjectileAndDashStopAtWallAndDoNotHitBehindIt()
        {
            var wall=new GameObject("Wall");cleanup.Add(wall);wall.transform.position=new Vector2(2,0);wall.AddComponent<BoxCollider2D>().size=new Vector2(.3f,4);
            var target=Enemy(4);Physics2D.SyncTransforms();yield return null;
            Assert.IsTrue(controller.TryStartAttack(3));yield return new WaitForSeconds(.6f);
            Assert.AreEqual(20,target.CurrentHealth);
            Assert.IsTrue(controller.TryStartAttack(4));yield return new WaitForSeconds(.4f);
            Assert.Less(player.Body.position.x,1.4f);Assert.AreEqual(20,target.CurrentHealth);
        }
        [UnityTest] public IEnumerator CJumpAndPauseInputGuard()
        {
            var floor=new GameObject("Floor");cleanup.Add(floor);floor.transform.position=new Vector2(0,-1);floor.AddComponent<BoxCollider2D>().size=new Vector2(20,1);
            player.Body.bodyType=RigidbodyType2D.Dynamic;player.Body.gravityScale=1;Physics2D.SyncTransforms();yield return null;
            Press(keyboard.cKey);yield return null;Release(keyboard.cKey);
            Assert.Greater(player.Body.linearVelocity.y,1);
            Time.timeScale=0;Assert.IsFalse(controller.TryJump());Assert.IsFalse(controller.TryStartAttack(0));
            yield return new WaitForSecondsRealtime(.03f);Time.timeScale=1;
        }
        [UnityTest] public IEnumerator LethalMappedLightAttackDestroysEnemyAndDisableCancelsDash()
        {
            var target=Enemy(1.1f,1);Physics2D.SyncTransforms();yield return null;
            Press(keyboard.zKey);yield return null;Release(keyboard.zKey);yield return new WaitForSeconds(.4f);
            Assert.IsTrue(target==null,"Zero-HP enemy remained in the scene.");
            Assert.IsTrue(controller.TryStartAttack(4));yield return new WaitForSeconds(.08f);
            controller.enabled=false;Assert.IsFalse(controller.IsAttacking);
        }
    }
}
