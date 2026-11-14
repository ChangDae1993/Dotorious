using System;
using System.Collections;
using System.Linq;
using JYW.Game.EventPlay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Dotorious.StoryIntro.Tests
{
    public sealed class NativeStoryboardContactTests : InputTestFixture
    {
        private const string Prefix = "Dotorious.Intro.";
        private const float Dt = .02f;
        private Keyboard keyboard;
        private PhysicsScene2D physics;
        private AuthorablePlayerInput2D actor;
        private Rigidbody2D body;
        private EventPlayManager presentation;
#if UNITY_EDITOR
        private Scene priorScene, loadedStoryboard;
#endif

        [UnityTest]
        public IEnumerator AllTenNativeEventsPlayFromContactAndActualInput()
        {
#if UNITY_EDITOR
            const string path = "Assets/StoryIntro/Scenes/Dotorious_SB002_SB006.unity";
            priorScene = SceneManager.GetActiveScene();
            var request = EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            Assert.That(request, Is.Not.Null, "Build the native storyboard before running this test.");
            yield return request;
            Scene scene = loadedStoryboard = SceneManager.GetSceneByPath(path);
            physics = scene.GetPhysicsScene2D();
            var roots = scene.GetRootGameObjects();
            actor = roots.SelectMany(root => root.GetComponentsInChildren<AuthorablePlayerInput2D>()).Single();
            body = actor.GetComponent<Rigidbody2D>();
            var button = roots.SelectMany(root => root.GetComponentsInChildren<EventBooleanButton>()).Single();
            var regions = roots.SelectMany(root => root.GetComponentsInChildren<EventConditionRegion2D>()).ToArray();
            var firstRegion = regions.Single(region => region.Event.name == "SB002_HomeEntry").GetComponent<BoxCollider2D>();
            GameObject Named(string name) => roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Single(item => item.name == name).gameObject;
            actor.enabled = false; // Manually clock actual movement and the isolated scene's 2D physics.
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            yield return null;

            {
                yield return WaitFor(() => (presentation = EventPlayManager.Instance) != null, "Presentation manager");
                Assert.That(actor.MoveAllowed || actor.DashAllowed || actor.InteractAllowed || actor.AttackAllowed, Is.False);
                var camera = Named("IntroCamera").transform;
                Assert.That(camera.position.y, Is.EqualTo(13f).Within(.02f));

                firstRegion.enabled = false;
                button.GetComponent<Button>().onClick.Invoke();
                for (int i = 0; i < 8; i++) { Step(); yield return null; }
                Assert.That(Flag("NewGame"), Is.True);
                Assert.That(camera.position.y, Is.EqualTo(13f).Within(.02f), "New Game alone may not start the intro.");
                firstRegion.enabled = true;
                yield return WaitFor(() => actor.MoveAllowed, "Entry cinematic releases Walk", timeout: 15f);
                Assert.That(camera.position.y, Is.EqualTo(2.7f).Within(.08f));
                Assert.That(Named("IntroDotVisual").activeInHierarchy, Is.True);
                Assert.That(actor.DashAllowed || actor.InteractAllowed || actor.AttackAllowed, Is.False);
                Assert.That(Flag("TracksReady"), Is.False);
                actor.RequestDash();
                yield return TravelUntil(() => Flag("TracksSeen"), 1, "First tracks");
                Assert.That(actor.SuccessfulDashCount, Is.Zero);
                Assert.That(Flag("ActualMove"), Is.True);
                Assert.That(Named("IntroFirstTrackContrast").activeInHierarchy, Is.True);

                yield return TravelUntil(() => Named("IntroResident").activeInHierarchy, 1, "Resident arrives");
                Assert.That(actor.MoveAllowed || actor.InteractAllowed, Is.False);
                yield return WaitFor(() => Flag("ResidentReady"), "Resident finish arrival");
                Assert.That(actor.MoveAllowed && actor.InteractAllowed, Is.True);
                yield return TravelUntil(() => body.position.x >= 25.05f, 1, "Approach resident");
                Assert.That(Named("IntroResidentGate").activeInHierarchy, Is.True);
                Assert.That(PortraitLines().Length, Is.Zero);
                yield return WaitFor(() => !presentation.IsEventRunning, "Resident presentation releases event runner");
                Assert.That(Flag("CanInteract"), Is.True);
                var talk = regions.Single(region => region.Event.name == "SB003_ResidentTalk");
                Assert.That(talk.Actor.IsTouching(talk.GetComponent<BoxCollider2D>()), Is.True,
                    "Player needs live resident-talk collider contact before E.");
                yield return Tap(Key.E);
                yield return Dialogue("도트……! 마을에 이상한 것들이 나타났어!",
                    "도망치느라 확인하지 못했어.", 13);
                yield return WaitFor(() => actor.DashAllowed, "Dash unlocked after resident conversation");
                Assert.That(actor.MoveAllowed, Is.True);
                Assert.That(actor.AttackAllowed || actor.InteractAllowed, Is.False);
                Assert.That(Named("IntroResidentGate").activeInHierarchy, Is.False);
                Assert.That(Named("IntroDashPrompt").activeInHierarchy, Is.True);
                Assert.That(Flag("DashComplete"), Is.False);

                yield return TravelUntil(() => body.position.x <= -1.92f, -1, "Walk left limit");
                actor.SetHorizontal(-1);
                for (int i = 0; i < 5; i++) { Step(); yield return null; }
                actor.RequestDash();
                for (int i = 0; i < 5; i++) { Step(); yield return null; }
                Assert.That(actor.SuccessfulDashCount, Is.Zero);
                Assert.That(Flag("ActualDash"), Is.False);
                Assert.That(Named("IntroDashPrompt").activeInHierarchy, Is.True);

                // Walk through the forest without dashing to prove there is no artificial barrier.
                yield return TravelUntil(() => body.position.x >= 38f, 1, "Walk past unapplied forest event");
                Assert.That(Flag("ForestSeen"), Is.False);
                actor.RequestDash();
                yield return WaitFor(() => Flag("DashComplete"), "Actually successful dash");
                Assert.That(actor.SuccessfulDashCount, Is.EqualTo(1));
                Assert.That(Named("IntroDashPrompt").activeInHierarchy, Is.False);
                Assert.That(Flag("ForestSeen"), Is.False, "Early forest overlap may not replay after the dash.");
                yield return TravelUntil(() => Flag("ForestSeen"), -1, "Re-enter the physical forest start region");

                yield return TravelUntil(() => Flag("OutskirtsSeen"), 1, "Outskirts");
                yield return TravelUntil(() => Flag("ApproachSeen"), 1, "Well approach");
                Assert.That(Named("IntroKeeper").activeInHierarchy, Is.False);
                Assert.That(Named("IntroMachine").activeInHierarchy, Is.False);
                yield return TravelUntil(() => Named("IntroKeeper").activeInHierarchy, 1, "Wellkeeper discovery");
                Assert.That(actor.MoveAllowed || actor.DashAllowed || actor.InteractAllowed, Is.False);
                float frozenX = body.position.x;
                Vector3 frozenCamera = camera.position;
                for (int i = 0; i < 5; i++)
                {
                    actor.SetHorizontal(1);
                    Step(); yield return null;
                }
                Assert.That(body.position.x, Is.EqualTo(frozenX).Within(.001f));
                Assert.That(camera.position, Is.EqualTo(frozenCamera));
                actor.SetHorizontal(0);
                yield return WaitFor(() => Flag("KeeperReady"), "Discovery releases walk and interact");

                yield return Tap(Key.E);
                Assert.That(PortraitLines().Length, Is.Zero, "Interact away from the keeper must not run dialogue.");
                yield return TravelUntil(() => body.position.x >= 74.5f, 1, "Approach the wellkeeper by walking");
                yield return Tap(Key.E);
                yield return Dialogue("우물지기!", "그런 말 하지 말고 정신 차려.", 13);
                Assert.That(Named("IntroReturnObjective").activeInHierarchy, Is.False);
                yield return WaitFor(() => Named("IntroLeftImpact").activeInHierarchy, "Leftward home impact");
                Assert.That(Named("IntroReturnObjective").activeInHierarchy, Is.False);
                yield return WaitFor(() => Flag("ReturnReady"), "Return home objective");
                Assert.That(Named("IntroReturnObjective").activeInHierarchy, Is.True);
                Assert.That(Named("IntroChangedTrail").activeInHierarchy, Is.True);
                Assert.That(Named("IntroOutboundTrail").activeInHierarchy, Is.False);
                Assert.That(Named("IntroNormalHouse").activeSelf, Is.True);
                Assert.That(Named("IntroMachine").activeSelf, Is.False);
                Assert.That(actor.MoveAllowed && actor.DashAllowed && !actor.InteractAllowed && !actor.AttackAllowed, Is.True);
                yield return Tap(Key.E);
                Assert.That(PortraitLines().Length, Is.Zero, "The keeper cannot be replayed on return.");

                int returnFrames = 0;
                float returnDeadline = Time.realtimeSinceStartup + 20f;
                actor.SetHorizontal(-1);
                while (!Named("IntroDamagedHouse").activeSelf && Time.realtimeSinceStartup < returnDeadline)
                {
                    Step(); returnFrames++; yield return null;
                }
                actor.SetHorizontal(0);
                Assert.That(Named("IntroDamagedHouse").activeSelf, Is.True);
                Assert.That(returnFrames * Dt, Is.InRange(12f, 15f));
                Assert.That(Named("IntroHomeMachineTracks").activeSelf, Is.True);
                Assert.That(Named("IntroMachine").activeSelf, Is.False, "Home and tracks reveal before SG-001.");
                Assert.That(Named("IntroReturnObjective").activeSelf, Is.False);
                Assert.That(actor.MoveAllowed || actor.DashAllowed || actor.InteractAllowed || actor.AttackAllowed, Is.False);
                yield return WaitFor(() => Flag("SB007Ready"), "SG-001 turns and hands off");
                Assert.That(Named("IntroMachine").activeSelf, Is.True);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Named("IntroMachine").transform.localEulerAngles.y, 180)), Is.LessThan(1f));
            }
            yield return null;
