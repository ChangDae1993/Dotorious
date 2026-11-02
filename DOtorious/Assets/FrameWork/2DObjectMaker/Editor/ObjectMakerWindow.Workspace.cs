using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JYW.Game.ObjectMaker.Editor
{
    // The workspace reuses the individual authoring controls and the original builder.
    // No Presentation reference is needed from this assembly.
    public partial class ObjectMakerWindow
    {
        public static Func<UnityEngine.Object, ObjectMakerWindow> WorkspaceOpener;
        public static Action<ObjectDefinitionSO, ObjectDefinitionData> LegacyDraftImporter;
        private bool workspaceInspector;

        private void MigrateLegacyWindow()
        {
            if (this == null || GetType() != typeof(ObjectMakerWindow) || WorkspaceOpener == null || LegacyDraftImporter == null) return;
            // A saved old Editor layout may contain an unsaved draft. Transfer it before closing.
            try { LegacyDraftImporter(currentDefinition, draft.Clone()); Close(); }
            catch (Exception exception) { Debug.LogError("[AllinOneEditor] 기존 창의 초안을 유지합니다: " + exception); }
        }

        protected void DrawObjectProperties(ObjectWorkspaceDocument document)
        {
            currentDefinition = document.Source;
            draft = document.Data;
            switch (document.Section)
            {
                case "identity":
                    EditorGUILayout.LabelField(draft.kind == ObjectKind.Player ? "Player 기본 정보" : "Monster 기본 정보", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("제작 종류는 상단 영역으로 구분합니다. 다른 종류는 해당 영역에서 새로 만드세요.", MessageType.None);
                    draft.objectId = EditorGUILayout.TextField("Object ID", draft.objectId);
                    GUI.SetNextControlName("ObjectMaker.DisplayName");
                    draft.displayName = EditorGUILayout.TextField("이름", draft.displayName);
                    LastDisplayNameRectForTests = ToWindowRect(GUILayoutUtility.GetLastRect());
                    draft.role = EditorGUILayout.TextField("역할", draft.role);
                    draft.firstAppearance = EditorGUILayout.TextField("첫 등장", draft.firstAppearance);
                    draft.facesRightByDefault = EditorGUILayout.Toggle("이미지 기본 방향: 오른쪽", draft.facesRightByDefault);
                    break;
                case "stats": showCore = true; DrawCore(); break;
                case "controls":
                    workspaceInspector = true;
                    try { showPlayerControls = true; DrawPlayerControls(); }
                    finally { workspaceInspector = false; }
                    break;
                case "rule":
                    if (document.Index < 0 || document.Index >= draft.ai.rules.Count) break;
                    var rule = draft.ai.rules[document.Index];
                    rule.enabled = EditorGUILayout.Toggle("규칙 사용", rule.enabled);
                    rule.label = EditorGUILayout.TextField("규칙 이름", rule.label);
                    rule.when = DrawConditionDropdown(document.Index, rule.when);
                    EditorGUILayout.HelpBox(ObjectBehaviorCatalog.DescribeCondition(rule.when), MessageType.None);
                    DrawConditionFields(rule);
                    rule.conditionSound = DrawSoundCue("조건 발동 소리", rule.conditionSound, false);
                    GUILayout.Space(12);
                    rule.action = DrawActionDropdown(document.Index, rule.action);
                    EditorGUILayout.HelpBox(ObjectBehaviorCatalog.DescribeAction(rule.action), MessageType.None);
                    DrawActionFields(rule);
                    rule.actionSound = DrawSoundCue("행동 소리", rule.actionSound, true);
                    DrawRuleEffect(rule);
                    break;
                case "attack":
                    if (document.Index < 0 || document.Index >= draft.player.attacks.Count) break;
                    var attack = draft.player.attacks[document.Index];
                    attack.displayName = EditorGUILayout.TextField("공격 이름", attack.displayName);
                    DrawPlayerAttackFields(attack, document.Index);
                    break;
                case "contact": showContactDamage = true; DrawContactDamage(); break;
                case "hit": showHit = true; DrawHit(); break;
                case "destruction": showDestruction = true; DrawDestruction(); break;
                case "spawns": showSceneSpawns = true; DrawSceneSpawns(); break;
                case "motions": showMotions = true; DrawMotions(); break;
            }
        }

        public static string WorkspaceEnumLabel(Enum value) => GetEnumLabel(value);
        private void DrawPlayerAttackFields(ObjectPlayerAttack attack, int i)
        {
            EditorGUILayout.LabelField("입력 · 피해", EditorStyles.boldLabel);
            attack.key = (Key)EditorGUILayout.EnumPopup("공격 키", attack.key);
            attack.damage = EditorGUILayout.IntField("데미지", attack.damage);
            GUILayout.Space(8); EditorGUILayout.LabelField("공격 타이밍 · 모션", EditorStyles.boldLabel);
            attack.inputDelaySeconds = EditorGUILayout.FloatField(
                "공격 전 딜레이", attack.inputDelaySeconds);
            attack.activeSeconds = EditorGUILayout.FloatField(
                "공격 판정 지속시간", attack.activeSeconds);
            attack.recoverySeconds = EditorGUILayout.FloatField(
                "공격 후 딜레이", attack.recoverySeconds);
            attack.cooldownSeconds = EditorGUILayout.FloatField(
                "재사용 대기시간", attack.cooldownSeconds);
            attack.animationSpeed = EditorGUILayout.FloatField(
                new GUIContent("공격 모션 배속", "1은 기본 속도입니다. 공격 Animator State에만 적용하며 이동·점프 속도와 공격 판정시간은 바꾸지 않습니다."),
                attack.animationSpeed);
            GUILayout.Space(8); EditorGUILayout.LabelField("판정 · 스킬 연출", EditorStyles.boldLabel);
            attack.pattern = (ObjectPlayerAttackPattern)EditorGUILayout.EnumPopup("공격 방식", attack.pattern);
            if (attack.pattern != ObjectPlayerAttackPattern.Melee)
            {
                attack.effectPrefab = (GameObject)EditorGUILayout.ObjectField("스킬 연출 프리팹", attack.effectPrefab, typeof(GameObject), false);
                attack.effectOffset = EditorGUILayout.Vector2Field("연출 위치 (전방 / 높이)", attack.effectOffset);
                if (attack.pattern == ObjectPlayerAttackPattern.Projectile)
                {
                    attack.projectileSpeed = EditorGUILayout.FloatField("투사체 속도", attack.projectileSpeed);
                    attack.projectileLifetime = EditorGUILayout.FloatField("투사체 수명", attack.projectileLifetime);
                    attack.projectileRadius = EditorGUILayout.FloatField("투사체 반경", attack.projectileRadius);
                    attack.projectileGravity = EditorGUILayout.FloatField("투사체 중력", attack.projectileGravity);
                }
                if (attack.pattern == ObjectPlayerAttackPattern.Dash)
                    attack.dashSpeed = EditorGUILayout.FloatField("돌진 속도", attack.dashSpeed);
                if (attack.pattern == ObjectPlayerAttackPattern.Dash || attack.pattern == ObjectPlayerAttackPattern.Projectile)
                    attack.obstacleMask = DrawLayerMask("지형/장애물 레이어", attack.obstacleMask);
                EditorGUILayout.HelpBox("투사체/돌진은 지형에 막힙니다. 연출 프리팹은 SpriteRenderer + ObjectSkillVisual2D로 교체할 수 있습니다.", MessageType.None);
            }
            if (attack.pattern != ObjectPlayerAttackPattern.Projectile)
            {
                attack.range = EditorGUILayout.FloatField("공격 거리", attack.range);
                attack.hitboxHeight = EditorGUILayout.FloatField("공격 판정 높이", attack.hitboxHeight);
            }
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
            GUILayout.Space(8); EditorGUILayout.LabelField("콤보 연결", EditorStyles.boldLabel);
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

        }
    }
}
