using UnityEngine;
using UnityEngine.Video;

namespace Cali.UI
{
    [CreateAssetMenu(menuName = "Cali/Intro Cutscene", fileName = "CaliIntroCutscene")]
    public class CaliIntroCutsceneCatalog : ScriptableObject
    {
        public const string ResourcesPath = "Cutscene/CaliIntroCutscene";

        [Tooltip("Drop the ~30s intro MP4 here (Unity imports it as a Video Clip).")]
        public VideoClip videoClip;

        [Tooltip("Fallback / safety length when the clip has no duration yet.")]
        public float durationSeconds = 30f;

        [Tooltip("Host can press Space, Esc, or click to skip for everyone.")]
        public bool allowHostSkip = true;

        public static CaliIntroCutsceneCatalog Get()
        {
            return Resources.Load<CaliIntroCutsceneCatalog>(ResourcesPath);
        }
    }
}
