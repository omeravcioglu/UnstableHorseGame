#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cali.Editor
{
    public static class CaliUrpSetup
    {
        const string UrpAssetPath = "Assets/Cali/Settings/Cali_URP.asset";

        [MenuItem("Cali/Assign URP Pipeline")]
        public static void AssignUrpPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                EditorUtility.DisplayDialog(
                    "Cali URP Missing",
                    $"Could not find:\n{UrpAssetPath}\n\nLook in Project window under Assets/Cali/Settings.",
                    "OK");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }

            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();

            Selection.activeObject = urp;
            EditorGUIUtility.PingObject(urp);

            EditorUtility.DisplayDialog(
                "URP Assigned",
                "Cali_URP is now set in Graphics + all Quality levels.\n\nIf materials are still purple, run:\nEdit → Rendering → Render Pipeline Converter → Built-in to URP",
                "OK");
        }

        [MenuItem("Cali/Ping Cali URP Asset")]
        public static void PingUrpAsset()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                EditorUtility.DisplayDialog("Missing", UrpAssetPath, "OK");
                return;
            }

            Selection.activeObject = urp;
            EditorGUIUtility.PingObject(urp);
        }
    }
}
#endif
