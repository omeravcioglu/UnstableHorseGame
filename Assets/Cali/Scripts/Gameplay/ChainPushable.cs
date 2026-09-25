using Cali.Combat;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Rigidbody crate/box the horse chain can slide and drag for puzzles.
    /// Horses can stand on it: Item is added to their Malbers ground mask, and Y is
    /// locked while a horse is on top so the crate does not sink.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ChainPushable : MonoBehaviour
    {
        [Tooltip("How strongly a sweeping chain accelerates this object.")]
        public float pushScale = 0.11f;

        [Tooltip("Cap on horizontal slide speed after a chain hit.")]
        public float maxSlideSpeed = 0.52f;

        [Tooltip("Keep the crate upright so it slides instead of tumbling.")]
        public bool freezeTilt = true;

        static readonly Collider[] Overlap = new Collider[12];

        Rigidbody _rb;
        Collider _col;
        int _pushFrame = -1;
        float _bestSqr;
        Vector3 _bestVel;
        Vector3 _bestPoint;
        bool _holdingHorse;
        bool _isProxy;

        public Rigidbody Body => _rb;

        /// <summary>
        /// On clients the crate is a replica of the host's simulation. Local gravity, chain sweeps
        /// and stand constraints all have to stop, or the two peers drift apart permanently — there
        /// is no correction path once they disagree.
        /// </summary>
        public void SetNetworkProxy(bool proxy)
        {
            _isProxy = proxy;

            if (_rb == null)
                _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                return;

            if (proxy)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                _rb.useGravity = false;
                // The pose is already a network sample; letting Unity interpolate it too would
                // smooth on top of smoothing.
                _rb.interpolation = RigidbodyInterpolation.None;
            }
            else
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        void Awake()
        {
            if (pushScale >= 0.2f)
                pushScale = 0.11f;
            if (maxSlideSpeed >= 0.9f)
                maxSlideSpeed = 0.52f;
            SetupPhysics();
            IncludeLayerInHorseGround();
        }

        void OnEnable()
        {
            IncludeLayerInHorseGround();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
                SetupPhysics();
        }

        void SetupPhysics()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();

            float scale = Mathf.Max(1f, transform.lossyScale.x);
            _rb.mass = Mathf.Max(280f, 70f * scale);
            _rb.linearDamping = Mathf.Max(8.5f, _rb.linearDamping);
            _rb.angularDamping = Mathf.Max(10f, _rb.angularDamping);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            if (freezeTilt)
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.useGravity = true;

            int item = LayerMask.NameToLayer("Item");
            if (item >= 0)
                gameObject.layer = item;

            _col = GetComponent<Collider>();
            if (_col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                var rend = GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    Bounds b = rend.bounds;
                    box.center = transform.InverseTransformPoint(b.center);
                    Vector3 size = transform.InverseTransformVector(b.size);
                    box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                }
                _col = box;
            }
        }

        static void IncludeLayerInHorseGround()
        {
            int item = LayerMask.NameToLayer("Item");
            if (item < 0)
                return;

            int bit = 1 << item;
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var animal = animals[i];
                if (animal == null || animal.GetComponentInParent<ChainKillableEnemy>() != null)
                    continue;

                int mask = animal.groundLayer.Value;
                if ((mask & bit) == 0)
                    animal.groundLayer.Value = mask | bit;
            }
        }

        public void ConsiderChainSweep(Vector3 velocity, Vector3 worldPoint)
        {
            float sqr = velocity.sqrMagnitude;
            if (sqr < _bestSqr && _pushFrame == Time.frameCount)
                return;

            _pushFrame = Time.frameCount;
            _bestSqr = sqr;
            _bestVel = velocity;
            _bestPoint = worldPoint;
        }

        void FixedUpdate()
        {
            if (_rb == null || _isProxy)
                return;

            bool onTop = HorseStandingOnTop();
            if (onTop != _holdingHorse)
            {
                _holdingHorse = onTop;
                ApplyStandConstraints(onTop);
            }

            if (!onTop)
                return;

            Vector3 v = _rb.linearVelocity;
            if (v.y < 0f)
                _rb.linearVelocity = new Vector3(v.x, 0f, v.z);
        }

        void ApplyStandConstraints(bool horseOnTop)
        {
            var tilt = freezeTilt ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.None;
            _rb.constraints = horseOnTop
                ? tilt | RigidbodyConstraints.FreezePositionY
                : tilt;
        }

        bool HorseStandingOnTop()
        {
            if (_col == null)
                _col = GetComponent<Collider>();
            if (_col == null || !_col.enabled)
                return false;

            Bounds b = _col.bounds;
            Vector3 center = new Vector3(b.center.x, b.max.y + 0.28f, b.center.z);
            Vector3 half = new Vector3(
                Mathf.Max(0.25f, b.extents.x * 0.92f),
                0.5f,
                Mathf.Max(0.25f, b.extents.z * 0.92f));

            int count = Physics.OverlapBoxNonAlloc(
                center,
                half,
                Overlap,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = Overlap[i];
                if (hit == null || hit == _col || hit.transform.root == transform.root)
                    continue;

                var animal = hit.GetComponentInParent<MAnimal>();
                if (animal != null && animal.GetComponentInParent<ChainKillableEnemy>() == null)
                    return true;
            }

            return false;
        }

        void LateUpdate()
        {
            if (_rb == null || _isProxy || _pushFrame != Time.frameCount)
                return;

            Vector3 vel = _bestVel;
            vel.y *= 0.15f;
            float speed = vel.magnitude;
            if (speed < 0.05f)
                return;

            // Chain reports in Update; scale by dt so this is acceleration, not a 60 Hz snap.
            Vector3 impulse = vel.normalized * (speed * pushScale * Time.deltaTime);
            _rb.AddForceAtPosition(impulse, _bestPoint, ForceMode.VelocityChange);
            ClampSlide();
            _bestSqr = 0f;
        }

        void ClampSlide()
        {
            Vector3 v = _rb.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float max = Mathf.Max(0.5f, maxSlideSpeed);
            if (flat.magnitude > max)
            {
                flat = flat.normalized * max;
                _rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }
        }
    }
}
