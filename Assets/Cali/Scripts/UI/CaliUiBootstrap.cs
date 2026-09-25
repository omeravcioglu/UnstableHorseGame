using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Spawns pause + settings + alter only if they are missing from the scene.
    /// Horse life HUD is not used.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class CaliUiBootstrap : MonoBehaviour
    {
        [Tooltip("Hide Fusion OnGUI host/join when Fantasy UI is active.")]
        public bool hideCoopOnGui = true;

#if UNITY_EDITOR
        [ContextMenu("Place Gameplay UI Prefabs In Scene")]
        void PlaceGameplayUiPrefabsInScene()
        {
            PlaceIfMissing<CaliPauseMenu>("UI/PauseMenuRoot");
            PlaceIfMissing<CaliSettingsMenu>("UI/SettingsRoot");
            PlaceIfMissing<CaliAlterHorsePanel>("UI/AlterHorseRoot");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        void PlaceIfMissing<T>(string resourcesPath) where T : Component
        {
            if (FindFirstObjectByType<T>(FindObjectsInactive.Include) != null)
                return;
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab == null)
            {
                Debug.LogWarning("[CaliUiBootstrap] Missing Resources prefab: " + resourcesPath);
                return;
            }

            var inst = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
            inst.name = prefab.name;
        }
#endif

        void Awake()
        {
            FantasyUiFactory.EnsureEventSystem();
            StripHorseLifeHud();

            EnsurePause();
            EnsureSettings();
            EnsureAlter();
            var alreadyOnline = Cali.Network.CoopSessionStarter.IsOnline;
            var coopState = Cali.Network.CoopGameController.Instance;
            bool skipWaitingRoom = Cali.Gameplay.CaliSinglePlayer.IsActiveScene
                || (alreadyOnline && coopState != null && !coopState.IsWaitingForMatch);

            if (!skipWaitingRoom)
                EnsureWaitingRoom();
            if (!Cali.Gameplay.CaliSinglePlayer.IsActiveScene)
                CaliIntroCutscene.Ensure();
            CaliFpsOverlay.Ensure();
            Cali.Gameplay.CaliSmoothPlay.Apply();
            Cali.Audio.CaliGameplayAudio.Ensure();
            Cali.Combat.CaliCameraShake.Ensure();
            DisableMiddleMouseSlowMo();

            if (hideCoopOnGui)
            {
                var coop = FindFirstObjectByType<Cali.Network.CoopSessionStarter>();
                if (coop != null)
                    coop.showGui = false;
            }
        }

        void Start()
        {
            ApplyPendingSession();
        }

        static void DisableMiddleMouseSlowMo()
        {
            var slow = FindObjectsByType<MalbersAnimations.SlowMotion>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < slow.Length; i++)
            {
                if (slow[i] != null)
                    slow[i].enabled = false;
            }

            var fast = FindObjectsByType<MalbersAnimations.InputSystem.MFastInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < fast.Length; i++)
            {
                var input = fast[i];
                if (input == null || input.inputs == null)
                    continue;
                for (int n = 0; n < input.inputs.Length; n++)
                {
                    if (input.inputs[n].name == "Slow Mo")
                    {
                        input.enabled = false;
                        break;
                    }
                }
            }
        }

        static void StripHorseLifeHud()
        {
            var huds = FindObjectsByType<CaliGameplayHud>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < huds.Length; i++)
            {
                if (huds[i] != null)
                    Destroy(huds[i].gameObject);
            }
        }

        void EnsurePause()
        {
            if (FindFirstObjectByType<CaliPauseMenu>(FindObjectsInactive.Include) != null)
                return;
            if (TrySpawnSeed("UI/PauseMenuRoot"))
                return;
            var pause = FantasyUiFactory.BuildPauseMenuRoot();
            var c = pause.AddComponent<CaliPauseMenu>();
            c.Bind(pause);
        }

        void EnsureSettings()
        {
            if (FindFirstObjectByType<CaliSettingsMenu>(FindObjectsInactive.Include) != null)
                return;
            if (TrySpawnSeed("UI/SettingsRoot"))
                return;
            var settings = FantasyUiFactory.BuildSettingsRoot();
            var c = settings.AddComponent<CaliSettingsMenu>();
            c.Bind(settings);
        }

        void EnsureAlter()
        {
            if (FindFirstObjectByType<CaliAlterHorsePanel>(FindObjectsInactive.Include) != null)
                return;
            if (TrySpawnSeed("UI/AlterHorseRoot"))
                return;
            var alter = FantasyUiFactory.BuildAlterHorsePanel();
            var c = alter.AddComponent<CaliAlterHorsePanel>();
            c.Bind(alter);
        }

        void EnsureWaitingRoom()
        {
            if (FindFirstObjectByType<CaliWaitingRoomPanel>(FindObjectsInactive.Include) != null)
                return;
            var waiting = FantasyUiFactory.BuildWaitingRoomRoot();
            var c = waiting.AddComponent<CaliWaitingRoomPanel>();
            c.Bind(waiting);
        }

        static bool TrySpawnSeed(string resourcesPath)
        {
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab == null)
                return false;
            Object.Instantiate(prefab);
            return true;
        }

        void ApplyPendingSession()
        {
            if (Cali.Network.CoopSessionStarter.IsOnline || Cali.Network.CoopSessionStarter.IsStarting)
                return;

            if (!CaliPendingSession.HasPending)
                return;

            var mode = CaliPendingSession.PendingMode;
            string region = CaliPendingSession.Region;
            string sessionName = CaliPendingSession.SessionName;
            string password = CaliPendingSession.Password;
            CaliPendingSession.Clear();

            var starter = Cali.Network.CoopSessionStarter.EnsurePersistent();
            if (starter == null)
            {
                Debug.LogWarning("[CaliUiBootstrap] Could not create CoopSessionStarter.");
                return;
            }

            if (mode == CaliPendingSession.Mode.Host)
                starter.StartHost(region, sessionName, password);
            else if (mode == CaliPendingSession.Mode.Client)
                starter.StartClient(region, sessionName, password);

            var dual = FindFirstObjectByType<Cali.Gameplay.LocalDualHorseInput>();
            if (dual != null)
                dual.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
