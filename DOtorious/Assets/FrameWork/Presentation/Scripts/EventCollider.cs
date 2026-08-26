using UnityEngine;

namespace JYW.Game.EventPlay
{
    public class EventCollider : MonoBehaviour
    {
        [SerializeField]
        private EventSO eventSO;

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                EventPlayManager.Instance.PlayEvent(eventSO, gameObject);
            }
        }
    }
}