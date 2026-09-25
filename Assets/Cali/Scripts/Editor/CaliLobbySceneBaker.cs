#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Cali.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.EditorTools
{
    public static class CaliLobbySceneBaker
    {
        public const string ScenePath = "Assets/Cali/Scenes/CaliLobby.unity";
        const string HorseAPath = "Assets/Malbers Animations/Horse AnimSet Pro/4 - Prefabs/Horses/Horse Realistic.prefab";
        const string HorseBPath = "Assets/Malbers Animations/Horse AnimSet Pro/4 - Prefabs/Horses/Horse Unicorn.prefab";

        [InitializeOnLoadMethod]
        static void AutoCreateIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                if (File.Exists(ScenePath))
                {
                    EnsureBuildSettings();
                    return;
                }

                try
                {
                    Bake(log: false);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Cali Lobby] Auto-create skipped: " + e.Message);
                }
            };
        }

        [MenuItem("Cali/Create Lobby Scene")]
        public static void BakeMenu()
        {
            Bake(true);
        }

        public static void Bake(bool log)
        {
            Directory.CreateDirectory("Assets/Cali/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("LobbyLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGo = new GameObject("LobbyCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 4.2f, -9.5f), Quaternion.Euler(18f, 0f, 0f));

            var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pad.name = "LobbyPad";
            pad.transform.localScale = new Vector3(4f, 1f, 4f);

            var spawnA = new GameObject("LobbySpawn_A");
            spawnA.transform.SetPositionAndRotation(new Vector3(-3.2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var spawnB = new GameObject("LobbySpawn_B");
            spawnB.transform.SetPositionAndRotation(new Vector3(3.2f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));

            var horseA = PlaceHorse(HorseAPath, spawnA.transform, "Horse Realistic");
            var horseB = PlaceHorse(HorseBPath, spawnB.transform, "Horse Unicorn");
            if (horseB != null)
                horseB.SetActive(false);

            FantasyUiFactory.EnsureEventSystem();

            var root = new GameObject("CaliLobbyBootstrap");
            root.AddComponent<CaliLobbyBootstrap>();
            var preview = root.AddComponent<CaliLobbyHorsePreview>();
            if (horseA != null)
                preview.horseA = horseA.GetComponent<MalbersAnimations.Controller.MAnimal>();
            if (horseB != null)
                preview.horseB = horseB.GetComponent<MalbersAnimations.Controller.MAnimal>();

            var waiting = FantasyUiFactory.BuildWaitingRoomRoot();
            waiting.AddComponent<CaliWaitingRoomPanel>().Bind(waiting);

            var catalog = AssetDatabase.LoadAssetAtPath<CaliLobbyCatalog>("Assets/Cali/Resources/Lobby/CaliLobbyCatalog.asset");
            if (catalog != null)
            {
                catalog.horseAPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HorseAPath);
                catalog.horseBPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HorseBPath);
                EditorUtility.SetDirty(catalog);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildSettings();
            AssetDatabase.Refresh();

            if (log)
                Debug.Log("[Cali Lobby] Scene ready at " + ScenePath);
        }

        static GameObject PlaceHorse(string prefabPath, Transform spawn, string objectName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[Cali Lobby] Missing horse prefab: " + prefabPath);
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = objectName;
            inst.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            return inst;
        }

        static void EnsureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path.Replace('\\', '/') == ScenePath)
                    return;
            }

            int insertAt = scenes.Count;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path.Contains("MC_Demo_Day"))
                {
                    insertAt = i;
                    break;
                }
            }

            scenes.Insert(insertAt, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
