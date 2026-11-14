using System.Collections.Generic;
using System.Linq;
using JYW.Game.EventPlay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dotorious.StoryIntro.Tests
{
    public sealed class NativeStoryboardAuthoringTests
    {
        private const string Scene = "Assets/StoryIntro/Scenes/Dotorious_SB002_SB006.unity";
        private const string Events = "Assets/StoryIntro/Resources/Presentation/Dotorious_SB002_SB006";

        [Test]
        public void EveryStoryboardAssetHasItsOwnRealSceneCollisionConditionAndNativeSteps()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene), Is.Not.Null,
                "Build the native storyboard before testing its saved asset.");
            var scene = EditorSceneManager.OpenPreviewScene(Scene);
            try
            {
                string[] names =
                {
                    "SB002_HomeEntry", "SB003_Tracks", "SB003_ResidentArrival", "SB003_ResidentTalk",
                    "SB004_DamagedForest", "SB004_Outskirts", "SB004_WellApproach", "SB004_Discovery",
                    "SB005_KeeperTalk", "SB006_HomeReturn"
                };
                var authored = AssetDatabase.FindAssets("t:EventSO", new[] { Events });
                Assert.That(authored, Has.Length.EqualTo(names.Length));
                var regions = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EventConditionRegion2D>(true)).ToArray();
                Assert.That(regions, Has.Length.EqualTo(names.Length));
                Assert.That(regions.Select(region => region.Event.name), Is.EquivalentTo(names));
                Assert.That(regions.Select(region => region.Actor).Distinct().Count(), Is.EqualTo(1));

                foreach (var region in regions)
                {
                    Assert.That(region.GetComponent<BoxCollider2D>().isTrigger, Is.True);
                    Assert.That(region.Actor.attachedRigidbody, Is.Not.Null);
                    Assert.That(AssetDatabase.GetAssetPath(region.Event), Does.StartWith(Events));
                    var condition = region.Event.Conditions[region.ConditionIndex];
                    Assert.That(region.Event.UseCondition, Is.True);
                    Assert.That(condition.Checks.HasFlag(EventSO.ConditionChecks.CollisionAB), Is.True);
                    Assert.That(condition.CollisionMode, Is.EqualTo(EventSO.CollisionCheckMode.CurrentContact2D));
                    Assert.That(condition.CollisionObjectA, Does.EndWith("/IntroDot"));
                    Assert.That(condition.CollisionObjectB, Does.EndWith("/" + region.name));
                    Assert.That(condition.Conditions.Length, Is.GreaterThan(0));
                    bool needsInteract = region.Event.name.EndsWith("Talk");
                    Assert.That(condition.Checks.HasFlag(EventSO.ConditionChecks.InputKey), Is.EqualTo(needsInteract));
                    if (needsInteract) Assert.That(condition.InputKeyCode, Is.EqualTo(KeyCode.E));

                    Assert.That(region.Event.ConditionSteps.Length, Is.EqualTo(region.Event.Conditions.Length + 1));
                    var fallback = region.Event.ConditionSteps.Last();
                    Assert.That(typeof(EventSO.EventStepFlags).GetFields()
                        .Where(field => field.FieldType == typeof(bool))
                        .All(field => !(bool)field.GetValue(fallback.Flags)), Is.True,
                        "The required editor Else slot must remain inert.");
                    Assert.That(string.IsNullOrEmpty(fallback.Action?.ActionName), Is.True);

                    var phases = Walk(region.Event.ConditionSteps.Take(region.Event.Conditions.Length)).ToArray();
                    Assert.That(phases, Has.Length.GreaterThan(1));
                    Assert.That(phases.All(step => !step.Flags.IsAction && string.IsNullOrEmpty(step.Action?.ActionName)), Is.True,
                        region.Event.name + " must not dispatch a custom runtime action.");
                    string[] boolWaits = phases.Where(step => step.Flags.IsWaitUntilCondition)
                        .SelectMany(step => step.WaitUntilCondition.Condition.Conditions)
                        .Select(check => check.GlobalNames).ToArray();
                    if (region.Event.name == "SB002_HomeEntry")
                        Assert.That(boolWaits, Does.Contain("Dotorious.Intro.ActualMove"));
                    if (region.Event.name == "SB003_ResidentTalk")
                        Assert.That(boolWaits, Does.Contain("Dotorious.Intro.ActualDash"));
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static IEnumerable<EventSO.EventStep> Walk(IEnumerable<EventSO.EventStep> steps)
        {
            foreach (var step in steps)
            {
                yield return step;
                if (step.Flags.IsEventExe)
                    foreach (var nested in Walk(step.EventExe.ConditionSteps)) yield return nested;
            }
        }
    }
}


