using Cali.Network;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Lobby scene: start Fusion from the menu pending session, show waiting room,
    /// and preview horses (guest hidden until player 2).
    /// </summary>
    [DefaultExecutionOrder(-180)]
    public class CaliLobbyBootstrap : MonoBehaviour
    {
        void Awake()
        {
            FantasyUiFactory.EnsureEventSystem();
            BuildPadIfMissing();
            EnsureHorses();
            EnsureWaitingRoom();
            CoopSessionStarter.EnsurePersistent();
        }

        void Start()
        {
            StartPendingSession();
        }

        void StartPendingSession()
        {
            var starter = CoopSessionStarter.EnsurePersistent();
            starter.showGui = false;

            if (CoopSessionStarter.IsOnline || CoopSessionStarter.IsStarting)
                return;

            if (!CaliPendingSession.HasPending)
            {
                Debug.LogWarning("[CaliLobby] No pending Host/Join. Returning to menu.");
                CaliScreenFader.LoadScene(CaliMainMenuController.MenuSceneName);
                return;
            }

            var mode = CaliPendingSession.PendingMode;
            string region = CaliPendingSession.Region;
            string sessionName = CaliPendingSession.SessionName;
            string password = CaliPendingSession.Password;
            CaliPendingSession.Clear();

            if (mode == CaliPendingSession.Mode.Host)
                starter.StartHost(region, sessionName, password);
            else if (mode == CaliPendingSession.Mode.Client)
                starter.StartClient(region, sessionName, password);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void EnsureWaitingRoom()
        {
            if (FindFirstObjectByType<CaliWaitingRoomPanel>(FindObjectsInactive.Include) != null)
                return;

            var waiting = FantasyUiFactory.BuildWaitingRoomRoot();
            var c = waiting.AddComponent<CaliWaitingRoomPanel>();
            c.Bind(waiting);
        }

        void BuildPadIfMissing()
        {
            if (GameObject.Find("LobbyPad") == null)
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
                pad.name = "LobbyPad";
                pad.transform.position = Vector3.zero;
                pad.transform.localScale = new Vector3(4f, 1f, 4f);
                var rend = pad.GetComponent<Renderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null && rend != null)
                {
                    var mat = new Material(shader);
                    var color = new Color(0.22f, 0.2f, 0.17f);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", color);
                    mat.color = color;
                    rend.sharedMaterial = mat;
                }
            }

            if (GameObject.Find("LobbySpawn_A") == null)
            {
                var a = new GameObject("LobbySpawn_A");
                a.transform.SetPositionAndRotation(new Vector3(-3.2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            }

            if (GameObject.Find("LobbySpawn_B") == null)
            {
                var b = new GameObject("LobbySpawn_B");
                b.transform.SetPositionAndRotation(new Vector3(3.2f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            }

            if (Camera.main == null && FindFirstObjectByType<Camera>() == null)
            {
                var camGo = new GameObject("LobbyCamera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                camGo.transform.SetPositionAndRotation(new Vector3(0f, 4.2f, -9.5f), Quaternion.Euler(18f, 0f, 0f));
            }

            if (FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("LobbyLight", typeof(Light));
                var light = lightGo.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        void EnsureHorses()
        {
            var preview = FindFirstObjectByType<CaliLobbyHorsePreview>(FindObjectsInactive.Include);
            if (preview == null)
                preview = gameObject.AddComponent<CaliLobbyHorsePreview>();

            if (preview.horseA == null)
                preview.horseA = SpawnHorse("Horse Realistic", true);
            if (preview.horseB == null)
                preview.horseB = SpawnHorse("Horse Unicorn", false);

            if (preview.horseB != null)
                preview.horseB.gameObject.SetActive(false);
        }

        static MAnimal SpawnHorse(string objectName, bool isHostHorse)
        {
            var existing = FindNamed(objectName);
            if (existing != null)
            {
                PlaceAtSpawn(existing.transform, isHostHorse);
                DisableMalbersAi(existing.gameObject);
                return existing;
            }

            var catalog = CaliLobbyCatalog.Get();
            var prefab = isHostHorse ? catalog?.horseAPrefab : catalog?.horseBPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[CaliLobby] Missing horse prefab for " + objectName);
                return null;
            }

            var spawn = GameObject.Find(isHostHorse ? "LobbySpawn_A" : "LobbySpawn_B");
            var inst = Instantiate(prefab, spawn != null ? spawn.transform.position : Vector3.zero,
                spawn != null ? spawn.transform.rotation : Quaternion.identity);
            inst.name = objectName;
            DisableMalbersAi(inst);
            PlaceAtSpawn(inst.transform, isHostHorse);
            return inst.GetComponent<MAnimal>() ?? inst.GetComponentInChildren<MAnimal>();
        }

        static void PlaceAtSpawn(Transform horse, bool isHostHorse)
        {
            var spawn = GameObject.Find(isHostHorse ? "LobbySpawn_A" : "LobbySpawn_B");
            if (spawn == null || horse == null)
                return;
            horse.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
        }

        static MAnimal FindNamed(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name == objectName)
                    return animals[i];
            }

            return null;
        }

        static void DisableMalbersAi(GameObject root)
        {
            if (root == null)
                return;

            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null)
                    continue;
                string typeName = b.GetType().Name;
                if (typeName.Contains("AnimalAI") ||
                    typeName.Contains("AIControl") ||
                    typeName.Contains("AIBrain") ||
                    typeName.Contains("MAnimalAI"))
                {
                    b.enabled = false;
                }
            }
        }
    }
}
