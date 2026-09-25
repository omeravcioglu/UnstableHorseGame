using MalbersAnimations;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Network
{
    /// <summary>
    /// Points Malbers' third-person camera at the correct horse for the local player.
    /// Each horse has an inactive "CameraTarget Horse" child that writes the shared Camera Target hook on enable.
    /// </summary>
    public static class CoopCameraFocus
    {
        const string CameraTargetChildName = "CameraTarget Horse";

        static MAnimal _lastFocused;

        public static bool IsFocusedOn(MAnimal horse)
        {
            if (horse == null || _lastFocused != horse)
                return false;

            var expected = FindCameraTargetChild(horse.transform) ?? horse.transform;
            var cams = Object.FindObjectsByType<ThirdPersonFollowTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var cam in cams)
            {
                if (cam.Target == null || cam.Target.Value != expected)
                    return false;
            }

            return cams.Length > 0;
        }

        public static void FocusOnHorse(MAnimal horse)
        {
            if (horse == null)
                return;

            // Disable other horses' camera-target hooks so they cannot steal focus on enable.
            var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var animal in animals)
            {
                var otherTarget = FindCameraTargetChild(animal.transform);
                if (otherTarget == null)
                    continue;

                if (animal == horse)
                {
                    if (!otherTarget.gameObject.activeSelf)
                        otherTarget.gameObject.SetActive(true);
                }
                else if (otherTarget.gameObject.activeSelf)
                {
                    otherTarget.gameObject.SetActive(false);
                }
            }

            var focus = FindCameraTargetChild(horse.transform) ?? horse.transform;

            var cams = Object.FindObjectsByType<ThirdPersonFollowTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (cams.Length == 0)
            {
                Debug.LogWarning("[CoopCameraFocus] No ThirdPersonFollowTarget found in scene.");
                return;
            }

            foreach (var cam in cams)
            {
                if (cam.Target != null && cam.Target.Variable != null)
                    cam.Target.Variable.SetValue(focus);
                else
                    cam.SetTarget(focus);

                // Force the follow pivot onto the new horse immediately.
                if (cam.CamPivot != null)
                    cam.CamPivot.SetPositionAndRotation(focus.position, focus.rotation);

                cam.TargetTeleport(true);
            }

            _lastFocused = horse;
            Debug.Log($"[CoopCameraFocus] Camera following '{horse.name}' via '{focus.name}'");
        }

        static Transform FindCameraTargetChild(Transform root)
        {
            if (root == null)
                return null;

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t.name == CameraTargetChildName)
                    return t;
            }

            return null;
        }
    }
}
