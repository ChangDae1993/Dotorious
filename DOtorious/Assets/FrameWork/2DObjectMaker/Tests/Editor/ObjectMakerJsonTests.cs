using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor.Tests
{
    public sealed class ObjectMakerJsonTests
    {
        const string Folder="Assets/__ObjectMakerJsonTestAssets";
        [SetUp] public void SetUp(){AssetDatabase.CreateFolder("Assets","__ObjectMakerJsonTestAssets");}
        [TearDown] public void TearDown(){AssetDatabase.DeleteAsset(Folder);}
        [Test] public void AllValuesAndPersistentReferencesRoundTrip()
        {
            var controller=UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Folder+"/Controller.controller");
            var clip=AudioClip.Create("Sound",256,1,22050,false);AssetDatabase.CreateAsset(clip,Folder+"/Sound.asset");
            var data=new ObjectDefinitionData();data.displayName="한글 JSON";data.animatorController=controller;
            data.ai.useLegacyTuning=false;data.ai.rules[0].actionSound.audioClip=clip;
            data.player.jumpSound.audioClip=clip;data.player.attacks[0].sound.audioClip=clip;
            data.core.maxHealth=27;data.player.attacks[0].comboSteps=3;data.sceneSpawns.Add(new ObjectSceneSpawnRule{sceneName="Main",count=3});
            data.Sanitize();
            string json=ObjectMakerJson.Export(data);
            Assert.That(json,Does.Contain("\"guid\""));Assert.That(json,Does.Contain("\"localId\""));
            var restored=ObjectMakerJson.Import(json,out string[] warnings);
            Assert.That(warnings,Is.Empty);
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(JsonUtility.ToJson(data)));
            Assert.AreSame(controller,data.animatorController,"Export must not mutate the draft.");
            Assert.IsTrue(clip==restored.player.attacks[0].sound.audioClip);
        }
        [TestCase("")] [TestCase("{")] [TestCase("{}")] [TestCase("{\"format\":\"2DObjectMaker\",\"version\":9,\"data\":{}}")]
        public void InvalidInputIsRejected(string json)
        {Assert.Throws<FormatException>(()=>ObjectMakerJson.Import(json,out _));}
        [Test] public void MissingReferenceGivesWarningWithoutUsingSessionInstanceIds()
        {
            var controller=UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Folder+"/Gone.controller");
            var data=new ObjectDefinitionData{animatorController=controller};
            string json=ObjectMakerJson.Export(data);AssetDatabase.DeleteAsset(Folder+"/Gone.controller");
            var restored=ObjectMakerJson.Import(json,out string[] warnings);
            Assert.IsNull(restored.animatorController);Assert.That(warnings,Has.Length.EqualTo(1));
        }
        [Test] public void Utf8FileRoundTripPreservesKoreanAndLineBreaks()
        {
            string path=Path.Combine(Folder,"RoundTrip.json");
            var data=new ObjectDefinitionData{displayName="눈 덮인 숲\n문지기"};
            ObjectMakerJson.ExportFile(path,data);
            var imported=ObjectMakerJson.ImportFile(path,out var warnings);
            Assert.That(imported.displayName,Is.EqualTo(data.displayName));Assert.That(warnings,Is.Empty);
        }
        [Test] public void SkillPatternAndEffectPrefabRoundTrip()
        {
            var go=new GameObject("SkillVisual");
            var prefab=PrefabUtility.SaveAsPrefabAsset(go,Folder+"/SkillVisual.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            var data=new ObjectDefinitionData();
            data.player.attacks[0].pattern=ObjectPlayerAttackPattern.Projectile;
            data.player.attacks[0].effectPrefab=prefab;
            data.player.attacks[0].projectileSpeed=12.5f;
            data.player.attacks[0].projectileRadius=.23f;
            var imported=ObjectMakerJson.Import(ObjectMakerJson.Export(data),out var warnings);
            Assert.That(warnings,Is.Empty);
            Assert.AreEqual(ObjectPlayerAttackPattern.Projectile,imported.player.attacks[0].pattern);
            Assert.AreSame(prefab,imported.player.attacks[0].effectPrefab);
            Assert.AreEqual(12.5f,imported.player.attacks[0].projectileSpeed);
            Assert.AreEqual(.23f,imported.player.attacks[0].projectileRadius);
            Assert.AreSame(prefab,data.player.attacks[0].effectPrefab);
        }
    }
}
