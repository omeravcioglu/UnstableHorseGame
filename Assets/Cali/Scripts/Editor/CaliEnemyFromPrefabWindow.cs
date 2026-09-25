#if UNITY_EDITOR
using Cali.Combat;
using UnityEditor;
using UnityEngine;

namespace Cali.EditorTools
{
    public class CaliEnemyFromPrefabWindow : EditorWindow
    {
        const string DefaultFolder = "Assets/Cali/Prefabs/Combat";

        GameObject _sourcePrefab;
        EnemyKind _kind = EnemyKind.Wolf;
        string _outputName = "";
        string _outputFolder = DefaultFolder;
        bool _copyToResources = true;
        GameObject _lastCreated;

        [MenuItem("Cali/Make Enemy From Prefab")]
        public static void Open()
        {
            var win = GetWindow<CaliEnemyFromPrefabWindow>(true, "Make Enemy From Prefab", true);
            win.minSize = new Vector2(380, 260);
            win.TryAssignSelection();
            win.Show();
        }

        [MenuItem("Assets/Cali/Make Enemy Prefab", false, 2000)]
        static void OpenFromProject()
        {
            Open();
        }

        [MenuItem("Assets/Cali/Make Enemy Prefab", true)]
        static bool OpenFromProjectValidate()
        {
            return GetSelectedPrefab() != null;
        }

        static GameObject GetSelectedPrefab()
        {
            var go = Selection.activeGameObject;
            if (go == null)
                return null;

            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                return null;

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        void OnEnable()
        {
            TryAssignSelection();
        }

        void TryAssignSelection()
        {
            var selected = GetSelectedPrefab();
            if (selected == null)
                return;

            _sourcePrefab = selected;
            if (string.IsNullOrEmpty(_outputName))
                _outputName = "CaliEnemy_" + selected.name;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Turn a character prefab into a Cali enemy.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            EditorGUI.BeginChangeCheck();
            _sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
                "Source Prefab", _sourcePrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && _sourcePrefab != null)
                _outputName = "CaliEnemy_" + _sourcePrefab.name;

            _kind = (EnemyKind)EditorGUILayout.EnumPopup(
                new GUIContent("Kind", "Wolf = close chase on the source prefab. Humanoid = CaliEnemy_Humanoid animator + Malbers components, with your character as the look."),
                _kind);

            if (_kind == EnemyKind.Humanoid)
                EditorGUILayout.HelpBox(
                    "Humanoid copies CaliEnemy_Humanoid (AC Human v5 animator, MAnimal, spear). Your prefab is parented as Body so it keeps its mesh and Avatar.",
                    MessageType.None);

            _outputName = EditorGUILayout.TextField("Output Name", _outputName);

            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(32)))
            {
                string picked = EditorUtility.OpenFolderPanel("Enemy prefab folder", _outputFolder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    if (picked.StartsWith(Application.dataPath))
                        _outputFolder = "Assets" + picked.Substring(Application.dataPath.Length).Replace('\\', '/');
                    else
                        EditorUtility.DisplayDialog("Invalid folder", "Pick a folder inside this project's Assets.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            _copyToResources = EditorGUILayout.Toggle(
                new GUIContent("Copy to Resources", "Also write Assets/Cali/Resources/Combat so runtime loaders can find it."),
                _copyToResources);

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(_sourcePrefab == null))
            {
                if (GUILayout.Button("Create Enemy Prefab", GUILayout.Height(32)))
                    Create();
            }

            if (_sourcePrefab == null)
                EditorGUILayout.HelpBox("Drop a character prefab. The original is left unchanged.", MessageType.Info);

            if (_lastCreated != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.ObjectField("Last Created", _lastCreated, typeof(GameObject), false);
            }
        }

        void Create()
        {
            if (_sourcePrefab == null)
                return;

            string folder = string.IsNullOrWhiteSpace(_outputFolder) ? DefaultFolder : _outputFolder;
            string name = string.IsNullOrWhiteSpace(_outputName) ? "CaliEnemy_" + _sourcePrefab.name : _outputName;

            _lastCreated = EnemyPrefabFactory.CreateEnemyFromCharacter(
                _sourcePrefab, _kind, folder, name, _copyToResources);

            if (_lastCreated != null)
            {
                Selection.activeObject = _lastCreated;
                EditorGUIUtility.PingObject(_lastCreated);
                Debug.Log($"[Cali] Created enemy prefab '{_lastCreated.name}' from '{_sourcePrefab.name}'.", _lastCreated);
            }
        }
    }
}
#endif
