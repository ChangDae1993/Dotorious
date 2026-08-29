using System.Collections;
using UnityEngine;

namespace JYW.Game.ObjectMaker
{
    [DisallowMultipleComponent]
    public sealed class ObjectAudioController2D : MonoBehaviour
    {
        [SerializeField] private AudioSource oneShotSource;
        [SerializeField] private AudioSource behaviorLoopSource;

        private Coroutine delayedBehavior;
        private int behaviorGeneration;

        public AudioSource OneShotSource => oneShotSource;
        public AudioSource BehaviorLoopSource => behaviorLoopSource;
        public AudioClip LastOneShotClip { get; private set; }
        public int OneShotPlayCount { get; private set; }
        public AudioClip ActiveBehaviorClip =>
            behaviorLoopSource != null && behaviorLoopSource.isPlaying
                ? behaviorLoopSource.clip
                : null;

        private void Awake()
        {
            EnsureSources();
        }

        private void OnDisable()
        {
            StopBehavior();
            StopAllCoroutines();
            if (oneShotSource != null)
                oneShotSource.Stop();
        }

        public void Configure()
        {
            EnsureSources();
        }

        public void PlayOneShot(ObjectSoundCue2D cue)
        {
            if (cue == null || cue.audioClip == null)
                return;

            EnsureSources();
            LastOneShotClip = cue.audioClip;
            OneShotPlayCount++;
            float delay = Mathf.Max(0f, cue.delaySeconds);
            float volume = Mathf.Clamp01(cue.volume);
            if (delay <= 0f)
            {
                oneShotSource.PlayOneShot(cue.audioClip, volume);
                return;
            }

            StartCoroutine(PlayOneShotAfterDelay(cue.audioClip, volume, delay));
        }

        public void PlayBehavior(ObjectSoundCue2D cue)
        {
            StopBehavior();
            if (cue == null || cue.audioClip == null)
                return;

            if (!cue.isLoop)
            {
                PlayOneShot(cue);
                return;
            }

            EnsureSources();
            int generation = ++behaviorGeneration;
            float delay = Mathf.Max(0f, cue.delaySeconds);
            if (delay <= 0f)
            {
                StartBehaviorLoop(cue.audioClip, cue.volume, generation);
                return;
            }

            delayedBehavior = StartCoroutine(StartBehaviorAfterDelay(
                cue.audioClip,
                cue.volume,
                delay,
                generation));
        }

        public void StopBehavior()
        {
            behaviorGeneration++;
            if (delayedBehavior != null)
            {
                StopCoroutine(delayedBehavior);
                delayedBehavior = null;
            }

            if (behaviorLoopSource == null)
                return;
            behaviorLoopSource.Stop();
            behaviorLoopSource.clip = null;
        }

        public void PlayDetachedOneShot(ObjectSoundCue2D cue)
        {
            if (cue == null || cue.audioClip == null)
                return;

            var holder = new GameObject(gameObject.name + " Death Sound");
            holder.transform.position = transform.position;
            AudioSource source = holder.AddComponent<AudioSource>();
            ConfigureSource(source, false);
            source.clip = cue.audioClip;
            source.volume = Mathf.Clamp01(cue.volume);
            float delay = Mathf.Max(0f, cue.delaySeconds);
            source.PlayDelayed(delay);
            Destroy(holder, delay + Mathf.Max(0.01f, cue.audioClip.length) + 0.25f);
        }

        private IEnumerator PlayOneShotAfterDelay(
            AudioClip clip,
            float volume,
            float delay)
        {
            yield return new WaitForSeconds(delay);
            if (oneShotSource != null && clip != null)
                oneShotSource.PlayOneShot(clip, volume);
        }

        private IEnumerator StartBehaviorAfterDelay(
            AudioClip clip,
            float volume,
            float delay,
            int generation)
        {
            yield return new WaitForSeconds(delay);
            delayedBehavior = null;
            StartBehaviorLoop(clip, volume, generation);
        }

        private void StartBehaviorLoop(AudioClip clip, float volume, int generation)
        {
            if (generation != behaviorGeneration || clip == null || !isActiveAndEnabled)
                return;
            EnsureSources();
            behaviorLoopSource.clip = clip;
            behaviorLoopSource.volume = Mathf.Clamp01(volume);
            behaviorLoopSource.loop = true;
            behaviorLoopSource.Play();
        }

        private void EnsureSources()
        {
            if (oneShotSource == null)
                oneShotSource = gameObject.AddComponent<AudioSource>();
            if (behaviorLoopSource == null)
                behaviorLoopSource = gameObject.AddComponent<AudioSource>();
            ConfigureSource(oneShotSource, false);
            ConfigureSource(behaviorLoopSource, true);
        }

        private static void ConfigureSource(AudioSource source, bool loop)
        {
            if (source == null)
                return;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
        }
    }
}
