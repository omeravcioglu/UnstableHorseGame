#if UNITY_EDITOR
using System.IO;
using Cali.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cali.UI.Editor
{
    /// <summary>
    /// Builds Cali-owned Fantasy UI prefabs, main menu scene, build settings, and demo bootstrap.
    /// </summary>
    public static class FantasyUiPrefabBuilder
    {
        const string PrefabFolder = "Assets/Cali/UI/Prefabs";
        const string MenuScenePath = "Assets/Cali/Scenes/CaliMainMenu.unity";
        const string DemoScenePath = "Assets/The_Modular_Medieval_Castle/Scenes/MC_Demo_Day.unity";

        [MenuItem("Cali/UI/Build Fantasy UI Prefabs")]
        public static void BuildPrefabsMenu()
        {
            BuildAllPrefabs(true);
        }

        [MenuItem("Cali/UI/Setup Main Menu + Build Settings + Demo Bootstrap")]
        public static void SetupAllMenu()
        {
            BuildAllPrefabs(false);
            CreateMainMenuScene(true);
            EnsureBuildSettings();
            EnsureDemoBootstrap(true);
            AssetDatabase.SaveAssets();
            Debug.Log("[Cali UI] Fantasy RPG UI setup complete.");
        }

        /// <summary>Batch entry: Unity.exe -batchmode -quit -executeMethod Cali.UI.Editor.FantasyUiPrefabBuilder.SetupAllBatch</summary>
        public static void SetupAllBatch()
        {
            SetupAllMenu();
        }

        [InitializeOnLoadMethod]
        static void AutoEnsure()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                try
                {
                    if (!Directory.Exists(PrefabFolder) || !File.Exists(Path.Combine(Application.dataPath, "Cali/UI/Prefabs/GameplayHudRoot.prefab")))
                        BuildAllPrefabs(false);
                    if (!File.Exists(Path.Combine(Application.dataPath, "Cali/Scenes/CaliMainMenu.unity")))
                        CreateMainMenuScene(false);
                    EnsureBuildSettings();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Cali UI] Auto-setup skipped: {e.Message}");
                }
            };
        }

        public static void BuildAllPrefabs(bool log)
        {
            EnsureFolders();

            SaveRootPrefab("MainMenuRoot", () =>
            {
                var go = FantasyUiFactory.BuildMainMenuRoot();
                go.AddComponent<CaliMainMenuController>();
                return go;
            });

            SaveRootPrefab("PauseMenuRoot", () =>
            {
                var go = FantasyUiFactory.BuildPauseMenuRoot();
                var c = go.AddComponent<CaliPauseMenu>();
                c.Bind(go);
                return go;
            });

            SaveRootPrefab("SettingsRoot", () =>
            {
                var go = FantasyUiFactory.BuildSettingsRoot();
                var c = go.AddComponent<CaliSettingsMenu>();
                c.Bind(go);
                return go;
            });

            SaveRootPrefab("GameplayHudRoot", () =>
            {
                var go = FantasyUiFactory.BuildGameplayHudRoot();
                var c = go.AddComponent<CaliGameplayHud>();
                c.Bind(go);
                return go;
            });

            SaveRootPrefab("AlterHorseRoot", () =>
            {
                var go = FantasyUiFactory.BuildAlterHorsePanel();
                var c = go.AddComponent<CaliAlterHorsePanel>();
                c.Bind(go);
                return go;
            });

            SaveRootPrefab("HostSetupRoot", () =>
            {
                var go = FantasyUiFactory.BuildHostSetupPanel();
                var c = go.AddComponent<CaliHostSetupPanel>();
                c.Bind(go);
                return go;
            });

            SaveRootPrefab("JoinBrowserRoot", () =>
            {
                var go = FantasyUiFactory.BuildJoinBrowserPanel();
                var c = go.AddComponent<CaliJoinBrowserPanel>();
                c.Bind(go);
                return go;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (log)
                Debug.Log($"[Cali UI] Prefabs saved under {PrefabFolder}");
        }

        static void SaveRootPrefab(string name, System.Func<GameObject> build)
        {
            var path = $"{PrefabFolder}/{name}.prefab";
            var go = build();
            // CanvasScaler can bake scale 0 when saving without a Game view — never persist that.
            go.transform.localScale = Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(go, path);

            if (!AssetDatabase.IsValidFolder("Assets/Cali/Resources"))
                AssetDatabase.CreateFolder("Assets/Cali", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Cali/Resources/UI"))
                AssetDatabase.CreateFolder("Assets/Cali/Resources", "UI");
            PrefabUtility.SaveAsPrefabAsset(go, $"Assets/Cali/Resources/UI/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Cali"))
                AssetDatabase.CreateFolder("Assets", "Cali");
            if (!AssetDatabase.IsValidFolder("Assets/Cali/UI"))
                AssetDatabase.CreateFolder("Assets/Cali", "UI");
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Cali/UI", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Cali/Scenes"))
                AssetDatabase.CreateFolder("Assets/Cali", "Scenes");
        }

        public static void CreateMainMenuScene(bool log)
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.08f, 1f);
            cam.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();

            // URP camera data if available
            var urpCamType = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCamType != null && camGo.GetComponent(urpCamType) == null)
                camGo.AddComponent(urpCamType);

            FantasyUiFactory.EnsureEventSystem();

            var menu = FantasyUiFactory.BuildMainMenuRoot();
            menu.AddComponent<CaliMainMenuController>();

            var settings = FantasyUiFactory.BuildSettingsRoot();
            var settingsCtrl = settings.AddComponent<CaliSettingsMenu>();
            settingsCtrl.Bind(settings);

            var host = FantasyUiFactory.BuildHostSetupPanel();
            var hostCtrl = host.AddComponent<CaliHostSetupPanel>();
            hostCtrl.Bind(host);

            var join = FantasyUiFactory.BuildJoinBrowserPanel();
            var joinCtrl = join.AddComponent<CaliJoinBrowserPanel>();
            joinCtrl.Bind(join);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            if (log)
                Debug.Log($"[Cali UI] Saved main menu scene: {MenuScenePath}");
        }

        public static void EnsureBuildSettings()
        {
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);
            var demo = AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath);
            if (menu == null || demo == null)
            {
                Debug.LogWarning("[Cali UI] Could not set Build Settings — missing scenes.");
                return;
            }

            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(DemoScenePath, true),
            };
            EditorBuildSettings.scenes = scenes;
        }

        public static void EnsureDemoBootstrap(bool log)
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../" + DemoScenePath)) &&
                !File.Exists(DemoScenePath.Replace("Assets/", Application.dataPath + "/")))
            {
                // Path check via AssetDatabase
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                Debug.LogWarning($"[Cali UI] Demo scene missing: {DemoScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var existing = Object.FindFirstObjectByType<CaliUiBootstrap>();
            if (existing == null)
            {
                var go = new GameObject("<<<Cali UI Bootstrap>>>");
                go.AddComponent<CaliUiBootstrap>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (log)
                    Debug.Log("[Cali UI] Added CaliUiBootstrap to MC_Demo_Day.");
            }
            else if (log)
            {
                Debug.Log("[Cali UI] CaliUiBootstrap already present on MC_Demo_Day.");
            }
        }
    }
}
#endif