#else
            Assert.Ignore("The standalone storyboard is editor-opened and not in Build Settings.");
            yield break;
#endif
        }

        [UnityTearDown]
        public IEnumerator RestoreSceneAndInput()
        {
#if UNITY_EDITOR
            // A failed assertion may leave this fixture's portrait waiting for Enter.
            // Cancel via the manager's real OnDisable cleanup before restoring the runner.
            if (loadedStoryboard.IsValid() && loadedStoryboard.isLoaded && presentation != null &&
                presentation.isActiveAndEnabled && presentation.IsEventRunning)
            {
                presentation.enabled = false;
                presentation.enabled = true;
            }
            if (priorScene.IsValid() && priorScene.isLoaded && SceneManager.GetActiveScene() != priorScene)
                SceneManager.SetActiveScene(priorScene);
            if (loadedStoryboard.IsValid() && loadedStoryboard.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(loadedStoryboard);
                if (unload != null) yield return unload;
            }
            loadedStoryboard = default;
#endif
            yield return null;
        }

        private void Step()
        {
            actor.SimulateControls(Dt);
            physics.Simulate(Dt);
        }

        private bool Flag(string name) => presentation.GetGlobal<bool>(Prefix + name);

        private IEnumerator WaitFor(Func<bool> condition, string description, float timeout = 8f)
        {
            float expires = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < expires)
            {
                Step();
                yield return null;
            }
            Assert.That(condition(), Is.True, description + " did not occur.");
        }

        private IEnumerator TravelUntil(Func<bool> condition, float direction, string description)
        {
            actor.SetHorizontal(direction);
            yield return WaitFor(condition, description);
            actor.SetHorizontal(0);
        }

        private IEnumerator Tap(Key key)
        {
            // Same package fixture/input-loop path used by the ObjectMaker PlayMode tests.
            var control = keyboard[key];
            Press(control);
            Step(); yield return null;
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(control.wasPressedThisFrame, Is.True,
                "InputTestFixture did not expose the real Input System key edge for " + key);
            Release(control);
            Step(); yield return null;
            Assert.That(control.isPressed, Is.False);
            Step(); yield return null;
        }

        private static string[] PortraitLines()
        {
            var canvas = UnityEngine.Object.FindFirstObjectByType<PortraitSpeechCanvas>();
            return canvas == null ? Array.Empty<string>() : canvas.GetComponentsInChildren<Text>()
                .Select(text => text.text).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        }

        private IEnumerator Dialogue(string first, string last, int count)
        {
            yield return WaitFor(() => PortraitLines().Contains(first), "First native dialogue line");
            for (int i = 0; i < count; i++)
            {
                string before = string.Join("\n", PortraitLines());
                Assert.That(before, Is.Not.Empty);
                Assert.That(actor.MoveAllowed || actor.DashAllowed || actor.InteractAllowed || actor.AttackAllowed, Is.False);
                if (i == count - 1) Assert.That(PortraitLines(), Does.Contain(last));
                yield return Tap(Key.Enter);
                yield return WaitFor(() => string.Join("\n", PortraitLines()) != before, "Enter advances native dialogue");
            }
            Assert.That(PortraitLines().Length, Is.Zero);
        }
    }
}




