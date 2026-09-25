using Cali.Combat;
using Cali.Network;
using Cali.UI;
using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Gameplay
{
    /// <summary>
    /// Repeatable coop checkpoint: Q (P for offline horse B) offers an 8s confirm,
    /// then both play the latch timing minigame. Success overwrites PlayerPrefs.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class CoopSavePoint : MonoBehaviour
    {
        public const int TimingSyncId = -9001;
        public const float OfferSeconds = 8f;
        public const int HitsNeeded = 3;
        const float SavedFlashSeconds = 2f;

        const string PrefValid = "Cali.CoopSave.Valid";
        const string PrefScene = "Cali.CoopSave.Scene";
        const string PrefAX = "Cali.CoopSave.AX";
        const string PrefAY = "Cali.CoopSave.AY";
        const string PrefAZ = "Cali.CoopSave.AZ";
        const string PrefAQx = "Cali.CoopSave.AQx";
        const string PrefAQy = "Cali.CoopSave.AQy";
        const string PrefAQz = "Cali.CoopSave.AQz";
        const string PrefAQw = "Cali.CoopSave.AQw";
        const string PrefBX = "Cali.CoopSave.BX";
        const string PrefBY = "Cali.CoopSave.BY";
        const string PrefBZ = "Cali.CoopSave.BZ";
        const string PrefBQx = "Cali.CoopSave.BQx";
        const string PrefBQy = "Cali.CoopSave.BQy";
        const string PrefBQz = "Cali.CoopSave.BQz";
        const string PrefBQw = "Cali.CoopSave.BQw";
        const string PrefXp = "Cali.CoopSave.Xp";
        const string PrefLevel = "Cali.CoopSave.Level";
        const string PrefDead0 = "Cali.CoopSave.Dead0";
        const string PrefDead1 = "Cali.CoopSave.Dead1";

        enum Phase
        {
            Idle,
            Offer,
            Minigame,
        }

        public static CoopSavePoint Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            string n = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(n) &&
                (n.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 n.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0))
                return;
            Ensure();
        }

        public static CoopSavePoint Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<CoopSavePoint>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("<<<Coop Save Point>>>");
            return go.AddComponent<CoopSavePoint>();
        }

        public static bool IsBusy => Instance != null && Instance._phase != Phase.Idle;

        public static bool SuppressHorseJump => Instance != null && Instance._suppressJump;

        static bool _hasStart;
        static string _startScene;
        static Vector3 _startPosA;
        static Vector3 _startPosB;
        static Quaternion _startRotA;
        static Quaternion _startRotB;

        Phase _phase = Phase.Idle;
        int _requesterSeat;
        float _offerEndTime;
        float _savedUntil;
        bool _suppressJump;

        int _seed;
        float _startTime;
        float _zoneA = 0.5f;
        float _zoneB = 0.5f;
        float _zoneWidth = StableLatch.DefaultZoneWidth;
        float _markerSpeed = StableLatch.DefaultMarkerSpeed;
        int _hitsA;
        int _hitsB;
        bool _wasJumpA;
        bool _wasJumpB;
        bool _ignoreJumpA;
        bool _ignoreJumpB;
        bool _playing;

        void OnEnable()
        {
            Instance = this;
            CaptureLevelStartIfUnset();
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            _suppressJump = false;
            _playing = false;
            _phase = Phase.Idle;
        }

        void Start()
        {
            CaptureLevelStartIfUnset();
            if (CoopSessionStarter.IsOnline)
                return;
            TryApplyToCurrentMatch();
        }

        void Update()
        {
            TickLocalSaveInput();

            if (_savedUntil > 0f)
            {
                if (Time.unscaledTime >= _savedUntil)
                {
                    _savedUntil = 0f;
                    if (_phase == Phase.Idle && !StableLatch.IsAnyPlaying())
                        StableTimingHud.Ensure().Hide();
                }
                else if (_phase == Phase.Idle)
                {
                    StableTimingHud.Ensure().ShowPrompt("Progress saved");
                }
            }

            if (_phase == Phase.Offer)
                TickOffer();
            else if (_phase == Phase.Minigame)
                TickMinigameFrame();
        }

        void TickLocalSaveInput()
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid && coop.IsWaitingForMatch)
                return;
            if (StableLatch.IsAnyPlaying())
                return;

            bool twoOnline = coop != null
                             && coop.Object != null
                             && coop.Object.IsValid
                             && coop.Runner != null
                             && coop.Runner.SessionInfo.PlayerCount > 1;

            if (CoopInput.ReadSavePressed())
                RequestFromLocalSeat(twoOnline ? LocalSeat() : 0);

            if (!twoOnline && CoopInput.ReadSavePressedB())
                RequestFromLocalSeat(1);
        }

        void RequestFromLocalSeat(int seat)
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
            {
                coop.RequestSaveFromLocal(seat);
                return;
            }

            HandleSaveRequest(seat);
        }

        int LocalSeat()
        {
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Runner != null)
                return CoopInput.GetLocalSeat(coop.Runner);
            return 0;
        }

        public void HandleSaveRequest(int seat)
        {
            if (!HasGameplayAuthority())
                return;
            if (StableLatch.IsAnyPlaying())
                return;
            if (_phase == Phase.Minigame)
                return;

            if (_phase == Phase.Idle)
            {
                BeginOffer(seat, broadcast: true);
                return;
            }

            if (_phase == Phase.Offer && seat != _requesterSeat)
                BeginMinigame(broadcast: true);
        }

        public void ApplyRemoteOffer(int requesterSeat, float endTime)
        {
            if (HasGameplayAuthority())
                return;
            _requesterSeat = requesterSeat;
            _offerEndTime = endTime;
            _phase = Phase.Offer;
            _playing = false;
            _suppressJump = false;
            _savedUntil = 0f;
        }

        public void ApplyRemoteOfferExpired()
        {
            if (HasGameplayAuthority())
                return;
            if (_phase != Phase.Offer)
                return;
            _phase = Phase.Idle;
            if (!StableLatch.IsAnyPlaying())
                StableTimingHud.Ensure().Hide();
        }

        public void ApplyRemoteStart(int seed, float startTime, float zoneA, float zoneB, float width, float speed)
        {
            _seed = seed;
            _startTime = startTime;
            _zoneA = zoneA;
            _zoneB = zoneB;
            if (width > 0.01f)
                _zoneWidth = width;
            if (speed > 0.01f)
                _markerSpeed = speed;
            _hitsA = 0;
            _hitsB = 0;
            _phase = Phase.Minigame;
            _playing = true;
            _suppressJump = true;
            _savedUntil = 0f;
            StableTimingHud.Ensure().ShowBars();
            RenderBars();
        }

        public void ApplyRemoteFlags(int hitsA, int hitsB, float zoneA, float zoneB)
        {
            _hitsA = hitsA;
            _hitsB = hitsB;
            _zoneA = zoneA;
            _zoneB = zoneB;
            if (_playing)
                RenderBars();
        }

        public void ApplyRemoteEnd(bool completed)
        {
            _playing = false;
            _suppressJump = false;
            _phase = Phase.Idle;
            if (completed)
                ShowSavedFlash();
            else if (!StableLatch.IsAnyPlaying())
                StableTimingHud.Ensure().Hide();
        }

        void BeginOffer(int seat, bool broadcast)
        {
            _requesterSeat = seat;
            _offerEndTime = SyncTime() + OfferSeconds;
            _phase = Phase.Offer;
            _savedUntil = 0f;
            if (broadcast)
            {
                var coop = CoopGameController.Instance;
                if (coop != null)
                    coop.BroadcastSaveOffer(seat, _offerEndTime);
            }
        }

        void TickOffer()
        {
            float remaining = _offerEndTime - SyncTime();
            if (HasGameplayAuthority() && remaining <= 0f)
            {
                ExpireOffer();
                return;
            }

            int secs = Mathf.Max(0, Mathf.CeilToInt(remaining));
            bool localIsRequester = LocalSeat() == _requesterSeat;
            bool twoOnline = CoopSessionStarter.IsOnline
                             && CoopSessionStarter.Runner != null
                             && CoopSessionStarter.Runner.SessionInfo.PlayerCount > 1;
            string line;
            if (localIsRequester)
                line = twoOnline
                    ? "Waiting for the other player…"
                    : "Waiting for the other player…\nP2 press P";
            else
                line = twoOnline
                    ? "the other player wants to take a save point\nPress Q"
                    : "the other player wants to take a save point\nPress P";
            StableTimingHud.Ensure().ShowPrompt($"{line}\n{secs}");
        }

        void ExpireOffer()
        {
            _phase = Phase.Idle;
            if (!StableLatch.IsAnyPlaying())
                StableTimingHud.Ensure().Hide();
            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastSaveOfferExpired();
        }

        void BeginMinigame(bool broadcast)
        {
            _seed = Random.Range(1, int.MaxValue);
            _startTime = SyncTime();
            _zoneA = RandomZone();
            _zoneB = RandomZone();
            _zoneWidth = StableLatch.DefaultZoneWidth;
            _markerSpeed = StableLatch.DefaultMarkerSpeed;
            _hitsA = 0;
            _hitsB = 0;
            _ignoreJumpA = ReadJumpA();
            _ignoreJumpB = ReadJumpB();
            _wasJumpA = _ignoreJumpA;
            _wasJumpB = _ignoreJumpB;
            _phase = Phase.Minigame;
            _playing = true;
            _suppressJump = true;
            StableTimingHud.Ensure().ShowBars();
            RenderBars();

            if (!broadcast)
                return;

            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastSaveTimingStart(TimingSyncId, _seed, _startTime, _zoneA, _zoneB, _zoneWidth, _markerSpeed);
        }

        void TickMinigameFrame()
        {
            if (!HasGameplayAuthority())
            {
                if (_playing)
                    RenderBars();
                return;
            }

            TickMinigameHits();
            RenderBars();
        }

        void TickMinigameHits()
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
            float half = Mathf.Max(0.04f, _zoneWidth) * 0.5f;
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

            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastSaveTimingFlags(TimingSyncId, _hitsA, _hitsB, _zoneA, _zoneB);

            if (PlayerDone(_hitsA) && PlayerDone(_hitsB))
                FinishSuccess();
        }

        bool PlayerDone(int hits) => hits >= HitsNeeded;

        void FinishSuccess()
        {
            WriteCheckpoint();
            _playing = false;
            _suppressJump = false;
            _phase = Phase.Idle;
            ShowSavedFlash();

            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastSaveTimingEnd(TimingSyncId, completed: true);
        }

        void ShowSavedFlash()
        {
            _savedUntil = Time.unscaledTime + SavedFlashSeconds;
            StableTimingHud.Ensure().ShowPrompt("Progress saved");
        }

        void RenderBars()
        {
            StableTimingHud.Ensure().Render(
                Marker01(true),
                Marker01(false),
                _zoneA,
                _zoneB,
                Mathf.Max(0.04f, _zoneWidth),
                _hitsA,
                _hitsB,
                HitsNeeded);
        }

        float Marker01(bool playerA)
        {
            float speed = Mathf.Max(0.2f, _markerSpeed);
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
            return Random.Range(0.18f, 0.82f);
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

        /// <summary>Write a checkpoint at the horses' current poses. Used by the pause menu.</summary>
        public static bool TrySaveHere()
        {
            var save = Ensure();
            if (save == null)
                return false;
            if (!save.WriteCheckpoint())
                return false;
            save.ShowSavedFlash();
            return true;
        }

        bool WriteCheckpoint()
        {
            ResolveHorses(out var horseA, out var horseB);
            if (horseA == null || horseB == null)
            {
                Debug.LogWarning("[CoopSavePoint] Cannot save — missing horse A or B.", this);
                return false;
            }

            int xp = 0;
            int level = 1;
            int dead0 = 0;
            int dead1 = 0;
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
            {
                xp = coop.SharedXp;
                level = Mathf.Max(1, coop.SharedLevel);
                dead0 = coop.EnemyDeadBits0;
                dead1 = coop.EnemyDeadBits1;
            }
            else
            {
                var pxp = PlayerXp.EnsureExists();
                xp = pxp.Xp;
                level = Mathf.Max(1, pxp.Level);
            }

            string scene = SceneManager.GetActiveScene().name;
            PlayerPrefs.SetInt(PrefValid, 1);
            PlayerPrefs.SetString(PrefScene, scene);
            WritePose(PrefAX, PrefAY, PrefAZ, PrefAQx, PrefAQy, PrefAQz, PrefAQw, horseA.transform);
            WritePose(PrefBX, PrefBY, PrefBZ, PrefBQx, PrefBQy, PrefBQz, PrefBQw, horseB.transform);
            PlayerPrefs.SetInt(PrefXp, xp);
            PlayerPrefs.SetInt(PrefLevel, level);
            PlayerPrefs.SetInt(PrefDead0, dead0);
            PlayerPrefs.SetInt(PrefDead1, dead1);
            PlayerPrefs.Save();
            Debug.Log($"[CoopSavePoint] Saved {scene} at A={horseA.transform.position} B={horseB.transform.position}", this);
            return true;
        }

        [ContextMenu("Reset Coop Save")]
        void ResetCoopSaveFromInspector()
        {
            ClearCheckpoint();
            Debug.Log("[CoopSavePoint] Checkpoint cleared.", this);
        }

        public static void InvalidateLevelStart()
        {
            _hasStart = false;
            _startScene = null;
        }

        /// <summary>
        /// A new hosted room always starts at the scene spawn. Mid-session Q-saves still
        /// work for death until the next Host.
        /// </summary>
        public static void ResetForNewHostSession()
        {
            ClearCheckpoint();
            InvalidateLevelStart();
        }

        public static void ClearCheckpoint()
        {
            PlayerPrefs.DeleteKey(PrefValid);
            PlayerPrefs.DeleteKey(PrefScene);
            PlayerPrefs.DeleteKey(PrefAX);
            PlayerPrefs.DeleteKey(PrefAY);
            PlayerPrefs.DeleteKey(PrefAZ);
            PlayerPrefs.DeleteKey(PrefAQx);
            PlayerPrefs.DeleteKey(PrefAQy);
            PlayerPrefs.DeleteKey(PrefAQz);
            PlayerPrefs.DeleteKey(PrefAQw);
            PlayerPrefs.DeleteKey(PrefBX);
            PlayerPrefs.DeleteKey(PrefBY);
            PlayerPrefs.DeleteKey(PrefBZ);
            PlayerPrefs.DeleteKey(PrefBQx);
            PlayerPrefs.DeleteKey(PrefBQy);
            PlayerPrefs.DeleteKey(PrefBQz);
            PlayerPrefs.DeleteKey(PrefBQw);
            PlayerPrefs.DeleteKey(PrefXp);
            PlayerPrefs.DeleteKey(PrefLevel);
            PlayerPrefs.DeleteKey(PrefDead0);
            PlayerPrefs.DeleteKey(PrefDead1);
            PlayerPrefs.Save();
        }

        public static bool TryApplyToCurrentMatch()
        {
            if (!TryRead(out var data))
                return false;
            if (data.Scene != SceneManager.GetActiveScene().name)
                return false;

            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
            {
                if (!coop.HasStateAuthority)
                    return false;
                coop.ApplyCheckpoint(data);
            }
            else
            {
                ApplyLocal(data);
            }

            return true;
        }

        /// <summary>
        /// Remember where the horses started this scene, before any checkpoint is applied.
        /// Death uses this when there is no save yet.
        /// </summary>
        public static void CaptureLevelStartIfUnset()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene))
                return;
            if (_hasStart && _startScene != scene)
                _hasStart = false;
            if (_hasStart)
                return;

            CaptureLevelStartNow();
        }

        /// <summary>Overwrite the level-start pose from the horses' current positions.</summary>
        public static void CaptureLevelStartNow()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene))
                return;

            ResolveHorses(out var horseA, out var horseB);
            if (horseA == null)
            {
                var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (animals.Length > 0)
                    horseA = animals[0];
            }
            if (horseA == null)
                return;
            if (horseB == null && !CaliSinglePlayer.IsActiveScene)
                return;

            _startPosA = horseA.transform.position;
            _startRotA = horseA.transform.rotation;
            if (horseB != null)
            {
                _startPosB = horseB.transform.position;
                _startRotB = horseB.transform.rotation;
            }
            else
            {
                _startPosB = _startPosA;
                _startRotB = _startRotA;
            }
            _startScene = scene;
            _hasStart = true;
        }

        /// <summary>Last checkpoint for this scene, or the level start if none exists.</summary>
        public static bool TryRespawnAfterDeath()
        {
            if (TryRespawnToLastCheckpoint())
                return true;
            return TryRespawnToLevelStart();
        }

        public static bool TryRespawnToLevelStart()
        {
            if (!_hasStart || _startScene != SceneManager.GetActiveScene().name)
                return false;
            return TeleportPair(_startPosA, _startRotA, _startPosB, _startRotB);
        }

        /// <summary>Teleport both horses to the last save for this scene. Does not rewind XP or kills.</summary>
        public static bool TryRespawnToLastCheckpoint()
        {
            if (!TryRead(out var data))
                return false;
            if (data.Scene != SceneManager.GetActiveScene().name)
                return false;

            return TeleportPair(data.PosA, data.RotA, data.PosB, data.RotB);
        }

        static bool TeleportPair(Vector3 posA, Quaternion rotA, Vector3 posB, Quaternion rotB)
        {
            RestorePlayHorseHealth();
            ResolveHorses(out var horseA, out var horseB);
            if (horseB != null)
                FitPairToChain(posA, rotA, posB, rotB, out posA, out rotA, out posB, out rotB);

            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
            {
                if (!coop.HasStateAuthority)
                    return false;
                coop.TeleportPlayHorses(posA, rotA, posB, rotB);
                SnapChainAfterTeleport();
                return true;
            }

            if (horseA == null && horseB == null)
                return false;
            PlaceHorse(horseA, posA, rotA);
            PlaceHorse(horseB, posB, rotB);
            SnapChainAfterTeleport();
            return true;
        }

        public static void RestorePlayHorseHealth()
        {
            ResolveHorses(out var horseA, out var horseB);
            FillHealth(horseA);
            FillHealth(horseB);
        }

        static void FillHealth(MAnimal animal)
        {
            if (animal == null)
                return;
            var health = animal.GetComponent<Cali.Combat.HorseHealth>()
                         ?? animal.GetComponentInParent<Cali.Combat.HorseHealth>();
            if (health != null)
                health.SetMaxHealth(health.maxHealth, fill: true);
        }

        /// <summary>Keep the pair within chain length so they always come back as one.</summary>
        public static void FitPairToChain(
            Vector3 posA, Quaternion rotA, Vector3 posB, Quaternion rotB,
            out Vector3 outA, out Quaternion outARot, out Vector3 outB, out Quaternion outBRot)
        {
            outA = posA;
            outARot = rotA;
            outBRot = rotB;

            float max = 6f;
            var chain = SoftHorseChain.Instance;
            if (chain != null && chain.maxLength > 0.5f)
                max = chain.maxLength;

            Vector3 delta = posB - posA;
            float sep = delta.magnitude;
            float together = Mathf.Clamp(max * 0.35f, 1.6f, 2.4f);

            if (sep < 0.4f)
            {
                Vector3 side = rotA * Vector3.right;
                side.y = 0f;
                if (side.sqrMagnitude < 0.0001f)
                    side = Vector3.right;
                outB = posA + side.normalized * together;
            }
            else if (sep > max * 0.9f)
            {
                outB = posA + delta * (together / sep);
            }
            else
            {
                outB = posB;
            }
        }

        /// <summary>After a teleport, snap the visible rope and forget stretch so it does not yank.</summary>
        public static void SnapChainAfterTeleport()
        {
            RestorePlayHorseHealth();

            var chain = SoftHorseChain.Instance;
            if (chain != null)
                chain.SnapToCurrentHorses();

            var netA = Cali.Network.NetHorse.Find(0);
            netA?.Tether?.ClearMotion();
            var netB = Cali.Network.NetHorse.Find(1);
            netB?.Tether?.ClearMotion();
        }

        static void ApplyLocal(Checkpoint data)
        {
            ResolveHorses(out var horseA, out var horseB);
            PlaceHorse(horseA, data.PosA, data.RotA);
            PlaceHorse(horseB, data.PosB, data.RotB);

            var pxp = PlayerXp.EnsureExists();
            pxp.SetProgress(data.Xp, data.Level);

            ChainKillableEnemy.AssignStableIds();
            ApplyDeadBits(data.Dead0, 1);
            ApplyDeadBits(data.Dead1, 33);
        }

        static void ApplyDeadBits(int mask, int startId)
        {
            if (mask == 0)
                return;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                ChainKillableEnemy.ApplyRemoteDeath(startId + i, Vector3.zero, Vector3.forward);
            }
        }

        static bool TryRead(out Checkpoint data)
        {
            data = default;
            if (PlayerPrefs.GetInt(PrefValid, 0) != 1)
                return false;

            data.Scene = PlayerPrefs.GetString(PrefScene, "");
            if (string.IsNullOrEmpty(data.Scene))
                return false;

            data.PosA = ReadVec(PrefAX, PrefAY, PrefAZ);
            data.RotA = ReadQuat(PrefAQx, PrefAQy, PrefAQz, PrefAQw);
            data.PosB = ReadVec(PrefBX, PrefBY, PrefBZ);
            data.RotB = ReadQuat(PrefBQx, PrefBQy, PrefBQz, PrefBQw);
            data.Xp = PlayerPrefs.GetInt(PrefXp, 0);
            data.Level = Mathf.Max(1, PlayerPrefs.GetInt(PrefLevel, 1));
            data.Dead0 = PlayerPrefs.GetInt(PrefDead0, 0);
            data.Dead1 = PlayerPrefs.GetInt(PrefDead1, 0);
            return true;
        }

        static void WritePose(string x, string y, string z, string qx, string qy, string qz, string qw, Transform t)
        {
            PlayerPrefs.SetFloat(x, t.position.x);
            PlayerPrefs.SetFloat(y, t.position.y);
            PlayerPrefs.SetFloat(z, t.position.z);
            PlayerPrefs.SetFloat(qx, t.rotation.x);
            PlayerPrefs.SetFloat(qy, t.rotation.y);
            PlayerPrefs.SetFloat(qz, t.rotation.z);
            PlayerPrefs.SetFloat(qw, t.rotation.w);
        }

        static Vector3 ReadVec(string x, string y, string z)
        {
            return new Vector3(PlayerPrefs.GetFloat(x, 0f), PlayerPrefs.GetFloat(y, 0f), PlayerPrefs.GetFloat(z, 0f));
        }

        static Quaternion ReadQuat(string x, string y, string z, string w)
        {
            var q = new Quaternion(
                PlayerPrefs.GetFloat(x, 0f),
                PlayerPrefs.GetFloat(y, 0f),
                PlayerPrefs.GetFloat(z, 0f),
                PlayerPrefs.GetFloat(w, 1f));
            float mag2 = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return mag2 < 0.0001f ? Quaternion.identity : q.normalized;
        }

        static void ResolveHorses(out MAnimal horseA, out MAnimal horseB)
        {
            horseA = null;
            horseB = null;
            var coop = CoopGameController.Instance;
            if (coop != null)
            {
                horseA = coop.PlayHorseA;
                horseB = coop.PlayHorseB;
            }

            if (horseA == null)
                horseA = CoopGameController.FindPlayAnimal("Horse Realistic")
                         ?? FindAnimalByName("Horse Realistic");
            if (horseB == null)
                horseB = CoopGameController.FindPlayAnimal("Horse Unicorn")
                         ?? FindAnimalByName("Horse Unicorn");
            if (CaliSinglePlayer.IsActiveScene
                || (horseB != null && !horseB.gameObject.activeInHierarchy))
                horseB = null;
        }

        public static void PlaceHorse(MAnimal animal, Vector3 pos, Quaternion rot)
        {
            if (animal == null)
                return;

            animal.Reset_Platform();
            animal.Rotation = rot;
            animal.Teleport(pos);
            animal.transform.SetPositionAndRotation(pos, rot);
            if (animal.RB != null)
            {
                animal.RB.position = pos;
                animal.RB.rotation = rot;
                animal.RB.linearVelocity = Vector3.zero;
                animal.RB.angularVelocity = Vector3.zero;
            }
        }

        static MAnimal FindAnimalByName(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name == objectName)
                    return animals[i];
            }

            return null;
        }

        public struct Checkpoint
        {
            public string Scene;
            public Vector3 PosA;
            public Quaternion RotA;
            public Vector3 PosB;
            public Quaternion RotB;
            public int Xp;
            public int Level;
            public int Dead0;
            public int Dead1;
        }
    }
}
