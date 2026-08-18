using System;
using System.Collections.Generic;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    /// <summary>
    /// Condition 전용 충돌 기록소. 씬이나 프리팹을 수정하지 않고 런타임에만 relay를 부착한다.
    /// 3D/2D Collision과 Trigger의 Enter/Stay를 모두 "두 오브젝트가 부딪힌 적 있음"으로 기록한다.
    /// </summary>
    internal static class EventCollisionTracker
    {
        private readonly struct InstancePair : IEquatable<InstancePair>
        {
            private readonly GameObject a;
            private readonly GameObject b;

            public InstancePair(GameObject first, GameObject second)
            {
                a = first;
                b = second;
            }

            public bool IsAlive => a != null && b != null;
            public bool Equals(InstancePair other) =>
                (a == other.a && b == other.b) || (a == other.b && b == other.a);
            public override bool Equals(object obj) => obj is InstancePair other && Equals(other);
            public override int GetHashCode()
            {
                return (a != null ? GetObjectId(a) : 0) ^ (b != null ? GetObjectId(b) : 0);
            }
        }

        private static readonly HashSet<GameObject> watchedTargets = new HashSet<GameObject>();
        private static readonly HashSet<InstancePair> contacts = new HashSet<InstancePair>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            watchedTargets.Clear();
            contacts.Clear();
        }

        internal static bool HasCollided(GameObject a, GameObject b)
        {
            if (a == null || b == null || a == b) return false;

            PruneDestroyedState();
            EnsureWatched(a);
            EnsureWatched(b);
            return contacts.Contains(new InstancePair(a, b));
        }

        private static void EnsureWatched(GameObject target)
        {
            if (target == null) return;
            watchedTargets.Add(target);

            BindRelay(target, target);

            Collider[] colliders3D = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders3D.Length; i++)
            {
                Collider collider = colliders3D[i];
                if (collider == null) continue;
                BindRelay(collider.gameObject, target);
                if (collider.attachedRigidbody != null)
                    BindRelay(collider.attachedRigidbody.gameObject, target);
            }

            Collider2D[] colliders2D = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders2D.Length; i++)
            {
                Collider2D collider = colliders2D[i];
                if (collider == null) continue;
                BindRelay(collider.gameObject, target);
                if (collider.attachedRigidbody != null)
                    BindRelay(collider.attachedRigidbody.gameObject, target);
            }
        }


        private static void BindRelay(GameObject host, GameObject target)
        {
            if (host == null || target == null) return;
            EventCollisionRelay relay = host.GetComponent<EventCollisionRelay>();
            if (relay == null) relay = host.AddComponent<EventCollisionRelay>();
            relay.hideFlags = HideFlags.HideInInspector;
            relay.Bind(target);
        }

        internal static void Report(EventCollisionRelay relay, params Transform[] contactTransforms)
        {
            if (relay == null || contactTransforms == null || contactTransforms.Length == 0) return;
            PruneDestroyedState();

            IReadOnlyList<GameObject> owners = relay.Owners;
            for (int oi = 0; oi < owners.Count; oi++)
            {
                GameObject owner = owners[oi];
                if (owner == null) continue;

                foreach (GameObject otherTarget in watchedTargets)
                {
                    if (otherTarget == null || otherTarget == owner) continue;
                    if (!ContainsAnyTransform(otherTarget.transform, contactTransforms)) continue;
                    Record(owner, otherTarget);
                }
            }
        }

        private static bool ContainsAnyTransform(Transform root, Transform[] candidates)
        {
            if (root == null) return false;
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform candidate = candidates[i];
                if (candidate != null && (candidate == root || candidate.IsChildOf(root)))
                    return true;
            }
            return false;
        }

        private static void Record(GameObject a, GameObject b)
        {
            contacts.Add(new InstancePair(a, b));
        }

        private static int GetObjectId(GameObject value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId().GetHashCode();
#else
            return value.GetInstanceID();
#endif
        }

        private static void PruneDestroyedState()
        {
            watchedTargets.RemoveWhere(target => target == null);
            if (contacts.Count == 0) return;

            contacts.RemoveWhere(pair => !pair.IsAlive);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class EventCollisionRelay : MonoBehaviour
    {
        private readonly List<GameObject> owners = new List<GameObject>();
        internal IReadOnlyList<GameObject> Owners => owners;

        internal void Bind(GameObject owner)
        {
            if (owner != null && !owners.Contains(owner)) owners.Add(owner);
            for (int i = owners.Count - 1; i >= 0; i--)
                if (owners[i] == null) owners.RemoveAt(i);
        }

        private void OnCollisionEnter(Collision collision) => Report3D(collision);
        private void OnCollisionStay(Collision collision) => Report3D(collision);
        private void OnTriggerEnter(Collider other) => ReportTransforms(other != null ? other.transform : null);
        private void OnTriggerStay(Collider other) => ReportTransforms(other != null ? other.transform : null);

        private void OnCollisionEnter2D(Collision2D collision) => Report2D(collision);
        private void OnCollisionStay2D(Collision2D collision) => Report2D(collision);
        private void OnTriggerEnter2D(Collider2D other) => ReportTransforms(other != null ? other.transform : null);
        private void OnTriggerStay2D(Collider2D other) => ReportTransforms(other != null ? other.transform : null);

        private void Report3D(Collision collision)
        {
            if (collision == null) return;
            ReportTransforms(
                collision.transform,
                collision.gameObject != null ? collision.gameObject.transform : null,
                collision.collider != null ? collision.collider.transform : null);
        }

        private void Report2D(Collision2D collision)
        {
            if (collision == null) return;
            ReportTransforms(
                collision.transform,
                collision.gameObject != null ? collision.gameObject.transform : null,
                collision.collider != null ? collision.collider.transform : null,
                collision.otherCollider != null ? collision.otherCollider.transform : null);
        }

        private void ReportTransforms(params Transform[] transforms)
        {
            EventCollisionTracker.Report(this, transforms);
        }
    }
}
