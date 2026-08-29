using System;
using System.Collections;
using UnityEngine;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class ObjectActor2D : MonoBehaviour, IObjectDamageable
    {
        [SerializeField] private ObjectDefinitionSO definition;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;

        // 새 직렬화 필드는 기존 Prefab 연결 순서를 보존하기 위해 끝에 추가합니다.
        [SerializeField] private ObjectAudioController2D audioController;

        private int currentHealth;
        private float invulnerableUntil;
        private bool initialized;
        private bool dead;
        private bool facingRight;
        private string currentAnimatorState = string.Empty;
        private Coroutine blinkCoroutine;
        private Color originalColor = Color.white;
        private SpriteRenderer[] facingRenderers = Array.Empty<SpriteRenderer>();

        public event Action<ObjectDamageResult> Damaged;
        public event Action<ObjectActor2D> Died;

        public ObjectDefinitionSO Definition => definition;
        public ObjectDefinitionData Data => definition != null ? definition.Data : null;
        public ObjectKind Kind => Data != null ? Data.kind : ObjectKind.Neutral;
        public Rigidbody2D Body => body;
        public Collider2D BodyCollider => bodyCollider;
        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public Animator Animator => animator;
        public ObjectAudioController2D AudioController
        {
            get
            {
                if (audioController == null)
                    audioController = GetComponent<ObjectAudioController2D>();
                if (audioController == null && Application.isPlaying)
                    audioController = gameObject.AddComponent<ObjectAudioController2D>();
                return audioController;
            }
        }
        public int CurrentHealth => currentHealth;
        public int MaxHealth => Data != null ? Data.core.maxHealth : 1;
        public float CurrentHealthRatio => MaxHealth > 0
            ? Mathf.Clamp01((float)currentHealth / MaxHealth)
            : 0f;
        public bool IsDead => dead;
        public bool IsFacingRight => facingRight;
        public string CurrentAnimatorState => currentAnimatorState;

        private void Awake()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            RefreshFromDefinition(true);
        }

        private void OnEnable()
        {
            ObjectMakerRegistry.Register(this);
        }

        private void OnDisable()
        {
            ObjectMakerRegistry.Unregister(this);
            if (blinkCoroutine != null)
            {
                StopCoroutine(blinkCoroutine);
                blinkCoroutine = null;
            }

            RestoreVisualColor();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        public void Configure(
            ObjectDefinitionSO sourceDefinition,
            Rigidbody2D sourceBody,
            Collider2D sourceCollider,
            Transform sourceVisualRoot,
            SpriteRenderer sourceRenderer,
            Animator sourceAnimator)
        {
            definition = sourceDefinition;
            body = sourceBody;
            bodyCollider = sourceCollider;
            visualRoot = sourceVisualRoot;
            spriteRenderer = sourceRenderer;
            animator = sourceAnimator;
            ResolveReferences();
        }

        public void RefreshFromDefinition(bool resetHealth)
        {
            ResolveReferences();
            if (definition == null)
                return;

            definition.Data.Sanitize();
            if (spriteRenderer != null)
                originalColor = spriteRenderer.color;

            if (animator != null)
            {
                animator.runtimeAnimatorController = definition.Data.animatorController;
                animator.applyRootMotion = false;
                SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, 0f);
                SetAnimatorBool(ObjectAnimatorGraph2D.AttackingParameter, false);
                SetAnimatorInteger(ObjectAnimatorGraph2D.PlayerAttackParameter, 0);
                SetAnimatorInteger(ObjectAnimatorGraph2D.PlayerComboParameter, 0);
                SetAnimatorInteger(ObjectAnimatorGraph2D.EnemyRuleParameter, 0);
                SetAnimatorInteger(ObjectAnimatorGraph2D.EnemyAttackPhaseParameter, 0);
            }

            facingRight = definition.Data.facesRightByDefault;
            ApplyFacing();

            if (resetHealth || !initialized)
            {
                currentHealth = definition.Data.core.maxHealth;
                invulnerableUntil = float.NegativeInfinity;
                dead = false;
            }

            initialized = true;
        }

        public bool TryReceiveDamage(ObjectDamageRequest request)
        {
            if (dead || definition == null || Time.time < invulnerableUntil)
                return false;

            int appliedDamage = Mathf.Max(0, request.Amount - definition.Data.core.defense);
            if (appliedDamage <= 0)
                return false;

            invulnerableUntil = Time.time + Mathf.Max(0f, request.InvulnerabilitySeconds);
            currentHealth = Mathf.Max(0, currentHealth - appliedDamage);
            ApplyKnockback(request);
            StartBlink();

            bool isDead = currentHealth <= 0;
            var result = new ObjectDamageResult(request, appliedDamage, currentHealth, isDead);
            SetAnimatorTrigger(ObjectAnimatorGraph2D.HitParameter);
            if (!isDead)
                AudioController?.PlayOneShot(definition.Data.hit.sound);
            if (isDead)
            {
                try
                {
                    Damaged?.Invoke(result);
                }
                finally
                {
                    Die();
                }
            }
            else
            {
                Damaged?.Invoke(result);
            }

            return true;
        }

        public void RestoreFullHealth()
        {
            if (definition == null)
                return;

            currentHealth = definition.Data.core.maxHealth;
            invulnerableUntil = float.NegativeInfinity;
            dead = false;
            if (bodyCollider != null)
                bodyCollider.enabled = true;
        }

        public void ForceDeath(GameObject source = null)
        {
            if (dead || definition == null)
                return;
            currentHealth = 0;
            Die();
        }

        public void ForceDestroy(GameObject source = null)
        {
            if (!dead && definition != null)
            {
                currentHealth = 0;
                Die();
                return;
            }

            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            Destroy(gameObject);
        }

        public void SetFacing(float horizontalDirection)
        {
            if (Mathf.Abs(horizontalDirection) < 0.001f)
                return;

            facingRight = horizontalDirection > 0f;
            ApplyFacing();
        }

        public bool PlayMotion(ObjectMotionCondition condition, bool forceRestart = false)
        {
            if (definition == null || animator == null || animator.runtimeAnimatorController == null)
                return false;

            ObjectMotionBinding binding = FindMotion(condition);
            if (binding == null || string.IsNullOrWhiteSpace(binding.animatorState))
                return false;

            return PlayAnimatorState(
                binding.animatorState.Trim(),
                forceRestart || binding.restartWhenEntered,
                binding.crossFadeSeconds);
        }

        public bool PlayAnimatorState(
            string state,
            bool forceRestart = false,
            float crossFadeSeconds = 0.05f)
        {
            if (animator == null || animator.runtimeAnimatorController == null ||
                string.IsNullOrWhiteSpace(state))
                return false;

            state = state.Trim();
            int stateHash = Animator.StringToHash(state);
            if (!animator.HasState(0, stateHash))
                return false;

            bool restart = forceRestart ||
                           !string.Equals(currentAnimatorState, state, StringComparison.Ordinal);
            if (!restart)
                return true;

            currentAnimatorState = state;
            if (crossFadeSeconds > 0f)
                animator.CrossFadeInFixedTime(state, crossFadeSeconds, 0, 0f);
            else
                animator.Play(state, 0, 0f);
            return true;
        }

        public void SetAnimatorFloat(string parameter, float value)
        {
            if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Float))
                animator.SetFloat(parameter, value);
        }

        public void SetAnimatorBool(string parameter, bool value)
        {
            if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Bool))
                animator.SetBool(parameter, value);
        }

        public void SetAnimatorTrigger(string parameter)
        {
            if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
                animator.SetTrigger(parameter);
        }

        public void ResetAnimatorTrigger(string parameter)
        {
            if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Trigger))
                animator.ResetTrigger(parameter);
        }

        public void SetAnimatorInteger(string parameter, int value)
        {
            if (HasAnimatorParameter(parameter, AnimatorControllerParameterType.Int))
                animator.SetInteger(parameter, value);
        }

        public void SetLocomotionAnimation(float absoluteSpeed, bool grounded)
        {
            SetAnimatorFloat(ObjectAnimatorGraph2D.SpeedParameter, Mathf.Abs(absoluteSpeed));
            SetAnimatorBool(ObjectAnimatorGraph2D.GroundedParameter, grounded);
        }

        public void SetPlayerAttackAnimation(bool attacking, int attackIndex, int comboIndex)
        {
            SetAnimatorBool(ObjectAnimatorGraph2D.AttackingParameter, attacking);
            SetAnimatorInteger(
                ObjectAnimatorGraph2D.PlayerAttackParameter,
                attacking ? Mathf.Max(0, attackIndex) + 1 : 0);
            SetAnimatorInteger(
                ObjectAnimatorGraph2D.PlayerComboParameter,
                attacking ? Mathf.Max(0, comboIndex) + 1 : 0);
        }

        public void SetEnemyRuleAnimation(int ruleIndex, ObjectEnemyAnimatorPhase phase)
        {
            SetAnimatorInteger(
                ObjectAnimatorGraph2D.EnemyRuleParameter,
                ruleIndex >= 0 ? ruleIndex + 1 : 0);
            SetAnimatorInteger(
                ObjectAnimatorGraph2D.EnemyAttackPhaseParameter,
                (int)phase);
        }

        private void Die()
        {
            if (dead)
                return;

            dead = true;
            if (definition.Data.destruction.disableCollisionsImmediately && bodyCollider != null)
                bodyCollider.enabled = false;

            if (body != null)
                body.linearVelocity = Vector2.zero;

            SetAnimatorBool(ObjectAnimatorGraph2D.AttackingParameter, false);
            SetAnimatorInteger(ObjectAnimatorGraph2D.EnemyRuleParameter, 0);
            SetAnimatorInteger(ObjectAnimatorGraph2D.EnemyAttackPhaseParameter, 0);
            ResetAnimatorTrigger(ObjectAnimatorGraph2D.HitParameter);
            SetAnimatorTrigger(ObjectAnimatorGraph2D.DeadParameter);
            AudioController?.PlayDetachedOneShot(definition.Data.destruction.sound);
            GameObject effect = definition.Data.destruction.effectPrefab;
            if (effect != null)
                Instantiate(effect, transform.position, transform.rotation);

            try
            {
                Died?.Invoke(this);
            }
            finally
            {
                // Destroy() is finalized at the end of the frame. Disable the root first so
                // the body can never remain visible while an independent death effect plays.
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider2D>();
            if (visualRoot == null && transform.childCount > 0)
                visualRoot = transform.GetChild(0);
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
            if (audioController == null)
                audioController = GetComponent<ObjectAudioController2D>();
            facingRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void ApplyFacing()
        {
            if (definition == null)
                return;

            bool flipX = facingRight != definition.Data.facesRightByDefault;
            bool applied = false;
            if (facingRenderers != null)
            {
                for (int i = 0; i < facingRenderers.Length; i++)
                {
                    SpriteRenderer renderer = facingRenderers[i];
                    if (renderer == null)
                        continue;
                    renderer.flipX = flipX;
                    applied = true;
                }
            }

            if (!applied && spriteRenderer != null)
                spriteRenderer.flipX = flipX;
        }

        private ObjectMotionBinding FindMotion(ObjectMotionCondition condition)
        {
            if (definition == null || definition.Data.motions == null)
                return null;

            for (int i = 0; i < definition.Data.motions.Count; i++)
            {
                ObjectMotionBinding candidate = definition.Data.motions[i];
                if (candidate != null && candidate.condition == condition)
                    return candidate;
            }

            return null;
        }

        private bool HasAnimatorParameter(
            string parameter,
            AnimatorControllerParameterType expectedType)
        {
            if (animator == null || animator.runtimeAnimatorController == null ||
                string.IsNullOrWhiteSpace(parameter))
                return false;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].type == expectedType &&
                    string.Equals(parameters[i].name, parameter, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void ApplyKnockback(ObjectDamageRequest request)
        {
            if (body == null || definition == null)
                return;

            float knockback = request.Knockback > 0f
                ? request.Knockback
                : definition.Data.hit.knockback;
            if (knockback <= 0f)
                return;

            float direction = Mathf.Sign(body.position.x - request.SourcePosition.x);
            if (Mathf.Approximately(direction, 0f))
                direction = facingRight ? -1f : 1f;
            body.position += Vector2.right * direction * knockback;
        }

        private void StartBlink()
        {
            if (spriteRenderer == null || definition == null || definition.Data.hit.blinkSeconds <= 0f)
                return;

            if (blinkCoroutine != null)
                StopCoroutine(blinkCoroutine);
            blinkCoroutine = StartCoroutine(BlinkRoutine());
        }

        private IEnumerator BlinkRoutine()
        {
            int count = Mathf.Max(1, definition.Data.hit.blinkCount);
            float segment = definition.Data.hit.blinkSeconds / (count * 2f);
            originalColor = spriteRenderer.color;

            for (int i = 0; i < count; i++)
            {
                SetVisualAlpha(0.2f);
                yield return WaitRealtime(segment);
                RestoreVisualColor();
                yield return WaitRealtime(segment);
            }

            blinkCoroutine = null;
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private void SetVisualAlpha(float alpha)
        {
            if (spriteRenderer == null)
                return;
            Color color = originalColor;
            color.a *= Mathf.Clamp01(alpha);
            spriteRenderer.color = color;
        }

        private void RestoreVisualColor()
        {
            if (spriteRenderer != null)
                spriteRenderer.color = originalColor;
        }
    }
}
