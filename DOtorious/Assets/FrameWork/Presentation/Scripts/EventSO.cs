using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace JYW.Game.EventPlay
{
    [CreateAssetMenu(menuName = "Game/Event Data")]
    public class EventSO : ScriptableObject
    {
        [System.Serializable]
        public class EventStep
        {
            public EventStepFlags Flags = new EventStepFlags();

            public SetValueData SetGlobals;

            // ── 그룹 래퍼 (서브 플래그 포함) ──
            public ObjectsData Objects;
            public SpeechesData Speeches;
            public ScenesGroupData Scenes;
            public CamerasData Cameras;
            public ComponentsGroupData Components;
            public TransformsData Transforms;

            // ── 독립 데이터 (그룹에 속하지 않는 것들) ──
            public WaitData Wait;
            public LockData Lock;
            public SoundData Sound;
            public FadeInfo FadeInfo;
            public CursorData Cursor;
            public ChoiceData Choice;
            public ActionData Action;
            public EventExeData EventExe;

            // ??????????????????????????????????????????
            //  레거시 호환 필드
            //  구 .asset 파일에서 이 이름으로 직렬화되어 있으므로
            //  Unity가 로드할 때 자동으로 값을 채워줍니다.
            //  마이그레이션 후 null로 클리어됩니다.
            // ??????????????????????????????????????????
            [SerializeField, HideInInspector] public JustTextData JustText;
            [SerializeField, HideInInspector] public RenameData Rename;
            [SerializeField, HideInInspector] public SpawnDatass SpawnObject;
            [SerializeField, HideInInspector] public DestroyData DestroyObject;
            [SerializeField, HideInInspector] public EnableData EnableObject;
            [SerializeField, HideInInspector] public DisableData DisableObject;
            [SerializeField, HideInInspector] public EnableComponentData EnableComponent;
            [SerializeField, HideInInspector] public DisableComponentData DisableComponent;
            [SerializeField, HideInInspector] public DisableColliderData DisableColliderObject;
            [SerializeField, HideInInspector] public SoftSpeechData SoftSpeech;
            [SerializeField, HideInInspector] public HardSpeechData HardSpeech;
            [SerializeField, HideInInspector] public MoveObjectData MoveObject;
            [SerializeField, HideInInspector] public RotateObjectData RotateObject;
            [SerializeField, HideInInspector] public SceneData SetActiveScene;
            [SerializeField, HideInInspector] public SceneData SceneChange;
            [SerializeField, HideInInspector] public SceneDatas SceneAdd;
            [SerializeField, HideInInspector] public SceneDatas SceneOff;
            [SerializeField, HideInInspector] public SceneDatas ScenePause;
            [SerializeField, HideInInspector] public SceneDatas SceneUnPause;
            [SerializeField, HideInInspector] public CameraMovementData CameraMovement;
            [SerializeField, HideInInspector] public CameraAimData CameraAim;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public TooltipData Tooltip;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public BlackLabelData BlackLabel;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public TimeScaleData TimeScale;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public ScreenFlashData ScreenFlash;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public WaitUntilConditionData WaitUntilCondition;
        }

        // ??????????????????????????????????????????
        //  최상위 플래그: 그룹 단위 + 독립 기능만
        // ??????????????????????????????????????????
        [System.Serializable]
        public class EventStepFlags
        {
            public bool IsSetGlobals = false;

            public bool IsObjects = false;
            public bool IsSpeeches = false;
            public bool IsScenes = false;
            public bool IsCameras = false;
            public bool IsComponents = false;
            public bool IsTransforms = false;

            public bool IsWait = false;
            public bool IsLock = false;
            public bool IsSound = false;
            public bool IsFadePlay = false;
            public bool IsCursorVisible = false;
            public bool IsChoice = false;
            public bool IsAction = false;
            public bool IsEventExe = false;

            // ??????????????????????????????????????????
            //  레거시 호환 서브 플래그
            //  구 .asset에서 이 이름으로 직렬화되어 있으므로
            //  원래 이름 그대로 유지합니다.
            // ??????????????????????????????????????????
            [SerializeField, HideInInspector] public bool IsJustText = false;
            [SerializeField, HideInInspector] public bool IsRename = false;
            [SerializeField, HideInInspector] public bool IsSpawnObject = false;
            [SerializeField, HideInInspector] public bool IsDestroyObject = false;
            [SerializeField, HideInInspector] public bool IsEnableObject = false;
            [SerializeField, HideInInspector] public bool IsDisableObject = false;
            [SerializeField, HideInInspector] public bool IsEnableComponent = false;
            [SerializeField, HideInInspector] public bool IsDisableComponent = false;
            [SerializeField, HideInInspector] public bool IsDisableColliderObject = false;
            [SerializeField, HideInInspector] public bool IsSoftSpeech = false;
            [SerializeField, HideInInspector] public bool IsHardSpeech = false;
            [SerializeField, HideInInspector] public bool IsMoveObject = false;
            [SerializeField, HideInInspector] public bool IsRotateObject = false;
            [SerializeField, HideInInspector] public bool IsSetActiveScene = false;
            [SerializeField, HideInInspector] public bool IsSceneChange = false;
            [SerializeField, HideInInspector] public bool IsSceneAdd = false;
            [SerializeField, HideInInspector] public bool IsSceneOff = false;
            [SerializeField, HideInInspector] public bool IsScenePause = false;
            [SerializeField, HideInInspector] public bool IsSceneUnPause = false;
            [SerializeField, HideInInspector] public bool IsCameraMove = false;
            [SerializeField, HideInInspector] public bool IsCameraAiming = false;

            // 새 플래그는 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsTooltip = false;

            // 새 플래그는 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsBlackLabel = false;

            // 새 플래그는 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsTimeScale = false;

            // 새 플래그는 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsScreenFlash = false;

            // 새 플래그는 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsWaitUntilCondition = false;
        }

        // ??????????????????????????????????????????
        //  그룹 래퍼 데이터 클래스 (서브 플래그 내장)
        // ??????????????????????????????????????????

        [System.Serializable]
        public class ObjectsData
        {
            public bool IsSpawnObject = false;
            public bool IsDestroyObject = false;
            public bool IsEnableObject = false;
            public bool IsDisableObject = false;
            public bool IsRename = false;

            public SpawnDatass SpawnObject;
            public DestroyData DestroyObject;
            public EnableData EnableObject;
            public DisableData DisableObject;
            public RenameData Rename;
        }

        [System.Serializable]
        public class SpeechesData
        {
            public bool IsSoftSpeech = false;
            public bool IsHardSpeech = false;

            public SoftSpeechData SoftSpeech;
            public HardSpeechData HardSpeech;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsPortraitSpeech = false;
            public PortraitSpeechData PortraitSpeech;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsSpeechBubble = false;
            public SpeechBubbleData SpeechBubble;
        }

        [System.Serializable]
        public class ScenesGroupData
        {
            public bool IsSetActiveScene = false;
            public bool IsSceneChange = false;
            public bool IsSceneAdd = false;
            public bool IsSceneOff = false;
            public bool IsScenePause = false;
            public bool IsSceneUnPause = false;

            public SceneData SetActiveScene;
            public SceneData SceneChange;
            public SceneDatas SceneAdd;
            public SceneDatas SceneOff;
            public SceneDatas ScenePause;
            public SceneDatas SceneUnPause;
        }

        [System.Serializable]
        public class CamerasData
        {
            public bool IsCameraMove = false;
            public bool IsCameraAiming = false;

            public CameraMovementData CameraMovement;
            public CameraAimData CameraAim;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsCameraShake = false;
            public CameraShakeData CameraShake;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsCameraLens = false;
            public CameraLensData CameraLens;
        }

        [System.Serializable]
        public class ComponentsGroupData
        {
            public bool IsEnableComponent = false;
            public bool IsDisableComponent = false;
            public bool IsDisableColliderObject = false;
            public bool IsUpdateComponent = false;

            public EnableComponentData EnableComponent;
            public DisableComponentData DisableComponent;
            public DisableColliderData DisableColliderObject;
            public UpdateComponentData UpdateComponent;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsAnimatorEvent = false;
            public AnimatorEventData AnimatorEvent;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsVisualFade = false;
            public VisualFadeData VisualFade;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsLightTween = false;
            public LightTweenData LightTween;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsParticleEvent = false;
            public ParticleEventData ParticleEvent;
        }

        [System.Serializable]
        public class TransformsData
        {
            public bool IsMoveObject = false;
            public bool IsRotateObject = false;

            public MoveObjectData MoveObject;
            public RotateObjectData RotateObject;

            // 새 기능은 기존 직렬화 필드 순서를 보존하기 위해 항상 끝에 추가합니다.
            public bool IsAttachObject = false;
            public AttachObjectData AttachObject;
        }

        [Tooltip("false이면 조건 검사 없이 ConditionSteps[0]을 실행합니다.")]
        [SerializeField] public bool UseCondition = true;

        [SerializeField] public ConditionGroupData[] Conditions = Array.Empty<ConditionGroupData>();
        [SerializeField] public EventStep[] ConditionSteps = Array.Empty<EventStep>();

        [Serializable]
        public class LegacyConditionData
        {
            public bool IsCheck = false;
            public ConditionSubData[] ConditionInfos = new ConditionSubData[0];
        }

        [SerializeField, FormerlySerializedAs("Condition")]
        private LegacyConditionData legacy_Condition;

        [SerializeField, FormerlySerializedAs("steps")]
        private EventStep[] legacy_steps = Array.Empty<EventStep>();

        [SerializeField, FormerlySerializedAs("noSteps")]
        private EventStep[] legacy_noSteps = Array.Empty<EventStep>();

        [SerializeField] public EventStepGroup[] stepsGroups = Array.Empty<EventStepGroup>();
        [SerializeField] public EventStep[] elseSteps = Array.Empty<EventStep>();

        public LegacyConditionData GetLegacyCondition() => legacy_Condition;
        public EventStep[] GetLegacySteps() => legacy_steps;
        public EventStep[] GetLegacyNoSteps() => legacy_noSteps;

        [System.Serializable]
        public class SetValueData
        {
            public SetValueSubData[] SetValues = Array.Empty<SetValueSubData>();
        }

        public enum SetType { Set, Add }
        public enum ValueType { Int, Float, Bool, String, GameObject }
        public enum CheckType
        {
            Value,
            Odd,
            Even,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual
        }

        [Flags]
        public enum ConditionChecks
        {
            None = 0,
            CheckValue = 1 << 0,
            TimeCount = 1 << 1,
            CollisionAB = 1 << 2,
            ObjectState = 1 << 3,
            Distance = 1 << 4,
            InputKey = 1 << 5,
            AnimatorState = 1 << 6,
            SceneState = 1 << 7,
            CameraView = 1 << 8
        }

        public enum ObjectStateCheck { Exists, Missing, Active, Inactive }
        public enum DistanceCheck { AtMost, AtLeast }
        public enum InputKeyCheck { PressedThisFrame, Held }
        public enum AnimatorStateCheck { Current, Completed }
        public enum SceneStateCheck { Loaded, Unloaded, Active }
        public enum CameraViewCheck { Visible, NotVisible }

        [System.Serializable]
        public class SetValueSubData
        {
            public bool IsPrefData = false;
            public SetType setType = SetType.Set;
            public ValueType valueType = ValueType.Int;
            public string ValueName = "";
            public int intValue;
            public float floatValue;
            public bool boolValue;
            public string stringValue;
            public GameObject gameObjectValue;
        }

        [System.Serializable]
        public class ConditionGroupData
        {
            public ConditionSubData[] Conditions = Array.Empty<ConditionSubData>();

            // 새 조건 필드는 기존 Conditions 뒤에만 추가해 직렬화 필드 순서를 보존합니다.
            [SerializeField] public ConditionChecks Checks = ConditionChecks.CheckValue;
            [Min(0f), Tooltip("게임 시작 후 이 시간이 지나면 Time Count 조건이 참이 됩니다.")]
            [SerializeField] public float TimeCountSeconds = 0f;
            [Tooltip("충돌을 확인할 씬 오브젝트 A의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string CollisionObjectA = "";
            [Tooltip("충돌을 확인할 씬 오브젝트 B의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string CollisionObjectB = "";

            [Tooltip("존재 또는 활성 상태를 확인할 오브젝트 이름이나 Root/Child 경로입니다.")]
            [SerializeField] public string ObjectStateTarget = "";
            [SerializeField] public ObjectStateCheck ObjectState = ObjectStateCheck.Exists;

            [Tooltip("거리를 확인할 오브젝트 A의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string DistanceObjectA = "";
            [Tooltip("거리를 확인할 오브젝트 B의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string DistanceObjectB = "";
            [Min(0f)] [SerializeField] public float DistanceThreshold = 1f;
            [SerializeField] public DistanceCheck DistanceComparison = DistanceCheck.AtMost;
            [Tooltip("켜면 X/Y 평면 거리만 사용하고, 끄면 X/Y/Z 3D 거리를 사용합니다.")]
            [SerializeField] public bool DistanceUse2D = false;

            [SerializeField] public KeyCode InputKeyCode = KeyCode.E;
            [SerializeField] public InputKeyCheck InputKeyState = InputKeyCheck.PressedThisFrame;

            [Tooltip("Animator를 확인할 오브젝트 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string AnimatorObject = "";
            [SerializeField] public string AnimatorStateName = "";
            [SerializeField] public int AnimatorLayer = 0;
            [SerializeField] public AnimatorStateCheck AnimatorState = AnimatorStateCheck.Current;
            [Min(0f), Tooltip("Completed 판정에 사용할 normalizedTime 기준입니다. 일반적으로 1입니다.")]
            [SerializeField] public float AnimatorCompletionTime = 1f;

            [SerializeField] public string SceneName = "";
            [SerializeField] public SceneStateCheck SceneState = SceneStateCheck.Loaded;

            [Tooltip("활성 게임 카메라 화면 안/밖 여부를 확인할 오브젝트 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string CameraViewTarget = "";
            [SerializeField] public CameraViewCheck CameraViewState = CameraViewCheck.Visible;
        }

        [System.Serializable]
        public class ConditionSubData
        {
            public string GlobalNames = "";
            public ValueType valueType = ValueType.Float;
            public CheckType checkType = CheckType.Value;
            public bool isPlayerPrefData = false;
            public int ExpectedInt = 0;
            public float ExpectedFloat = 0f;
            public bool ExpectedBool = false;
            public string ExpectedString = "";
            public GameObject ExpectedGameObject;
        }

        [System.Serializable]
        public class EventStepGroup
        {
            public EventStep[] Steps = Array.Empty<EventStep>();
        }

        [System.Serializable]
        public class MoveObjectSubData
        {
            public string objectName = "";
            [SerializeField, HideInInspector] public bool isLocal = false;
            public bool isRelative = false;
            public string targetName = "";
            public bool IsAnotherStartPosition = false;
            public bool startIsRelative = false;
            public string startTargetName = "";
            public Vector3 startPosition = Vector3.zero;
            public Vector3 targetPosition = Vector3.zero;
            public float Duration = 0f;
            public MovementType movementType = MovementType.Linear;
            public float SpiralRadius = 0f;
            public int SpiralTurns = 1;
            public float ZigZagAmplitude = 0f;
            public float ZigZagFrequency = 1f;
            public int BounceCount = 2;
            public float BounceHeight = 1f;
            public float EaseExponent = 2f;
            public float ArcHeight = 2f;
            public float ParabolaPeakHeight = 3f;
            public bool isDrawer = false;
            public Vector3 drawerOffset = Vector3.zero;
        }

        [Serializable]
        public enum MovementType { Linear, Spiral, ZigZag, Bounce, EaseInOut, Arc, Parabola }

        [System.Serializable]
        public class MoveObjectData { public MoveObjectSubData[] MoveObjects = new MoveObjectSubData[0]; }

        [System.Serializable]
        public class RotateObjectData
        {
            public string ObjectName = "";
            public bool isLookAt = true;
            public string LookAtName = "";
            public float Duration = 0f;
            public bool isLocal = true;
            public bool isDelta = true;
            public Vector3 eulerAngles = Vector3.zero;
            public bool isDoor = false;
            public Vector3 doorEulerOffset = Vector3.zero;
            public bool isRelative = false;
            public string TargetName = "";
        }

        [System.Serializable]
        public class JustTextData { [TextArea] public string Text = ""; }

        [System.Serializable]
        public class UpdateComponentSubData
        {
            public string gameObjectName = "";
            public string componentName = "";
            public string propertyName = "";
            public ValueType valueType = ValueType.String;
            public int intValue;
            public float floatValue;
            public bool boolValue;
            public string stringValue;
            public GameObject gameObjectValue;
        }

        [System.Serializable]
        public class UpdateComponentData { public UpdateComponentSubData[] UpdateComponents = new UpdateComponentSubData[0]; }

        [System.Serializable]
        public class WaitData { public float WaitTime = 0f; }

        [System.Serializable]
        public class LockData
        {
            // 기존 EventSO YAML과 외부 코드를 보존하기 위한 직렬화 저장 필드입니다.
            public bool IsLockCamera = false;
            public bool IsLockMove = false;

            public bool IsLockKeyboard
            {
                get => IsLockMove;
                set => IsLockMove = value;
            }

            public bool IsLockMouse
            {
                get => IsLockCamera;
                set => IsLockCamera = value;
            }
        }

        [System.Serializable]
        public class SpawnDatass { public SpawnData[] SpawnDatas = new SpawnData[0]; }

        [System.Serializable]
        public class DestroyData { public string[] DestroyObjectNames = Array.Empty<string>(); }

        [System.Serializable]
        public class EnableData { public string[] EnableObjectNames = Array.Empty<string>(); }

        [System.Serializable]
        public class EnableComponentSubData { public string gameObjectName = ""; public string componentName = ""; }

        [System.Serializable]
        public class EnableComponentData { public EnableComponentSubData[] ComponentDatas = new EnableComponentSubData[0]; }

        [System.Serializable]
        public class DisableComponentSubData { public string gameObjectName = ""; public string componentName = ""; }

        [System.Serializable]
        public class DisableComponentData { public DisableComponentSubData[] ComponentDatas = new DisableComponentSubData[0]; }

        [System.Serializable]
        public class DisableData { public string[] DisableObjectNames = Array.Empty<string>(); }

        [System.Serializable]
        public class DisableColliderData { public string[] DisableColliderObjectNames = Array.Empty<string>(); }

        [System.Serializable]
        public class SoftSpeechData { public List<SpeechData> SoftSpeechTexts = new List<SpeechData>(); public bool isTyping = false; }

        [System.Serializable]
        public class HardSpeechData { public KeyCode HardSpeechKey = KeyCode.E; public List<string> HardSpeechTexts = new List<string>(); public bool IsTyping = false; }

        [System.Serializable]
        public class SpeechBubbleData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트 위에 표시합니다.")]
            public string GameObjectName = "";
            public List<SpeechData> SpeechBubbleTexts = new List<SpeechData>();
            public bool isTyping = false;
        }

        [System.Serializable]
        public class SpawnData
        {
            public GameObject prefab = null;
            public string naming = "";
            public string parentName = "";
            public string sceneName = "";
            public bool IsUI = false;
            public Vector2 AnchoredPosition = Vector2.zero;
            public Vector2 AnchorMin = new Vector2(0.5f, 0.5f);
            public Vector2 AnchorMax = new Vector2(0.5f, 0.5f);
            public Vector2 Pivot = new Vector2(0.5f, 0.5f);
            public Vector3 LocalScale = Vector3.one;
            public Vector3 LocalEulerAngles = Vector3.zero;
            public bool IsLocal = false;
            public string LocalCenterName = "";
            public Vector3 LocalOffset = Vector3.zero;
            public Vector3 position = Vector3.zero;
            public Vector3 rotation = Vector3.zero;
        }

        [System.Serializable]
        public class CameraMovementData
        {
            public bool IsCameraMoveUseMain = false;
            public bool IsRelative = false;
            public string CenterObject = "";
            public Vector3 StartPosition = Vector3.zero;
            public Vector3 EndPosition = Vector3.zero;
            public float StartTime = 0f;
            public float EndTime = 0f;
        }

        [System.Serializable]
        public class CameraAimData
        {
            public bool IsCameraAimingUseMain = false;
            public string aimedTargetName = "";
            public float StartTime = 0f;
            public float EndTime = 0f;
            public float InitDuration = 0f;
            public Vector3 TargetLocalOffset = Vector3.zero;
        }

        [System.Serializable]
        public class SceneDatas { public string[] Scenes; }

        [System.Serializable]
        public class SceneData { public string Scene; }

        [System.Serializable]
        public class SoundData
        {
            public float time = 0f;
            public AudioClip audioClip = null;
            public float volume = 1.0f;
            public bool isLoop = false;

            // 기존 Sound 동작은 false일 때 그대로 유지합니다.
            public bool WaitForCompletion = false;
        }

        [System.Serializable]
        public class FadeInfo { public bool FadeIn = false; public float StartTime = 0f; public float EndTime = 0f; }

        [System.Serializable]
        public class SpeechData { public float Duration = 0f; [TextArea] public string Text = ""; }

        [System.Serializable]
        public class Candidate
        {
            public string Text;
            public string ValueName;
            public ValueType type = ValueType.Int;
            public int intValue;
            public float floatValue;
            public bool boolValue;
            public string stringValue;
            public GameObject gameObjectValue;
        }

        [System.Serializable]
        public class CursorData { public bool isCursorVisible = false; }

        [System.Serializable]
        public class ChoiceData { public Candidate[] Candidates = new Candidate[0]; }

        [System.Serializable]
        public class EventExeData
        {
            public EventSO eventSO;
            [Tooltip("false이면 조건 검사 없이 ConditionSteps[0]을 실행합니다. (inline EventExe)")]
            [SerializeField] public bool UseCondition = true;
            [SerializeField] public ConditionGroupData[] Conditions = Array.Empty<ConditionGroupData>();
            [SerializeReference] public EventStep[] ConditionSteps = Array.Empty<EventStep>();
        }

        [System.Serializable]
        public class ActionData { public string ActionName = ""; }

        [System.Serializable]
        public class RenameData { public string ObjectName = ""; public string NewName = ""; }

        [System.Serializable]
        public class TooltipData
        {
            [TextArea] public string Content = "";
            [Min(0f)] public float Duration = 0f;
            public bool isBlocked = false;
            public bool IsRelative = false;
            public string CenterObject = "";
            public Vector2 Position = Vector2.zero;
        }

        [System.Serializable]
        public class BlackLabelData
        {
            [Min(0f)] public float Duration = 0f;
        }

        public enum PortraitAdvanceMode { Timed, Input }

        [System.Serializable]
        public class PortraitSpeechLineData
        {
            public Sprite Portrait;
            public string SpeakerName = "";
            [TextArea] public string Text = "";
            [Min(0f)] public float Duration = 2f;
        }

        [System.Serializable]
        public class PortraitSpeechData
        {
            public PortraitAdvanceMode AdvanceMode = PortraitAdvanceMode.Input;
            public KeyCode AdvanceKey = KeyCode.E;
            public bool IsTyping = true;
            public PortraitSpeechLineData[] Lines = Array.Empty<PortraitSpeechLineData>();
        }

        public enum AnimatorCommand
        {
            PlayState,
            CrossFade,
            SetTrigger,
            ResetTrigger,
            SetBool,
            SetInteger,
            SetFloat,
            SetSpeed
        }

        [System.Serializable]
        public class AnimatorEventData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트를 사용합니다.")]
            public string ObjectName = "";
            public AnimatorCommand Command = AnimatorCommand.PlayState;
            public string StateOrParameter = "";
            public int Layer = -1;
            [Range(0f, 1f)] public float NormalizedTime = 0f;
            [Min(0f)] public float TransitionDuration = 0.15f;
            public bool BoolValue = false;
            public int IntValue = 0;
            public float FloatValue = 0f;
            [Min(0f), Tooltip("명령 적용 후 다음 Phase로 넘어가기 전에 기다릴 시간입니다.")]
            public float Duration = 0f;
        }

        [System.Serializable]
        public class CameraShakeData
        {
            [Tooltip("켜면 Main Camera, 끄면 Presentation EventCamera를 흔듭니다.")]
            public bool UseMainCamera = true;
            [Min(0f)] public float Duration = 0.5f;
            public Vector3 PositionStrength = new Vector3(0.15f, 0.15f, 0f);
            public Vector3 RotationStrength = new Vector3(0.8f, 0.8f, 1.2f);
            [Min(0f)] public float Frequency = 24f;
            public bool FadeOut = true;
        }

        [System.Serializable]
        public class TimeScaleData
        {
            [Range(0f, 10f)] public float TargetScale = 0.2f;
            [Min(0f)] public float Duration = 0.15f;
            [Tooltip("켜면 Duration 후 이벤트 시작 전 Time Scale로 복구합니다.")]
            public bool RestoreAfterDuration = true;
        }

        [System.Serializable]
        public class ScreenFlashData
        {
            public Color FlashColor = Color.white;
            [Range(0f, 1f)] public float PeakAlpha = 1f;
            [Min(0f)] public float FadeInDuration = 0f;
            [Min(0f), Tooltip("최대 알파로 유지할 시간입니다.")]
            public float Duration = 0.05f;
            [Min(0f)] public float FadeOutDuration = 0.2f;
        }

        [System.Serializable]
        public class AttachObjectEntryData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트를 사용합니다.")]
            public string ObjectName = "";
            [Tooltip("비우면 부모에서 분리합니다. 이름 또는 Root/Child 경로를 사용할 수 있습니다.")]
            public string ParentName = "";
            public bool WorldPositionStays = true;
            public bool ApplyLocalTransform = false;
            public Vector3 LocalPosition = Vector3.zero;
            public Vector3 LocalEulerAngles = Vector3.zero;
            public Vector3 LocalScale = Vector3.one;
        }

        [System.Serializable]
        public class AttachObjectData
        {
            public AttachObjectEntryData[] AttachObjects = Array.Empty<AttachObjectEntryData>();
        }

        [System.Serializable]
        public class WaitUntilConditionData
        {
            public ConditionGroupData Condition = new ConditionGroupData();
            [Min(0f), Tooltip("0이면 조건이 충족될 때까지 제한 없이 기다립니다.")]
            public float Timeout = 0f;
        }

        [System.Serializable]
        public class CameraLensData
        {
            [Tooltip("켜면 Main Camera, 끄면 Presentation EventCamera의 렌즈를 조절합니다.")]
            public bool UseMainCamera = true;
            [Range(1f, 179f), Tooltip("Perspective Camera일 때 적용할 Field Of View입니다.")]
            public float TargetFieldOfView = 60f;
            [Min(0.0001f), Tooltip("Orthographic Camera일 때 적용할 Size입니다.")]
            public float TargetOrthographicSize = 5f;
            [Min(0f), Tooltip("목표 렌즈 값까지 전환하며 다음 Phase를 막는 unscaled seconds입니다.")]
            public float Duration = 0.5f;
            public bool EaseInOut = true;
        }

        [System.Serializable]
        public class VisualFadeEntryData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트를 사용합니다.")]
            public string ObjectName = "";
            public bool IncludeChildren = true;
            [Range(0f, 1f)] public float TargetAlpha = 0f;
            [Min(0f), Tooltip("목표 Alpha까지 전환하며 다음 Phase를 막는 unscaled seconds입니다.")]
            public float Duration = 0.5f;
            public bool EaseInOut = true;
        }

        [System.Serializable]
        public class VisualFadeData
        {
            public VisualFadeEntryData[] Fades = Array.Empty<VisualFadeEntryData>();
        }

        [System.Serializable]
        public class LightTweenEntryData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트를 사용합니다.")]
            public string ObjectName = "";
            public bool IncludeChildren = true;
            public bool AffectColor = false;
            public Color TargetColor = Color.white;
            public bool AffectIntensity = true;
            [Min(0f)] public float TargetIntensity = 1f;
            public bool AffectRange = false;
            [Min(0f)] public float TargetRange = 10f;
            [Min(0f), Tooltip("목표 조명 값까지 전환하며 다음 Phase를 막는 unscaled seconds입니다.")]
            public float Duration = 0.5f;
            public bool EaseInOut = true;
        }

        [System.Serializable]
        public class LightTweenData
        {
            public LightTweenEntryData[] Lights = Array.Empty<LightTweenEntryData>();
        }

        public enum ParticleCommand
        {
            Play,
            Pause,
            StopEmitting,
            StopAndClear,
            Clear
        }

        [System.Serializable]
        public class ParticleEventData
        {
            [Tooltip("비우면 이 EventSO를 호출한 오브젝트를 사용합니다.")]
            public string ObjectName = "";
            public bool IncludeChildren = true;
            public ParticleCommand Command = ParticleCommand.Play;
            [Min(0f), Tooltip("명령 적용 후 다음 Phase를 막는 unscaled seconds입니다.")]
            public float Duration = 0f;
        }
    }
}
