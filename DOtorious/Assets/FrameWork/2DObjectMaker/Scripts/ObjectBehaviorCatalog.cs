using System;
using System.Collections.Generic;
using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    public enum ObjectAICondition
    {
        [InspectorName("게임 오브젝트가 생성됐을 때")] OnSpawn,
        [InspectorName("항상 / 다른 규칙이 없을 때")] Always,
        [InspectorName("타겟이 없을 때")] NoTarget,
        [InspectorName("타겟이 있을 때")] HasTarget,
        [InspectorName("플레이어를 감지했을 때")] TargetDetected,
        [InspectorName("플레이어를 처음 발견했을 때")] TargetEnteredDetection,
        [InspectorName("플레이어를 놓쳤을 때")] TargetLost,
        [InspectorName("타겟이 지정 거리 안일 때")] TargetWithinDistance,
        [InspectorName("타겟이 지정 거리 밖일 때")] TargetOutsideDistance,
        [InspectorName("타겟이 공격 거리 안일 때")] TargetWithinAttackRange,
        [InspectorName("피해를 받았을 때")] Damaged,
        [InspectorName("체력이 지정 비율 이하일 때")] LowHealth,
        [InspectorName("일정 시간이 지날 때마다")] EveryInterval,
        [InspectorName("스폰 위치에 도착했을 때")] AtSpawn,
        [InspectorName("스폰 위치에서 너무 멀 때")] FarFromSpawn,
        [InspectorName("앞에 높은 장애물이 있을 때")] ObstacleAhead,
        [InspectorName("앞이 낭떠러지일 때")] CliffAhead,
        [InspectorName("현재 행동이 끝났을 때")] ActionFinished,
        [InspectorName("체력이 0이 되어 파괴될 때")] Died,
        [InspectorName("타겟이 죽었을 때")] TargetDead,
        [InspectorName("동료에게 경보를 받았을 때")] AllyAlertReceived,
        [InspectorName("플레이어와 접촉했을 때")] ContactingPlayer,
        [InspectorName("지정 확률에 당첨됐을 때")] RandomChance
    }

    public enum ObjectAIAction
    {
        [InspectorName("아무것도 하지 않기")] None,
        [InspectorName("정지")] Stop,
        [InspectorName("일정 시간 기다리기")] Wait,
        [InspectorName("Patrol - 정찰")] Patrol,
        [InspectorName("무작위 배회")] Wander,
        [InspectorName("제자리 경계")] Guard,
        [InspectorName("타겟 따라가기")] FollowTarget,
        [InspectorName("피해를 준 대상 따라가기")] FollowDamageSource,
        [InspectorName("마지막 위치 수색 후 귀환")] SearchLastSeenThenReturn,
        [InspectorName("타겟과 거리 유지")] KeepDistance,
        [InspectorName("타겟 바라보기")] FaceTarget,
        [InspectorName("타겟에게서 도망가기")] FleeTarget,
        [InspectorName("스폰 위치로 귀환")] ReturnToSpawn,
        [InspectorName("반대 방향으로 돌기")] TurnAround,
        [InspectorName("좌우 무작위 방향 선택")] RandomTurn,
        [InspectorName("점프")] Jump,
        [InspectorName("공중 부유 이동")] Hover,
        [InspectorName("좌우 수색")] Search,
        [InspectorName("선택한 방식으로 공격")] Attack,
        [InspectorName("즉시 돌진 공격")] Charge,
        [InspectorName("투사체 한 발 발사")] Shoot,
        [InspectorName("부채꼴 세 발 발사")] BurstShoot,
        [InspectorName("주변 범위 공격")] AreaAttack,
        [InspectorName("주변 동료에게 경보")] AlertAllies,
        [InspectorName("타겟 뒤로 순간이동")] TeleportBehind,
        [InspectorName("자폭")] SelfDestruct,
        [InspectorName("즉시 파괴")] DestroySelf,
        [InspectorName("모션만 재생")] PlayMotion
    }

    public enum ObjectAIPatrolPattern
    {
        [InspectorName("이동 시작 때 확률 방향 전환")] ChanceTurn,
        [InspectorName("매번 좌우 완전 랜덤")] RandomDirection,
        [InspectorName("짧은 구간마다 반드시 반전")] ShortPingPong,
        [InspectorName("빠른 이동과 정지 반복")] BurstAndPause,
        [InspectorName("느린 이동과 정지 반복")] CreepAndFreeze,
        [InspectorName("점프하며 이동")] HopAlong,
        [InspectorName("한 방향으로 계속 이동")] OneWay,
        [InspectorName("정지하며 좌우 스캔")] TurnInPlace
    }

    public enum ObjectAIFollowPattern
    {
        [InspectorName("현재 위치로 직선 추적")] Direct,
        [InspectorName("이동 방향 예측 추적")] Predictive,
        [InspectorName("질주와 감속 반복")] BurstSprint,
        [InspectorName("시선 반응 스토킹")] Stalker,
        [InspectorName("공격 사거리 유지")] KeepAttackDistance,
        [InspectorName("감지 상실을 무시하고 추적")] Relentless,
        [InspectorName("퇴로 차단 예측")] Intercept,
        [InspectorName("속도 파동 + 작은 도약")] ZigZag,
        [InspectorName("스폰 반경 안에서만 추적")] GuardSpawn,
        [InspectorName("느린 그림자 추적")] ShadowFollow
    }

    public enum ObjectAIDetectionShape
    {
        [InspectorName("전방/후방 방향형")] Directional,
        [InspectorName("360도 전방위형")] Omnidirectional,
        [InspectorName("전방 전용형")] FrontOnly,
        [InspectorName("원형 근접 센서")] ProximityCircle,
        [InspectorName("벽을 확인하는 시야형")] LineOfSight,
        [InspectorName("주기적으로 켜지는 스캐너")] PeriodicScanner,
        [InspectorName("움직이는 목표 전용")] MotionSensor,
        [InspectorName("피해를 준 대상 전용")] DamageSourceOnly,
        [InspectorName("동료가 공유한 목표 전용")] AllyTargetOnly,
        [InspectorName("공격 거리 잠복형")] AmbushRange
    }

    public enum ObjectAttackPattern
    {
        [InspectorName("짧은 근접 공격")] ShortMelee,
        [InspectorName("강한 내려찍기")] HeavySmash,
        [InspectorName("빠른 3연격")] RapidCombo,
        [InspectorName("몸통 돌진")] Charge,
        [InspectorName("도약 강습")] LeapStrike,
        [InspectorName("단발 투사체")] SingleProjectile,
        [InspectorName("부채꼴 3연발")] TripleProjectile,
        [InspectorName("주변 충격파")] AreaPulse,
        [InspectorName("치고 빠지기")] HitAndRun,
        [InspectorName("근접 자폭")] SelfDestruct,
        [InspectorName("관통 저격선")] SniperBeam
    }

    public enum ObjectAITerrainResponse
    {
        [InspectorName("정지")] Stop,
        [InspectorName("방향 전환")] TurnAround,
        [InspectorName("점프 시도")] Jump,
        [InspectorName("스폰 위치로 귀환")] ReturnToSpawn,
        [InspectorName("잠시 본 뒤 귀환")] WatchThenReturn,
        [InspectorName("검사 무시")] Ignore
    }

    public enum ObjectRuleEffect
    {
        [InspectorName("연출 없음")] None,
        [InspectorName("느낌표 경보")] AlertMark,
        [InspectorName("원형 스캔 파동")] ScanRing,
        [InspectorName("네 방향 타겟 락")] TargetLock,
        [InspectorName("눈빛 섬광")] EyeFlash,
        [InspectorName("초승달 베기")] SlashArc,
        [InspectorName("무거운 충격 파편")] HeavyImpact,
        [InspectorName("고속 잔상")] DashTrail,
        [InspectorName("단발 총구 섬광")] MuzzleFlash,
        [InspectorName("삼연발 총구 섬광")] TripleMuzzle,
        [InspectorName("지면 충격파")] Shockwave,
        [InspectorName("전기 방전")] LightningBurst,
        [InspectorName("불꽃 폭발")] FireBurst,
        [InspectorName("얼음 파편")] IceShards,
        [InspectorName("어둠 잉크 폭발")] DarkSplash,
        [InspectorName("금속 스파크")] SparkBurst,
        [InspectorName("홀로그램 글리치")] Glitch,
        [InspectorName("연기 분산")] SmokeBurst,
        [InspectorName("공간 포털")] Portal,
        [InspectorName("빛 입자 조립")] Materialize,
        [InspectorName("픽셀 분해")] PixelScatter,
        [InspectorName("강한 폭발")] Explosion,
        [InspectorName("중심으로 붕괴")] Implosion,
        [InspectorName("영혼 상승")] SoulRise,
        [InspectorName("그림자 솟구침")] ShadowRise,
        [InspectorName("회복 파동")] HealPulse
    }

    [Serializable]
    public sealed class ObjectRuleEffectParameters
    {
        public Color primaryColor = new Color(1f, 0.32f, 0.12f, 1f);
        public Color secondaryColor = new Color(1f, 0.92f, 0.35f, 1f);
        [Range(0.1f, 3f)] public float scale = 1f;
        [Range(0.1f, 3f)] public float duration = 0.55f;
        [Range(1, 32)] public int amount = 10;

        public ObjectRuleEffectParameters Clone()
        {
            return (ObjectRuleEffectParameters)MemberwiseClone();
        }

        public void Sanitize()
        {
            scale = Mathf.Clamp(scale, 0.1f, 3f);
            duration = Mathf.Clamp(duration, 0.1f, 3f);
            amount = Mathf.Clamp(amount, 1, 32);
        }
    }

    [Serializable]
    public sealed class ObjectAIConditionParameters
    {
        public ObjectAIDetectionShape detectionShape = ObjectAIDetectionShape.Directional;
        [Min(0f)] public float distance = 1.2f;
        [Min(0f)] public float frontDistance = 5f;
        [Min(0f)] public float rearDistance = 1.5f;
        [Min(0f)] public float verticalRange = 2f;
        [Range(0f, 1f)] public float healthRatio = 0.3f;
        public Vector2 intervalSeconds = new Vector2(1f, 2f);
        [Range(0f, 1f)] public float chance = 0.5f;
        public bool requireLineOfSight;
        public LayerMask targetMask = ~0;
        public LayerMask sightBlockingMask = ~0;

        public ObjectAIConditionParameters Clone()
        {
            return (ObjectAIConditionParameters)MemberwiseClone();
        }

        public void Sanitize()
        {
            distance = Mathf.Max(0f, distance);
            frontDistance = Mathf.Max(0f, frontDistance);
            rearDistance = Mathf.Max(0f, rearDistance);
            verticalRange = Mathf.Max(0f, verticalRange);
            healthRatio = Mathf.Clamp01(healthRatio);
            chance = Mathf.Clamp01(chance);
            intervalSeconds = OrderedNonNegative(intervalSeconds);
        }

        private static Vector2 OrderedNonNegative(Vector2 value)
        {
            float x = Mathf.Max(0f, value.x);
            float y = Mathf.Max(0f, value.y);
            return x <= y ? new Vector2(x, y) : new Vector2(y, x);
        }
    }

    [Serializable]
    public sealed class ObjectAIActionParameters
    {
        public Vector2 durationSeconds = new Vector2(0.8f, 1.8f);
        public Vector2 moveSeconds = new Vector2(1.5f, 3f);
        [Min(0f)] public float speedMultiplier = 1f;
        [Min(0f)] public float distance = 1.2f;
        [Range(0f, 1f)] public float turnChance = 0.35f;
        public ObjectAIPatrolPattern patrolPattern = ObjectAIPatrolPattern.ChanceTurn;
        public ObjectAIFollowPattern followPattern = ObjectAIFollowPattern.Direct;
        [Min(0f)] public float minimumFollowSeconds = 1f;
        [Min(0f)] public float memorySeconds = 3f;
        [Min(0f)] public float maxDistanceFromSpawn = 8f;
        [Min(0f)] public float giveUpTargetDistance = 7f;

        public ObjectAttackPattern attackPattern = ObjectAttackPattern.ShortMelee;
        [Min(0)] public int damage = 1;
        [Min(0f)] public float attackRange = 1.2f;
        [Min(0f)] public float prepareSeconds = 0.5f;
        [Min(0f)] public float activeSeconds = 0.15f;
        [Min(0f)] public float recoverySeconds = 0.7f;
        [Min(0f)] public float cooldownSeconds = 0.8f;
        [Min(0.01f)] public float hitboxHeight = 1.2f;
        [Min(0f)] public float targetInvulnerabilitySeconds = 1f;
        [Min(0f)] public float knockback = 0.2f;

        [Min(0.1f)] public float projectileSpeed = 6f;
        [Min(0.1f)] public float projectileLifetime = 2.5f;
        [Min(1f)] public float dashSpeedMultiplier = 3f;
        [Min(0f)] public float jumpForce = 5f;
        [Min(0f)] public float teleportDistance = 1f;
        [Min(0.1f)] public float teleportCooldown = 2.5f;
        [Min(0f)] public float allyAlertRadius = 4f;

        public bool groundMovement = true;
        [Min(0f)] public float smallStepHeight = 0.35f;
        [Min(0.01f)] public float obstacleProbeDistance = 0.18f;
        [Min(0.01f)] public float cliffProbeDistance = 0.6f;
        public LayerMask groundMask = ~0;
        public ObjectAITerrainResponse obstacleResponse = ObjectAITerrainResponse.TurnAround;
        public ObjectAITerrainResponse cliffResponse = ObjectAITerrainResponse.TurnAround;
        public ObjectMotionCondition motion = ObjectMotionCondition.Idle;

        public ObjectAIActionParameters Clone()
        {
            return (ObjectAIActionParameters)MemberwiseClone();
        }

        public void Sanitize()
        {
            durationSeconds = OrderedNonNegative(durationSeconds);
            moveSeconds = OrderedNonNegative(moveSeconds);
            speedMultiplier = Mathf.Max(0f, speedMultiplier);
            distance = Mathf.Max(0f, distance);
            turnChance = Mathf.Clamp01(turnChance);
            minimumFollowSeconds = Mathf.Max(0f, minimumFollowSeconds);
            memorySeconds = Mathf.Max(0f, memorySeconds);
            maxDistanceFromSpawn = Mathf.Max(0f, maxDistanceFromSpawn);
            giveUpTargetDistance = Mathf.Max(0f, giveUpTargetDistance);
            damage = Mathf.Max(0, damage);
            attackRange = Mathf.Max(0f, attackRange);
            prepareSeconds = Mathf.Max(0f, prepareSeconds);
            activeSeconds = Mathf.Max(0f, activeSeconds);
            recoverySeconds = Mathf.Max(0f, recoverySeconds);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            hitboxHeight = Mathf.Max(0.01f, hitboxHeight);
            targetInvulnerabilitySeconds = Mathf.Max(0f, targetInvulnerabilitySeconds);
            knockback = Mathf.Max(0f, knockback);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileLifetime = Mathf.Max(0.1f, projectileLifetime);
            dashSpeedMultiplier = Mathf.Max(1f, dashSpeedMultiplier);
            jumpForce = Mathf.Max(0f, jumpForce);
            teleportDistance = Mathf.Max(0f, teleportDistance);
            teleportCooldown = Mathf.Max(0.1f, teleportCooldown);
            allyAlertRadius = Mathf.Max(0f, allyAlertRadius);
            smallStepHeight = Mathf.Max(0f, smallStepHeight);
            obstacleProbeDistance = Mathf.Max(0.01f, obstacleProbeDistance);
            cliffProbeDistance = Mathf.Max(0.01f, cliffProbeDistance);
        }

        private static Vector2 OrderedNonNegative(Vector2 value)
        {
            float x = Mathf.Max(0f, value.x);
            float y = Mathf.Max(0f, value.y);
            return x <= y ? new Vector2(x, y) : new Vector2(y, x);
        }
    }

    [Serializable]
    public sealed class ObjectAIRule
    {
        public string label = "새 규칙";
        public bool enabled = true;
        public ObjectAICondition when = ObjectAICondition.NoTarget;
        public ObjectAIAction action = ObjectAIAction.Stop;
        public ObjectAIConditionParameters condition = new ObjectAIConditionParameters();
        public ObjectAIActionParameters settings = new ObjectAIActionParameters();
        public ObjectRuleEffect effect = ObjectRuleEffect.None;
        public ObjectRuleEffectParameters effectSettings = new ObjectRuleEffectParameters();

        public ObjectAIRule Clone()
        {
            var clone = (ObjectAIRule)MemberwiseClone();
            clone.condition = condition != null ? condition.Clone() : new ObjectAIConditionParameters();
            clone.settings = settings != null ? settings.Clone() : new ObjectAIActionParameters();
            clone.effectSettings = effectSettings != null
                ? effectSettings.Clone()
                : new ObjectRuleEffectParameters();
            return clone;
        }

        public void Sanitize()
        {
            label = label ?? string.Empty;
            condition = condition ?? new ObjectAIConditionParameters();
            settings = settings ?? new ObjectAIActionParameters();
            effectSettings = effectSettings ?? new ObjectRuleEffectParameters();
            condition.Sanitize();
            settings.Sanitize();
            effectSettings.Sanitize();
        }
    }

    [Serializable]
    public sealed class ObjectAIProgram
    {
        public bool useLegacyTuning = true;
        public List<ObjectAIRule> rules = CreateSG001Rules();

        public ObjectAIProgram Clone()
        {
            var clone = (ObjectAIProgram)MemberwiseClone();
            clone.rules = new List<ObjectAIRule>();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                    if (rules[i] != null)
                        clone.rules.Add(rules[i].Clone());
            }
            return clone;
        }

        public void Sanitize()
        {
            rules = rules ?? new List<ObjectAIRule>();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null)
                    rules[i] = new ObjectAIRule();
                rules[i].Sanitize();
            }
        }

        public static List<ObjectAIRule> CreateSG001Rules()
        {
            ObjectAIRule died = Rule("파괴될 때", ObjectAICondition.Died,
                ObjectAIAction.None);
            died.effect = ObjectRuleEffect.PixelScatter;

            ObjectAIRule damaged = Rule("피격했을 때", ObjectAICondition.Damaged,
                ObjectAIAction.FollowDamageSource);
            ConfigureFollow(damaged.settings);
            damaged.effect = ObjectRuleEffect.SparkBurst;

            ObjectAIRule attack = Rule("공격 거리 안일 때", ObjectAICondition.TargetWithinAttackRange,
                ObjectAIAction.Attack);
            attack.condition.distance = 1.2f;
            ConfigureAttack(attack.settings);
            attack.effect = ObjectRuleEffect.SlashArc;

            ObjectAIRule lost = Rule("플레이어를 놓쳤을 때", ObjectAICondition.TargetLost,
                ObjectAIAction.SearchLastSeenThenReturn);
            lost.settings.durationSeconds = new Vector2(3f, 3f);
            lost.settings.speedMultiplier = 1.4f;
            lost.settings.cliffResponse = ObjectAITerrainResponse.WatchThenReturn;
            lost.effect = ObjectRuleEffect.ScanRing;

            ObjectAIRule follow = Rule("플레이어를 감지했을 때", ObjectAICondition.TargetDetected,
                ObjectAIAction.FollowTarget);
            follow.condition.frontDistance = 5f;
            follow.condition.rearDistance = 1.5f;
            follow.condition.verticalRange = 2f;
            ConfigureFollow(follow.settings);
            follow.effect = ObjectRuleEffect.AlertMark;

            ObjectAIRule patrol = Rule("타겟이 없을 때", ObjectAICondition.NoTarget,
                ObjectAIAction.Patrol);
            patrol.settings.moveSeconds = new Vector2(1.5f, 3f);
            patrol.settings.durationSeconds = new Vector2(0.8f, 1.8f);
            patrol.settings.turnChance = 0.35f;
            patrol.settings.patrolPattern = ObjectAIPatrolPattern.ChanceTurn;
            patrol.settings.speedMultiplier = 1f;
            patrol.settings.obstacleResponse = ObjectAITerrainResponse.TurnAround;
            patrol.settings.cliffResponse = ObjectAITerrainResponse.TurnAround;

            return new List<ObjectAIRule> { died, damaged, attack, lost, follow, patrol };
        }

        private static ObjectAIRule Rule(string label, ObjectAICondition condition, ObjectAIAction action)
        {
            return new ObjectAIRule { label = label, when = condition, action = action };
        }

        private static void ConfigureFollow(ObjectAIActionParameters settings)
        {
            settings.speedMultiplier = 1.4f;
            settings.minimumFollowSeconds = 1f;
            settings.memorySeconds = 3f;
            settings.maxDistanceFromSpawn = 8f;
            settings.giveUpTargetDistance = 7f;
            settings.followPattern = ObjectAIFollowPattern.Direct;
            settings.obstacleResponse = ObjectAITerrainResponse.WatchThenReturn;
            settings.cliffResponse = ObjectAITerrainResponse.WatchThenReturn;
        }

        private static void ConfigureAttack(ObjectAIActionParameters settings)
        {
            settings.attackPattern = ObjectAttackPattern.ShortMelee;
            settings.damage = 1;
            settings.attackRange = 1.2f;
            settings.prepareSeconds = 0.5f;
            settings.activeSeconds = 0.15f;
            settings.recoverySeconds = 0.7f;
            settings.cooldownSeconds = 0.8f;
            settings.hitboxHeight = 1.2f;
            settings.targetInvulnerabilitySeconds = 1f;
        }
    }

    public static class ObjectBehaviorCatalog
    {
        public static int ConditionCount => Enum.GetValues(typeof(ObjectAICondition)).Length;
        public static int ActionCount => Enum.GetValues(typeof(ObjectAIAction)).Length;

        public static void ImportLegacyTuning(ObjectDefinitionData data)
        {
            if (data == null || data.ai == null || !data.ai.useLegacyTuning || data.ai.rules == null)
                return;

            for (int i = 0; i < data.ai.rules.Count; i++)
            {
                ObjectAIRule rule = data.ai.rules[i];
                if (rule == null)
                    continue;
                if (rule.when == ObjectAICondition.TargetDetected ||
                    rule.when == ObjectAICondition.TargetEnteredDetection)
                {
                    rule.condition.frontDistance = data.detection.frontDistance;
                    rule.condition.rearDistance = data.detection.rearDistance;
                    rule.condition.verticalRange = data.detection.verticalRange;
                    rule.condition.targetMask = data.detection.targetMask;
                    rule.condition.requireLineOfSight = data.detection.requireLineOfSight;
                    rule.condition.sightBlockingMask = data.detection.sightBlockingMask;
                }
                if (rule.when == ObjectAICondition.TargetWithinAttackRange)
                    rule.condition.distance = data.attack.attackRange;

                ObjectAIActionParameters settings = rule.settings;
                if (settings == null)
                    continue;
                if (IsMovementAction(rule.action))
                {
                    settings.groundMovement = data.movement.groundWalker;
                    settings.smallStepHeight = data.movement.smallStepHeight;
                    settings.obstacleProbeDistance = data.movement.obstacleProbeDistance;
                    settings.cliffProbeDistance = data.movement.cliffProbeDistance;
                    settings.groundMask = data.movement.groundMask;
                }
                if (rule.action == ObjectAIAction.Patrol || rule.action == ObjectAIAction.Wander)
                {
                    settings.moveSeconds = data.movement.patrolMoveSeconds;
                    settings.durationSeconds = data.movement.idleSeconds;
                    settings.turnChance = data.movement.turnChanceOnMove;
                    settings.speedMultiplier = 1f;
                }
                if (rule.action == ObjectAIAction.FollowTarget ||
                    rule.action == ObjectAIAction.FollowDamageSource ||
                    rule.action == ObjectAIAction.KeepDistance)
                {
                    settings.speedMultiplier = data.chase.speedMultiplier;
                    settings.minimumFollowSeconds = data.chase.minimumChaseSeconds;
                    settings.memorySeconds = Mathf.Max(
                        data.chase.detectionMemorySeconds,
                        data.chase.lostSightChaseSeconds);
                    settings.maxDistanceFromSpawn = data.chase.maxDistanceFromSpawn;
                    settings.giveUpTargetDistance = data.chase.giveUpTargetDistance;
                    settings.durationSeconds = new Vector2(
                        data.chase.unreachableLookSeconds,
                        data.chase.unreachableLookSeconds);
                }
                if (rule.action == ObjectAIAction.SearchLastSeenThenReturn)
                {
                    float memory = Mathf.Max(
                        data.chase.detectionMemorySeconds,
                        data.chase.lostSightChaseSeconds);
                    settings.durationSeconds = new Vector2(memory, memory);
                    settings.speedMultiplier = data.chase.speedMultiplier;
                }
                if (IsAttackAction(rule.action))
                {
                    settings.damage = data.attack.attackDamage;
                    settings.attackRange = data.attack.attackRange;
                    settings.prepareSeconds = data.attack.prepareSeconds;
                    settings.activeSeconds = data.attack.activeSeconds;
                    settings.recoverySeconds = data.attack.recoverySeconds;
                    settings.cooldownSeconds = data.attack.cooldownSeconds;
                    settings.hitboxHeight = data.attack.hitboxHeight;
                    settings.targetInvulnerabilitySeconds = data.attack.targetInvulnerabilitySeconds;
                    settings.knockback = 0f;
                }
            }
            data.ai.useLegacyTuning = false;
            data.ai.Sanitize();
        }

        public static bool UsesDetectionValues(ObjectAICondition condition)
        {
            return condition == ObjectAICondition.TargetDetected ||
                   condition == ObjectAICondition.TargetEnteredDetection;
        }

        public static bool UsesDistance(ObjectAICondition condition)
        {
            return condition == ObjectAICondition.TargetWithinDistance ||
                   condition == ObjectAICondition.TargetOutsideDistance ||
                   condition == ObjectAICondition.TargetWithinAttackRange ||
                   condition == ObjectAICondition.FarFromSpawn ||
                   condition == ObjectAICondition.AtSpawn;
        }

        public static bool IsMovementAction(ObjectAIAction action)
        {
            return action == ObjectAIAction.Patrol || action == ObjectAIAction.Wander ||
                   action == ObjectAIAction.FollowTarget || action == ObjectAIAction.FollowDamageSource ||
                   action == ObjectAIAction.SearchLastSeenThenReturn || action == ObjectAIAction.KeepDistance ||
                   action == ObjectAIAction.FleeTarget || action == ObjectAIAction.ReturnToSpawn ||
                   action == ObjectAIAction.Hover || action == ObjectAIAction.Search;
        }

        public static bool IsAttackAction(ObjectAIAction action)
        {
            return action == ObjectAIAction.Attack || action == ObjectAIAction.Charge ||
                   action == ObjectAIAction.Shoot || action == ObjectAIAction.BurstShoot ||
                   action == ObjectAIAction.AreaAttack;
        }

        public static bool IsRangedAttack(ObjectAttackPattern attack)
        {
            return attack == ObjectAttackPattern.SingleProjectile ||
                   attack == ObjectAttackPattern.TripleProjectile ||
                   attack == ObjectAttackPattern.SniperBeam;
        }

        public static string DescribeCondition(ObjectAICondition condition)
        {
            switch (condition)
            {
                case ObjectAICondition.OnSpawn: return "이 오브젝트가 활성화된 직후 한 번만 참입니다.";
                case ObjectAICondition.Always: return "위쪽의 우선 규칙이 하나도 맞지 않을 때 계속 참입니다.";
                case ObjectAICondition.NoTarget: return "현재 기억하거나 감지한 타겟이 없을 때 참입니다.";
                case ObjectAICondition.HasTarget: return "감지 여부와 관계없이 추적 중인 타겟이 있으면 참입니다.";
                case ObjectAICondition.TargetDetected: return "설정한 전방/후방/수직 감지 범위 안에 플레이어가 있는 동안 참입니다.";
                case ObjectAICondition.TargetEnteredDetection: return "플레이어가 감지 범위 밖에서 안으로 들어온 순간 한 번만 참입니다.";
                case ObjectAICondition.TargetLost: return "감지하던 플레이어가 범위 밖으로 나간 순간 한 번만 참입니다.";
                case ObjectAICondition.TargetWithinDistance: return "타겟과의 거리가 지정 값 이하일 때 참입니다.";
                case ObjectAICondition.TargetOutsideDistance: return "타겟과의 거리가 지정 값 이상일 때 참입니다.";
                case ObjectAICondition.TargetWithinAttackRange: return "타겟이 이 규칙의 거리와 수직 판정 안에 있을 때 참입니다.";
                case ObjectAICondition.Damaged: return "피해가 실제 적용된 순간 한 번만 참입니다.";
                case ObjectAICondition.LowHealth: return "현재 체력 비율이 지정 값 이하인 동안 참입니다.";
                case ObjectAICondition.EveryInterval: return "설정한 최소~최대 시간 중 무작위 간격마다 한 번 참입니다.";
                case ObjectAICondition.AtSpawn: return "스폰 위치와의 거리가 지정 값 이하일 때 참입니다.";
                case ObjectAICondition.FarFromSpawn: return "스폰 위치와의 거리가 지정 값 이상일 때 참입니다.";
                case ObjectAICondition.ObstacleAhead: return "현재 바라보는 방향의 높은 장애물을 감지했을 때 참입니다.";
                case ObjectAICondition.CliffAhead: return "현재 바라보는 방향 아래에 지면이 없을 때 참입니다.";
                case ObjectAICondition.ActionFinished: return "바로 전 규칙 행동이 끝난 순간 한 번만 참입니다.";
                case ObjectAICondition.Died: return "이 오브젝트의 체력이 0이 된 순간 한 번만 사용됩니다.";
                case ObjectAICondition.TargetDead: return "현재 타겟의 체력이 0이 된 순간 참입니다.";
                case ObjectAICondition.AllyAlertReceived: return "주변 2DObjectMaker 몬스터에게 목표를 전달받은 순간 한 번만 참입니다.";
                case ObjectAICondition.ContactingPlayer: return "몸체가 플레이어와 접촉 중일 때 참입니다.";
                case ObjectAICondition.RandomChance: return "AI 판단 시 설정 확률에 당첨된 프레임에만 참입니다.";
                default: return string.Empty;
            }
        }

        public static string DescribeAction(ObjectAIAction action)
        {
            switch (action)
            {
                case ObjectAIAction.None: return "현재 규칙은 조건을 소비하지만 행동 상태를 바꾸지 않습니다.";
                case ObjectAIAction.Stop: return "즉시 수평 이동을 멈추고 Idle 모션을 유지합니다.";
                case ObjectAIAction.Wait: return "최소~최대 시간 중 무작위 시간 동안 멈춥니다.";
                case ObjectAIAction.Patrol: return "이동/정지 시간을 반복하며 선택한 Patrol 패턴을 실행합니다.";
                case ObjectAIAction.Wander: return "이동할 때마다 좌우를 무작위로 골라 배회합니다.";
                case ObjectAIAction.Guard: return "위치는 지키고 대기 시간마다 좌우를 번갈아 바라봅니다.";
                case ObjectAIAction.FollowTarget: return "현재 타겟을 선택한 Follow 패턴과 속도로 추적합니다.";
                case ObjectAIAction.FollowDamageSource: return "가장 최근에 피해를 준 ObjectActor2D를 타겟으로 정하고 추적합니다.";
                case ObjectAIAction.SearchLastSeenThenReturn: return "마지막 위치를 지정 시간 수색한 뒤 스폰 위치로 돌아갑니다.";
                case ObjectAIAction.KeepDistance: return "지정 거리보다 멀면 접근하고 가까우면 후퇴합니다.";
                case ObjectAIAction.FaceTarget: return "이동하지 않고 현재 타겟 쪽으로 방향만 바꿉니다.";
                case ObjectAIAction.FleeTarget: return "현재 타겟의 반대 방향으로 이동합니다.";
                case ObjectAIAction.ReturnToSpawn: return "최초 생성 위치까지 이동한 뒤 행동을 끝냅니다.";
                case ObjectAIAction.TurnAround: return "바라보는 방향을 한 번 반대로 바꿉니다.";
                case ObjectAIAction.RandomTurn: return "좌/우 중 한 방향을 무작위로 한 번 선택합니다.";
                case ObjectAIAction.Jump: return "현재 Rigidbody2D에 위쪽 힘을 한 번 적용합니다.";
                case ObjectAIAction.Hover: return "지면 검사를 무시하고 수평 이동과 수직 파동을 만듭니다.";
                case ObjectAIAction.Search: return "지정 시간 동안 좌우 방향을 번갈아 수색합니다.";
                case ObjectAIAction.Attack: return "선택한 공격 방식과 준비/판정/회수/쿨다운을 실행합니다.";
                case ObjectAIAction.Charge: return "Attack 설정을 사용해 즉시 몸통 돌진을 실행합니다.";
                case ObjectAIAction.Shoot: return "Attack 설정으로 타겟 방향에 투사체 한 발을 만듭니다.";
                case ObjectAIAction.BurstShoot: return "타겟 방향과 위/아래 방향에 투사체 세 발을 만듭니다.";
                case ObjectAIAction.AreaAttack: return "자신을 중심으로 Attack Range 안의 플레이어를 공격합니다.";
                case ObjectAIAction.AlertAllies: return "지정 반경의 몬스터 Brain에 현재 타겟을 전달합니다.";
                case ObjectAIAction.TeleportBehind: return "쿨다운이 허용되면 타겟 반대편으로 즉시 이동합니다.";
                case ObjectAIAction.SelfDestruct: return "범위 피해를 한 번 적용하고 자신의 체력을 0으로 만듭니다.";
                case ObjectAIAction.DestroySelf: return "피해 판정 없이 자신의 체력을 0으로 만듭니다.";
                case ObjectAIAction.PlayMotion: return "설정한 Motion Condition만 재생하고 이동/전투는 바꾸지 않습니다.";
                default: return string.Empty;
            }
        }
    }
}
