using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cali.Audio
{
    public static class CaliSoundIds
    {
        public const string Background = "Background";
        public const string Chill = "Chill";
        public const string Fight = "Fight";
        public const string Environment = "Environment";
        public const string BloodSplash = "Blood Splash";
        public const string ChainRandom = "Chain Random";
        public const string ChainHit = "Chain Hit";
        public const string Horse = "Horse";
        public const string HorseHit = "Horse Hit";
        public const string XpOrb = "Xp Orb";
        public const string Extra = "Extra";
        public const string DoorOpen = "Door Open";
        public const string DoorClose = "Door Close";

        /// <summary>The stall gate the latch mini-game opens. Deliberately not the generic door sound.</summary>
        public const string LatchDoor = "Latch Door";
    }

    public enum CaliSoundKind
    {
        MusicLoop = 0,
        AmbienceLoop = 1,
        SfxOneShot = 2,
    }

    [Serializable]
    public class CaliSoundGroup
    {
        public string name = "New";
        public CaliSoundKind kind = CaliSoundKind.SfxOneShot;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Minimum seconds between plays of this group.")]
        public float minInterval;
        public bool playOnStart;
        public bool spatial = true;
        public float minDistance = 4f;
        public float maxDistance = 40f;
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [Tooltip("Playback speed. Below 1 is deeper and heavier, above 1 is lighter and faster.")]
        [Range(0.25f, 2f)] public float pitch = 1f;

        [Tooltip("Borrow this group's clips while this one has none of its own. Empty means stay silent.")]
        public string clipFallback = string.Empty;

        /// <summary>Banks saved before <see cref="pitch"/> existed read back as 0, which would mute playback.</summary>
        public float SafePitch => pitch > 0.01f ? pitch : 1f;

        public bool HasClips
        {
            get
            {
                if (clips == null)
                    return false;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null)
                        return true;
                }

                return false;
            }
        }

        public AudioClip PickRandom(AudioClip avoid = null)
        {
            if (clips == null || clips.Length == 0)
                return null;

            AudioClip pick = null;
            int guard = 0;
            while (guard++ < 12)
            {
                var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
                if (clip == null)
                    continue;
                if (clip == avoid && clips.Length > 1)
                    continue;
                pick = clip;
                break;
            }

            return pick;
        }
    }

    /// <summary>
    /// Gameplay sound catalog edited by Cali → Sound Editor.
    /// Menu music stays on CaliMusicController.
    /// </summary>
    [CreateAssetMenu(menuName = "Cali/Sound Bank", fileName = "CaliSoundBank")]
    public class CaliSoundBank : ScriptableObject
    {
        public const string ResourcesPath = "Audio/CaliSoundBank";
        public const string AssetPath = "Assets/Cali/Resources/Audio/CaliSoundBank.asset";

        public CaliSoundGroup[] groups = Array.Empty<CaliSoundGroup>();

        static CaliSoundBank _cached;

        public static CaliSoundBank Get()
        {
            if (_cached != null)
                return _cached;

            _cached = Resources.Load<CaliSoundBank>(ResourcesPath);
            return _cached;
        }

        public CaliSoundGroup FindGroup(string groupName)
        {
            if (groups == null || string.IsNullOrEmpty(groupName))
                return null;

            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null &&
                    string.Equals(groups[i].name, groupName, StringComparison.OrdinalIgnoreCase))
                    return groups[i];
            }

            return null;
        }

        public static CaliSoundGroup[] CreateDefaultGroups()
        {
            return new[]
            {
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Chill,
                    kind = CaliSoundKind.MusicLoop,
                    volume = 0.7f,
                    playOnStart = true,
                    spatial = false,
                    minInterval = 0f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Fight,
                    kind = CaliSoundKind.MusicLoop,
                    volume = 0.8f,
                    playOnStart = false,
                    spatial = false,
                    minInterval = 0f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Background,
                    kind = CaliSoundKind.MusicLoop,
                    volume = 0.7f,
                    playOnStart = false,
                    spatial = false,
                    minInterval = 0f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Environment,
                    kind = CaliSoundKind.AmbienceLoop,
                    volume = 0.55f,
                    playOnStart = true,
                    spatial = false,
                    minInterval = 0f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.BloodSplash,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.9f,
                    spatial = true,
                    minInterval = 0.05f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.ChainRandom,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.75f,
                    spatial = true,
                    minInterval = 0.55f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.ChainHit,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 1f,
                    spatial = true,
                    minInterval = 0.08f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Horse,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.8f,
                    spatial = true,
                    minInterval = 0.2f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.HorseHit,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.95f,
                    spatial = false,
                    minInterval = 0.12f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.XpOrb,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.85f,
                    spatial = false,
                    minInterval = 0.04f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.Extra,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 1f,
                    spatial = true
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.DoorOpen,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.9f,
                    spatial = true,
                    minInterval = 0.15f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.DoorClose,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 0.9f,
                    spatial = true,
                    minInterval = 0.15f
                },
                new CaliSoundGroup
                {
                    name = CaliSoundIds.LatchDoor,
                    kind = CaliSoundKind.SfxOneShot,
                    volume = 1f,
                    spatial = true,
                    minInterval = 0.15f,
                    // Heavier and slower than the other doors, and audibly different even
                    // while it is still borrowing the generic door clip.
                    pitch = 0.68f,
                    clipFallback = CaliSoundIds.DoorOpen
                },
            };
        }

        static CaliSoundGroup[] _defaults;

        /// <summary>
        /// Like <see cref="FindGroup"/>, but falls back to the built-in definition for ids added
        /// after this bank was last saved. Without this, a new id plays nothing until someone
        /// opens the Sound Editor. Clips then come from the definition's own fallback group.
        /// </summary>
        public CaliSoundGroup FindGroupOrDefault(string groupName)
        {
            var group = FindGroup(groupName);
            if (group != null || string.IsNullOrEmpty(groupName))
                return group;

            _defaults ??= CreateDefaultGroups();
            for (int i = 0; i < _defaults.Length; i++)
            {
                if (string.Equals(_defaults[i].name, groupName, StringComparison.OrdinalIgnoreCase))
                    return _defaults[i];
            }

            return null;
        }

        /// <summary>
        /// The group that supplies clips for <paramref name="group"/>: itself when it has any,
        /// otherwise its declared fallback. Lets a specific group keep its own volume and pitch
        /// while inheriting a general sound until a dedicated clip is assigned.
        /// </summary>
        public CaliSoundGroup ResolveClipSource(CaliSoundGroup group)
        {
            if (group == null || group.HasClips || string.IsNullOrEmpty(group.clipFallback))
                return group;

            var fallback = FindGroup(group.clipFallback);
            return fallback != null && fallback.HasClips ? fallback : group;
        }

        /// <summary>
        /// Appends default groups this asset does not have yet, leaving existing groups and
        /// their assigned clips untouched. Returns true when the list changed.
        /// </summary>
        public bool EnsureDefaultGroups()
        {
            var list = new List<CaliSoundGroup>();
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i] != null)
                        list.Add(groups[i]);
                }
            }

            bool changed = list.Count != (groups == null ? 0 : groups.Length);
            var defaults = CreateDefaultGroups();
            for (int i = 0; i < defaults.Length; i++)
            {
                if (Contains(list, defaults[i].name))
                    continue;

                list.Add(defaults[i]);
                changed = true;
            }

            if (changed)
                groups = list.ToArray();

            return changed;
        }

        static bool Contains(List<CaliSoundGroup> list, string groupName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].name, groupName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
