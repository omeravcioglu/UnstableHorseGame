#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Cali.Editor
{
    /// <summary>
    /// Draw-distance helpers only. Baked Umbra occlusion is disabled: a 141 MB bake made URP
    /// fail with "Failed to build mask for shadow culler. ErroCode: 3" and then crashed natively
    /// inside umbraoptimizer64.dll.
    /// </summary>
    public static class CaliCullingTools
    {
        const string UrpAssetPath = "Assets/Cali/Settings/Cali_URP.asset";
        const float ShadowDistance = 70f;
        const float LodBias = 1f;
        const float FarClip = 400f;

        [MenuItem("Cali/Culling/Disable Baked Occlusion (crash fix)", priority = 0)]
        public static void DisableBakedOcclusion()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
                UnlinkSceneOcclusion(scene.path);

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam == null)
                    continue;
                cam.useOcclusionCulling = false;
                EditorUtility.SetDirty(cam);
            }

            string dataPath = OcclusionDataPath(scene);
            if (!string.IsNullOrEmpty(dataPath) && File.Exists(ToAbsolute(dataPath)))
                AssetDatabase.DeleteAsset(dataPath);

            MarkOpenScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            EditorUtility.DisplayDialog(
                "Baked Occlusion Off",
                "Scene unlinked, camera occlusion unchecked, bake data removed.\nUnity should no longer crash on shadow culling.",
                "OK");
        }

        [MenuItem("Cali/Culling/Apply Draw Distance Settings", priority = 1)]
        public static void ApplyDrawDistanceSettings()
        {
            var msg = new StringBuilder();

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                EditorUtility.DisplayDialog("Missing URP Asset", UrpAssetPath, "OK");
                return;
            }

            msg.AppendLine($"Shadow distance: {urp.shadowDistance:F0} -> {ShadowDistance:F0}");
            urp.shadowDistance = ShadowDistance;
            EditorUtility.SetDirty(urp);

            msg.AppendLine($"LOD bias ({QualitySettings.names[QualitySettings.GetQualityLevel()]}): " +
                           $"{QualitySettings.lodBias:F2} -> {LodBias:F2}");
            QualitySettings.lodBias = LodBias;

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam == null || cam.targetTexture != null)
                    continue;
                cam.useOcclusionCulling = false;
                if (cam.farClipPlane > FarClip)
                {
                    msg.AppendLine($"{cam.name} far clip: {cam.farClipPlane:F0} -> {FarClip:F0}");
                    cam.farClipPlane = FarClip;
                }
                EditorUtility.SetDirty(cam);
            }

            MarkOpenScenesDirty();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[CaliCulling] Draw distance settings\n{msg}");
            EditorUtility.DisplayDialog("Draw Distances Applied", msg.ToString(), "OK");
        }

        [MenuItem("Cali/Culling/Report Culling State", priority = 2)]
        public static void ReportCullingState()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            var scene = EditorSceneManager.GetActiveScene();
            string dataPath = OcclusionDataPath(scene);
            bool dataOnDisk = !string.IsNullOrEmpty(dataPath) && File.Exists(ToAbsolute(dataPath));

            var msg = new StringBuilder();
            msg.AppendLine($"Scene: {scene.path}");
            msg.AppendLine($"Bake file on disk: {dataOnDisk}");
            msg.AppendLine($"Shadow distance: {(urp != null ? urp.shadowDistance : -1):F0}");
            msg.AppendLine($"LOD bias: {QualitySettings.lodBias:F2} ({QualitySettings.names[QualitySettings.GetQualityLevel()]})");

            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in cams)
                msg.AppendLine($"  camera '{c.name}' occlusion={c.useOcclusionCulling} far={c.farClipPlane:F0}");

            Debug.Log($"[CaliCulling] State\n{msg}");
            EditorUtility.DisplayDialog("Culling State", msg.ToString(), "OK");
        }

        static void UnlinkSceneOcclusion(string scenePath)
        {
            string abs = ToAbsolute(scenePath);
            if (!File.Exists(abs))
                return;

            var text = File.ReadAllText(abs);
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"m_SceneGUID: [0-9a-fA-F]{32}",
                "m_SceneGUID: 00000000000000000000000000000000",
                System.Text.RegularExpressions.RegexOptions.None,
                System.TimeSpan.FromSeconds(2));
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"m_OcclusionCullingData: \{fileID: 36300000, guid: [0-9a-fA-F]{32}, type: 2\}",
                "m_OcclusionCullingData: {fileID: 0}",
                System.Text.RegularExpressions.RegexOptions.None,
                System.TimeSpan.FromSeconds(2));
            File.WriteAllText(abs, text);
        }

        static string OcclusionDataPath(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                return null;
            string dir = Path.GetDirectoryName(scene.path)?.Replace("\\", "/");
            string name = Path.GetFileNameWithoutExtension(scene.path);
            return $"{dir}/{name}/OcclusionCullingData.asset";
        }

        static string ToAbsolute(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        static void MarkOpenScenesDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
#endif
