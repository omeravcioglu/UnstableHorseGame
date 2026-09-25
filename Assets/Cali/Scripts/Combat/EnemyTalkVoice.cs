using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Plays a random EnemyTalking line when a humanoid spots / stays near a horse.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EnemyTalkVoice : MonoBehaviour
    {
        [Tooltip("Unused. Talk uses the enemy detect radius.")]
        public float hearRange = 34f;

        [Tooltip("Seconds after first seeing a horse before the opening line.")]
        public Vector2 firstLineDelay = new Vector2(0.35f, 1.1f);

        [Tooltip("Seconds between lines while still in range.")]
        public Vector2 lineInterval = new Vector2(7.5f, 13f);

        [Range(0f, 1f)]
        public float volume = 0.92f;

        static float _globalReadyAt;
        static AudioClip _lastGlobalClip;

        ChainKillableEnemy _enemy;
        AudioSource _source;
        bool _hasSpotted;
        float _nextLineAt;
        float _nextSenseAt;

        void Awake()
        {
            _enemy = GetComponent<ChainKillableEnemy>();
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.dopplerLevel = 0f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 5f;
            _source.maxDistance = 42f;
            _source.priority = 96;
        }

        void Update()
        {
            if (Cali.Audio.CaliGameplayAudio.IsSuppressed)
            {
                StopTalk();
                return;
            }

            if (_enemy != null && _enemy.IsDead)
            {
                StopTalk();
                enabled = false;
                return;
            }

            if (Time.time < _nextSenseAt)
                return;
            _nextSenseAt = Time.time + 0.2f;

            float detect = _enemy != null ? _enemy.DetectRadius : hearRange;
            var horse = EnemyCombatTargets.FindNearestHorseInRange(transform.position, detect);
            if (horse == null)
            {
                _hasSpotted = false;
                return;
            }

            if (!_hasSpotted)
            {
                _hasSpotted = true;
                _nextLineAt = Time.time + Random.Range(firstLineDelay.x, firstLineDelay.y);
            }

            if (Time.time < _nextLineAt || Time.time < _globalReadyAt)
                return;

            if (_source.isPlaying)
                return;

            TryPlayLine();
        }

        public static void StopAllTalk()
        {
            var voices = FindObjectsByType<EnemyTalkVoice>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i] != null)
                    voices[i].StopTalk();
            }
        }

        public void StopTalk()
        {
            if (_source != null && _source.isPlaying)
                _source.Stop();
        }

        void TryPlayLine()
        {
            var lib = EnemyTalkLibrary.Get();
            if (lib == null)
                return;

            var clip = lib.PickRandom(_lastGlobalClip);
            if (clip == null)
                return;

            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            _source.pitch = Random.Range(0.97f, 1.04f);
            _source.volume = volume;
            _source.PlayOneShot(clip, volume);

            _lastGlobalClip = clip;
            float duration = clip.length > 0.05f ? clip.length : 1.5f;
            _globalReadyAt = Time.time + duration + 0.65f;
            _nextLineAt = Time.time + duration + Random.Range(lineInterval.x, lineInterval.y);
        }
    }
}
