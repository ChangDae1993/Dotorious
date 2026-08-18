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
        }

        [System.Serializable]
        public class TransformsData
        {
            public bool IsMoveObject = false;
            public bool IsRotateObject = false;

            public MoveObjectData MoveObject;
            public RotateObjectData RotateObject;
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
        public enum CheckType { Value, Odd, Even }

        [Flags]
        public enum ConditionChecks
        {
            None = 0,
            CheckValue = 1 << 0,
            TimeCount = 1 << 1,
            CollisionAB = 1 << 2
        }

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
            [SerializeField] public ConditionChecks Checks = ConditionChecks.CheckValue;
            [Min(0f), Tooltip("게임 시작 후 이 시간이 지나면 Time Count 조건이 참이 됩니다.")]
            [SerializeField] public float TimeCountSeconds = 0f;
            public ConditionSubData[] Conditions = Array.Empty<ConditionSubData>();
            [Tooltip("충돌을 확인할 씬 오브젝트 A의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string CollisionObjectA = "";
            [Tooltip("충돌을 확인할 씬 오브젝트 B의 이름 또는 Root/Child 경로입니다.")]
            [SerializeField] public string CollisionObjectB = "";
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
        public class LockData { public bool IsLockCamera = false; public bool IsLockMove = false; }

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
        public class SceneDatas { public string[] Scenes = Array.Empty<string>(); }

        [System.Serializable]
        public class SceneData { public string Scene; }

        [System.Serializable]
        public class SoundData { public float time = 0f; public AudioClip audioClip = null; public float volume = 1.0f; public bool isLoop = false; }

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
    }
}
