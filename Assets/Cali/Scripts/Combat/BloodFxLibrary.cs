using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Catalog of RVFX BloodEffectsPack splash + decal prefabs.
    /// Built by <c>Cali/Build Blood FX Library</c>; loaded from Resources at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Cali/Blood FX Library", fileName = "BloodFxLibrary")]
    public class BloodFxLibrary : ScriptableObject
    {
        public const string ResourcesPath = "Combat/BloodFxLibrary";

        [Tooltip("One-shot / gut splash prefabs (Blood_Splash_*_URP).")]
        public GameObject[] splashPrefabs;

        [Tooltip("Ground decal prefabs (BloodDecal_*_Static / SpriteSheet_URP).")]
        public GameObject[] decalPrefabs;

        static BloodFxLibrary _cached;

        public static BloodFxLibrary Get()
        {
            if (_cached != null)
                return _cached;

            _cached = Resources.Load<BloodFxLibrary>(ResourcesPath);
            return _cached;
        }

        public GameObject PickRandomSplash()
        {
            return Pick(splashPrefabs);
        }

        public GameObject PickRandomSplashExcept(GameObject except)
        {
            if (splashPrefabs == null || splashPrefabs.Length == 0)
                return null;

            if (except == null || splashPrefabs.Length == 1)
                return Pick(splashPrefabs);

            int guard = 0;
            while (guard++ < 16)
            {
                var go = splashPrefabs[Random.Range(0, splashPrefabs.Length)];
                if (go != null && go != except)
                    return go;
            }

            return Pick(splashPrefabs);
        }

        public GameObject PickRandomDecal()
        {
            return Pick(decalPrefabs);
        }

        static GameObject Pick(GameObject[] list)
        {
            if (list == null || list.Length == 0)
                return null;

            // Skip null holes if any assets failed to load.
            int guard = 0;
            while (guard++ < 16)
            {
                var go = list[Random.Range(0, list.Length)];
                if (go != null)
                    return go;
            }

            return null;
        }
    }
}
