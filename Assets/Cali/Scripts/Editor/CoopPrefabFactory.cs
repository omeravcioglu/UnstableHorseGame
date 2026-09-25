#if UNITY_EDITOR
using System.IO;
using Cali.Gameplay;
using Cali.Network;
using Fusion;
using MalbersAnimations.Controller;
using UnityEditor;
using UnityEngine;

namespace Cali.Network.Editor
{
    public static class CoopPrefabFactory
    {
        const string PrefabPath = "Assets/Cali/Resources/CoopGameState.prefab";
        const string HorseStatePath = "Assets/Cali/Resources/NetHorse.prefab";

        [MenuItem("Cali/Create Coop Prefab")]
        public static void CreateCoopPrefab()
        {
            EnsurePrefab(true);
            EnsureHorseStatePrefab(true);
        }

        [MenuItem("Cali/Create Pegasus Powerup Trigger")]
        public static void CreatePegasusTrigger()
        {
            var form = Object.FindFirstObjectByType<HorsePegasusForm>();
            if (form == null)
            {
                var dual = Object.FindFirstObjectByType<LocalDualHorseInput>();
                var host = dual != null ? dual.gameObject : new GameObject("<<<Horse Pegasus Form>>>");
                form = host.GetComponent<HorsePegasusForm>();
                if (form == null)
                    form = Undo.AddComponent<HorsePegasusForm>(host);
                form.ResolveRefs();
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "<<<Pegasus Powerup>>>";
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null)
                Object.DestroyImmediate(meshFilter);

            var box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            go.transform.localScale = new Vector3(4f, 2f, 4f);

            var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var a in animals)
            {
                if (a.name.Contains("Horse Realistic") && !a.name.Contains("Pegasus"))
                {
                    go.transform.position = a.transform.position + a.transform.forward * 6f + Vector3.up;
                    break;
                }
            }

            var trigger = Undo.AddComponent<PegasusPowerupTrigger>(go);
            trigger.duration = 30f;
            trigger.form = form;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Powerup Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            marker.transform.localScale = new Vector3(0.35f, 0.05f, 0.35f);
            var markerCol = marker.GetComponent<Collider>();
            if (markerCol != null)
                Object.DestroyImmediate(markerCol);

            Undo.RegisterCreatedObjectUndo(go, "Create Pegasus Powerup Trigger");
            Selection.activeGameObject = go;
            Debug.Log("[Cali] Created Pegasus powerup trigger (30s). Ride a horse into it.");
        }

        [InitializeOnLoadMethod]
        static void AutoEnsure()
        {
            EditorApplication.delayCall += () =>
            {
                EnsurePrefab(false);
                EnsureHorseStatePrefab(false);
            };
        }

        static void EnsurePrefab(bool logAlways)
        {
            if (File.Exists(PrefabPath))
            {
                if (logAlways)
                    Debug.Log($"[Cali] Coop prefab already exists at {PrefabPath}");
                return;
            }

            Directory.CreateDirectory("Assets/Cali/Resources");

            var go = new GameObject("CoopGameState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<CoopGameController>();

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Cali] Created Fusion prefab: {PrefabPath}");
        }

        static void EnsureHorseStatePrefab(bool logAlways)
        {
            if (File.Exists(HorseStatePath))
            {
                PatchHorseStatePrefab();
                if (logAlways)
                    Debug.Log($"[Cali] Horse state prefab already exists at {HorseStatePath}");
                return;
            }

            Directory.CreateDirectory("Assets/Cali/Resources");

            var go = new GameObject("NetHorse");
            var no = go.AddComponent<NetworkObject>();
            no.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
            go.AddComponent<NetHorse>();

            PrefabUtility.SaveAsPrefabAsset(go, HorseStatePath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PatchHorseStatePrefab();

            Debug.Log($"[Cali] Created Fusion prefab: {HorseStatePath}");
        }

        static void PatchHorseStatePrefab()
        {
            var no = AssetDatabase.LoadAssetAtPath<NetworkObject>(HorseStatePath);
            if (no == null)
                return;

            bool dirty = false;
            if ((no.Flags & NetworkObjectFlags.AllowStateAuthorityOverride) == 0)
            {
                no.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
                dirty = true;
            }

            var labels = AssetDatabase.GetLabels(no.gameObject);
            var next = WithFusionPrefabLabel(labels);
            if (!ReferenceEquals(next, labels))
            {
                AssetDatabase.SetLabels(no.gameObject, next);
                dirty = true;
            }

            if (!dirty)
                return;

            EditorUtility.SetDirty(no);
            AssetDatabase.SaveAssets();
        }

        static string[] WithFusionPrefabLabel(string[] labels)
        {
            const string tag = "FusionPrefab";
            if (labels != null)
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] == tag)
                        return labels;
                }
            }

            int n = labels != null ? labels.Length : 0;
            var next = new string[n + 1];
            if (n > 0)
                System.Array.Copy(labels, next, n);
            next[n] = tag;
            return next;
        }
    }
}
#endif
