using System;
using System.Linq;
using Dotorious.StoryIntro;
using JYW.Game.EventPlay;
using UnityEngine;
using UnityEditor;
using static JYW.Game.EventPlay.EventSO;

namespace Dotorious.StoryIntro.Editor
{
    // Editor-only recipes for native assets. Each resulting phase and condition is editable in AllinOneEditor.
    internal static class StoryIntroEvents
    {
        internal const string SceneName = "Dotorious_SB002_SB006";
        internal const string EventPath = "Assets/StoryIntro/Resources/Presentation/" + SceneName;
        internal const string Actor = "IntroDot";
        internal const string Camera = "IntroCamera";
        internal const string NewGame = "Dotorious.Intro.NewGame";
        internal const string ActualMove = "Dotorious.Intro.ActualMove";
        internal const string ActualDash = "Dotorious.Intro.ActualDash";
        internal const string TracksReady = "Dotorious.Intro.TracksReady";
        internal const string TracksSeen = "Dotorious.Intro.TracksSeen";
        internal const string ResidentReady = "Dotorious.Intro.ResidentReady";
        internal const string CanInteract = "Dotorious.Intro.CanInteract";
        internal const string DashComplete = "Dotorious.Intro.DashComplete";
        internal const string ForestSeen = "Dotorious.Intro.ForestSeen";
        internal const string OutskirtsSeen = "Dotorious.Intro.OutskirtsSeen";
        internal const string ApproachSeen = "Dotorious.Intro.ApproachSeen";
        internal const string KeeperReady = "Dotorious.Intro.KeeperReady";
        internal const string ReturnReady = "Dotorious.Intro.ReturnReady";
        internal const string Handoff = "Dotorious.Intro.SB007Ready";
        internal const string Resident = "IntroResident";
        internal const string Keeper = "IntroKeeper";
        internal const string Forest = "IntroForestCue";
        internal const string Well = "IntroWellCue";

