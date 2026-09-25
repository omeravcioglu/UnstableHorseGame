#if UNITY_EDITOR
using System.IO;
using Cali.Cutscene;
using UnityEditor;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class CutsceneChainFactory
    {
        const string PrefabPath = "Assets/Cali/Prefabs/Cutscene/CaliCutsceneChain.prefab";

        [MenuItem("Cali/Create Cutscene Chain Prefab")]
        public static void CreatePrefab()
        {
            var prefab = EnsurePrefab(true);
            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
        }

        [InitializeOnLoadMethod]
        static void AutoEnsure()
        {
            EditorApplication.delayCall += () => EnsurePrefab(false);
        }

        static GameObject EnsurePrefab(bool logAlways)
        {
            const string ChainMesh = "Assets/PretoriusLab/IndustrialProps/Prefabs/Chain.prefab";
            var chainMesh = AssetDatabase.LoadAssetAtPath<GameObject>(ChainMesh);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<CutsceneChain>() != null)
            {
                var c = existing.GetComponent<CutsceneChain>();
                if (c.linkPrefab == null && chainMesh != null)
                {
                    c.linkPrefab = chainMesh;
                    c.linkMeshLength = 0.35f;
                    c.linkEulerOffset = new Vector3(90f, 0f, 0f);
                    c.hideLineWhenUsingMesh = true;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }
                if (logAlways)
                    Debug.Log("[Cali] Cutscene chain prefab is at " + PrefabPath);
                return existing;
            }

            Directory.CreateDirectory("Assets/Cali/Prefabs/Cutscene");

            var go = new GameObject("CaliCutsceneChain");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 5;
            line.startWidth = 0.07f;
            line.endWidth = 0.07f;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var a = new GameObject("Point A").transform;
            a.SetParent(go.transform, false);
            a.localPosition = new Vector3(-2f, 1f, 0f);

            var b = new GameObject("Point B").transform;
            b.SetParent(go.transform, false);
            b.localPosition = new Vector3(2f, 1f, 0f);

            var chain = go.AddComponent<CutsceneChain>();
            chain.pointA = a;
            chain.pointB = b;
            chain.sag = 0.4f;
            chain.segments = 16;
            chain.width = 0.07f;
            chain.linkPrefab = chainMesh;
            chain.linkMeshLength = 0.35f;
            chain.linkEulerOffset = new Vector3(90f, 0f, 0f);
            chain.hideLineWhenUsingMesh = true;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("[Cali] Created cutscene chain prefab: " + PrefabPath);
            return prefab;
        }
    }
}
#endif
