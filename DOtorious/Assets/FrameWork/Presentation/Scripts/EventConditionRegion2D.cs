using UnityEngine;

namespace JYW.Game.EventPlay
{
    /// <summary>Authoring link only. The EventSO's own collision condition is evaluated by Presentation.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
    public sealed class EventConditionRegion2D : MonoBehaviour
    {
        [SerializeField] private EventSO eventSO;
        [SerializeField] private Collider2D actor;
        [SerializeField, Min(0)] private int conditionIndex;
        public EventSO Event { get => eventSO; set => eventSO = value; }
        public Collider2D Actor { get => actor; set => actor = value; }
        public int ConditionIndex { get => conditionIndex; set => conditionIndex = value; }
    }
}
