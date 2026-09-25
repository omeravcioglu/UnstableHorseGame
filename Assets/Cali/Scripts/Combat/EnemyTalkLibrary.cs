using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Catalog of enemy talk clips from Assets/EnemyTalking.
    /// Loaded from Resources at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Cali/Enemy Talk Library", fileName = "EnemyTalkLibrary")]
    public class EnemyTalkLibrary : ScriptableObject
    {
        public const string ResourcesPath = "Combat/EnemyTalkLibrary";

        public AudioClip[] clips;

        static EnemyTalkLibrary _cached;

        public static EnemyTalkLibrary Get()
        {
            if (_cached != null)
                return _cached;

            _cached = Resources.Load<EnemyTalkLibrary>(ResourcesPath);
            return _cached;
        }

        public AudioClip PickRandom(AudioClip avoid = null)
        {
            if (clips == null || clips.Length == 0)
                return null;

            AudioClip pick = null;
            int guard = 0;
            while (guard++ < 12)
            {
                var clip = clips[Random.Range(0, clips.Length)];
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
}
