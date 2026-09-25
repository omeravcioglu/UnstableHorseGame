using System.Collections;
using System.Collections.Generic;
using Cali.Network;
using MalbersAnimations;
using MalbersAnimations.Controller;
using Unity.Cinemachine;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Scene-authored look-at camera. On a batch/latch trigger the camera blends to
    /// placed empties while horse WASD/arrows go idle (animators keep playing).
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class CaliAchievementCamera : MonoBehaviour
    {
        public enum TriggerKind
        {
            Batch1Cleared = 0,
            Batch2Cleared = 1,
            Batch3Cleared = 2,
            LatchCompleted = 3,
            LatchDoorOpened = 4
        }

        [System.Serializable]
        public class Shot
        {
            [Tooltip("Empty you place and aim. The camera sits here and looks along its forward.")]
            public Transform cameraPoint;

            [Tooltip("Seconds to stay on this shot after the blend finishes.")]
            public float holdSeconds = 5f;
        }

        [System.Serializable]
        public class Cue
        {
            public TriggerKind trigger = TriggerKind.Batch1Cleared;

            [Tooltip("Leave empty to skip this trigger.")]
            public List<Shot> shots = new();

            public float blendIn = 0.6f;
            public float blendOut = 0.6f;
        }

        const int CinematicPriority = 50;

        [Tooltip("One cue per trigger. Empty shot lists are skipped.")]
        public Cue[] cues = new Cue[]
        {
            new Cue { trigger = TriggerKind.Batch1Cleared },
            new Cue { trigger = TriggerKind.Batch2Cleared },
            new Cue { trigger = TriggerKind.Batch3Cleared },
            new Cue { trigger = TriggerKind.LatchCompleted },
            new Cue { trigger = TriggerKind.LatchDoorOpened }
        };

        public static CaliAchievementCamera Instance { get; private set; }
        public static bool IsPlaying { get; private set; }

        /// <summary>True while a cue is playing, or the host has networked idle-lock on.</summary>
        public static bool BlocksHorseInput
        {
            get
            {
                if (IsPlaying)
                    return true;
                if (Cali.UI.LevelClearedHud.IsOpen)
                    return true;

                var coop = CoopGameController.Instance;
                return coop != null && coop.Object != null && coop.Object.IsValid && coop.CinematicFreeze;
            }
        }

        CinemachineCamera _vcamA;
        CinemachineCamera _vcamB;
        CinemachineCamera _live;
        CinemachineBrain _brain;
        CinemachineBlendDefinition _savedBlend;
        bool _savedBlendValid;
        Coroutine _play;
        bool[] _savedLook;

        void Awake()
        {
            Instance = this;
            EnsureVcams();
        }

        void OnEnable()
        {
            Instance = this;

            var doors = FindObjectsByType<EnemyBatchDoors>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] != null)
                    doors[i].onBatchCleared.AddListener(OnBatchCleared);
            }

            var latches = FindObjectsByType<StableLatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < latches.Length; i++)
            {
                if (latches[i] == null)
                    continue;
                latches[i].Completed += OnLatchCompleted;
                latches[i].DoorOpened += OnLatchDoorOpened;
            }
        }

        void OnDisable()
        {
            var doors = FindObjectsByType<EnemyBatchDoors>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] != null)
                    doors[i].onBatchCleared.RemoveListener(OnBatchCleared);
            }

            var latches = FindObjectsByType<StableLatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < latches.Length; i++)
            {
                if (latches[i] == null)
                    continue;
                latches[i].Completed -= OnLatchCompleted;
                latches[i].DoorOpened -= OnLatchDoorOpened;
            }

            if (_play != null)
            {
                StopCoroutine(_play);
                _play = null;
            }

            if (IsPlaying)
                AbortPlayback();

            if (Instance == this)
                Instance = null;
        }

        void OnBatchCleared(int index)
        {
            if (index < 0 || index > 2)
                return;
            TryStartFromLocalEvent((TriggerKind)index);
        }

        void OnLatchCompleted()
        {
            TryStartFromLocalEvent(TriggerKind.LatchCompleted);
        }

        void OnLatchDoorOpened()
        {
            TryStartFromLocalEvent(TriggerKind.LatchDoorOpened);
        }

        void TryStartFromLocalEvent(TriggerKind trigger)
        {
            // Clients wait for the host RPC so both machines play the same cue.
            if (IsOnlineClient)
                return;
            Begin(trigger, fromNetwork: false);
        }

        public void PlayFromNetwork(int triggerId)
        {
            if (triggerId < 0 || triggerId > 4)
                return;
            Begin((TriggerKind)triggerId, fromNetwork: true);
        }

        void Begin(TriggerKind trigger, bool fromNetwork)
        {
            if (IsPlaying)
                return;

            var cue = FindCue(trigger);
            if (cue == null || !HasAnyShot(cue))
                return;

            if (!fromNetwork && IsOnlineHost)
            {
                var coop = CoopGameController.Instance;
                if (coop != null)
                    coop.BroadcastAchievement((int)trigger);
            }

            if (_play != null)
                StopCoroutine(_play);
            _play = StartCoroutine(PlayCue(cue));
        }

        Cue FindCue(TriggerKind trigger)
        {
            if (cues == null)
                return null;
            for (int i = 0; i < cues.Length; i++)
            {
                if (cues[i] != null && cues[i].trigger == trigger)
                    return cues[i];
            }
            return null;
        }

        static bool HasAnyShot(Cue cue)
        {
            if (cue.shots == null)
                return false;
            for (int i = 0; i < cue.shots.Count; i++)
            {
                if (cue.shots[i] != null && cue.shots[i].cameraPoint != null)
                    return true;
            }
            return false;
        }

        static bool IsOnline =>
            CoopSessionStarter.IsOnline &&
            CoopGameController.Instance != null &&
            CoopGameController.Instance.Object != null &&
            CoopGameController.Instance.Object.IsValid;

        static bool IsOnlineHost =>
            IsOnline &&
            CoopGameController.Instance.Runner != null &&
            CoopGameController.Instance.Runner.IsSharedModeMasterClient;

        static bool IsOnlineClient =>
            IsOnline &&
            CoopGameController.Instance.Runner != null &&
            !CoopGameController.Instance.Runner.IsSharedModeMasterClient;

        IEnumerator PlayCue(Cue cue)
        {
            IsPlaying = true;
            SetHorseIdle(true);
            SetLookEnabled(false);

            EnsureVcams();
            CacheBrainBlend();

            int shown = 0;
            for (int i = 0; i < cue.shots.Count; i++)
            {
                var shot = cue.shots[i];
                if (shot == null || shot.cameraPoint == null)
                    continue;

                float blend = shown == 0 ? Mathf.Max(0.01f, cue.blendIn) : Mathf.Max(0.01f, cue.blendIn);
                ActivateShot(shot.cameraPoint, blend);
                shown++;
                yield return new WaitForSeconds(blend);
                yield return new WaitForSeconds(Mathf.Max(0f, shot.holdSeconds));
            }

            SetBrainBlend(Mathf.Max(0.01f, cue.blendOut));
            LowerVcams();
            RestoreFollow();
            yield return new WaitForSeconds(Mathf.Max(0.01f, cue.blendOut));

            RestoreLook();
            SetHorseIdle(false);
            RestoreBrainBlend();
            IsPlaying = false;
            _play = null;
        }

        void AbortPlayback()
        {
            LowerVcams();
            RestoreFollow();
            RestoreLook();
            SetHorseIdle(false);
            RestoreBrainBlend();
            IsPlaying = false;
        }

        void SetHorseIdle(bool idle)
        {
            var coop = CoopGameController.Instance;
            if (coop == null || coop.Object == null || !coop.Object.IsValid)
                return;
            if (coop.Runner == null || !coop.HasStateAuthority)
                return;
            coop.CinematicFreeze = idle;
        }

        void EnsureVcams()
        {
            if (_vcamA == null)
                _vcamA = CreateVcam("Achievement Cam A");
            if (_vcamB == null)
                _vcamB = CreateVcam("Achievement Cam B");
        }

        CinemachineCamera CreateVcam(string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var vcam = go.AddComponent<CinemachineCamera>();
            vcam.Priority.Value = -1;
            vcam.Priority.Enabled = false;
            CopyLens(vcam);
            return vcam;
        }

        static void CopyLens(CinemachineCamera vcam)
        {
            if (vcam == null || Camera.main == null)
                return;
            vcam.Lens = LensSettings.FromCamera(Camera.main);
        }

        void ActivateShot(Transform point, float blendSeconds)
        {
            var next = _live == _vcamA ? _vcamB : _vcamA;
            CopyLens(next);
            next.transform.SetPositionAndRotation(point.position, point.rotation);
            SetBrainBlend(blendSeconds);

            int incoming = _live == null ? CinematicPriority : CinematicPriority + 1;
            next.Priority.Value = incoming;
            next.Priority.Enabled = true;

            if (_live != null && _live != next)
            {
                _live.Priority.Value = CinematicPriority - 1;
                _live.Priority.Enabled = true;
            }

            _live = next;
        }

        void LowerVcams()
        {
            if (_vcamA != null)
            {
                _vcamA.Priority.Value = -1;
                _vcamA.Priority.Enabled = false;
            }
            if (_vcamB != null)
            {
                _vcamB.Priority.Value = -1;
                _vcamB.Priority.Enabled = false;
            }
            _live = null;
        }

        void CacheBrainBlend()
        {
            _brain = FindFirstObjectByType<CinemachineBrain>();
            if (_brain == null)
                return;
            _savedBlend = _brain.DefaultBlend;
            _savedBlendValid = true;
        }

        void SetBrainBlend(float seconds)
        {
            if (_brain == null)
                _brain = FindFirstObjectByType<CinemachineBrain>();
            if (_brain == null)
                return;
            _brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut,
                Mathf.Max(0.01f, seconds));
        }

        void RestoreBrainBlend()
        {
            if (_brain == null || !_savedBlendValid)
                return;
            _brain.DefaultBlend = _savedBlend;
        }

        void SetLookEnabled(bool enabled)
        {
            var cams = FindObjectsByType<ThirdPersonFollowTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (!enabled)
            {
                _savedLook = new bool[cams.Length];
                for (int i = 0; i < cams.Length; i++)
                {
                    _savedLook[i] = cams[i].AllowCameraRotation.Value;
                    cams[i].AllowCameraRotation.Value = false;
                }
            }
            else
            {
                RestoreLook();
            }
        }

        void RestoreLook()
        {
            var cams = FindObjectsByType<ThirdPersonFollowTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                bool on = _savedLook != null && i < _savedLook.Length ? _savedLook[i] : true;
                cams[i].AllowCameraRotation.Value = on;
            }
            _savedLook = null;
        }

        void RestoreFollow()
        {
            MAnimal horse = null;
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
                horse = coop.LocalPlayHorse;

            if (horse == null)
            {
                var dual = FindFirstObjectByType<LocalDualHorseInput>();
                if (dual != null)
                    horse = dual.horseA;
            }

            if (horse != null)
                CoopCameraFocus.FocusOnHorse(horse);
        }
    }
}
