using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Builds / wires the main menu when CaliMainMenu starts.
    /// Prefers scene-baked objects (editable in Edit mode); only spawns missing pieces.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CaliMainMenuBootstrap : MonoBehaviour
    {
        void Awake()
        {
            DisableLodsAndExtraCulling();
            FantasyUiFactory.EnsureEventSystem();

            EnsureMainMenu();
            EnsureOverlay<CaliSettingsMenu>("UI/SettingsRoot", () =>
            {
                var go = FantasyUiFactory.BuildSettingsRoot();
                go.AddComponent<CaliSettingsMenu>().Bind(go);
                return go;
            });
            EnsureOverlay<CaliHostSetupPanel>("UI/HostSetupRoot", () =>
            {
                var go = FantasyUiFactory.BuildHostSetupPanel();
                go.AddComponent<CaliHostSetupPanel>().Bind(go);
                return go;
            });
            EnsureOverlay<CaliJoinBrowserPanel>("UI/JoinBrowserRoot", () =>
            {
                var go = FantasyUiFactory.BuildJoinBrowserPanel();
                go.AddComponent<CaliJoinBrowserPanel>().Bind(go);
                return go;
            });

            // Hide overlays until the director opens them (scene keeps them for Edit mode).
            SetInactiveAll<CaliSettingsMenu>();
            SetInactiveAll<CaliHostSetupPanel>();
            SetInactiveAll<CaliJoinBrowserPanel>();

            EnsureCinematicMenuFlow();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static void DisableLodsAndExtraCulling()
        {
            var lods = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lods.Length; i++)
            {
                var lod = lods[i];
                if (lod == null)
                    continue;
                lod.ForceLOD(0);
                lod.enabled = false;
            }

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                if (cam == null)
                    continue;
                cam.useOcclusionCulling = false;
            }
        }

        static void EnsureCinematicMenuFlow()
        {
            var menu = Object.FindFirstObjectByType<CaliMainMenuController>(FindObjectsInactive.Include);
            var host = Object.FindFirstObjectByType<CaliHostSetupPanel>(FindObjectsInactive.Include);
            var join = Object.FindFirstObjectByType<CaliJoinBrowserPanel>(FindObjectsInactive.Include);
            var settings = Object.FindFirstObjectByType<CaliSettingsMenu>(FindObjectsInactive.Include);

            var flowRoot = GameObject.Find("<<<MenuFlow>>>");
            if (flowRoot == null)
                flowRoot = new GameObject("<<<MenuFlow>>>");

            var rig = flowRoot.GetComponent<CaliMenuCameraRig>();
            if (rig == null)
                rig = flowRoot.AddComponent<CaliMenuCameraRig>();

            EnsureCameraAndAnchors(rig, flowRoot.transform);

            var director = flowRoot.GetComponent<CaliMenuFlowDirector>();
            if (director == null)
                director = flowRoot.AddComponent<CaliMenuFlowDirector>();

            GameObject mainRoot = menu != null ? menu.gameObject : null;
            director.Initialize(mainRoot, host, join, settings, rig);
        }

        static void EnsureCameraAndAnchors(CaliMenuCameraRig rig, Transform flow)
        {
            if (rig.menuCamera == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    var camGo = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
                    cam = camGo.GetComponent<Camera>();
                    cam.tag = "MainCamera";
                    cam.clearFlags = CameraClearFlags.Skybox;
                    cam.fieldOfView = 50f;
                }

                rig.menuCamera = cam;
            }

            // Only create missing anchors — never overwrite poses you set in Edit mode.
            if (rig.anchorMain == null)
                rig.anchorMain = EnsureAnchor(flow, "CamAnchor_Main", new Vector3(0f, 1.6f, -6f), Quaternion.Euler(8f, 0f, 0f));
            if (rig.anchorHost == null)
                rig.anchorHost = EnsureAnchor(flow, "CamAnchor_Host", new Vector3(-4.5f, 1.8f, -3.5f), Quaternion.Euler(6f, 35f, 0f));
            if (rig.anchorJoin == null)
                rig.anchorJoin = EnsureAnchor(flow, "CamAnchor_Join", new Vector3(4.5f, 1.8f, -3.5f), Quaternion.Euler(6f, -35f, 0f));
            if (rig.anchorSettings == null)
                rig.anchorSettings = EnsureAnchor(flow, "CamAnchor_Settings", new Vector3(0f, 2.2f, -4f), Quaternion.Euler(12f, 0f, 0f));

            // Resolve references if children exist but fields were cleared.
            if (rig.anchorMain == null) rig.anchorMain = flow.Find("CamAnchor_Main");
            if (rig.anchorHost == null) rig.anchorHost = flow.Find("CamAnchor_Host");
            if (rig.anchorJoin == null) rig.anchorJoin = flow.Find("CamAnchor_Join");
            if (rig.anchorSettings == null) rig.anchorSettings = flow.Find("CamAnchor_Settings");

            // Optional empty markers only (not used to force panel positions).
            EnsureUiMountEmpty(flow, "UiMount_Main");
            EnsureUiMountEmpty(flow, "UiMount_Host");
            EnsureUiMountEmpty(flow, "UiMount_Join");
            EnsureUiMountEmpty(flow, "UiMount_Settings");

            // Do not SnapTo Main here — intro AnimationClip owns the start pose;
            // CaliMenuFlowDirector snaps to CamAnchor_Main after the intro.
        }

        static Transform EnsureAnchor(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }

        static void EnsureUiMountEmpty(Transform parent, string name)
        {
            if (parent.Find(name) != null)
                return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
        }

        static void EnsureMainMenu()
        {
            var existing = Object.FindFirstObjectByType<CaliMainMenuController>(FindObjectsInactive.Include);
            if (existing != null)
            {
                FixScale(existing.gameObject);
                return;
            }

            if (TrySpawn("UI/MainMenuRoot"))
            {
                var spawned = Object.FindFirstObjectByType<CaliMainMenuController>(FindObjectsInactive.Include);
                if (spawned != null)
                {
                    FixScale(spawned.gameObject);
                    return;
                }
            }

            DestroyNamed("MainMenuRoot");
            var menu = FantasyUiFactory.BuildMainMenuRoot();
            menu.AddComponent<CaliMainMenuController>();
            FixScale(menu);
        }

        static void EnsureOverlay<T>(string resourcesPath, System.Func<GameObject> factory) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (existing != null)
            {
                FixScale(existing.gameObject);
                return;
            }

            if (TrySpawn(resourcesPath))
            {
                var spawned = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
                if (spawned != null)
                {
                    FixScale(spawned.gameObject);
                    return;
                }
            }

            var built = factory();
            FixScale(built);
        }

        static bool TrySpawn(string resourcesPath)
        {
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab == null)
                return false;
            Object.Instantiate(prefab);
            return true;
        }

        static void FixScale(GameObject go)
        {
            if (go == null)
                return;
            // Do not force scale 1 on world-space menu canvases (they use ~0.0015).
            if (go.GetComponent<Canvas>() is Canvas c && c.renderMode == RenderMode.WorldSpace)
                return;
            go.transform.localScale = Vector3.one;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.localScale = Vector3.one;
        }

        static void SetInactiveAll<T>() where T : Component
        {
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c != null)
                    c.gameObject.SetActive(false);
            }
        }

        static void DestroyNamed(string name)
        {
            foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go != null && go.name == name && go.parent == null)
                    Object.DestroyImmediate(go.gameObject);
            }
        }
    }
}
