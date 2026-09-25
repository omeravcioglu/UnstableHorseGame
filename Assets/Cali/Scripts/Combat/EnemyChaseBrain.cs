using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Simple chase: walk toward nearest horse, stop in a band.
    /// Humanoids hold at spear range; wolves close in. Malbers AI stays disabled.
    /// </summary>
    public class EnemyChaseBrain : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 3.2f;
        public float turnSpeed = 8f;
        public float stopDistance = 2.5f;
        public float groundProbe = 2.5f;
        public float groundClearance = 0.02f;

        [Header("Wall Blocking")]
        [Tooltip("Sweep radius. Taken from the root CapsuleCollider when there is one.")]
        public float bodyRadius = 0.35f;

        [Tooltip("Sweep capsule height. Taken from the root CapsuleCollider when there is one.")]
        public float bodyHeight = 2f;

        [Tooltip("Lifts the sweep off the floor so kerbs and small steps stay walkable.")]
        public float stepClearance = 0.28f;

        [Tooltip("Gap kept between the chaser and a wall it slid into.")]
        public float wallSkin = 0.04f;

        [Tooltip("Most a chaser may be pushed out of geometry in one frame.")]
        public float maxDepenetration = 0.75f;

        static readonly RaycastHit[] SweepHits = new RaycastHit[16];
        static readonly Collider[] OverlapBuf = new Collider[16];

        CapsuleCollider _capsule;
        ChainKillableEnemy _enemy;
        MAnimal _animal;
        CharacterController _cc;
        Rigidbody _rb;
        EnemySpearThrower _thrower;
        Animator _animator;
        bool _defaultsApplied;
        int _groundMask = -1;
        int _verticalHash;
        int _speedHash;
        float _nextGroundSnap;
        bool SimpleHumanoid => _enemy != null && _enemy.kind == EnemyKind.Humanoid;

        void Awake()
        {
            _enemy = GetComponent<ChainKillableEnemy>();
            _animal = GetComponent<MAnimal>();
            _cc = GetComponent<CharacterController>();
            _rb = GetComponent<Rigidbody>();
            if (_rb == null && _animal != null)
                _rb = _animal.RB;
            _thrower = GetComponent<EnemySpearThrower>();
            _animator = GetComponentInChildren<Animator>();

            _capsule = GetComponent<CapsuleCollider>();
            if (_capsule != null)
            {
                float scale = Mathf.Max(0.01f, transform.lossyScale.y);
                bodyRadius = Mathf.Max(0.05f, _capsule.radius * scale);
                bodyHeight = Mathf.Max(bodyRadius * 2f, _capsule.height * scale);
            }

            _verticalHash = Animator.StringToHash("Vertical");
            _speedHash = Animator.StringToHash("Speed");

            if (SimpleHumanoid)
            {
                _animal = null;
                if (_cc != null)
                    _cc.enabled = false;
                if (_animator != null)
                    _animator.applyRootMotion = false;
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            _groundMask = Physics.DefaultRaycastLayers;
            if (enemyLayer >= 0)
                _groundMask &= ~(1 << enemyLayer);

            DisableMalbersAi();
            UnfreezeMotion();
            ApplyKindDefaults();
        }

        void ApplyKindDefaults()
        {
            if (_defaultsApplied || _enemy == null)
                return;
            _defaultsApplied = true;

            if (_enemy.kind == EnemyKind.Humanoid)
            {
                moveSpeed = 2.8f;
                var thrower = GetComponent<EnemySpearThrower>();
                float throwRange = thrower != null ? thrower.range : 28f;
                stopDistance = Mathf.Clamp(throwRange * 0.75f, 12f, 22f);
            }
            else
            {
                moveSpeed = 3.5f;
                stopDistance = 2.5f;
            }
        }

        void DisableMalbersAi()
        {
            var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var b in behaviours)
            {
                if (b == null || b == this)
                    continue;

                var typeName = b.GetType().Name;
                if (typeName.Contains("AnimalAI") ||
                    typeName.Contains("AIControl") ||
                    typeName.Contains("AIBrain") ||
                    typeName.Contains("MAnimalAI"))
                {
                    b.enabled = false;
                }
            }
        }

        void UnfreezeMotion()
        {
            if (_animal != null)
            {
                _animal.DisablePosition = false;
                _animal.DisableRotation = false;
            }
        }

        void Update()
        {
            EnemyCrowdSim.EnsureTicked();

            if (!ChainKillableEnemy.ShouldSimulateAi)
            {
                if (_cc != null)
                    _cc.enabled = false;
                return;
            }

            if (_enemy != null && _enemy.IsDead)
            {
                StopMotion();
                enabled = false;
                return;
            }

            var lod = _enemy != null ? _enemy.Lod : EnemyLod.Hot;
            if (lod >= EnemyLod.Cold)
            {
                StopMotion();
                return;
            }

            float detect = _enemy != null ? _enemy.DetectRadius : 14f;
            var target = EnemyCombatTargets.FindNearestHorseInRange(transform.position, detect);
            if (target == null)
            {
                StopMotion();
                return;
            }

            if (_thrower != null && _thrower.IsBusy)
            {
                StopMotion();
                Vector3 face = target.position - transform.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.001f)
                    Face(face.normalized);
                return;
            }

            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.001f)
            {
                StopMotion();
                return;
            }

            Vector3 dir = to / dist;
            Face(dir);

            if (dist <= stopDistance)
            {
                StopMotion();
                return;
            }

            bool cheap = lod != EnemyLod.Hot || SimpleHumanoid;
            Move(dir, cheap);
        }

        void Face(Vector3 dir)
        {
            var look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        void Move(Vector3 dir, bool cheap)
        {
            if (!cheap && !SimpleHumanoid && _animal != null && _animal.enabled)
            {
                _animal.Move(dir);
                return;
            }

            SetWalkAnim(true);
            Vector3 delta = dir * (moveSpeed * Time.deltaTime);

            if (_cc != null && _cc.enabled)
            {
                Vector3 vel = delta;
                vel.y = -9.81f * Time.deltaTime;
                _cc.Move(vel);
                return;
            }

            Vector3 next = ResolveMove(Depenetrate(transform.position), delta);
            if (Time.time >= _nextGroundSnap)
            {
                _nextGroundSnap = Time.time + 0.12f;
                next = SnapToGround(next);
            }

            transform.position = next;
        }

        /// <summary>
        /// Sweeps the body capsule along <paramref name="delta"/> and slides along whatever it hits.
        /// Without this the humanoid path is a raw transform write, so chasers walk into walls.
        /// </summary>
        Vector3 ResolveMove(Vector3 from, Vector3 delta)
        {
            delta.y = 0f;
            float remaining = delta.magnitude;
            if (remaining < 1e-5f)
                return from;

            Vector3 dir = delta / remaining;

            // Two passes: one to hit the wall, one to travel along it around a corner.
            for (int pass = 0; pass < 2; pass++)
            {
                if (!SweepBody(from, dir, remaining + wallSkin, out RaycastHit hit))
                    return from + dir * remaining;

                float travel = Mathf.Max(0f, hit.distance - wallSkin);
                from += dir * travel;
                remaining -= travel;
                if (remaining < 1e-5f)
                    return from;

                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude < 1e-6f)
                    return from;
                normal.Normalize();

                Vector3 slide = dir * remaining;
                slide -= normal * Vector3.Dot(slide, normal);
                remaining = slide.magnitude;
                if (remaining < 1e-5f)
                    return from;
                dir = slide / remaining;
            }

            return from;
        }

        /// <summary>
        /// Capsule spanning the body, lifted clear of the floor so the sweep reacts to walls rather
        /// than to the ground it is standing on.
        /// </summary>
        void BodyCapsule(Vector3 footPos, out Vector3 p0, out Vector3 p1, out float radius)
        {
            radius = Mathf.Max(0.05f, bodyRadius);
            float h = Mathf.Max(radius * 2f, bodyHeight);
            float bottom = radius + Mathf.Max(0f, stepClearance);
            p0 = footPos + Vector3.up * bottom;
            p1 = footPos + Vector3.up * Mathf.Max(h - radius, bottom);
        }

        /// <summary>
        /// Pushes the chaser horizontally out of geometry it is already inside. Needed because a
        /// sweep that starts overlapping reports distance 0 with an unusable normal, so an enemy
        /// that got embedded could otherwise never escape.
        /// </summary>
        Vector3 Depenetrate(Vector3 footPos)
        {
            if (_capsule == null)
                return footPos;

            BodyCapsule(footPos, out Vector3 p0, out Vector3 p1, out float r);
            int count = Physics.OverlapCapsuleNonAlloc(
                p0, p1, r, OverlapBuf, _groundMask, QueryTriggerInteraction.Ignore);
            if (count == 0)
                return footPos;

            Transform root = transform.root;
            Quaternion rot = transform.rotation;
            Vector3 offset = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                var col = OverlapBuf[i];
                if (col == null || col.transform.root == root)
                    continue;

                if (!Physics.ComputePenetration(
                        _capsule, footPos + offset, rot,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 dir, out float dist))
                    continue;

                // Skip floors and slopes; only walls should displace the chaser sideways.
                if (Mathf.Abs(dir.y) > 0.5f)
                    continue;

                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f)
                    continue;

                offset += dir.normalized * (dist + wallSkin);
            }

            float max = Mathf.Max(0f, maxDepenetration);
            if (offset.sqrMagnitude > max * max)
                offset = offset.normalized * max;

            return footPos + offset;
        }

        bool SweepBody(Vector3 footPos, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;

            BodyCapsule(footPos, out Vector3 p0, out Vector3 p1, out float r);

            int count = Physics.CapsuleCastNonAlloc(
                p0, p1, r, dir, SweepHits, distance, _groundMask, QueryTriggerInteraction.Ignore);

            Transform root = transform.root;
            float bestDist = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                var hit = SweepHits[i];
                if (hit.collider == null || hit.collider.transform.root == root)
                    continue;
                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            return found;
        }

        void StopMotion()
        {
            if (!SimpleHumanoid && _animal != null && _animal.enabled)
                _animal.Move(Vector3.zero);

            SetWalkAnim(false);

            if (_rb != null && !_rb.isKinematic)
            {
                var v = _rb.linearVelocity;
                _rb.linearVelocity = new Vector3(0f, v.y, 0f);
            }
        }

        void SetWalkAnim(bool walking)
        {
            if (_animator == null || !_animator.enabled)
                return;

            float v = walking ? 1f : 0f;
            if (SimpleHumanoid)
                _animator.SetFloat(_speedHash, v);
            else if (_verticalHash != 0)
                _animator.SetFloat(_verticalHash, v);
        }

        Vector3 SnapToGround(Vector3 pos)
        {
            Vector3 origin = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, groundProbe, _groundMask, QueryTriggerInteraction.Ignore))
            {
                pos.y = hit.point.y + groundClearance;
            }

            return pos;
        }
    }
}
