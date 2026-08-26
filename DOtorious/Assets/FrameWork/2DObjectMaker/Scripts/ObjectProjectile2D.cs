using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    [DisallowMultipleComponent]
    public sealed class ObjectProjectile2D : MonoBehaviour
    {
        private static Sprite projectileSprite;

        private ObjectActor2D owner;
        private Rigidbody2D body;
        private int damage;
        private float invulnerabilitySeconds;
        private float knockback;
        private float expiresAt;

        public ObjectActor2D Owner => owner;
        public Vector2 Velocity => body != null ? body.linearVelocity : Vector2.zero;

        public static ObjectProjectile2D Spawn(
            ObjectActor2D source,
            Vector2 position,
            Vector2 velocity,
            int damage,
            float invulnerabilitySeconds,
            float knockback,
            float lifetime,
            Color color)
        {
            var host = new GameObject(source != null
                ? source.name + "_Projectile"
                : "2DObjectMaker_Projectile");
            host.transform.position = position;

            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = GetProjectileSprite();
            renderer.color = color;
            renderer.sortingOrder = source != null && source.SpriteRenderer != null
                ? source.SpriteRenderer.sortingOrder + 2
                : 2;

            var rigidbody = host.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            rigidbody.gravityScale = 0f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rigidbody.linearVelocity = velocity;

            var collider = host.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.16f;

            ObjectProjectile2D projectile = host.AddComponent<ObjectProjectile2D>();
            projectile.owner = source;
            projectile.body = rigidbody;
            projectile.damage = Mathf.Max(0, damage);
            projectile.invulnerabilitySeconds = Mathf.Max(0f, invulnerabilitySeconds);
            projectile.knockback = Mathf.Max(0f, knockback);
            projectile.expiresAt = Time.time + Mathf.Max(0.1f, lifetime);

            if (source != null && source.BodyCollider != null)
                Physics2D.IgnoreCollision(collider, source.BodyCollider, true);
            return projectile;
        }

        private void Update()
        {
            if (Time.time >= expiresAt)
                Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null)
                return;

            ObjectActor2D candidate = other.GetComponentInParent<ObjectActor2D>();
            if (candidate == null || candidate == owner || candidate.IsDead ||
                candidate.Kind != ObjectKind.Player)
                return;

            Vector2 sourcePosition = owner != null ? owner.transform.position : transform.position;
            if (candidate.TryReceiveDamage(new ObjectDamageRequest(
                    damage,
                    owner != null ? owner.gameObject : gameObject,
                    sourcePosition,
                    invulnerabilitySeconds,
                    knockback,
                    ObjectDamageCause.Attack)))
            {
                Destroy(gameObject);
            }
        }

        private static Sprite GetProjectileSprite()
        {
            if (projectileSprite != null)
                return projectileSprite;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "2DObjectMaker_Projectile",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.46f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    byte alpha = distance <= radius
                        ? (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - distance + 0.5f) * 255f)
                        : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            projectileSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            projectileSprite.name = "2DObjectMaker_Projectile_Sprite";
            projectileSprite.hideFlags = HideFlags.HideAndDontSave;
            return projectileSprite;
        }
    }
}
