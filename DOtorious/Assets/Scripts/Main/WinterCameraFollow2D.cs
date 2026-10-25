using JYW.Game.EventPlay;
using JYW.Game.ObjectMaker;
using UnityEngine;

namespace Dotorious.Winter
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class WinterCameraFollow2D : MonoBehaviour
    {
        public WinterStageRuntime stage;
        public Camera gameplayCamera;
        [Min(0f)] public float lookAheadDistance = 2.5f;
        [Min(0.01f)] public float smoothTime = 0.22f;
        public Vector3 baseOffset = new Vector3(0f, 3f, -10f);

        private Vector3 cameraVelocity;
        private ObjectActor2D followedPlayer;

        private void Awake()
        {
            if (gameplayCamera == null)
                gameplayCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            cameraVelocity = Vector3.zero;
            followedPlayer = null;
        }

        private void LateUpdate()
        {
            // The authored Start event owns the camera until the title intro
            // finishes. Zoom belongs to the Camera/Lens settings, not follow.
            if (stage == null || !stage.HasStarted || gameplayCamera == null ||
                !gameplayCamera.isActiveAndEnabled || Time.deltaTime <= 0f)
            {
                cameraVelocity = Vector3.zero;
                return;
            }

            ObjectActor2D player = stage.player;
            if (player == null || player.IsDead)
            {
                cameraVelocity = Vector3.zero;
                followedPlayer = null;
                return;
            }

            EventPlayManager manager = EventPlayManager.Instance;
            if (manager != null && manager.IsCameraMovementControlled(gameplayCamera))
            {
                cameraVelocity = Vector3.zero;
                return;
            }

            // WinterStageRuntime replaces stage.player when respawning. Read
            // it here rather than retaining the original scene player's ID.
            if (followedPlayer != player)
            {
                followedPlayer = player;
                cameraVelocity = Vector3.zero;
            }

            float direction = player.IsFacingRight ? 1f : -1f;
            Vector3 target = player.transform.position + baseOffset +
                             Vector3.right * (direction * Mathf.Max(0f, lookAheadDistance));
            gameplayCamera.transform.position = Vector3.SmoothDamp(
                gameplayCamera.transform.position, target, ref cameraVelocity,
                Mathf.Max(0.01f, smoothTime), Mathf.Infinity, Time.deltaTime);
        }
    }
}
