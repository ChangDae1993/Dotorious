using UnityEngine;
using JYW.FrameWork;

namespace JYW.Game.ObjectMaker
{
    internal enum ObjectFxShape
    {
        Circle,
        Ring,
        Square,
        Diamond,
        Star,
        Arc,
        Smoke
    }

    [DisallowMultipleComponent]
    public sealed class ObjectFxController2D : MonoBehaviour
    {
        [SerializeField] private ObjectActor2D actor;
        [SerializeField] private SpriteRenderer referenceRenderer;

        private static readonly Sprite[] ShapeSprites = new Sprite[7];
        private int spawnedEffectCount;

        public int SpawnedEffectCount => spawnedEffectCount;

        private void Awake()
        {
            if (!FrameWorkFeatureGate.IsEnabled(FrameWorkModule.ObjectMaker))
            {
                enabled = false;
                return;
            }
            ResolveReferences();
        }

        public void Configure(ObjectActor2D sourceActor, SpriteRenderer sourceRenderer)
        {
            actor = sourceActor;
            referenceRenderer = sourceRenderer;
            ResolveReferences();
        }

        public void Play(
            ObjectRuleEffect effect,
            ObjectRuleEffectParameters settings,
            float facingDirection = 1f)
        {
            if (!enabled || effect == ObjectRuleEffect.None)
                return;
            settings = settings ?? new ObjectRuleEffectParameters();
            settings.Sanitize();
            spawnedEffectCount++;

            float facing = facingDirection >= 0f ? 1f : -1f;
            switch (effect)
            {
                case ObjectRuleEffect.AlertMark:
                    PlayAlertMark(settings);
                    break;
                case ObjectRuleEffect.ScanRing:
                    Pulse(ObjectFxShape.Ring, Vector2.zero, settings.primaryColor,
                        0.25f * settings.scale, 1.5f * settings.scale, settings.duration);
                    Pulse(ObjectFxShape.Ring, Vector2.zero, settings.secondaryColor,
                        0.1f * settings.scale, 1.05f * settings.scale, settings.duration * 0.7f);
                    break;
                case ObjectRuleEffect.TargetLock:
                    PlayTargetLock(settings);
                    break;
                case ObjectRuleEffect.EyeFlash:
                    Emit(ObjectFxShape.Diamond, Vector2.up * 0.35f, Vector2.zero,
                        new Vector2(0.1f, 0.08f) * settings.scale,
                        new Vector2(1.3f, 0.12f) * settings.scale,
                        settings.primaryColor, settings.duration, 0f, 0f);
                    break;
                case ObjectRuleEffect.SlashArc:
                    Emit(ObjectFxShape.Arc, Vector2.right * facing * 0.35f, Vector2.right * facing * 0.35f,
                        Vector2.one * 0.5f * settings.scale,
                        Vector2.one * 1.35f * settings.scale,
                        settings.primaryColor, settings.duration, -180f * facing, 0.2f);
                    Burst(ObjectFxShape.Star, Mathf.Max(3, settings.amount / 3),
                        settings.secondaryColor, settings.scale * 0.7f, settings.duration * 0.7f, facing);
                    break;
                case ObjectRuleEffect.HeavyImpact:
                    Pulse(ObjectFxShape.Ring, Vector2.down * 0.35f, settings.primaryColor,
                        0.15f * settings.scale, 1.8f * settings.scale, settings.duration);
                    Burst(ObjectFxShape.Diamond, settings.amount, settings.secondaryColor,
                        settings.scale, settings.duration, 0f, true);
                    break;
                case ObjectRuleEffect.DashTrail:
                    TrailSquares(settings, -facing);
                    break;
                case ObjectRuleEffect.MuzzleFlash:
                    PlayMuzzle(settings, facing, 0f);
                    break;
                case ObjectRuleEffect.TripleMuzzle:
                    PlayMuzzle(settings, facing, 0f);
                    PlayMuzzle(settings, facing, 0.25f);
                    PlayMuzzle(settings, facing, -0.25f);
                    break;
                case ObjectRuleEffect.Shockwave:
                    Pulse(ObjectFxShape.Ring, Vector2.down * 0.35f, settings.primaryColor,
                        0.2f * settings.scale, 2f * settings.scale, settings.duration);
                    Burst(ObjectFxShape.Diamond, settings.amount, settings.secondaryColor,
                        settings.scale, settings.duration, 0f, true);
                    break;
                case ObjectRuleEffect.LightningBurst:
                    Burst(ObjectFxShape.Star, settings.amount, settings.primaryColor,
                        settings.scale, settings.duration * 0.65f, 0f);
                    Burst(ObjectFxShape.Diamond, Mathf.Max(3, settings.amount / 2),
                        settings.secondaryColor, settings.scale * 0.7f, settings.duration, 0f);
                    break;
                case ObjectRuleEffect.FireBurst:
                    RisingBurst(ObjectFxShape.Circle, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale, settings.duration, 1.25f);
                    break;
                case ObjectRuleEffect.IceShards:
                    Burst(ObjectFxShape.Diamond, settings.amount, settings.primaryColor,
                        settings.scale, settings.duration, 0f);
                    Pulse(ObjectFxShape.Ring, Vector2.zero, settings.secondaryColor,
                        0.2f * settings.scale, 1.2f * settings.scale, settings.duration * 0.8f);
                    break;
                case ObjectRuleEffect.DarkSplash:
                    Burst(ObjectFxShape.Circle, settings.amount, settings.primaryColor,
                        settings.scale, settings.duration, 0f);
                    Burst(ObjectFxShape.Diamond, Mathf.Max(2, settings.amount / 3),
                        settings.secondaryColor, settings.scale * 0.6f, settings.duration * 1.2f, 0f);
                    break;
                case ObjectRuleEffect.SparkBurst:
                    Burst(ObjectFxShape.Star, settings.amount, settings.secondaryColor,
                        settings.scale * 0.8f, settings.duration * 0.6f, facing);
                    break;
                case ObjectRuleEffect.Glitch:
                    PlayGlitch(settings);
                    break;
                case ObjectRuleEffect.SmokeBurst:
                    RisingBurst(ObjectFxShape.Smoke, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale * 1.2f, settings.duration * 1.4f, 0.65f);
                    break;
                case ObjectRuleEffect.Portal:
                    PlayPortal(settings);
                    break;
                case ObjectRuleEffect.Materialize:
                    Implode(ObjectFxShape.Star, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale, settings.duration);
                    Pulse(ObjectFxShape.Ring, Vector2.zero, settings.primaryColor,
                        1.4f * settings.scale, 0.2f * settings.scale, settings.duration);
                    break;
                case ObjectRuleEffect.PixelScatter:
                    Burst(ObjectFxShape.Square, settings.amount, settings.primaryColor,
                        settings.scale, settings.duration * 1.3f, 0f);
                    break;
                case ObjectRuleEffect.Explosion:
                    Pulse(ObjectFxShape.Circle, Vector2.zero, settings.secondaryColor,
                        0.1f * settings.scale, 1.6f * settings.scale, settings.duration * 0.65f);
                    Burst(ObjectFxShape.Star, settings.amount, settings.primaryColor,
                        settings.scale, settings.duration, 0f);
                    break;
                case ObjectRuleEffect.Implosion:
                    Implode(ObjectFxShape.Diamond, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale * 1.3f, settings.duration);
                    break;
                case ObjectRuleEffect.SoulRise:
                    RisingBurst(ObjectFxShape.Circle, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale, settings.duration * 1.6f, 1.5f);
                    break;
                case ObjectRuleEffect.ShadowRise:
                    RisingBurst(ObjectFxShape.Smoke, settings.amount, settings.primaryColor,
                        settings.secondaryColor, settings.scale * 1.25f, settings.duration * 1.5f, 1.1f);
                    break;
                case ObjectRuleEffect.HealPulse:
                    Pulse(ObjectFxShape.Ring, Vector2.zero, settings.primaryColor,
                        0.25f * settings.scale, 1.5f * settings.scale, settings.duration);
                    RisingBurst(ObjectFxShape.Star, Mathf.Max(4, settings.amount / 2),
                        settings.primaryColor, settings.secondaryColor,
                        settings.scale * 0.65f, settings.duration, 0.8f);
                    break;
            }
        }

