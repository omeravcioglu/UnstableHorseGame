using Cali.Gameplay;
using Fusion;
using MalbersAnimations.Controller;
using MalbersAnimations.Utilities;
using System.Text;
using UnityEngine;

namespace Cali.Network
{
    public struct NetPlatformPose : INetworkStruct
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    public struct NetEnemyPose : INetworkStruct
    {
        public int Id;
        public Vector3 Position;
        public float Yaw;
    }

    public struct NetHorseTeleport : INetworkStruct
    {
        public int Seq;
        public Vector3 PosA;
        public Quaternion RotA;
        public Vector3 PosB;
        public Quaternion RotB;
    }

    /// <summary>
    /// Shared-mode world state: Master Client owns match phase, platforms, crates, enemies.
    /// Each horse is its own NetworkObject with per-player State Authority.
    /// </summary>
    public class CoopGameController : NetworkBehaviour
    {
        const int MaxPlatforms = 24;
        const int MaxEnemyPoses = 48;
        const int MaxCrates = 32;
        const int MaxWaterBobs = 80;

        public static CoopGameController Instance { get; private set; }

        /// <summary>-1 = off, 0 = Realistic seat, 1 = Unicorn seat.</summary>
        [Networked] public int PegasusSeat { get; set; }
        [Networked] public float PegasusEndTime { get; set; }

        [Networked] public int PlatformCount { get; set; }
        [Networked, Capacity(MaxPlatforms)] public NetworkArray<NetPlatformPose> PlatformPoses => default;

        /// <summary>Chain-pushable crates are host-simulated Rigidbodies; clients only replicate them.</summary>
        [Networked] public int CrateCount { get; set; }
        [Networked, Capacity(MaxCrates)] public NetworkArray<NetPlatformPose> CratePoses => default;

        /// <summary>Host-simulated WaterBob / HorseBoat colliders. Clients freeze local bob.</summary>
        [Networked] public int WaterBobCount { get; set; }
        [Networked, Capacity(MaxWaterBobs)] public NetworkArray<NetPlatformPose> WaterBobPoses => default;

        [Networked] public int EnemyPoseCount { get; set; }
        [Networked, Capacity(MaxEnemyPoses)] public NetworkArray<NetEnemyPose> EnemyPoses => default;
        [Networked] public int EnemyDeadBits0 { get; set; }
        [Networked] public int EnemyDeadBits1 { get; set; }

        [Networked] public int SharedXp { get; set; }
        [Networked] public int SharedLevel { get; set; }

        /// <summary>0 = Waiting room, 1 = Playing, 2 = Intro cutscene.</summary>
        [Networked] public int MatchPhase { get; set; }
        [Networked] public NetworkBool IntroComplete { get; set; }

        /// <summary>
        /// Master-authored idle lock during achievement camera. Horses keep simulating
        /// (idle anims) — only WASD/arrows/jump/sprint are zeroed.
        /// </summary>
        [Networked] public NetworkBool CinematicFreeze { get; set; }

        /// <summary>
        /// Host-authored horse snap (dead zone, checkpoint). Each peer applies it to the
        /// horse it owns; relying on a per-horse StateAuthority RPC leaves the client behind.
        /// </summary>
        [Networked] public NetHorseTeleport HorseTeleport { get; set; }

        public const int PhaseWaiting = 0;
        public const int PhasePlaying = 1;
        public const int PhaseIntro = 2;

        public const int JumpStateId = 2; // Malbers StatesID/Jump.asset
        public const int FallStateId = 3; // Malbers StatesID/Fall.asset

        /// <summary>Master Start already happened — new Spawned() must stay in Playing after scene load.</summary>
        public static bool KeepPlayingAcrossLoad;

        public bool IsWorldAuthority => Object != null && Object.IsValid && HasStateAuthority;

        public bool IsMatchPlaying => MatchPhase == PhasePlaying;
        public bool AllowsHorseInput => MatchPhase == PhasePlaying && !CinematicFreeze;
        public bool IsWaitingForMatch => MatchPhase == PhaseWaiting;
        public bool IsInIntro => MatchPhase == PhaseIntro;
        public bool HasPlayHorses => _horseA != null && _horseB != null;
        public MAnimal LocalPlayHorse => GetLocalHorse();

        public bool JumpHeldA => JumpHeldForSeat(0, CoopInput.ReadJumpHeld);
        public bool JumpHeldB => JumpHeldForSeat(1, CoopInput.ReadJumpHeldB);

        public MAnimal PlayHorseA => _horseA;
        public MAnimal PlayHorseB => _horseB;

        static bool JumpHeldForSeat(int seat, System.Func<bool> offlineFallback)
        {
            var horse = NetHorse.Find(seat);
            if (horse == null)
                return offlineFallback();
            return horse.JumpHeld || horse.JumpHeldLocal;
        }

        MAnimal _horseA;
        MAnimal _horseB;
        SoftHorseChain _chain;
        LocalDualHorseInput _localInput;
        HorsePegasusForm _pegasusForm;

        Transform[] _platformObjects;
        MSimpleTransformer[] _platformMovers;
        ChainPushable[] _crates;
        Transform[] _waterBobObjects;
        WaterBob[] _waterBobScripts;
        HorseBoat[] _horseBoatScripts;

        /// <summary>
        /// Horse poses reach the client through Fusion's buffer interpolator, which renders
        /// a fraction of a tick behind the newest snapshot. Replicated scene objects have to
        /// be sampled on that same render tick, or a horse standing on a moving platform is
        /// drawn offset from the platform and appears to sink into or slide off it.
        /// </summary>
        const int SceneHistory = 8;

