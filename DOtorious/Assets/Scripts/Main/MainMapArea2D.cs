using UnityEngine;

namespace Dotorious.Winter
{
    // Ground references, entry and exit travel with the map root; world positions are not assumed.
    [DisallowMultipleComponent]
    public sealed class MainMapArea2D : MonoBehaviour
    {
        public string displayName;
        public Collider2D[] ground = System.Array.Empty<Collider2D>();
        public Transform entryPoint;
        [Min(1f)] public float fallMargin = 8f;

        public Bounds GroundBounds
        {
            get
            {
                var bounds = new Bounds(transform.position, Vector3.zero);
                bool found = false;
                foreach (var surface in ground)
                {
                    if (surface == null || !surface.enabled) continue;
                    if (!found) { bounds = surface.bounds; found = true; }
                    else bounds.Encapsulate(surface.bounds);
                }
                return bounds;
            }
        }

        public float FallY => GroundBounds.min.y - fallMargin;
    }
}
