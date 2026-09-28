using System;
using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    /// <summary>Swept player projectile; enemy ObjectProjectile2D behavior remains unchanged.</summary>
    [DisallowMultipleComponent]
    public sealed class ObjectPlayerProjectile2D : MonoBehaviour
    {
        private ObjectActor2D source;
        private ObjectPlayerAttack settings;
        private Vector2 velocity;
        private float expires;
        public static ObjectPlayerProjectile2D Spawn(ObjectActor2D owner, ObjectPlayerAttack attack,
            Vector2 origin, float direction)
        {
            var go = attack.effectPrefab != null
                ? Instantiate(attack.effectPrefab, origin, Quaternion.identity)
                : new GameObject("Player Projectile");
            go.transform.position = origin;
            Vector3 scale = go.transform.localScale;
            scale.x = Mathf.Abs(scale.x) * direction;
            go.transform.localScale = scale;
            var projectile = go.GetComponent<ObjectPlayerProjectile2D>();
            if (projectile == null) projectile = go.AddComponent<ObjectPlayerProjectile2D>();
            projectile.source = owner;
            projectile.settings = attack.Clone();
            projectile.velocity = Vector2.right * direction * attack.projectileSpeed;
            projectile.expires = Time.time + attack.projectileLifetime;
            go.GetComponent<ObjectSkillVisual2D>()?.Play(attack.projectileLifetime, true);
            return projectile;
        }

        private void FixedUpdate()
        {
            if (source == null || source.IsDead || Time.time >= expires) { Destroy(gameObject); return; }
            velocity += Physics2D.gravity * settings.projectileGravity * Time.fixedDeltaTime;
            Vector2 delta = velocity * Time.fixedDeltaTime;
            RaycastHit2D[] hits = Physics2D.CircleCastAll(transform.position, settings.projectileRadius,
                delta.normalized, delta.magnitude);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.transform.IsChildOf(source.transform)) continue;
                var target = hit.collider.GetComponentInParent<ObjectActor2D>();
                if (target != null)
                {
                    if (target == source || target.IsDead || target.Kind == ObjectKind.Player) continue;
                    target.TryReceiveDamage(new ObjectDamageRequest(settings.damage, source.gameObject,
                        transform.position, settings.targetInvulnerabilitySeconds, settings.knockback, ObjectDamageCause.Attack));
                    Destroy(gameObject);
                    return;
                }
                if (!hit.collider.isTrigger && (settings.obstacleMask.value & (1 << hit.collider.gameObject.layer)) != 0)
                { Destroy(gameObject); return; }
            }
            transform.position += (Vector3)delta;
        }
    }
}