        private void ResolveReferences()
        {
            if (actor == null)
                actor = GetComponent<ObjectActor2D>();
            if (referenceRenderer == null)
                referenceRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        private void PlayAlertMark(ObjectRuleEffectParameters settings)
        {
            Vector2 top = Vector2.up * (BoundsHeight * 0.65f + 0.3f);
            Emit(ObjectFxShape.Square, top + Vector2.up * 0.12f, Vector2.up * 0.25f,
                new Vector2(0.12f, 0.42f) * settings.scale,
                new Vector2(0.09f, 0.34f) * settings.scale,
                settings.primaryColor, settings.duration, 0f, 0.08f);
            Emit(ObjectFxShape.Circle, top + Vector2.down * 0.2f, Vector2.up * 0.25f,
                Vector2.one * 0.12f * settings.scale,
                Vector2.one * 0.08f * settings.scale,
                settings.secondaryColor, settings.duration, 0f, 0.08f);
        }

        private void PlayTargetLock(ObjectRuleEffectParameters settings)
        {
            float radius = 0.65f * settings.scale;
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.right;
                Emit(ObjectFxShape.Diamond, direction * radius, -direction * radius * 0.65f,
                    Vector2.one * 0.18f * settings.scale,
                    Vector2.one * 0.1f * settings.scale,
                    i % 2 == 0 ? settings.primaryColor : settings.secondaryColor,
                    settings.duration, 120f, 0f);
            }
        }

