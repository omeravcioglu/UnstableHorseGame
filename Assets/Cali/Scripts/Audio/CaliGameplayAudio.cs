using System;
using Cali.Combat;
using Cali.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Audio
{
    /// <summary>
    /// Play-scene audio. Chill/Fight music crossfade plus one-shots from CaliSoundBank.
    /// Menu music stays on CaliMusicController.
    /// </summary>
    [DefaultExecutionOrder(-160)]
    public class CaliGameplayAudio : MonoBehaviour
    {
        public static CaliGameplayAudio Instance { get; private set; }

        public CaliSoundBank bank;
        public float musicFadeSeconds = 1.1f;
        public float fightHoldSeconds = 2.5f;

        AudioSource _chill;
        AudioSource _fight;
        AudioSource _ambience;
        AudioSource[] _sfx;
        int _sfxCursor;
        float[] _nextReady;
        AudioClip[] _lastClip;
        CaliSoundGroup _chillGroup;
        CaliSoundGroup _fightGroup;
        float _mood;
        bool _wantFight;
        float _combatLostAt = -1f;
        bool _suppressed;

        public static bool IsSuppressed => Instance != null && Instance._suppressed;

        public static void SetMusicSuppressed(bool suppressed) => SetSuppressed(suppressed);

        public static void SetSuppressed(bool suppressed)
        {
            if (Instance != null)
                Instance.ApplySuppressed(suppressed);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (!IsPlayScene())
                return;
            Ensure();
        }

        public static CaliGameplayAudio Ensure()
        {
            if (!IsPlayScene())
                return Instance;

            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<CaliGameplayAudio>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("CaliGameplayAudio");
            return go.AddComponent<CaliGameplayAudio>();
        }

        public static void Play(string groupName)
        {
            var inst = Ensure();
            if (inst != null && !inst._suppressed)
                inst.PlayGroup(groupName, null);
        }

        public static void PlayAt(string groupName, Vector3 position)
        {
            var inst = Ensure();
            if (inst != null && !inst._suppressed)
                inst.PlayGroup(groupName, position);
        }

        /// <summary>Plays a one-shot at a point and keeps it going for <paramref name="seconds"/> (loops if the clip is shorter).</summary>
        public static void PlayAtFor(string groupName, Vector3 position, float seconds)
        {
            var inst = Ensure();
            if (inst != null && !inst._suppressed)
                inst.PlayGroupFor(groupName, position, seconds);
        }

        /// <summary>Plays every clip in the group at once (used for spear hit). Empty clips are skipped.</summary>
        public static void PlayAll(string groupName)
        {
            var inst = Ensure();
            if (inst != null && !inst._suppressed)
                inst.PlayGroupAll(groupName, null);
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (bank == null)
                bank = CaliSoundBank.Get();

            BuildSources();
        }

        void Start()
        {
            if (bank == null || !IsPlayScene())
                return;

            StartMoodMusic();

            for (int i = 0; i < bank.groups.Length; i++)
            {
                var g = bank.groups[i];
                if (g == null || !g.playOnStart || !g.HasClips)
                    continue;
                if (g.kind == CaliSoundKind.MusicLoop)
                    continue;
                PlayGroup(g.name, null);
            }
        }

        void Update()
        {
            UpdateCombatMood();
            ApplyMusicVolumes();
        }

        void OnDestroy()
        {
            if (_suppressed)
                AudioListener.pause = false;
            if (Instance == this)
                Instance = null;
        }

        void StartMoodMusic()
        {
            _chillGroup = ResolveChill();
            _fightGroup = bank != null ? bank.FindGroup(CaliSoundIds.Fight) : null;

            StartLoop(_chill, _chillGroup);
            StartLoop(_fight, _fightGroup);
            _mood = 0f;
            ApplyMusicVolumes();
        }

        CaliSoundGroup ResolveChill()
        {
            if (bank == null)
                return null;

            var chill = bank.FindGroup(CaliSoundIds.Chill);
            if (chill != null && chill.HasClips)
                return chill;

            return bank.FindGroup(CaliSoundIds.Background);
        }

        void UpdateCombatMood()
        {
            bool combat = EnemyCombatTargets.AnyHorseInCombat();
            if (combat)
            {
                _wantFight = true;
                _combatLostAt = -1f;
            }
            else if (_wantFight)
            {
                if (_combatLostAt < 0f)
                    _combatLostAt = Time.unscaledTime;
                if (Time.unscaledTime - _combatLostAt >= fightHoldSeconds)
                    _wantFight = false;
            }

            float target = _wantFight ? 1f : 0f;
            float step = musicFadeSeconds > 0.01f ? Time.unscaledDeltaTime / musicFadeSeconds : 1f;
            _mood = Mathf.MoveTowards(_mood, target, step);
        }

        void ApplyMusicVolumes()
        {
            float bus = CaliSettingsMenu.MusicVolume;
            if (bus < 0.01f)
                bus = 1f;
            if (_suppressed)
            {
                bus = 0f;
                if (_ambience != null)
                    _ambience.volume = 0f;
            }

            if (_chill != null)
            {
                float vol = _chillGroup != null ? _chillGroup.volume : 0.7f;
                _chill.volume = Mathf.Clamp01(vol * bus * (1f - _mood));
            }

            if (_fight != null)
            {
                float vol = _fightGroup != null ? _fightGroup.volume : 0.8f;
                _fight.volume = Mathf.Clamp01(vol * bus * _mood);
            }
        }

        void ApplySuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            AudioListener.pause = suppressed;
            if (suppressed)
                SilenceAll();
            else
                RestoreLoops();
            ApplyMusicVolumes();
        }

        void SilenceAll()
        {
            PauseLoop(_chill);
            PauseLoop(_fight);
            PauseLoop(_ambience);
            Cali.Combat.EnemyTalkVoice.StopAllTalk();
            MuteHorseSources(true);

            if (_sfx == null)
                return;

            for (int i = 0; i < _sfx.Length; i++)
            {
                if (_sfx[i] != null)
                    _sfx[i].Stop();
            }
        }

        void RestoreLoops()
        {
            if (_chill != null)
                _chill.UnPause();
            if (_fight != null)
                _fight.UnPause();
            if (_ambience != null)
                _ambience.UnPause();
            MuteHorseSources(false);
        }

        static void PauseLoop(AudioSource src)
        {
            if (src != null && src.isPlaying)
                src.Pause();
        }

        static void MuteHorseSources(bool mute)
        {
            var animals = UnityEngine.Object.FindObjectsByType<MalbersAnimations.Controller.MAnimal>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var animal = animals[i];
                if (animal == null)
                    continue;
                if (animal.GetComponentInParent<Cali.Combat.ChainKillableEnemy>() != null)
                    continue;

                var sources = animal.GetComponentsInChildren<AudioSource>(true);
                for (int s = 0; s < sources.Length; s++)
                {
                    var src = sources[s];
                    if (src == null)
                        continue;
                    src.mute = mute;
                    if (mute)
                        src.Stop();
                }
            }
        }

        void PlayGroup(string groupName, Vector3? position)
        {
            if (_suppressed || bank == null || string.IsNullOrEmpty(groupName))
                return;

            var group = bank.FindGroupOrDefault(groupName);
            var clipSource = bank.ResolveClipSource(group);
            if (group == null || clipSource == null || !clipSource.HasClips)
                return;

            int index = IndexOf(groupName);
            if (index >= 0 && group.minInterval > 0f && Time.unscaledTime < _nextReady[index])
                return;

            AudioClip avoid = index >= 0 ? _lastClip[index] : null;
            var clip = clipSource.PickRandom(avoid);
            if (clip == null)
                return;

            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            if (group.kind == CaliSoundKind.MusicLoop)
                return;
            if (group.kind == CaliSoundKind.AmbienceLoop)
                PlayLoop(_ambience, clip, group, CaliSettingsMenu.SfxVolume);
            else
                PlayOneShot(clip, group, position);

            if (index >= 0)
            {
                _lastClip[index] = clip;
                _nextReady[index] = Time.unscaledTime + Mathf.Max(0f, group.minInterval);
            }
        }

        void PlayGroupAll(string groupName, Vector3? _)
        {
            if (_suppressed || bank == null || string.IsNullOrEmpty(groupName))
                return;

            var group = bank.FindGroupOrDefault(groupName);
            var clipSource = bank.ResolveClipSource(group);
            if (group == null || clipSource == null || !clipSource.HasClips ||
                group.kind != CaliSoundKind.SfxOneShot)
                return;

            int index = IndexOf(groupName);
            if (index >= 0 && group.minInterval > 0f && Time.unscaledTime < _nextReady[index])
                return;

            AudioClip last = null;
            for (int i = 0; i < clipSource.clips.Length; i++)
            {
                var clip = clipSource.clips[i];
                if (clip == null)
                    continue;
                if (clip.loadState != AudioDataLoadState.Loaded)
                    clip.LoadAudioData();
            }

            var src = NextSfx();
            if (src == null)
                return;

            float bus = CaliSettingsMenu.SfxVolume;
            if (bus < 0.01f)
                bus = 1f;

            src.spatialBlend = 0f;
            src.pitch = group.SafePitch;
            src.volume = Mathf.Clamp01(group.volume * bus);
            src.transform.localPosition = Vector3.zero;

            for (int i = 0; i < clipSource.clips.Length; i++)
            {
                var clip = clipSource.clips[i];
                if (clip == null)
                    continue;
                src.PlayOneShot(clip, src.volume);
                last = clip;
            }

            if (index >= 0)
            {
                _lastClip[index] = last;
                _nextReady[index] = Time.unscaledTime + Mathf.Max(0f, group.minInterval);
            }
        }

        void StartLoop(AudioSource src, CaliSoundGroup group)
        {
            if (src == null || group == null || !group.HasClips)
                return;

            var clip = group.PickRandom();
            if (clip == null)
                return;

            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            src.Stop();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.Play();
        }

        void PlayLoop(AudioSource src, AudioClip clip, CaliSoundGroup group, float bus)
        {
            if (src == null)
                return;

            src.Stop();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = Mathf.Clamp01(group.volume * Mathf.Max(0.01f, bus));
            src.Play();
        }

        void PlayOneShot(AudioClip clip, CaliSoundGroup group, Vector3? position)
        {
            var src = NextSfx();
            if (src == null)
                return;

            float bus = CaliSettingsMenu.SfxVolume;
            if (bus < 0.01f)
                bus = 1f;

            src.spatialBlend = group.spatial ? 1f : 0f;
            src.minDistance = group.minDistance;
            src.maxDistance = group.maxDistance;
            src.pitch = group.SafePitch * UnityEngine.Random.Range(0.96f, 1.05f);
            src.volume = Mathf.Clamp01(group.volume * bus);

            if (group.spatial && position.HasValue)
                src.transform.position = position.Value;
            else
                src.transform.localPosition = Vector3.zero;

            src.PlayOneShot(clip, src.volume);
        }

        void PlayGroupFor(string groupName, Vector3 position, float seconds)
        {
            if (_suppressed || bank == null || string.IsNullOrEmpty(groupName))
                return;

            var group = bank.FindGroupOrDefault(groupName);
            var clipSource = bank.ResolveClipSource(group);
            if (group == null || clipSource == null || !clipSource.HasClips)
                return;

            var clip = clipSource.PickRandom();
            if (clip == null)
                return;
            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();

            float dur = Mathf.Max(0.05f, seconds);
            float bus = CaliSettingsMenu.SfxVolume;
            if (bus < 0.01f)
                bus = 1f;

            float pitch = group.SafePitch;
            var go = new GameObject("TimedSfx_" + groupName);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.clip = clip;
            // Pitch stretches the clip, so compare against how long it will actually take.
            src.loop = clip.length / pitch < dur - 0.05f;
            src.spatialBlend = group.spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = group.minDistance;
            src.maxDistance = group.maxDistance;
            src.dopplerLevel = 0f;
            src.priority = 70;
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(group.volume * bus);
            src.Play();
            Destroy(go, dur);
        }

        AudioSource NextSfx()
        {
            if (_sfx == null || _sfx.Length == 0)
                return null;

            var src = _sfx[_sfxCursor];
            _sfxCursor = (_sfxCursor + 1) % _sfx.Length;
            return src;
        }

        void BuildSources()
        {
            _chill = MakeSource("MusicChill", 0, false);
            _fight = MakeSource("MusicFight", 0, false);
            _ambience = MakeSource("Ambience", 32, false);
            _sfx = new AudioSource[8];
            for (int i = 0; i < _sfx.Length; i++)
                _sfx[i] = MakeSource("Sfx_" + i, 80, true);

            int n = bank != null && bank.groups != null ? bank.groups.Length : 0;
            _nextReady = new float[n];
            _lastClip = new AudioClip[n];
            PreloadSfxClips();
        }

        AudioSource MakeSource(string childName, int priority, bool spatial)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
            src.priority = priority;
            return src;
        }

        void PreloadSfxClips()
        {
            if (bank == null || bank.groups == null)
                return;

            for (int i = 0; i < bank.groups.Length; i++)
            {
                var group = bank.groups[i];
                if (group == null || group.kind != CaliSoundKind.SfxOneShot || group.clips == null)
                    continue;
                for (int c = 0; c < group.clips.Length; c++)
                {
                    var clip = group.clips[c];
                    if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
                        clip.LoadAudioData();
                }
            }
        }

        int IndexOf(string groupName)
        {
            if (bank == null || bank.groups == null)
                return -1;

            for (int i = 0; i < bank.groups.Length; i++)
            {
                if (bank.groups[i] != null &&
                    string.Equals(bank.groups[i].name, groupName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static bool IsPlayScene()
        {
            return CaliSceneIds.IsActive(CaliMainMenuController.GameSceneName);
        }
    }
}
