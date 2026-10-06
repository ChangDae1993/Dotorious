using UnityEngine;

namespace Dotorious.Maps
{
    public enum MapIngredientKind
    {
        Ground, Ramp, Stairs, Platform, Bridge, Wall,
        Foreground, Background, FarBackground, Decoration
    }

    /// <summary>Identifies a ready-to-place map part. Physics and art are baked into its prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class MapIngredient2D : MonoBehaviour
    {
        [SerializeField] private MapIngredientKind kind;
        [SerializeField, Range(1, 10)] private int version = 1;
        [SerializeField] private string theme;

        public MapIngredientKind Kind => kind;
        public int Version => version;
        public string Theme => theme;
        public bool IsTerrain => kind <= MapIngredientKind.Wall;
        public bool VisibleOnMinimap => IsTerrain || kind == MapIngredientKind.Background;

        public void Configure(MapIngredientKind nextKind, int nextVersion, string nextTheme)
        {
            kind = nextKind;
            version = Mathf.Clamp(nextVersion, 1, 10);
            theme = nextTheme;
        }
    }
}
