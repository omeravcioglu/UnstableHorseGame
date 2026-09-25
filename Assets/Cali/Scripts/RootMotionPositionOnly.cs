using System.Collections.Generic;
using UnityEngine;

namespace Cali
{
    /// <summary>
    /// Root motion is off. The object walks straight between the scene points you assign.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-400)]
    public class RootMotionPositionOnly : MonoBehaviour
    {
        [SerializeField] Animator animator;

        [Header("Path")]
        [Tooltip("Place empties in the scene and assign them here, in order.")]
        public Transform[] points;

        [Tooltip("Seconds to travel the full path (from the start through the last point).")]
        public float duration = 6f;

        public bool playOnStart = true;
        public bool loop = true;
        public bool faceMovement = true;
        public bool useUnscaledTime = true;

        [Tooltip("How quickly facing catches the path. Lower = slower, wider turns.")]
        public float turnDamp = 2.2f;

        [Tooltip("Meters to look ahead for facing. Keep small so short gaps are not cut.")]
        public float lookAhead = 2f;

        [Tooltip("Extra virtual meters on every segment so close points take longer.")]
        public float evenOut = 18f;

        CharacterController _cc;
        Rigidbody _rb;
        float _elapsed;
        bool _playing;
        bool _driven;
        Vector3 _spawnPos;
        Quaternion _spawnRot;
        Vector3 _startPos;
        Vector3 _lookDir;
        bool _hasLookDir;
        bool _cachedSpawn;
        Vector3[] _knots;
        float[] _pace;
        float _totalLen;
        float _paceLen;

        public Animator Animator => animator;

        void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            _cc = GetComponent<CharacterController>();
            _rb = GetComponent<Rigidbody>();
            CacheSpawn();
            FreezeDrivers();

            if (animator != null)
                animator.applyRootMotion = false;
        }

        void Start()
        {
            CacheSpawn();
            HoldAtSpawn();
            if (playOnStart)
                Play();
        }

        public void HoldAtSpawn()
        {
            CacheSpawn();
            FreezeDrivers();
            _playing = false;
            _driven = false;
            transform.SetPositionAndRotation(_spawnPos, _spawnRot);
        }

        public void Play()
        {
            CacheSpawn();
            FreezeDrivers();
            transform.SetPositionAndRotation(_spawnPos, _spawnRot);
            _elapsed = 0f;
            _startPos = _spawnPos;
            _hasLookDir = false;
            RebuildPolyline();
            _playing = _knots != null && _knots.Length > 0;
            _driven = false;
            PrepareAnimator();
            ApplyAt(_elapsed);
        }

        /// <summary>Lockstep with the camera: same elapsed seconds, no extra Update drift.</summary>
        public void SetElapsed(float seconds)
        {
            if (_knots == null)
                Play();
            _driven = true;
            _playing = false;
            _elapsed = Mathf.Max(0f, seconds);
            ApplyPace(PaceAt(_elapsed));
        }

        public void Stop()
        {
            _playing = false;
            _driven = false;
        }

