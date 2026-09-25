#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Cali.Combat;
using UnityEditor;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class EnemyTalkLibraryBuilder
    {
        const string LibraryAssetPath = "Assets/Cali/Resources/Combat/EnemyTalkLibrary.asset";
        const string TalkFolder = "Assets/EnemyTalking";

        [MenuItem("Cali/Build Enemy Talk Library")]
        public static void BuildLibrary()
        {
            Directory.CreateDirectory("Assets/Cali/Resources/Combat");

            var clips = new List<AudioClip>();
            if (Directory.Exists(TalkFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { TalkFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null)
                        clips.Add(clip);
                }
            }

            var lib = AssetDatabase.LoadAssetAtPath<EnemyTalkLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<EnemyTalkLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryAssetPath);
            }

            lib.clips = clips.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Cali] EnemyTalkLibrary ready — {clips.Count} talk clips.");
        }
    }
}
#endif
