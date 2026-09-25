#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cali.Editor
{
    /// <summary>
    /// PackageCache test folders keep breaking compile (Bee CacheHit of corrupt TestRunner).
    /// Strip them on load so Cali menus and gameplay scripts can compile.
    /// </summary>
    [InitializeOnLoad]
    static class StripPackageTestsOnLoad
    {
        static StripPackageTestsOnLoad()
        {
            EditorApplication.delayCall += Strip;
        }

        [MenuItem("Cali/Strip Package Test Folders")]
        static void Strip()
        {
            var root = Path.GetFullPath("Library/PackageCache");
            if (!Directory.Exists(root))
                return;

            var removed = 0;
            foreach (var testsDir in Directory.GetDirectories(root, "Tests", SearchOption.AllDirectories))
            {
                // Remove package Tests trees (e.g. com.unity.inputsystem*/Tests).
                var rel = testsDir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length < 2 || parts[1] != "Tests")
                    continue;

                try
                {
                    Directory.Delete(testsDir, true);
                    removed++;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Cali: could not remove {testsDir}: {e.Message}");
                }
            }

            if (removed > 0)
                Debug.Log($"Cali: removed {removed} PackageCache Tests folder(s) to keep scripts compiling.");
        }
    }
}
#endif
