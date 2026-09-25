using Cali.Gameplay;
using Fusion;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Network
{
    /// <summary>
    /// One per player. The owner simulates its horse with Malbers exactly as in single player
    /// and publishes pose plus rider intent; the other peer renders that as a proxy.
    ///
    /// No force, velocity or position is ever applied to a horse from the network. Rider input
    /// reaches Malbers untouched, and the chain is a local limit on how far this peer's own horse
    /// travels (<see cref="ChainTether"/>), so there is no authority round trip to correct and
    /// nothing that can snap.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class NetHorse : NetworkBehaviour
    {
        public const string ResourcesPath = "NetHorse";

        static readonly NetHorse[] BySeat = new NetHorse[2];

        [Networked] public int Seat { get; set; }
        [Networked] public Vector3 Position { get; set; }
        [Networked] public Quaternion Rotation { get; set; }

        /// <summary>
        /// Raw rider intent. Drives the proxy's animation, and the partner's tether reads it to
        /// tell a horse that is driving away from one that is dead weight on the rope.
        /// </summary>
        [Networked] public Vector3 WishDir { get; set; }
        [Networked] public int StateId { get; set; }
        [Networked] public NetworkBool Grounded { get; set; }
        [Networked] public NetworkBool Sprint { get; set; }
        [Networked] public NetworkBool JumpHeld { get; set; }

        /// <summary>False until the owner publishes a real pose. Stops proxies and the tether
        /// from using the spawn-time placeholder.</summary>
        [Networked] public NetworkBool PoseValid { get; set; }

        public MAnimal Animal { get; private set; }
        public bool JumpHeldLocal { get; private set; }
        public int AssignedSeat => Seat == 1 ? 1 : 0;
        public ChainTether Tether => _tether;

        /// <summary>
        /// This horse's rider intent as seen from this peer: live for the horse we drive,
        /// replicated for the partner. The partner's tether needs it to tell a horse that is
        /// driving away from dead weight it should be dragging.
        /// </summary>
        public Vector3 OwnWish => IsMine() ? _wish : WishDir;

        readonly ChainTether _tether = new ChainTether();

        Vector3 _wish;
        bool _jumpWas;
        int _viewState = int.MinValue;
        bool _proxyApplied;
        bool _hasProxyApplied;
        bool _proxyHasPose;
        float _holdProxyUntil;

        public static NetHorse Find(int seat)
        {
            if (seat < 0 || seat > 1)
                return null;

            var cached = BySeat[seat];
            if (cached != null && cached.Object != null && cached.Object.IsValid && cached.AssignedSeat == seat)
                return cached;

            var all = UnityEngine.Object.FindObjectsByType<NetHorse>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var h = all[i];
                if (h == null || h.Object == null || !h.Object.IsValid || h.AssignedSeat != seat)
                    continue;
                BySeat[seat] = h;
                return h;
            }

            return null;
        }

        public static NetHorse Local => Find(CoopInput.GetLocalSeat(CoopSessionStarter.Runner));

        public static NetHorse Remote => Find(1 - CoopInput.GetLocalSeat(CoopSessionStarter.Runner));

        public override void Spawned()
        {
            Object.Flags &= ~NetworkObjectFlags.MasterClientObject;
            Object.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
            BindAnimal();
            Register();
            ApplyAuthorityMode();
            Debug.Log(
                $"[NetHorse] Spawned seat={AssignedSeat} localSeat={CoopInput.GetLocalSeat(Runner)} " +
                $"SA={HasStateAuthority} animal={(Animal != null ? Animal.name : "null")}",
                this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Seat >= 0 && Seat < BySeat.Length && BySeat[Seat] == this)
                BySeat[Seat] = null;

            _tether.Release();
            ConfigureProxy(Animal, false);
        }

        public override void FixedUpdateNetwork()
        {
            if (Animal == null || !HasStateAuthority)
                return;

            PublishState();
        }

        void Update()
        {
            if (Object == null || !Object.IsValid)
                return;

            if (Animal == null)
                BindAnimal();

            Register();
            ApplyAuthorityMode();

            if (IsMine())
                DriveLocal();
            else
                DriveProxyAnim();
        }

        public override void Render()
        {
            if (Animal == null || IsMine() || StampsLocalHorse())
                return;
            if (!PoseValid)
            {
                _proxyHasPose = false;
                return;
            }

            ApplyRemotePose();
        }

        void DriveLocal()
        {
            if (Animal == null)
                return;

            _tether.Bind(Animal);
            HorseJump.Prepare(Animal, CoopGameController.JumpStateId);

            var coop = CoopGameController.Instance;
            if (coop != null && !coop.AllowsHorseInput)
            {
                _wish = Vector3.zero;
                JumpHeldLocal = false;
                _tether.NoPartner();
                Animal.Move(Vector3.zero);
                Animal.SetSprint(false);
                HorseJump.UpdateHeld(Animal, false, ref _jumpWas, CoopGameController.JumpStateId);
                return;
            }

            int localSeat = CoopInput.GetLocalSeat(Runner);
            int seat = AssignedSeat;

            Vector2 stick;
            bool jump;
            bool sprint;
            if (seat == localSeat)
            {
                stick = CoopInput.ReadWasd();
                jump = CoopInput.ReadJumpHeld();
                sprint = CoopInput.ReadSprintHeld();
            }
            else
            {
                stick = CoopInput.ReadArrows();
                jump = CoopInput.ReadJumpHeldB();
                sprint = CoopInput.ReadSprintHeldB();
            }

            bool air = HorseJump.UseAirSteer(Animal);
            Vector3 wish;
            if (air)
            {
                wish = Vector3.ProjectOnPlane(Animal.Forward, Vector3.up);
                Animal.Move(Vector3.zero);
                Animal.SetSprint(false);
            }
            else
            {
                wish = CoopInput.CameraRelativeMove(stick);
                Animal.Move(wish);
                Animal.SetSprint(sprint && wish.sqrMagnitude > 0.04f);
            }

            _wish = wish;
            JumpHeldLocal = jump;
            HorseJump.UpdateHeld(Animal, jump, stick, ref _jumpWas, CoopGameController.JumpStateId);

            TrackChain(wish);
        }

        /// <summary>
        /// Feeds the rope the partner's on-screen position, so the chain the player sees and the
        /// limit they feel are the same thing.
        /// </summary>
        void TrackChain(Vector3 wish)
        {
            MAnimal partner = null;
            var other = Find(1 - AssignedSeat);
            if (other != null && other.Animal != null && other.Animal != Animal)
                partner = other.Animal;

            if (partner == null)
            {
                var coop = CoopGameController.Instance;
                if (coop != null)
                    partner = AssignedSeat == 0 ? coop.PlayHorseB : coop.PlayHorseA;
            }

            if (partner == null || partner == Animal)
            {
                _tether.NoPartner();
                return;
            }

            var chain = SoftHorseChain.Instance;
            if (chain != null)
                _tether.Length = chain.maxLength;

            Vector3 partnerWish = other != null ? other.OwnWish : Vector3.zero;
            _tether.Track(wish, partner.transform.position, partnerWish);
        }

        /// <summary>
        /// The proxy animator is on Normal update, which runs after Update. Feeding Malbers
        /// here — not only in Render — is what lets it pick walk/sprint before the clip
        /// evaluates. Render still stamps the interpolated transform afterwards.
        /// </summary>
        void DriveProxyAnim()
        {
            if (Animal == null || !PoseValid || StampsLocalHorse())
                return;
            if (Time.time < _holdProxyUntil)
                return;

            Animal.Grounded = Grounded;
            Animal.Move(WishDir);
            Animal.SetSprint(Sprint);
            HorseJump.UpdateHeld(Animal, JumpHeld, ref _jumpWas, CoopGameController.JumpStateId);
            SyncRemoteState();
        }

        void ApplyRemotePose()
        {
            Vector3 pos = Position;
            Quaternion rot = Rotation;
            // First valid pose (and teleports) snap. Interpolating from the NetworkObject's
            // default (0,0,0) is what dropped a joining client out of the sky.
            bool snap = !_proxyHasPose || Time.time < _holdProxyUntil;
            if (!snap)
            {
                var interp = new NetworkBehaviourBufferInterpolator(this);
                if (interp)
                {
                    pos = interp.Vector3(nameof(Position));
                    rot = interp.Quaternion(nameof(Rotation));
                }
            }

            _proxyHasPose = true;
            Animal.Position = pos;
            Animal.Rotation = rot;
            Animal.Grounded = Grounded;
            Animal.Move(WishDir);
            Animal.SetSprint(Sprint);
            SyncRemoteState();
        }

        /// <summary>
        /// Idle and locomotion must come from <see cref="DriveProxyAnim"/> or Idle steals
        /// the gait after a one-shot Force and the horse slides in a frozen stand. Jump,
        /// fall and fly still need a force because they are not input-axis states.
        /// </summary>
        void SyncRemoteState()
        {
            int stateId = StateId;
            if (!ShouldForceRemoteState(stateId))
            {
                _viewState = int.MinValue;
                return;
            }

            if (stateId == _viewState)
                return;

            Animal.State_Force(stateId);
            _viewState = stateId;
        }

        static bool ShouldForceRemoteState(int stateId)
        {
            return stateId == CoopGameController.JumpStateId
                || stateId == CoopGameController.FallStateId
                || stateId == MalbersAnimations.StateEnum.Fly
                || stateId == MalbersAnimations.StateEnum.Swim
                || stateId == MalbersAnimations.StateEnum.Glide
                || stateId == MalbersAnimations.StateEnum.Death;
        }

        void PublishState()
        {
            var t = Animal.transform;
            Position = t.position;
            Rotation = t.rotation;
            WishDir = _wish;
            StateId = Animal.ActiveStateID != null ? Animal.ActiveStateID.ID : 0;
            Grounded = Animal.Grounded;
            Sprint = Animal.Sprint;
            JumpHeld = JumpHeldLocal;
            PoseValid = true;
        }

        /// <summary>True when this peer controls this seat's horse.</summary>
        public bool IsMine()
        {
            if (Runner == null || !Runner.IsRunning)
                return false;

            return AssignedSeat == CoopInput.GetLocalSeat(Runner)
                || (Runner.SessionInfo.PlayerCount <= 1 && Runner.IsSharedModeMasterClient);
        }

        /// <summary>Guards the solo-host case where both seats resolve to the same animal.</summary>
        bool StampsLocalHorse()
        {
            var local = Local;
            return local != null && local != this && Animal != null && local.Animal == Animal;
        }

        void ApplyAuthorityMode()
        {
            // Nothing to configure until the play-scene horse is bound, and it must not be
            // recorded as applied either or the proxy would keep letting Malbers move it.
            if (Animal == null)
            {
                _hasProxyApplied = false;
                return;
            }

            bool introHold = false;
            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid)
                introHold = coop.IsInIntro;

            bool proxy = !IsMine() || introHold;
            if (_hasProxyApplied && _proxyApplied == proxy)
                return;

            ConfigureProxy(Animal, proxy);
            _hasProxyApplied = true;
            _proxyApplied = proxy;
            _viewState = int.MinValue;
            _proxyHasPose = false;

            if (proxy)
                _tether.Release();
        }

        public void Rebind()
        {
            _tether.Release();
            Animal = null;
            _viewState = int.MinValue;
            _hasProxyApplied = false;
            _proxyHasPose = false;
            BindAnimal();
            Register();
            ApplyAuthorityMode();
        }

        void BindAnimal()
        {
            Animal = AssignedSeat == 1
                ? CoopGameController.FindPlayAnimal("Horse Unicorn")
                : CoopGameController.FindPlayAnimal("Horse Realistic");
        }

        void Register()
        {
            int seat = AssignedSeat;
            BySeat[seat] = this;
            int other = 1 - seat;
            if (BySeat[other] == this)
                BySeat[other] = null;
        }

        public void TryTeleportIfAuthority(Vector3 pos, Quaternion rot)
        {
            if (!HasStateAuthority)
                return;

            ApplyTeleport(pos, rot);
        }

        /// <summary>
        /// Snap this seat's scene horse. The owner publishes the new pose; a proxy holds
        /// still so interpolation does not slide it back to the pit for a few ticks.
        /// </summary>
        public void ApplyTeleport(Vector3 pos, Quaternion rot)
        {
            if (Animal == null)
                BindAnimal();
            if (Animal == null)
                return;

            _tether.ClearMotion();
            CoopSavePoint.PlaceHorse(Animal, pos, rot);
            _holdProxyUntil = Time.time + 0.4f;
            _proxyHasPose = false;

            if (HasStateAuthority)
                PublishState();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Teleport(Vector3 pos, Quaternion rot)
        {
            ApplyTeleport(pos, rot);
        }

        /// <summary>
        /// A proxy keeps running Malbers for animation and states, but Malbers must not move or
        /// turn it: its transform comes from the interpolated snapshot instead.
        /// </summary>
        static void ConfigureProxy(MAnimal animal, bool proxy)
        {
            HorseBody.SetSimulated(animal, !proxy);
        }
    }
}
