#if UNITY_EDITOR
using Cali.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class CoopSaveResetMenu
    {
        [MenuItem("Cali/Reset Coop Save", priority = 50)]
        public static void ResetCoopSave()
        {
            bool had = PlayerPrefs.GetInt("Cali.CoopSave.Valid", 0) == 1;
            string scene = PlayerPrefs.GetString("Cali.CoopSave.Scene", "");
            CoopSavePoint.ClearCheckpoint();
            if (had)
                Debug.Log($"[Cali] Coop save cleared (was scene '{scene}'). Next Play starts at the default spawn.");
            else
                Debug.Log("[Cali] No coop save was stored.");
        }

        [MenuItem("Cali/Reset Coop Save", true)]
        public static bool ResetCoopSaveValidate()
        {
            return true;
        }

        [MenuItem("Cali/Create Dead Zone", priority = 20)]
        public static void CreateDeadZone()
        {
            var go = new GameObject("CoopDeadZone");
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                go.transform.position = view.pivot;
            else
                go.transform.position = Vector3.zero;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(12f, 6f, 12f);

            go.AddComponent<CoopDeadZone>();
            Undo.RegisterCreatedObjectUndo(go, "Create Dead Zone");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            Debug.Log("[Cali] Dead zone created. Scale it to cover the kill area. Touching it sends both horses to the last save.");
        }
    }
}
#endif