        internal static void Build(Transform regions, Collider2D actor)
        {
            void Begin(string name, float x, float width, ConditionGroupData condition, params EventStep[] phases)
            {
                var region = new GameObject(name + "_Start");
                region.transform.SetParent(regions, false);
                region.transform.position = new Vector3(x, .7f, 0);
                var box = region.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(width, 2.6f);
                string path = EventPath + "/" + name + ".asset";
                var asset = AssetDatabase.LoadAssetAtPath<EventSO>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<EventSO>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                asset.UseCondition = true;
                asset.Conditions = new[] { condition };
                // AllinOne stores one phase root per condition plus a final Else slot.
                // Current-contact auto-run never executes this deliberately empty fallback.
                asset.ConditionSteps = new[] { Sequence(phases), new EventStep() };
                EventConditionRegion2DPlacement.Assign(asset, box, actor);
                EditorUtility.SetDirty(asset);
            }

            Begin("SB002_HomeEntry", 2.5f, 2f, Flags(NewGame),
                Objects(enable: new[] { "IntroNewGameHighlight" }),
                Pause(.16f), Fade("IntroOtherMenu", .35f), Fade("IntroLogo", .42f),
                Objects(disable: new[] { "IntroTitle" }), MoveCamera(),
                Objects(enable: new[] { "IntroDotVisual" }), Pause(.6f),
                Bubble(Actor, ("물이 다 떨어졌네……", 1.35f), ("우물지기한테 다녀와야겠어.", 1.65f)),
                Objects(enable: new[] { "IntroMovePrompt" }), Follow(true), Controls(true),
                WaitFor(ActualMove), Objects(disable: new[] { "IntroMovePrompt" }), Set(TracksReady));

            Begin("SB003_Tracks", 13f, 1.6f, Flags(TracksReady),
                Objects(enable: new[] { "IntroFirstTrackContrast" }), Set(TracksSeen));

            Begin("SB003_ResidentArrival", 24f, 1.3f, Flags(TracksSeen),
                Controls(false), Objects(enable: new[] { Resident }),
                Move(Resident, new Vector3(26, 0, 0), .48f),
                Objects(enable: new[] { "IntroResidentPrompt" }), Controls(true, interact: true), Set(ResidentReady));

            Begin("SB003_ResidentTalk", 26f, 3.1f, Interact(ResidentReady),
                Controls(false), Objects(disable: new[] { "IntroResidentPrompt" }),
                Portrait(
                    ("주민","도트……! 마을에 이상한 것들이 나타났어!"),
                    ("주민","처음 보는 기계들이 집과 길을 전부 부수고 있어."),
                    ("주민","부탁이야. 우리를 좀 도와줘!"),
                    ("도트","……내가 왜?"), ("주민","뭐……?"),
                    ("도트","너희도 내가 도움이 필요했을 때 아무도 도와주지 않았잖아."),
                    ("도트","그런데 이제 와서 내가 너희를 도와야 해?"),
                    ("주민","지금 그 이야기를 할 때가 아니야!"),
                    ("주민","벌써 우물 근처까지 당했다고!"),
                    ("주민","우물지기도 그쪽에 남아 있었어!"),
                    ("도트","……우물지기가?"),
                    ("주민","나도 그 뒤로는 어떻게 됐는지 몰라."),
                    ("주민","도망치느라 확인하지 못했어.")),
                Objects(enable: new[] { "IntroDashPrompt" }, disable: new[] { "IntroResidentGate" }),
                Controls(true, dash: true), WaitFor(ActualDash),
                Objects(disable: new[] { "IntroDashPrompt", Resident }), Set(DashComplete));

            Begin("SB004_DamagedForest", 33f, 2f, Flags(DashComplete),
                Objects(enable: new[] { Forest }), Set(ForestSeen));

            Begin("SB004_Outskirts", 47f, 2f, Flags(ForestSeen),
                Objects(enable: new[] { "IntroOutskirtsCue" }, disable: new[] { Forest }), Set(OutskirtsSeen));

            Begin("SB004_WellApproach", 62f, 2f, Flags(OutskirtsSeen),
                Objects(enable: new[] { Well }, disable: new[] { "IntroOutskirtsCue" }), Set(ApproachSeen));

            Begin("SB004_Discovery", 69f, 1.35f, Flags(ApproachSeen),
                Controls(false), Follow(false), Objects(enable: new[] { Keeper }, disable: new[] { Well }),
                Pause(.7f), Follow(true), Controls(true, interact: true),
                Objects(enable: new[] { "IntroKeeperPrompt" }), Set(KeeperReady));

            Begin("SB005_KeeperTalk", 75f, 2.5f, Interact(KeeperReady),
                Controls(false), Objects(disable: new[] { "IntroKeeperPrompt" }),
                Portrait(
                    ("도트","우물지기!"), ("도트","정신 차려!"), ("우물지기","도트…… 왔구나."),
                    ("도트","대체 무슨 일이야?"), ("도트","누가 이런 짓을 한 거야?"),
                    ("우물지기","오늘은 물을 길어주지 못하겠구나, 도트야……."),
                    ("도트","지금 물이 문제야?"), ("도트","일단 상처부터 어떻게 해야지."),
                    ("우물지기","내게 받은 건 갚지 않아도 된다."),
                    ("우물지기","언젠가 목마른 누군가에게……"),
                    ("우물지기","한 번만 길어다오."),
                    ("도트","……갑자기 무슨 소리야."), ("도트","그런 말 하지 말고 정신 차려.")),
                Pause(1f), Objects(enable: new[] { "IntroLeftImpact" }), Impact(),
                Objects(disable: new[] { "IntroLeftImpact" }),
                Bubble(Actor, ("……방금 소리.", 1.25f), ("집 쪽인가?", 1.25f)),
                Objects(enable: new[] { "IntroReturnObjective", "IntroChangedTrail" },
                    disable: new[] { "IntroOutboundTrail", "IntroFirstTrackContrast" }),
                Controls(true, dash: true), Set(ReturnReady));

            Begin("SB006_HomeReturn", 2.5f, 1.35f, Flags(ReturnReady),
                Controls(false), Follow(false),
                Objects(enable: new[] { "IntroDamagedHouse", "IntroHomeMachineTracks" },
                    disable: new[] { "IntroReturnObjective", "IntroNormalHouse" }),
                Pause(.85f), Objects(enable: new[] { "IntroMachine" }),
                Pause(.32f), Rotate("IntroMachine"), Pause(.53f), Set(Handoff));

            AssetDatabase.SaveAssets();
        }

        internal static EventStep Sequence(params EventStep[] phases)
        {
            for (int i = 0; i + 1 < phases.Length; i++)
            {
                phases[i].Flags.IsEventExe = true;
                phases[i].EventExe = new EventExeData { UseCondition = false,
                    ConditionSteps = new[] { phases[i + 1] } };
            }
            return phases[0];
        }

        private static ConditionGroupData Flags(params string[] names) => new ConditionGroupData
        {
            Checks = ConditionChecks.CheckValue,
            Conditions = names.Select(name => new ConditionSubData { GlobalNames = name,
                valueType = EventSO.ValueType.Bool, ExpectedBool = true }).ToArray()
        };

        private static ConditionGroupData Interact(string ready)
        {
            var condition = Flags(ready, CanInteract);
            condition.Checks |= ConditionChecks.InputKey;
            condition.InputKeyCode = KeyCode.E;
            condition.InputKeyState = InputKeyCheck.PressedThisFrame;
            return condition;
        }

        private static EventStep Set(string name, bool value = true) => new EventStep
        {
            Flags = new EventStepFlags { IsSetGlobals = true },
            SetGlobals = new SetValueData { SetValues = new[] { new SetValueSubData
                { ValueName = name, valueType = EventSO.ValueType.Bool, boolValue = value } } }
        };

        private static EventStep Pause(float duration) => new EventStep
        {
            Flags = new EventStepFlags { IsWait = true }, Wait = new WaitData { WaitTime = duration }
        };

