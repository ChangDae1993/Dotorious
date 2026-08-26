using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    public enum ObjectDamageCause
    {
        Attack,
        Contact,
        Environment,
        Script
    }

    public readonly struct ObjectDamageRequest
    {
        public readonly int Amount;
        public readonly GameObject Source;
        public readonly Vector2 SourcePosition;
        public readonly float InvulnerabilitySeconds;
        public readonly float Knockback;
        public readonly ObjectDamageCause Cause;

        public ObjectDamageRequest(
            int amount,
            GameObject source,
            Vector2 sourcePosition,
            float invulnerabilitySeconds,
            float knockback,
            ObjectDamageCause cause)
        {
            Amount = amount;
            Source = source;
            SourcePosition = sourcePosition;
            InvulnerabilitySeconds = invulnerabilitySeconds;
            Knockback = knockback;
            Cause = cause;
        }
    }

    public readonly struct ObjectDamageResult
    {
        public readonly ObjectDamageRequest Request;
        public readonly int AppliedDamage;
        public readonly int RemainingHealth;
        public readonly bool IsDead;

        public ObjectDamageResult(
            ObjectDamageRequest request,
            int appliedDamage,
            int remainingHealth,
            bool isDead)
        {
            Request = request;
            AppliedDamage = appliedDamage;
            RemainingHealth = remainingHealth;
            IsDead = isDead;
        }
    }

    public interface IObjectDamageable
    {
        bool TryReceiveDamage(ObjectDamageRequest request);
    }
}
