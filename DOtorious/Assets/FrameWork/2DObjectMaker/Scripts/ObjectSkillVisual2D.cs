using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    /// <summary>Reusable sprite flipbook. Sprite pivots define the effect's origin.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ObjectSkillVisual2D : MonoBehaviour
    {
        public Sprite[] frames = new Sprite[0];
        public float framesPerSecond = 12f;
        public bool loop;
        private SpriteRenderer visual;
        private float started;
        private float duration;

        private void OnEnable() { visual = GetComponent<SpriteRenderer>(); started = Time.time; }
        public void Play(float seconds, bool repeat = false)
        {
            duration = Mathf.Max(0.01f, seconds);
            loop = repeat;
            started = Time.time;
        }
        private void Update()
        {
            if (visual == null || frames == null || frames.Length == 0) return;
            float age = Time.time - started;
            int index = duration > 0f && !loop
                ? Mathf.FloorToInt(age / duration * frames.Length)
                : Mathf.FloorToInt(age * Mathf.Max(1f, framesPerSecond));
            visual.sprite = frames[loop ? index % frames.Length : Mathf.Clamp(index, 0, frames.Length - 1)];
        }
    }
}
