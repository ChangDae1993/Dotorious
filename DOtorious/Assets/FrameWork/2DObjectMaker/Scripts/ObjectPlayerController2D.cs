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

        private readonly HashSet<ObjectActor2D> damagedThisStep = new HashSet<ObjectActor2D>();
        private float moveInput;
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

        public bool IsGrounded => grounded;
        public bool IsAttacking => attackPhase != AttackPhase.None;
        public int CurrentAttackIndex => attackIndex;
        public int CurrentComboStep => comboStep;
        public float MoveInput => moveInput;

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
            if (actor != null)
            {
                actor.Damaged -= OnActorDamaged;
                actor.Died -= OnActorDied;
            }
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (actor == null || actor.IsDead || Settings == null)
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
            if (Mathf.Abs(moveInput) > 0.001f && actor != null)
                actor.SetFacing(moveInput);
            ApplyHorizontalMovement();
            if (!IsAttacking)
                UpdateLocomotionAnimation();
        }

        public bool TryJump()
        {
            if (actor == null || actor.IsDead || body == null || Settings == null)
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
            return true;
        }

        public bool TryStartAttack(int index)
        {
            EnsureRuntimeArrays();
            if (actor == null || actor.IsDead || Settings == null ||
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

            Collider2D[] contacts = Physics2D.OverlapCircleAll(
                groundProbe.position,
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
            if (body == null || actor == null || actor.IsDead || Data == null)
                return;

            bool movementLocked = Time.time < hitLockUntil ||
                                  (IsAttacking && !Settings.allowMovementDuringAttack);
            float horizontalVelocity = movementLocked
                ? 0f
                : moveInput * Data.core.moveSpeed;
            body.linearVelocity = new Vector2(horizontalVelocity, body.linearVelocity.y);
            actor.SetLocomotionAnimation(Mathf.Abs(horizontalVelocity), grounded);
        }

        private void UpdateLocomotionAnimation(bool forceRestart = false)
        {
            if (actor == null || !grounded || IsAttacking || Time.time < hitLockUntil)
                return;
            float speed = body != null
                ? Mathf.Abs(body.linearVelocity.x)
                : Mathf.Abs(moveInput) * (Data != null ? Data.core.moveSpeed : 0f);
            actor.SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, speed);
        }

        private void StartAttackStep(int index, int step)
        {
            ObjectPlayerAttack attack = Settings.attacks[index];
            attackIndex = index;
            comboStep = Mathf.Clamp(step, 0, attack.comboSteps - 1);
            queuedCombo = false;
            damagedThisStep.Clear();
            attackPhase = AttackPhase.InputDelay;
            attackStepStartedAt = Time.time;
            phaseEndsAt = Time.time + attack.inputDelaySeconds;

            actor.SetPlayerAttackAnimation(true, attackIndex, comboStep);
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
                        ApplyAttackHit(attack);
                        attackPhase = AttackPhase.Active;
                        phaseEndsAt += attack.activeSeconds;
                        break;
                    case AttackPhase.Active:
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
        }

        private void ApplyAttackHit(ObjectPlayerAttack attack)
        {
            if (actor == null || body == null || attack.damage <= 0 || attack.range <= 0f)
                return;

            float direction = actor.IsFacingRight ? 1f : -1f;
            Vector2 center = body.position + Vector2.right * direction * (attack.range * 0.5f);
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
            SetMoveInput(0f);
        }

        private void OnActorDied(ObjectActor2D deadActor)
        {
            CancelAttack();
            moveInput = 0f;
            if (body != null)
                body.linearVelocity = Vector2.zero;
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
                Gizmos.DrawWireSphere(groundProbe.position, settings.groundCheckRadius);
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
            Vector2 center = body.position + Vector2.right * direction * (attack.range * 0.5f);
            Gizmos.color = new Color(1f, 0.3f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(center, new Vector3(attack.range, attack.hitboxHeight, 0f));
        }
    }
}