        private void PlayMuzzle(ObjectRuleEffectParameters settings, float facing, float vertical)
        {
            Vector2 direction = new Vector2(facing, vertical).normalized;
            Vector2 offset = direction * (BoundsWidth * 0.5f + 0.25f);
            Emit(ObjectFxShape.Star, offset, direction * 1.5f,
                Vector2.one * 0.5f * settings.scale,
                Vector2.one * 0.05f * settings.scale,
                settings.secondaryColor, settings.duration * 0.45f, 360f, 0f);
            Emit(ObjectFxShape.Diamond, offset, direction * 1.1f,
                new Vector2(0.75f, 0.12f) * settings.scale,
                new Vector2(0.1f, 0.03f) * settings.scale,
                settings.primaryColor, settings.duration * 0.5f, 0f, 0f);
        }

        private void TrailSquares(ObjectRuleEffectParameters settings, float direction)
        {
            int count = Mathf.Clamp(settings.amount, 3, 16);
            for (int i = 0; i < count; i++)
            {
                float offset = (i + 1f) / count;
                Vector2 position = new Vector2(direction * offset * BoundsWidth,
                    Random.Range(-0.35f, 0.35f) * BoundsHeight);
                Emit(ObjectFxShape.Square, position, Vector2.right * direction * 0.25f,
                    Vector2.one * Random.Range(0.08f, 0.2f) * settings.scale,
                    Vector2.zero,
                    Color.Lerp(settings.primaryColor, settings.secondaryColor, offset),
                    settings.duration * Random.Range(0.5f, 1f), Random.Range(-180f, 180f), 0f);
            }
        }

        private void PlayGlitch(ObjectRuleEffectParameters settings)
        {
            int count = Mathf.Clamp(settings.amount, 4, 24);
            Color[] colors =
            {
                settings.primaryColor,
                settings.secondaryColor,
                new Color(0.1f, 0.9f, 1f, 1f)
            };
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = new Vector2(Random.Range(-BoundsWidth, BoundsWidth) * 0.55f,
                    Random.Range(-BoundsHeight, BoundsHeight) * 0.55f);
                Emit(ObjectFxShape.Square, offset,
                    Vector2.right * Random.Range(-1.5f, 1.5f),
                    new Vector2(Random.Range(0.12f, 0.45f), Random.Range(0.03f, 0.1f)) * settings.scale,
                    Vector2.zero, colors[i % colors.Length],
                    settings.duration * Random.Range(0.35f, 1f), 0f, 0f);
            }
        }

