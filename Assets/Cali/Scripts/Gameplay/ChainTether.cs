using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// A real chain between two horses: 3D max length. Jump, fall and walk cannot
    /// take a horse farther than <see cref="Length"/> from its partner.
    ///
    /// Input, gait and animation are left alone. The rope only edits this horse's
    /// displacement after Malbers has written the step (including gravity).
    ///
    /// One instance per horse. Each peer runs it only for the horse it controls.
    /// </summary>
    public sealed class ChainTether
    {
        /// <summary>Rope length. 3D separation should never exceed this.</summary>
        public float Length = 6f;

        /// <summary>How fast an idle horse slides when towed, and how fast the puller may walk out.</summary>
        public float TowSpeed = 1.9f;

        /// <summary>Distance before full length over which the rope takes hold.</summary>
        public float EngageBand = 0.3f;

        /// <summary>Wish component along the rope that counts as the rider driving outward.</summary>
        const float DriveThreshold = 0.15f;

        /// <summary>Partner motion below this (m/s along the rope) does not count as paying the rope out.</summary>
        const float FollowDeadzone = 0.2f;

        public float Separation { get; private set; }
        public bool Taut { get; private set; }

        /// <summary>This horse is dead weight on the end of the rope and is being dragged.</summary>
        public bool BeingTowed { get; private set; }

        /// <summary>Speed the rope is currently taking off this horse, in m/s.</summary>
        public float Hold { get; private set; }

        MAnimal _self;
        Vector3 _wish;
        Vector3 _partnerPos;
        Vector3 _partnerWish;
        Vector3 _partnerVel;
        Vector3 _partnerSamplePos;
        bool _hasPartner;
        bool _hasPartnerSample;

        /// <summary>Subscribes to the animal's movement step. Safe to call repeatedly.</summary>
        public void Bind(MAnimal self)
        {
            if (_self == self)
                return;

            if (_self != null)
                _self.PostStateMovement -= Apply;

            _self = self;
            ClearMotion();

            if (_self != null)
                _self.PostStateMovement += Apply;
        }

        public void Release()
        {
            Bind(null);
            _hasPartner = false;
            Taut = false;
            BeingTowed = false;
            Hold = 0f;
        }

        /// <summary>Forgets motion history, e.g. after a teleport, so no phantom stretch is seen.</summary>
        public void ClearMotion()
        {
            Hold = 0f;
            _partnerVel = Vector3.zero;
            _hasPartnerSample = false;
        }

        /// <summary>
        /// Feed the current frame's state. <paramref name="wish"/> is this horse's rider intent
        /// and is used only to tell a driving horse from dead weight — it is never modified.
        /// </summary>
        public void Track(Vector3 wish, Vector3 partnerPos, Vector3 partnerWish)
        {
            float dt = Time.deltaTime;
            if (_hasPartnerSample && dt > 1e-5f)
            {
                Vector3 raw = partnerPos - _partnerSamplePos;
                if (raw.sqrMagnitude > 64f)
                {
                    _partnerVel = Vector3.zero;
                }
                else
                {
                    raw /= dt;
                    _partnerVel = Vector3.Lerp(_partnerVel, raw, 0.45f);
                }
            }

            _partnerSamplePos = partnerPos;
            _hasPartnerSample = true;

            _wish = wish;
            _partnerPos = partnerPos;
            _partnerWish = partnerWish;
            _hasPartner = true;
        }

        public void NoPartner()
        {
            _hasPartner = false;
            Taut = false;
            BeingTowed = false;
            Hold = 0f;
        }

        /// <summary>
        /// Runs inside Malbers' fixed movement step, after root motion and gravity have been
        /// written into <see cref="MAnimal.AdditivePosition"/>.
        /// </summary>
        void Apply(MAnimal self)
        {
            Hold = 0f;

            if (!_hasPartner || self == null)
                return;

            float dt = self.DeltaTime;
            if (dt <= 0f)
                return;

            Vector3 toPartner = _partnerPos - self.transform.position;
            float sep = toPartner.magnitude;
            Separation = sep;

            if (sep < 0.01f)
            {
                Taut = false;
                BeingTowed = false;
                return;
            }

            Vector3 away = -toPartner / sep;

            float engage = Mathf.Clamp01((sep - (Length - Mathf.Max(0.05f, EngageBand))) / Mathf.Max(0.05f, EngageBand));
            Taut = engage > 0f;

            Vector3 demand = (self.AdditivePosition + self.InertiaPositionSpeed) / dt;
            float myOut = Vector3.Dot(demand, away);

            bool iDrive = Vector3.Dot(_wish, away) > DriveThreshold;
            bool partnerPulling = Vector3.Dot(_partnerWish, -away) > DriveThreshold;

            float follow = Vector3.Dot(_partnerVel, away);
            if (follow < FollowDeadzone)
                follow = 0f;

            float allowedOut = follow + (Length - sep) / dt;
            float hold = myOut > allowedOut ? myOut - allowedOut : 0f;

            float tow = Mathf.Max(0.4f, TowSpeed);
            if (!iDrive && partnerPulling && Mathf.Abs(myOut) < 0.55f)
                hold += tow * engage;

            Hold = hold;
            BeingTowed = !iDrive && partnerPulling && hold > 0.01f;

            if (hold > 0.001f)
                self.AdditivePosition += -away * (hold * dt);

            ClampToLength(self);
        }

        /// <summary>Hard 3D cap so jump and fall cannot stretch the rope past <see cref="Length"/>.</summary>
        void ClampToLength(MAnimal self)
        {
            Vector3 next = self.transform.position + self.AdditivePosition;
            Vector3 fromPartner = next - _partnerPos;
            float sep = fromPartner.magnitude;
            if (sep <= Length || sep < 0.0001f)
                return;

            Vector3 clamped = _partnerPos + fromPartner * (Length / sep);
            self.AdditivePosition += clamped - next;
            Taut = true;
        }
    }
}
