using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Bob like water. Horses inside the Carry Boundary move with this object.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(800)]
    [AddComponentMenu("Cali/Water Bob")]
    public class WaterBob : MonoBehaviour
    {
        [Tooltip("How far it rises and sinks, in meters.")]
        public float height = 0.12f;

        [Tooltip("How fast it bobs. Around 0.6–1.2 looks like gentle water.")]
        public float speed = 0.85f;

        [Tooltip("Max tilt in degrees. 0 = up/down only.")]
        public float tilt = 4f;

        [Tooltip("Offset timing so nearby objects do not bob in sync. Uses a stable id so every player sees the same motion.")]
        public bool randomPhase = true;

        Rigidbody _rb;
        Vector3 _restLocalPos;
        Quaternion _restLocalRot;
        float _phase;
        float _phaseTilt;
        bool _hadGravity;
        bool _wasKinematic;
        bool _isProxy;

        /// <summary>
        /// Clients replicate the host pose. Local bob would pick a different phase and
        /// Time.time, so colliders would not match between players.
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
                _rb.useGravity = false;
                _rb.isKinematic = true;
                _rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _restLocalPos = transform.localPosition;
            _restLocalRot = transform.localRotation;
            if (randomPhase)
            {
                int h = StableHash(transform);
                _phase = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
                _phaseTilt = ((h >> 8) & 0xFFFF) / 65535f * Mathf.PI * 2f;
            }
            else
            {
                _phase = 0f;
                _phaseTilt = 1.3f;
            }
            if (GetComponent<MovingCarryZone>() == null)
                gameObject.AddComponent<MovingCarryZone>();
        }

        void OnEnable()
        {
            if (_rb == null)
                return;
            _hadGravity = _rb.useGravity;
            _wasKinematic = _rb.isKinematic;
            _rb.useGravity = false;
            _rb.isKinematic = true;
            if (_isProxy)
                _rb.interpolation = RigidbodyInterpolation.None;
        }

        void OnDisable()
        {
            if (_rb == null || _isProxy)
                return;
            _rb.useGravity = _hadGravity;
            _rb.isKinematic = _wasKinematic;
        }

        void LateUpdate()
        {
            if (_isProxy)
                return;

            float t = Time.time * speed;
            float bob = Mathf.Sin(t + _phase) * height
                        + Mathf.Sin(t * 1.73f + _phase * 0.6f) * height * 0.28f;

            Quaternion wobble = Quaternion.Euler(
                Mathf.Sin(t * 0.91f + _phaseTilt) * tilt,
                0f,
                Mathf.Cos(t * 1.13f + _phaseTilt) * tilt);

            transform.localPosition = _restLocalPos + new Vector3(0f, bob, 0f);
            transform.localRotation = _restLocalRot * wobble;
        }

        /// <summary>Hierarchy hash that is the same on every peer (not string.GetHashCode).</summary>
        static int StableHash(Transform t)
        {
            unchecked
            {
                int h = 17;
                while (t != null)
                {
                    string n = t.name;
                    for (int i = 0; i < n.Length; i++)
                        h = h * 31 + n[i];
                    h = h * 31 + t.GetSiblingIndex();
                    t = t.parent;
                }
                return h;
            }
        }
    }
}
