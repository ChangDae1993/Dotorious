using System.Collections.Generic;
using UnityEngine;

namespace JYW.Game.EventPlay
{
    /// <summary>Persistent camera anchors, released explicitly by Camera Fix Disable.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class CameraFixController : MonoBehaviour
    {
        private sealed class Pin
        {
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public Vector3 WorldPosition;
        }

        private readonly Dictionary<Camera, Pin> pins = new Dictionary<Camera, Pin>();
        private readonly List<Camera> destroyedCameras = new List<Camera>();
        private EventPlayManager owner;

        public void Configure(EventPlayManager manager) { owner = manager; }

        public bool IsFixed(Camera camera) { return camera != null && pins.ContainsKey(camera); }

        public void FixTo(Camera camera, Vector3 worldPosition)
        {
            if (camera == null || !IsFinite(worldPosition)) return;
            if (!pins.TryGetValue(camera, out Pin pin))
            {
                pin = new Pin { Parent = camera.transform.parent, LocalPosition = camera.transform.localPosition,
                    LocalRotation = camera.transform.localRotation, LocalScale = camera.transform.localScale };
                pins.Add(camera, pin);
            }
            pin.WorldPosition = worldPosition;
            camera.transform.SetParent(null, true);
            camera.transform.position = worldPosition;
        }

        private void LateUpdate()
        {
            destroyedCameras.Clear();
            foreach (var pair in pins)
            {
                Camera camera = pair.Key;
                if (camera == null) { destroyedCameras.Add(camera); continue; }
                Pin pin = pair.Value;
                // Respawn code may attach the camera to a replacement player.
                // Remember that new follow attachment, then keep the anchor active.
                if (camera.transform.parent != null)
                {
                    pin.Parent = camera.transform.parent;
                    pin.LocalPosition = camera.transform.localPosition;
                    pin.LocalRotation = camera.transform.localRotation;
                    pin.LocalScale = camera.transform.localScale;
                    camera.transform.SetParent(null, true);
                }
                // Authored Move/Aim/Shake can play temporarily over an anchor.
                // They retain their existing duration and cleanup behavior.
                if (owner == null || !owner.HasTransientCameraMotion(camera))
                    camera.transform.position = pin.WorldPosition;
            }
            foreach (Camera camera in destroyedCameras) pins.Remove(camera);
        }

        public void ReleaseAll()
        {
            foreach (var pair in pins)
            {
                Camera camera = pair.Key;
                if (camera == null) continue;
                camera.transform.SetParent(pair.Value.Parent, false);
                camera.transform.localPosition = pair.Value.LocalPosition;
                camera.transform.localRotation = pair.Value.LocalRotation;
                camera.transform.localScale = pair.Value.LocalScale;
            }
            pins.Clear();
        }

        private void OnDisable() { ReleaseAll(); }
        private void OnDestroy() { ReleaseAll(); }

        private static bool IsFinite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
                   !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        }
    }
}
