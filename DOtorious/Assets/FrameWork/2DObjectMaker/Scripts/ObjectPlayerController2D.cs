using System;
using System.Collections.Generic;
using JYW.FrameWork;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JYW.Game.ObjectMaker
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ObjectActor2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ObjectPlayerController2D : MonoBehaviour
    {
        private enum LocomotionSoundState
        {
            None,
            Walk,
            Run
        }

        private enum AttackPhase
        {
            None,
            InputDelay,
            Active,
            Recovery
        }

        [SerializeField] private ObjectActor2D actor;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Transform groundProbe;
        [SerializeField] private ObjectAudioController2D audioController;

        private readonly HashSet<ObjectActor2D> damagedThisStep = new HashSet<ObjectActor2D>();
        private readonly List<Collider2D> attachedColliders = new List<Collider2D>();
        private float moveInput;
        private bool runInput;
        private bool grounded;
        private bool wasGrounded;
        private AttackPhase attackPhase;
        private int attackIndex = -1;
        private int comboStep = -1;
        private bool queuedCombo;
        private float attackStepStartedAt;
        private float phaseEndsAt;
        private float[] nextAttackAt = Array.Empty<float>();
        private float hitLockUntil;
        private LocomotionSoundState locomotionSoundState;
        private float attackDirection = 1f;
        private GameObject skillVisual;
        private Vector2 lastDashPosition;
        private readonly RaycastHit2D[] dashObstacles = new RaycastHit2D[32];

        public bool IsGrounded => grounded;
        public bool IsAttacking => attackPhase != AttackPhase.None;
        public int CurrentAttackIndex => attackIndex;
        public int CurrentComboStep => comboStep;
        public float MoveInput => moveInput;
        public bool IsRunning => runInput && Mathf.Abs(moveInput) > 0.001f &&
                                 Settings != null && Settings.runKey != Key.None;

        private ObjectDefinitionData Data => actor != null ? actor.Data : null;
        private ObjectPlayerSettings Settings => Data != null ? Data.player : null;

        private void Awake()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            EnsureRuntimeArrays();
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
            RefreshGrounded();
        }

        private void OnDisable()
        {
            if (IsAttacking && Settings != null && attackIndex >= 0 && attackIndex < Settings.attacks.Count &&
                Settings.attacks[attackIndex].pattern == ObjectPlayerAttackPattern.Dash && body != null)
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (actor != null)
            {
                actor.Damaged -= OnActorDamaged;
                actor.Died -= OnActorDied;
            }
            StopLocomotionSound();
            CancelAttack();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (actor == null || actor.IsDead || Settings == null || Time.timeScale <= 0f)
                return;

            RefreshGrounded();
            ReadKeyboardInput();
            TickAttack();

            if (!IsAttacking && Time.time >= hitLockUntil)
                UpdateLocomotionAnimation();
        }

        private void FixedUpdate()
        {
            ApplyHorizontalMovement();
            if (IsAttacking && Settings.attacks[attackIndex].pattern == ObjectPlayerAttackPattern.Dash && attackPhase == AttackPhase.Active)
                TickSkill(Settings.attacks[attackIndex]);
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
            EnsureRuntimeArrays();
        }

        public void SetMoveInput(float horizontal)
        {
            moveInput = Mathf.Clamp(horizontal, -1f, 1f);
            if (Mathf.Abs(moveInput) > 0.001f && actor != null &&
                (!IsAttacking || Settings.attacks[attackIndex].pattern == ObjectPlayerAttackPattern.Melee))
                actor.SetFacing(moveInput);
            ApplyHorizontalMovement();
            if (!IsAttacking)
                UpdateLocomotionAnimation();
        }

        public void SetRunInput(bool running)
        {
            bool requested = running && Settings != null && Settings.runKey != Key.None;
            if (runInput == requested)
                return;

            runInput = requested;
            ApplyHorizontalMovement();
            if (!IsAttacking)
                UpdateLocomotionAnimation();
        }

        public bool TryJump()
        {
            if (actor == null || actor.IsDead || body == null || Settings == null || Time.timeScale <= 0f)
                return false;

            RefreshGrounded();
            if (!grounded)
                return false;

            body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
            body.AddForce(Vector2.up * Settings.jumpForce, ForceMode2D.Impulse);
            grounded = false;
            wasGrounded = false;
            actor.SetLocomotionAnimation(Mathf.Abs(body.linearVelocity.x), false);
            actor.SetAnimatorTrigger(ObjectAnimatorGraph2D.JumpParameter);
            StopLocomotionSound();
            audioController?.PlayOneShot(Settings.jumpSound);
            return true;
        }

        public bool TryStartAttack(int index)
        {
            EnsureRuntimeArrays();
            if (actor == null || actor.IsDead || Settings == null || Time.timeScale <= 0f ||
                Settings.attacks == null || index < 0 || index >= Settings.attacks.Count)
                return false;

            ObjectPlayerAttack attack = Settings.attacks[index];
            if (attack == null || attack.key == Key.None)
                return false;

            if (IsAttacking)
            {
                if (index != attackIndex || comboStep + 1 >= attack.comboSteps)
                    return false;

                float total = attack.inputDelaySeconds + attack.activeSeconds +
                              attack.recoverySeconds;
                float remaining = Mathf.Max(0f, total - (Time.time - attackStepStartedAt));
                if (remaining > attack.comboInputWindowSeconds)
                    return false;

                queuedCombo = true;
                return true;
            }

            if (index < nextAttackAt.Length && Time.time < nextAttackAt[index])
                return false;

            StartAttackStep(index, 0);
            TickAttack();
            return true;
        }

        public bool RefreshGrounded()
        {
            wasGrounded = grounded;
            grounded = false;
            if (groundProbe == null || Settings == null)
                return false;

            Vector2 groundCheckPosition = ResolveGroundCheckPosition();
            Collider2D[] contacts = Physics2D.OverlapCircleAll(
                groundCheckPosition,
                Settings.groundCheckRadius,
                Settings.groundMask.value);
            for (int i = 0; i < contacts.Length; i++)
            {
                Collider2D candidate = contacts[i];
                if (candidate == null || candidate == bodyCollider ||
                    candidate.transform == transform || candidate.transform.IsChildOf(transform))
                    continue;
                grounded = true;
                break;
            }

            if (actor != null)
            {
                float speed = body != null ? Mathf.Abs(body.linearVelocity.x) : 0f;
                actor.SetLocomotionAnimation(speed, grounded);
            }
            if (grounded && !wasGrounded && !IsAttacking)
                UpdateLocomotionAnimation(true);
            return grounded;
        }

        private Vector2 ResolveGroundCheckPosition()
        {
            Vector2 position = groundProbe != null
                ? groundProbe.position
                : (Vector2)transform.position;
            if (body == null)
                return position;

            attachedColliders.Clear();
            body.GetAttachedColliders(attachedColliders, false);
            float lowestColliderBottom = float.PositiveInfinity;
            for (int i = 0; i < attachedColliders.Count; i++)
            {
                Collider2D candidate = attachedColliders[i];
                if (candidate == null || !candidate.enabled || candidate.isTrigger)
                    continue;

                Bounds bounds = candidate.bounds;
                if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
                    continue;
                lowestColliderBottom = Mathf.Min(lowestColliderBottom, bounds.min.y);
            }

            if (!float.IsPositiveInfinity(lowestColliderBottom))
                position.y = Mathf.Min(position.y, lowestColliderBottom);
            return position;
        }

        private void ReadKeyboardInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || Settings == null)
                return;

            float horizontal = 0f;
            if (IsPressed(keyboard, Settings.moveLeftKey))
                horizontal -= 1f;
            if (IsPressed(keyboard, Settings.moveRightKey))
                horizontal += 1f;
            SetRunInput(IsPressed(keyboard, Settings.runKey));
            SetMoveInput(horizontal);

            if (WasPressedThisFrame(keyboard, Settings.jumpKey))
                TryJump();

            if (Settings.attacks == null)
                return;
            for (int i = 0; i < Settings.attacks.Count; i++)
            {
                ObjectPlayerAttack attack = Settings.attacks[i];
                if (attack != null && WasPressedThisFrame(keyboard, attack.key))
                {
                    TryStartAttack(i);
                    break;
                }
            }
        }

        private void ApplyHorizontalMovement()
        {
            if (body == null || actor == null || actor.IsDead || Data == null || Settings == null)
                return;

            bool movementLocked = Time.time < hitLockUntil ||
                                  (IsAttacking && !Settings.allowMovementDuringAttack);
            float speedMultiplier = runInput && Settings.runKey != Key.None
                ? Settings.runSpeedMultiplier
                : 1f;
            float horizontalVelocity = movementLocked
                ? 0f
                : moveInput * Data.core.moveSpeed * speedMultiplier;
            if (IsAttacking && attackPhase == AttackPhase.Active &&
                Settings.attacks[attackIndex].pattern == ObjectPlayerAttackPattern.Dash)
            {
                var attack = Settings.attacks[attackIndex];
                float distance = attack.dashSpeed * Time.fixedDeltaTime;
                var filter = new ContactFilter2D();
                filter.SetLayerMask(attack.obstacleMask);
                filter.useTriggers = false;
                int count = body.Cast(Vector2.right * attackDirection, filter, dashObstacles, distance + 0.02f);
                for (int i = 0; i < count; i++)
                    if (dashObstacles[i].collider != null && Mathf.Abs(dashObstacles[i].normal.x) > 0.5f &&
                        dashObstacles[i].collider.GetComponentInParent<ObjectActor2D>() == null)
                        distance = Mathf.Min(distance, Mathf.Max(0f, dashObstacles[i].distance - 0.02f));
                horizontalVelocity = attackDirection * distance / Time.fixedDeltaTime;
            }
            body.linearVelocity = new Vector2(horizontalVelocity, body.linearVelocity.y);
            actor.SetLocomotionAnimation(Mathf.Abs(horizontalVelocity), grounded);
            actor.SetAnimatorBool(
                ObjectAnimatorGraph2D.RunningParameter,
                !movementLocked && IsRunning && Mathf.Abs(horizontalVelocity) > 0.001f);
            UpdateLocomotionSound(movementLocked ? 0f : horizontalVelocity);
        }

        private void UpdateLocomotionAnimation(bool forceRestart = false)
        {
            if (actor == null || !grounded || IsAttacking || Time.time < hitLockUntil)
                return;
            float speed = body != null
                ? Mathf.Abs(body.linearVelocity.x)
                : Mathf.Abs(moveInput) * (Data != null ? Data.core.moveSpeed : 0f);
            actor.SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, speed);
            actor.SetAnimatorBool(ObjectAnimatorGraph2D.RunningParameter, IsRunning);
        }

        private void StartAttackStep(int index, int step)
        {
            ObjectPlayerAttack attack = Settings.attacks[index];
            attackIndex = index;
            comboStep = Mathf.Clamp(step, 0, attack.comboSteps - 1);
            queuedCombo = false;
            damagedThisStep.Clear();
            ClearSkillVisual();
            attackDirection = actor.IsFacingRight ? 1f : -1f;
            lastDashPosition = body.position;
            attackPhase = AttackPhase.InputDelay;
            attackStepStartedAt = Time.time;
            phaseEndsAt = Time.time + attack.inputDelaySeconds;

            actor.SetPlayerAttackAnimation(true, attackIndex, comboStep);
            audioController?.PlayOneShot(attack.sound);
            ApplyHorizontalMovement();
        }

        private void TickAttack()
        {
            if (!IsAttacking || Settings == null || attackIndex < 0 ||
                attackIndex >= Settings.attacks.Count)
                return;

            ObjectPlayerAttack attack = Settings.attacks[attackIndex];
            if (attack == null)
            {
                CancelAttack();
                return;
            }

            int safety = 0;
            while (IsAttacking && Time.time >= phaseEndsAt && safety++ < 4)
            {
                switch (attackPhase)
                {
                    case AttackPhase.InputDelay:
                        attackPhase = AttackPhase.Active;
                        phaseEndsAt += attack.activeSeconds;
                        BeginSkill(attack);
                        break;
                    case AttackPhase.Active:
                        TickSkill(attack, true);
                        ClearSkillVisual();
                        attackPhase = AttackPhase.Recovery;
                        phaseEndsAt += attack.recoverySeconds;
                        break;
                    case AttackPhase.Recovery:
                        if (queuedCombo && comboStep + 1 < attack.comboSteps)
                            StartAttackStep(attackIndex, comboStep + 1);
                        else
                            FinishAttack(attack);
                        break;
                }
            }
            if (attackPhase == AttackPhase.Active) TickSkill(attack);
        }

        private Vector2 SkillOrigin(ObjectPlayerAttack attack)
        {
            Vector2 origin = ResolveAttackHitboxCenter(attackDirection, 0f);
            return origin + new Vector2(attack.effectOffset.x * attackDirection, attack.effectOffset.y);
        }

        private void BeginSkill(ObjectPlayerAttack attack)
        {
            if (attack.pattern == ObjectPlayerAttackPattern.Melee) { ApplyAttackHit(attack); return; }
            if (attack.pattern == ObjectPlayerAttackPattern.Projectile)
            { ObjectPlayerProjectile2D.Spawn(actor, attack, SkillOrigin(attack), attackDirection); return; }
            lastDashPosition = body.position;
            if (attack.effectPrefab == null) return;
            skillVisual = Instantiate(attack.effectPrefab, SkillOrigin(attack), Quaternion.identity);
            Vector3 scale = skillVisual.transform.localScale;
            scale.x = Mathf.Abs(scale.x) * attackDirection;
            if (attack.pattern == ObjectPlayerAttackPattern.GrowingThrust)
            {
                var renderer = skillVisual.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.sprite != null)
                    scale.x *= attack.range / Mathf.Max(0.01f, renderer.sprite.bounds.size.x * Mathf.Abs(scale.x));
            }
            skillVisual.transform.localScale = scale;
            skillVisual.GetComponent<ObjectSkillVisual2D>()?.Play(attack.activeSeconds);
        }

        private void TickSkill(ObjectPlayerAttack attack, bool ending = false)
        {
            if (skillVisual != null) skillVisual.transform.position = SkillOrigin(attack);
            if (attack.pattern == ObjectPlayerAttackPattern.GrowingThrust)
            {
                float progress = ending ? 1f : Mathf.Clamp01((Time.time - (phaseEndsAt - attack.activeSeconds)) / Mathf.Max(0.01f, attack.activeSeconds * 0.65f));
                float length = attack.range * progress;
                if (length > 0f) DamageBox(SkillOrigin(attack) + Vector2.right * attackDirection * length * 0.5f,
                    new Vector2(length, attack.hitboxHeight), attack);
            }
            else if (attack.pattern == ObjectPlayerAttackPattern.Dash)
            {
                Vector2 now = body.position;
                DamageBox((now + lastDashPosition) * 0.5f + Vector2.right * attackDirection * attack.range * 0.25f,
                    new Vector2(Mathf.Abs(now.x - lastDashPosition.x) + attack.range, attack.hitboxHeight), attack);
                lastDashPosition = now;
            }
        }

        private void DamageBox(Vector2 center, Vector2 size, ObjectPlayerAttack attack)
        {
            foreach (var hit in Physics2D.OverlapBoxAll(center, size, 0f))
            {
                var target = hit.GetComponentInParent<ObjectActor2D>();
                if (target == null || target == actor || target.IsDead || target.Kind == ObjectKind.Player ||
                    !damagedThisStep.Add(target)) continue;
                target.TryReceiveDamage(new ObjectDamageRequest(attack.damage, gameObject, body.position,
                    attack.targetInvulnerabilitySeconds, attack.knockback, ObjectDamageCause.Attack));
            }
        }

        private void ClearSkillVisual()
        {
            if (skillVisual != null) Destroy(skillVisual);
            skillVisual = null;
        }

        private void ApplyAttackHit(ObjectPlayerAttack attack)
        {
            if (actor == null || body == null || attack.damage <= 0 || attack.range <= 0f)
                return;

            float direction = actor.IsFacingRight ? 1f : -1f;
            Vector2 center = ResolveAttackHitboxCenter(direction, attack.range);
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                center,
                new Vector2(attack.range, attack.hitboxHeight),
                0f);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null || hit == bodyCollider)
                    continue;
                ObjectActor2D target = hit.GetComponentInParent<ObjectActor2D>();
                if (target == null || target == actor || target.IsDead ||
                    target.Kind == ObjectKind.Player || !damagedThisStep.Add(target))
                    continue;

                target.TryReceiveDamage(new ObjectDamageRequest(
                    attack.damage,
                    gameObject,
                    body.position,
                    attack.targetInvulnerabilitySeconds,
                    attack.knockback,
                    ObjectDamageCause.Attack));
            }
        }

        private void FinishAttack(ObjectPlayerAttack attack)
        {
            ClearSkillVisual();
            int finishedIndex = attackIndex;
            if (finishedIndex >= 0 && finishedIndex < nextAttackAt.Length)
                nextAttackAt[finishedIndex] = Time.time + attack.cooldownSeconds;

            attackPhase = AttackPhase.None;
            attackIndex = -1;
            comboStep = -1;
            queuedCombo = false;
            ResetAttackAnimatorParameters();
            UpdateLocomotionAnimation(true);
        }

        private void CancelAttack()
        {
            ClearSkillVisual();
            attackPhase = AttackPhase.None;
            attackIndex = -1;
            comboStep = -1;
            queuedCombo = false;
            damagedThisStep.Clear();
            ResetAttackAnimatorParameters();
        }

        private void OnActorDamaged(ObjectDamageResult result)
        {
            CancelAttack();
            hitLockUntil = Time.time + Mathf.Max(0f, Data != null ? Data.hit.hitStopSeconds : 0f);
            StopLocomotionSound();
            SetMoveInput(0f);
        }

        private void OnActorDied(ObjectActor2D deadActor)
        {
            CancelAttack();
            moveInput = 0f;
            runInput = false;
            StopLocomotionSound();
            if (actor != null)
                actor.SetAnimatorBool(ObjectAnimatorGraph2D.RunningParameter, false);
            if (body != null)
                body.linearVelocity = Vector2.zero;
        }

        private Vector2 ResolveAttackHitboxCenter(float direction, float range)
        {
            Vector2 center = body != null
                ? body.position
                : (Vector2)transform.position;
            float frontX = center.x;
            bool foundCollider = false;

            if (body != null)
            {
                attachedColliders.Clear();
                body.GetAttachedColliders(attachedColliders, false);
                for (int i = 0; i < attachedColliders.Count; i++)
                {
                    Collider2D candidate = attachedColliders[i];
                    if (candidate == null || !candidate.enabled || candidate.isTrigger)
                        continue;

                    Bounds bounds = candidate.bounds;
                    if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
                        continue;

                    float candidateFront = direction >= 0f ? bounds.max.x : bounds.min.x;
                    if (!foundCollider ||
                        (direction >= 0f && candidateFront > frontX) ||
                        (direction < 0f && candidateFront < frontX))
                    {
                        frontX = candidateFront;
                        foundCollider = true;
                    }
                }
            }

            center.x = frontX + direction * (range * 0.5f);
            return center;
        }

        private void ResolveReferences()
        {
            if (actor == null)
                actor = GetComponent<ObjectActor2D>();
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider2D>();
            if (groundProbe == null)
            {
                Transform found = transform.Find("GroundProbe");
                if (found != null)
                    groundProbe = found;
            }
            if (audioController == null && actor != null)
                audioController = actor.AudioController;
        }

        private void UpdateLocomotionSound(float horizontalVelocity)
        {
            LocomotionSoundState nextState = LocomotionSoundState.None;
            if (grounded && Mathf.Abs(horizontalVelocity) > 0.001f && Settings != null)
                nextState = IsRunning ? LocomotionSoundState.Run : LocomotionSoundState.Walk;
            if (nextState == locomotionSoundState)
                return;

            audioController?.StopBehavior();
            locomotionSoundState = nextState;
            if (audioController == null || Settings == null)
                return;
            if (nextState == LocomotionSoundState.Run)
                audioController.PlayBehavior(Settings.runSound);
            else if (nextState == LocomotionSoundState.Walk)
                audioController.PlayBehavior(Settings.walkSound);
        }

        private void StopLocomotionSound()
        {
            locomotionSoundState = LocomotionSoundState.None;
            audioController?.StopBehavior();
        }

        private void EnsureRuntimeArrays()
        {
            int count = Settings != null && Settings.attacks != null
                ? Settings.attacks.Count
                : 0;
            if (nextAttackAt == null || nextAttackAt.Length != count)
                nextAttackAt = new float[count];
        }

        private void ResetAttackAnimatorParameters()
        {
            if (actor == null)
                return;
            actor.SetPlayerAttackAnimation(false, -1, -1);
        }

        private static bool IsPressed(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key] != null && keyboard[key].isPressed;
        }

        private static bool WasPressedThisFrame(Keyboard keyboard, Key key)
        {
            return key != Key.None && keyboard[key] != null && keyboard[key].wasPressedThisFrame;
        }

        private void OnDrawGizmosSelected()
        {
            ObjectPlayerSettings settings = Settings;
            if (groundProbe != null && settings != null)
            {
                Gizmos.color = grounded ? Color.green : Color.yellow;
                Gizmos.DrawWireSphere(ResolveGroundCheckPosition(), settings.groundCheckRadius);
            }

            if (actor == null || body == null || settings == null ||
                settings.attacks == null || settings.attacks.Count == 0)
                return;
            ObjectPlayerAttack attack = settings.attacks[Mathf.Clamp(
                attackIndex >= 0 ? attackIndex : 0,
                0,
                settings.attacks.Count - 1)];
            if (attack == null)
                return;
            float direction = actor.IsFacingRight ? 1f : -1f;
            Vector2 center = ResolveAttackHitboxCenter(direction, attack.range);
            Gizmos.color = new Color(1f, 0.3f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(center, new Vector3(attack.range, attack.hitboxHeight, 0f));
        }
    }
}