        private void PlayPortal(ObjectRuleEffectParameters settings)
        {
            Pulse(ObjectFxShape.Ring, Vector2.zero, settings.primaryColor,
                1.4f * settings.scale, 0.45f * settings.scale, settings.duration);
            int count = Mathf.Clamp(settings.amount, 4, 20);
            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.right;
                Emit(ObjectFxShape.Diamond, direction * settings.scale,
                    Vector2.Perpendicular(direction) * 1.2f,
                    Vector2.one * 0.12f * settings.scale,
                    Vector2.one * 0.02f,
                    Color.Lerp(settings.primaryColor, settings.secondaryColor, (float)i / count),
                    settings.duration, 240f, 0.18f);
            }
        }

        private void Pulse(
            ObjectFxShape shape,
            Vector2 offset,
            Color color,
            float startScale,
            float endScale,
            float duration)
        {
            Emit(shape, offset, Vector2.zero,
                Vector2.one * startScale,
                Vector2.one * endScale,
                color, duration, 0f, 0f);
        }

        private void Burst(
            ObjectFxShape shape,
            int amount,
            Color color,
            float scale,
            float duration,
            float directionBias,
            bool groundBiased = false)
        {
            int count = Mathf.Clamp(amount, 1, 32);
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Random.insideUnitCircle;
                if (groundBiased)
                    direction.y = Mathf.Abs(direction.y) + 0.2f;
                if (Mathf.Abs(directionBias) > 0.01f)
                    direction.x += directionBias * 0.7f;
                direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.up;
                float particleScale = Random.Range(0.08f, 0.23f) * scale;
                Emit(shape, Vector2.zero, direction * Random.Range(0.6f, 2.2f) * scale,
                    Vector2.one * particleScale,
                    Vector2.one * particleScale * 0.15f,
                    color, duration * Random.Range(0.65f, 1.15f),
                    Random.Range(-360f, 360f), Random.Range(0f, 0.18f));
            }
        }

        private void RisingBurst(
            ObjectFxShape shape,
            int amount,
            Color first,
            Color second,
            float scale,
            float duration,
            float riseSpeed)
        {
            int count = Mathf.Clamp(amount, 1, 32);
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = new Vector2(Random.Range(-0.45f, 0.45f) * BoundsWidth,
                    Random.Range(-0.45f, 0.2f) * BoundsHeight);
                Vector2 velocity = new Vector2(Random.Range(-0.35f, 0.35f),
                    Random.Range(0.45f, 1.2f) * riseSpeed);
                float particleScale = Random.Range(0.12f, 0.3f) * scale;
                Emit(shape, offset, velocity,
                    Vector2.one * particleScale,
                    Vector2.one * particleScale * Random.Range(0.7f, 1.6f),
                    Color.Lerp(first, second, Random.value),
                    duration * Random.Range(0.7f, 1.2f),
                    Random.Range(-90f, 90f), 0.12f);
            }
        }

        private void Implode(
            ObjectFxShape shape,
            int amount,
            Color first,
            Color second,
            float scale,
            float duration)
        {
            int count = Mathf.Clamp(amount, 1, 32);
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(0.7f, 1.5f) * scale;
                Vector2 velocity = -offset / Mathf.Max(0.1f, duration);
                float particleScale = Random.Range(0.08f, 0.2f) * scale;
                Emit(shape, offset, velocity,
                    Vector2.one * particleScale,
                    Vector2.one * particleScale * 0.15f,
                    Color.Lerp(first, second, Random.value), duration,
                    Random.Range(-240f, 240f), 0.08f);
            }
        }

        private void Emit(
            ObjectFxShape shape,
            Vector2 localOffset,
            Vector2 velocity,
            Vector2 startScale,
            Vector2 endScale,
            Color color,
            float duration,
            float spin,
            float wave)
        {
            var particle = new GameObject("2DObjectMakerFx_" + shape);
            particle.transform.position = (Vector2)transform.position + localOffset;
            var renderer = particle.AddComponent<SpriteRenderer>();
            renderer.sprite = GetShapeSprite(shape);
            renderer.color = color;
            renderer.sortingLayerID = referenceRenderer != null ? referenceRenderer.sortingLayerID : 0;
            renderer.sortingOrder = referenceRenderer != null ? referenceRenderer.sortingOrder + 10 : 10;
            particle.AddComponent<ObjectFxParticle2D>().Initialize(
                velocity,
                startScale,
                endScale,
                color,
                Mathf.Max(0.1f, duration),
                spin,
                wave);
        }

        private float BoundsWidth => referenceRenderer != null && referenceRenderer.sprite != null
            ? Mathf.Max(0.5f, referenceRenderer.bounds.size.x)
            : 1f;

        private float BoundsHeight => referenceRenderer != null && referenceRenderer.sprite != null
            ? Mathf.Max(0.75f, referenceRenderer.bounds.size.y)
            : 1.5f;

        private static Sprite GetShapeSprite(ObjectFxShape shape)
        {
            int index = (int)shape;
            if (ShapeSprites[index] != null)
                return ShapeSprites[index];

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "2DObjectMakerFx_" + shape,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.45f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool filled;
                    float alpha = 1f;
                    switch (shape)
                    {
                        case ObjectFxShape.Ring:
                            filled = distance <= radius && distance >= radius * 0.7f;
                            break;
                        case ObjectFxShape.Square:
                            filled = Mathf.Abs(dx) <= radius * 0.78f && Mathf.Abs(dy) <= radius * 0.78f;
                            break;
                        case ObjectFxShape.Diamond:
                            filled = Mathf.Abs(dx) + Mathf.Abs(dy) <= radius;
                            break;
                        case ObjectFxShape.Star:
                            filled = Mathf.Abs(dx) <= 2.2f || Mathf.Abs(dy) <= 2.2f ||
                                     Mathf.Abs(dx - dy) <= 1.5f || Mathf.Abs(dx + dy) <= 1.5f;
                            filled &= distance <= radius;
                            break;
                        case ObjectFxShape.Arc:
                            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            filled = distance <= radius && distance >= radius * 0.68f &&
                                     Mathf.Abs(angle) <= 105f;
                            break;
                        case ObjectFxShape.Smoke:
                            filled = distance <= radius;
                            alpha = Mathf.Clamp01(1f - distance / radius) * 0.75f;
                            break;
                        default:
                            filled = distance <= radius;
                            alpha = Mathf.Clamp01(radius - distance + 0.65f);
                            break;
                    }
                    byte a = filled ? (byte)Mathf.RoundToInt(alpha * 255f) : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size);
            sprite.name = texture.name + "_Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            ShapeSprites[index] = sprite;
            return sprite;
        }
    }

    internal sealed class ObjectFxParticle2D : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;
        private Vector2 velocity;
        private Vector2 startScale;
        private Vector2 endScale;
        private Color startColor;
        private float duration;
        private float spin;
        private float wave;
        private float startedAt;

        public void Initialize(
            Vector2 sourceVelocity,
            Vector2 sourceStartScale,
            Vector2 sourceEndScale,
            Color color,
            float sourceDuration,
            float sourceSpin,
            float sourceWave)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            velocity = sourceVelocity;
            startScale = sourceStartScale;
            endScale = sourceEndScale;
            startColor = color;
            duration = Mathf.Max(0.05f, sourceDuration);
            spin = sourceSpin;
            wave = sourceWave;
            startedAt = Time.time;
            transform.localScale = new Vector3(startScale.x, startScale.y, 1f);
        }

        private void Update()
        {
            float elapsed = Time.time - startedAt;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector2 perpendicular = velocity.sqrMagnitude > 0.001f
                ? Vector2.Perpendicular(velocity.normalized)
                : Vector2.right;
            transform.position += (Vector3)((velocity + perpendicular *
                Mathf.Sin(t * Mathf.PI * 2f) * wave) * Time.deltaTime);
            transform.Rotate(0f, 0f, spin * Time.deltaTime);
            Vector2 scale = Vector2.Lerp(startScale, endScale, t);
            transform.localScale = new Vector3(scale.x, scale.y, 1f);
            if (spriteRenderer != null)
            {
                Color color = startColor;
                color.a *= 1f - t;
                spriteRenderer.color = color;
            }
            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
