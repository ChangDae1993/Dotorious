using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JYW.Game.ObjectMaker
{
    public static class ObjectCommonAnimator
    {
        public const string Idle = "Idle";
        public const string Walk = "Walk";
        public const string Run = "Run";
        public const string Jump = "Jump";
        public const string Attack = "Attack";
        public const string Hit = "Hit";
        public const string Dead = "Dead";
    }

    public static class ObjectAnimatorGraph2D
    {
        public const string SpeedParameter = "MoveSpeed";
        public const string RunningParameter = "IsRunning";
        public const string GroundedParameter = "IsGrounded";
        public const string AttackingParameter = "IsAttacking";
        public const string PlayerAttackParameter = "PlayerAttack";
        public const string PlayerComboParameter = "PlayerCombo";
        public const string EnemyRuleParameter = "EnemyRule";
        public const string EnemyAttackPhaseParameter = "EnemyAttackPhase";
        public const string JumpParameter = "Jump";
        public const string HitParameter = "Hit";
        public const string DeadParameter = "Dead";

        public static string PlayerAttackState(int attackIndex, int comboIndex)
        {
            return string.Format(
                "Player_Attack_{0:00}_Combo_{1:00}",
                Mathf.Max(0, attackIndex) + 1,
                Mathf.Max(0, comboIndex) + 1);
        }

        [Obsolete("Generated controllers use PlayerAttack/PlayerCombo integer parameters.")]
        public static string PlayerAttackTrigger(int attackIndex, int comboIndex)
        {
            return "Do_" + PlayerAttackState(attackIndex, comboIndex);
        }

        public static string EnemyRuleState(int ruleIndex, ObjectAIAction action)
        {
            return string.Format(
                "Enemy_Rule_{0:00}_{1}",
                Mathf.Max(0, ruleIndex) + 1,
                action);
        }

        public static string EnemyRuleAttackState(
            int ruleIndex,
            ObjectAIAction action,
            ObjectMotionCondition phase)
        {
            return EnemyRuleState(ruleIndex, action) + "_" + phase;
        }

        [Obsolete("Generated controllers use the EnemyRule integer parameter.")]
        public static string EnemyRuleTrigger(int ruleIndex)
        {
            return string.Format("Do_Enemy_Rule_{0:00}", Mathf.Max(0, ruleIndex) + 1);
        }

    }

    public enum ObjectEnemyAnimatorPhase
    {
        None = 0,
        Prepare = 1,
        Active = 2,
        Recover = 3
    }

    public enum ObjectKind
    {
        Monster,
        Player,
        Neutral
    }

    public enum ObjectMotionCondition
    {
        Idle,
        Patrol,
        Alert,
        Chase,
        Return,
        AttackPrepare,
        Attack,
        AttackRecover,
        Hit,
        Death,
        Jump
    }

    [Serializable]
    public sealed class ObjectPlayerAttack
    {
        public string displayName = "Attack 1";
        public Key key = Key.J;
        [Min(0)] public int damage = 1;
        [Min(0f)] public float inputDelaySeconds = 0.1f;
        [Min(0f)] public float activeSeconds = 0.15f;
        [Min(0f)] public float recoverySeconds = 0.25f;
        [Min(0f)] public float cooldownSeconds = 0.4f;
        [Min(0f)] public float range = 1.2f;
        [Min(0.01f)] public float hitboxHeight = 1.2f;
        [Min(0f)] public float knockback = 0.2f;
        [Min(0f)] public float targetInvulnerabilitySeconds = 0.1f;
        [Range(1, 12)] public int comboSteps = 1;
        [Min(0f)] public float comboInputWindowSeconds = 0.35f;
        [Min(0f)] public float comboResetSeconds = 0.8f;

        public ObjectPlayerAttack Clone()
        {
            return (ObjectPlayerAttack)MemberwiseClone();
        }

        public void Sanitize(int index)
        {
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? "Attack " + (index + 1)
                : displayName.Trim();
            damage = Mathf.Max(0, damage);
            inputDelaySeconds = Mathf.Max(0f, inputDelaySeconds);
            activeSeconds = Mathf.Max(0f, activeSeconds);
            recoverySeconds = Mathf.Max(0f, recoverySeconds);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            range = Mathf.Max(0f, range);
            hitboxHeight = Mathf.Max(0.01f, hitboxHeight);
            knockback = Mathf.Max(0f, knockback);
            targetInvulnerabilitySeconds = Mathf.Max(0f, targetInvulnerabilitySeconds);
            comboSteps = Mathf.Clamp(comboSteps, 1, 12);
            comboInputWindowSeconds = Mathf.Max(0f, comboInputWindowSeconds);
            comboResetSeconds = Mathf.Max(0f, comboResetSeconds);
        }
    }

    [Serializable]
    public sealed class ObjectPlayerSettings
    {
        public Key moveLeftKey = Key.A;
        public Key moveRightKey = Key.D;
        public Key runKey = Key.LeftShift;
        [Min(1f)] public float runSpeedMultiplier = 1.5f;
        public Key jumpKey = Key.Space;
        [Min(0f)] public float jumpForce = 7f;
        [Min(0.01f)] public float groundCheckRadius = 0.12f;
        public LayerMask groundMask = ~0;
        public bool allowMovementDuringAttack;
        public List<ObjectPlayerAttack> attacks = CreateDefaultAttacks();

        public ObjectPlayerSettings Clone()
        {
            var clone = (ObjectPlayerSettings)MemberwiseClone();
            clone.attacks = new List<ObjectPlayerAttack>();
            if (attacks != null)
            {
                for (int i = 0; i < attacks.Count; i++)
                {
                    if (attacks[i] != null)
                        clone.attacks.Add(attacks[i].Clone());
                }
            }
            return clone;
        }

        public void Sanitize()
        {
            runSpeedMultiplier = Mathf.Max(1f, runSpeedMultiplier);
            jumpForce = Mathf.Max(0f, jumpForce);
            groundCheckRadius = Mathf.Max(0.01f, groundCheckRadius);
            attacks = attacks ?? new List<ObjectPlayerAttack>();
            for (int i = 0; i < attacks.Count; i++)
            {
                if (attacks[i] == null)
                    attacks[i] = new ObjectPlayerAttack();
                attacks[i].Sanitize(i);
            }
        }

        public static List<ObjectPlayerAttack> CreateDefaultAttacks()
        {
            return new List<ObjectPlayerAttack> { new ObjectPlayerAttack() };
        }
    }

    [Serializable]
    public sealed class ObjectCoreStats
    {
        [Min(1)] public int maxHealth = 1;
        [Min(0)] public int attackPower = 1;
        [Min(0)] public int defense;
        [Min(0f)] public float moveSpeed = 0.5f;

        public ObjectCoreStats Clone()
        {
            return (ObjectCoreStats)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectMovementSettings
    {
        public bool groundWalker = true;
        public Vector2 patrolMoveSeconds = new Vector2(1.5f, 3f);
        public Vector2 idleSeconds = new Vector2(0.8f, 1.8f);
        [Range(0f, 1f)] public float turnChanceOnMove = 0.35f;
        [Min(0f)] public float smallStepHeight = 0.35f;
        [Min(0.01f)] public float obstacleProbeDistance = 0.18f;
        [Min(0.01f)] public float cliffProbeDistance = 0.6f;
        public LayerMask groundMask = ~0;

        public ObjectMovementSettings Clone()
        {
            return (ObjectMovementSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectDetectionSettings
    {
        [Min(0f)] public float frontDistance = 5f;
        [Min(0f)] public float rearDistance = 1.5f;
        [Min(0f)] public float verticalRange = 2f;
        public LayerMask targetMask = ~0;
        public bool requireLineOfSight;
        public LayerMask sightBlockingMask = ~0;

        public ObjectDetectionSettings Clone()
        {
            return (ObjectDetectionSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectChaseSettings
    {
        [Min(0f)] public float minimumChaseSeconds = 1f;
        [Min(0f)] public float detectionMemorySeconds = 3f;
        [Min(0f)] public float maxDistanceFromSpawn = 8f;
        [Min(0f)] public float giveUpTargetDistance = 7f;
        [Min(0f)] public float speedMultiplier = 1.4f;
        [Min(0f)] public float lostSightChaseSeconds = 3f;
        [Min(0f)] public float unreachableLookSeconds = 1.5f;
        [Min(0.01f)] public float returnTolerance = 0.15f;

        public ObjectChaseSettings Clone()
        {
            return (ObjectChaseSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectAttackSettings
    {
        [Min(0)] public int attackDamage = 1;
        [Min(0f)] public float attackRange = 1.2f;
        [Min(0)] public int contactDamage = 1;
        [Min(0f)] public float contactKnockback = 0.2f;
        [Min(0f)] public float targetInvulnerabilitySeconds = 1f;
        [Min(0f)] public float prepareSeconds = 0.5f;
        [Min(0f)] public float activeSeconds = 0.15f;
        [Min(0f)] public float recoverySeconds = 0.7f;
        [Min(0f)] public float cooldownSeconds = 0.8f;
        [Min(0.01f)] public float hitboxHeight = 1.2f;

        public ObjectAttackSettings Clone()
        {
            return (ObjectAttackSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectHitSettings
    {
        public bool interruptCurrentAction = true;
        [Min(0f)] public float hitStopSeconds = 0.05f;
        [Min(0f)] public float knockback = 0.2f;
        [Min(0f)] public float blinkSeconds = 0.18f;
        [Min(1)] public int blinkCount = 2;

        public ObjectHitSettings Clone()
        {
            return (ObjectHitSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectDestructionSettings
    {
        public GameObject effectPrefab;
        public bool disableCollisionsImmediately = true;
        [Min(0f)] public float destroyDelaySeconds = 0.8f;

        public ObjectDestructionSettings Clone()
        {
            return (ObjectDestructionSettings)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectMotionBinding
    {
        public ObjectMotionCondition condition;
        public string animatorState = string.Empty;
        [Min(0f)] public float crossFadeSeconds = 0.05f;
        public bool restartWhenEntered = true;

        public ObjectMotionBinding Clone()
        {
            return (ObjectMotionBinding)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectSceneSpawnRule
    {
        public bool enabled = true;
        public string sceneName = string.Empty;
        public Vector3 position;
        public Vector3 eulerAngles;
        [Min(1)] public int count = 1;
        public Vector3 spacing = new Vector3(1.5f, 0f, 0f);

        public ObjectSceneSpawnRule Clone()
        {
            return (ObjectSceneSpawnRule)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class ObjectDefinitionData
    {
        public string objectId = "SG-001";
        public string displayName = "SG-001";
        public ObjectKind kind = ObjectKind.Monster;
        public string role = "기본 근접형 적";
        public string firstAppearance = "Act 1 초반";
        public Sprite previewSprite;
        public RuntimeAnimatorController animatorController;
        public bool facesRightByDefault = true;
        public ObjectCoreStats core = new ObjectCoreStats();
        public ObjectMovementSettings movement = new ObjectMovementSettings();
        public ObjectDetectionSettings detection = new ObjectDetectionSettings();
        public ObjectChaseSettings chase = new ObjectChaseSettings();
        public ObjectAttackSettings attack = new ObjectAttackSettings();
        public ObjectHitSettings hit = new ObjectHitSettings();
        public ObjectDestructionSettings destruction = new ObjectDestructionSettings();
        public List<ObjectMotionBinding> motions = CreateCommonMotions();
        public List<ObjectSceneSpawnRule> sceneSpawns = new List<ObjectSceneSpawnRule>();
        public ObjectAIProgram ai = new ObjectAIProgram();
        public ObjectPlayerSettings player = new ObjectPlayerSettings();

        public static ObjectDefinitionData CreateSG001()
        {
            return new ObjectDefinitionData();
        }

        public ObjectDefinitionData Clone()
        {
            var clone = (ObjectDefinitionData)MemberwiseClone();
            clone.core = core != null ? core.Clone() : new ObjectCoreStats();
            clone.movement = movement != null ? movement.Clone() : new ObjectMovementSettings();
            clone.detection = detection != null ? detection.Clone() : new ObjectDetectionSettings();
            clone.chase = chase != null ? chase.Clone() : new ObjectChaseSettings();
            clone.attack = attack != null ? attack.Clone() : new ObjectAttackSettings();
            clone.hit = hit != null ? hit.Clone() : new ObjectHitSettings();
            clone.destruction = destruction != null ? destruction.Clone() : new ObjectDestructionSettings();
            clone.ai = ai != null ? ai.Clone() : new ObjectAIProgram();
            clone.player = player != null ? player.Clone() : new ObjectPlayerSettings();
            clone.motions = new List<ObjectMotionBinding>();
            if (motions != null)
            {
                for (int i = 0; i < motions.Count; i++)
                    if (motions[i] != null)
                        clone.motions.Add(motions[i].Clone());
            }

            clone.sceneSpawns = new List<ObjectSceneSpawnRule>();
            if (sceneSpawns != null)
            {
                for (int i = 0; i < sceneSpawns.Count; i++)
                    if (sceneSpawns[i] != null)
                        clone.sceneSpawns.Add(sceneSpawns[i].Clone());
            }

            return clone;
        }

        public void Sanitize()
        {
            objectId = string.IsNullOrWhiteSpace(objectId) ? "Object" : objectId.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? objectId : displayName.Trim();
            role = role ?? string.Empty;
            firstAppearance = firstAppearance ?? string.Empty;

            core = core ?? new ObjectCoreStats();
            core.maxHealth = Mathf.Max(1, core.maxHealth);
            core.attackPower = Mathf.Max(0, core.attackPower);
            core.defense = Mathf.Max(0, core.defense);
            core.moveSpeed = Mathf.Max(0f, core.moveSpeed);

            movement = movement ?? new ObjectMovementSettings();
            movement.patrolMoveSeconds = OrderedNonNegative(movement.patrolMoveSeconds);
            movement.idleSeconds = OrderedNonNegative(movement.idleSeconds);
            movement.turnChanceOnMove = Mathf.Clamp01(movement.turnChanceOnMove);
            movement.smallStepHeight = Mathf.Max(0f, movement.smallStepHeight);
            movement.obstacleProbeDistance = Mathf.Max(0.01f, movement.obstacleProbeDistance);
            movement.cliffProbeDistance = Mathf.Max(0.01f, movement.cliffProbeDistance);

            detection = detection ?? new ObjectDetectionSettings();
            detection.frontDistance = Mathf.Max(0f, detection.frontDistance);
            detection.rearDistance = Mathf.Max(0f, detection.rearDistance);
            detection.verticalRange = Mathf.Max(0f, detection.verticalRange);

            chase = chase ?? new ObjectChaseSettings();
            chase.minimumChaseSeconds = Mathf.Max(0f, chase.minimumChaseSeconds);
            chase.detectionMemorySeconds = Mathf.Max(0f, chase.detectionMemorySeconds);
            chase.maxDistanceFromSpawn = Mathf.Max(0f, chase.maxDistanceFromSpawn);
            chase.giveUpTargetDistance = Mathf.Max(0f, chase.giveUpTargetDistance);
            chase.speedMultiplier = Mathf.Max(0f, chase.speedMultiplier);
            chase.lostSightChaseSeconds = Mathf.Max(0f, chase.lostSightChaseSeconds);
            chase.unreachableLookSeconds = Mathf.Max(0f, chase.unreachableLookSeconds);
            chase.returnTolerance = Mathf.Max(0.01f, chase.returnTolerance);

            attack = attack ?? new ObjectAttackSettings();
            attack.attackDamage = Mathf.Max(0, attack.attackDamage);
            attack.attackRange = Mathf.Max(0f, attack.attackRange);
            attack.contactDamage = Mathf.Max(0, attack.contactDamage);
            attack.contactKnockback = Mathf.Max(0f, attack.contactKnockback);
            attack.targetInvulnerabilitySeconds = Mathf.Max(0f, attack.targetInvulnerabilitySeconds);
            attack.prepareSeconds = Mathf.Max(0f, attack.prepareSeconds);
            attack.activeSeconds = Mathf.Max(0f, attack.activeSeconds);
            attack.recoverySeconds = Mathf.Max(0f, attack.recoverySeconds);
            attack.cooldownSeconds = Mathf.Max(0f, attack.cooldownSeconds);
            attack.hitboxHeight = Mathf.Max(0.01f, attack.hitboxHeight);

            hit = hit ?? new ObjectHitSettings();
            hit.hitStopSeconds = Mathf.Max(0f, hit.hitStopSeconds);
            hit.knockback = Mathf.Max(0f, hit.knockback);
            hit.blinkSeconds = Mathf.Max(0f, hit.blinkSeconds);
            hit.blinkCount = Mathf.Max(1, hit.blinkCount);

            destruction = destruction ?? new ObjectDestructionSettings();
            destruction.destroyDelaySeconds = Mathf.Max(0f, destruction.destroyDelaySeconds);

            ai = ai ?? new ObjectAIProgram();
            ai.Sanitize();

            player = player ?? new ObjectPlayerSettings();
            player.Sanitize();

            motions = motions ?? CreateCommonMotions();
            sceneSpawns = sceneSpawns ?? new List<ObjectSceneSpawnRule>();
            for (int i = 0; i < sceneSpawns.Count; i++)
            {
                if (sceneSpawns[i] == null)
                    sceneSpawns[i] = new ObjectSceneSpawnRule();
                sceneSpawns[i].sceneName = sceneSpawns[i].sceneName ?? string.Empty;
                sceneSpawns[i].count = Mathf.Max(1, sceneSpawns[i].count);
            }
        }

        private static Vector2 OrderedNonNegative(Vector2 value)
        {
            float x = Mathf.Max(0f, value.x);
            float y = Mathf.Max(0f, value.y);
            return x <= y ? new Vector2(x, y) : new Vector2(y, x);
        }

        public void UseCommonAnimatorLayout()
        {
            motions = new List<ObjectMotionBinding>
            {
                Motion(ObjectMotionCondition.Idle, ObjectCommonAnimator.Idle),
                Motion(ObjectMotionCondition.Hit, ObjectCommonAnimator.Hit),
                Motion(ObjectMotionCondition.Death, ObjectCommonAnimator.Dead)
            };

            if (kind == ObjectKind.Player)
            {
                bool hasMoveKey = core.moveSpeed > 0f &&
                                  (player.moveLeftKey != Key.None ||
                                   player.moveRightKey != Key.None);
                if (hasMoveKey)
                {
                    AddMotion(ObjectMotionCondition.Patrol, ObjectCommonAnimator.Walk);
                    AddMotion(
                        ObjectMotionCondition.Chase,
                        player.runKey != Key.None
                            ? ObjectCommonAnimator.Run
                            : ObjectCommonAnimator.Walk);
                    AddMotion(ObjectMotionCondition.Return, ObjectCommonAnimator.Walk);
                }
                if (player.jumpKey != Key.None && player.jumpForce > 0f)
                    AddMotion(ObjectMotionCondition.Jump, ObjectCommonAnimator.Jump);

                if (player.attacks != null)
                {
                    for (int i = 0; i < player.attacks.Count; i++)
                    {
                        ObjectPlayerAttack playerAttack = player.attacks[i];
                        if (playerAttack == null || playerAttack.key == Key.None)
                            continue;
                        string state = ObjectAnimatorGraph2D.PlayerAttackState(i, 0);
                        AddMotion(ObjectMotionCondition.AttackPrepare, state);
                        AddMotion(ObjectMotionCondition.Attack, state, false);
                        AddMotion(ObjectMotionCondition.AttackRecover, state, false);
                        break;
                    }
                }
                return;
            }

            if (kind != ObjectKind.Monster || ai == null || ai.rules == null)
                return;
            for (int i = 0; i < ai.rules.Count; i++)
            {
                ObjectAIRule rule = ai.rules[i];
                if (rule == null || !rule.enabled || rule.action == ObjectAIAction.None)
                    continue;

                if (ObjectBehaviorCatalog.IsAttackAction(rule.action))
                {
                    AddMotion(
                        ObjectMotionCondition.AttackPrepare,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            i, rule.action, ObjectMotionCondition.AttackPrepare));
                    AddMotion(
                        ObjectMotionCondition.Attack,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            i, rule.action, ObjectMotionCondition.Attack),
                        false);
                    AddMotion(
                        ObjectMotionCondition.AttackRecover,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            i, rule.action, ObjectMotionCondition.AttackRecover),
                        false);
                    continue;
                }

                ObjectMotionCondition condition = MotionConditionForAction(rule.action);
                AddMotion(
                    condition,
                    ObjectAnimatorGraph2D.EnemyRuleState(i, rule.action));
            }
        }

        private void AddMotion(
            ObjectMotionCondition condition,
            string state,
            bool restartWhenEntered = true)
        {
            for (int i = 0; i < motions.Count; i++)
            {
                if (motions[i] != null && motions[i].condition == condition)
                    return;
            }
            motions.Add(Motion(condition, state, restartWhenEntered));
        }

        private static ObjectMotionCondition MotionConditionForAction(ObjectAIAction action)
        {
            if (action == ObjectAIAction.Jump)
                return ObjectMotionCondition.Jump;
            if (action == ObjectAIAction.Patrol || action == ObjectAIAction.Wander ||
                action == ObjectAIAction.Hover)
                return ObjectMotionCondition.Patrol;
            if (action == ObjectAIAction.FollowTarget ||
                action == ObjectAIAction.FollowDamageSource ||
                action == ObjectAIAction.KeepDistance ||
                action == ObjectAIAction.FleeTarget)
                return ObjectMotionCondition.Chase;
            if (action == ObjectAIAction.ReturnToSpawn ||
                action == ObjectAIAction.SearchLastSeenThenReturn)
                return ObjectMotionCondition.Return;
            return ObjectMotionCondition.Alert;
        }

        public static List<ObjectMotionBinding> CreateCommonMotions()
        {
            return new List<ObjectMotionBinding>
            {
                Motion(ObjectMotionCondition.Idle, ObjectCommonAnimator.Idle),
                Motion(ObjectMotionCondition.Patrol, ObjectCommonAnimator.Walk),
                Motion(ObjectMotionCondition.Alert, ObjectCommonAnimator.Idle),
                Motion(ObjectMotionCondition.Chase, ObjectCommonAnimator.Run),
                Motion(ObjectMotionCondition.Return, ObjectCommonAnimator.Walk),
                Motion(ObjectMotionCondition.AttackPrepare, ObjectCommonAnimator.Attack),
                Motion(ObjectMotionCondition.Attack, ObjectCommonAnimator.Attack, false),
                Motion(ObjectMotionCondition.AttackRecover, ObjectCommonAnimator.Attack, false),
                Motion(ObjectMotionCondition.Hit, ObjectCommonAnimator.Hit),
                Motion(ObjectMotionCondition.Death, ObjectCommonAnimator.Dead),
                Motion(ObjectMotionCondition.Jump, ObjectCommonAnimator.Jump)
            };
        }

        private static ObjectMotionBinding Motion(
            ObjectMotionCondition condition,
            string state,
            bool restartWhenEntered = true)
        {
            return new ObjectMotionBinding
            {
                condition = condition,
                animatorState = state,
                restartWhenEntered = restartWhenEntered
            };
        }
    }

    [CreateAssetMenu(fileName = "ObjectDefinition", menuName = "2DObjectMaker/Object Definition")]
    public sealed class ObjectDefinitionSO : ScriptableObject
    {
        [SerializeField] private ObjectDefinitionData data = ObjectDefinitionData.CreateSG001();
        [SerializeField] private GameObject generatedPrefab;

        public ObjectDefinitionData Data
        {
            get
            {
                if (data == null)
                    data = ObjectDefinitionData.CreateSG001();
                return data;
            }
        }

        public GameObject GeneratedPrefab => generatedPrefab;

        public void ReplaceData(ObjectDefinitionData source)
        {
            data = source != null ? source.Clone() : ObjectDefinitionData.CreateSG001();
            data.Sanitize();
        }

        public void SetGeneratedPrefab(GameObject prefab)
        {
            generatedPrefab = prefab;
        }

        public void ResetToSG001()
        {
            data = ObjectDefinitionData.CreateSG001();
        }

        private void OnValidate()
        {
            Data.Sanitize();
        }
    }
}
