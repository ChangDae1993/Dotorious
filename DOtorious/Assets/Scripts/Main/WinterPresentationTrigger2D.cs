using System.Collections;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using UnityEngine;

namespace Dotorious.Winter
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class WinterPresentationTrigger2D : MonoBehaviour
    {
        public EventSO presentation;
        public WinterStageRuntime stage;
        public bool checkpoint;
        public string checkpointLabel;
        public Vector3 respawnPosition;
        public bool finish;
        private bool used;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (used || stage == null || !stage.HasStarted) return;
            var actor = other.GetComponentInParent<ObjectActor2D>();
            if (actor == null || actor.Kind != ObjectKind.Player) return;
            used = true;
            if (checkpoint) stage.SetCheckpoint(respawnPosition, checkpointLabel);
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            var manager = EventPlayManager.Instance;
            if (manager != null && presentation != null)
            {
                while (manager != null && manager.IsEventRunning) yield return null;
                if (manager != null)
                {
                    manager.PlayEvent(presentation, gameObject);
                    yield return null;
                    while (manager != null && manager.IsEventRunning) yield return null;
                }
            }
            if (finish && stage != null) stage.FinishStage();
        }
    }
}

