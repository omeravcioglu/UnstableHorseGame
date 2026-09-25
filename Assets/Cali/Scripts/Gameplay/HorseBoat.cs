using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Rowboat: when enough horses are inside the Carry Boundary, paddles row
    /// and the boat travels to a stop. Horses in the boundary ride with it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(800)]
    [AddComponentMenu("Cali/Horse Boat")]
    public class HorseBoat : MonoBehaviour
    {
        [Header("Paddles")]
        [Tooltip("Leave empty to auto-find children named Paddle.")]
        public Transform paddleLeft;
        public Transform paddleRight;
        public float paddleAngle = 38f;
        public float paddleRate = 2.4f;
        public Vector3 paddleAxis = Vector3.right;

        [Header("Passengers")]
        [Tooltip("How many horses must be in the boundary before it launches.")]
        public int horsesToLaunch = 1;

        [Header("Travel")]
        [Tooltip("Empty transform where the boat should go. Or fill Stops for a path.")]
        public Transform destination;
        public Transform[] stops;
        public float moveSpeed = 3.2f;
        public float arriveDistance = 0.6f;
        [Tooltip("Turn the boat to face where it is going. Off if your mesh is imported on its side.")]
        public bool faceTravelDirection = false;
        public float turnSpeed = 2.2f;

        [Header("Water")]
        public float bobHeight = 0.08f;
        public float bobSpeed = 0.7f;

        MovingCarryZone _carry;
        Quaternion _leftRest;
        Quaternion _rightRest;
        Vector3 _restPos;
        Quaternion _restRot;
        float _bobPhase;
        float _stroke;
        bool _launched;
        bool _arrived;
        int _stopIndex;
        Rigidbody _rb;
        bool _hadGravity;
        bool _wasKinematic;
        bool _isProxy;

        /// <summary>
        /// Clients replicate the host pose. Local bob, launch and travel would diverge
        /// and give each player a different collider.
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
            _carry = GetComponent<MovingCarryZone>();
            if (_carry == null)
                _carry = gameObject.AddComponent<MovingCarryZone>();

            AutoFindPaddles();
            if (paddleLeft != null)
                _leftRest = paddleLeft.localRotation;
            if (paddleRight != null)
                _rightRest = paddleRight.localRotation;

            _restPos = transform.position;
            _restRot = transform.rotation;
            _bobPhase = StablePhase(transform);
            _rb = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            gameObject.isStatic = false;
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

            float dt = Time.deltaTime;
            int onboard = _carry != null ? _carry.PassengerCount : 0;
            if (!_launched && !_arrived && onboard >= Mathf.Max(1, horsesToLaunch))
                _launched = true;

            bool rowing = _launched && !_arrived;
            AnimatePaddles(rowing, dt);
            BobAndTravel(rowing, dt);
        }

        void AutoFindPaddles()
        {
            if (paddleLeft != null && paddleRight != null)
                return;

            var found = new System.Collections.Generic.List<Transform>();
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == transform)
                    continue;
                if (all[i].name.IndexOf("paddle", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                found.Add(all[i]);
            }

            if (paddleLeft == null && found.Count > 0)
                paddleLeft = found[0];
            if (paddleRight == null && found.Count > 1)
                paddleRight = found[1];
        }

        void AnimatePaddles(bool rowing, float dt)
        {
            if (rowing)
                _stroke += dt * paddleRate * Mathf.PI * 2f;
            else
                _stroke = Mathf.MoveTowards(_stroke, 0f, dt * 4f);

            float swing = Mathf.Sin(_stroke) * paddleAngle;
            if (!rowing)
                swing = 0f;

            Vector3 axis = paddleAxis.sqrMagnitude > 0.01f ? paddleAxis.normalized : Vector3.right;
            if (paddleLeft != null)
                paddleLeft.localRotation = _leftRest * Quaternion.AngleAxis(swing, axis);
            if (paddleRight != null)
                paddleRight.localRotation = _rightRest * Quaternion.AngleAxis(-swing, axis);
        }

        void BobAndTravel(bool moving, float dt)
        {
            float bob = Mathf.Sin(Time.time * bobSpeed + _bobPhase) * bobHeight;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            if (moving)
            {
                Transform stop = CurrentStop();
                if (stop != null)
                {
                    Vector3 goal = stop.position;
                    Vector3 flat = goal - pos;
                    flat.y = 0f;
                    float dist = flat.magnitude;
                    if (dist <= arriveDistance)
                    {
                        _stopIndex++;
                        if (CurrentStop() == null)
                        {
                            _arrived = true;
                            _launched = false;
                        }
                    }
                    else
                    {
                        Vector3 dir = flat / Mathf.Max(dist, 0.0001f);
                        pos += dir * moveSpeed * dt;
                        if (faceTravelDirection)
                        {
                            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
                            rot = Quaternion.Slerp(rot, look, 1f - Mathf.Exp(-turnSpeed * dt));
                        }
                    }

                    _restPos.x = pos.x;
                    _restPos.z = pos.z;
                    _restPos.y = Mathf.MoveTowards(_restPos.y, goal.y, moveSpeed * 0.35f * dt);
                    _restRot = rot;
                }
            }

            pos.x = _restPos.x;
            pos.z = _restPos.z;
            pos.y = _restPos.y + bob;
            transform.SetPositionAndRotation(pos, rot);
        }

        Transform CurrentStop()
        {
            if (stops != null && _stopIndex < stops.Length && stops[_stopIndex] != null)
                return stops[_stopIndex];
            if ((stops == null || stops.Length == 0) && _stopIndex == 0)
                return destination;
            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);
            Vector3 from = transform.position;
            if (stops != null)
            {
                for (int i = 0; i < stops.Length; i++)
                {
                    if (stops[i] == null)
                        continue;
                    Gizmos.DrawLine(from, stops[i].position);
                    Gizmos.DrawWireSphere(stops[i].position, 0.4f);
                    from = stops[i].position;
                }
            }
            else if (destination != null)
            {
                Gizmos.DrawLine(from, destination.position);
                Gizmos.DrawWireSphere(destination.position, 0.4f);
            }
        }

        static float StablePhase(Transform t)
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
                return (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
            }
        }
    }
}
