#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Remaps materials that reference missing URP shaders (pink/magenta) to Built-in shaders.
/// Run: Tools/Fix Purple Materials, or Unity -batchmode -executeMethod FixPurpleMaterials.Fix
/// </summary>
public static class FixPurpleMaterials
{
    static readonly Dictionary<string, string> ShaderRemap = new()
    {
        // URP Lit → Standard
        { "933532a4fcc9baf4fa0491de14d08ed7", "Standard" },
        // URP Particles Unlit → transparent unlit
        { "0406db5a14f94604a8c57ccfbc9f3b46", "Unlit/Transparent" },
        // URP Terrain Lit → Built-in terrain
        { "69c1f799e772cb6438f56c23efccb782", "Nature/Terrain/Standard" },
    };

    [MenuItem("Tools/Fix Purple Materials")]
    public static void Fix()
    {
        var guids = AssetDatabase.FindAssets("t:Material");
        int fixedCount = 0;
        var log = new List<string>();

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            bool needsFix = mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader";
            string targetShaderName = null;

            if (needsFix)
            {
                // Read raw YAML for original missing shader guid
                var yaml = System.IO.File.ReadAllText(path);
                foreach (var kv in ShaderRemap)
                {
                    if (yaml.Contains($"guid: {kv.Key}"))
                    {
                        targetShaderName = kv.Value;
                        break;
                    }
                }

                // Fallback for already-broken materials whose YAML was partially edited
                if (targetShaderName == null)
                {
                    if (path.Contains("Terrain"))
                        targetShaderName = "Nature/Terrain/Standard";
                    else if (path.Contains("Interactables") || path.Contains("Click") || path.Contains("Cross") || path.Contains("Position Select"))
                        targetShaderName = "Unlit/Transparent";
                    else
                        targetShaderName = "Standard";
                }
            }
            else
            {
                // Also catch materials still pointing at URP guids that somehow resolve as error
                var yaml = System.IO.File.ReadAllText(path);
                foreach (var kv in ShaderRemap)
                {
                    if (yaml.Contains($"guid: {kv.Key}"))
                    {
                        targetShaderName = kv.Value;
                        needsFix = true;
                        break;
                    }
                }
            }

            if (!needsFix || string.IsNullOrEmpty(targetShaderName))
                continue;

            var shader = Shader.Find(targetShaderName);
            if (shader == null)
            {
                log.Add($"FAIL: Shader.Find('{targetShaderName}') null for {path}");
                continue;
            }

            // Preserve key maps before shader swap
            var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            var baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            var bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color")
                : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : Color.white;
            var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            var gloss = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness")
                : mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness")
                : 0.5f;

            Undo.RecordObject(mat, "Fix Purple Material");
            mat.shader = shader;

            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", mainTex != null ? mainTex : baseMap);
            if (mat.HasProperty("_BumpMap") && bump != null)
                mat.SetTexture("_BumpMap", bump);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", gloss);

            EditorUtility.SetDirty(mat);
            fixedCount++;
            log.Add($"OK: {path} → {targetShaderName}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[FixPurpleMaterials] Fixed {fixedCount} materials.\n" + string.Join("\n", log));
    }
}
#endif
