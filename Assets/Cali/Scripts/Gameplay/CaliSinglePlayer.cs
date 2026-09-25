using Cali.Network;
using Cali.UI;
using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Gameplay
{
    /// <summary>
    /// Offline one-horse mode for the Singleplayer scene: Horse Realistic only, no chain.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public class CaliSinglePlayer : MonoBehaviour
    {
        public const string SceneName = "Singleplayer";

        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
        }

        public static bool IsActiveScene =>
            Active || SceneManager.GetActiveScene().name == SceneName;

        void Awake()
        {
            Active = true;
            Apply();
        }

        void OnDestroy()
        {
            if (Active)
                Active = SceneManager.GetActiveScene().name == SceneName;
        }

        public static void Apply()
        {
            Active = true;

            var dual = FindFirstObjectByType<LocalDualHorseInput>(FindObjectsInactive.Include);
            if (dual != null)
            {
                dual.horseB = null;
                dual.allowSecondHorse = false;
                dual.enabled = true;
            }

            var starter = FindFirstObjectByType<CoopSessionStarter>(FindObjectsInactive.Include);
            if (starter != null)
                starter.enabled = false;

            StripChains();
            HideSecondHorse();

            var horse = FindPlayHorse();
            if (horse != null)
            {
                HorseBody.SetSimulated(horse, true);
                HorsePhysics.Apply(horse);
                CoopCameraFocus.FocusOnHorse(horse);
            }

            CoopSavePoint.CaptureLevelStartNow();
        }

        void Start()
        {
            CoopSavePoint.CaptureLevelStartNow();
        }

        static void StripChains()
        {
            var chains = FindObjectsByType<SoftHorseChain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain == null)
                    continue;
                chain.enabled = false;
                var line = chain.GetComponent<LineRenderer>();
                if (line != null)
                    line.enabled = false;
            }

            var meshes = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < meshes.Length; i++)
            {
                var t = meshes[i];
                if (t != null && t.name == "IronChainMesh")
                    t.gameObject.SetActive(false);
            }
        }

        static void HideSecondHorse()
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var animal = animals[i];
                if (animal == null)
                    continue;
                if (animal.gameObject.name.Contains("Unicorn")
                    || animal.gameObject.name.Contains("Pegasus"))
                    animal.gameObject.SetActive(false);
            }
        }

        static MAnimal FindPlayHorse()
        {
            var named = CoopGameController.FindPlayAnimal("Horse Realistic");
            if (named != null)
                return named;
            return FindFirstObjectByType<MAnimal>(FindObjectsInactive.Exclude);
        }
    }
}
