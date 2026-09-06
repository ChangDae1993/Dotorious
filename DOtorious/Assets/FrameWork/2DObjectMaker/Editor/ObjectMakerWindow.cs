using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JYW.Game.ObjectMaker.Editor
{
    public sealed class ObjectMakerWindow : EditorWindow
    {
        [SerializeField] private ObjectDefinitionSO currentDefinition;
        [SerializeField] private ObjectDefinitionData draft = ObjectDefinitionData.CreateSG001();
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private bool showCore = true;
        [SerializeField] private bool showRules = true;
        [SerializeField] private bool showPlayerControls = true;
        [SerializeField] private bool showPlayerAttacks = true;
        [SerializeField] private bool showContactDamage;
        [SerializeField] private bool showMovement = true;
        [SerializeField] private bool showDetection = true;
        [SerializeField] private bool showChase = true;
        [SerializeField] private bool showAttack = true;
        [SerializeField] private bool showHit = true;
        [SerializeField] private bool showDestruction = true;
        [SerializeField] private bool showMotions = true;
        [SerializeField] private bool showSceneSpawns;
        [SerializeField] private int openConditionRule = -1;
        [SerializeField] private int openActionRule = -1;
        [SerializeField] private string statusMessage = "SG-001 기본값이 준비되었습니다.";

        public static Rect LastMakeButtonRectForTests { get; private set; }
        public static Rect LastDefinitionDropRectForTests { get; private set; }
        public static string LastBuildButtonLabelForTests { get; private set; }
        public static Rect LastDisplayNameRectForTests { get; private set; }
        public static Rect LastConditionPopupRectForTests { get; private set; }
        public static Rect LastActionPopupRectForTests { get; private set; }
        public static Rect LastAddRuleButtonRectForTests { get; private set; }
        public static Rect LastConditionChoiceRectForTests { get; private set; }
        public static Rect LastHealthReachedConditionChoiceRectForTests { get; private set; }
        public static Rect LastActionChoiceRectForTests { get; private set; }
        public static Rect LastHealthReachedValueRectForTests { get; private set; }
        public static Rect LastEnemyTargetButtonRectForTests { get; private set; }
        public static Rect LastPlayerTargetButtonRectForTests { get; private set; }
        public static Rect LastAddPlayerAttackButtonRectForTests { get; private set; }
        public static Rect LastRunKeyRectForTests { get; private set; }
        public static Rect LastRunSpeedRectForTests { get; private set; }
        public static Rect LastPlayerWalkSoundRectForTests { get; private set; }
        public static Rect LastPlayerRunSoundRectForTests { get; private set; }
        public static Rect LastPlayerJumpSoundRectForTests { get; private set; }
        public static Rect LastPlayerAttackSoundRectForTests { get; private set; }
        public static Rect LastEnemyConditionSoundRectForTests { get; private set; }
        public static Rect LastEnemyActionSoundRectForTests { get; private set; }
        public static Rect LastHitSoundRectForTests { get; private set; }
        public static Rect LastDestructionSoundRectForTests { get; private set; }
        public ObjectDefinitionSO CurrentDefinitionForTests => currentDefinition;

        [MenuItem("Window/FrameWork/2DObjectMaker", false, 2301)]
        public static void OpenWindow()
        {
            Open(Selection.activeObject);
        }

        public static ObjectMakerWindow Open(UnityEngine.Object source)
        {
            ObjectMakerWindow window = GetWindow<ObjectMakerWindow>();
            window.titleContent = new GUIContent("2DObjectMaker");
            window.minSize = new Vector2(520f, 520f);
            if (source != null)
                window.LoadObject(source);
            window.Show();
            window.Focus();
            return window;
        }

        [OnOpenAsset]
#if UNITY_6000_5_OR_NEWER
        private static bool OpenDefinitionAsset(EntityId entityId, int line)
        {
            ObjectDefinitionSO definition = EditorUtility.EntityIdToObject(entityId) as ObjectDefinitionSO;
#else
        private static bool OpenDefinitionAsset(int instanceId, int line)
        {
            ObjectDefinitionSO definition = EditorUtility.InstanceIDToObject(instanceId) as ObjectDefinitionSO;
#endif
            if (definition == null)
                return false;
            Open(definition);
            return true;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("2DObjectMaker");
            minSize = new Vector2(520f, 520f);
            if (draft == null)
                draft = ObjectDefinitionData.CreateSG001();
            PrepareDraftForRuleEditing();
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (!followSelection)
                return;
            ObjectDefinitionSO selected = ResolveDefinition(Selection.activeObject);
            if (selected != null && selected != currentDefinition)
                LoadDefinition(selected);
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawDefinitionDropZone();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawVisualHeader();
            GUILayout.Space(4f);
            DrawCore();
            if (draft.kind == ObjectKind.Player)
            {
                DrawPlayerControls();
            }
            else
            {
                DrawAIRules();
                DrawContactDamage();
            }
            DrawHit();
            DrawDestruction();
            DrawSceneSpawns();
            DrawBuildArea();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("2DObjectMaker", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                EditorApplication.delayCall += ExportJson;
            if (GUILayout.Button("Import JSON", EditorStyles.toolbarButton, GUILayout.Width(82f)))
                EditorApplication.delayCall += ImportJson;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Edit Source", GUILayout.Width(68f));
            UnityEngine.Object selected = EditorGUILayout.ObjectField(
                currentDefinition,
                typeof(UnityEngine.Object),
                false,
                GUILayout.MinWidth(100f));
            if (selected != currentDefinition)
            {
                if (selected != null)
                    LoadObject(selected);
                else
                    currentDefinition = null;
            }

            bool nextFollow = GUILayout.Toggle(
                followSelection,
                "Follow Selection",
                EditorStyles.toolbarButton,
                GUILayout.Width(104f));
            if (nextFollow != followSelection)
                followSelection = nextFollow;

            if (GUILayout.Button("SG-001 Reset", EditorStyles.toolbarButton, GUILayout.Width(88f)))
            {
                draft = ObjectDefinitionData.CreateSG001();
                PrepareDraftForRuleEditing();
                currentDefinition = null;
                statusMessage = "SG-001 기본값으로 초기화했습니다.";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ExportJson()
        {
            if (this == null) return;
            try
            {
                ObjectMakerPaths.EnsureGeneratedStructure();
                string path = EditorUtility.SaveFilePanel("Export 2DObjectMaker JSON",
                    System.IO.Path.GetFullPath(ObjectMakerPaths.GeneratedJson),
                    ObjectMakerPaths.SafeFileName(draft.displayName), "json");
                if (string.IsNullOrEmpty(path)) return;
                ObjectMakerJson.ExportFile(path, draft);
                statusMessage = "JSON 내보내기: " + path;
            }
            catch (Exception exception)
            {
                statusMessage = "JSON 내보내기 실패: " + exception.Message;
            }
            Repaint();
        }

        private void ImportJson()
        {
            if (this == null) return;
            try
            {
                string path = EditorUtility.OpenFilePanel("Import 2DObjectMaker JSON",
                    System.IO.Directory.Exists(ObjectMakerPaths.GeneratedJson)
                        ? System.IO.Path.GetFullPath(ObjectMakerPaths.GeneratedJson) : Application.dataPath, "json");
                if (string.IsNullOrEmpty(path)) return;
                ObjectDefinitionData imported = ObjectMakerJson.ImportFile(path, out string[] warnings);
                Undo.RecordObject(this, "Import 2DObjectMaker JSON");
                draft = imported;
                PrepareDraftForRuleEditing();
                statusMessage = "JSON 불러옴: " + path + "\n" +
                    (currentDefinition != null
                        ? "현재 Modify 대상에 설정을 불러왔습니다. Modify를 누르면 저장됩니다."
                        : "Make를 누르면 새 Definition / Prefab / Animator가 생성됩니다.");
                if (warnings.Length > 0)
                    statusMessage += "\n연결되지 않은 에셋: " + string.Join("\n", warnings);
            }
            catch (Exception exception)
            {
                statusMessage = "JSON 가져오기 실패: " + exception.Message;
            }
            Repaint();
        }

        private void DrawDefinitionDropZone()
        {
            Rect rect = GUILayoutUtility.GetRect(
                0f,
                52f,
                GUILayout.ExpandWidth(true));
            LastDefinitionDropRectForTests = ToWindowRect(rect);
            string label = currentDefinition == null
                ? "생성된 Prefab 또는 Object Definition SO를 여기에 드롭"
                : "Modify: " + currentDefinition.Data.displayName +
                  "  |  다른 Prefab/Definition을 드롭하면 전환";
            GUI.Box(rect, label, EditorStyles.helpBox);

            Event current = Event.current;
            if (current == null || !rect.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated &&
                 current.type != EventType.DragPerform))
                return;

            ObjectDefinitionSO definition = null;
            UnityEngine.Object[] dragged = DragAndDrop.objectReferences;
            for (int i = 0; dragged != null && i < dragged.Length; i++)
            {
                definition = ResolveDefinition(dragged[i]);
                if (definition != null)
                    break;
            }

            if (definition == null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                current.Use();
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                LoadDefinition(definition);
                GUI.FocusControl(null);
            }
            current.Use();
        }

        private void DrawVisualHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("오브젝트 정체성", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("제작 대상", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            bool enemySelected = draft.kind != ObjectKind.Player;
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = enemySelected
                ? new Color(1f, 0.72f, 0.45f)
                : previousBackground;
            if (GUILayout.Button("Enemy", EditorStyles.miniButtonLeft, GUILayout.Height(28f)))
            {
                draft.kind = ObjectKind.Monster;
                openConditionRule = -1;
                openActionRule = -1;
                GUI.FocusControl(null);
            }
            LastEnemyTargetButtonRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            GUI.backgroundColor = !enemySelected
                ? new Color(0.45f, 0.78f, 1f)
                : previousBackground;
            if (GUILayout.Button("Player", EditorStyles.miniButtonRight, GUILayout.Height(28f)))
            {
                draft.kind = ObjectKind.Player;
                openConditionRule = -1;
                openActionRule = -1;
                GUI.FocusControl(null);
            }
            LastPlayerTargetButtonRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            GUI.backgroundColor = previousBackground;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                draft.kind == ObjectKind.Player
                    ? "Player를 만들면 이동·점프 입력과 모든 공격/콤보가 연결된 Prefab 및 Animator가 생성됩니다. " +
                      "Prefab의 Visual/Sprite Renderer에서 이미지를 바꾸고, 생성된 Idle·Walk·Run·Jump·Hit·Dead 및 Player_Attack State에 모션만 넣으세요."
                    : "Enemy를 만들면 아래 AI 규칙 수와 행동에 맞춘 State/전이가 포함된 Prefab 및 Animator가 생성됩니다. " +
                      "Prefab의 Visual/Sprite Renderer에서 이미지를 바꾸고, 생성된 공용/Enemy_Rule State에 모션만 넣으세요.",
                MessageType.Info);
            draft.objectId = EditorGUILayout.TextField("Object ID", draft.objectId);
            GUI.SetNextControlName("ObjectMaker.DisplayName");
            draft.displayName = EditorGUILayout.TextField("Display Name", draft.displayName);
            LastDisplayNameRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            draft.role = EditorGUILayout.TextField("Role", draft.role);
            draft.firstAppearance = EditorGUILayout.TextField("First Appearance", draft.firstAppearance);
            draft.facesRightByDefault = EditorGUILayout.Toggle("Image Faces Right", draft.facesRightByDefault);
            EditorGUILayout.EndVertical();
        }

        private void DrawCore()
        {
            showCore = EditorGUILayout.BeginFoldoutHeaderGroup(showCore, "기본 스탯");
            if (showCore)
            {
                draft.core.maxHealth = EditorGUILayout.IntField("Health", draft.core.maxHealth);
                if (draft.kind != ObjectKind.Player)
                    draft.core.attackPower = EditorGUILayout.IntField("Attack", draft.core.attackPower);
                draft.core.defense = EditorGUILayout.IntField("Defense", draft.core.defense);
                draft.core.moveSpeed = EditorGUILayout.FloatField("Move Speed", draft.core.moveSpeed);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPlayerControls()
        {
            if (draft.player == null)
                draft.player = new ObjectPlayerSettings();
            if (draft.player.attacks == null)
                draft.player.attacks = new List<ObjectPlayerAttack>();

            showPlayerControls = EditorGUILayout.BeginFoldoutHeaderGroup(
                showPlayerControls,
                "Player 이동 / 점프 키");
            if (showPlayerControls)
            {
                EditorGUILayout.HelpBox(
                    "새 Input System 기준 키입니다. 이동은 Rigidbody2D 속도로 처리하고, 점프는 GroundProbe가 지면에 닿을 때 한 번만 실행되어 점프 모션이 공중에서 반복되지 않습니다.",
                    MessageType.Info);
                draft.player.moveLeftKey = (Key)EditorGUILayout.EnumPopup(
                    "왼쪽 이동 키", draft.player.moveLeftKey);
                draft.player.moveRightKey = (Key)EditorGUILayout.EnumPopup(
                    "오른쪽 이동 키", draft.player.moveRightKey);
                draft.player.runKey = (Key)EditorGUILayout.EnumPopup(
                    "달리기 키", draft.player.runKey);
                LastRunKeyRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
                draft.player.runSpeedMultiplier = EditorGUILayout.FloatField(
                    "달리기 속도 배율", draft.player.runSpeedMultiplier);
                LastRunSpeedRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
                draft.player.jumpKey = (Key)EditorGUILayout.EnumPopup(
                    "점프 키", draft.player.jumpKey);
                draft.player.jumpForce = EditorGUILayout.FloatField(
                    "점프 힘", draft.player.jumpForce);
                draft.player.groundCheckRadius = EditorGUILayout.FloatField(
                    "지면 확인 반경", draft.player.groundCheckRadius);
                draft.player.groundMask = DrawLayerMask(
                    "지면 레이어", draft.player.groundMask);
                draft.player.allowMovementDuringAttack = EditorGUILayout.Toggle(
                    "공격 중 이동 허용", draft.player.allowMovementDuringAttack);

                GUILayout.Space(4f);
                EditorGUILayout.LabelField("Player 행동 소리", EditorStyles.boldLabel);
                draft.player.walkSound = DrawSoundCue(
                    "걷기 소리",
                    draft.player.walkSound ?? ObjectSoundCue2D.CreateLooping(),
                    true,
                    rect => LastPlayerWalkSoundRectForTests = rect);
                draft.player.runSound = DrawSoundCue(
                    "달리기 소리",
                    draft.player.runSound ?? ObjectSoundCue2D.CreateLooping(),
                    true,
                    rect => LastPlayerRunSoundRectForTests = rect);
                draft.player.jumpSound = DrawSoundCue(
                    "점프 소리",
                    draft.player.jumpSound,
                    false,
                    rect => LastPlayerJumpSoundRectForTests = rect);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            showPlayerAttacks = EditorGUILayout.BeginFoldoutHeaderGroup(
                showPlayerAttacks,
                "Player 공격 / 콤보");
            if (showPlayerAttacks)
            {
                EditorGUILayout.HelpBox(
                    "공격 하나마다 키와 판정 수치를 정합니다. 콤보 수가 3이면 Make할 때 해당 공격용 Animator State 3개가 순서대로 연결됩니다.",
                    MessageType.Info);

                int removeIndex = -1;
                int moveFrom = -1;
                int moveTo = -1;
                for (int i = 0; i < draft.player.attacks.Count; i++)
                {
                    ObjectPlayerAttack attack = draft.player.attacks[i] ?? new ObjectPlayerAttack();
                    draft.player.attacks[i] = attack;
                    attack.Sanitize(i);

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("공격 " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(54f));
                    attack.displayName = EditorGUILayout.TextField(attack.displayName);
                    EditorGUI.BeginDisabledGroup(i == 0);
                    if (GUILayout.Button("▲", GUILayout.Width(25f)))
                    {
                        moveFrom = i;
                        moveTo = i - 1;
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(i >= draft.player.attacks.Count - 1);
                    if (GUILayout.Button("▼", GUILayout.Width(25f)))
                    {
                        moveFrom = i;
                        moveTo = i + 1;
                    }
                    EditorGUI.EndDisabledGroup();
                    if (GUILayout.Button("X", GUILayout.Width(25f)))
                        removeIndex = i;
                    EditorGUILayout.EndHorizontal();

                    attack.key = (Key)EditorGUILayout.EnumPopup("공격 키", attack.key);
                    attack.damage = EditorGUILayout.IntField("데미지", attack.damage);
                    attack.inputDelaySeconds = EditorGUILayout.FloatField(
                        "공격 전 딜레이", attack.inputDelaySeconds);
                    attack.activeSeconds = EditorGUILayout.FloatField(
                        "공격 판정 지속시간", attack.activeSeconds);
                    attack.recoverySeconds = EditorGUILayout.FloatField(
                        "공격 후 딜레이", attack.recoverySeconds);
                    attack.cooldownSeconds = EditorGUILayout.FloatField(
                        "재사용 대기시간", attack.cooldownSeconds);
                    attack.range = EditorGUILayout.FloatField("공격 거리", attack.range);
                    attack.hitboxHeight = EditorGUILayout.FloatField(
                        "공격 판정 높이", attack.hitboxHeight);
                    attack.knockback = EditorGUILayout.FloatField("넉백", attack.knockback);
                    attack.targetInvulnerabilitySeconds = EditorGUILayout.FloatField(
                        "대상 피격 무적", attack.targetInvulnerabilitySeconds);
                    attack.sound = DrawSoundCue(
                        "공격 소리",
                        attack.sound,
                        false,
                        i == 0
                            ? rect => LastPlayerAttackSoundRectForTests = rect
                            : (Action<Rect>)null);
                    attack.comboSteps = EditorGUILayout.IntSlider(
                        "콤보 수", attack.comboSteps, 1, 12);
                    if (attack.comboSteps > 1)
                    {
                        attack.comboInputWindowSeconds = EditorGUILayout.FloatField(
                            "콤보 입력 가능시간", attack.comboInputWindowSeconds);
                        attack.comboResetSeconds = EditorGUILayout.FloatField(
                            "콤보 초기화 시간", attack.comboResetSeconds);
                        EditorGUILayout.LabelField(
                            "생성 State",
                            ObjectAnimatorGraph2D.PlayerAttackState(i, 0) + " ~ " +
                            ObjectAnimatorGraph2D.PlayerAttackState(i, attack.comboSteps - 1),
                            EditorStyles.miniLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            "생성 State",
                            ObjectAnimatorGraph2D.PlayerAttackState(i, 0),
                            EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndVertical();
                }

                if (removeIndex >= 0)
                    draft.player.attacks.RemoveAt(removeIndex);
                else if (moveFrom >= 0 && moveTo >= 0)
                {
                    ObjectPlayerAttack moved = draft.player.attacks[moveFrom];
                    draft.player.attacks.RemoveAt(moveFrom);
                    draft.player.attacks.Insert(moveTo, moved);
                }

                if (GUILayout.Button("+ 공격 추가", GUILayout.Height(28f)))
                {
                    draft.player.attacks.Add(new ObjectPlayerAttack
                    {
                        displayName = "Attack " + (draft.player.attacks.Count + 1)
                    });
                }
                LastAddPlayerAttackButtonRectForTests = ToWindowRect(
                    GUILayoutUtility.GetLastRect());
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAIRules()
        {
            showRules = EditorGUILayout.BeginFoldoutHeaderGroup(showRules, "AI 규칙 프로그램");
            if (showRules)
            {
                EditorGUILayout.HelpBox(
                    "위쪽 규칙부터 검사해 처음 맞는 '~할 때' 하나를 실행합니다. 모든 카드의 '이 행동'은 같은 공용 AI 목록이며, 같은 행동을 여러 조건에서 반복 선택할 수 있습니다.",
                    MessageType.Info);
                EditorGUILayout.LabelField(
                    ObjectBehaviorCatalog.ConditionCount + "개 조건 / " +
                    ObjectBehaviorCatalog.ActionCount + "개 공용 행동 / 규칙 수 제한 없음",
                    EditorStyles.miniBoldLabel);

                if (draft.ai == null)
                    draft.ai = new ObjectAIProgram();
                if (draft.ai.rules == null)
                    draft.ai.rules = new List<ObjectAIRule>();

                int removeIndex = -1;
                int moveFrom = -1;
                int moveTo = -1;
                for (int i = 0; i < draft.ai.rules.Count; i++)
                {
                    ObjectAIRule rule = draft.ai.rules[i] ?? new ObjectAIRule();
                    draft.ai.rules[i] = rule;
                    rule.Sanitize();

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(18f));
                    GUILayout.Label("우선순위 " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(76f));
                    rule.label = EditorGUILayout.TextField(rule.label);
                    EditorGUI.BeginDisabledGroup(i == 0);
                    if (GUILayout.Button("▲", GUILayout.Width(25f)))
                    {
                        moveFrom = i;
                        moveTo = i - 1;
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(i >= draft.ai.rules.Count - 1);
                    if (GUILayout.Button("▼", GUILayout.Width(25f)))
                    {
                        moveFrom = i;
                        moveTo = i + 1;
                    }
                    EditorGUI.EndDisabledGroup();
                    if (GUILayout.Button("X", GUILayout.Width(25f)))
                        removeIndex = i;
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.BeginDisabledGroup(!rule.enabled);
                    rule.when = DrawConditionDropdown(i, rule.when);
                    EditorGUILayout.HelpBox(
                        ObjectBehaviorCatalog.DescribeCondition(rule.when),
                        MessageType.None);
                    DrawConditionFields(rule);
                    rule.conditionSound = DrawSoundCue(
                        "~할 때 발동 소리",
                        rule.conditionSound,
                        false,
                        i == 0
                            ? rect => LastEnemyConditionSoundRectForTests = rect
                            : (Action<Rect>)null);

                    rule.action = DrawActionDropdown(i, rule.action);
                    EditorGUILayout.HelpBox(
                        ObjectBehaviorCatalog.DescribeAction(rule.action),
                        MessageType.None);
                    DrawActionFields(rule);
                    rule.actionSound = DrawSoundCue(
                        "이 행동 소리",
                        rule.actionSound,
                        true,
                        i == 0
                            ? rect => LastEnemyActionSoundRectForTests = rect
                            : (Action<Rect>)null);
                    DrawRuleEffect(rule);
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(3f);
                }

                if (removeIndex >= 0 && removeIndex < draft.ai.rules.Count)
                    draft.ai.rules.RemoveAt(removeIndex);
                else if (moveFrom >= 0 && moveTo >= 0 &&
                         moveFrom < draft.ai.rules.Count && moveTo < draft.ai.rules.Count)
                {
                    ObjectAIRule moving = draft.ai.rules[moveFrom];
                    draft.ai.rules.RemoveAt(moveFrom);
                    draft.ai.rules.Insert(moveTo, moving);
                }

                if (GUILayout.Button("+ ~할 때 규칙 추가", GUILayout.Height(30f)))
                    draft.ai.rules.Add(new ObjectAIRule());
                LastAddRuleButtonRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());

                if (GUILayout.Button("SG-001 예시 규칙 6개로 되돌리기"))
                    draft.ai.rules = ObjectAIProgram.CreateSG001Rules();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private ObjectAICondition DrawConditionDropdown(
            int ruleIndex,
            ObjectAICondition current)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("~할 때");
            if (GUILayout.Button(GetEnumLabel(current) + "  ▼", EditorStyles.popup))
            {
                openConditionRule = openConditionRule == ruleIndex ? -1 : ruleIndex;
                openActionRule = -1;
            }
            if (ruleIndex == 0)
                LastConditionPopupRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            EditorGUILayout.EndHorizontal();

            if (openConditionRule != ruleIndex)
                return current;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Array rawValues = Enum.GetValues(typeof(ObjectAICondition));
            var values = new List<ObjectAICondition>();
            var seenValues = new HashSet<int>();
            for (int i = 0; i < rawValues.Length; i++)
            {
                ObjectAICondition value = (ObjectAICondition)rawValues.GetValue(i);
                if (seenValues.Add((int)value))
                    values.Add(value);
            }
            for (int index = 0; index < values.Count; index += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < 2; column++)
                {
                    int optionIndex = index + column;
                    if (optionIndex >= values.Count)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    ObjectAICondition option = values[optionIndex];
                    Color previous = GUI.backgroundColor;
                    if (option == current)
                        GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                    if (GUILayout.Button(GetEnumLabel(option), EditorStyles.miniButton))
                    {
                        current = option;
                        openConditionRule = -1;
                        GUI.FocusControl(null);
                    }
                    if (ruleIndex == 0 && option == ObjectAICondition.HasTarget)
                        LastConditionChoiceRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
                    if (ruleIndex == 0 && option == ObjectAICondition.HealthReached)
                        LastHealthReachedConditionChoiceRectForTests =
                            ToWindowRect(GUILayoutUtility.GetLastRect());
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            return current;
        }

        private ObjectAIAction DrawActionDropdown(int ruleIndex, ObjectAIAction current)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("이 행동");
            if (GUILayout.Button(GetEnumLabel(current) + "  ▼", EditorStyles.popup))
            {
                openActionRule = openActionRule == ruleIndex ? -1 : ruleIndex;
                openConditionRule = -1;
            }
            if (ruleIndex == 0)
                LastActionPopupRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            EditorGUILayout.EndHorizontal();

            if (openActionRule != ruleIndex)
                return current;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Array values = Enum.GetValues(typeof(ObjectAIAction));
            for (int index = 0; index < values.Length; index += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < 2; column++)
                {
                    int optionIndex = index + column;
                    if (optionIndex >= values.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    ObjectAIAction option = (ObjectAIAction)values.GetValue(optionIndex);
                    Color previous = GUI.backgroundColor;
                    if (option == current)
                        GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                    if (GUILayout.Button(GetEnumLabel(option), EditorStyles.miniButton))
                    {
                        current = option;
                        openActionRule = -1;
                        GUI.FocusControl(null);
                    }
                    if (ruleIndex == 0 && option == ObjectAIAction.Wait)
                        LastActionChoiceRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            return current;
        }

        private static string GetEnumLabel(Enum value)
        {
            if (value == null)
                return string.Empty;
            var field = value.GetType().GetField(value.ToString());
            if (field != null)
            {
                var attribute = Attribute.GetCustomAttribute(
                    field,
                    typeof(InspectorNameAttribute)) as InspectorNameAttribute;
                if (attribute != null && !string.IsNullOrWhiteSpace(attribute.displayName))
                    return attribute.displayName;
            }
            return ObjectNames.NicifyVariableName(value.ToString());
        }

        private void DrawConditionFields(ObjectAIRule rule)
        {
            ObjectAIConditionParameters condition = rule.condition;
            if (ObjectBehaviorCatalog.UsesDetectionValues(rule.when))
            {
                condition.detectionShape = (ObjectAIDetectionShape)EditorGUILayout.EnumPopup(
                    "감지 방식", condition.detectionShape);
                if (condition.detectionShape == ObjectAIDetectionShape.AmbushRange)
                {
                    condition.distance = EditorGUILayout.FloatField("잠복 해제 거리", condition.distance);
                }
                else
                {
                    condition.frontDistance = EditorGUILayout.FloatField(
                        "전방 감지 거리", condition.frontDistance);
                    if (condition.detectionShape == ObjectAIDetectionShape.Directional)
                        condition.rearDistance = EditorGUILayout.FloatField(
                            "후방 감지 거리", condition.rearDistance);
                    condition.verticalRange = EditorGUILayout.FloatField(
                        "수직 감지 범위", condition.verticalRange);
                    condition.targetMask = DrawLayerMask("플레이어 레이어", condition.targetMask);
                }

                if (condition.detectionShape == ObjectAIDetectionShape.PeriodicScanner)
                    condition.intervalSeconds = DrawRange("스캔 켜짐 / 주기", condition.intervalSeconds);

                bool lineOfSightShape = condition.detectionShape == ObjectAIDetectionShape.LineOfSight;
                if (!lineOfSightShape)
                    condition.requireLineOfSight = EditorGUILayout.Toggle(
                        "벽에 가리면 감지 안 함", condition.requireLineOfSight);
                if (lineOfSightShape || condition.requireLineOfSight)
                    condition.sightBlockingMask = DrawLayerMask(
                        "시야 차단 레이어", condition.sightBlockingMask);
            }
            else if (ObjectBehaviorCatalog.UsesDistance(rule.when))
            {
                condition.distance = EditorGUILayout.FloatField("판정 거리", condition.distance);
            }
            else if (rule.when == ObjectAICondition.LowHealth)
            {
                condition.healthRatio = EditorGUILayout.Slider(
                    "체력 비율", condition.healthRatio, 0f, 1f);
            }
            else if (rule.when == ObjectAICondition.HealthReached)
            {
                condition.healthValue = EditorGUILayout.IntField(
                    "도달 HP (이하)", condition.healthValue);
                LastHealthReachedValueRectForTests =
                    ToWindowRect(GUILayoutUtility.GetLastRect());
            }
            else if (rule.when == ObjectAICondition.EveryInterval)
            {
                condition.intervalSeconds = DrawRange("반복 간격", condition.intervalSeconds);
            }
            else if (rule.when == ObjectAICondition.RandomChance)
            {
                condition.chance = EditorGUILayout.Slider("당첨 확률", condition.chance, 0f, 1f);
                condition.intervalSeconds.x = EditorGUILayout.FloatField(
                    "판단 간격", condition.intervalSeconds.x);
            }

            if (rule.when == ObjectAICondition.ObstacleAhead ||
                rule.when == ObjectAICondition.CliffAhead)
                DrawTerrainProbeFields(rule.settings);
        }

        private void DrawActionFields(ObjectAIRule rule)
        {
            ObjectAIActionParameters settings = rule.settings;
            switch (rule.action)
            {
                case ObjectAIAction.Stop:
                    settings.durationSeconds = DrawRange(
                        "정지 시간 (0~0 = 계속)", settings.durationSeconds);
                    break;
                case ObjectAIAction.Wait:
                    settings.durationSeconds = DrawRange("기다릴 시간", settings.durationSeconds);
                    break;
                case ObjectAIAction.Patrol:
                case ObjectAIAction.Wander:
                    if (rule.action == ObjectAIAction.Patrol)
                        settings.patrolPattern = (ObjectAIPatrolPattern)EditorGUILayout.EnumPopup(
                            "Patrol 방식", settings.patrolPattern);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "이동 속도 배율", settings.speedMultiplier);
                    settings.moveSeconds = DrawRange("한 번 이동 시간", settings.moveSeconds);
                    settings.durationSeconds = DrawRange("한 번 정지 시간", settings.durationSeconds);
                    if (settings.patrolPattern == ObjectAIPatrolPattern.ChanceTurn)
                        settings.turnChance = EditorGUILayout.Slider(
                            "이동 시작 시 방향 전환 확률", settings.turnChance, 0f, 1f);
                    if (settings.patrolPattern == ObjectAIPatrolPattern.HopAlong)
                        settings.jumpForce = EditorGUILayout.FloatField("점프 힘", settings.jumpForce);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.Guard:
                    settings.durationSeconds = DrawRange("방향을 바꿀 간격", settings.durationSeconds);
                    break;
                case ObjectAIAction.FollowTarget:
                case ObjectAIAction.FollowDamageSource:
                    settings.followPattern = (ObjectAIFollowPattern)EditorGUILayout.EnumPopup(
                        "Follow 방식", settings.followPattern);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "추적 속도 배율", settings.speedMultiplier);
                    settings.minimumFollowSeconds = EditorGUILayout.FloatField(
                        "최소 추적 시간", settings.minimumFollowSeconds);
                    settings.memorySeconds = EditorGUILayout.FloatField(
                        "감지 상실 후 기억 시간", settings.memorySeconds);
                    settings.maxDistanceFromSpawn = EditorGUILayout.FloatField(
                        "스폰 위치 기준 최대 거리", settings.maxDistanceFromSpawn);
                    settings.giveUpTargetDistance = EditorGUILayout.FloatField(
                        "타겟과 이 거리면 포기", settings.giveUpTargetDistance);
                    if (settings.followPattern == ObjectAIFollowPattern.KeepAttackDistance)
                        settings.distance = EditorGUILayout.FloatField("유지 거리", settings.distance);
                    if (settings.followPattern == ObjectAIFollowPattern.ZigZag)
                        settings.jumpForce = EditorGUILayout.FloatField("작은 도약 힘", settings.jumpForce);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.SearchLastSeenThenReturn:
                    settings.durationSeconds = DrawRange("수색 시간", settings.durationSeconds);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "수색/귀환 속도 배율", settings.speedMultiplier);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.KeepDistance:
                    settings.distance = EditorGUILayout.FloatField("유지 거리", settings.distance);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "이동 속도 배율", settings.speedMultiplier);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.FleeTarget:
                    settings.durationSeconds = DrawRange("도주 시간", settings.durationSeconds);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "도주 속도 배율", settings.speedMultiplier);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.ReturnToSpawn:
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "귀환 속도 배율", settings.speedMultiplier);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.Jump:
                    settings.jumpForce = EditorGUILayout.FloatField("점프 힘", settings.jumpForce);
                    break;
                case ObjectAIAction.Hover:
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "부유 이동 속도 배율", settings.speedMultiplier);
                    settings.moveSeconds = DrawRange("부유 시간", settings.moveSeconds);
                    break;
                case ObjectAIAction.Search:
                    settings.durationSeconds = DrawRange("수색 시간", settings.durationSeconds);
                    settings.speedMultiplier = EditorGUILayout.FloatField(
                        "수색 속도 배율", settings.speedMultiplier);
                    DrawTerrainFields(settings);
                    break;
                case ObjectAIAction.Attack:
                    settings.attackPattern = (ObjectAttackPattern)EditorGUILayout.EnumPopup(
                        "공격 방식", settings.attackPattern);
                    DrawAttackFields(settings, settings.attackPattern);
                    break;
                case ObjectAIAction.Charge:
                    DrawForcedAttackLabel("몸통 돌진");
                    DrawAttackFields(settings, ObjectAttackPattern.Charge);
                    break;
                case ObjectAIAction.Shoot:
                    DrawForcedAttackLabel("단발 투사체");
                    DrawAttackFields(settings, ObjectAttackPattern.SingleProjectile);
                    break;
                case ObjectAIAction.BurstShoot:
                    DrawForcedAttackLabel("부채꼴 3연발");
                    DrawAttackFields(settings, ObjectAttackPattern.TripleProjectile);
                    break;
                case ObjectAIAction.AreaAttack:
                    DrawForcedAttackLabel("주변 범위 공격");
                    DrawAttackFields(settings, ObjectAttackPattern.AreaPulse);
                    break;
                case ObjectAIAction.AlertAllies:
                    settings.allyAlertRadius = EditorGUILayout.FloatField(
                        "동료 경보 반경", settings.allyAlertRadius);
                    break;
                case ObjectAIAction.TeleportBehind:
                    settings.teleportDistance = EditorGUILayout.FloatField(
                        "타겟 뒤쪽 거리", settings.teleportDistance);
                    settings.teleportCooldown = EditorGUILayout.FloatField(
                        "순간이동 재사용 시간", settings.teleportCooldown);
                    settings.maxDistanceFromSpawn = EditorGUILayout.FloatField(
                        "스폰 위치 기준 최대 거리", settings.maxDistanceFromSpawn);
                    break;
                case ObjectAIAction.SelfDestruct:
                    settings.damage = EditorGUILayout.IntField("범위 피해", settings.damage);
                    settings.attackRange = EditorGUILayout.FloatField("폭발 범위", settings.attackRange);
                    settings.targetInvulnerabilitySeconds = EditorGUILayout.FloatField(
                        "타겟 피격 무적", settings.targetInvulnerabilitySeconds);
                    settings.knockback = EditorGUILayout.FloatField("넉백", settings.knockback);
                    break;
                case ObjectAIAction.PlayMotion:
                    settings.motion = (ObjectMotionCondition)EditorGUILayout.EnumPopup(
                        "재생할 모션 상황", settings.motion);
                    break;
            }
        }

        private void DrawAttackFields(ObjectAIActionParameters settings, ObjectAttackPattern pattern)
        {
            settings.damage = EditorGUILayout.IntField("피해", settings.damage);
            settings.attackRange = EditorGUILayout.FloatField("공격 거리", settings.attackRange);
            settings.prepareSeconds = EditorGUILayout.FloatField("공격 준비", settings.prepareSeconds);
            settings.activeSeconds = EditorGUILayout.FloatField("공격 판정", settings.activeSeconds);
            settings.recoverySeconds = EditorGUILayout.FloatField("공격 후 경직", settings.recoverySeconds);
            settings.cooldownSeconds = EditorGUILayout.FloatField("재공격 대기", settings.cooldownSeconds);
            settings.hitboxHeight = EditorGUILayout.FloatField("수직 판정 높이", settings.hitboxHeight);
            settings.targetInvulnerabilitySeconds = EditorGUILayout.FloatField(
                "타겟 피격 무적", settings.targetInvulnerabilitySeconds);
            settings.knockback = EditorGUILayout.FloatField("넉백", settings.knockback);

            if (ObjectBehaviorCatalog.IsRangedAttack(pattern))
            {
                settings.projectileSpeed = EditorGUILayout.FloatField(
                    "투사체 속도", settings.projectileSpeed);
                settings.projectileLifetime = EditorGUILayout.FloatField(
                    "투사체 수명", settings.projectileLifetime);
            }
            if (pattern == ObjectAttackPattern.Charge)
                settings.dashSpeedMultiplier = EditorGUILayout.FloatField(
                    "돌진 속도 배율", settings.dashSpeedMultiplier);
            if (pattern == ObjectAttackPattern.LeapStrike)
                settings.jumpForce = EditorGUILayout.FloatField("도약 힘", settings.jumpForce);
        }

        private static void DrawForcedAttackLabel(string label)
        {
            EditorGUILayout.LabelField("공격 방식", label);
        }

        private void DrawTerrainFields(ObjectAIActionParameters settings)
        {
            settings.groundMovement = EditorGUILayout.Toggle("지상 이동", settings.groundMovement);
            if (!settings.groundMovement)
                return;
            DrawTerrainProbeFields(settings);
            settings.obstacleResponse = (ObjectAITerrainResponse)EditorGUILayout.EnumPopup(
                "높은 장애물 대응", settings.obstacleResponse);
            settings.cliffResponse = (ObjectAITerrainResponse)EditorGUILayout.EnumPopup(
                "낭떠러지 대응", settings.cliffResponse);
        }

        private void DrawTerrainProbeFields(ObjectAIActionParameters settings)
        {
            settings.smallStepHeight = EditorGUILayout.FloatField(
                "자동으로 넘을 작은 턱", settings.smallStepHeight);
            settings.obstacleProbeDistance = EditorGUILayout.FloatField(
                "앞쪽 장애물 검사 거리", settings.obstacleProbeDistance);
            settings.cliffProbeDistance = EditorGUILayout.FloatField(
                "아래쪽 지면 검사 거리", settings.cliffProbeDistance);
            settings.groundMask = DrawLayerMask("지면/장애물 레이어", settings.groundMask);
        }

        private void DrawRuleEffect(ObjectAIRule rule)
        {
            GUILayout.Space(3f);
            rule.effect = (ObjectRuleEffect)EditorGUILayout.EnumPopup("이때 연출", rule.effect);
            if (rule.effect == ObjectRuleEffect.None)
                return;
            rule.effectSettings.primaryColor = EditorGUILayout.ColorField(
                "연출 주 색상", rule.effectSettings.primaryColor);
            rule.effectSettings.secondaryColor = EditorGUILayout.ColorField(
                "연출 보조 색상", rule.effectSettings.secondaryColor);
            rule.effectSettings.scale = EditorGUILayout.Slider(
                "연출 크기", rule.effectSettings.scale, 0.1f, 3f);
            rule.effectSettings.duration = EditorGUILayout.Slider(
                "연출 시간", rule.effectSettings.duration, 0.1f, 3f);
            rule.effectSettings.amount = EditorGUILayout.IntSlider(
                "연출 입자 수", rule.effectSettings.amount, 1, 32);
        }

        private ObjectSoundCue2D DrawSoundCue(
            string label,
            ObjectSoundCue2D cue,
            bool allowLoop,
            Action<Rect> captureObjectFieldRect = null)
        {
            cue = cue ?? new ObjectSoundCue2D();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            cue.audioClip = (AudioClip)EditorGUILayout.ObjectField(
                "소리 파일",
                cue.audioClip,
                typeof(AudioClip),
                false);
            captureObjectFieldRect?.Invoke(ToWindowRect(GUILayoutUtility.GetLastRect()));
            if (cue.audioClip != null)
            {
                cue.delaySeconds = EditorGUILayout.FloatField(
                    "시작 딜레이", cue.delaySeconds);
                cue.volume = EditorGUILayout.Slider("볼륨", cue.volume, 0f, 1f);
                if (allowLoop)
                    cue.isLoop = EditorGUILayout.Toggle("행동 중 반복", cue.isLoop);
                else
                    cue.isLoop = false;
            }
            else if (!allowLoop)
            {
                cue.isLoop = false;
            }
            cue.Sanitize();
            EditorGUILayout.EndVertical();
            return cue;
        }

        private void DrawContactDamage()
        {
            showContactDamage = EditorGUILayout.BeginFoldoutHeaderGroup(
                showContactDamage,
                "몸체 접촉 피해 (AI 규칙과 별도)");
            if (showContactDamage)
            {
                EditorGUILayout.HelpBox(
                    "AI 행동과 관계없이 Collider2D가 플레이어 몸체와 닿아 있는 동안 적용되는 공통 물리 피해입니다.",
                    MessageType.Info);
                draft.attack.contactDamage = EditorGUILayout.IntField(
                    "접촉 피해", draft.attack.contactDamage);
                draft.attack.contactKnockback = EditorGUILayout.FloatField(
                    "접촉 넉백", draft.attack.contactKnockback);
                draft.attack.targetInvulnerabilitySeconds = EditorGUILayout.FloatField(
                    "피격 무적 시간", draft.attack.targetInvulnerabilitySeconds);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMovement()
        {
            showMovement = EditorGUILayout.BeginFoldoutHeaderGroup(showMovement, "이동 / 지형 대응");
            if (showMovement)
            {
                draft.movement.groundWalker = EditorGUILayout.Toggle("Ground Walker", draft.movement.groundWalker);
                draft.movement.patrolMoveSeconds = DrawRange("Move Time", draft.movement.patrolMoveSeconds);
                draft.movement.idleSeconds = DrawRange("Idle Time", draft.movement.idleSeconds);
                draft.movement.turnChanceOnMove = EditorGUILayout.Slider(
                    "Turn Chance", draft.movement.turnChanceOnMove, 0f, 1f);
                draft.movement.smallStepHeight = EditorGUILayout.FloatField(
                    "Small Step Height", draft.movement.smallStepHeight);
                draft.movement.obstacleProbeDistance = EditorGUILayout.FloatField(
                    "Obstacle Probe", draft.movement.obstacleProbeDistance);
                draft.movement.cliffProbeDistance = EditorGUILayout.FloatField(
                    "Cliff Probe", draft.movement.cliffProbeDistance);
                draft.movement.groundMask = DrawLayerMask("Ground Layers", draft.movement.groundMask);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDetection()
        {
            showDetection = EditorGUILayout.BeginFoldoutHeaderGroup(showDetection, "감지");
            if (showDetection)
            {
                draft.detection.frontDistance = EditorGUILayout.FloatField(
                    "Front Distance", draft.detection.frontDistance);
                draft.detection.rearDistance = EditorGUILayout.FloatField(
                    "Rear Distance", draft.detection.rearDistance);
                draft.detection.verticalRange = EditorGUILayout.FloatField(
                    "Vertical Range", draft.detection.verticalRange);
                draft.detection.targetMask = DrawLayerMask("Target Layers", draft.detection.targetMask);
                draft.detection.requireLineOfSight = EditorGUILayout.Toggle(
                    "Require Line Of Sight", draft.detection.requireLineOfSight);
                if (draft.detection.requireLineOfSight)
                    draft.detection.sightBlockingMask = DrawLayerMask(
                        "Sight Blocking Layers", draft.detection.sightBlockingMask);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawChase()
        {
            showChase = EditorGUILayout.BeginFoldoutHeaderGroup(showChase, "추격 / 귀환");
            if (showChase)
            {
                draft.chase.minimumChaseSeconds = EditorGUILayout.FloatField(
                    "Minimum Chase", draft.chase.minimumChaseSeconds);
                draft.chase.detectionMemorySeconds = EditorGUILayout.FloatField(
                    "Detection Memory", draft.chase.detectionMemorySeconds);
                draft.chase.maxDistanceFromSpawn = EditorGUILayout.FloatField(
                    "Max From Spawn", draft.chase.maxDistanceFromSpawn);
                draft.chase.giveUpTargetDistance = EditorGUILayout.FloatField(
                    "Give Up Distance", draft.chase.giveUpTargetDistance);
                draft.chase.speedMultiplier = EditorGUILayout.FloatField(
                    "Chase Speed Multiplier", draft.chase.speedMultiplier);
                draft.chase.lostSightChaseSeconds = EditorGUILayout.FloatField(
                    "Lost Sight Chase", draft.chase.lostSightChaseSeconds);
                draft.chase.unreachableLookSeconds = EditorGUILayout.FloatField(
                    "Other Platform Look", draft.chase.unreachableLookSeconds);
                draft.chase.returnTolerance = EditorGUILayout.FloatField(
                    "Return Tolerance", draft.chase.returnTolerance);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAttack()
        {
            showAttack = EditorGUILayout.BeginFoldoutHeaderGroup(showAttack, "공격 / 접촉 피해");
            if (showAttack)
            {
                draft.attack.attackDamage = EditorGUILayout.IntField("Attack Damage", draft.attack.attackDamage);
                draft.attack.attackRange = EditorGUILayout.FloatField("Attack Range", draft.attack.attackRange);
                draft.attack.contactDamage = EditorGUILayout.IntField("Contact Damage", draft.attack.contactDamage);
                draft.attack.contactKnockback = EditorGUILayout.FloatField(
                    "Contact Knockback", draft.attack.contactKnockback);
                draft.attack.targetInvulnerabilitySeconds = EditorGUILayout.FloatField(
                    "Target Invulnerability", draft.attack.targetInvulnerabilitySeconds);
                draft.attack.prepareSeconds = EditorGUILayout.FloatField(
                    "Prepare", draft.attack.prepareSeconds);
                draft.attack.activeSeconds = EditorGUILayout.FloatField(
                    "Active", draft.attack.activeSeconds);
                draft.attack.recoverySeconds = EditorGUILayout.FloatField(
                    "Recovery", draft.attack.recoverySeconds);
                draft.attack.cooldownSeconds = EditorGUILayout.FloatField(
                    "Cooldown", draft.attack.cooldownSeconds);
                draft.attack.hitboxHeight = EditorGUILayout.FloatField(
                    "Hitbox Height", draft.attack.hitboxHeight);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawHit()
        {
            showHit = EditorGUILayout.BeginFoldoutHeaderGroup(showHit, "피격");
            if (showHit)
            {
                draft.hit.interruptCurrentAction = EditorGUILayout.Toggle(
                    "Interrupt Action", draft.hit.interruptCurrentAction);
                draft.hit.hitStopSeconds = EditorGUILayout.FloatField("Hit Stop", draft.hit.hitStopSeconds);
                draft.hit.knockback = EditorGUILayout.FloatField("Knockback", draft.hit.knockback);
                draft.hit.blinkSeconds = EditorGUILayout.FloatField("Blink Duration", draft.hit.blinkSeconds);
                draft.hit.blinkCount = EditorGUILayout.IntField("Blink Count", draft.hit.blinkCount);
                draft.hit.sound = DrawSoundCue(
                    "피격 소리",
                    draft.hit.sound,
                    false,
                    rect => LastHitSoundRectForTests = rect);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDestruction()
        {
            showDestruction = EditorGUILayout.BeginFoldoutHeaderGroup(showDestruction, "HP 0 / 제거 연출");
            if (showDestruction)
            {
                EditorGUILayout.HelpBox(
                    "HP가 0이 되면 본체는 연출 재생 시간과 관계없이 즉시 사라지고 제거됩니다.",
                    MessageType.Info);
                draft.destruction.effectPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Parts / Effect Prefab",
                    draft.destruction.effectPrefab,
                    typeof(GameObject),
                    false);
                draft.destruction.disableCollisionsImmediately = EditorGUILayout.Toggle(
                    "Disable Collision At 0 HP",
                    draft.destruction.disableCollisionsImmediately);
                draft.destruction.sound = DrawSoundCue(
                    "HP 0 / 제거 소리",
                    draft.destruction.sound,
                    false,
                    rect => LastDestructionSoundRectForTests = rect);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawMotions()
        {
            showMotions = EditorGUILayout.BeginFoldoutHeaderGroup(showMotions, "상황별 모션");
            if (showMotions)
            {
                EditorGUILayout.HelpBox(
                    "Condition이 발생하면 같은 행의 Animator State를 재생합니다. State가 비어 있으면 그 상황은 모션을 바꾸지 않습니다.",
                    MessageType.Info);
                for (int i = 0; i < draft.motions.Count; i++)
                {
                    ObjectMotionBinding motion = draft.motions[i] ?? new ObjectMotionBinding();
                    draft.motions[i] = motion;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    motion.condition = (ObjectMotionCondition)EditorGUILayout.EnumPopup(
                        motion.condition,
                        GUILayout.Width(120f));
                    motion.animatorState = EditorGUILayout.TextField(motion.animatorState);
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        draft.motions.RemoveAt(i);
                        i--;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();
                    motion.crossFadeSeconds = EditorGUILayout.FloatField(
                        "Cross Fade", motion.crossFadeSeconds);
                    motion.restartWhenEntered = EditorGUILayout.Toggle(
                        "Restart On Enter", motion.restartWhenEntered);
                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("Add Motion Binding"))
                    draft.motions.Add(new ObjectMotionBinding());
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawSceneSpawns()
        {
            showSceneSpawns = EditorGUILayout.BeginFoldoutHeaderGroup(
                showSceneSpawns,
                "멀티씬 자동 배치");
            if (showSceneSpawns)
            {
                EditorGUILayout.HelpBox(
                    "씬 이름이 일치하면 Additive 로드를 포함해 프리팹을 자동 생성합니다. 비워 두면 수동으로 배치합니다.",
                    MessageType.Info);
                for (int i = 0; i < draft.sceneSpawns.Count; i++)
                {
                    ObjectSceneSpawnRule rule = draft.sceneSpawns[i] ?? new ObjectSceneSpawnRule();
                    draft.sceneSpawns[i] = rule;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(18f));
                    rule.sceneName = EditorGUILayout.TextField("Scene", rule.sceneName);
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        draft.sceneSpawns.RemoveAt(i);
                        i--;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();
                    rule.position = EditorGUILayout.Vector3Field("Position", rule.position);
                    rule.eulerAngles = EditorGUILayout.Vector3Field("Rotation", rule.eulerAngles);
                    rule.count = EditorGUILayout.IntField("Count", rule.count);
                    rule.spacing = EditorGUILayout.Vector3Field("Spacing", rule.spacing);
                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("Add Scene Spawn Rule"))
                    draft.sceneSpawns.Add(new ObjectSceneSpawnRule());
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawBuildArea()
        {
            GUILayout.Space(10f);
            draft.Sanitize();
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);

            GUI.backgroundColor = new Color(0.45f, 0.9f, 0.55f);
            bool modify = currentDefinition != null;
            string buttonLabel = modify
                ? "Modify (Definition + Prefab + Animator)"
                : "Make (Definition + Prefab + Animator)";
            LastBuildButtonLabelForTests = buttonLabel;
            if (GUILayout.Button(new GUIContent(
                    buttonLabel,
                    modify
                        ? "불러온 Definition, 같은 Prefab, 같은 Animator를 현재 설정으로 다시 정의합니다. 교체한 이미지와 노드에 넣은 모션은 유지됩니다."
                        : "현재 설정의 Definition과 공용 Animator는 Assets/Resources/2DObjectMaker에, Prefab은 Assets/Resources/Prefabs/Enemy 또는 Player에 생성합니다."),
                GUILayout.Height(38f)))
            {
                if (modify)
                    ModifyCurrent();
                else
                    MakeNew();
            }
            LastMakeButtonRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
            GUI.backgroundColor = Color.white;
            GUILayout.Space(8f);
        }

        private Rect ToWindowRect(Rect guiRect)
        {
            Rect screenRect = GUIUtility.GUIToScreenRect(guiRect);
            screenRect.position -= position.position;
            return screenRect;
        }

        private void MakeNew()
        {
            try
            {
                ObjectMakerBuildResult result = ObjectMakerPrefabBuilder.Make(draft);
                currentDefinition = result.Definition;
                Selection.activeObject = result.Definition;
                EditorGUIUtility.PingObject(result.Prefab);
                statusMessage = "생성 완료: " + result.PrefabPath + " / " +
                                result.AnimatorControllerPath;
            }
            catch (Exception exception)
            {
                statusMessage = "생성 실패: " + exception.Message;
                Debug.LogError("[2DObjectMaker] 만들기 실패\n" + exception);
            }
        }

        private void ModifyCurrent()
        {
            if (currentDefinition == null)
                return;

            try
            {
                ObjectMakerBuildResult result = ObjectMakerPrefabBuilder.Make(draft, currentDefinition);
                currentDefinition = result.Definition;
                Selection.activeObject = result.Definition;
                statusMessage = "Modify 완료: " + result.PrefabPath + " / " +
                                result.AnimatorControllerPath;
            }
            catch (Exception exception)
            {
                statusMessage = "Modify 실패: " + exception.Message;
                Debug.LogError("[2DObjectMaker] Modify 실패\n" + exception);
            }
        }

        private void LoadObject(UnityEngine.Object source)
        {
            ObjectDefinitionSO definition = ResolveDefinition(source);
            if (definition == null)
            {
                statusMessage = "불러올 수 없습니다. 2DObjectMaker가 만든 Prefab 또는 Object Definition SO를 넣어주세요.";
                Repaint();
                return;
            }
            LoadDefinition(definition);
        }

        public static ObjectDefinitionSO ResolveDefinition(UnityEngine.Object source)
        {
            ObjectDefinitionSO definition = source as ObjectDefinitionSO;
            if (definition != null)
                return definition;

            GameObject gameObject = source as GameObject;
            if (gameObject == null)
            {
                Component component = source as Component;
                if (component != null)
                    gameObject = component.gameObject;
            }
            if (gameObject == null)
                return null;

            ObjectActor2D actor = gameObject.GetComponent<ObjectActor2D>();
            if (actor == null)
                actor = gameObject.GetComponentInParent<ObjectActor2D>(true);
            if (actor == null)
                actor = gameObject.GetComponentInChildren<ObjectActor2D>(true);
            return actor != null ? actor.Definition : null;
        }

        private void LoadDefinition(ObjectDefinitionSO definition)
        {
            currentDefinition = definition;
            draft = definition != null ? definition.Data.Clone() : ObjectDefinitionData.CreateSG001();
            PrepareDraftForRuleEditing();
            statusMessage = definition != null
                ? "Modify 대상으로 불러옴: " + AssetDatabase.GetAssetPath(definition) +
                  (definition.GeneratedPrefab != null
                      ? " / " + AssetDatabase.GetAssetPath(definition.GeneratedPrefab)
                      : string.Empty)
                : "SG-001 기본값이 준비되었습니다.";
            Repaint();
        }

        private void PrepareDraftForRuleEditing()
        {
            if (draft == null)
                draft = ObjectDefinitionData.CreateSG001();
            draft.Sanitize();
            ObjectBehaviorCatalog.ImportLegacyTuning(draft);
        }

        private static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
            if (sprite == null)
            {
                GUI.Label(rect, "Drop Sprite", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Texture2D texture = AssetPreview.GetAssetPreview(sprite);
            if (texture == null)
                texture = AssetPreview.GetMiniThumbnail(sprite);
            if (texture != null)
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        private static Vector2 DrawRange(string label, Vector2 value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            value.x = EditorGUILayout.FloatField(value.x);
            GUILayout.Label("~", GUILayout.Width(12f));
            value.y = EditorGUILayout.FloatField(value.y);
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private static LayerMask DrawLayerMask(string label, LayerMask selected)
        {
            string[] layers = InternalEditorUtility.layers;
            int compactMask = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                int layer = LayerMask.NameToLayer(layers[i]);
                if (layer >= 0 && (selected.value & (1 << layer)) != 0)
                    compactMask |= 1 << i;
            }

            compactMask = EditorGUILayout.MaskField(label, compactMask, layers);
            int fullMask = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if ((compactMask & (1 << i)) == 0)
                    continue;
                int layer = LayerMask.NameToLayer(layers[i]);
                if (layer >= 0)
                    fullMask |= 1 << layer;
            }

            selected.value = fullMask;
            return selected;
        }
    }
}
