#if UNITY_EDITOR
using Cali.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cali.UI.Editor
{
    /// <summary>
    /// Drops the editable gameplay UI prefabs into MC_Demo_Day so they can be tweaked in the Hierarchy.
    /// Bootstrap skips spawn when these already exist.
    /// </summary>
    public static class CaliGameplayUiSceneBaker
    {
        const string DemoScenePath = "Assets/SCENES 1/MC_Demo_Day.unity";

        [MenuItem("Cali/UI/Place Gameplay UI In Demo Scene")]
        public static void PlaceInDemoScene()
        {
            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindFirstObjectByType<CaliUiBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogWarning("[Cali UI] <<<Cali UI Bootstrap>>> missing in MC_Demo_Day.");
                return;
            }

            Place<CaliPauseMenu>("UI/PauseMenuRoot", bootstrap.transform);
            Place<CaliSettingsMenu>("UI/SettingsRoot", bootstrap.transform);
            Place<CaliAlterHorsePanel>("UI/AlterHorseRoot", bootstrap.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Cali UI] Gameplay UI prefabs are in MC_Demo_Day under <<<Cali UI Bootstrap>>>.");
        }

        static void Place<T>(string resourcesPath, Transform parent) where T : Component
        {
            if (Object.FindFirstObjectByType<T>(FindObjectsInactive.Include) != null)
                return;

            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab == null)
            {
                Debug.LogWarning("[Cali UI] Missing Resources prefab: " + resourcesPath);
                return;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.name = prefab.name;
        }
    }
}
#endif
