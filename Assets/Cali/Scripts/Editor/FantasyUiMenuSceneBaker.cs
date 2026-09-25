#if UNITY_EDITOR
using Cali.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cali.UI.Editor
{
    /// <summary>
    /// Bakes MenuFlow + Host/Join/Settings into CaliMainMenu so they are editable in Edit mode.
    /// </summary>
    public static class FantasyUiMenuSceneBaker
    {
        const string MenuScenePath = "Assets/Cali/Scenes/CaliMainMenu.unity";

        [InitializeOnLoadMethod]
        static void AutoBakeWhenMenuSceneOpen()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                TryAutoBakeActiveMenuScene();
            };
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                EditorApplication.delayCall += TryAutoBakeActiveMenuScene;
            };
        }

        static void TryAutoBakeActiveMenuScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || !scene.path.Replace('\\', '/').EndsWith("CaliMainMenu.unity"))
                return;
            if (GameObject.Find("<<<MenuFlow>>>") != null &&
                Object.FindFirstObjectByType<CaliHostSetupPanel>(FindObjectsInactive.Include) != null &&
                Object.FindFirstObjectByType<CaliJoinBrowserPanel>(FindObjectsInactive.Include) != null)
                return;

            Bake(log: true);
        }

        [MenuItem("Cali/UI/Bake Cinematic Menu Into Scene")]
        public static void BakeMenuIntoScene()
        {
            Bake(true);
        }

        /// <summary>Batch: Unity -batchmode -executeMethod Cali.UI.Editor.FantasyUiMenuSceneBaker.BakeBatch</summary>
        public static void BakeBatch()
        {
            Bake(false);
            EditorApplication.Exit(0);
        }

        public static void Bake(bool log)
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError($"[Cali UI] Missing scene: {MenuScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

            FantasyUiFactory.EnsureEventSystem();

            EnsurePanelInScene<CaliMainMenuController>(
                "MainMenuRoot",
                "UI/MainMenuRoot",
                () =>
                {
                    var go = FantasyUiFactory.BuildMainMenuRoot();
                    go.AddComponent<CaliMainMenuController>();
                    return go;
                },
                startActive: true);

            EnsurePanelInScene<CaliSettingsMenu>(
                "SettingsRoot",
                "UI/SettingsRoot",
                () =>
                {
                    var go = FantasyUiFactory.BuildSettingsRoot();
                    var c = go.AddComponent<CaliSettingsMenu>();
                    c.Bind(go);
                    return go;
                },
                startActive: true);

            EnsurePanelInScene<CaliHostSetupPanel>(
                "HostSetupRoot",
                "UI/HostSetupRoot",
                () =>
                {
                    var go = FantasyUiFactory.BuildHostSetupPanel();
                    var c = go.AddComponent<CaliHostSetupPanel>();
                    c.Bind(go);
                    return go;
                },
                startActive: true);

            EnsurePanelInScene<CaliJoinBrowserPanel>(
                "JoinBrowserRoot",
                "UI/JoinBrowserRoot",
                () =>
                {
                    var go = FantasyUiFactory.BuildJoinBrowserPanel();
                    var c = go.AddComponent<CaliJoinBrowserPanel>();
                    c.Bind(go);
                    return go;
                },
                startActive: true);

            var flow = EnsureMenuFlow(out var rig);

            // Do NOT auto-move panels to mounts — user places menus and cameras independently.
            // Only wire world-space camera reference.
            WireWorldCameraOnly(rig);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (log)
            {
                Debug.Log("[Cali UI] MenuFlow + Host/Join ready in CaliMainMenu.\n" +
                          "Place MainMenuRoot / HostSetupRoot / JoinBrowserRoot / SettingsRoot anywhere.\n" +
                          "Aim CamAnchor_Main / Host / Join / Settings separately (camera only).\n" +
                          "Assign intro AnimationClip on CaliMenuFlowDirector (<<<MenuFlow>>>).");
                Selection.activeGameObject = flow;
            }
        }

        static void WireWorldCameraOnly(CaliMenuCameraRig rig)
        {
            var cam = rig != null ? rig.menuCamera : Camera.main;
            ConfigureCanvas(GameObject.Find("MainMenuRoot"), cam);
            ConfigureCanvas(GameObject.Find("HostSetupRoot"), cam);
            ConfigureCanvas(GameObject.Find("JoinBrowserRoot"), cam);
            ConfigureCanvas(GameObject.Find("SettingsRoot"), cam);
        }

        static void ConfigureCanvas(GameObject root, Camera cam)
        {
            if (root == null)
                return;
            var canvas = root.GetComponent<Canvas>() ?? root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                return;
            var tx = canvas.transform;
            Vector3 pos = tx.position;
            Quaternion rot = tx.rotation;
            Vector3 scale = tx.localScale;
            if (canvas.renderMode != RenderMode.WorldSpace)
                canvas.renderMode = RenderMode.WorldSpace;
            if (cam != null)
                canvas.worldCamera = cam;
            tx.SetPositionAndRotation(pos, rot);
            if (scale.sqrMagnitude > 0.0000001f)
                tx.localScale = scale;
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null)
                scaler.enabled = false;

            var content = root.transform.Find("Content");
            if (content != null)
                content.gameObject.SetActive(true);
            var blocker = FantasyUiFactory.FindDeepChild(root.transform, "Blocker");
            if (blocker != null)
                blocker.gameObject.SetActive(false);

            root.SetActive(true);
            EditorUtility.SetDirty(root);
        }

        static GameObject EnsureMenuFlow(out CaliMenuCameraRig rig)
        {
            var flow = GameObject.Find("<<<MenuFlow>>>");
            if (flow == null)
                flow = new GameObject("<<<MenuFlow>>>");

            rig = flow.GetComponent<CaliMenuCameraRig>();
            if (rig == null)
                rig = flow.AddComponent<CaliMenuCameraRig>();

            if (flow.GetComponent<CaliMenuFlowDirector>() == null)
                flow.AddComponent<CaliMenuFlowDirector>();

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = GameObject.Find("Main Camera");
                if (camGo != null)
                    cam = camGo.GetComponent<Camera>();
            }

            rig.menuCamera = cam;

            rig.anchorMain = EnsureChild(flow.transform, "CamAnchor_Main", new Vector3(0f, 1.6f, -6f), Quaternion.Euler(8f, 0f, 0f));
            rig.anchorHost = EnsureChild(flow.transform, "CamAnchor_Host", new Vector3(-4.5f, 1.8f, -3.5f), Quaternion.Euler(6f, 35f, 0f));
            rig.anchorJoin = EnsureChild(flow.transform, "CamAnchor_Join", new Vector3(4.5f, 1.8f, -3.5f), Quaternion.Euler(6f, -35f, 0f));
            rig.anchorSettings = EnsureChild(flow.transform, "CamAnchor_Settings", new Vector3(0f, 2.2f, -4f), Quaternion.Euler(12f, 0f, 0f));

            // Optional empty markers only — not linked to panels.
            EnsureChild(flow.transform, "UiMount_Main", new Vector3(0f, 1.5f, -1.5f), Quaternion.identity);
            EnsureChild(flow.transform, "UiMount_Host", new Vector3(-2f, 1.5f, -1f), Quaternion.identity);
            EnsureChild(flow.transform, "UiMount_Join", new Vector3(2f, 1.5f, -1f), Quaternion.identity);
            EnsureChild(flow.transform, "UiMount_Settings", new Vector3(0f, 1.8f, -1.2f), Quaternion.identity);

            if (cam != null && rig.anchorMain != null)
                cam.transform.SetPositionAndRotation(rig.anchorMain.position, rig.anchorMain.rotation);

            EditorUtility.SetDirty(rig);
            return flow;
        }

        static void EnsurePanelInScene<T>(
            string objectName,
            string resourcesPath,
            System.Func<GameObject> factory,
            bool startActive) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.gameObject.SetActive(startActive);
                existing.gameObject.name = objectName;
                return;
            }

            GameObject go;
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (go.GetComponent<T>() == null)
                {
                    if (typeof(T) == typeof(CaliHostSetupPanel))
                        go.AddComponent<CaliHostSetupPanel>().Bind(go);
                    else if (typeof(T) == typeof(CaliJoinBrowserPanel))
                        go.AddComponent<CaliJoinBrowserPanel>().Bind(go);
                    else if (typeof(T) == typeof(CaliSettingsMenu))
                        go.AddComponent<CaliSettingsMenu>().Bind(go);
                    else if (typeof(T) == typeof(CaliMainMenuController))
                        go.AddComponent<CaliMainMenuController>();
                }
                else
                {
                    if (go.GetComponent<CaliHostSetupPanel>() is CaliHostSetupPanel host)
                        host.Bind(go);
                    if (go.GetComponent<CaliJoinBrowserPanel>() is CaliJoinBrowserPanel join)
                        join.Bind(go);
                    if (go.GetComponent<CaliSettingsMenu>() is CaliSettingsMenu settings)
                        settings.Bind(go);
                }
            }
            else
            {
                go = factory();
            }

            go.name = objectName;
            go.SetActive(startActive);
            go.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(go, "Bake " + objectName);
        }

        static Transform EnsureChild(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }
    }
}
#endif
