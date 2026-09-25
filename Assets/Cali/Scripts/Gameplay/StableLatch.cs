using System;
using System.Collections;
using System.Collections.Generic;
using Cali.Audio;
using Cali.Network;
using Cali.UI;
using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.AI;

namespace Cali.Gameplay
{
    /// <summary>
    /// Stall latch: both player horses enter, play a dual timing mini-game, then NPC horses flee.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class StableLatch : MonoBehaviour
    {
        public const float DefaultZoneWidth = 0.18f;
        public const float DefaultMarkerSpeed = 1.35f;

        static readonly List<StableLatch> Instances = new();

        [Tooltip("Unique across stables in the scene. 0 hashes the object name (same on all peers).")]
        public int syncId;

        [Header("Players")]
        [Tooltip("Leave empty to auto-find 'Horse Realistic'.")]
        public MAnimal horseA;
        [Tooltip("Leave empty to auto-find 'Horse Unicorn'.")]
        public MAnimal horseB;

        [Header("Stall")]
        public Transform stallDoor;
        public float swingYaw = 95f;
        public float raiseHeight = 4.5f;
        public float openSeconds = 1.2f;
        public CaliNpcHorse[] npcHorses = new CaliNpcHorse[3];
        public Transform outsideTarget;
        public float horseSideSpacing = 2.2f;

        [Header("Timing")]
        public float zoneWidth = DefaultZoneWidth;
        public float markerSpeed = DefaultMarkerSpeed;
        [Tooltip("Each player must land this many green-zone hits. Progress is independent.")]
        public int hitsNeeded = 3;

        public event Action Completed;

        /// <summary>Fired when the stall door starts opening (before the raise/swing animation).</summary>
        public event Action DoorOpened;

        public bool IsCompleted { get; private set; }
        public bool IsUnlocked { get; private set; }
        public bool IsPlaying { get; private set; }

        public int SyncId => syncId != 0 ? syncId : Animator.StringToHash(name);

        public static bool SuppressHorseJump
        {
            get
            {
                if (CoopSavePoint.SuppressHorseJump)
                    return true;

                for (int i = 0; i < Instances.Count; i++)
                {
                    var latch = Instances[i];
                    if (latch != null && latch._suppressJump)
                        return true;
                }

                return false;
            }
        }

        readonly HashSet<Collider> _insideA = new();
        readonly HashSet<Collider> _insideB = new();

        struct HeldDoor
        {
            public Transform t;
            public Vector3 pos;
            public Quaternion rot;
        }

        HeldDoor _heldDoor;
        bool _holdingDoor;
        bool _bothInside;
        bool _suppressJump;
        int _hitsA;
        int _hitsB;
        bool _wasJumpA;
        bool _wasJumpB;
        bool _ignoreJumpA;
        bool _ignoreJumpB;
        bool _finishing;
        int _seed;
        float _startTime;
        float _zoneA = 0.5f;
        float _zoneB = 0.5f;
        NavMeshObstacle _doorObstacle;
        LatchDoorMarker _doorMarker;

        void Reset()
        {
            EnsureTrigger();
        }

        void OnEnable()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);
        }

        void OnDisable()
        {
            Instances.Remove(this);
            if (IsPlaying && !_finishing && !IsCompleted)
                CancelMinigame(broadcast: false);
            if (!IsPlaying && !IsCompleted)
                _suppressJump = false;
        }

        void Awake()
        {
            EnsureTrigger();
            ResolveHorses();
            EnsureDoorObstacle();
        }

        void EnsureTrigger()
        {
            var cols = GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].isTrigger)
                    return;
            }

            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(6f, 4f, 6f);
            box.center = new Vector3(0f, 1.5f, 0f);
            box.isTrigger = true;
        }

        void EnsureDoorObstacle()
        {
            if (stallDoor == null)
                return;

            _doorObstacle = stallDoor.GetComponent<NavMeshObstacle>();
            if (_doorObstacle == null)
                _doorObstacle = stallDoor.gameObject.AddComponent<NavMeshObstacle>();

            _doorObstacle.shape = NavMeshObstacleShape.Box;
            _doorObstacle.carving = true;
            _doorObstacle.carveOnlyStationary = false;
            _doorObstacle.size = new Vector3(1.4f, 2.6f, 0.5f);
            _doorObstacle.center = new Vector3(0f, 1.2f, 0f);
            _doorObstacle.enabled = true;
        }

        void OpenDoorOnNavMesh()
        {
            if (_doorObstacle != null)
                _doorObstacle.enabled = false;
        }

        void ResolveHorses()
        {
            if (horseA == null)
                horseA = FindAnimalByName("Horse Realistic");
            if (horseB == null)
                horseB = FindAnimalByName("Horse Unicorn");
        }

        public void Unlock()
        {
            if (IsCompleted)
                return;
            IsUnlocked = true;
            RefreshDoorMarker();
        }

        void OnTriggerEnter(Collider other)
        {
            Track(other, add: true);
        }

        void OnTriggerStay(Collider other)
        {
            Track(other, add: true);
        }

        void OnTriggerExit(Collider other)
        {
            Track(other, add: false);
        }

        void Track(Collider other, bool add)
        {
            if (other == null)
                return;

            var animal = other.GetComponentInParent<MAnimal>();
            if (animal == null)
                return;

            if (animal == horseA)
            {
                if (add) _insideA.Add(other);
                else _insideA.Remove(other);
            }
            else if (animal == horseB)
            {
                if (add) _insideB.Add(other);
                else _insideB.Remove(other);
            }
        }

        void LateUpdate()
        {
            if (_holdingDoor && _heldDoor.t != null)
                _heldDoor.t.SetPositionAndRotation(_heldDoor.pos, _heldDoor.rot);
        }

        void Update()
        {
            if (horseA == null || horseB == null)
                ResolveHorses();

            PruneInside(_insideA);
            PruneInside(_insideB);
            _bothInside = _insideA.Count > 0 && _insideB.Count > 0;
            _suppressJump = !IsCompleted && IsUnlocked && (_bothInside || IsPlaying);
            RefreshDoorMarker();

            if (IsCompleted || _finishing)
            {
                HideHudIfIdle();
                return;
            }

            if (CoopSavePoint.IsBusy && !IsPlaying)
                return;

            if (!HasGameplayAuthority())
            {
                if (IsPlaying)
                    RenderBars();
                else if (IsUnlocked && _bothInside)
                    ShowPrompt();
                else
                    HideHudIfIdle();
                return;
            }

            if (!IsUnlocked)
            {
                HideHudIfIdle();
                return;
            }

            if (IsPlaying)
            {
                if (!_bothInside)
                {
                    CancelMinigame(broadcast: true);
                    return;
                }

                TickMinigame();
                RenderBars();
                return;
            }

            if (!_bothInside)
            {
                HideHudIfIdle();
                return;
            }

            ShowPrompt();
            if (PressedStart())
                BeginMinigame();
        }

        bool PressedStart()
        {
            bool a = ReadJumpA();
            bool b = ReadJumpB();
            bool edgeA = a && !_wasJumpA;
            bool edgeB = b && !_wasJumpB;
            _wasJumpA = a;
            _wasJumpB = b;
            return edgeA || edgeB;
        }

        void BeginMinigame()
        {
            _seed = UnityEngine.Random.Range(1, int.MaxValue);
            _startTime = SyncTime();
            _zoneA = RandomZone();
            _zoneB = RandomZone();
            _hitsA = 0;
            _hitsB = 0;
            _ignoreJumpA = ReadJumpA();
            _ignoreJumpB = ReadJumpB();
            _wasJumpA = _ignoreJumpA;
            _wasJumpB = _ignoreJumpB;
            IsPlaying = true;
            _suppressJump = true;

            var hud = StableTimingHud.Ensure();
            hud.ShowBars();
            RenderBars();

            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastStableTimingStart(SyncId, _seed, _startTime, _zoneA, _zoneB, zoneWidth, markerSpeed);
        }

        void TickMinigame()
        {
            bool jumpA = ReadJumpA();
            bool jumpB = ReadJumpB();

            if (_ignoreJumpA)
            {
                if (!jumpA)
                    _ignoreJumpA = false;
            }
            else if (jumpA && !_wasJumpA && !PlayerDone(_hitsA))
            {
                TryHit(playerA: true);
            }

            if (_ignoreJumpB)
            {
                if (!jumpB)
                    _ignoreJumpB = false;
            }
            else if (jumpB && !_wasJumpB && !PlayerDone(_hitsB))
            {
                TryHit(playerA: false);
            }

            _wasJumpA = jumpA;
            _wasJumpB = jumpB;
        }

        void TryHit(bool playerA)
        {
            float marker = Marker01(playerA);
            float zone = playerA ? _zoneA : _zoneB;
            float half = Mathf.Max(0.04f, zoneWidth) * 0.5f;
            bool hit = Mathf.Abs(marker - zone) <= half;

            if (hit)
            {
                if (playerA) _hitsA = Mathf.Min(HitsNeeded, _hitsA + 1);
                else _hitsB = Mathf.Min(HitsNeeded, _hitsB + 1);
            }

            if (playerA && !PlayerDone(_hitsA))
                _zoneA = RandomZone();
            else if (!playerA && !PlayerDone(_hitsB))
                _zoneB = RandomZone();

            BroadcastFlags();

            if (PlayerDone(_hitsA) && PlayerDone(_hitsB))
                FinishSuccess();
        }

        int HitsNeeded => Mathf.Max(1, hitsNeeded);

        bool PlayerDone(int hits) => hits >= HitsNeeded;

        void BroadcastFlags()
        {
            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastStableTimingFlags(SyncId, _hitsA, _hitsB, _zoneA, _zoneB);
        }

        void FinishSuccess()
        {
            if (IsCompleted || _finishing)
                return;

            _finishing = true;
            IsPlaying = false;
            _suppressJump = false;
            StableTimingHud.Ensure().Hide();

            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastStableTimingEnd(SyncId, completed: true);

            StartCoroutine(OpenStallAndRelease());
        }

        void CancelMinigame(bool broadcast)
        {
            IsPlaying = false;
            _hitsA = 0;
            _hitsB = 0;
            _ignoreJumpA = false;
            _ignoreJumpB = false;
            StableTimingHud.Ensure().Hide();

            if (broadcast)
            {
                var coop = CoopGameController.Instance;
                if (coop != null)
                    coop.BroadcastStableTimingEnd(SyncId, completed: false);
            }
        }

        IEnumerator OpenStallAndRelease()
        {
            DoorOpened?.Invoke();
            OpenDoorOnNavMesh();
            if (stallDoor != null)
            {
                yield return EnemyBatchDoors.AnimateSwingOrRaise(
                    stallDoor, swingYaw, raiseHeight, openSeconds, CaliSoundIds.LatchDoor);
                _heldDoor = new HeldDoor
                {
                    t = stallDoor,
                    pos = stallDoor.position,
                    rot = stallDoor.rotation
                };
                _holdingDoor = true;
            }

            ReleaseNpcs();
            MarkCompleted();
            RefreshDoorMarker();

            // DoorOpened starts the LatchDoorOpened camera on this frame; wait until
            // that cue (and its blend-out) is actually over before the overlay.
            yield return null;
            while (CaliAchievementCamera.IsPlaying)
                yield return null;

            Cali.UI.LevelClearedHud.Show();
        }

        void MarkCompleted()
        {
            if (IsCompleted)
                return;

            IsCompleted = true;
            _finishing = true;
            IsPlaying = false;
            _suppressJump = false;
            Completed?.Invoke();
        }

        void ReleaseNpcs()
        {
            if (npcHorses == null)
                return;

            Transform dest = outsideTarget != null ? outsideTarget : transform;
            Vector3 right = dest.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();

            int count = npcHorses.Length;
            int live = 0;
            for (int i = 0; i < count; i++)
            {
                if (npcHorses[i] != null)
                    live++;
            }

            int slot = 0;
            for (int i = 0; i < count; i++)
            {
                var horse = npcHorses[i];
                if (horse == null)
                    continue;
                float side = live <= 1 ? 0f : (slot - (live - 1) * 0.5f) * horseSideSpacing;
                horse.Release(stallDoor, dest, right * side, slot * 0.45f);
                slot++;
            }

            CaliNpcHorse.IgnoreEachOther(npcHorses);
        }

        void ShowPrompt()
        {
            if (IsAnyPlaying() || CoopSavePoint.IsBusy)
                return;
            StableTimingHud.Ensure().ShowPrompt("Press Jump to unlatch\nP1 Space   ·   P2 Right Ctrl / Numpad 0");
        }

        void HideHudIfIdle()
        {
            if (IsAnyPlaying() || CoopSavePoint.IsBusy)
                return;
            if (StableTimingHud.Instance != null)
                StableTimingHud.Instance.Hide();
        }

        public static bool IsAnyPlaying()
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                if (Instances[i] != null && Instances[i].IsPlaying)
                    return true;
            }

            return false;
        }

        void RenderBars()
        {
            StableTimingHud.Ensure().Render(
                Marker01(true),
                Marker01(false),
                _zoneA,
                _zoneB,
                Mathf.Max(0.04f, zoneWidth),
                _hitsA,
                _hitsB,
                HitsNeeded);
        }

        public float Marker01(bool playerA)
        {
            float speed = Mathf.Max(0.2f, markerSpeed);
            float offset = playerA ? 0f : 0.17f + (_seed % 100) * 0.0017f;
            float t = (SyncTime() - _startTime) * speed + offset;
            return Mathf.PingPong(t, 1f);
        }

        float SyncTime()
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid && coop.Runner != null)
                return (float)coop.Runner.SimulationTime;
            return Time.unscaledTime;
        }

        static float RandomZone()
        {
            return UnityEngine.Random.Range(0.18f, 0.82f);
        }

        bool ReadJumpA()
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
                return coop.JumpHeldA;
            return CoopInput.ReadJumpHeld();
        }

        bool ReadJumpB()
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
                return coop.JumpHeldB;
            return CoopInput.ReadJumpHeldB();
        }

        static bool HasGameplayAuthority()
        {
            var coop = CoopGameController.Instance;
            if (coop == null || coop.Object == null || !coop.Object.IsValid)
                return true;
            return coop.HasStateAuthority;
        }

        void RefreshDoorMarker()
        {
            bool show = IsUnlocked && !IsCompleted && !_finishing && !IsPlaying;
            if (show)
            {
                if (_doorMarker == null)
                    _doorMarker = LatchDoorMarker.Attach(stallDoor != null ? stallDoor : transform);
                _doorMarker.SetVisible(true);
            }
            else if (_doorMarker != null)
            {
                _doorMarker.SetVisible(false);
            }
        }

        public static StableLatch FindBySyncId(int id)
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                var latch = Instances[i];
                if (latch != null && latch.SyncId == id)
                    return latch;
            }

            var all = FindObjectsByType<StableLatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].SyncId == id)
                    return all[i];
            }

            return null;
        }

        public void ApplyRemoteStart(int seed, float startTime, float zoneA, float zoneB, float width, float speed)
        {
            if (IsCompleted)
                return;

            _seed = seed;
            _startTime = startTime;
            _zoneA = zoneA;
            _zoneB = zoneB;
            if (width > 0.01f)
                zoneWidth = width;
            if (speed > 0.01f)
                markerSpeed = speed;
            _hitsA = 0;
            _hitsB = 0;
            IsUnlocked = true;
            IsPlaying = true;
            _suppressJump = true;
            RefreshDoorMarker();
            RenderBars();
        }

        public void ApplyRemoteFlags(int hitsA, int hitsB, float zoneA, float zoneB)
        {
            _hitsA = hitsA;
            _hitsB = hitsB;
            _zoneA = zoneA;
            _zoneB = zoneB;
            if (IsPlaying)
                RenderBars();
        }

        public void ApplyRemoteEnd(bool completed)
        {
            if (completed)
            {
                if (IsCompleted || _finishing)
                    return;

                _finishing = true;
                IsPlaying = false;
                _suppressJump = false;
                _hitsA = HitsNeeded;
                _hitsB = HitsNeeded;
                StableTimingHud.Ensure().Hide();
                StartCoroutine(OpenStallAndRelease());
                return;
            }

            if (_finishing || IsCompleted)
                return;
            CancelMinigame(broadcast: false);
        }

        static void PruneInside(HashSet<Collider> set)
        {
            set.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        }

        static MAnimal FindAnimalByName(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name == objectName)
                    return animals[i];
            }

            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name.Contains(objectName))
                    return animals[i];
            }

            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.7f, 0.2f, 0.35f);
            var col = GetComponent<BoxCollider>();
            if (col != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(col.center, col.size);
            }

            if (outsideTarget != null)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(outsideTarget.position, 0.6f);
                Gizmos.DrawLine(transform.position, outsideTarget.position);
            }
        }
    }
}
