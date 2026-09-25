#if UNITY_EDITOR
using System.IO;
using Cali.Gameplay;
using MalbersAnimations.Controller;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class ChainPuzzleBoxFactory
    {
        const string PrefabDir = "Assets/Cali/Prefabs/Puzzle";
        const string PrefabPath = PrefabDir + "/CaliChainBox.prefab";
        const string DemoScenePath = "Assets/SCENES 1/MC_Demo_Day.unity";
        const string WoodMeshPath = "Assets/PretoriusLab/IndustrialProps/Prefabs/WoodenBox.prefab";

        [MenuItem("Cali/Place Chain Puzzle Boxes")]
        public static void PlaceInActiveScene()
        {
            var prefab = EnsurePrefab(true);
            if (prefab == null)
                return;

            if (Object.FindObjectsByType<ChainPushable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0)
            {
                Debug.Log("[Cali] Chain puzzle boxes already exist in this scene.");
                return;
            }

            Vector3 center = FindHorseCenter();
            PlaceOne(prefab, center + new Vector3(3.6f, 0.08f, 2.2f), "CaliChainBox_A");
            PlaceOne(prefab, center + new Vector3(5.1f, 0.08f, -0.8f), "CaliChainBox_B");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Cali] Placed two chain-pushable boxes near the horses. Sweep the chain into them to slide.");
        }

        [MenuItem("Cali/Place Chain Puzzle Boxes In MC_Demo_Day")]
        public static void PlaceInDemoScene()
        {
            if (!File.Exists(DemoScenePath))
            {
                Debug.LogError("[Cali] Scene not found: " + DemoScenePath);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(DemoScenePath);
            PlaceInActiveScene();
            EditorSceneManager.SaveOpenScenes();
        }

        [InitializeOnLoadMethod]
        static void AutoEnsure()
        {
            EditorApplication.delayCall += () => EnsurePrefab(false);
        }

        static GameObject EnsurePrefab(bool logAlways)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                if (existing.GetComponent<ChainPushable>() != null)
                {
                    if (logAlways)
                        Debug.Log("[Cali] Chain box prefab already exists at " + PrefabPath);
                    return existing;
                }
            }

            Directory.CreateDirectory(PrefabDir);

            var go = new GameObject("CaliChainBox");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(WoodMeshPath);
            if (source != null)
            {
                var srcFilter = source.GetComponent<MeshFilter>();
                var srcRend = source.GetComponent<MeshRenderer>();
                var filter = go.AddComponent<MeshFilter>();
                var rend = go.AddComponent<MeshRenderer>();
                if (srcFilter != null)
                    filter.sharedMesh = srcFilter.sharedMesh;
                if (srcRend != null)
                    rend.sharedMaterials = srcRend.sharedMaterials;
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.AddComponent<MeshFilter>().sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = cube.GetComponent<MeshRenderer>().sharedMaterial;
                Object.DestroyImmediate(cube);
            }

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.85f, 0.8f, 0.85f);
            box.center = new Vector3(0f, 0.4f, 0f);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 280f;
            rb.linearDamping = 4.2f;
            rb.angularDamping = 6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.useGravity = true;

            go.AddComponent<ChainPushable>();
            int item = LayerMask.NameToLayer("Item");
            if (item >= 0)
                go.layer = item;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            if (logAlways)
                Debug.Log("[Cali] Created chain box prefab: " + PrefabPath);
            return prefab;
        }

        static Vector3 FindHorseCenter()
        {
            var chain = Object.FindFirstObjectByType<SoftHorseChain>();
            if (chain != null && chain.horseA != null)
                return chain.horseA.transform.position;

            var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var a in animals)
            {
                if (a.name.Contains("Horse Realistic") && !a.name.Contains("Pegasus"))
                    return a.transform.position;
            }

            foreach (var a in animals)
                return a.transform.position;

            return new Vector3(74f, 0.13f, -10f);
        }

        static void PlaceOne(GameObject prefab, Vector3 pos, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out var hit, 12f))
                pos.y = hit.point.y;
            go.transform.position = pos;
            Undo.RegisterCreatedObjectUndo(go, "Place Chain Puzzle Box");
        }
    }
}
#endif