        /// <summary>One tick of replicated scene-object state, kept per snapshot tick.</summary>
        sealed class SceneFrame
        {
            public int Tick = int.MinValue;
            public int PlatformN;
            public int CrateN;
            public int WaterBobN;
            public int EnemyN;
            public readonly NetPlatformPose[] Platforms = new NetPlatformPose[MaxPlatforms];
            public readonly NetPlatformPose[] Crates = new NetPlatformPose[MaxCrates];
            public readonly NetPlatformPose[] WaterBobs = new NetPlatformPose[MaxWaterBobs];
            public readonly NetEnemyPose[] Enemies = new NetEnemyPose[MaxEnemyPoses];
        }

        SceneFrame[] _sceneHistory;
        SceneFrame _sceneResolved;
        int _sceneHead = -1;
        int _sceneFilled;
        int _sceneLastTick = int.MinValue;
        bool _sceneResolvedValid;

        int _cameraSeat = -1;
        int _localSeat;
        int _appliedPegasusSeat = -1;
        int _lastMatchPhase = -1;
        int _appliedHorseTeleportSeq;

        public override void Spawned()
        {
            Instance = this;
            PersistAcrossScenes();
            ResolveSceneRefs();
            BindScenePlatforms();
            BindSceneCrates();
            BindWaterBobs();
            BindChainToHorses();
            Cali.Combat.ChainKillableEnemy.AssignStableIds();

            if (_localInput != null)
                _localInput.enabled = false;
            LocalDualHorseInput.ClaimExclusiveControl();

            _localSeat = CoopInput.GetLocalSeat(Runner);
            _sceneHead = -1;
            _sceneFilled = 0;
            _sceneLastTick = int.MinValue;
            _sceneResolvedValid = false;
            _lastMatchPhase = MatchPhase;
            _cameraSeat = -1;

            if (IsWorldAuthority)
            {
                if (KeepPlayingAcrossLoad || MatchPhase == PhasePlaying || MatchPhase == PhaseIntro)
                {
                    MatchPhase = IntroComplete ? PhasePlaying : PhaseIntro;
                    if (SharedLevel < 1)
                        SharedLevel = 1;
                }
                else
                {
                    ResetWorldForNewMatch();
                }
            }

            _lastMatchPhase = MatchPhase;

            Cali.Combat.PlayerXp.EnsureExists();

            ApplyLocalCamera(force: true);
            SnapHorsesToLevelStart();
            ApplyNetworkedHorseTeleport();

            NetHorseSpawner.EnsureLocal(Runner);

            Debug.Log(
                $"[CoopGameController] Spawned (master={Runner.IsSharedModeMasterClient}, seat={_localSeat}, phase={MatchPhase}, A={_horseA?.name}, B={_horseB?.name})",
                this);
        }

        public void RebindSceneRefs()
        {
            _horseA = null;
            _horseB = null;
            _chain = null;
            _localInput = null;
            _pegasusForm = null;
            ResolveSceneRefs();
            BindScenePlatforms();
            BindSceneCrates();
            BindWaterBobs();
            BindChainToHorses();
            Cali.Combat.ChainKillableEnemy.AssignStableIds();
            LocalDualHorseInput.ClaimExclusiveControl();
            if (_localInput != null)
                _localInput.enabled = false;
            NetHorseSpawner.EnsureLocal(Runner);
            NetHorse.Find(0)?.Rebind();
            NetHorse.Find(1)?.Rebind();

            ApplyLocalCamera(force: true);
            SnapHorsesToLevelStart();
            ApplyNetworkedHorseTeleport();
        }

        public void EnsureMatchPlaying()
        {
            if (!IsWorldAuthority)
                return;
            MatchPhase = IntroComplete ? PhasePlaying : PhaseIntro;
        }

