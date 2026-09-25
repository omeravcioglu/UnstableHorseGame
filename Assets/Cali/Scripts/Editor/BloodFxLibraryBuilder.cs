#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Cali.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cali.EditorTools
{
    /// <summary>
    /// Builds the BloodFxLibrary and remaps RVFX materials to Cali/BloodFX_URP
    /// so splash/decals keep wet PBR highlights on this project's URP pipeline.
    /// </summary>
    public static class BloodFxLibraryBuilder
    {
        const string LibraryAssetPath = "Assets/Cali/Resources/Combat/BloodFxLibrary.asset";
        const string SplashFolder = "Assets/RVFX/BloodEffectsPack/1_URP/Blood/Splash";
        const string DecalFolder = "Assets/RVFX/BloodEffectsPack/1_URP/Blood/Decal";
        const string UrpBloodRoot = "Assets/RVFX/BloodEffectsPack/1_URP";
        const string BrokenShaderGuid = "03cfcd9d341aaba468949a1c3f92d035"; // BloodFX_PBR_URP.shadergraph

        [MenuItem("Cali/Fix Blood FX Materials (URP)")]
        public static void FixBloodMaterials()
        {
            var hdBlood = Shader.Find("Cali/BloodFX_URP");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (hdBlood == null && unlit == null)
            {
                Debug.LogError("[Cali] URP Unlit shader not found. Is URP installed?");
                return;
            }

            Shader target = hdBlood != null ? hdBlood : unlit;

            string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { UrpBloodRoot });
            int fixedCount = 0;
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Projector decals need URP Decal Renderer Feature, not the splash shader.
                if (path.Replace('\\', '/').Contains("/Decal_Projector/"))
                    continue;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null)
                    continue;

                string shaderPath = AssetDatabase.GetAssetPath(mat.shader);
                string shaderGuid = string.IsNullOrEmpty(shaderPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(shaderPath);

                if (mat.shader.name == "Cali/BloodFX_URP")
                    continue;

                bool isUnlit = mat.shader.name.Contains("Unlit");
                bool broken = shaderGuid == BrokenShaderGuid
                              || mat.shader.name.Contains("BloodFX")
                              || mat.shader.name == "Hidden/InternalErrorShader"
                              || isUnlit;
                if (!broken)
                    continue;

                Texture mainTex = null;
                if (mat.HasProperty("_MainTex"))
                    mainTex = mat.GetTexture("_MainTex");
                if (mainTex == null && mat.HasProperty("_BaseMap"))
                    mainTex = mat.GetTexture("_BaseMap");
                if (mainTex == null)
                    mainTex = mat.mainTexture;

                Texture normalTex = null;
                if (mat.HasProperty("_NormalMap"))
                    normalTex = mat.GetTexture("_NormalMap");
                if (normalTex == null && mat.HasProperty("_NormalTex"))
                    normalTex = mat.GetTexture("_NormalTex");
                if (normalTex == null && mat.HasProperty("_BumpMap"))
                    normalTex = mat.GetTexture("_BumpMap");

                Color color = Color.white;
                if (mat.HasProperty("_Color"))
                    color = mat.GetColor("_Color");
                else if (mat.HasProperty("_BaseColor"))
                    color = mat.GetColor("_BaseColor");

                mat.shader = target;

                if (mat.HasProperty("_BaseMap") && mainTex != null)
                    mat.SetTexture("_BaseMap", mainTex);
                if (mat.HasProperty("_MainTex") && mainTex != null)
                    mat.SetTexture("_MainTex", mainTex);
                mat.mainTexture = mainTex;
                if (mat.HasProperty("_NormalMap") && normalTex != null)
                    mat.SetTexture("_NormalMap", normalTex);

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", color);

                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend"))
                    mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");

                EditorUtility.SetDirty(mat);
                fixedCount++;
            }

            // Second pass: any URP Unlit blood mat still missing BaseMap texture.
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    continue;
                if (!mat.HasProperty("_BaseMap") || !mat.HasProperty("_MainTex"))
                    continue;
                if (mat.GetTexture("_BaseMap") == null && mat.GetTexture("_MainTex") != null)
                {
                    mat.SetTexture("_BaseMap", mat.GetTexture("_MainTex"));
                    EditorUtility.SetDirty(mat);
                    fixedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cali] Assigned HD blood shader to {fixedCount} RVFX materials (Cali/BloodFX_URP).");
        }

        [MenuItem("Cali/Build Blood FX Library")]
        public static void BuildLibrary()
        {
            FixBloodMaterials();

            Directory.CreateDirectory("Assets/Cali/Resources/Combat");

            var splashes = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SplashFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                // Prefer one-shots for kills (skip Continuous for death bursts).
                if (name.Contains("Continuous"))
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                    splashes.Add(prefab);
            }

            // Keep WithGut in the pool even if a rebuild filter changes later.
            string withGutPath = SplashFolder + "/Blood_Splash_01_WithGut_URP.prefab";
            var withGut = AssetDatabase.LoadAssetAtPath<GameObject>(withGutPath);
            if (withGut != null && !splashes.Contains(withGut))
                splashes.Insert(0, withGut);

            var decals = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { DecalFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                // Static + SpriteSheet look good as ground marks; skip Continuous/Trail.
                if (name.Contains("Continuous") || name.Contains("Trail"))
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                    decals.Add(prefab);
            }

            var lib = AssetDatabase.LoadAssetAtPath<BloodFxLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<BloodFxLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryAssetPath);
            }

            lib.splashPrefabs = splashes.ToArray();
            lib.decalPrefabs = decals.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ApplyLibraryToAllEnemies();

            Debug.Log(
                $"[Cali] BloodFxLibrary ready — {splashes.Count} splashes, {decals.Count} decals. " +
                "Kills will pick random splash + random decal.");
        }

        [MenuItem("Cali/Apply Blood FX Library To All Enemies")]
        public static void ApplyLibraryToAllEnemies()
        {
            int count = 0;
            foreach (var enemy in Object.FindObjectsByType<ChainKillableEnemy>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Clear overrides so library random picks are used.
                enemy.bloodPrefab = null;
                enemy.bloodDecalPrefab = null;
                enemy.spawnBloodDecal = true;
                enemy.useRandomPackFx = true;
                EditorUtility.SetDirty(enemy);
                count++;
            }

            // Also clear on prefabs.
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Cali" });
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                var enemies = root.GetComponentsInChildren<ChainKillableEnemy>(true);
                bool dirty = false;
                foreach (var enemy in enemies)
                {
                    enemy.bloodPrefab = null;
                    enemy.bloodDecalPrefab = null;
                    enemy.spawnBloodDecal = true;
                    enemy.useRandomPackFx = true;
                    dirty = true;
                    count++;
                }

                if (dirty)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log($"[Cali] Applied random Blood FX library to {count} enemy instance(s)/prefab(s).");
        }

        [InitializeOnLoadMethod]
        static void AutoBuildIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                if (File.Exists(LibraryAssetPath))
                    return;
                if (!Directory.Exists(SplashFolder))
                    return;

                try
                {
                    BuildLibrary();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Cali] Auto Blood FX library build skipped: {e.Message}");
                }
            };
        }
    }
}
#endif
