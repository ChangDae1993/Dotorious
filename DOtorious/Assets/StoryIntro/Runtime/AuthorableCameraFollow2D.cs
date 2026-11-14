using JYW.Game.EventPlay;
using UnityEngine;

namespace Dotorious.StoryIntro
{
    [RequireComponent(typeof(Camera))]
    public sealed class AuthorableCameraFollow2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0, 2.7f, -10);
        [SerializeField] private float smoothTime = .2f;
        public bool Follow { get; set; }
        private Vector3 velocity;

        public void Configure(Transform source) => target = source;

        private void LateUpdate()
        {
            if (!Follow || target == null ||
                (EventPlayManager.Instance?.IsCameraMovementControlled(GetComponent<Camera>()) ?? false))
            {
                velocity = Vector3.zero;
                return;
            }
            transform.position = Vector3.SmoothDamp(transform.position, target.position + offset,
                ref velocity, smoothTime, Mathf.Infinity, Time.deltaTime);
        }
    }
}