        public void FinishIntro()
        {
            if (!IsWorldAuthority)
                return;
            if (IntroComplete && MatchPhase == PhasePlaying)
            {
                RPC_IntroFinished();
                return;
            }

            IntroComplete = true;
            MatchPhase = PhasePlaying;
            SnapHorsesToLevelStart();
            Cali.UI.CaliIntroCutscene.NotifyHostFinished();
            RPC_IntroFinished();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_IntroFinished()
        {
            Cali.UI.CaliIntroCutscene.NotifyHostFinished();
        }

        void PersistAcrossScenes()
        {
            var starter = CoopSessionStarter.Instance;
            if (starter != null && transform.parent != starter.transform)
                transform.SetParent(starter.transform, true);
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Master-only: start the match. One player is enough for local WASD+arrows debug.</summary>
        public bool RequestStartMatch()
        {
            if (!IsWorldAuthority)
                return false;
            if (MatchPhase == PhasePlaying)
                return true;
            if (Runner == null || !Runner.IsRunning)
                return false;

            KeepPlayingAcrossLoad = true;
            ResetMatchProgress();
            bool solo = Runner.SessionInfo.PlayerCount < 2;
            IntroComplete = solo;
            MatchPhase = solo ? PhasePlaying : PhaseIntro;
            Debug.Log($"[CoopGameController] Match started — {(solo ? "solo dual keyboard" : "intro")}.", this);
            return true;
        }

        void ResetWorldForNewMatch()
        {
            MatchPhase = PhaseWaiting;
            IntroComplete = false;
            ResetMatchProgress();
        }

        void ResetMatchProgress()
        {
            PegasusSeat = -1;
            SharedXp = 0;
            SharedLevel = 1;
            EnemyDeadBits0 = 0;
            EnemyDeadBits1 = 0;
            HorseTeleport = default;
            _appliedHorseTeleportSeq = 0;
            CoopSavePoint.ResetForNewHostSession();
            Cali.UI.CaliIntroCutscene.ResetForNewSession();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;

            if (_localInput != null)
                _localInput.enabled = true;
            if (_chain != null)
                _chain.enabled = true;
        }

        public override void FixedUpdateNetwork()
        {
            NetHorseSpawner.Tick(Runner);

            if (!IsWorldAuthority)
                return;

            PublishWorldState();

            if (MatchPhase == PhasePlaying)
                ServerTickPegasus();
        }

        void Update()
        {
            if (Object == null || !Object.IsValid)
                return;

            // Seat/camera can be wrong on the first frames after join — keep correcting.
            int seat = CoopInput.GetLocalSeat(Runner);
            if (seat != _localSeat)
            {
                _localSeat = seat;
                ApplyLocalCamera(force: true);
            }

            if (MatchPhase != _lastMatchPhase)
            {
                _lastMatchPhase = MatchPhase;
                ApplyLocalCamera(force: true);
                if (MatchPhase == PhasePlaying)
                {
                    SnapHorsesToLevelStart();
                    Debug.Log($"[CoopGameController] Phase → Playing (localSeat={_localSeat})", this);
                }
            }

            ApplyLocalCamera(force: false);
            NetHorseSpawner.EnsureLocal(Runner);
            ApplyNetworkedHorseTeleport();
            EnsureChainBound();

            if (!IsWorldAuthority)
                ClientSyncPegasusForm();
        }

        void LateUpdate()
        {
            if (Object == null || !Object.IsValid)
                return;

            if (!IsWorldAuthority)
            {
                ApplyEnemyDeathBits();
                ApplySceneFrame();
            }
        }

        public override void Render()
        {
            if (IsWorldAuthority)
                return;

            var interp = new NetworkBehaviourBufferInterpolator(this);
            ApplyEnemyDeathBits();
            ResolveSceneFrame(interp);
            ApplySceneFrame();
        }

        public bool ServerActivatePegasus(MAnimal animal, float duration)
        {
            if (!IsWorldAuthority || _pegasusForm == null)
                return false;

            int seat = _pegasusForm.SeatOf(animal);
            if (seat < 0)
                return false;

            PegasusSeat = seat;
            PegasusEndTime = (float)Runner.SimulationTime + duration;
            _pegasusForm.Activate(seat, duration, useLocalTimer: false);
            _appliedPegasusSeat = seat;
            return true;
        }

        public void ServerAddXp(int amount, int baseXpPerLevel)
        {
            if (!IsWorldAuthority || amount <= 0)
                return;

            if (SharedLevel < 1)
                SharedLevel = 1;

            SharedXp += amount;
            int need = Mathf.Max(1, baseXpPerLevel * SharedLevel);
            while (SharedXp >= need)
            {
                SharedXp -= need;
                SharedLevel++;
                need = Mathf.Max(1, baseXpPerLevel * SharedLevel);
            }
        }

        public void ServerBroadcastEnemyDeath(int enemyId, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (!IsWorldAuthority)
                return;

            MarkEnemyDead(enemyId);
            RPC_EnemyDied(enemyId, hitPoint, hitDirection);
        }

        public void ServerBroadcastEnemyThrow(int enemyId, Vector3 origin, Vector3 direction)
        {
            if (!IsWorldAuthority)
                return;

            RPC_EnemyThrow(enemyId, origin, direction);
        }

        void MarkEnemyDead(int enemyId)
        {
            int i = enemyId - 1;
            if (i < 0 || i >= 64)
                return;
            if (i < 32)
                EnemyDeadBits0 |= 1 << i;
            else
                EnemyDeadBits1 |= 1 << (i - 32);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_EnemyDied(int enemyId, Vector3 hitPoint, Vector3 hitDirection)
        {
            Cali.Combat.ChainKillableEnemy.ApplyRemoteDeath(enemyId, hitPoint, hitDirection);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_EnemyThrow(int enemyId, Vector3 origin, Vector3 direction)
        {
            var enemy = Cali.Combat.ChainKillableEnemy.FindById(enemyId);
            if (enemy != null && !enemy.IsDead)
                enemy.PlayRemoteThrow(origin, direction);
        }

        public void BroadcastStableTimingStart(
            int latchSyncId,
            int seed,
            float startTime,
            float zoneA,
            float zoneB,
            float zoneWidth,
            float markerSpeed)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;

            RPC_StableTimingStart(latchSyncId, seed, startTime, zoneA, zoneB, zoneWidth, markerSpeed);
        }

        public void BroadcastStableTimingFlags(int latchSyncId, int hitsA, int hitsB, float zoneA, float zoneB)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;

            RPC_StableTimingFlags(latchSyncId, hitsA, hitsB, zoneA, zoneB);
        }

        public void BroadcastStableTimingEnd(int latchSyncId, bool completed)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;

            RPC_StableTimingEnd(latchSyncId, completed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_StableTimingStart(
            int latchSyncId,
            int seed,
            float startTime,
            float zoneA,
            float zoneB,
            float zoneWidth,
            float markerSpeed)
        {
            var latch = StableLatch.FindBySyncId(latchSyncId);
            if (latch != null)
                latch.ApplyRemoteStart(seed, startTime, zoneA, zoneB, zoneWidth, markerSpeed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_StableTimingFlags(int latchSyncId, int hitsA, int hitsB, float zoneA, float zoneB)
        {
            var latch = StableLatch.FindBySyncId(latchSyncId);
            if (latch != null)
                latch.ApplyRemoteFlags(hitsA, hitsB, zoneA, zoneB);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_StableTimingEnd(int latchSyncId, bool completed)
        {
            var latch = StableLatch.FindBySyncId(latchSyncId);
            if (latch != null)
                latch.ApplyRemoteEnd(completed);
        }

        public void RequestSaveFromLocal(int preferredSeat)
        {
            if (Object == null || !Object.IsValid)
                return;

            if (HasStateAuthority)
            {
                int seat = preferredSeat;
                if (Runner != null && Runner.SessionInfo.PlayerCount > 1)
                    seat = CoopInput.GetLocalSeat(Runner);
                CoopSavePoint.Ensure().HandleSaveRequest(seat);
                return;
            }

            RPC_RequestSave();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_RequestSave(RpcInfo info = default)
        {
            int seat = CoopInput.GetSeatForPlayer(Runner, info.Source);
            CoopSavePoint.Ensure().HandleSaveRequest(seat);
        }

        public void BroadcastSaveOffer(int requesterSeat, float endTime)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_SaveOffer(requesterSeat, endTime);
        }

        public void BroadcastSaveOfferExpired()
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_SaveOfferExpired();
        }

        public void BroadcastSaveTimingStart(
            int syncId,
            int seed,
            float startTime,
            float zoneA,
            float zoneB,
            float zoneWidth,
            float markerSpeed)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_SaveTimingStart(syncId, seed, startTime, zoneA, zoneB, zoneWidth, markerSpeed);
        }

        public void BroadcastSaveTimingFlags(int syncId, int hitsA, int hitsB, float zoneA, float zoneB)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_SaveTimingFlags(syncId, hitsA, hitsB, zoneA, zoneB);
        }

        public void BroadcastSaveTimingEnd(int syncId, bool completed)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_SaveTimingEnd(syncId, completed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_SaveOffer(int requesterSeat, float endTime)
        {
            var save = CoopSavePoint.Instance;
            if (save != null)
                save.ApplyRemoteOffer(requesterSeat, endTime);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_SaveOfferExpired()
        {
            var save = CoopSavePoint.Instance;
            if (save != null)
                save.ApplyRemoteOfferExpired();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_SaveTimingStart(
            int syncId,
            int seed,
            float startTime,
            float zoneA,
            float zoneB,
            float zoneWidth,
            float markerSpeed)
        {
            if (syncId != CoopSavePoint.TimingSyncId)
                return;
            var save = CoopSavePoint.Instance;
            if (save != null)
                save.ApplyRemoteStart(seed, startTime, zoneA, zoneB, zoneWidth, markerSpeed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_SaveTimingFlags(int syncId, int hitsA, int hitsB, float zoneA, float zoneB)
        {
            if (syncId != CoopSavePoint.TimingSyncId)
                return;
            var save = CoopSavePoint.Instance;
            if (save != null)
                save.ApplyRemoteFlags(hitsA, hitsB, zoneA, zoneB);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_SaveTimingEnd(int syncId, bool completed)
        {
            if (syncId != CoopSavePoint.TimingSyncId)
                return;
            var save = CoopSavePoint.Instance;
            if (save != null)
                save.ApplyRemoteEnd(completed);
        }

        public void SnapHorsesToLevelStart()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                != Cali.UI.CaliMainMenuController.GameSceneName)
                return;
            if (!HasPlayHorses)
                return;

            CoopSavePoint.CaptureLevelStartIfUnset();
            if (!IsWorldAuthority)
                return;

            CoopSavePoint.TryRespawnToLevelStart();
        }

        public void ApplyCheckpoint(CoopSavePoint.Checkpoint data)
        {
            SharedXp = Mathf.Max(0, data.Xp);
            SharedLevel = Mathf.Max(1, data.Level);
            EnemyDeadBits0 = data.Dead0;
            EnemyDeadBits1 = data.Dead1;
            ApplyEnemyDeathBits();
            TeleportPlayHorses(data.PosA, data.RotA, data.PosB, data.RotB);
            var xp = Cali.Combat.PlayerXp.Instance;
            if (xp != null)
                xp.NotifyChanged();
        }

        public void TeleportPlayHorses(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot)
        {
            int nextSeq = 1;
            if (Object != null && Object.IsValid)
                nextSeq = HorseTeleport.Seq + 1;

            var snap = new NetHorseTeleport
            {
                Seq = nextSeq,
                PosA = aPos,
                RotA = aRot,
                PosB = bPos,
                RotB = bRot
            };

            if (Object != null && Object.IsValid && HasStateAuthority)
            {
                HorseTeleport = snap;
                RPC_HorseTeleport(aPos, aRot, bPos, bRot, snap.Seq);
            }

            ApplyHorseTeleports(snap);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_HorseTeleport(Vector3 aPos, Quaternion aRot, Vector3 bPos, Quaternion bRot, int seq)
        {
            ApplyHorseTeleports(new NetHorseTeleport
            {
                Seq = seq,
                PosA = aPos,
                RotA = aRot,
                PosB = bPos,
                RotB = bRot
            });
        }

        void ApplyNetworkedHorseTeleport()
        {
            var snap = HorseTeleport;
            if (snap.Seq == 0 || snap.Seq == _appliedHorseTeleportSeq)
                return;

            ApplyHorseTeleports(snap);
        }

        void ApplyHorseTeleports(NetHorseTeleport snap)
        {
            if (snap.Seq == 0 || snap.Seq == _appliedHorseTeleportSeq)
                return;
            if (_horseA == null && _horseB == null
                && NetHorse.Find(0) == null && NetHorse.Find(1) == null)
                return;

            _appliedHorseTeleportSeq = snap.Seq;
            TeleportSeat(0, snap.PosA, snap.RotA);
            TeleportSeat(1, snap.PosB, snap.RotB);
            CoopSavePoint.SnapChainAfterTeleport();
        }

        void TeleportSeat(int seat, Vector3 pos, Quaternion rot)
        {
            var net = NetHorse.Find(seat);
            if (net != null)
            {
                net.ApplyTeleport(pos, rot);
                return;
            }

            var horse = seat == 0 ? _horseA : _horseB;
            CoopSavePoint.PlaceHorse(horse, pos, rot);
        }

        public void BroadcastPlayerDied()
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;
            RPC_PlayerDied();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_PlayerDied()
        {
            CoopDeadZone.ShowDeathPrompt();
        }

        public void BroadcastAchievement(int triggerId)
        {
            if (Object == null || !Object.IsValid || !IsWorldAuthority)
                return;

            CinematicFreeze = true;
            RPC_PlayAchievement(triggerId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
        void RPC_PlayAchievement(int triggerId)
        {
            var cam = CaliAchievementCamera.Instance;
            if (cam == null)
                cam = FindFirstObjectByType<CaliAchievementCamera>();
            if (cam != null)
                cam.PlayFromNetwork(triggerId);
        }

        void ServerTickPegasus()
        {
            if (PegasusSeat < 0)
                return;

            if (Runner.SimulationTime >= PegasusEndTime)
            {
                PegasusSeat = -1;
                if (_pegasusForm != null && _pegasusForm.IsActive)
                    _pegasusForm.Restore();
                _appliedPegasusSeat = -1;
            }
        }

        void ClientSyncPegasusForm()
        {
            if (_pegasusForm == null)
                return;

            if (PegasusSeat < 0)
            {
                if (_pegasusForm.IsActive)
                    _pegasusForm.Restore();
                _appliedPegasusSeat = -1;
                return;
            }

            if (_appliedPegasusSeat != PegasusSeat || !_pegasusForm.IsActive)
            {
                float remaining = Mathf.Max(0.1f, PegasusEndTime - (float)Runner.SimulationTime);
                _pegasusForm.Activate(PegasusSeat, remaining, useLocalTimer: false);
                _appliedPegasusSeat = PegasusSeat;
            }
        }

        public void SetHorseA(MAnimal animal) => _horseA = animal;

        public void SetHorseB(MAnimal animal) => _horseB = animal;

        void PublishWorldState()
        {
            PublishPlatformPoses();
            PublishCratePoses();
            PublishWaterBobPoses();
            PublishEnemyPoses();
        }

        void BindScenePlatforms()
        {
            var movers = FindObjectsByType<MSimpleTransformer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            System.Array.Sort(movers, (a, b) =>
                string.CompareOrdinal(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform)));

            int n = Mathf.Min(movers.Length, MaxPlatforms);
            _platformMovers = new MSimpleTransformer[n];
            _platformObjects = new Transform[n];

            for (int i = 0; i < n; i++)
            {
                _platformMovers[i] = movers[i];
                _platformObjects[i] = movers[i].Object != null ? movers[i].Object : movers[i].transform;
            }

            if (IsWorldAuthority)
            {
                PlatformCount = n;
            }
            else
            {
                // Stop local timers — host poses are authoritative.
                for (int i = 0; i < n; i++)
                    FreezeTransformer(_platformMovers[i]);
            }

            Debug.Log($"[CoopGameController] Syncing {n} scene platform mover(s) (master={IsWorldAuthority})", this);
        }

        void BindSceneCrates()
        {
            var crates = FindObjectsByType<ChainPushable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            // Hierarchy path gives both peers the same index for the same crate without needing ids.
            System.Array.Sort(crates, (a, b) =>
                string.CompareOrdinal(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform)));

            int n = Mathf.Min(crates.Length, MaxCrates);
            _crates = new ChainPushable[n];
            for (int i = 0; i < n; i++)
                _crates[i] = crates[i];

            bool worldAuth = IsWorldAuthority;
            if (worldAuth)
                CrateCount = n;

            for (int i = 0; i < n; i++)
                _crates[i].SetNetworkProxy(!worldAuth);

            if (crates.Length > MaxCrates)
                Debug.LogWarning(
                    $"[CoopGameController] {crates.Length} ChainPushable crates exceed MaxCrates={MaxCrates}; " +
                    "the extras will desync.", this);

            Debug.Log($"[CoopGameController] Syncing {n} chain crate(s) (master={worldAuth})", this);
        }

        void BindWaterBobs()
        {
            var bobs = FindObjectsByType<WaterBob>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var boats = FindObjectsByType<HorseBoat>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            int cap = bobs.Length + boats.Length;
            var transforms = new Transform[cap];
            var bobScripts = new WaterBob[cap];
            var boatScripts = new HorseBoat[cap];
            int gathered = 0;

            for (int i = 0; i < bobs.Length; i++)
            {
                var b = bobs[i];
                if (b == null)
                    continue;
                transforms[gathered] = b.transform;
                bobScripts[gathered] = b;
                boatScripts[gathered] = b.GetComponent<HorseBoat>();
                gathered++;
            }

            for (int i = 0; i < boats.Length; i++)
            {
                var boat = boats[i];
                if (boat == null)
                    continue;

                bool dup = false;
                for (int j = 0; j < gathered; j++)
                {
                    if (transforms[j] == boat.transform)
                    {
                        dup = true;
                        break;
                    }
                }
                if (dup)
                    continue;

                transforms[gathered] = boat.transform;
                bobScripts[gathered] = boat.GetComponent<WaterBob>();
                boatScripts[gathered] = boat;
                gathered++;
            }

            var order = new int[gathered];
            for (int i = 0; i < gathered; i++)
                order[i] = i;
            System.Array.Sort(order, (a, b) =>
                string.CompareOrdinal(GetStablePath(transforms[a]), GetStablePath(transforms[b])));

            int n = Mathf.Min(gathered, MaxWaterBobs);
            _waterBobObjects = new Transform[n];
            _waterBobScripts = new WaterBob[n];
            _horseBoatScripts = new HorseBoat[n];
            for (int i = 0; i < n; i++)
            {
                int src = order[i];
                _waterBobObjects[i] = transforms[src];
                _waterBobScripts[i] = bobScripts[src];
                _horseBoatScripts[i] = boatScripts[src];
            }

            bool worldAuth = IsWorldAuthority;
            if (worldAuth)
                WaterBobCount = n;

            for (int i = 0; i < n; i++)
            {
                if (_waterBobScripts[i] != null)
                    _waterBobScripts[i].SetNetworkProxy(!worldAuth);
                if (_horseBoatScripts[i] != null)
                    _horseBoatScripts[i].SetNetworkProxy(!worldAuth);
            }

            if (gathered > MaxWaterBobs)
                Debug.LogWarning(
                    $"[CoopGameController] {gathered} water movers exceed MaxWaterBobs={MaxWaterBobs}; " +
                    "the extras will desync.", this);

            Debug.Log($"[CoopGameController] Syncing {n} water mover(s) (master={worldAuth})", this);
        }

        void PublishWaterBobPoses()
        {
            if (_waterBobObjects == null)
                return;

            int n = Mathf.Min(WaterBobCount, _waterBobObjects.Length);
            for (int i = 0; i < n; i++)
            {
                var t = _waterBobObjects[i];
                if (t == null)
                    continue;

                WaterBobPoses.Set(i, new NetPlatformPose
                {
                    Position = t.position,
                    Rotation = t.rotation,
                });
            }
        }

        void PublishCratePoses()
        {
            if (_crates == null)
                return;

            int n = Mathf.Min(CrateCount, _crates.Length);
            for (int i = 0; i < n; i++)
            {
                var c = _crates[i];
                if (c == null)
                    continue;

                var t = c.transform;
                CratePoses.Set(i, new NetPlatformPose
                {
                    Position = t.position,
                    Rotation = t.rotation,
                });
            }
        }

        /// <summary>Client: snapshot the replicated scene arrays for the newest tick received.</summary>
        void RecordSceneFrame(int tick)
        {
            if (_sceneHistory == null)
            {
                _sceneHistory = new SceneFrame[SceneHistory];
                for (int i = 0; i < SceneHistory; i++)
                    _sceneHistory[i] = new SceneFrame();
                _sceneResolved = new SceneFrame();
            }

            if (tick == _sceneLastTick)
                return;

            if (tick < _sceneLastTick)
            {
                // Snapshot timeline moved backwards (reconnect / time reset): stale frames.
                _sceneHead = -1;
                _sceneFilled = 0;
            }

            _sceneLastTick = tick;
            _sceneHead = (_sceneHead + 1) % SceneHistory;
            if (_sceneFilled < SceneHistory)
                _sceneFilled++;

            var f = _sceneHistory[_sceneHead];
            f.Tick = tick;

            f.PlatformN = Mathf.Clamp(PlatformCount, 0, MaxPlatforms);
            for (int i = 0; i < f.PlatformN; i++)
                f.Platforms[i] = PlatformPoses[i];

            f.CrateN = Mathf.Clamp(CrateCount, 0, MaxCrates);
            for (int i = 0; i < f.CrateN; i++)
                f.Crates[i] = CratePoses[i];

            f.WaterBobN = Mathf.Clamp(WaterBobCount, 0, MaxWaterBobs);
            for (int i = 0; i < f.WaterBobN; i++)
                f.WaterBobs[i] = WaterBobPoses[i];

            f.EnemyN = Mathf.Clamp(EnemyPoseCount, 0, MaxEnemyPoses);
            for (int i = 0; i < f.EnemyN; i++)
                f.Enemies[i] = EnemyPoses[i];
        }

        SceneFrame FindSceneFrame(int tick)
        {
            for (int k = 0; k < _sceneFilled; k++)
            {
                int i = (_sceneHead - k + SceneHistory) % SceneHistory;
                if (_sceneHistory[i].Tick == tick)
                    return _sceneHistory[i];
            }
            return null;
        }

        /// <summary>
        /// Client: build this frame's scene-object poses on the same render tick Fusion uses
        /// for the horse poses, so both are drawn at one consistent point in time.
        /// </summary>
        void ResolveSceneFrame(in NetworkBehaviourBufferInterpolator interp)
        {
            RecordSceneFrame(interp.Valid ? (int)interp.To.Tick : (int)Runner.Tick);

            SceneFrame from = null;
            SceneFrame to = null;
            if (interp.Valid)
            {
                from = FindSceneFrame((int)interp.From.Tick);
                to = FindSceneFrame((int)interp.To.Tick);
            }

            var r = _sceneResolved;
            _sceneResolvedValid = true;

            if (from == null || to == null)
            {
                CopySceneFrame(_sceneHistory[_sceneHead], r);
                return;
            }

            float t = Mathf.Clamp01(interp.Alpha);
            r.Tick = to.Tick;
            r.PlatformN = LerpPoses(from.Platforms, from.PlatformN, to.Platforms, to.PlatformN, t, r.Platforms);
            r.CrateN = LerpPoses(from.Crates, from.CrateN, to.Crates, to.CrateN, t, r.Crates);
            r.WaterBobN = LerpPoses(from.WaterBobs, from.WaterBobN, to.WaterBobs, to.WaterBobN, t, r.WaterBobs);
            r.EnemyN = LerpEnemies(from, to, t, r.Enemies);
        }

        static int LerpPoses(NetPlatformPose[] from, int fromN, NetPlatformPose[] to, int toN, float t,
            NetPlatformPose[] dst)
        {
            for (int i = 0; i < toN; i++)
            {
                if (i >= fromN)
                {
                    dst[i] = to[i];
                    continue;
                }

                dst[i] = new NetPlatformPose
                {
                    Position = Vector3.Lerp(from[i].Position, to[i].Position, t),
                    Rotation = Quaternion.Slerp(from[i].Rotation, to[i].Rotation, t),
                };
            }
            return toN;
        }

        static int LerpEnemies(SceneFrame from, SceneFrame to, float t, NetEnemyPose[] dst)
        {
            for (int i = 0; i < to.EnemyN; i++)
            {
                var b = to.Enemies[i];
                int j = IndexOfEnemy(from, b.Id, i);
                if (j < 0)
                {
                    dst[i] = b;
                    continue;
                }

                var a = from.Enemies[j];
                dst[i] = new NetEnemyPose
                {
                    Id = b.Id,
                    Position = Vector3.Lerp(a.Position, b.Position, t),
                    Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t),
                };
            }
            return to.EnemyN;
        }

        static int IndexOfEnemy(SceneFrame f, int id, int hint)
        {
            // Enemy order comes from a stable list, so the same slot almost always matches.
            if (hint < f.EnemyN && f.Enemies[hint].Id == id)
                return hint;

            for (int i = 0; i < f.EnemyN; i++)
                if (f.Enemies[i].Id == id)
                    return i;

            return -1;
        }

        /// <summary>False until the host has published this slot; the default rotation is all zeros.</summary>
        static bool IsPoseReady(in NetPlatformPose pose)
        {
            var q = pose.Rotation;
            return q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 1e-6f;
        }

        static void CopySceneFrame(SceneFrame src, SceneFrame dst)
        {
            dst.Tick = src.Tick;
            dst.PlatformN = src.PlatformN;
            dst.CrateN = src.CrateN;
            dst.WaterBobN = src.WaterBobN;
            dst.EnemyN = src.EnemyN;
            System.Array.Copy(src.Platforms, dst.Platforms, src.PlatformN);
            System.Array.Copy(src.Crates, dst.Crates, src.CrateN);
            System.Array.Copy(src.WaterBobs, dst.WaterBobs, src.WaterBobN);
            System.Array.Copy(src.Enemies, dst.Enemies, src.EnemyN);
        }

        /// <summary>Client: stamp the resolved poses onto platforms, crates and enemies.</summary>
        void ApplySceneFrame()
        {
            if (!_sceneResolvedValid)
                return;

            var f = _sceneResolved;

            if (_platformObjects != null)
            {
                int n = Mathf.Min(f.PlatformN, _platformObjects.Length);
                for (int i = 0; i < n; i++)
                {
                    var t = _platformObjects[i];
                    if (t != null && IsPoseReady(f.Platforms[i]))
                        t.SetPositionAndRotation(f.Platforms[i].Position, f.Platforms[i].Rotation);
                }
            }

            if (_crates != null)
            {
                int n = Mathf.Min(f.CrateN, _crates.Length);
                for (int i = 0; i < n; i++)
                {
                    var c = _crates[i];
                    if (c != null && IsPoseReady(f.Crates[i]))
                        c.ApplyNetworkPose(f.Crates[i].Position, f.Crates[i].Rotation);
                }
            }

            if (_waterBobObjects != null)
            {
                int n = Mathf.Min(f.WaterBobN, _waterBobObjects.Length);
                for (int i = 0; i < n; i++)
                {
                    if (!IsPoseReady(f.WaterBobs[i]))
                        continue;

                    var pose = f.WaterBobs[i];
                    if (_waterBobScripts != null && i < _waterBobScripts.Length && _waterBobScripts[i] != null)
                        _waterBobScripts[i].ApplyNetworkPose(pose.Position, pose.Rotation);
                    else if (_horseBoatScripts != null && i < _horseBoatScripts.Length && _horseBoatScripts[i] != null)
                        _horseBoatScripts[i].ApplyNetworkPose(pose.Position, pose.Rotation);
                    else if (_waterBobObjects[i] != null)
                        _waterBobObjects[i].SetPositionAndRotation(pose.Position, pose.Rotation);
                }
            }

            for (int i = 0; i < f.EnemyN; i++)
            {
                var pose = f.Enemies[i];
                var e = Cali.Combat.ChainKillableEnemy.FindById(pose.Id);
                if (e == null || e.IsDead)
                    continue;
                e.ApplyNetworkPose(pose.Position, pose.Yaw);
            }
        }

        static void FreezeTransformer(MSimpleTransformer mover)
        {
            if (mover == null)
                return;

            // Avoid SetToManual() — it Restart()s and can snap Once platforms.
            mover.update = MSimpleTransformer.UpdateCycle.Manual;
            mover.Playing = false;
            mover.Waiting = false;
            mover.StopAllCoroutines();
        }

        void PublishPlatformPoses()
        {
            if (_platformObjects == null)
                return;

            int n = Mathf.Min(PlatformCount, _platformObjects.Length);
            for (int i = 0; i < n; i++)
            {
                var t = _platformObjects[i];
                if (t == null)
                    continue;

                PlatformPoses.Set(i, new NetPlatformPose
                {
                    Position = t.position,
                    Rotation = t.rotation,
                });
            }
        }

        void PublishEnemyPoses()
        {
            var list = Cali.Combat.ChainKillableEnemy.StableList;
            if (list == null)
            {
                EnemyPoseCount = 0;
                return;
            }

            int count = 0;
            for (int i = 0; i < list.Length && count < MaxEnemyPoses; i++)
            {
                var e = list[i];
                if (e == null || e.IsDead)
                    continue;

                EnemyPoses.Set(count, new NetEnemyPose
                {
                    Id = e.enemyId,
                    Position = e.transform.position,
                    Yaw = e.transform.eulerAngles.y,
                });
                count++;
            }

            EnemyPoseCount = count;
        }

        void ApplyEnemyDeathBits()
        {
            ApplyEnemyDeathMask(EnemyDeadBits0, 1);
            ApplyEnemyDeathMask(EnemyDeadBits1, 33);
        }

        static void ApplyEnemyDeathMask(int mask, int startId)
        {
            if (mask == 0)
                return;

            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                Cali.Combat.ChainKillableEnemy.ApplyRemoteDeath(startId + i, Vector3.zero, Vector3.forward);
            }
        }

        static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(128);
            while (t != null)
            {
                sb.Insert(0, t.name);
                sb.Insert(0, '/');
                t = t.parent;
            }
            return sb.ToString();
        }

        /// <summary>Includes sibling index so cloned WaterBobs with the same name stay ordered.</summary>
        static string GetStablePath(Transform t)
        {
            var sb = new StringBuilder(128);
            while (t != null)
            {
                sb.Insert(0, t.GetSiblingIndex().ToString());
                sb.Insert(0, ':');
                sb.Insert(0, t.name);
                sb.Insert(0, '/');
                t = t.parent;
            }
            return sb.ToString();
        }

        MAnimal GetLocalHorse() => _localSeat == 0 ? _horseA : _horseB;

        void ResolveSceneRefs()
        {
            _localInput = FindFirstObjectByType<LocalDualHorseInput>();
            if (_localInput != null)
            {
                if (_localInput.horseA != null)
                    _horseA = _localInput.horseA;
                if (_localInput.horseB != null)
                    _horseB = _localInput.horseB;
            }

            if (_horseA == null)
                _horseA = FindPlayAnimal("Horse Realistic");
            if (_horseB == null)
                _horseB = FindPlayAnimal("Horse Unicorn");

            _chain = FindFirstObjectByType<SoftHorseChain>(FindObjectsInactive.Include);
            _pegasusForm = FindFirstObjectByType<HorsePegasusForm>(FindObjectsInactive.Include);
            if (_pegasusForm == null)
            {
                var host = _localInput != null ? _localInput.gameObject : gameObject;
                _pegasusForm = host.AddComponent<HorsePegasusForm>();
            }

            _appliedPegasusSeat = -1;

            if (_horseA != null && !_horseA.gameObject.activeInHierarchy)
                _horseA.gameObject.SetActive(true);
            if (_horseB != null && !_horseB.gameObject.activeInHierarchy)
                _horseB.gameObject.SetActive(true);

            // Keep extras off, but leave Pegasus available for HorsePegasusForm (inactive is fine).
            // Do not disable chain-killable enemies (wolves / humanoids).
            var pegasus = _pegasusForm != null ? _pegasusForm.pegasus : null;
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var animal in animals)
            {
                if (animal == _horseA || animal == _horseB || animal == pegasus)
                    continue;
                if (animal.GetComponentInParent<Cali.Combat.ChainKillableEnemy>() != null)
                    continue;
                if (animal.GetComponentInParent<CaliNpcHorse>() != null)
                    continue;
                if (animal.GetComponent<SoftHorseChain>() != null)
                    continue;
                animal.gameObject.SetActive(false);
            }

            if (pegasus != null && pegasus.gameObject.activeSelf && (_pegasusForm == null || !_pegasusForm.IsActive))
                pegasus.gameObject.SetActive(false);

            if (_horseA == null || _horseB == null)
                Debug.LogWarning(
                    $"[CoopGameController] Missing horses. A={_horseA?.name ?? "null"} B={_horseB?.name ?? "null"}",
                    this);
            else
                CoopSavePoint.CaptureLevelStartIfUnset();
        }

        void EnsureChainBound()
        {
            if (_horseA == null || _horseB == null)
                ResolveSceneRefs();
            if (_horseA == null || _horseB == null)
                return;
            if (_chain != null && _chain.horseA == _horseA && _chain.horseB == _horseB && _chain.isActiveAndEnabled)
                return;
            BindChainToHorses();
        }

        void BindChainToHorses()
        {
            if (_chain == null)
            {
                var host = _localInput != null ? _localInput.gameObject : null;
                if (host == null)
                    host = new GameObject("<<<Soft Horse Chain>>>");
                _chain = host.GetComponent<SoftHorseChain>();
                if (_chain == null)
                    _chain = host.AddComponent<SoftHorseChain>();
            }

            _chain.enabled = true;
            _chain.gameObject.SetActive(true);
            _chain.RebindHorses(_horseA, _horseB);
            _chain.SnapToCurrentHorses();
        }

        void ApplyLocalCamera(bool force)
        {
            if (_horseA == null && _horseB == null)
                return;

            if (CinematicFreeze || CaliAchievementCamera.IsPlaying)
                return;

            int seat = CoopInput.GetLocalSeat(Runner);
            _localSeat = seat;

            var focus = seat == 0 ? _horseA : _horseB;
            if (focus == null)
            {
                Debug.LogWarning(
                    $"[CoopGameController] Seat {seat} horse missing — falling back. A={_horseA?.name} B={_horseB?.name}",
                    this);
                focus = _horseA ?? _horseB;
            }

            if (!force && seat == _cameraSeat && CoopCameraFocus.IsFocusedOn(focus))
                return;

            _cameraSeat = seat;
            CoopCameraFocus.FocusOnHorse(focus);
            Debug.Log(
                $"[CoopGameController] Local seat={seat} → camera on '{focus.name}' (master={IsWorldAuthority} IsClient={Runner.IsClient})",
                this);
        }

        public static MAnimal FindPlayAnimal(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            MAnimal exactFallback = null;
            MAnimal containsFallback = null;
            string playScene = Cali.UI.CaliMainMenuController.GameSceneName;

            foreach (var animal in animals)
            {
                if (animal == null)
                    continue;

                bool exact = animal.gameObject.name == objectName;
                bool contains = !exact && animal.gameObject.name.Contains(objectName);
                if (!exact && !contains)
                    continue;

                bool inPlayScene = animal.gameObject.scene.name == playScene;
                if (exact && inPlayScene)
                    return animal;
                if (exact && exactFallback == null)
                    exactFallback = animal;
                if (contains && containsFallback == null)
                    containsFallback = animal;
            }

            return exactFallback != null ? exactFallback : containsFallback;
        }
    }

}
