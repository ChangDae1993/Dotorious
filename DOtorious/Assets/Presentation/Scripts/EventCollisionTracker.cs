using System;
using System.Collections.Generic;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    /// <summary>
    /// Condition 전용 충돌 기록소. 씬이나 프리팹을 수정하지 않고 런타임에만 relay를 부착한다.
    /// 3D/2D Collision과 Trigger의 Enter/Stay를 모두 "두 오브젝트가 부딪힌 적 있음"으로 기록한다.
    /// 필요한 물리 공간에 Collider가 없는 대상에는 런타임 전용 Trigger Collider를 자동 보충한다.
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
            EnsureCollisionComponents(a, b);
            EnsureWatched(a);
            EnsureWatched(b);
            return contacts.Contains(new InstancePair(a, b));
        }

        [Flags]
        private enum PhysicsSpace
        {
            None = 0,
            ThreeD = 1 << 0,
            TwoD = 1 << 1
        }

        private static void EnsureCollisionComponents(GameObject a, GameObject b)
        {
            PhysicsSpace spaces = PhysicsSpace.None;
            if (Has3DPhysics(a) || Has3DPhysics(b)) spaces |= PhysicsSpace.ThreeD;
            if (Has2DPhysics(a) || Has2DPhysics(b)) spaces |= PhysicsSpace.TwoD;

            // 물리 구성요소가 전혀 없는 새 대상은 SpriteRenderer가 있으면 2D,
            // 그 외에는 기존 Presentation의 기본 공간인 3D로 준비한다.
            if (spaces == PhysicsSpace.None)
            {
                spaces = HasSpriteRenderer(a) || HasSpriteRenderer(b)
                    ? PhysicsSpace.TwoD
                    : PhysicsSpace.ThreeD;
            }

            if ((spaces & PhysicsSpace.ThreeD) != 0)
            {
                bool addedA = Ensure3DTriggerCollider(a);
                bool addedB = Ensure3DTriggerCollider(b);
                Ensure3DPhysicsDriver(a, b, addedA, addedB);
            }

            if ((spaces & PhysicsSpace.TwoD) != 0)
            {
                bool addedA = Ensure2DTriggerCollider(a);
                bool addedB = Ensure2DTriggerCollider(b);
                Ensure2DPhysicsDriver(a, b, addedA, addedB);
            }
        }

        private static bool Has3DPhysics(GameObject target)
        {
            if (target == null) return false;
            if (HasEnabled3DCollider(target)) return true;
            return target.GetComponentInParent<Rigidbody>() != null ||
                   target.GetComponentInChildren<Rigidbody>(true) != null;
        }

        private static bool Has2DPhysics(GameObject target)
        {
            if (target == null) return false;
            if (HasEnabled2DCollider(target)) return true;
            return target.GetComponentInParent<Rigidbody2D>() != null ||
                   target.GetComponentInChildren<Rigidbody2D>(true) != null;
        }

        private static bool HasEnabled3DCollider(GameObject target)
        {
            if (target == null) return false;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled)
                    return true;
            return false;
        }

        private static bool HasEnabled2DCollider(GameObject target)
        {
            if (target == null) return false;
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled)
                    return true;
            return false;
        }

        private static bool HasSpriteRenderer(GameObject target)
        {
            return target != null && target.GetComponentInChildren<SpriteRenderer>(true) != null;
        }

        private static bool Ensure3DTriggerCollider(GameObject target)
        {
            if (target == null || HasEnabled3DCollider(target))
                return false;

            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            ApplyAutoBounds(target, collider, null);
            collider.hideFlags = HideFlags.HideInInspector;
            return true;
        }

        private static bool Ensure2DTriggerCollider(GameObject target)
        {
            if (target == null || HasEnabled2DCollider(target))
                return false;

            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            ApplyAutoBounds(target, null, collider);
            collider.hideFlags = HideFlags.HideInInspector;
            return true;
        }

        private static void Ensure3DPhysicsDriver(
            GameObject a,
            GameObject b,
            bool addedA,
            bool addedB)
        {
            bool hasCallbackBody = HasCallback3DRigidbody(a) || HasCallback3DRigidbody(b);
            bool hasTrigger = HasEnabled3DTrigger(a) || HasEnabled3DTrigger(b);
            if (HasDynamic3DRigidbody(a) || HasDynamic3DRigidbody(b) ||
                (hasCallbackBody && hasTrigger))
                return;

            // Collider가 원래부터 양쪽에 있어도 둘 다 static이면 Unity가 접촉 콜백을 보내지 않는다.
            // 기존 Collider/Rigidbody는 건드리지 않고 한쪽에 trigger 복제본과 드라이버만 보충한다.
            GameObject host = HasCallback3DRigidbody(a) ? a :
                (HasCallback3DRigidbody(b) ? b :
                    (!HasAttached3DRigidbody(a) && a != null ? a :
                        (!HasAttached3DRigidbody(b) && b != null ? b :
                            (addedA ? a : (addedB ? b : (a != null ? a : b))))));
            if (host == null) return;

            // 기존 static Collider를 대상에 직접 추가한 Rigidbody 소속으로 바꾸거나,
            // detectCollisions=false인 기존 Rigidbody 값을 변경하지 않는다.
            // Collider별 숨김 자식 probe가 독립적인 kinematic body로 콜백만 보충한다.
            AddDetached3DProbes(host);
        }

        private static void Ensure2DPhysicsDriver(
            GameObject a,
            GameObject b,
            bool addedA,
            bool addedB)
        {
            bool hasCallbackBody = HasCallback2DRigidbody(a) || HasCallback2DRigidbody(b);
            bool hasTrigger = HasEnabled2DTrigger(a) || HasEnabled2DTrigger(b);
            if (HasDynamic2DRigidbody(a) || HasDynamic2DRigidbody(b) ||
                (hasCallbackBody && hasTrigger))
                return;

            GameObject host = HasCallback2DRigidbody(a) ? a :
                (HasCallback2DRigidbody(b) ? b :
                    (!HasAttached2DRigidbody(a) && a != null ? a :
                        (!HasAttached2DRigidbody(b) && b != null ? b :
                            (addedA ? a : (addedB ? b : (a != null ? a : b))))));
            if (host == null) return;

            // 기존 static Collider2D를 대상에 직접 추가한 Rigidbody2D 소속으로 바꾸거나,
            // 기존 bodyType/simulated 값을 변경하지 않는다.
            AddDetached2DProbes(host);
        }

        private static bool HasAttached3DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled &&
                    colliders[i].attachedRigidbody != null)
                    return true;
            return false;
        }

        private static bool HasAttached2DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled &&
                    colliders[i].attachedRigidbody != null)
                    return true;
            return false;
        }

        private static bool HasEnabled3DTrigger(GameObject target)
        {
            if (target == null) return false;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled && colliders[i].isTrigger)
                    return true;
            return false;
        }

        private static bool HasEnabled2DTrigger(GameObject target)
        {
            if (target == null) return false;
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled && colliders[i].isTrigger)
                    return true;
            return false;
        }

        private static bool HasDynamic3DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                Rigidbody body = collider != null && collider.enabled
                    ? collider.attachedRigidbody
                    : null;
                if (body != null && body.detectCollisions && !body.isKinematic)
                    return true;
            }
            return false;
        }

        private static bool HasCallback3DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                Rigidbody body = collider != null && collider.enabled
                    ? collider.attachedRigidbody
                    : null;
                if (body != null && body.detectCollisions)
                    return true;
            }
            return false;
        }

        private static bool HasDynamic2DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                Rigidbody2D body = collider != null && collider.enabled
                    ? collider.attachedRigidbody
                    : null;
                if (body != null && body.simulated && body.bodyType == RigidbodyType2D.Dynamic)
                    return true;
            }
            return false;
        }

        private static bool HasCallback2DRigidbody(GameObject target)
        {
            if (target == null) return false;
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                Rigidbody2D body = collider != null && collider.enabled
                    ? collider.attachedRigidbody
                    : null;
                if (body != null && body.simulated && body.bodyType != RigidbodyType2D.Static)
                    return true;
            }
            return false;
        }

        private static void AddAuxiliary3DTriggers(GameObject target)
        {
            if (target == null) return;
            Collider[] sources = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                Collider source = sources[i];
                if (source == null || !source.enabled || source.isTrigger) continue;

                Collider trigger;
                BoxCollider sourceBox = source as BoxCollider;
                SphereCollider sourceSphere = source as SphereCollider;
                CapsuleCollider sourceCapsule = source as CapsuleCollider;
                MeshCollider sourceMesh = source as MeshCollider;

                if (sourceBox != null)
                {
                    BoxCollider copy = source.gameObject.AddComponent<BoxCollider>();
                    copy.center = sourceBox.center;
                    copy.size = sourceBox.size;
                    trigger = copy;
                }
                else if (sourceSphere != null)
                {
                    SphereCollider copy = source.gameObject.AddComponent<SphereCollider>();
                    copy.center = sourceSphere.center;
                    copy.radius = sourceSphere.radius;
                    trigger = copy;
                }
                else if (sourceCapsule != null)
                {
                    CapsuleCollider copy = source.gameObject.AddComponent<CapsuleCollider>();
                    copy.center = sourceCapsule.center;
                    copy.radius = sourceCapsule.radius;
                    copy.height = sourceCapsule.height;
                    copy.direction = sourceCapsule.direction;
                    trigger = copy;
                }
                else if (sourceMesh != null && sourceMesh.convex && sourceMesh.sharedMesh != null)
                {
                    MeshCollider copy = source.gameObject.AddComponent<MeshCollider>();
                    copy.sharedMesh = sourceMesh.sharedMesh;
                    copy.convex = true;
                    trigger = copy;
                }
                else
                {
                    BoxCollider copy = source.gameObject.AddComponent<BoxCollider>();
                    ApplyWorldBounds(source.transform, source.bounds, copy, null);
                    trigger = copy;
                }

                trigger.isTrigger = true;
                trigger.hideFlags = HideFlags.HideInInspector;
            }
        }

        private static void AddAuxiliary2DTriggers(GameObject target)
        {
            if (target == null) return;
            Collider2D[] sources = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                Collider2D source = sources[i];
                if (source == null || !source.enabled || source.isTrigger) continue;

                Collider2D trigger;
                BoxCollider2D sourceBox = source as BoxCollider2D;
                CircleCollider2D sourceCircle = source as CircleCollider2D;
                CapsuleCollider2D sourceCapsule = source as CapsuleCollider2D;

                if (sourceBox != null)
                {
                    BoxCollider2D copy = source.gameObject.AddComponent<BoxCollider2D>();
                    copy.offset = sourceBox.offset;
                    copy.size = sourceBox.size;
                    copy.edgeRadius = sourceBox.edgeRadius;
                    trigger = copy;
                }
                else if (sourceCircle != null)
                {
                    CircleCollider2D copy = source.gameObject.AddComponent<CircleCollider2D>();
                    copy.offset = sourceCircle.offset;
                    copy.radius = sourceCircle.radius;
                    trigger = copy;
                }
                else if (sourceCapsule != null)
                {
                    CapsuleCollider2D copy = source.gameObject.AddComponent<CapsuleCollider2D>();
                    copy.offset = sourceCapsule.offset;
                    copy.size = sourceCapsule.size;
                    copy.direction = sourceCapsule.direction;
                    trigger = copy;
                }
                else
                {
                    BoxCollider2D copy = source.gameObject.AddComponent<BoxCollider2D>();
                    ApplyWorldBounds(source.transform, source.bounds, null, copy);
                    trigger = copy;
                }

                trigger.isTrigger = true;
                trigger.hideFlags = HideFlags.HideInInspector;
            }
        }

        private static void AddDetached3DProbes(GameObject target)
        {
            if (target == null) return;

            Collider[] sources = target.GetComponentsInChildren<Collider>(true);
            bool hasSolidSource = false;
            for (int i = 0; i < sources.Length; i++)
            {
                Collider source = sources[i];
                if (IsUsableProbeSource(source) && !source.isTrigger)
                {
                    hasSolidSource = true;
                    break;
                }
            }

            for (int i = 0; i < sources.Length; i++)
            {
                Collider source = sources[i];
                if (!IsUsableProbeSource(source) || (hasSolidSource && source.isTrigger)) continue;

                GameObject probe = CreateProbeObject("__PresentationCollisionProbe3D", source.gameObject);
                Collider trigger = Clone3DColliderAsTrigger(source, probe);
                if (trigger == null)
                {
                    DestroyProbe(probe);
                    continue;
                }

                Rigidbody body = probe.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
                body.detectCollisions = true;
                body.hideFlags = HideFlags.HideInInspector;
            }
        }

        private static void AddDetached2DProbes(GameObject target)
        {
            if (target == null) return;

            Collider2D[] sources = target.GetComponentsInChildren<Collider2D>(true);
            bool hasSolidSource = false;
            for (int i = 0; i < sources.Length; i++)
            {
                Collider2D source = sources[i];
                if (IsUsableProbeSource(source) && !source.isTrigger)
                {
                    hasSolidSource = true;
                    break;
                }
            }

            for (int i = 0; i < sources.Length; i++)
            {
                Collider2D source = sources[i];
                if (!IsUsableProbeSource(source) || (hasSolidSource && source.isTrigger)) continue;

                GameObject probe = CreateProbeObject("__PresentationCollisionProbe2D", source.gameObject);
                Collider2D trigger = Clone2DColliderAsTrigger(source, probe);
                if (trigger == null)
                {
                    DestroyProbe(probe);
                    continue;
                }

                Rigidbody2D body = probe.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.simulated = true;
                body.useFullKinematicContacts = true;
                body.hideFlags = HideFlags.HideInInspector;
            }
        }

        private static bool IsUsableProbeSource(Collider source)
        {
            return source != null && source.enabled &&
                   source.GetComponent<EventCollisionProbeMarker>() == null;
        }

        private static bool IsUsableProbeSource(Collider2D source)
        {
            return source != null && source.enabled &&
                   source.GetComponent<EventCollisionProbeMarker>() == null;
        }

        private static GameObject CreateProbeObject(string name, GameObject sourceObject)
        {
            var probe = new GameObject(name);
            probe.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            if (sourceObject != null)
            {
                probe.layer = sourceObject.layer;
                probe.transform.SetParent(sourceObject.transform, false);
            }
            EventCollisionProbeMarker marker = probe.AddComponent<EventCollisionProbeMarker>();
            marker.hideFlags = HideFlags.HideInInspector;
            return probe;
        }

        private static Collider Clone3DColliderAsTrigger(Collider source, GameObject probe)
        {
            if (source == null || probe == null) return null;

            Collider trigger;
            if (source is BoxCollider sourceBox)
            {
                BoxCollider copy = probe.AddComponent<BoxCollider>();
                copy.center = sourceBox.center;
                copy.size = sourceBox.size;
                trigger = copy;
            }
            else if (source is SphereCollider sourceSphere)
            {
                SphereCollider copy = probe.AddComponent<SphereCollider>();
                copy.center = sourceSphere.center;
                copy.radius = sourceSphere.radius;
                trigger = copy;
            }
            else if (source is CapsuleCollider sourceCapsule)
            {
                CapsuleCollider copy = probe.AddComponent<CapsuleCollider>();
                copy.center = sourceCapsule.center;
                copy.radius = sourceCapsule.radius;
                copy.height = sourceCapsule.height;
                copy.direction = sourceCapsule.direction;
                trigger = copy;
            }
            else if (source is MeshCollider sourceMesh && sourceMesh.convex && sourceMesh.sharedMesh != null)
            {
                MeshCollider copy = probe.AddComponent<MeshCollider>();
                copy.sharedMesh = sourceMesh.sharedMesh;
                copy.convex = true;
                trigger = copy;
            }
            else
            {
                BoxCollider copy = probe.AddComponent<BoxCollider>();
                ApplyWorldBounds(probe.transform, source.bounds, copy, null);
                trigger = copy;
            }

            trigger.isTrigger = true;
            trigger.hideFlags = HideFlags.HideInInspector;
            return trigger;
        }

        private static Collider2D Clone2DColliderAsTrigger(Collider2D source, GameObject probe)
        {
            if (source == null || probe == null) return null;

            Collider2D trigger;
            if (source is BoxCollider2D sourceBox)
            {
                BoxCollider2D copy = probe.AddComponent<BoxCollider2D>();
                copy.offset = sourceBox.offset;
                copy.size = sourceBox.size;
                copy.edgeRadius = sourceBox.edgeRadius;
                trigger = copy;
            }
            else if (source is CircleCollider2D sourceCircle)
            {
                CircleCollider2D copy = probe.AddComponent<CircleCollider2D>();
                copy.offset = sourceCircle.offset;
                copy.radius = sourceCircle.radius;
                trigger = copy;
            }
            else if (source is CapsuleCollider2D sourceCapsule)
            {
                CapsuleCollider2D copy = probe.AddComponent<CapsuleCollider2D>();
                copy.offset = sourceCapsule.offset;
                copy.size = sourceCapsule.size;
                copy.direction = sourceCapsule.direction;
                trigger = copy;
            }
            else if (source is PolygonCollider2D sourcePolygon)
            {
                PolygonCollider2D copy = probe.AddComponent<PolygonCollider2D>();
                copy.offset = sourcePolygon.offset;
                copy.pathCount = sourcePolygon.pathCount;
                for (int pathIndex = 0; pathIndex < sourcePolygon.pathCount; pathIndex++)
                    copy.SetPath(pathIndex, sourcePolygon.GetPath(pathIndex));
                trigger = copy;
            }
            else if (source is EdgeCollider2D sourceEdge)
            {
                EdgeCollider2D copy = probe.AddComponent<EdgeCollider2D>();
                copy.offset = sourceEdge.offset;
                copy.edgeRadius = sourceEdge.edgeRadius;
                copy.points = sourceEdge.points;
                trigger = copy;
            }
            else
            {
                BoxCollider2D copy = probe.AddComponent<BoxCollider2D>();
                ApplyWorldBounds(probe.transform, source.bounds, null, copy);
                trigger = copy;
            }

            trigger.isTrigger = true;
            trigger.hideFlags = HideFlags.HideInInspector;
            return trigger;
        }

        private static void DestroyProbe(GameObject probe)
        {
            if (probe == null) return;
            UnityEngine.Object.Destroy(probe);
        }

        private static void ApplyWorldBounds(
            Transform localSpace,
            Bounds worldBounds,
            BoxCollider collider3D,
            BoxCollider2D collider2D)
        {
            Bounds localBounds = CalculateLocalBounds(localSpace, worldBounds);
            Vector3 size = localBounds.size;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));

            if (collider3D != null)
            {
                collider3D.center = localBounds.center;
                collider3D.size = size;
            }

            if (collider2D != null)
            {
                collider2D.offset = new Vector2(localBounds.center.x, localBounds.center.y);
                collider2D.size = new Vector2(size.x, size.y);
            }
        }

        private static void ApplyAutoBounds(
            GameObject target,
            BoxCollider collider3D,
            BoxCollider2D collider2D)
        {
            Bounds localBounds = CalculateLocalBounds(target);
            Vector3 size = localBounds.size;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));

            if (collider3D != null)
            {
                collider3D.center = localBounds.center;
                collider3D.size = size;
            }

            if (collider2D != null)
            {
                collider2D.offset = new Vector2(localBounds.center.x, localBounds.center.y);
                collider2D.size = new Vector2(size.x, size.y);
            }
        }

        private static Bounds CalculateLocalBounds(GameObject target)
        {
            if (target == null) return new Bounds(Vector3.zero, Vector3.one);

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    Vector3 localCorner = target.transform.InverseTransformPoint(worldCorner);
                    if (!initialized)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            if (initialized) return localBounds;

            RectTransform rectTransform = target.transform as RectTransform;
            if (rectTransform != null)
            {
                Rect rect = rectTransform.rect;
                return new Bounds(
                    new Vector3(rect.center.x, rect.center.y, 0f),
                    new Vector3(Mathf.Abs(rect.width), Mathf.Abs(rect.height), 0.01f));
            }

            return new Bounds(Vector3.zero, Vector3.one);
        }

        private static Bounds CalculateLocalBounds(Transform localSpace, Bounds worldBounds)
        {
            if (localSpace == null) return new Bounds(Vector3.zero, Vector3.one);

            bool initialized = false;
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = center + Vector3.Scale(
                    extents,
                    new Vector3(x, y, z));
                Vector3 localCorner = localSpace.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }

            return initialized ? localBounds : new Bounds(Vector3.zero, Vector3.one);
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
    internal sealed class EventCollisionProbeMarker : MonoBehaviour { }

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
