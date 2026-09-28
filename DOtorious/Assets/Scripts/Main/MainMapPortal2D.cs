using System.Collections;
using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using UnityEngine;

namespace Dotorious.Winter
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class MainMapPortal2D : MonoBehaviour
    {
        public WinterStageRuntime stage;
        public MainMapArea2D sourceMap;
        public MainMapArea2D nextMap;
        public EventSO presentation;
        public bool finishAtLastMap;
        private bool pending;
        private EventSO playback;

        private void OnTriggerEnter2D(Collider2D other) { TryEnter(other); }
        private void OnTriggerStay2D(Collider2D other) { TryEnter(other); }

        private void TryEnter(Collider2D other)
        {
            if (pending || stage == null || !stage.HasStarted || stage.IsFinished ||
                stage.CurrentMap != sourceMap) return;
            var actor = other.GetComponentInParent<ObjectActor2D>();
            if (actor == null || actor != stage.player || actor.IsDead) return;
            if (nextMap == null)
            {
                if (finishAtLastMap) stage.FinishStage();
                return;
            }
            if (nextMap.entryPoint == null || presentation == null || EventPlayManager.Instance == null)
                return;
            pending = true;
            StartCoroutine(Transfer(actor));
        }

        private IEnumerator Transfer(ObjectActor2D actor)
        {
            var manager = EventPlayManager.Instance;
            while (manager != null && manager.IsEventRunning) yield return null;
            // The actor may leave the portal while a preceding story event is playing.
            if (manager == null || actor == null || actor.IsDead ||
                !GetComponent<BoxCollider2D>().bounds.Intersects(actor.BodyCollider.bounds))
            { pending = false; yield break; }

            // Collision is handled here. Keep the authored condition and all phases on the asset intact.
            playback = Instantiate(presentation);
            playback.UseCondition = false;
            manager.PlayEvent(playback, actor.gameObject);
            yield return null;
            // Only commit the new checkpoint if the EventSO actually moved the player.
            if (actor != null && nextMap != null && nextMap.entryPoint != null &&
                Vector2.Distance(actor.transform.position, nextMap.entryPoint.position) < 1f)
                stage.EnterMap(nextMap);
            while (manager != null && manager.IsEventRunning) yield return null;
            if (playback != null) Destroy(playback);
            playback = null;
            pending = false;
        }

        private void OnDestroy() { if (playback != null) Destroy(playback); }
    }
}
