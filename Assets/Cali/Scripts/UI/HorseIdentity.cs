using System;
using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Display name + portrait for a horse slot. Persists lightly via PlayerPrefs.
    /// </summary>
    public class HorseIdentity : MonoBehaviour
    {
        public const string KeyA = "HorseA";
        public const string KeyB = "HorseB";

        [Tooltip("Stable key used for PlayerPrefs (HorseA / HorseB).")]
        public string horseKey = KeyA;

        public string displayName = "Horse";
        public Sprite portrait;

        public event Action<HorseIdentity> Changed;

        void Awake()
        {
            Load();
        }

        public void Apply(string name, Sprite newPortrait, bool save = true)
        {
            if (!string.IsNullOrWhiteSpace(name))
                displayName = name.Trim();
            if (newPortrait != null)
                portrait = newPortrait;
            if (save)
                Save();
            Changed?.Invoke(this);
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(horseKey))
                return;
            PlayerPrefs.SetString(PrefsNameKey(horseKey), displayName ?? "");
            if (portrait != null)
                PlayerPrefs.SetString(PrefsPortraitKey(horseKey), portrait.name);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            if (string.IsNullOrEmpty(horseKey))
                return;

            string savedName = PlayerPrefs.GetString(PrefsNameKey(horseKey), "");
            if (!string.IsNullOrEmpty(savedName))
                displayName = savedName;
            else if (string.IsNullOrEmpty(displayName) || displayName == "Horse")
                displayName = DefaultNameForKey(horseKey);

            string portraitName = PlayerPrefs.GetString(PrefsPortraitKey(horseKey), "");
            if (!string.IsNullOrEmpty(portraitName))
            {
                var loaded = FantasyUiFactory.LoadPortrait(portraitName);
                if (loaded != null)
                    portrait = loaded;
            }
            else if (portrait == null)
            {
                portrait = FantasyUiFactory.LoadPortrait(horseKey == KeyB ? "hero_03" : "hero_02");
            }

            Changed?.Invoke(this);
        }

        public static string PrefsNameKey(string key) => $"Cali.Horse.{key}.Name";
        public static string PrefsPortraitKey(string key) => $"Cali.Horse.{key}.Portrait";

        public static string DefaultNameForKey(string key) =>
            key == KeyB ? "Starfall" : "Ashmane";
    }
}
