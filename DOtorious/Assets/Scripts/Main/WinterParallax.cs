using UnityEngine;
namespace Dotorious.Winter
{
    public sealed class WinterParallax : MonoBehaviour
    {
        public Transform cameraTransform;
        [Range(0f,1f)] public float horizontalFactor = 0.12f;
        private Vector3 initialPosition;
        private float initialCameraX;
        private void Start()
        {
            initialPosition = transform.position;
            if (cameraTransform != null) initialCameraX = cameraTransform.position.x;
        }
        private void LateUpdate()
        {
            if (cameraTransform != null)
                transform.position = initialPosition + Vector3.right *
                    ((cameraTransform.position.x - initialCameraX) * horizontalFactor);
        }
    }
}