        private static EventStep WaitFor(string signal) => new EventStep
        {
            Flags = new EventStepFlags { IsWaitUntilCondition = true },
            WaitUntilCondition = new WaitUntilConditionData { Condition = Flags(signal), Timeout = 0f }
        };

        private static EventStep Objects(string[] enable = null, string[] disable = null) => new EventStep
        {
            Flags = new EventStepFlags { IsObjects = true },
            Objects = new ObjectsData { IsEnableObject = enable != null, IsDisableObject = disable != null,
                EnableObject = new EnableData { EnableObjectNames = enable ?? Array.Empty<string>() },
                DisableObject = new DisableData { DisableObjectNames = disable ?? Array.Empty<string>() } }
        };

        private static EventStep Fade(string target, float duration) => new EventStep
        {
            Flags = new EventStepFlags { IsComponents = true },
            Components = new ComponentsGroupData { IsVisualFade = true,
                VisualFade = new VisualFadeData { Fades = new[] { new VisualFadeEntryData
                    { ObjectName = target, TargetAlpha = 0, Duration = duration } } } }
        };

        private static EventStep Portrait(params (string speaker, string text)[] lines) => new EventStep
        {
            Flags = new EventStepFlags { IsSpeeches = true },
            Speeches = new SpeechesData { IsPortraitSpeech = true,
                PortraitSpeech = new PortraitSpeechData { AdvanceMode = PortraitAdvanceMode.Input,
                    AdvanceKey = KeyCode.Return, IsTyping = false,
                    Lines = lines.Select(line => new PortraitSpeechLineData
                    { SpeakerName = line.speaker, Text = line.text }).ToArray() } }
        };

        private static EventStep Bubble(string target, params (string text, float duration)[] lines) => new EventStep
        {
            Flags = new EventStepFlags { IsSpeeches = true },
            Speeches = new SpeechesData { IsSpeechBubble = true, SpeechBubble = new SpeechBubbleData
                { GameObjectName = target, SpeechBubbleTexts = lines.Select(line =>
                    new SpeechData { Text = line.text, Duration = line.duration }).ToList() } }
        };

        private static EventStep Move(string name, Vector3 to, float duration) => new EventStep
        {
            Flags = new EventStepFlags { IsTransforms = true },
            Transforms = new TransformsData { IsMoveObject = true,
                MoveObject = new MoveObjectData { MoveObjects = new[] { new MoveObjectSubData
                    { objectName = name, targetPosition = to, Duration = duration,
                      movementType = MovementType.EaseInOut } } } }
        };

        private static EventStep MoveCamera() => Move(Camera, new Vector3(2.5f, 2.7f, -10f), 3.5f);

        private static EventStep Rotate(string name) => new EventStep
        {
            Flags = new EventStepFlags { IsTransforms = true },
            Transforms = new TransformsData { IsRotateObject = true,
                RotateObject = new RotateObjectData { ObjectName = name, isLookAt = false,
                    isDelta = false, eulerAngles = new Vector3(0, 180, 0), Duration = .26f } }
        };

        private static EventStep Controls(bool move, bool interact = false, bool dash = false)
        {
            var step = Set(CanInteract, interact);
            step.Flags.IsComponents = true;
            step.Components = Update(
                Bool(Actor, nameof(AuthorablePlayerInput2D), nameof(AuthorablePlayerInput2D.MoveAllowed), move),
                Bool(Actor, nameof(AuthorablePlayerInput2D), nameof(AuthorablePlayerInput2D.InteractAllowed), interact),
                Bool(Actor, nameof(AuthorablePlayerInput2D), nameof(AuthorablePlayerInput2D.DashAllowed), dash),
                Bool(Actor, nameof(AuthorablePlayerInput2D), nameof(AuthorablePlayerInput2D.AttackAllowed), false));
            return step;
        }

        private static EventStep Follow(bool enabled) => new EventStep
        {
            Flags = new EventStepFlags { IsComponents = true },
            Components = Update(Bool(Camera, nameof(AuthorableCameraFollow2D), nameof(AuthorableCameraFollow2D.Follow), enabled))
        };

        private static ComponentsGroupData Update(params UpdateComponentSubData[] fields) =>
            new ComponentsGroupData { IsUpdateComponent = true,
                UpdateComponent = new UpdateComponentData { UpdateComponents = fields } };

        private static UpdateComponentSubData Bool(string target, string type, string field, bool value) =>
            new UpdateComponentSubData { gameObjectName = target, componentName = type,
                propertyName = field, valueType = EventSO.ValueType.Bool, boolValue = value };

        private static EventStep Impact() => new EventStep
        {
            Flags = new EventStepFlags { IsSound = true, IsCameras = true },
            Sound = new SoundData { audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Resources/Audio/Main/MetalHit.wav"), volume = .65f },
            Cameras = new CamerasData { IsCameraShake = true, CameraShake = new CameraShakeData
                { Duration = .65f, PositionStrength = new Vector3(.24f, .07f, 0), RotationStrength = Vector3.zero } }
        };
    }
}

