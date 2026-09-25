#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cali.Audio;
using UnityEditor;
using UnityEngine;

namespace Cali.EditorTools
{
    public class CaliSoundEditorWindow : EditorWindow
    {
        CaliSoundBank _bank;
        SerializedObject _so;
        Vector2 _scroll;
        DefaultAsset _importFolder;
        int _importGroup = 2;

        [MenuItem("Cali/Sound Editor")]
        public static void Open()
        {
            var win = GetWindow<CaliSoundEditorWindow>(false, "Cali Sounds", true);
            win.minSize = new Vector2(460, 420);
            win.Show();
        }

        void OnEnable()
        {
            EnsureBank();
        }

        void OnGUI()
        {
            EnsureBank();
            if (_bank == null || _so == null)
            {
                EditorGUILayout.HelpBox("Could not create CaliSoundBank.", MessageType.Error);
                return;
            }

            _so.Update();

            EditorGUILayout.LabelField("Gameplay sounds", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "CaliMusicController is menu-only. This bank drives play-scene audio: background, environment, blood, chain, and anything else you add. Empty groups are skipped.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var next = (CaliSoundBank)EditorGUILayout.ObjectField("Sound Bank", _bank, typeof(CaliSoundBank), false);
            if (EditorGUI.EndChangeCheck() && next != null)
            {
                _bank = next;
                _so = new SerializedObject(_bank);
            }

            EditorGUILayout.Space(6);
            DrawImporter();
            EditorGUILayout.Space(8);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var groups = _so.FindProperty("groups");
            if (groups != null)
            {
                for (int i = 0; i < groups.arraySize; i++)
                    DrawGroup(groups, i);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Group", GUILayout.Height(24)))
            {
                groups.arraySize++;
                var g = groups.GetArrayElementAtIndex(groups.arraySize - 1);
                g.FindPropertyRelative("name").stringValue = "New";
                g.FindPropertyRelative("kind").enumValueIndex = (int)CaliSoundKind.SfxOneShot;
                g.FindPropertyRelative("volume").floatValue = 1f;
                g.FindPropertyRelative("spatial").boolValue = true;
            }

            if (GUILayout.Button("Restore Default Groups", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog(
                        "Restore default groups?",
                        "This replaces the group list with the built-in set and drops any group you added by hand. Clips already assigned to a matching name are kept.",
                        "Restore",
                        "Cancel"))
                {
                    RestoreDefaults();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_so.ApplyModifiedProperties())
                EditorUtility.SetDirty(_bank);
        }

        void DrawImporter()
        {
            EditorGUILayout.LabelField("Add clips", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _importFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder", _importFolder, typeof(DefaultAsset), false);
            if (_bank.groups != null && _bank.groups.Length > 0)
            {
                string[] names = new string[_bank.groups.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = string.IsNullOrEmpty(_bank.groups[i].name) ? "Group " + i : _bank.groups[i].name;
                _importGroup = Mathf.Clamp(_importGroup, 0, names.Length - 1);
                _importGroup = EditorGUILayout.Popup(_importGroup, names, GUILayout.Width(160));
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Folder To Group"))
                ImportFolderIntoGroup();
            if (GUILayout.Button("Add Project Selection To Group"))
                ImportSelectionIntoGroup();
            EditorGUILayout.EndHorizontal();
        }

        void DrawGroup(SerializedProperty groups, int index)
        {
            var g = groups.GetArrayElementAtIndex(index);
            var nameProp = g.FindPropertyRelative("name");
            string title = string.IsNullOrEmpty(nameProp.stringValue) ? "Group " + index : nameProp.stringValue;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            g.isExpanded = EditorGUILayout.Foldout(g.isExpanded, title, true);
            if (GUILayout.Button("Preview", GUILayout.Width(70)))
                PreviewGroup(index);
            if (GUILayout.Button("Stop", GUILayout.Width(50)))
                StopPreview();
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                groups.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (g.isExpanded)
            {
                EditorGUILayout.PropertyField(nameProp);
                EditorGUILayout.PropertyField(g.FindPropertyRelative("kind"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("volume"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("pitch"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("clipFallback"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("minInterval"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("playOnStart"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("spatial"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("minDistance"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("maxDistance"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative("clips"), true);
            }

            EditorGUILayout.EndVertical();
        }

        void ImportFolderIntoGroup()
        {
            if (_importFolder == null || _bank.groups == null || _bank.groups.Length == 0)
                return;

            string folder = AssetDatabase.GetAssetPath(_importFolder);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Sound Editor", "Pick a folder in the Project window.", "OK");
                return;
            }

            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null)
                    clips.Add(clip);
            }

            AppendClips(_importGroup, clips);
        }

        void ImportSelectionIntoGroup()
        {
            var clips = new List<AudioClip>();
            foreach (var obj in Selection.objects)
            {
                if (obj is AudioClip clip)
                    clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                EditorUtility.DisplayDialog("Sound Editor", "Select one or more audio clips in the Project window first.", "OK");
                return;
            }

            AppendClips(_importGroup, clips);
        }

        void AppendClips(int groupIndex, List<AudioClip> add)
        {
            if (add == null || add.Count == 0 || _bank.groups == null)
                return;

            groupIndex = Mathf.Clamp(groupIndex, 0, _bank.groups.Length - 1);
            Undo.RecordObject(_bank, "Add sound clips");

            var existing = _bank.groups[groupIndex].clips ?? System.Array.Empty<AudioClip>();
            var merged = new List<AudioClip>(existing);
            for (int i = 0; i < add.Count; i++)
            {
                if (add[i] != null && !merged.Contains(add[i]))
                    merged.Add(add[i]);
            }

            _bank.groups[groupIndex].clips = merged.ToArray();
            EditorUtility.SetDirty(_bank);
            _so = new SerializedObject(_bank);
        }

        void RestoreDefaults()
        {
            Undo.RecordObject(_bank, "Restore default sound groups");
            var fresh = CaliSoundBank.CreateDefaultGroups();
            if (_bank.groups != null)
            {
                for (int i = 0; i < fresh.Length; i++)
                {
                    var old = _bank.FindGroup(fresh[i].name);
                    if (old != null)
                        fresh[i].clips = old.clips;
                }
            }

            _bank.groups = fresh;
            EditorUtility.SetDirty(_bank);
            _so = new SerializedObject(_bank);
        }

        void PreviewGroup(int index)
        {
            if (_bank.groups == null || index < 0 || index >= _bank.groups.Length)
                return;

            var clip = _bank.groups[index].PickRandom();
            if (clip == null)
                return;

            StopPreview();
            TryPlayPreview(clip);
        }

        static void StopPreview()
        {
            TryInvokeAudioUtil("StopAllPreviewClips");
        }

        static void TryPlayPreview(AudioClip clip)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (type == null)
                return;

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "PlayPreviewClip")
                    continue;

                var p = methods[i].GetParameters();
                if (p.Length == 3 && p[0].ParameterType == typeof(AudioClip))
                {
                    methods[i].Invoke(null, new object[] { clip, 0, false });
                    return;
                }

                if (p.Length == 1 && p[0].ParameterType == typeof(AudioClip))
                {
                    methods[i].Invoke(null, new object[] { clip });
                    return;
                }
            }
        }

        static void TryInvokeAudioUtil(string methodName)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            var method = type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(null, null);
        }

        void EnsureBank()
        {
            if (_bank != null && _so != null)
                return;

            Directory.CreateDirectory("Assets/Cali/Resources/Audio");
            _bank = AssetDatabase.LoadAssetAtPath<CaliSoundBank>(CaliSoundBank.AssetPath);
            if (_bank == null)
            {
                _bank = CreateInstance<CaliSoundBank>();
                _bank.groups = CaliSoundBank.CreateDefaultGroups();
                AssetDatabase.CreateAsset(_bank, CaliSoundBank.AssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else if (_bank.EnsureDefaultGroups())
            {
                // Picks up groups added after this bank was last saved, such as Latch Door.
                EditorUtility.SetDirty(_bank);
            }

            _so = new SerializedObject(_bank);
        }
    }
}
#endif
