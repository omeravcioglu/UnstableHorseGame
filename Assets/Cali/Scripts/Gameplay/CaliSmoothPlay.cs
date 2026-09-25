using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Caps hitch catch-up and keeps vsync on so Animate Physics horses do not stutter
    /// against an uncapped render rate.
    /// </summary>
    public static class CaliSmoothPlay
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Apply();
        }

        public static void Apply()
        {
            QualitySettings.vSyncCount = Mathf.Max(1, QualitySettings.vSyncCount);
            Application.targetFrameRate = -1;
            Time.maximumDeltaTime = 0.05f;
        }
    }
}
