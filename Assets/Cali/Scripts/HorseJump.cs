using System.Collections.Generic;
using MalbersAnimations;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// TPS jump: Space applies an instant physics hop. Malbers Jump is animation-only
    /// (no Neigh, no root-motion lift). Gravity and landing are ours.
    /// </summary>
    public static class HorseJump
    {
        public const int DefaultJumpStateId = 2;

        const float JumpSpeed = 9.5f;
        const float Gravity = -22f;
        const float HoldGravity = -12f;
        const float ReleaseCut = 0.42f;
        const float HoldMax = 0.32f;
        const float AirAccel = 8f;
        const float AirDrag = 4f;
        const float MinAirSpeed = 2f;
        const float Coyote = 0.12f;
        const float MinAirTime = 0.18f;
        const float StickDeadzone = 0.04f;
        const float AnimBlend = 0.1f;
        const float LandBlend = 0.08f;
        const float LandHold = 0.42f;

        const string AnimStart = "Jump Forward Start";
        const string AnimAir = "Jump Forward Air";
        const string AnimFall = "Fall";
        const string AnimLand = "Jump Forward End";
        const string AnimFallLand = "Landing";

        struct Flight
        {
            public bool Active;
            public bool LeftGround;
            public float Vert;
            public Vector3 Horiz;
            public float Age;
            public int Phase;
            public int JumpStateId;
            public bool Landing;
            public float LandAge;
            public bool Cut;
            public int MissedFooting;
        }

        static readonly HashSet<MAnimal> Bound = new HashSet<MAnimal>();
        static readonly Dictionary<MAnimal, Flight> Air = new Dictionary<MAnimal, Flight>();
        static readonly Dictionary<MAnimal, Vector2> Stick = new Dictionary<MAnimal, Vector2>();
        static readonly Dictionary<MAnimal, float> LastGrounded = new Dictionary<MAnimal, float>();
        static readonly Dictionary<MAnimal, bool> Held = new Dictionary<MAnimal, bool>();

        public static bool IsAirborne(MAnimal animal)
        {
            if (animal == null)
                return false;
            if (IsOurs(animal))
                return true;
            if (animal.ActiveStateID == null)
                return false;
            int id = animal.ActiveStateID.ID;
            return id == StateEnum.Jump || id == StateEnum.Fall;
        }

        public static bool UseAirSteer(MAnimal animal)
        {
            return IsOurs(animal) && !IsLanding(animal);
        }

        /// <summary>
        /// True when walkable ground is under the hips, close enough to stand on.
        /// A chest/nose snag on a ledge does not count.
        /// </summary>
        public static bool HasFooting(MAnimal animal)
        {
            if (animal == null)
                return false;

            Vector3 origin;
            if (animal.Has_Pivot_Hip && animal.Pivot_Hip != null)
                origin = animal.Pivot_Hip.World(animal.transform) + Vector3.up * 0.25f;
            else
                origin = animal.transform.position + Vector3.up * (animal.Height * 0.7f) - animal.transform.forward * 0.35f;

            float height = animal.Height;
            float max = height * 1.65f;
            if (max < 0.2f)
                max = 0.2f;

            if (!Physics.Raycast(origin, Vector3.down, out var hit, max, animal.GroundLayer, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.42f)
                return false;
            return hit.distance <= max;
        }

        public static void Pulse(MAnimal animal, int jumpStateId = DefaultJumpStateId)
        {
            bool dummy = false;
            UpdateHeld(animal, true, Vector2.zero, ref dummy, jumpStateId);
        }

        public static void Prepare(MAnimal animal, int jumpStateId = DefaultJumpStateId)
        {
            Bind(animal, jumpStateId);
        }

        public static void UpdateHeld(MAnimal animal, bool held, ref bool wasHeld, int jumpStateId = DefaultJumpStateId)
        {
            UpdateHeld(animal, held, Vector2.zero, ref wasHeld, jumpStateId);
        }

        public static void UpdateHeld(
            MAnimal animal, bool held, Vector2 stick, ref bool wasHeld, int jumpStateId = DefaultJumpStateId)
        {
            if (animal == null)
                return;

            Bind(animal, jumpStateId);
            Stick[animal] = stick;
            Held[animal] = held;

            var malbersJump = animal.State_Get(jumpStateId);
            if (malbersJump != null)
                malbersJump.SetInput(false);

            if (!Simulated(animal))
            {
                wasHeld = held;
                return;
            }

            if (StableLatch.SuppressHorseJump || CoopSavePoint.SuppressHorseJump)
            {
                wasHeld = held;
                return;
            }

            if (held && !wasHeld && CanJump(animal))
                StartJump(animal, jumpStateId);

            wasHeld = held;
        }

        static bool IsOurs(MAnimal animal)
        {
            return Air.TryGetValue(animal, out var flight) && flight.Active;
        }

        static bool IsLanding(MAnimal animal)
        {
            return Air.TryGetValue(animal, out var flight) && flight.Landing;
        }

        static bool Simulated(MAnimal animal)
        {
            if (animal.DisablePosition)
                return false;
            return animal.RB == null || !animal.RB.isKinematic;
        }

        static bool CanJump(MAnimal animal)
        {
            if (IsOurs(animal) || IsLanding(animal))
                return false;
            if (animal.ActiveStateID != null)
            {
                int id = animal.ActiveStateID.ID;
                if (id == StateEnum.Death || id == StateEnum.Swim || id == StateEnum.Fly)
                    return false;
            }

            if (animal.Grounded)
                return true;
            if (HasFooting(animal))
                return true;

            LastGrounded.TryGetValue(animal, out float t);
            return Time.time - t <= Coyote;
        }

        static void StartJump(MAnimal animal, int jumpStateId)
        {
            Vector3 up = animal.UpVector;
            Vector3 horiz = Vector3.ProjectOnPlane(animal.HorizontalVelocity, up);
            if (animal.RB != null)
                horiz = Vector3.ProjectOnPlane(animal.RB.linearVelocity, up);

            Air[animal] = new Flight
            {
                Active = true,
                LeftGround = false,
                Vert = JumpSpeed,
                Horiz = horiz,
                Age = 0f,
                Phase = 0,
                JumpStateId = jumpStateId,
                Cut = false
            };

            animal.Grounded = false;
            animal.UseGravity = false;
            animal.RootMotion = false;
            animal.RootMotionRotation = false;
            animal.UseOrientToGround = false;
            animal.ResetGravityValues();
            animal.ResetInertiaSpeed(Vector3.zero);
            animal.UpInertia_Clear();

            if (animal.ActiveState != null)
                animal.ActiveState.IsPersistent = true;

            animal.State_Force(jumpStateId);
            Play(animal, AnimStart, AnimBlend);
        }

        static void Bind(MAnimal animal, int jumpStateId)
        {
            if (animal == null)
                return;

            MuteMalbersJump(animal, jumpStateId);
            if (!Bound.Add(animal))
                return;

            animal.PreStateMovement += OnPre;
            animal.PostStateMovement += OnStep;
        }

        static void MuteMalbersJump(MAnimal animal, int jumpStateId)
        {
            var jump = animal.State_Get(jumpStateId) as Jump;
            if (jump == null || jump.jumpProfiles == null)
                return;

            for (int i = 0; i < jump.jumpProfiles.Count; i++)
            {
                var profile = jump.jumpProfiles[i];
                profile.HeightMultiplier = 0f;
                profile.ForwardMultiplier = 0f;
                if (profile.JumpLandDistance <= 0.01f)
                    profile.JumpLandDistance = 2.3f;
                jump.jumpProfiles[i] = profile;
            }
        }

        static void OnPre(MAnimal animal)
        {
            if (animal == null)
                return;

            StripExtras(animal);

            if (IsLanding(animal))
            {
                animal.Grounded = true;
                animal.UseGravity = false;
                if (animal.ActiveState != null)
                    animal.ActiveState.IsPersistent = true;
                return;
            }

            if (!IsOurs(animal))
                return;

            animal.Grounded = false;
            animal.UseGravity = false;
            animal.RootMotion = false;
            animal.RootMotionRotation = false;
            animal.UseOrientToGround = false;
            if (animal.ActiveState != null)
                animal.ActiveState.IsPersistent = true;
        }

        static void OnStep(MAnimal animal)
        {
            if (animal == null)
                return;

            if (!IsOurs(animal))
            {
                StripExtras(animal);
                if (IsLanding(animal))
                    TickLand(animal);
                else
                    RecoverWalk(animal);
                return;
            }

            Air.TryGetValue(animal, out var flight);
            float dt = Mathf.Max(animal.DeltaTime, 0.0001f);
            flight.Age += dt;

            Vector3 worldUp = Vector3.up;
            Stick.TryGetValue(animal, out var stick);
            Vector3 wish = Vector3.ProjectOnPlane(Cali.Network.CoopInput.CameraRelativeMove(stick), worldUp);

            float cap = Mathf.Max(flight.Horiz.magnitude, MinAirSpeed);
            if (wish.sqrMagnitude > StickDeadzone)
            {
                Vector3 target = wish.normalized * cap;
                flight.Horiz = Vector3.MoveTowards(flight.Horiz, target, AirAccel * dt);
            }
            else
            {
                flight.Horiz = Vector3.MoveTowards(flight.Horiz, Vector3.zero, AirDrag * dt);
            }

            if (flight.Horiz.sqrMagnitude > cap * cap)
                flight.Horiz = flight.Horiz.normalized * cap;

            Held.TryGetValue(animal, out bool holding);
            if (holding && flight.Vert > 0f && flight.Age < HoldMax)
            {
                flight.Vert += HoldGravity * dt;
            }
            else
            {
                if (!holding && flight.Vert > 0f && !flight.Cut)
                {
                    flight.Vert *= ReleaseCut;
                    flight.Cut = true;
                }
                flight.Vert += Gravity * dt;
            }

            Vector3 delta = (flight.Horiz + worldUp * flight.Vert) * dt;
            animal.AdditivePosition = delta;
            animal.ResetInertiaSpeed(delta);
            LevelOut(animal, dt);

            if (flight.Age > 0.08f)
                flight.LeftGround = true;

            UpdateAnim(animal, ref flight);
            Air[animal] = flight;

            if (flight.LeftGround && flight.Vert <= 0f && flight.Age >= MinAirTime && HasFooting(animal))
                Land(animal, ref flight);
        }

        static void UpdateAnim(MAnimal animal, ref Flight flight)
        {
            if (flight.Phase == 0 && flight.Age > 0.14f)
            {
                Play(animal, AnimAir, AnimBlend);
                flight.Phase = 1;
            }

            if (flight.Vert < 0f && flight.Phase < 2)
            {
                Play(animal, AnimFall, 0.16f);
                flight.Phase = 2;
            }
        }

        static void Land(MAnimal animal, ref Flight flight)
        {
            flight.Active = false;
            flight.Landing = true;
            flight.LandAge = 0f;
            flight.MissedFooting = 0;
            Air[animal] = flight;

            if (animal.ActiveState != null)
                animal.ActiveState.IsPersistent = true;

            animal.Grounded = true;
            animal.UseGravity = false;
            animal.UpInertia_Clear();
            float dt = Mathf.Max(animal.DeltaTime, 0.0001f);
            animal.AdditivePosition = flight.Horiz * dt;
            animal.ResetInertiaSpeed(animal.AdditivePosition);

            Play(animal, flight.Phase >= 2 ? AnimFallLand : AnimLand, LandBlend);
            LastGrounded[animal] = Time.time;
        }

        static void TickLand(MAnimal animal)
        {
            Air.TryGetValue(animal, out var flight);
            if (!flight.Landing)
                return;

            if (!HasFooting(animal))
            {
                flight.MissedFooting++;
                Air[animal] = flight;
                if (flight.MissedFooting > 8)
                    ResumeFall(animal, ref flight);
                return;
            }
            flight.MissedFooting = 0;

            float dt = Mathf.Max(animal.DeltaTime, 0.0001f);
            flight.LandAge += dt;
            animal.Grounded = true;
            animal.UseGravity = false;
            animal.AdditivePosition = Vector3.ProjectOnPlane(animal.AdditivePosition, animal.UpVector);
            Air[animal] = flight;

            bool clipDone = LandClipDone(animal, flight);
            if (!clipDone && flight.LandAge < LandHold)
                return;

            FinishLand(animal, ref flight);
        }

        static bool LandClipDone(MAnimal animal, Flight flight)
        {
            var anim = animal.Anim;
            if (anim == null)
                return false;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (!info.IsTag("JumpEnd") && !info.IsName(AnimLand) && !info.IsName(AnimFallLand))
                return false;
            return info.normalizedTime >= 0.72f && !anim.IsInTransition(0);
        }

        static void FinishLand(MAnimal animal, ref Flight flight)
        {
            flight.Landing = false;
            Air[animal] = flight;

            if (animal.ActiveState != null)
                animal.ActiveState.IsPersistent = false;

            Stick.TryGetValue(animal, out var stick);
            bool moving = stick.sqrMagnitude > StickDeadzone;
            int next = moving ? StateEnum.Locomotion : StateEnum.Idle;
            animal.State_Force(next);
            Play(animal, moving ? "Locomotion" : "Idle", 0.16f);
        }

        static void RecoverWalk(MAnimal animal)
        {
            if (!Simulated(animal))
                return;

            if (animal.Grounded)
                LastGrounded[animal] = Time.time;

            if (animal.ActiveStateID == null)
                return;
            int id = animal.ActiveStateID.ID;
            if (id != StateEnum.Jump)
                return;
            if (!animal.Grounded)
                return;

            if (animal.ActiveState != null)
                animal.ActiveState.IsPersistent = false;
            animal.State_Force(StateEnum.Idle);
        }

        static void LevelOut(MAnimal animal, float dt)
        {
            Vector3 up = animal.transform.up;
            if (Vector3.Dot(up, Vector3.up) > 0.98f)
                return;
            Quaternion upright = Quaternion.FromToRotation(up, Vector3.up) * animal.Rotation;
            animal.Rotation = Quaternion.Slerp(animal.Rotation, upright, 10f * dt);
        }

        static void StripExtras(MAnimal animal)
        {
            animal.slideAmount = 0f;
            animal.slideThreshold = 90f;
            animal.SlopeDirectionSmooth = Vector3.zero;
            animal.UpInertia_Clear();
        }

        static void ResumeFall(MAnimal animal, ref Flight flight)
        {
            flight.Landing = false;
            flight.Active = true;
            flight.Vert = Mathf.Min(flight.Vert, -2.5f);
            flight.Phase = 2;
            Air[animal] = flight;

            animal.Grounded = false;
            animal.UseGravity = false;
            animal.UseOrientToGround = false;
            Play(animal, AnimFall, 0.12f);
        }

        static void Play(MAnimal animal, string state, float blend)
        {
            var anim = animal.Anim;
            if (anim == null || !anim.enabled)
                return;
            anim.CrossFadeInFixedTime(state, blend, 0, 0f);
        }
    }
}
