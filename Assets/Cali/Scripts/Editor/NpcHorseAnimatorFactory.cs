#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class NpcHorseAnimatorFactory
    {
        const string ArtPath = "Assets/Cali/Art/CaliNpcHorse.controller";
        const string ResourcePath = "Assets/Cali/Resources/Gameplay/CaliNpcHorse.controller";
        const string IdleClip = "Assets/LPHorse_Version_2_9/Version_2_9/Animations/Primary_Cycles/Rig_Idle_Center.anim";
        const string RunClip = "Assets/LPHorse_Version_2_9/Version_2_9/Animations/Primary_Cycles/Rig_Gallop_Steady_RootMotion.anim";

        [InitializeOnLoadMethod]
        static void AutoEnsure()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ResourcePath) == null)
                    Ensure(log: false);
            };
        }

        [MenuItem("Cali/Ensure NPC Horse Animator")]
        public static void MenuEnsure()
        {
            var ctrl = Ensure(log: true);
            if (ctrl != null)
                Debug.Log("[Cali] NPC horse animator ready (Idle + Gallop, Running bool).", ctrl);
        }

        public static AnimatorController Ensure(bool log)
        {
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip);
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClip);
            if (idle == null || run == null)
            {
                if (log)
                    Debug.LogWarning("[Cali] LPHorse idle/gallop clips missing; cannot build CaliNpcHorse animator.");
                return null;
            }

            Directory.CreateDirectory("Assets/Cali/Art");
            Directory.CreateDirectory("Assets/Cali/Resources/Gameplay");

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ArtPath);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(ArtPath);
                ctrl.AddParameter("Running", AnimatorControllerParameterType.Bool);

                var root = ctrl.layers[0].stateMachine;
                var idleState = root.AddState("Idle");
                idleState.motion = idle;
                var runState = root.AddState("Run");
                runState.motion = run;
                root.defaultState = idleState;

                var toRun = idleState.AddTransition(runState);
                toRun.hasExitTime = false;
                toRun.duration = 0.12f;
                toRun.AddCondition(AnimatorConditionMode.If, 0f, "Running");

                var toIdle = runState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = 0.12f;
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Running");

                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssets();
            }

            CopyToResources();
            return ctrl;
        }

        static void CopyToResources()
        {
            if (!File.Exists(ArtPath))
                return;
            if (File.Exists(ResourcePath))
                return;

            AssetDatabase.CopyAsset(ArtPath, ResourcePath);
        }
    }
}
#endif
