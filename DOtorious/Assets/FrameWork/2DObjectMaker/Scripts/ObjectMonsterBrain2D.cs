using System.Collections;
using UnityEngine;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker
{
    public enum ObjectBrainState
    {
        Idle,
        Patrol,
        Alert,
        Chase,
        Return,
        AttackPrepare,
        AttackActive,
        AttackRecover,
        AttackCooldown,
        Hit,
        Dead,
        Search,
        Flee
    }

    internal enum ObjectMoveBlockReason
    {
        None,
        Obstacle,
        Cliff
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(ObjectActor2D))]
    public sealed class ObjectMonsterBrain2D : MonoBehaviour
    {
        [SerializeField] private ObjectActor2D actor;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Transform groundProbe;
        [SerializeField] private ObjectFxController2D effects;
        [SerializeField] private ObjectAudioController2D audioController;

        private ObjectBrainState state;
        private ObjectActor2D target;
        private ObjectActor2D detectionLockedTarget;
        private ObjectActor2D lastDamageSource;
        private Vector2 spawnPosition;
        private Vector2 lastSeenPosition;
        private float stateTimer;
        private float actionStartedAt;
        private float lastDetectedAt = float.NegativeInfinity;
        private float lastTeleportAt = float.NegativeInfinity;
        private float hitStopRemaining;
        private float moveDirection = 1f;
        private float nextDirectionDecisionAt;
        private float blockedSeconds;
        private bool attackApplied;
        private bool patrolMoving;
        private bool wasDetected;
        private bool onSpawnPending;
        private bool enteredDetectionPending;
        private bool targetLostPending;
        private bool targetLostQueued;
        private bool damagedPending;
        private bool actionFinishedPending;
        private bool allyAlertPending;
        private bool targetDeadPending;
        private bool actionHoldsAfterCondition;
        private bool attackLocksRules;
        private bool searchReturning;
        private bool resumeActionSoundPending;
        private int resumeActionSoundRuleIndex = -1;
        private float contactUntil;
        private int activeRuleIndex = -1;
        private float[] nextIntervalAt = new float[0];
        private float[] nextRandomDecisionAt = new float[0];
        private bool[] consumedRules = new bool[0];
        private bool[] playedHealthReachedEffects = new bool[0];
        private ObjectAIActionParameters activeSettings;
        private ObjectAttackPattern activeAttackPattern;

        public ObjectBrainState State => state;
        public ObjectActor2D Target => target;
        public Vector2 SpawnPosition => spawnPosition;
        public int ActiveRuleIndex => activeRuleIndex;
        public ObjectAIRule ActiveRule => GetRule(activeRuleIndex);
        public ObjectAIAction ActiveAction => ActiveRule != null ? ActiveRule.action : ObjectAIAction.None;

        private ObjectDefinitionData Data => actor != null ? actor.Data : null;
        private ObjectAIProgram Program => Data != null ? Data.ai : null;

        private void Awake()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
            {
                enabled = false;
                return;
            }
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (actor != null)
            {
                actor.Damaged -= OnActorDamaged;
                actor.Damaged += OnActorDamaged;
                actor.Died -= OnActorDied;
                actor.Died += OnActorDied;
            }

            ObjectAIRule activeRule = ActiveRule;
            resumeActionSoundPending = actor != null && !actor.IsDead &&
                                       audioController != null && activeRule != null &&
                                       activeRule.actionSound != null &&
                                       activeRule.actionSound.isLoop;
            resumeActionSoundRuleIndex = resumeActionSoundPending
                ? activeRuleIndex
                : -1;
        }

        private void Start()
        {
            if (actor == null || Data == null || actor.Kind != ObjectKind.Monster)
            {
                enabled = false;
                return;
            }

            Data.Sanitize();
            spawnPosition = body != null ? body.position : (Vector2)transform.position;
            lastSeenPosition = spawnPosition;
            moveDirection = Data.facesRightByDefault ? 1f : -1f;
            onSpawnPending = true;
            EnsureRuleRuntimeState();
            actor.SetEnemyRuleAnimation(-1, ObjectEnemyAnimatorPhase.None);
            BeginState(ObjectBrainState.Idle, 0f);
        }

        private void OnDisable()
        {
            if (actor != null)
            {
                actor.Damaged -= OnActorDamaged;
                actor.Died -= OnActorDied;
                if (actor.Animator != null)
                    actor.Animator.speed = 1f;
            }
            resumeActionSoundPending = false;
            resumeActionSoundRuleIndex = -1;
            audioController?.StopBehavior();
        }

        private void Update()
        {
            if (actor == null || Data == null || actor.IsDead || state == ObjectBrainState.Dead)
                return;

            if (hitStopRemaining > 0f)
            {
                hitStopRemaining -= Time.unscaledDeltaTime;
                if (actor.Animator != null)
                    actor.Animator.speed = 0f;
                StopHorizontalMovement();
                if (hitStopRemaining > 0f)
                    return;
                if (actor.Animator != null)
                    actor.Animator.speed = 1f;
            }

            EnsureRuleRuntimeState();
            UpdateTargetContext();
            stateTimer -= Time.deltaTime;
            RefreshConsumedRules();

            int nextRule = FindMatchingRule();
            if (attackLocksRules && !damagedPending)
            {
                nextRule = activeRuleIndex;
            }
            else if (actionHoldsAfterCondition && nextRule < 0)
            {
                nextRule = activeRuleIndex;
            }

            if (nextRule != activeRuleIndex)
                EnterRule(nextRule);

            TickActiveAction();
            ClearTransientConditions();

            if (resumeActionSoundPending)
            {
                // Resume only when condition evaluation kept the same rule. EnterRule
                // already starts the correct sound when the rule changed while disabled.
                resumeActionSoundPending = false;
                int expectedRuleIndex = resumeActionSoundRuleIndex;
                resumeActionSoundRuleIndex = -1;
                ObjectAIRule activeRule = ActiveRule;
                if (activeRuleIndex == expectedRuleIndex && activeRule != null &&
                    activeRule.actionSound != null && activeRule.actionSound.isLoop)
                    audioController?.PlayBehavior(activeRule.actionSound);
            }
        }

        private void FixedUpdate()
        {
            if (actor == null || Data == null || actor.IsDead || body == null)
                return;

            ObjectAIRule rule = ActiveRule;
            if (rule == null || activeSettings == null)
            {
                StopHorizontalMovement();
                return;
            }

            float direction;
            float speed;
            float verticalVelocity;
            if (!TryGetMovement(rule.action, activeSettings, out direction, out speed, out verticalVelocity))
            {
                StopHorizontalMovement();
                return;
            }

            if (Mathf.Abs(direction) < 0.001f || speed <= 0f)
            {
                StopHorizontalMovement();
                if (!Mathf.Approximately(verticalVelocity, 0f))
                    body.linearVelocity = new Vector2(body.linearVelocity.x, verticalVelocity);
                return;
            }

            moveDirection = direction;
            actor.SetFacing(direction);
            ObjectMoveBlockReason block = ProbeMoveBlock(direction, activeSettings, true);
            if (block != ObjectMoveBlockReason.None && !HandleMoveBlocked(block, activeSettings))
            {
                StopHorizontalMovement();
                return;
            }

            blockedSeconds = 0f;
            body.linearVelocity = new Vector2(direction * speed,
                Mathf.Approximately(verticalVelocity, 0f) ? body.linearVelocity.y : verticalVelocity);
            actor.SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, Mathf.Abs(speed));
        }

        public void Configure(
            ObjectActor2D sourceActor,
            Rigidbody2D sourceBody,
            Collider2D sourceCollider,
            Transform sourceGroundProbe)
        {
            actor = sourceActor;
            body = sourceBody;
            bodyCollider = sourceCollider;
            groundProbe = sourceGroundProbe;
            ResolveReferences();
        }

        public void ForceTarget(ObjectActor2D nextTarget)
        {
            target = nextTarget;
            if (target == null)
                return;
            lastSeenPosition = target.transform.position;
            lastDetectedAt = Time.time;
            enteredDetectionPending = true;
            wasDetected = true;
        }

        public void ReceiveAllyAlert(ObjectActor2D sharedTarget)
        {
            if (sharedTarget == null || sharedTarget.IsDead)
                return;
            target = sharedTarget;
            lastSeenPosition = sharedTarget.transform.position;
            lastDetectedAt = Time.time;
            allyAlertPending = true;
        }

        private void ResolveReferences()
        {
            if (actor == null)
                actor = GetComponent<ObjectActor2D>();
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider2D>();
            if (effects == null)
                effects = GetComponent<ObjectFxController2D>();
            if (audioController == null && actor != null)
                audioController = actor.AudioController;
        }

        private void EnsureRuleRuntimeState()
        {
            int count = Program != null && Program.rules != null ? Program.rules.Count : 0;
            if (nextIntervalAt.Length == count && playedHealthReachedEffects.Length == count)
                return;

            nextIntervalAt = new float[count];
            nextRandomDecisionAt = new float[count];
            consumedRules = new bool[count];
            playedHealthReachedEffects = new bool[count];
            for (int i = 0; i < count; i++)
            {
                ObjectAIRule rule = GetRule(i);
                Vector2 interval = rule != null ? rule.condition.intervalSeconds : Vector2.one;
                nextIntervalAt[i] = Time.time + RandomRange(interval);
                nextRandomDecisionAt[i] = Time.time;
            }
            if (activeRuleIndex >= count)
                activeRuleIndex = -1;
        }

        private ObjectAIRule GetRule(int index)
        {
            if (Program == null || Program.rules == null || index < 0 || index >= Program.rules.Count)
                return null;
            return Program.rules[index];
        }

        private void UpdateTargetContext()
        {
            if (targetLostQueued)
            {
                targetLostPending = true;
                targetLostQueued = false;
            }

            if (target != null && target.IsDead)
                targetDeadPending = true;

            ObjectActor2D candidate = target;
            if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled)
                candidate = ObjectMakerRegistry.FindClosestPlayer(transform.position, ~0);

            bool detected;
            if (candidate != null && candidate == detectionLockedTarget)
            {
                bool stillInside = IsDetectedByProgram(candidate, true);
                if (!stillInside)
                    detectionLockedTarget = null;
                detected = false;
            }
            else
            {
                detected = candidate != null && IsDetectedByProgram(candidate);
            }
            if (target == null && detected)
                target = candidate;

            if (target != null && target != candidate && !target.IsDead)
                detected = IsDetectedByProgram(target);

            if (detected && target != null)
            {
                lastDetectedAt = Time.time;
                lastSeenPosition = target.transform.position;
            }

            enteredDetectionPending = !wasDetected && detected;
            targetLostPending |= wasDetected && !detected;
            wasDetected = detected;
        }

        private bool IsDetectedByProgram(ObjectActor2D candidate, bool ignoreDetectionLock = false)
        {
            bool foundDetectionRule = false;
            if (Program != null && Program.rules != null)
            {
                for (int i = 0; i < Program.rules.Count; i++)
                {
                    ObjectAIRule rule = Program.rules[i];
                    if (rule == null || !rule.enabled ||
                        (rule.when != ObjectAICondition.TargetDetected &&
                         rule.when != ObjectAICondition.TargetEnteredDetection))
                        continue;
                    foundDetectionRule = true;
                    if (IsDetected(candidate, ResolveCondition(rule), ignoreDetectionLock))
                        return true;
                }
            }

            if (foundDetectionRule)
                return false;

            var fallback = new ObjectAIConditionParameters
            {
                frontDistance = Data.detection.frontDistance,
                rearDistance = Data.detection.rearDistance,
                verticalRange = Data.detection.verticalRange,
                targetMask = Data.detection.targetMask,
                requireLineOfSight = Data.detection.requireLineOfSight,
                sightBlockingMask = Data.detection.sightBlockingMask
            };
            return IsDetected(candidate, fallback, ignoreDetectionLock);
        }

        private bool IsDetected(
            ObjectActor2D candidate,
            ObjectAIConditionParameters condition,
            bool ignoreDetectionLock = false)
        {
            if (candidate == null || candidate.IsDead || condition == null ||
                (!ignoreDetectionLock && candidate == detectionLockedTarget) ||
                (condition.targetMask.value & (1 << candidate.gameObject.layer)) == 0)
                return false;

            Vector2 delta = candidate.transform.position - transform.position;
            float facing = actor.IsFacingRight ? 1f : -1f;
            float forward = delta.x * facing;
            bool inside;

            switch (condition.detectionShape)
            {
                case ObjectAIDetectionShape.Omnidirectional:
                    inside = Mathf.Abs(delta.x) <= condition.frontDistance &&
                             Mathf.Abs(delta.y) <= condition.verticalRange;
                    break;
                case ObjectAIDetectionShape.FrontOnly:
                    inside = forward >= 0f && forward <= condition.frontDistance &&
                             Mathf.Abs(delta.y) <= condition.verticalRange;
                    break;
                case ObjectAIDetectionShape.ProximityCircle:
                    inside = delta.magnitude <= condition.frontDistance;
                    break;
                case ObjectAIDetectionShape.PeriodicScanner:
                    float period = Mathf.Max(0.2f, condition.intervalSeconds.y);
                    float window = Mathf.Max(0.05f, condition.intervalSeconds.x);
                    inside = Mathf.Repeat(Time.time, period) <= window &&
                             Mathf.Abs(delta.x) <= condition.frontDistance &&
                             Mathf.Abs(delta.y) <= condition.verticalRange;
                    break;
                case ObjectAIDetectionShape.MotionSensor:
                    inside = candidate.Body != null && candidate.Body.linearVelocity.sqrMagnitude > 0.01f &&
                             Mathf.Abs(delta.x) <= condition.frontDistance &&
                             Mathf.Abs(delta.y) <= condition.verticalRange;
                    break;
                case ObjectAIDetectionShape.DamageSourceOnly:
                    inside = candidate == lastDamageSource;
                    break;
                case ObjectAIDetectionShape.AllyTargetOnly:
                    inside = allyAlertPending && candidate == target;
                    break;
                case ObjectAIDetectionShape.AmbushRange:
                    inside = delta.magnitude <= Mathf.Max(0.05f, condition.distance);
                    break;
                default:
                    float allowed = forward >= 0f ? condition.frontDistance : condition.rearDistance;
                    inside = Mathf.Abs(delta.x) <= allowed &&
                             Mathf.Abs(delta.y) <= condition.verticalRange;
                    break;
            }

            if (!inside)
                return false;
            bool lineOfSight = condition.requireLineOfSight ||
                               condition.detectionShape == ObjectAIDetectionShape.LineOfSight;
            return !lineOfSight || HasLineOfSight(candidate, condition.sightBlockingMask);
        }

        private bool HasLineOfSight(ObjectActor2D candidate, LayerMask blockingMask)
        {
            Vector2 origin = bodyCollider != null ? bodyCollider.bounds.center : transform.position;
            Vector2 destination = candidate.BodyCollider != null
                ? candidate.BodyCollider.bounds.center
                : candidate.transform.position;
            Vector2 delta = destination - origin;
            if (delta.sqrMagnitude < 0.0001f)
                return true;

            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, delta.normalized, delta.magnitude, blockingMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform))
                    continue;
                if (hit.transform.IsChildOf(candidate.transform))
                    return true;
                return false;
            }
            return true;
        }

        private void RefreshConsumedRules()
        {
            for (int i = 0; i < consumedRules.Length; i++)
            {
                ObjectAIRule rule = GetRule(i);
                if (rule != null && rule.when == ObjectAICondition.HealthReached &&
                    actor.CurrentHealth > Mathf.Max(0, rule.condition.healthValue))
                    playedHealthReachedEffects[i] = false;

                if (!consumedRules[i])
                    continue;
                if (rule == null || !ConditionMatches(rule, i, true))
                    consumedRules[i] = false;
            }
        }

        private int FindMatchingRule()
        {
            if (Program == null || Program.rules == null)
                return -1;

            for (int i = 0; i < Program.rules.Count; i++)
            {
                ObjectAIRule rule = Program.rules[i];
                if (rule == null || !rule.enabled || consumedRules[i])
                    continue;
                if (ConditionMatches(rule, i, false))
                    return i;
            }
            return -1;
        }

        private bool ConditionMatches(ObjectAIRule rule, int index, bool rawCheck)
        {
            ObjectAIConditionParameters condition = ResolveCondition(rule);
            float targetDistance = target != null
                ? Vector2.Distance(body != null ? body.position : (Vector2)transform.position,
                    target.transform.position)
                : float.PositiveInfinity;
            switch (rule.when)
            {
                case ObjectAICondition.OnSpawn:
                    return onSpawnPending;
                case ObjectAICondition.Always:
                    return true;
                case ObjectAICondition.NoTarget:
                    return target == null || target.IsDead;
                case ObjectAICondition.HasTarget:
                    return target != null && !target.IsDead;
                case ObjectAICondition.TargetDetected:
                    return target != null && IsDetected(target, condition);
                case ObjectAICondition.TargetEnteredDetection:
                    return enteredDetectionPending && target != null && IsDetected(target, condition);
                case ObjectAICondition.TargetLost:
                    return targetLostPending;
                case ObjectAICondition.TargetWithinDistance:
                    return target != null && targetDistance <= condition.distance;
                case ObjectAICondition.TargetOutsideDistance:
                    return target != null && targetDistance >= condition.distance;
                case ObjectAICondition.TargetWithinAttackRange:
                    return target != null && Mathf.Abs(target.transform.position.x - transform.position.x) <=
                           condition.distance && Mathf.Abs(target.transform.position.y - transform.position.y) <=
                           Mathf.Max(0.01f, rule.settings.hitboxHeight) * 0.5f;
                case ObjectAICondition.Damaged:
                    return damagedPending;
                case ObjectAICondition.LowHealth:
                    return actor.CurrentHealthRatio <= condition.healthRatio;
                case ObjectAICondition.HealthReached:
                    return actor.CurrentHealth <= condition.healthValue;
                case ObjectAICondition.EveryInterval:
                    return index >= 0 && index < nextIntervalAt.Length && Time.time >= nextIntervalAt[index];
                case ObjectAICondition.AtSpawn:
                    return Vector2.Distance(body.position, spawnPosition) <= condition.distance;
                case ObjectAICondition.FarFromSpawn:
                    return Vector2.Distance(body.position, spawnPosition) >= condition.distance;
                case ObjectAICondition.ObstacleAhead:
                    return ProbeMoveBlock(moveDirection, rule.settings, false) == ObjectMoveBlockReason.Obstacle;
                case ObjectAICondition.CliffAhead:
                    return ProbeMoveBlock(moveDirection, rule.settings, false) == ObjectMoveBlockReason.Cliff;
                case ObjectAICondition.ActionFinished:
                    return actionFinishedPending;
                case ObjectAICondition.TargetDead:
                    return targetDeadPending || (target != null && target.IsDead);
                case ObjectAICondition.AllyAlertReceived:
                    return allyAlertPending;
                case ObjectAICondition.ContactingPlayer:
                    return Time.time <= contactUntil;
                case ObjectAICondition.RandomChance:
                    if (rawCheck || index < 0 || index >= nextRandomDecisionAt.Length)
                        return false;
                    if (Time.time < nextRandomDecisionAt[index])
                        return false;
                    nextRandomDecisionAt[index] = Time.time + Mathf.Max(0.05f,
                        condition.intervalSeconds.x);
                    return Random.value <= condition.chance;
                default:
                    return false;
            }
        }

        private ObjectAIConditionParameters ResolveCondition(ObjectAIRule rule)
        {
            ObjectAIConditionParameters source = rule != null && rule.condition != null
                ? rule.condition
                : new ObjectAIConditionParameters();
            if (Program == null || !Program.useLegacyTuning || Data == null)
                return source;

            ObjectAIConditionParameters resolved = source.Clone();
            if (rule.when == ObjectAICondition.TargetDetected ||
                rule.when == ObjectAICondition.TargetEnteredDetection)
            {
                resolved.frontDistance = Data.detection.frontDistance;
                resolved.rearDistance = Data.detection.rearDistance;
                resolved.verticalRange = Data.detection.verticalRange;
                resolved.targetMask = Data.detection.targetMask;
                resolved.requireLineOfSight = Data.detection.requireLineOfSight;
                resolved.sightBlockingMask = Data.detection.sightBlockingMask;
            }
            else if (rule.when == ObjectAICondition.TargetWithinAttackRange)
            {
                resolved.distance = Data.attack.attackRange;
            }
            return resolved;
        }

        private void ApplyLegacyTuning(ObjectAIAction action, ObjectAIActionParameters settings)
        {
            if (Program == null || !Program.useLegacyTuning || Data == null || settings == null)
                return;

            bool movementAction = ObjectBehaviorCatalog.IsMovementAction(action) ||
                                  action == ObjectAIAction.Patrol ||
                                  action == ObjectAIAction.Wander;
            if (movementAction)
            {
                settings.groundMovement = Data.movement.groundWalker;
                settings.smallStepHeight = Data.movement.smallStepHeight;
                settings.obstacleProbeDistance = Data.movement.obstacleProbeDistance;
                settings.cliffProbeDistance = Data.movement.cliffProbeDistance;
                settings.groundMask = Data.movement.groundMask;
            }

            if (action == ObjectAIAction.Patrol || action == ObjectAIAction.Wander)
            {
                settings.moveSeconds = Data.movement.patrolMoveSeconds;
                settings.durationSeconds = Data.movement.idleSeconds;
                settings.turnChance = Data.movement.turnChanceOnMove;
                settings.speedMultiplier = 1f;
            }

            if (action == ObjectAIAction.FollowTarget ||
                action == ObjectAIAction.FollowDamageSource ||
                action == ObjectAIAction.KeepDistance)
            {
                settings.speedMultiplier = Data.chase.speedMultiplier;
                settings.minimumFollowSeconds = Data.chase.minimumChaseSeconds;
                settings.memorySeconds = Mathf.Max(
                    Data.chase.detectionMemorySeconds,
                    Data.chase.lostSightChaseSeconds);
                settings.maxDistanceFromSpawn = Data.chase.maxDistanceFromSpawn;
                settings.giveUpTargetDistance = Data.chase.giveUpTargetDistance;
                settings.durationSeconds = new Vector2(
                    Data.chase.unreachableLookSeconds,
                    Data.chase.unreachableLookSeconds);
            }

            if (action == ObjectAIAction.SearchLastSeenThenReturn)
            {
                float memory = Mathf.Max(
                    Data.chase.detectionMemorySeconds,
                    Data.chase.lostSightChaseSeconds);
                settings.durationSeconds = new Vector2(memory, memory);
                settings.speedMultiplier = Data.chase.speedMultiplier;
            }

            if (ObjectBehaviorCatalog.IsAttackAction(action))
            {
                settings.damage = Data.attack.attackDamage;
                settings.attackRange = Data.attack.attackRange;
                settings.prepareSeconds = Data.attack.prepareSeconds;
                settings.activeSeconds = Data.attack.activeSeconds;
                settings.recoverySeconds = Data.attack.recoverySeconds;
                settings.cooldownSeconds = Data.attack.cooldownSeconds;
                settings.hitboxHeight = Data.attack.hitboxHeight;
                settings.targetInvulnerabilitySeconds = Data.attack.targetInvulnerabilitySeconds;
                settings.knockback = 0f;
            }
        }

        private void EnterRule(int index)
        {
            audioController?.StopBehavior();
            activeRuleIndex = index;
            actionHoldsAfterCondition = false;
            attackLocksRules = false;
            attackApplied = false;
            searchReturning = false;
            blockedSeconds = 0f;
            actionStartedAt = Time.time;
            actor.SetEnemyRuleAnimation(index, ObjectEnemyAnimatorPhase.None);

            ObjectAIRule rule = ActiveRule;
            if (rule == null)
            {
                activeSettings = null;
                BeginState(ObjectBrainState.Idle, 0f);
                return;
            }

            activeSettings = rule.settings != null
                ? rule.settings.Clone()
                : new ObjectAIActionParameters();
            ApplyLegacyTuning(rule.action, activeSettings);
            if (effects != null)
                effects.Play(rule.effect, rule.effectSettings, moveDirection);
            if (audioController != null)
            {
                bool destroysImmediately = rule.action == ObjectAIAction.SelfDestruct ||
                                           rule.action == ObjectAIAction.DestroySelf;
                if (destroysImmediately)
                {
                    audioController.PlayDetachedOneShot(rule.conditionSound);
                    audioController.PlayDetachedOneShot(rule.actionSound);
                }
                else
                {
                    audioController.PlayOneShot(rule.conditionSound);
                    audioController.PlayBehavior(rule.actionSound);
                }
            }
            if (rule.when == ObjectAICondition.HealthReached &&
                index >= 0 && index < playedHealthReachedEffects.Length)
                playedHealthReachedEffects[index] = true;
            if (rule.when == ObjectAICondition.EveryInterval && index < nextIntervalAt.Length)
                nextIntervalAt[index] = Time.time + RandomRange(rule.condition.intervalSeconds);

            switch (rule.action)
            {
                case ObjectAIAction.None:
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.Stop:
                    StopHorizontalMovement();
                    BeginState(ObjectBrainState.Idle, RandomRange(activeSettings.durationSeconds));
                    actionHoldsAfterCondition = true;
                    break;
                case ObjectAIAction.Wait:
                    StopHorizontalMovement();
                    BeginState(ObjectBrainState.Idle, RandomRange(activeSettings.durationSeconds));
                    actionHoldsAfterCondition = true;
                    break;
                case ObjectAIAction.Patrol:
                case ObjectAIAction.Wander:
                    StartPatrol(rule.action == ObjectAIAction.Wander);
                    break;
                case ObjectAIAction.Guard:
                    StopHorizontalMovement();
                    BeginState(ObjectBrainState.Alert, RandomRange(activeSettings.durationSeconds));
                    break;
                case ObjectAIAction.FollowTarget:
                case ObjectAIAction.FollowDamageSource:
                    if (rule.action == ObjectAIAction.FollowDamageSource && lastDamageSource != null)
                        target = lastDamageSource;
                    StartFollow();
                    break;
                case ObjectAIAction.SearchLastSeenThenReturn:
                    BeginState(ObjectBrainState.Search, RandomRange(activeSettings.durationSeconds));
                    actionHoldsAfterCondition = true;
                    break;
                case ObjectAIAction.KeepDistance:
                    BeginState(ObjectBrainState.Chase, 0f);
                    break;
                case ObjectAIAction.FaceTarget:
                    FaceTarget();
                    StopHorizontalMovement();
                    BeginState(ObjectBrainState.Alert, 0f);
                    actionHoldsAfterCondition = true;
                    break;
                case ObjectAIAction.FleeTarget:
                    BeginState(ObjectBrainState.Flee, RandomRange(activeSettings.durationSeconds));
                    break;
                case ObjectAIAction.ReturnToSpawn:
                    BeginState(ObjectBrainState.Return, 0f);
                    actionHoldsAfterCondition = true;
                    break;
                case ObjectAIAction.TurnAround:
                    SetDirection(-moveDirection);
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.RandomTurn:
                    SetDirection(Random.value < 0.5f ? -1f : 1f);
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.Jump:
                    ApplyJump(activeSettings.jumpForce);
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.Hover:
                    BeginState(ObjectBrainState.Patrol, RandomRange(activeSettings.moveSeconds));
                    break;
                case ObjectAIAction.Search:
                    BeginState(ObjectBrainState.Search, RandomRange(activeSettings.durationSeconds));
                    actionHoldsAfterCondition = true;
                    nextDirectionDecisionAt = Time.time + Mathf.Max(0.1f,
                        activeSettings.durationSeconds.x * 0.25f);
                    break;
                case ObjectAIAction.Attack:
                    StartAttack(activeSettings.attackPattern);
                    break;
                case ObjectAIAction.Charge:
                    StartAttack(ObjectAttackPattern.Charge);
                    break;
                case ObjectAIAction.Shoot:
                    StartAttack(ObjectAttackPattern.SingleProjectile);
                    break;
                case ObjectAIAction.BurstShoot:
                    StartAttack(ObjectAttackPattern.TripleProjectile);
                    break;
                case ObjectAIAction.AreaAttack:
                    StartAttack(ObjectAttackPattern.AreaPulse);
                    break;
                case ObjectAIAction.AlertAllies:
                    AlertAllies(activeSettings.allyAlertRadius);
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.TeleportBehind:
                    TeleportBehindTarget(activeSettings);
                    CompleteOneShotRule();
                    break;
                case ObjectAIAction.SelfDestruct:
                    ApplyAreaAttack(activeSettings, 1.5f);
                    actor.ForceDeath(gameObject);
                    break;
                case ObjectAIAction.DestroySelf:
                    actor.ForceDestroy(gameObject);
                    break;
                case ObjectAIAction.PlayMotion:
                    BeginState(ObjectBrainState.Idle, RandomRange(activeSettings.durationSeconds));
                    actionHoldsAfterCondition = true;
                    break;
            }
        }

        private void TickActiveAction()
        {
            ObjectAIRule rule = ActiveRule;
            if (rule == null || activeSettings == null)
                return;

            switch (rule.action)
            {
                case ObjectAIAction.Stop:
                case ObjectAIAction.Wait:
                case ObjectAIAction.PlayMotion:
                    if (stateTimer <= 0f && activeSettings.durationSeconds.y > 0f)
                        CompleteAction(false);
                    break;
                case ObjectAIAction.Patrol:
                case ObjectAIAction.Wander:
                    UpdatePatrol(rule.action == ObjectAIAction.Wander);
                    break;
                case ObjectAIAction.Guard:
                    if (stateTimer <= 0f)
                    {
                        SetDirection(-moveDirection);
                        BeginState(ObjectBrainState.Alert, RandomRange(activeSettings.durationSeconds));
                    }
                    break;
                case ObjectAIAction.FollowTarget:
                case ObjectAIAction.FollowDamageSource:
                case ObjectAIAction.KeepDistance:
                    UpdateFollow();
                    break;
                case ObjectAIAction.SearchLastSeenThenReturn:
                    UpdateSearchThenReturn();
                    break;
                case ObjectAIAction.FaceTarget:
                    FaceTarget();
                    break;
                case ObjectAIAction.FleeTarget:
                    if (stateTimer <= 0f && activeSettings.durationSeconds.y > 0f)
                        CompleteAction(false);
                    break;
                case ObjectAIAction.ReturnToSpawn:
                    UpdateReturn();
                    break;
                case ObjectAIAction.Search:
                    if (Time.time >= nextDirectionDecisionAt)
                    {
                        SetDirection(-moveDirection);
                        nextDirectionDecisionAt = Time.time + Mathf.Max(0.1f,
                            activeSettings.durationSeconds.x * 0.25f);
                    }
                    if (stateTimer <= 0f)
                        CompleteAction(false);
                    break;
                case ObjectAIAction.Attack:
                case ObjectAIAction.Charge:
                case ObjectAIAction.Shoot:
                case ObjectAIAction.BurstShoot:
                case ObjectAIAction.AreaAttack:
                    UpdateAttack();
                    break;
            }
        }

        private void StartPatrol(bool forceRandom)
        {
            patrolMoving = true;
            ObjectAIPatrolPattern pattern = forceRandom
                ? ObjectAIPatrolPattern.RandomDirection
                : activeSettings.patrolPattern;
            if (pattern == ObjectAIPatrolPattern.RandomDirection)
                SetDirection(Random.value < 0.5f ? -1f : 1f);
            else if (pattern == ObjectAIPatrolPattern.ShortPingPong)
                SetDirection(-moveDirection);
            else if (pattern == ObjectAIPatrolPattern.ChanceTurn &&
                     Random.value <= activeSettings.turnChance)
                SetDirection(-moveDirection);

            float duration = RandomRange(activeSettings.moveSeconds);
            if (pattern == ObjectAIPatrolPattern.ShortPingPong)
                duration = Mathf.Min(duration, 0.8f);
            BeginState(ObjectBrainState.Patrol, duration);
        }

        private void UpdatePatrol(bool forceRandom)
        {
            if (stateTimer > 0f)
                return;

            if (patrolMoving)
            {
                patrolMoving = false;
                BeginState(ObjectBrainState.Idle, RandomRange(activeSettings.durationSeconds));
            }
            else
            {
                StartPatrol(forceRandom);
            }
        }

        private void StartFollow()
        {
            if (target == null || target.IsDead)
            {
                CompleteAction(false);
                return;
            }
            actionStartedAt = Time.time;
            BeginState(ObjectBrainState.Chase, 0f);
        }

        private void UpdateFollow()
        {
            if (searchReturning)
            {
                UpdateReturn();
                return;
            }

            if (target == null || target.IsDead)
            {
                CompleteAction(false);
                return;
            }

            FaceTarget();
            if (activeSettings.maxDistanceFromSpawn > 0f &&
                Vector2.Distance(body.position, spawnPosition) > activeSettings.maxDistanceFromSpawn)
            {
                target = null;
                CompleteAction(false);
                return;
            }

            if (activeSettings.giveUpTargetDistance > 0f &&
                Vector2.Distance(body.position, target.transform.position) >
                activeSettings.giveUpTargetDistance &&
                Time.time - actionStartedAt >= activeSettings.minimumFollowSeconds &&
                activeSettings.followPattern != ObjectAIFollowPattern.Relentless)
            {
                targetLostQueued = true;
            }

            if (!wasDetected && activeSettings.followPattern != ObjectAIFollowPattern.Relentless &&
                Time.time - lastDetectedAt > activeSettings.memorySeconds)
                targetLostQueued = true;
        }

        private void UpdateSearchThenReturn()
        {
            if (!searchReturning)
            {
                if (Vector2.Distance(body.position, lastSeenPosition) <= 0.15f || stateTimer <= 0f)
                {
                    searchReturning = true;
                    BeginState(ObjectBrainState.Return, 0f);
                }
            }
            else
            {
                UpdateReturn();
            }
        }

        private void UpdateReturn()
        {
            if (Vector2.Distance(body.position, spawnPosition) > 0.15f)
                return;
            body.position = spawnPosition;
            target = null;
            wasDetected = false;
            CompleteAction(false);
        }

        private void StartAttack(ObjectAttackPattern pattern)
        {
            activeAttackPattern = pattern;
            attackLocksRules = true;
            attackApplied = false;
            FaceTarget();
            float prepare = activeSettings.prepareSeconds;
            if (pattern == ObjectAttackPattern.HeavySmash)
                prepare *= 1.4f;
            else if (pattern == ObjectAttackPattern.RapidCombo)
                prepare *= 0.55f;
            BeginState(ObjectBrainState.AttackPrepare, prepare);
        }

        private void UpdateAttack()
        {
            switch (state)
            {
                case ObjectBrainState.AttackPrepare:
                    FaceTarget();
                    if (stateTimer <= 0f)
                    {
                        if (activeAttackPattern == ObjectAttackPattern.LeapStrike)
                            ApplyJump(activeSettings.jumpForce);
                        BeginState(ObjectBrainState.AttackActive, activeSettings.activeSeconds);
                    }
                    break;
                case ObjectBrainState.AttackActive:
                    if (!attackApplied)
                    {
                        attackApplied = true;
                        PerformAttack(activeAttackPattern, activeSettings);
                    }
                    if (stateTimer <= 0f)
                        BeginState(ObjectBrainState.AttackRecover, activeSettings.recoverySeconds);
                    break;
                case ObjectBrainState.AttackRecover:
                    if (stateTimer <= 0f)
                        BeginState(ObjectBrainState.AttackCooldown, activeSettings.cooldownSeconds);
                    break;
                case ObjectBrainState.AttackCooldown:
                    if (stateTimer <= 0f)
                    {
                        attackLocksRules = false;
                        CompleteAction(false);
                    }
                    break;
            }
        }

        private bool TryGetMovement(
            ObjectAIAction action,
            ObjectAIActionParameters settings,
            out float direction,
            out float speed,
            out float verticalVelocity)
        {
            direction = moveDirection;
            speed = 0f;
            verticalVelocity = 0f;
            float baseSpeed = Data.core.moveSpeed * settings.speedMultiplier;

            if (searchReturning)
            {
                direction = Mathf.Sign(spawnPosition.x - body.position.x);
                speed = baseSpeed;
                return true;
            }

            switch (action)
            {
                case ObjectAIAction.Patrol:
                case ObjectAIAction.Wander:
                    if (!patrolMoving)
                        return false;
                    speed = baseSpeed;
                    switch (settings.patrolPattern)
                    {
                        case ObjectAIPatrolPattern.BurstAndPause:
                            speed *= Mathf.Repeat(Time.time, 1f) < 0.42f ? 1.8f : 0f;
                            break;
                        case ObjectAIPatrolPattern.CreepAndFreeze:
                            speed *= Mathf.Repeat(Time.time, 1.2f) < 0.7f ? 0.45f : 0f;
                            break;
                        case ObjectAIPatrolPattern.HopAlong:
                            if (Time.time >= nextDirectionDecisionAt)
                            {
                                ApplyJump(settings.jumpForce * 0.55f);
                                nextDirectionDecisionAt = Time.time + 0.65f;
                            }
                            break;
                        case ObjectAIPatrolPattern.TurnInPlace:
                            speed = 0f;
                            break;
                    }
                    return true;

                case ObjectAIAction.FollowTarget:
                case ObjectAIAction.FollowDamageSource:
                    return TryGetFollowMovement(settings, false, out direction, out speed, out verticalVelocity);
                case ObjectAIAction.KeepDistance:
                    return TryGetFollowMovement(settings, true, out direction, out speed, out verticalVelocity);
                case ObjectAIAction.FleeTarget:
                    if (target == null)
                        return false;
                    direction = -Mathf.Sign(target.transform.position.x - transform.position.x);
                    speed = baseSpeed;
                    return true;
                case ObjectAIAction.ReturnToSpawn:
                    direction = Mathf.Sign(spawnPosition.x - body.position.x);
                    speed = baseSpeed;
                    return true;
                case ObjectAIAction.SearchLastSeenThenReturn:
                    direction = searchReturning
                        ? Mathf.Sign(spawnPosition.x - body.position.x)
                        : Mathf.Sign(lastSeenPosition.x - body.position.x);
                    speed = baseSpeed;
                    return true;
                case ObjectAIAction.Search:
                    speed = baseSpeed * 0.65f;
                    return true;
                case ObjectAIAction.Hover:
                    speed = baseSpeed;
                    verticalVelocity = Mathf.Sin(Time.time * 3f) * Mathf.Max(0.25f, baseSpeed * 0.65f);
                    return true;
                case ObjectAIAction.Attack:
                case ObjectAIAction.Charge:
                    if (state == ObjectBrainState.AttackActive &&
                        activeAttackPattern == ObjectAttackPattern.Charge)
                    {
                        direction = actor.IsFacingRight ? 1f : -1f;
                        speed = baseSpeed * settings.dashSpeedMultiplier;
                        return true;
                    }
                    if (state == ObjectBrainState.AttackRecover &&
                        activeAttackPattern == ObjectAttackPattern.HitAndRun && target != null)
                    {
                        direction = -Mathf.Sign(target.transform.position.x - transform.position.x);
                        speed = baseSpeed * 1.25f;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private bool TryGetFollowMovement(
            ObjectAIActionParameters settings,
            bool forceKeepDistance,
            out float direction,
            out float speed,
            out float verticalVelocity)
        {
            direction = 0f;
            speed = 0f;
            verticalVelocity = 0f;
            if (target == null)
                return false;

            Vector2 targetPosition = target.transform.position;
            Vector2 targetVelocity = target.Body != null ? target.Body.linearVelocity : Vector2.zero;
            ObjectAIFollowPattern pattern = forceKeepDistance
                ? ObjectAIFollowPattern.KeepAttackDistance
                : settings.followPattern;

            if (pattern == ObjectAIFollowPattern.Predictive)
                targetPosition += targetVelocity * 0.45f;
            else if (pattern == ObjectAIFollowPattern.Intercept)
                targetPosition += targetVelocity * 0.9f;

            float deltaX = targetPosition.x - transform.position.x;
            direction = Mathf.Sign(deltaX);
            speed = Data.core.moveSpeed * settings.speedMultiplier;

            switch (pattern)
            {
                case ObjectAIFollowPattern.BurstSprint:
                    speed *= Mathf.Repeat(Time.time, 1.25f) < 0.5f ? 1.65f : 0.45f;
                    break;
                case ObjectAIFollowPattern.Stalker:
                    float targetFacing = target.IsFacingRight ? 1f : -1f;
                    bool targetLooksAtActor = targetFacing *
                        (transform.position.x - target.transform.position.x) > 0f;
                    speed *= targetLooksAtActor ? 0.35f : 1.25f;
                    break;
                case ObjectAIFollowPattern.KeepAttackDistance:
                    float desired = Mathf.Max(0.05f, settings.distance > 0f
                        ? settings.distance
                        : settings.attackRange);
                    if (Mathf.Abs(deltaX) < desired * 0.8f)
                        direction *= -1f;
                    else if (Mathf.Abs(deltaX) <= desired * 1.15f)
                        direction = 0f;
                    break;
                case ObjectAIFollowPattern.ZigZag:
                    speed *= 0.75f + Mathf.Abs(Mathf.Sin(Time.time * 5f)) * 0.75f;
                    if (Time.time >= nextDirectionDecisionAt)
                    {
                        ApplyJump(settings.jumpForce * 0.35f);
                        nextDirectionDecisionAt = Time.time + 0.75f;
                    }
                    break;
                case ObjectAIFollowPattern.GuardSpawn:
                    if (settings.maxDistanceFromSpawn > 0f &&
                        Vector2.Distance(body.position, spawnPosition) >
                        settings.maxDistanceFromSpawn * 0.65f)
                        direction = Mathf.Sign(spawnPosition.x - body.position.x);
                    break;
                case ObjectAIFollowPattern.ShadowFollow:
                    speed *= 0.55f;
                    break;
            }
            return true;
        }

        private ObjectMoveBlockReason ProbeMoveBlock(
            float direction,
            ObjectAIActionParameters settings,
            bool allowStep)
        {
            if (settings == null || !settings.groundMovement || bodyCollider == null ||
                Mathf.Abs(direction) < 0.001f ||
                settings.obstacleResponse == ObjectAITerrainResponse.Ignore &&
                settings.cliffResponse == ObjectAITerrainResponse.Ignore)
                return ObjectMoveBlockReason.None;

            Bounds bounds = bodyCollider.bounds;
            float edgeX = direction > 0f ? bounds.max.x : bounds.min.x;
            Vector2 horizontal = Vector2.right * direction;
            Vector2 lowerOrigin = new Vector2(edgeX + direction * 0.02f, bounds.min.y + 0.08f);
            RaycastHit2D obstacle = Physics2D.Raycast(
                lowerOrigin, horizontal, settings.obstacleProbeDistance, settings.groundMask);

            if (obstacle.collider != null && !obstacle.collider.transform.IsChildOf(transform))
            {
                float stepHeight = obstacle.collider.bounds.max.y - bounds.min.y;
                Vector2 upperOrigin = new Vector2(lowerOrigin.x,
                    bounds.min.y + settings.smallStepHeight + 0.04f);
                RaycastHit2D upperBlock = Physics2D.Raycast(
                    upperOrigin, horizontal, settings.obstacleProbeDistance, settings.groundMask);
                bool upperClear = upperBlock.collider == null || upperBlock.collider.transform.IsChildOf(transform);
                if (allowStep && stepHeight > 0f && stepHeight <= settings.smallStepHeight && upperClear)
                {
                    body.MovePosition(body.position + Vector2.up * (stepHeight + 0.02f));
                }
                else if (settings.obstacleResponse != ObjectAITerrainResponse.Ignore)
                {
                    return ObjectMoveBlockReason.Obstacle;
                }
            }

            if (settings.cliffResponse == ObjectAITerrainResponse.Ignore)
                return ObjectMoveBlockReason.None;

            float aheadX = edgeX + direction * settings.obstacleProbeDistance;
            Vector2 groundOrigin = new Vector2(aheadX,
                bounds.min.y + settings.smallStepHeight + 0.05f);
            RaycastHit2D ground = Physics2D.Raycast(
                groundOrigin,
                Vector2.down,
                settings.smallStepHeight + settings.cliffProbeDistance,
                settings.groundMask);
            return ground.collider == null || ground.collider.transform.IsChildOf(transform)
                ? ObjectMoveBlockReason.Cliff
                : ObjectMoveBlockReason.None;
        }

        private bool HandleMoveBlocked(ObjectMoveBlockReason reason, ObjectAIActionParameters settings)
        {
            ObjectAITerrainResponse response = reason == ObjectMoveBlockReason.Obstacle
                ? settings.obstacleResponse
                : settings.cliffResponse;
            switch (response)
            {
                case ObjectAITerrainResponse.Ignore:
                    return true;
                case ObjectAITerrainResponse.TurnAround:
                    SetDirection(-moveDirection);
                    return false;
                case ObjectAITerrainResponse.Jump:
                    ApplyJump(settings.jumpForce);
                    return false;
                case ObjectAITerrainResponse.ReturnToSpawn:
                    detectionLockedTarget = target;
                    BeginState(ObjectBrainState.Return, 0f);
                    searchReturning = true;
                    actionHoldsAfterCondition = true;
                    return false;
                case ObjectAITerrainResponse.WatchThenReturn:
                    blockedSeconds += Time.fixedDeltaTime;
                    if (blockedSeconds >= Mathf.Max(0.1f, settings.durationSeconds.x))
                    {
                        detectionLockedTarget = target;
                        BeginState(ObjectBrainState.Return, 0f);
                        searchReturning = true;
                        actionHoldsAfterCondition = true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private void PerformAttack(ObjectAttackPattern pattern, ObjectAIActionParameters settings)
        {
            switch (pattern)
            {
                case ObjectAttackPattern.HeavySmash:
                    ApplyMeleeAttack(settings, 2f, 1.35f, 1.8f);
                    break;
                case ObjectAttackPattern.RapidCombo:
                    StartCoroutine(RapidComboRoutine(settings.Clone()));
                    break;
                case ObjectAttackPattern.Charge:
                    ApplyMeleeAttack(settings, 1.35f, 1.2f, 1.4f);
                    break;
                case ObjectAttackPattern.LeapStrike:
                    ApplyMeleeAttack(settings, 1.5f, 1.4f, 1.5f);
                    break;
                case ObjectAttackPattern.SingleProjectile:
                    SpawnProjectile(settings, 0f);
                    break;
                case ObjectAttackPattern.TripleProjectile:
                    SpawnProjectile(settings, 0f);
                    SpawnProjectile(settings, 0.24f);
                    SpawnProjectile(settings, -0.24f);
                    break;
                case ObjectAttackPattern.AreaPulse:
                    ApplyAreaAttack(settings, 1f);
                    break;
                case ObjectAttackPattern.HitAndRun:
                    ApplyMeleeAttack(settings, 1f, 1f, 1f);
                    break;
                case ObjectAttackPattern.SelfDestruct:
                    ApplyAreaAttack(settings, 1.5f);
                    actor.ForceDeath(gameObject);
                    break;
                case ObjectAttackPattern.SniperBeam:
                    ApplySniperAttack(settings);
                    break;
                default:
                    ApplyMeleeAttack(settings, 1f, 1f, 1f);
                    break;
            }
        }

        private IEnumerator RapidComboRoutine(ObjectAIActionParameters settings)
        {
            for (int i = 0; i < 3; i++)
            {
                ApplyMeleeAttack(settings, 0.6f, 1f, 0.55f, 0.03f);
                if (i < 2)
                    yield return new WaitForSeconds(0.06f);
            }
        }

        private void ApplyMeleeAttack(
            ObjectAIActionParameters settings,
            float damageMultiplier,
            float rangeMultiplier,
            float knockbackMultiplier,
            float? invulnerabilityOverride = null)
        {
            if (target == null || target.IsDead)
                return;
            Vector2 delta = target.transform.position - transform.position;
            float facing = actor.IsFacingRight ? 1f : -1f;
            if (delta.x * facing < 0f || Mathf.Abs(delta.x) > settings.attackRange * rangeMultiplier ||
                Mathf.Abs(delta.y) > settings.hitboxHeight * 0.5f)
                return;

            int baseDamage = Mathf.Max(Data.core.attackPower, settings.damage);
            int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * damageMultiplier));
            target.TryReceiveDamage(new ObjectDamageRequest(
                damage,
                gameObject,
                transform.position,
                invulnerabilityOverride ?? settings.targetInvulnerabilitySeconds,
                settings.knockback * knockbackMultiplier,
                ObjectDamageCause.Attack));
        }

        private void SpawnProjectile(ObjectAIActionParameters settings, float verticalDirection)
        {
            float facing = actor.IsFacingRight ? 1f : -1f;
            Vector2 direction = new Vector2(facing, verticalDirection).normalized;
            Vector2 origin = bodyCollider != null
                ? (Vector2)bodyCollider.bounds.center + direction * (bodyCollider.bounds.extents.x + 0.12f)
                : (Vector2)transform.position + direction * 0.5f;
            int damage = Mathf.Max(Data.core.attackPower, settings.damage);
            ObjectProjectile2D.Spawn(
                actor,
                origin,
                direction * settings.projectileSpeed,
                damage,
                settings.targetInvulnerabilitySeconds,
                settings.knockback,
                settings.projectileLifetime,
                Color.white);
        }

        private void ApplyAreaAttack(ObjectAIActionParameters settings, float multiplier)
        {
            float range = Mathf.Max(0.05f, settings.attackRange * multiplier);
            int damage = Mathf.Max(Data.core.attackPower, settings.damage);
            var hitActors = ObjectMakerRegistry.Actors;
            for (int i = 0; i < hitActors.Count; i++)
            {
                ObjectActor2D candidate = hitActors[i];
                if (candidate == null || candidate == actor || candidate.IsDead ||
                    candidate.Kind != ObjectKind.Player ||
                    Vector2.Distance(transform.position, candidate.transform.position) > range)
                    continue;
                candidate.TryReceiveDamage(new ObjectDamageRequest(
                    damage,
                    gameObject,
                    transform.position,
                    settings.targetInvulnerabilitySeconds,
                    settings.knockback,
                    ObjectDamageCause.Attack));
            }
        }

        private void ApplySniperAttack(ObjectAIActionParameters settings)
        {
            if (target == null || target.IsDead ||
                Vector2.Distance(transform.position, target.transform.position) > settings.attackRange)
                return;
            int damage = Mathf.Max(Data.core.attackPower, settings.damage);
            target.TryReceiveDamage(new ObjectDamageRequest(
                Mathf.Max(1, Mathf.RoundToInt(damage * 1.5f)),
                gameObject,
                transform.position,
                settings.targetInvulnerabilitySeconds,
                settings.knockback,
                ObjectDamageCause.Attack));
        }

        private void AlertAllies(float radius)
        {
            if (target == null)
                return;
            var actors = ObjectMakerRegistry.Actors;
            for (int i = 0; i < actors.Count; i++)
            {
                ObjectActor2D ally = actors[i];
                if (ally == null || ally == actor || ally.IsDead || ally.Kind != ObjectKind.Monster ||
                    Vector2.Distance(transform.position, ally.transform.position) > radius)
                    continue;
                ObjectMonsterBrain2D brain = ally.GetComponent<ObjectMonsterBrain2D>();
                if (brain != null)
                    brain.ReceiveAllyAlert(target);
            }
        }

        private void TeleportBehindTarget(ObjectAIActionParameters settings)
        {
            if (target == null || Time.time - lastTeleportAt < settings.teleportCooldown)
                return;
            float targetFacing = target.IsFacingRight ? 1f : -1f;
            Vector2 destination = (Vector2)target.transform.position -
                                  Vector2.right * targetFacing * settings.teleportDistance;
            if (settings.maxDistanceFromSpawn <= 0f ||
                Vector2.Distance(destination, spawnPosition) <= settings.maxDistanceFromSpawn)
            {
                body.position = destination;
                SetDirection(targetFacing);
                lastTeleportAt = Time.time;
            }
        }

        private void ApplyJump(float force)
        {
            if (body == null || force <= 0f)
                return;
            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
            body.AddForce(Vector2.up * force, ForceMode2D.Impulse);
        }

        private void FaceTarget()
        {
            if (target == null)
                return;
            float direction = Mathf.Sign(target.transform.position.x - transform.position.x);
            if (Mathf.Abs(direction) > 0.001f)
                SetDirection(direction);
        }

        private void SetDirection(float direction)
        {
            if (Mathf.Abs(direction) < 0.001f)
                return;
            moveDirection = direction > 0f ? 1f : -1f;
            actor.SetFacing(moveDirection);
        }

        private void BeginState(ObjectBrainState nextState, float duration)
        {
            state = nextState;
            stateTimer = Mathf.Max(0f, duration);
            attackApplied = false;
            ObjectEnemyAnimatorPhase phase = ObjectEnemyAnimatorPhase.None;
            switch (nextState)
            {
                case ObjectBrainState.AttackPrepare:
                    phase = ObjectEnemyAnimatorPhase.Prepare;
                    break;
                case ObjectBrainState.AttackActive:
                    phase = ObjectEnemyAnimatorPhase.Active;
                    break;
                case ObjectBrainState.AttackRecover:
                    phase = ObjectEnemyAnimatorPhase.Recover;
                    break;
            }
            if (actor != null)
                actor.SetEnemyRuleAnimation(activeRuleIndex, phase);
        }

        private void CompleteOneShotRule()
        {
            if (activeRuleIndex >= 0 && activeRuleIndex < consumedRules.Length)
                consumedRules[activeRuleIndex] = true;
            CompleteAction(true);
        }

        private void CompleteAction(bool preserveState)
        {
            audioController?.StopBehavior();
            activeRuleIndex = -1;
            activeSettings = null;
            actionHoldsAfterCondition = false;
            attackLocksRules = false;
            actionFinishedPending = true;
            if (actor != null)
                actor.SetEnemyRuleAnimation(-1, ObjectEnemyAnimatorPhase.None);
            if (!preserveState)
                BeginState(ObjectBrainState.Idle, 0f);
        }

        private void StopHorizontalMovement()
        {
            if (body != null)
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (actor != null)
                actor.SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, 0f);
        }

        private void OnActorDamaged(ObjectDamageResult result)
        {
            if (result.IsDead || Data == null)
                return;
            if (result.Request.Source != null)
                lastDamageSource = result.Request.Source.GetComponentInParent<ObjectActor2D>();
            if (lastDamageSource != null)
            {
                target = lastDamageSource;
                lastSeenPosition = lastDamageSource.transform.position;
            }
            damagedPending = true;
            hitStopRemaining = Data.hit.hitStopSeconds;
            if (Data.hit.interruptCurrentAction)
            {
                audioController?.StopBehavior();
                attackLocksRules = false;
                actionHoldsAfterCondition = false;
                activeRuleIndex = -1;
                activeSettings = null;
                actor.SetEnemyRuleAnimation(-1, ObjectEnemyAnimatorPhase.None);
                BeginState(ObjectBrainState.Hit, Mathf.Max(0.05f, Data.hit.hitStopSeconds));
            }
        }

        private void OnActorDied(ObjectActor2D deadActor)
        {
            audioController?.StopBehavior();
            EnsureRuleRuntimeState();
            if (Program != null && Program.rules != null)
            {
                for (int i = 0; i < Program.rules.Count; i++)
                {
                    ObjectAIRule rule = Program.rules[i];
                    if (rule == null || !rule.enabled ||
                        rule.when != ObjectAICondition.HealthReached ||
                        deadActor.CurrentHealth > Mathf.Max(0, rule.condition.healthValue) ||
                        (i < playedHealthReachedEffects.Length && playedHealthReachedEffects[i]))
                        continue;

                    if (effects != null)
                        effects.Play(rule.effect, rule.effectSettings, moveDirection);
                    if (audioController != null)
                    {
                        // HP 0 is handled synchronously by ObjectActor2D before another AI
                        // Update can enter this rule. Detach its condition cue so disabling
                        // the dead root in the same frame cannot cut it off. Only immediate
                        // removal actions are fulfilled by this death path; unrelated action
                        // cues (Attack, Follow, and so on) must not be played.
                        audioController.PlayDetachedOneShot(rule.conditionSound);
                        if (rule.action == ObjectAIAction.SelfDestruct ||
                            rule.action == ObjectAIAction.DestroySelf)
                            audioController.PlayDetachedOneShot(rule.actionSound);
                    }
                    if (i < playedHealthReachedEffects.Length)
                        playedHealthReachedEffects[i] = true;
                }
            }
            actor.SetEnemyRuleAnimation(-1, ObjectEnemyAnimatorPhase.None);
            BeginState(ObjectBrainState.Dead, 0f);
            StopHorizontalMovement();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandlePlayerContact(collision != null ? collision.collider : null);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            HandlePlayerContact(collision != null ? collision.collider : null);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandlePlayerContact(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            HandlePlayerContact(other);
        }

        private void HandlePlayerContact(Collider2D other)
        {
            if (other == null || actor == null || actor.IsDead || Data == null)
                return;
            ObjectActor2D otherActor = other.GetComponentInParent<ObjectActor2D>();
            if (otherActor == null || otherActor == actor || otherActor.Kind != ObjectKind.Player)
                return;

            contactUntil = Time.time + 0.1f;
            if (Data.attack.contactDamage <= 0)
                return;
            otherActor.TryReceiveDamage(new ObjectDamageRequest(
                Data.attack.contactDamage,
                gameObject,
                transform.position,
                Data.attack.targetInvulnerabilitySeconds,
                Data.attack.contactKnockback,
                ObjectDamageCause.Contact));
        }

        private void ClearTransientConditions()
        {
            onSpawnPending = false;
            enteredDetectionPending = false;
            targetLostPending = false;
            damagedPending = false;
            actionFinishedPending = false;
            allyAlertPending = false;
            targetDeadPending = false;
        }

        private static float RandomRange(Vector2 range)
        {
            return Random.Range(range.x, range.y);
        }
    }
}