        void Update()
        {
            if (_driven || !_playing)
                return;

            int count = CountPoints();
            if (count == 0)
            {
                _playing = false;
                return;
            }

            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt > 0.1f)
                dt = 0.1f;
            _elapsed += dt;
            float pace = PaceAt(_elapsed);
            if (!loop && _paceLen > 0.01f && pace >= _paceLen - 0.001f)
                _playing = false;
            ApplyPace(pace);
        }

        void ApplyAt(float elapsed)
        {
            ApplyPace(PaceAt(elapsed));
        }

        void ApplyPace(float pace)
        {
            if (_knots == null)
                RebuildPolyline();
            MoveTo(SamplePace(pace));
            if (faceMovement)
                FaceToward(pace);
        }

        float PaceAt(float elapsed)
        {
            if (_paceLen < 0.0001f)
                return 0f;
            float speed = _paceLen / Mathf.Max(0.05f, duration);
            float pace = elapsed * speed;
            return loop ? Mathf.Repeat(pace, _paceLen) : Mathf.Clamp(pace, 0f, _paceLen);
        }

        void PrepareAnimator()
        {
            if (animator == null)
                return;
            animator.applyRootMotion = false;
            animator.updateMode = useUnscaledTime
                ? AnimatorUpdateMode.UnscaledTime
                : AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            TrySetFloat(animator, "Vertical", 1f);
            TrySetFloat(animator, "VerticalRaw", 1f);
            TrySetFloat(animator, "Speed", 1f);
            TrySetBool(animator, "Grounded", true);
        }

        void CacheSpawn()
        {
            if (_cachedSpawn)
                return;
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
            _startPos = _spawnPos;
            _cachedSpawn = true;
        }

        void FreezeDrivers()
        {
            if (_cc != null)
                _cc.enabled = false;

            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.detectCollisions = false;
                _rb.constraints = RigidbodyConstraints.None;
            }

            var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null || b == this)
                    continue;
                string typeName = b.GetType().Name;
                if (typeName == "MAnimal" ||
                    typeName.Contains("AnimalAI") ||
                    typeName.Contains("AIControl") ||
                    typeName.Contains("MInput") ||
                    typeName.Contains("MRider"))
                    b.enabled = false;
            }
        }

        static void TrySetFloat(Animator anim, string name, float value)
        {
            foreach (var p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float && p.name == name)
                {
                    anim.SetFloat(name, value);
                    return;
                }
            }
        }

        static void TrySetBool(Animator anim, string name, bool value)
        {
            foreach (var p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool && p.name == name)
                {
                    anim.SetBool(name, value);
                    return;
                }
            }
        }

        void OnAnimatorMove()
        {
            if (animator != null)
                animator.applyRootMotion = false;
        }

        void FaceToward(float pace)
        {
            float lookPace = pace + PaceLookAhead();
            if (loop && _paceLen > 0.01f)
                lookPace = Mathf.Repeat(lookPace, _paceLen);
            else
                lookPace = Mathf.Min(lookPace, _paceLen);
            Vector3 lookPoint = SamplePace(lookPace);
            Vector3 desired = lookPoint - transform.position;
            desired.y = 0f;
            if (desired.sqrMagnitude < 0.0001f)
                return;
            desired.Normalize();

            if (!_hasLookDir)
            {
                _lookDir = desired;
                _hasLookDir = true;
            }
            else
            {
                float step = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float k = 1f - Mathf.Exp(-Mathf.Max(0.15f, turnDamp) * step);
                _lookDir = Vector3.Slerp(_lookDir, desired, k);
                _lookDir.y = 0f;
                if (_lookDir.sqrMagnitude < 0.0001f)
                    return;
                _lookDir.Normalize();
            }

            transform.rotation = Quaternion.LookRotation(_lookDir, Vector3.up);
        }

        void MoveTo(Vector3 worldPos)
        {
            transform.position = worldPos;
        }

        float PaceLookAhead()
        {
            if (_paceLen < 0.0001f || _totalLen < 0.0001f)
                return Mathf.Max(0.25f, lookAhead);
            return Mathf.Max(0.25f, lookAhead) * (_paceLen / _totalLen);
        }

        void RebuildPolyline()
        {
            var knots = new List<Vector3>(8);
            knots.Add(_startPos);
            if (points != null)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i] == null)
                        continue;
                    Vector3 p = points[i].position;
                    if (GroundDist(knots[knots.Count - 1], p) < 0.35f)
                        continue;
                    knots.Add(p);
                }
            }

            _knots = knots.ToArray();
            int n = _knots.Length;
            int segments = n <= 1 ? 0 : (loop ? n : n - 1);
            _pace = new float[segments + 1];
            _pace[0] = 0f;
            _totalLen = 0f;
            _paceLen = 0f;
            float pad = Mathf.Max(0f, evenOut);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = _knots[i];
                Vector3 b = _knots[NextKnot(i, n)];
                float len = GroundDist(a, b);
                _totalLen += len;
                _paceLen += len + pad;
                _pace[i + 1] = _paceLen;
            }
        }

        Vector3 SamplePace(float pace)
        {
            if (_knots == null || _knots.Length == 0)
                return _startPos;
            if (_knots.Length == 1 || _paceLen < 0.0001f)
                return _knots[0];

            pace = loop ? Mathf.Repeat(pace, _paceLen) : Mathf.Clamp(pace, 0f, _paceLen);
            int segments = _pace.Length - 1;
            for (int i = 0; i < segments; i++)
            {
                float from = _pace[i];
                float to = _pace[i + 1];
                if (pace > to && i < segments - 1)
                    continue;

                float span = to - from;
                float s = span < 0.0001f ? 1f : (pace - from) / span;
                Vector3 a = _knots[i];
                Vector3 b = _knots[NextKnot(i, _knots.Length)];
                return Vector3.Lerp(a, b, Mathf.Clamp01(s));
            }

            return _knots[loop ? 0 : _knots.Length - 1];
        }

        int NextKnot(int i, int n) => loop && i + 1 == n ? 0 : i + 1;

        static float GroundDist(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        int CountPoints()
        {
            if (points == null)
                return 0;

            int n = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] != null)
                    n++;
            }

            if (n != points.Length && n > 0)
            {
                var packed = new Transform[n];
                int w = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i] != null)
                        packed[w++] = points[i];
                }
                points = packed;
            }

            return n;
        }
    }
}
