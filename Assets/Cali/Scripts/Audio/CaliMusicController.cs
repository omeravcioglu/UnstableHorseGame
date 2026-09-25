using System.Collections;
using System.Collections.Generic;
using Cali.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Audio
{
    [System.Serializable]
    public class CaliMusicTrack
    {
        public string name;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Seconds to fade in from silence.")]
        public float fadeIn = 1.5f;
        [Tooltip("Seconds to fade out to silence.")]
        public float fadeOut = 1.5f;
        [Tooltip("How long to play after fade-in starts. 0 = whole clip, or forever if Loop is on.")]
        public float playSeconds = 0f;
        public bool loop;
        public bool playOnStart = true;
    }

    /// <summary>
    /// Inspector list of music/ambience clips with per-track volume, fade, duration, and loop.
    /// </summary>
    public class CaliMusicController : MonoBehaviour
    {
        public static CaliMusicController Instance { get; private set; }

        public List<CaliMusicTrack> tracks = new List<CaliMusicTrack>();

        readonly List<AudioSource> _sources = new List<AudioSource>();
        readonly List<Coroutine> _runners = new List<Coroutine>();
        readonly List<float> _fade = new List<float>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureListener();
            EnsureSources();
        }

        void OnEnable()
        {
            EnsureListener();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (Instance != this)
                return;

            EnsureListener();
            EnsureSources();
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && tracks[i].playOnStart)
                    Play(i);
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;

            string n = scene.name;
            if (n == CaliMainMenuController.MenuSceneName || n == CaliMainMenuController.LobbySceneName)
                return;

            if (n == CaliMainMenuController.GameSceneName || n == CaliMainMenuController.SplashSceneName)
                ReleaseForGameplay();
        }

        void ReleaseForGameplay()
        {
            StopAll(true);
            float extra = 0.05f;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null)
                    extra = Mathf.Max(extra, tracks[i].fadeOut);
            }

            Destroy(gameObject, extra);
        }

        void LateUpdate()
        {
            ApplyVolumes();
        }

        public void Play(int index)
        {
            EnsureSources();
            if (!Valid(index))
            {
                Debug.LogWarning("[Cali Music] Track index " + index + " is missing. Add a clip in the Tracks list.");
                return;
            }

            if (tracks[index].clip == null)
            {
                Debug.LogWarning("[Cali Music] Track '" + tracks[index].name + "' has no clip assigned.");
                return;
            }

            StopRunner(index);
            _runners[index] = StartCoroutine(PlayRoutine(index));
        }

        public void Play(string trackName)
        {
            int i = IndexOf(trackName);
            if (i >= 0)
                Play(i);
        }

        public void Stop(int index, bool fade = true)
        {
            if (!Valid(index))
                return;
            StopRunner(index);
            if (fade && tracks[index].fadeOut > 0.01f && _sources[index].isPlaying)
                _runners[index] = StartCoroutine(FadeOutAndStop(index));
            else
                HardStop(index);
        }

        public void Stop(string trackName, bool fade = true)
        {
            int i = IndexOf(trackName);
            if (i >= 0)
                Stop(i, fade);
        }

        public void StopAll(bool fade = true)
        {
            for (int i = 0; i < tracks.Count; i++)
                Stop(i, fade);
        }

        static void EnsureListener()
        {
            AudioListener.pause = false;
            if (AudioListener.volume < 0.01f)
                AudioListener.volume = 1f;

            if (FindFirstObjectByType<AudioListener>() != null)
                return;

            var cam = Camera.main;
            if (cam != null)
                cam.gameObject.AddComponent<AudioListener>();
            else
                new GameObject("AudioListener").AddComponent<AudioListener>();
        }

        void EnsureSources()
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (i < _sources.Count && _sources[i] != null)
                    continue;

                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.spatialize = false;
                src.priority = 0;
                src.dopplerLevel = 0f;
                src.ignoreListenerPause = true;
                src.ignoreListenerVolume = true;
                src.bypassEffects = true;
                src.bypassListenerEffects = true;
                src.bypassReverbZones = true;
                src.outputAudioMixerGroup = null;

                if (i < _sources.Count)
                    _sources[i] = src;
                else
                    _sources.Add(src);

                if (i < _runners.Count)
                    _runners[i] = null;
                else
                    _runners.Add(null);

                if (i < _fade.Count)
                    _fade[i] = 0f;
                else
                    _fade.Add(0f);
            }
        }

        IEnumerator PlayRoutine(int index)
        {
            var track = tracks[index];
            var src = _sources[index];
            if (track.clip == null)
                yield break;

            if (track.clip.loadState != AudioDataLoadState.Loaded)
            {
                track.clip.LoadAudioData();
                while (track.clip.loadState == AudioDataLoadState.Loading)
                    yield return null;
            }

            if (track.clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogWarning("[Cali Music] Could not load clip '" + track.clip.name + "'.");
                yield break;
            }

            src.Stop();
            src.clip = track.clip;
            src.loop = track.loop;
            src.time = 0f;
            src.mute = false;
            src.spatialBlend = 0f;
            src.ignoreListenerVolume = true;
            _fade[index] = fadeInStart(track);
            ApplyVolume(index);
            src.Play();

            if (!src.isPlaying)
            {
                src.PlayOneShot(track.clip, Mathf.Clamp01(track.volume));
                Debug.LogWarning("[Cali Music] AudioSource.Play did not start '" + track.clip.name + "'. Tried PlayOneShot.");
            }

            float fadeIn = Mathf.Max(0f, track.fadeIn);
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.unscaledDeltaTime;
                _fade[index] = fadeIn > 0.001f ? Mathf.Clamp01(t / fadeIn) : 1f;
                ApplyVolume(index);
                yield return null;
            }

            _fade[index] = 1f;
            ApplyVolume(index);

            float playFor = track.playSeconds;
            if (playFor <= 0.01f)
            {
                if (track.loop)
                {
                    while (src.isPlaying)
                    {
                        ApplyVolume(index);
                        yield return null;
                    }

                    _runners[index] = null;
                    yield break;
                }

                playFor = Mathf.Max(0.05f, track.clip.length);
            }

            float hold = Mathf.Max(0f, playFor - fadeIn);
            float held = 0f;
            while (held < hold && src.isPlaying)
            {
                held += Time.unscaledDeltaTime;
                ApplyVolume(index);
                yield return null;
            }

            yield return FadeOutAndStop(index);
        }

        static float fadeInStart(CaliMusicTrack track) => track.fadeIn <= 0.01f ? 1f : 0f;

        IEnumerator FadeOutAndStop(int index)
        {
            var track = tracks[index];
            var src = _sources[index];
            float fadeOut = Mathf.Max(0f, track.fadeOut);
            float start = _fade[index];
            float t = 0f;
            while (t < fadeOut)
            {
                t += Time.unscaledDeltaTime;
                _fade[index] = Mathf.Lerp(start, 0f, fadeOut > 0.001f ? Mathf.Clamp01(t / fadeOut) : 1f);
                ApplyVolume(index);
                yield return null;
            }

            HardStop(index);
            _runners[index] = null;
        }

        void HardStop(int index)
        {
            _fade[index] = 0f;
            if (_sources[index] != null)
            {
                _sources[index].Stop();
                _sources[index].volume = 0f;
            }
        }

        void StopRunner(int index)
        {
            if (index < _runners.Count && _runners[index] != null)
            {
                StopCoroutine(_runners[index]);
                _runners[index] = null;
            }
        }

        void ApplyVolumes()
        {
            for (int i = 0; i < _sources.Count && i < tracks.Count; i++)
                ApplyVolume(i);
        }

        void ApplyVolume(int index)
        {
            if (index < 0 || index >= _sources.Count || _sources[index] == null)
                return;

            float music = CaliSettingsMenu.MusicVolume;
            if (music < 0.01f)
                music = 1f;

            _sources[index].volume = Mathf.Clamp01(tracks[index].volume * music * _fade[index]);
        }

        bool Valid(int index) =>
            index >= 0 && index < tracks.Count && index < _sources.Count && _sources[index] != null;

        int IndexOf(string trackName)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && tracks[i].name == trackName)
                    return i;
            }

            return -1;
        }
    }
}
