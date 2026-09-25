using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.UI
{
    /// <summary>
    /// Resolves build-settings scenes by name so reordering indices cannot break loads.
    /// </summary>
    public static class CaliSceneIds
    {
        public static int FindBuildIndex(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return -1;

            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path))
                    continue;
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    return i;
            }

            return -1;
        }

        public static bool IsActive(string sceneName)
        {
            return SceneManager.GetActiveScene().name == sceneName;
        }
    }
}
