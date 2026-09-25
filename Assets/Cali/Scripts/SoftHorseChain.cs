using System.Collections.Generic;
using Cali.Combat;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    public enum ChainPhysicsMode
    {
        SoftSpring,
        FixedLength
    }

    /// <summary>
    /// The rope you see between the two horses: a segmented Verlet simulation with obstacle
    /// collision that ignores horse colliders, stays above ground and keeps a stable
    /// attach-height band. Host/offline it also kills <see cref="ChainKillableEnemy"/> on the
    /// Enemy layer, slides <see cref="ChainPushable"/> crates and sweeps ragdolls.
    ///
    /// It never moves a horse. The rope's effect on the horses lives in
    /// <see cref="ChainTether"/>: a 3D max-length cap on jump, fall and travel.
    /// This component just supplies <see cref="maxLength"/> to it.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(LineRenderer))]
    public class SoftHorseChain : MonoBehaviour
    {
        [Header("Horses")]
        [Tooltip("Leave empty to auto-find 'Horse Realistic'.")]
        public MAnimal horseA;

        [Tooltip("Leave empty to auto-find 'Horse Unicorn'.")]
        public MAnimal horseB;

        [Header("Physics Mode")]
        [Tooltip("Soft Spring is the current system. Fixed Length keeps a constant chain length and sags under gravity.")]
        public ChainPhysicsMode physicsMode = ChainPhysicsMode.SoftSpring;

        [Tooltip("Gravity multiplier used only in Fixed Length mode.")]
        public float chainGravityScale = 1f;

        [Header("Soft Constraint")]
        [Tooltip("Rope length. Read by ChainTether, which is what actually limits the horses.")]
        public float maxLength = 6f;

        [Header("Rope Simulation")]
        [Range(8, 48)]
        public int segmentCount = 24;

        [Tooltip("Sphere radius used for obstacle collision at each rope point.")]
        public float collisionRadius = 0.12f;

        [Tooltip("Solver iterations per frame.")]
        [Range(1, 16)]
        public int constraintIterations = 8;

        [Range(0.8f, 1f)]
        public float damping = 0.95f;

        [Tooltip("Layers the chain collides with (horse bodies are always ignored).")]
        public LayerMask collisionMask = ~0;

        [Tooltip("How strongly points stay near the attach-height line (stops ground sag).")]
        [Range(0f, 1f)]
        public float heightKeep = 0.65f;

        [Tooltip("Extra clearance above detected ground.")]
        public float groundClearance = 0.15f;

        [Tooltip("How far down to search for ground under each point.")]
        public float groundProbe = 6f;

        [Header("Attach")]
        [Tooltip("Height above the horse root.")]
        public float attachHeight = 1.15f;

        [Tooltip("Forward from the horse root toward the head. Raise this to move the chain off the rump.")]
        public float attachForward = 0.7f;

        [Tooltip("Side offset in the horse's local space. Positive is the horse's right.")]
        public float attachSide = 0f;

        [Tooltip("Optional. If set, this transform is the chain end on Horse A instead of the offsets.")]
        public Transform attachPointA;

        [Tooltip("Optional. If set, this transform is the chain end on Horse B instead of the offsets.")]
        public Transform attachPointB;

        [Header("Visual")]
        public Color slackColor = new(0.35f, 0.22f, 0.12f, 1f);
        public Color tautColor = new(0.75f, 0.35f, 0.15f, 1f);
        public float lineWidth = 0.06f;

        [Tooltip("PretoriusLab Chain prefab (or any single-link mesh). Leave empty to keep the line.")]
        public GameObject linkPrefab;

        [Tooltip("World-space length of one visual piece after split. Long meshes are sliced to this.")]
        public float linkMeshLength = 0.35f;

        [Tooltip("0 = auto-slice the prefab along its long axis. >0 forces that many pieces.")]
        public int meshSplitCount = 0;

        public Vector3 linkScale = Vector3.one;

        [Tooltip("Extra rotation if the original prefab does not face Z-forward. Sliced pieces ignore this.")]
        public Vector3 linkEulerOffset = new(90f, 0f, 0f);

        [Tooltip("Twist every other link 90 degrees for a chain look.")]
        public bool alternateLinkTwist = true;

        public bool hideLineWhenUsingMesh = false;

        [Header("Enemy Kill")]
        [Tooltip("Layer(s) queried for chain kills (typically Enemy).")]
        public LayerMask enemyHitMask;

        [Tooltip("Minimum segment speed (m/s) required to kill.")]
        public float killSpeedThreshold = 7f;

        [Tooltip("Capsule radius around each rope segment for kill checks.")]
        public float killQueryRadius = 0.25f;

        [Header("Iron Visual")]
        [Tooltip("Visible hanging points. More points wrap cleaner around posts and walls.")]
        [Range(24, 72)]
        public int visualSegments = 48;

        [Tooltip("Extra gravity on the visible chain so it hangs like heavy iron.")]
        public float visualGravity = 4f;

        [Range(0.82f, 0.98f)]
        public float visualDamping = 0.9f;

        [Tooltip("Thickness of the iron mesh.")]
        public float visualThickness = 1f;

        [Tooltip("URP material for the iron FBX. Leave empty to use IronChain.mat.")]
        public Material ironMaterial;

        const float PushSpeedThreshold = 0.55f;
        const float PushQueryRadius = 0.4f;
        static readonly Vector3[] EscapeDirs =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        LineRenderer _line;
        Vector3[] _pos;
        Vector3[] _prev;
        Vector3[] _visPos;
        Vector3[] _visPrev;
        Vector3[] _visSmooth;
        Transform[] _links;
        Transform _linkRoot;
        Mesh[] _sliceMeshes;
        Material[] _linkMaterials;
        float _sliceLength;
        readonly IronChainMesh _iron = new IronChainMesh();
        bool _ironTried;
        bool _skipHeightBand;
        readonly Collider[] _overlap = new Collider[64];
        readonly RaycastHit[] _segmentHits = new RaycastHit[16];
        SphereCollider _sphereProbe;
        CapsuleCollider _capsuleProbe;
        Transform _sphereProbeTf;
        Transform _capsuleProbeTf;
        readonly Collider[] _enemyOverlap = new Collider[24];
        readonly RaycastHit[] _groundHits = new RaycastHit[16];
        Transform _rootA;
        Transform _rootB;
        float _nextChainRattle;
        bool _initialized;
        Vector3 _lastAttachA;
        Vector3 _lastAttachB;
        bool _hasLastAttach;
        float _horseSpeed;
        Vector3 _horseVel;
        float _settleUntil;

        public static SoftHorseChain Instance { get; private set; }

        public float CurrentDistance { get; private set; }
        public float RawDistance => StraightSep();

        void Awake()
        {
            if (CaliSinglePlayer.IsActiveScene)
            {
                enabled = false;
                return;
            }

            Instance = this;
            _line = GetComponent<LineRenderer>();
            SetupLine();
            TryAssignDefaultLinkPrefab();
            ResolveHorses();
            InitParticles();

            if (enemyHitMask.value == 0)
            {
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                if (enemyLayer >= 0)
                    enemyHitMask = 1 << enemyLayer;
            }

            int enemyColLayer = LayerMask.NameToLayer("Enemy");
            if (enemyColLayer >= 0)
                collisionMask &= ~(1 << enemyColLayer);
            StripWrapLayers();
        }

        void Update()
        {
            if (horseA == null || horseB == null || !_initialized)
                return;

            float dt = Mathf.Clamp(Time.smoothDeltaTime, 0.008f, 0.05f);
            if (dt <= 0f)
                return;

            bool worldAuth = true;
            if (Cali.Network.CoopSessionStarter.IsOnline)
            {
                var runner = Cali.Network.CoopSessionStarter.Runner;
                worldAuth = runner != null && runner.IsSharedModeMasterClient;
            }

            PinEndpoints();
            Vector3 attachA = _pos[0];
            Vector3 attachB = _pos[^1];
            if (_hasLastAttach)
            {
                Vector3 delta = 0.5f * ((attachA - _lastAttachA) + (attachB - _lastAttachB));
                delta.y = 0f;
                _horseVel = delta / dt;
                _horseSpeed = _horseVel.magnitude;
            }
            else
            {
                _horseVel = Vector3.zero;
                _horseSpeed = 0f;
            }

            _lastAttachA = attachA;
            _lastAttachB = attachB;
            _hasLastAttach = true;

            SimulateVerlet(dt);
            SolveConstraints();
            PinEndpoints();

            CurrentDistance = StraightSep();

            if (worldAuth)
            {
                DetectEnemyKills(dt);
                PushChainObjects(dt);
                SweepRagdolls(dt);
            }

            MaybePlayChainRattle(dt);
            SimulateVisual(dt);
            UpdateVisual();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _iron.Release();
            DestroyProbe(_sphereProbeTf);
            DestroyProbe(_capsuleProbeTf);
            _sphereProbe = null;
            _capsuleProbe = null;
            _sphereProbeTf = null;
            _capsuleProbeTf = null;
        }

        static void DestroyProbe(Transform probe)
        {
            if (probe == null)
                return;
            if (Application.isPlaying)
                Destroy(probe.gameObject);
            else
                DestroyImmediate(probe.gameObject);
        }

        void ResolveHorses()
        {
            if (horseA == null)
                horseA = FindAnimalByName("Horse Realistic");
            if (horseB == null)
                horseB = FindAnimalByName("Horse Unicorn");

            _rootA = horseA != null ? horseA.transform : null;
            _rootB = horseB != null ? horseB.transform : null;

            if (horseA == null || horseB == null)
                Debug.LogWarning("[SoftHorseChain] Missing horse reference(s).", this);
            else
                Debug.Log($"[SoftHorseChain] Linked '{horseA.name}' <-> '{horseB.name}'", this);
        }

        public void SetHorseA(MAnimal animal)
        {
            horseA = animal;
            _rootA = animal != null ? animal.transform : null;
            if (horseA != null && horseB != null)
                InitParticles();
        }

        public void SetHorseB(MAnimal animal)
        {
            horseB = animal;
            _rootB = animal != null ? animal.transform : null;
            if (horseA != null && horseB != null)
                InitParticles();
        }

        public void RebindHorses(MAnimal a, MAnimal b)
        {
            if (a != null)
                horseA = a;
            if (b != null)
                horseB = b;
            ResolveHorses();
            if (horseA != null && horseB != null)
                InitParticles();
        }

        static MAnimal FindAnimalByName(string objectName)
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

                if (exact && animal.gameObject.scene.name == playScene)
                    return animal;
                if (exact && exactFallback == null)
                    exactFallback = animal;
                if (contains && containsFallback == null)
                    containsFallback = animal;
            }

            return exactFallback != null ? exactFallback : containsFallback;
        }

        public void SnapToCurrentHorses()
        {
            _hasLastAttach = false;
            _horseVel = Vector3.zero;
            _horseSpeed = 0f;
            _settleUntil = Time.time + 0.8f;
            InitParticles();
            InitVisualParticles();
        }

        void InitParticles()
        {
            int n = Mathf.Clamp(segmentCount, 8, 48);
            segmentCount = n;
            if (_pos == null || _pos.Length != n)
            {
                _pos = new Vector3[n];
                _prev = new Vector3[n];
            }

            if (horseA == null || horseB == null)
                return;

            Vector3 a = AttachPoint(horseA);
            Vector3 b = AttachPoint(horseB);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                _pos[i] = Vector3.Lerp(a, b, t);
                _prev[i] = _pos[i];
            }

            _line.positionCount = n;
            _initialized = true;
            InitVisualParticles();
        }

        Vector3 AttachPoint(MAnimal animal)
        {
            if (animal == null)
                return transform.position;

            if (animal == horseA && attachPointA != null)
                return attachPointA.position;
            if (animal == horseB && attachPointB != null)
                return attachPointB.position;

            return animal.transform.TransformPoint(new Vector3(attachSide, attachHeight, attachForward));
        }

        void PinEndpoints()
        {
            Vector3 a = AttachPoint(horseA);
            Vector3 b = AttachPoint(horseB);
            _pos[0] = a;
            _prev[0] = a;
            _pos[^1] = b;
            _prev[^1] = b;
        }

        void InitVisualParticles()
        {
            int n = Mathf.Clamp(visualSegments, 24, 72);
            visualSegments = n;
            if (_visPos == null || _visPos.Length != n)
            {
                _visPos = new Vector3[n];
                _visPrev = new Vector3[n];
                _visSmooth = new Vector3[n];
            }
            if (horseA == null || horseB == null)
                return;

            Vector3 a = AttachPoint(horseA);
            Vector3 b = AttachPoint(horseB);
            float span = Vector3.Distance(a, b);
            float length = Mathf.Max(0.5f, maxLength);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                _visPos[i] = HangPoint(a, b, t, span, length);
                _visPrev[i] = _visPos[i];
            }
        }

        void SimulateVisual(float dt)
        {
            if (horseA == null || horseB == null)
                return;
            if (_visPos == null || _visPos.Length != Mathf.Clamp(visualSegments, 24, 72))
                InitVisualParticles();
            if (_visPos == null || _visPos.Length < 3)
                return;

            Vector3 a = AttachPoint(horseA);
            Vector3 b = AttachPoint(horseB);
            float span = Vector3.Distance(a, b);
            float length = Mathf.Max(0.5f, maxLength);
            float slack = Mathf.Clamp01(1f - span / length);
            // Close horses leave extra rope. Forcing rest-length + solid tests in that
            // pile makes the mesh thrash. Hang it, and only wrap solids when taut.
            bool piled = slack > 0.22f;
            bool taut = slack < 0.008f;
            bool hangOnly = piled || SuppressWrap();

            _visPos[0] = a;
            _visPrev[0] = a;
            _visPos[^1] = b;
            _visPrev[^1] = b;

            float settle = taut
                ? 1f
                : hangOnly
                    ? (piled ? Mathf.Lerp(0.35f, 0.55f, slack) : 1f)
                    : Mathf.Lerp(0.04f, 0.14f, slack);
            for (int i = 1; i < _visPos.Length - 1; i++)
            {
                float t = i / (float)(_visPos.Length - 1);
                Vector3 seed = taut ? Vector3.Lerp(a, b, t) : HangPoint(a, b, t, span, length);
                _visPos[i] = Vector3.Lerp(_visPos[i], seed, settle);
                if (taut || hangOnly)
                    _visPrev[i] = _visPos[i];
            }

            if (!hangOnly)
            {
                float damp = piled
                    ? Mathf.Lerp(0.72f, 0.62f, slack)
                    : Mathf.Lerp(0.88f, Mathf.Clamp(visualDamping, 0.82f, 0.98f), slack);
                float gravScale = piled ? visualGravity * 0.35f : visualGravity * (0.7f + slack);
                Vector3 gravity = Physics.gravity * (gravScale * dt * dt);
                for (int i = 1; i < _visPos.Length - 1; i++)
                {
                    Vector3 position = _visPos[i];
                    Vector3 velocity = (position - _visPrev[i]) * damp;
                    _visPrev[i] = position;
                    _visPos[i] = position + velocity + gravity;
                }
            }

            if (hangOnly)
            {
                ClampVisualGround();
                StabilizeVisualPath(a, b, span, length);
                return;
            }

            float rest = length / (_visPos.Length - 1);
            int iters = piled
                ? Mathf.Max(6, constraintIterations)
                : Mathf.Max(14, constraintIterations + _visPos.Length / 4);
            var bak = _pos;
            _skipHeightBand = true;
            for (int iter = 0; iter < iters; iter++)
            {
                _visPos[0] = a;
                _visPos[^1] = b;
                for (int i = 0; i < _visPos.Length - 1; i++)
                {
                    Vector3 p1 = _visPos[i];
                    Vector3 p2 = _visPos[i + 1];
                    Vector3 delta = p2 - p1;
                    float dist = delta.magnitude;
                    if (dist < 0.0001f)
                        continue;

                    // Close: only stop stretch. Taut: keep rest so it does not shrink
                    // into a bar. Mid slack: same as taut.
                    if (piled && dist <= rest)
                        continue;
                    if (!piled && dist <= rest && slack < 0.04f)
                        continue;
                    if (Mathf.Abs(dist - rest) < 0.00015f)
                        continue;

                    Vector3 correction = delta * ((dist - rest) / dist);
                    bool pinStart = i == 0;
                    bool pinEnd = i + 1 == _visPos.Length - 1;
                    if (pinStart && !pinEnd)
                        _visPos[i + 1] -= correction;
                    else if (pinEnd && !pinStart)
                        _visPos[i] += correction;
                    else
                    {
                        _visPos[i] += correction * 0.5f;
                        _visPos[i + 1] -= correction * 0.5f;
                    }
                }

                _pos = _visPos;
                if (piled)
                {
                    ClampVisualGround();
                }
                else
                {
                    SeparateVisualSelf(rest);
                    CollideInteriorPoints();
                    if ((iter & 1) == 1 || iter >= iters - 3)
                    {
                        ResolveSegmentSolids();
                        UnstickSegments();
                    }
                }
            }

            if (piled)
                SmoothVisualHang();

            _pos = bak;
            _skipHeightBand = false;
            _visPos[0] = a;
            _visPos[^1] = b;
            StabilizeVisualPath(a, b, span, length);
        }

        void StripWrapLayers()
        {
            ExcludeLayer(ref collisionMask, "Water");
            ExcludeLayer(ref collisionMask, "Animal");
            ExcludeLayer(ref collisionMask, "BodyPart");
            ExcludeLayer(ref collisionMask, "Enemy");
            ExcludeLayer(ref collisionMask, "UI");
            ExcludeLayer(ref collisionMask, "Ignore Raycast");
            ExcludeLayer(ref collisionMask, "TransparentFX");
        }

        static void ExcludeLayer(ref LayerMask mask, string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0)
                mask &= ~(1 << layer);
        }

        bool SuppressWrap()
        {
            if (Time.time < _settleUntil)
                return true;
            var coop = Cali.Network.CoopGameController.Instance;
            return coop != null && coop.Object != null && coop.Object.IsValid && coop.IsInIntro;
        }

        void StabilizeVisualPath(Vector3 a, Vector3 b, float span, float length)
        {
            if (_visPos == null || _visPos.Length < 3)
                return;

            float limit = Mathf.Max(length, 2f) + 1.5f;
            float limitSq = limit * limit;
            bool reset = false;
            for (int i = 1; i < _visPos.Length - 1; i++)
            {
                Vector3 p = _visPos[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
                {
                    reset = true;
                    break;
                }

                float t = i / (float)(_visPos.Length - 1);
                Vector3 chord = Vector3.Lerp(a, b, t);
                if ((p - chord).sqrMagnitude > limitSq)
                {
                    reset = true;
                    break;
                }
            }

            if (!reset)
                return;

            for (int i = 0; i < _visPos.Length; i++)
            {
                float t = i / (float)(_visPos.Length - 1);
                _visPos[i] = HangPoint(a, b, t, span, length);
                _visPrev[i] = _visPos[i];
            }
            _visPos[0] = a;
            _visPos[^1] = b;
            _visPrev[0] = a;
            _visPrev[^1] = b;
        }

        static Vector3 HangPoint(Vector3 a, Vector3 b, float t, float span, float length)
        {
            Vector3 chord = Vector3.Lerp(a, b, t);
            float sag = HangDepth(span, length);
            if (sag <= 0.001f)
                return chord;
            float drop = 4f * t * (1f - t);
            return chord + Vector3.down * (sag * drop);
        }

        static float HangDepth(float span, float length)
        {
            float extra = length - span;
            if (extra <= 0.001f)
                return 0f;

            float halfL = length * 0.5f;
            float halfD = span * 0.5f;
            float deep = halfL > halfD ? Mathf.Sqrt(halfL * halfL - halfD * halfD) : 0f;
            float shallow = Mathf.Sqrt(extra * 3f * Mathf.Max(span, 0.2f) / 8f);
            float k = Mathf.Clamp01(extra / (length * 0.22f));
            float sag = Mathf.Lerp(shallow, deep * 0.92f, k);
            // A deep pile between close horses hits the floor and then explodes sideways.
            return Mathf.Min(sag, 0.85f);
        }

        void SeparateVisualSelf(float rest)
        {
            if (_visPos == null)
                return;

            float minSep = Mathf.Min(SolidRadius() * 0.85f, Mathf.Max(0.04f, rest * 0.7f));
            float minSq = minSep * minSep;
            int n = _visPos.Length;
            for (int i = 1; i < n - 1; i++)
            {
                for (int j = i + 2; j < n - 1; j++)
                {
                    Vector3 d = _visPos[j] - _visPos[i];
                    float sq = d.sqrMagnitude;
                    if (sq >= minSq || sq < 1e-10f)
                        continue;

                    float dist = Mathf.Sqrt(sq);
                    Vector3 push = d * ((minSep - dist) / dist * 0.5f);
                    _visPos[i] -= push;
                    _visPos[j] += push;
                }
            }
        }

        void SmoothVisualHang()
        {
            if (_visPos == null || _visSmooth == null || _visSmooth.Length != _visPos.Length)
                return;

            int n = _visPos.Length;
            _visSmooth[0] = _visPos[0];
            _visSmooth[n - 1] = _visPos[n - 1];
            for (int i = 1; i < n - 1; i++)
                _visSmooth[i] = 0.25f * _visPos[i - 1] + 0.5f * _visPos[i] + 0.25f * _visPos[i + 1];

            for (int i = 1; i < n - 1; i++)
                _visPos[i] = _visSmooth[i];
        }

        void ClampVisualGround()
        {
            if (_visPos == null)
                return;
            for (int i = 1; i < _visPos.Length - 1; i++)
            {
                Vector3 before = _visPos[i];
                _pos[i] = _visPos[i];
                ClampAboveGround(i);
                _visPos[i] = _pos[i];
                AbsorbInward(i, before);
            }
        }

        void SimulateVerlet(float dt)
        {
            Vector3 a = _pos[0];
            Vector3 b = _pos[^1];

            for (int i = 1; i < _pos.Length - 1; i++)
            {
                float t = i / (float)(_pos.Length - 1);
                Vector3 targetHeightPoint = Vector3.Lerp(a, b, t);

                Vector3 position = _pos[i];
                Vector3 velocity = (position - _prev[i]) * damping;
                _prev[i] = position;

                Vector3 next = position + velocity;

                if (physicsMode == ChainPhysicsMode.FixedLength)
                    next += Physics.gravity * chainGravityScale * (dt * dt);
                else
                    next.y = Mathf.Lerp(next.y, targetHeightPoint.y, heightKeep);

                _pos[i] = next;
            }
        }

        void SolveConstraints()
        {
            float rest = maxLength / (_pos.Length - 1);
            float span = _pos != null && _pos.Length > 1
                ? Vector3.Distance(_pos[0], _pos[^1])
                : maxLength;
            bool piled = span < maxLength * 0.78f;

            for (int iter = 0; iter < constraintIterations; iter++)
            {
                for (int i = 0; i < _pos.Length - 1; i++)
                {
                    Vector3 p1 = _pos[i];
                    Vector3 p2 = _pos[i + 1];
                    Vector3 delta = p2 - p1;
                    float dist = delta.magnitude;
                    if (dist < 0.0001f)
                        continue;

                    if (physicsMode == ChainPhysicsMode.SoftSpring || piled)
                    {
                        if (dist <= rest)
                            continue;
                    }
                    else if (Mathf.Abs(dist - rest) < 0.0001f)
                    {
                        continue;
                    }

                    Vector3 correction = delta * ((dist - rest) / dist);
                    bool pinStart = i == 0;
                    bool pinEnd = i + 1 == _pos.Length - 1;

                    if (pinStart && !pinEnd)
                        _pos[i + 1] -= correction;
                    else if (pinEnd && !pinStart)
                        _pos[i] += correction;
                    else if (!pinStart && !pinEnd)
                    {
                        _pos[i] += correction * 0.5f;
                        _pos[i + 1] -= correction * 0.5f;
                    }
                }

                if (piled || SuppressWrap())
                {
                    for (int i = 1; i < _pos.Length - 1; i++)
                        ClampAboveGround(i);
                }
                else
                {
                    CollideInteriorPoints();
                    ResolveSegmentSolids();
                    UnstickSegments();
                }

                PinEndpoints();
            }

            CurrentDistance = 0f;
            for (int i = 0; i < _pos.Length - 1; i++)
                CurrentDistance += Vector3.Distance(_pos[i], _pos[i + 1]);
        }

        bool IsHorseCollider(Collider col)
        {
            if (col == null)
                return true;

            Transform t = col.transform;
            if (_rootA != null && (t == _rootA || t.IsChildOf(_rootA)))
                return true;
            if (_rootB != null && (t == _rootB || t.IsChildOf(_rootB)))
                return true;

            // Extra safety: anything with MAnimal on parents.
            return t.GetComponentInParent<MAnimal>() != null;
        }

        bool SkipChainCollider(Collider col)
        {
            if (col == null)
                return true;
            if (col == _sphereProbe || col == _capsuleProbe)
                return true;

            Transform t = col.transform;
            if (t == transform || t.IsChildOf(transform))
                return true;

            return IsHorseCollider(col) || ChainIgnore.IsIgnored(col);
        }

        bool IsVerticalPostObstacle(Collider col)
        {
            if (col == null || SkipChainCollider(col))
                return false;
            if (col.GetComponentInParent<ChainPushable>() != null)
                return true;

            var rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic)
                return false;
            if (col.GetComponentInParent<ChainKillableEnemy>() != null)
                return false;

            Bounds b = col.bounds;
            if (b.size.y < 0.35f)
                return false;
            if (b.size.x > 8f && b.size.z > 8f)
                return false;
            return b.size.x > 0.2f || b.size.z > 0.2f;
        }

        float SolidRadius()
        {
            return Mathf.Max(0.2f, collisionRadius, 0.12f * Mathf.Max(0.5f, visualThickness));
        }

        void CollideInteriorPoints()
        {
            if (_pos == null)
                return;

            for (int i = 1; i < _pos.Length - 1; i++)
            {
                Vector3 before = _pos[i];
                ResolveSolidCollision(i);
                ClampAboveGround(i);
                AbsorbInward(i, before);
            }
        }

        void ResolveSolidCollision(int index)
        {
            float radius = SolidRadius();
            Vector3 p = _pos[index];
            for (int pass = 0; pass < 2; pass++)
            {
                int count = Physics.OverlapSphereNonAlloc(
                    p, radius, _overlap, collisionMask, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    Collider col = _overlap[i];
                    if (SkipChainCollider(col))
                        continue;
                    if (IsGroundLike(col, p))
                        continue;
                    p = ExpelFromCollider(p, col, radius);
                }
            }

            _pos[index] = p;
        }

        void ResolveSegmentSolids()
        {
            if (_pos == null || _pos.Length < 2)
                return;

            float radius = SolidRadius();
            for (int i = 0; i < _pos.Length - 1; i++)
            {
                Vector3 a = _pos[i];
                Vector3 b = _pos[i + 1];
                int count = Physics.OverlapCapsuleNonAlloc(
                    a, b, radius, _overlap, collisionMask, QueryTriggerInteraction.Ignore);

                for (int c = 0; c < count; c++)
                {
                    Collider col = _overlap[c];
                    if (SkipChainCollider(col))
                        continue;

                    Vector3 mid = 0.5f * (a + b);
                    if (IsGroundLike(col, mid))
                        continue;

                    Vector3 push = SeparateCapsule(a, b, col, radius);
                    if (push.sqrMagnitude < 1e-8f)
                        continue;

                    if (i > 0)
                    {
                        Vector3 before = _pos[i];
                        _pos[i] += push;
                        AbsorbInward(i, before);
                    }
                    if (i + 1 < _pos.Length - 1)
                    {
                        Vector3 before = _pos[i + 1];
                        _pos[i + 1] += push;
                        AbsorbInward(i + 1, before);
                    }

                    a = _pos[i];
                    b = _pos[i + 1];
                }
            }
        }

        /// <summary>
        /// Sphere-cast each short link. Overlap tests miss when a segment tunnels
        /// through a wall between two particles; this slides those points back out.
        /// </summary>
        void UnstickSegments()
        {
            if (_pos == null || _pos.Length < 2)
                return;

            float radius = SolidRadius() * 0.9f;
            float skin = radius * 0.05f;

            for (int i = 0; i < _pos.Length - 1; i++)
            {
                Vector3 a = _pos[i];
                Vector3 b = _pos[i + 1];
                Vector3 delta = b - a;
                float len = delta.magnitude;
                if (len < radius * 0.2f)
                    continue;

                Vector3 dir = delta / len;
                float cast = Mathf.Max(0.001f, len - skin);
                int hits = Physics.SphereCastNonAlloc(
                    a + dir * 0.001f, radius, dir, _segmentHits, cast,
                    collisionMask, QueryTriggerInteraction.Ignore);

                float best = float.MaxValue;
                RaycastHit pick = default;
                bool found = false;
                for (int h = 0; h < hits; h++)
                {
                    var hit = _segmentHits[h];
                    if (hit.collider == null || SkipChainCollider(hit.collider))
                        continue;
                    if (IsGroundLike(hit.collider, hit.point))
                        continue;
                    if (hit.distance >= best)
                        continue;
                    best = hit.distance;
                    pick = hit;
                    found = true;
                }

                if (!found)
                    continue;

                Vector3 outPos = pick.point + pick.normal * radius;
                if (i > 0 && i < _pos.Length - 1)
                {
                    Vector3 before = _pos[i];
                    _pos[i] = Vector3.Lerp(_pos[i], outPos, 0.65f);
                    AbsorbInward(i, before);
                }
                if (i + 1 > 0 && i + 1 < _pos.Length - 1)
                {
                    Vector3 before = _pos[i + 1];
                    _pos[i + 1] = Vector3.Lerp(_pos[i + 1], outPos, 0.85f);
                    AbsorbInward(i + 1, before);
                }
            }

            for (int i = 1; i < _pos.Length - 1; i++)
                UnstickPointMotion(i, radius);
        }

        void UnstickPointMotion(int index, float radius)
        {
            Vector3[] prevArr = _skipHeightBand ? _visPrev : _prev;
            if (prevArr == null || index >= prevArr.Length)
                return;

            Vector3 from = prevArr[index];
            Vector3 to = _pos[index];
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 0.008f)
                return;

            Vector3 dir = delta / len;
            int hits = Physics.SphereCastNonAlloc(
                from, radius, dir, _segmentHits, len,
                collisionMask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            RaycastHit pick = default;
            bool found = false;
            for (int h = 0; h < hits; h++)
            {
                var hit = _segmentHits[h];
                if (hit.collider == null || SkipChainCollider(hit.collider))
                    continue;
                if (IsGroundLike(hit.collider, hit.point))
                    continue;
                if (hit.distance >= best)
                    continue;
                best = hit.distance;
                pick = hit;
                found = true;
            }

            if (!found)
                return;

            Vector3 before = _pos[index];
            _pos[index] = pick.point + pick.normal * radius;
            AbsorbInward(index, before);
        }

        void AbsorbInward(int index, Vector3 before)
        {
            Vector3[] prevArr = _skipHeightBand ? _visPrev : _prev;
            if (prevArr == null || index < 0 || index >= prevArr.Length || index >= _pos.Length)
                return;

            Vector3 pos = _pos[index];
            Vector3 correction = pos - before;
            if (correction.sqrMagnitude < 1e-10f)
                return;

            float inv = 1f / Mathf.Sqrt(correction.sqrMagnitude);
            Vector3 n = correction * inv;
            Vector3 vel = pos - prevArr[index];
            float vn = Vector3.Dot(vel, n);
            if (vn < 0f)
                vel -= n * vn;
            prevArr[index] = pos - vel;
        }

        void EnsureProbes()
        {
            if (_sphereProbe != null)
                return;

            int layer = LayerMask.NameToLayer("Ignore Raycast");
            if (layer < 0)
                layer = 2;

            var sphereGo = new GameObject("ChainSphereProbe");
            sphereGo.hideFlags = HideFlags.HideAndDontSave;
            sphereGo.layer = layer;
            _sphereProbeTf = sphereGo.transform;
            _sphereProbe = sphereGo.AddComponent<SphereCollider>();
            _sphereProbe.isTrigger = true;

            var capGo = new GameObject("ChainCapsuleProbe");
            capGo.hideFlags = HideFlags.HideAndDontSave;
            capGo.layer = layer;
            _capsuleProbeTf = capGo.transform;
            _capsuleProbe = capGo.AddComponent<CapsuleCollider>();
            _capsuleProbe.isTrigger = true;
            _capsuleProbe.direction = 1;
        }

        Vector3 SeparateCapsule(Vector3 a, Vector3 b, Collider col, float radius)
        {
            EnsureProbes();
            Vector3 axis = b - a;
            float len = axis.magnitude;
            Vector3 mid = 0.5f * (a + b);
            Quaternion rot = len > 1e-5f
                ? Quaternion.FromToRotation(Vector3.up, axis / len)
                : Quaternion.identity;

            _capsuleProbe.radius = radius;
            _capsuleProbe.height = Mathf.Max(radius * 2.01f, len + radius * 2f);
            _capsuleProbeTf.SetPositionAndRotation(mid, rot);

            if (Physics.ComputePenetration(
                    _capsuleProbe, mid, rot,
                    col, col.transform.position, col.transform.rotation,
                    out Vector3 dir, out float dist)
                && dist > 0f)
            {
                return dir * dist;
            }

            Vector3 expelled = ExpelFromCollider(mid, col, radius);
            return expelled - mid;
        }

        Vector3 ExpelFromCollider(Vector3 p, Collider col, float radius)
        {
            EnsureProbes();
            _sphereProbe.radius = radius;
            _sphereProbeTf.SetPositionAndRotation(p, Quaternion.identity);

            if (Physics.ComputePenetration(
                    _sphereProbe, p, Quaternion.identity,
                    col, col.transform.position, col.transform.rotation,
                    out Vector3 dir, out float dist)
                && dist > 0f)
            {
                return p + dir * dist;
            }

            Vector3 closest = col.ClosestPoint(p);
            Vector3 delta = p - closest;
            float sqr = delta.sqrMagnitude;
            if (sqr > 1e-8f)
            {
                float d = Mathf.Sqrt(sqr);
                if (d >= radius)
                    return p;
                return closest + delta * (radius / d);
            }

            if (!IsConvexSolid(col))
            {
                Bounds b = col.bounds;
                if (b.size.x > 8f && b.size.z > 8f)
                    return p;
                return ExpelBySurfaceCast(p, col, radius);
            }

            return ExpelFromConvexBounds(p, col, radius);
        }

        static bool IsConvexSolid(Collider col)
        {
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider)
                return true;
            if (col is MeshCollider mesh)
                return mesh.convex;
            return false;
        }

        Vector3 ExpelBySurfaceCast(Vector3 p, Collider col, float radius)
        {
            float reach = col.bounds.extents.magnitude + radius + 2f;
            float best = float.MaxValue;
            Vector3 bestPos = p;
            bool found = false;

            for (int i = 0; i < EscapeDirs.Length; i++)
            {
                Vector3 dir = EscapeDirs[i];
                Vector3 start = p + dir * reach;
                if (!Physics.Raycast(start, -dir, out RaycastHit hit, reach + 0.05f, collisionMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider != col)
                    continue;

                Vector3 candidate = hit.point + hit.normal * radius;
                float d = (candidate - p).sqrMagnitude;
                if (d >= best)
                    continue;

                best = d;
                bestPos = candidate;
                found = true;
            }

            return found ? bestPos : p;
        }

        static Vector3 ExpelFromConvexBounds(Vector3 p, Collider col, float radius)
        {
            var box = col as BoxCollider;
            if (box != null)
            {
                Transform t = box.transform;
                Vector3 local = t.InverseTransformPoint(p);
                Vector3 lossy = t.lossyScale;
                float rx = radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
                float ry = radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
                float rz = radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.z));
                Vector3 c = box.center;
                Vector3 e = box.size * 0.5f + new Vector3(rx, ry, rz);

                float dx0 = local.x - (c.x - e.x);
                float dx1 = (c.x + e.x) - local.x;
                float dy0 = local.y - (c.y - e.y);
                float dy1 = (c.y + e.y) - local.y;
                float dz0 = local.z - (c.z - e.z);
                float dz1 = (c.z + e.z) - local.z;

                float bestBox = dx0;
                Vector3 outLocal = new Vector3(c.x - e.x, local.y, local.z);
                if (dx1 < bestBox) { bestBox = dx1; outLocal = new Vector3(c.x + e.x, local.y, local.z); }
                if (dz0 < bestBox) { bestBox = dz0; outLocal = new Vector3(local.x, local.y, c.z - e.z); }
                if (dz1 < bestBox) { bestBox = dz1; outLocal = new Vector3(local.x, local.y, c.z + e.z); }
                if (dy0 < bestBox) { bestBox = dy0; outLocal = new Vector3(local.x, c.y - e.y, local.z); }
                if (dy1 < bestBox) { outLocal = new Vector3(local.x, c.y + e.y, local.z); }

                return t.TransformPoint(outLocal);
            }

            Bounds b = col.bounds;
            float minX = b.min.x - radius;
            float maxX = b.max.x + radius;
            float minY = b.min.y - radius;
            float maxY = b.max.y + radius;
            float minZ = b.min.z - radius;
            float maxZ = b.max.z + radius;

            float bx0 = p.x - minX;
            float bx1 = maxX - p.x;
            float by0 = p.y - minY;
            float by1 = maxY - p.y;
            float bz0 = p.z - minZ;
            float bz1 = maxZ - p.z;

            float best = bx0;
            Vector3 o = new Vector3(minX, p.y, p.z);
            if (bx1 < best) { best = bx1; o = new Vector3(maxX, p.y, p.z); }
            if (bz0 < best) { best = bz0; o = new Vector3(p.x, p.y, minZ); }
            if (bz1 < best) { best = bz1; o = new Vector3(p.x, p.y, maxZ); }
            if (by0 < best) { best = by0; o = new Vector3(p.x, minY, p.z); }
            if (by1 < best) { o = new Vector3(p.x, maxY, p.z); }

            return o;
        }

        static bool IsGroundLike(Collider col, Vector3 point)
        {
            if (col == null)
                return true;
            if (col is TerrainCollider)
                return true;

            Bounds b = col.bounds;
            // Courtyard floors and combined castle meshes are huge in XZ. Treating
            // their AABB as a post flings the rope around the spawn.
            if (b.size.x > 8f && b.size.z > 8f)
                return true;

            return IsFlatGroundCollider(col, point);
        }

        static bool IsFlatGroundCollider(Collider col, Vector3 point)
        {
            // Large horizontal colliders (terrain/floor) are treated as ground.
            Bounds b = col.bounds;
            bool wide = b.size.x > 4f && b.size.z > 4f;
            bool thin = b.size.y < 1.25f || point.y >= b.max.y - 0.5f;
            return wide && thin;
        }

        void ClampAboveGround(int index)
        {
            Vector3 p = _pos[index];
            Vector3 origin = p + Vector3.up * 2f;
            float probe = groundProbe + 2f;

            int hits = Physics.RaycastNonAlloc(
                origin, Vector3.down, _groundHits, probe, collisionMask, QueryTriggerInteraction.Ignore);

            float bestY = float.NegativeInfinity;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (SkipChainCollider(hit.collider))
                    continue;
                if (IsVerticalPostObstacle(hit.collider))
                    continue;
                if (hit.normal.y < 0.35f)
                    continue; // walls / steep sides — not floor support

                bestY = Mathf.Max(bestY, hit.point.y);
            }

            if (bestY > float.NegativeInfinity)
            {
                float minY = bestY + Mathf.Max(groundClearance, SolidRadius() * 0.85f);
                if (p.y < minY)
                    p.y = minY;
            }

            if (!_skipHeightBand && physicsMode != ChainPhysicsMode.FixedLength)
            {
                float t = index / (float)(_pos.Length - 1);
                float bandY = Mathf.Lerp(_pos[0].y, _pos[^1].y, t) - 0.35f;
                if (p.y < bandY)
                    p.y = bandY;
            }

            _pos[index] = p;
        }

        float StraightSep()
        {
            if (horseA == null || horseB == null)
                return 0f;
            Vector3 delta = horseA.transform.position - horseB.transform.position;
            return delta.magnitude;
        }

        void MaybePlayChainRattle(float dt)
        {
            if (_pos == null || _pos.Length < 2 || Time.time < _nextChainRattle)
                return;

            float invDt = 1f / Mathf.Max(dt, 0.0001f);
            float best = 0f;
            for (int i = 0; i < _pos.Length; i++)
            {
                float speed = (_pos[i] - _prev[i]).magnitude * invDt;
                if (speed > best)
                    best = speed;
            }

            if (best < 5.5f)
                return;

            Vector3 mid = 0.5f * (_pos[0] + _pos[_pos.Length - 1]);
            Cali.Audio.CaliGameplayAudio.PlayAt(Cali.Audio.CaliSoundIds.ChainRandom, mid);
            _nextChainRattle = Time.time + Random.Range(0.45f, 1.15f);
        }

        void DetectEnemyKills(float dt)
        {
            if (enemyHitMask.value == 0 || _pos == null || _pos.Length < 2)
                return;

            float invDt = 1f / Mathf.Max(dt, 0.0001f);
            float radius = Mathf.Max(0.05f, killQueryRadius);
            float horseSpeed = _horseSpeed;

            for (int i = 0; i < _pos.Length - 1; i++)
            {
                Vector3 a = _pos[i];
                Vector3 b = _pos[i + 1];

                float speedA = (_pos[i] - _prev[i]).magnitude * invDt;
                float speedB = (_pos[i + 1] - _prev[i + 1]).magnitude * invDt;
                float speed = Mathf.Max(0.5f * (speedA + speedB), horseSpeed);
                if (speed < killSpeedThreshold)
                    continue;

                Vector3 seg = b - a;
                float segLen = seg.magnitude;
                Vector3 dir = segLen > 0.0001f ? seg / segLen : Vector3.forward;
                Vector3 hitPoint = 0.5f * (a + b);

                int count = Physics.OverlapCapsuleNonAlloc(
                    a, b, radius, _enemyOverlap, enemyHitMask, QueryTriggerInteraction.Ignore);

                for (int c = 0; c < count; c++)
                {
                    var col = _enemyOverlap[c];
                    if (col == null || ChainIgnore.IsIgnored(col))
                        continue;

                    var enemy = col.GetComponentInParent<ChainKillableEnemy>();
                    if (enemy == null || enemy.IsDead)
                        continue;

                    Vector3 closest = col.ClosestPoint(hitPoint);
                    if (enemy.TryKill(closest, dir * speed))
                        Cali.Audio.CaliGameplayAudio.PlayAt(Cali.Audio.CaliSoundIds.ChainHit, closest);
                }
            }
        }

        void PushChainObjects(float dt)
        {
            if (_pos == null || _pos.Length < 2)
                return;

            float invDt = 1f / Mathf.Max(dt, 0.0001f);
            float radius = Mathf.Max(0.08f, PushQueryRadius);
            float minSpeed = Mathf.Max(0.35f, PushSpeedThreshold);

            for (int i = 0; i < _pos.Length - 1; i++)
            {
                Vector3 a = _pos[i];
                Vector3 b = _pos[i + 1];
                Vector3 mid = 0.5f * (a + b);
                Vector3 prevMid = 0.5f * (_prev[i] + _prev[i + 1]);
                Vector3 vel = (mid - prevMid) * invDt;
                vel.y = 0f;

                if (vel.sqrMagnitude < minSpeed * minSpeed && _horseSpeed >= minSpeed)
                    vel = _horseVel;

                if (vel.sqrMagnitude < minSpeed * minSpeed)
                    continue;

                int count = Physics.OverlapCapsuleNonAlloc(
                    a, b, radius, _enemyOverlap, collisionMask, QueryTriggerInteraction.Ignore);

                for (int c = 0; c < count; c++)
                {
                    var col = _enemyOverlap[c];
                    if (col == null || SkipChainCollider(col))
                        continue;

                    var box = col.GetComponentInParent<ChainPushable>();
                    if (box == null)
                        continue;

                    Vector3 closest = col.ClosestPoint(mid);
                    box.ConsiderChainSweep(vel, closest);
                }
            }
        }

        void SweepRagdolls(float dt)
        {
            if (_pos == null || _pos.Length < 2)
                return;

            int mask = enemyHitMask.value != 0 ? enemyHitMask : collisionMask;
            if (mask == 0)
                return;

            float invDt = 1f / Mathf.Max(dt, 0.0001f);
            float radius = Mathf.Max(0.22f, killQueryRadius);

            for (int i = 0; i < _pos.Length - 1; i++)
            {
                Vector3 a = _pos[i];
                Vector3 b = _pos[i + 1];
                Vector3 mid = 0.5f * (a + b);
                Vector3 prevMid = 0.5f * (_prev[i] + _prev[i + 1]);
                Vector3 vel = (mid - prevMid) * invDt;
                if (vel.sqrMagnitude < 1.2f * 1.2f)
                    continue;

                int count = Physics.OverlapCapsuleNonAlloc(
                    a, b, radius, _enemyOverlap, mask, QueryTriggerInteraction.Ignore);

                for (int c = 0; c < count; c++)
                {
                    var col = _enemyOverlap[c];
                    if (col == null || SkipChainCollider(col))
                        continue;

                    var ragdoll = col.GetComponentInParent<ChainRagdoll>();
                    if (ragdoll == null)
                        continue;

                    Vector3 closest = col.ClosestPoint(mid);
                    ragdoll.ApplyChainSweep(vel, closest);
                }
            }
        }

        void SetupLine()
        {
            _line.useWorldSpace = true;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth;
            _line.numCapVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            if (_line.sharedMaterial == null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = slackColor;
                _line.sharedMaterial = mat;
            }
        }

        void UpdateVisual()
        {
            if (_pos == null || _pos.Length < 2)
                return;

            if (TryDrawIronMesh())
            {
                HideStretchedLinks();
                if (_line != null)
                    _line.enabled = false;
                return;
            }

            HideStretchedLinks();
            if (_line != null)
            {
                _line.enabled = true;
                for (int i = 0; i < _pos.Length; i++)
                    _line.SetPosition(i, _pos[i]);

                float t = maxLength > 0f ? Mathf.Clamp01(CurrentDistance / maxLength) : 0f;
                Color c = Color.Lerp(slackColor, tautColor, t);
                _line.startColor = c;
                _line.endColor = c;
                if (_line.sharedMaterial != null)
                    _line.sharedMaterial.color = c;
            }
        }

        bool TryDrawIronMesh()
        {
            ResolveIronAssets();

            if (!_iron.Ready)
            {
                if (_ironTried)
                    return false;
                _ironTried = true;

                bool setup = false;
                if (_ironBindMesh != null)
                    setup = _iron.Setup(transform, _ironBindMesh, ironMaterial);
                if (!setup && linkPrefab != null)
                    setup = _iron.Setup(transform, linkPrefab, ironMaterial);
                if (!setup)
                    return false;
            }

            _iron.SetThickness(visualThickness);
            _iron.FitRestLength(Mathf.Max(0.5f, maxLength));
            Vector3[] path = _visPos != null && _visPos.Length > 2 ? _visPos : _pos;
            _iron.Deform(path);
            return _iron.IsVisible;
        }

        Mesh _ironBindMesh;

        const string MainchainPath = "Assets/Mainchain.fbx";
        const string IronMatPath = "Assets/iron-chain/IronChain.mat";

        void ResolveIronAssets()
        {
            if (ironMaterial == null)
                ironMaterial = Resources.Load<Material>("CaliIronChain");
#if UNITY_EDITOR
            if (ironMaterial == null)
                ironMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(IronMatPath);
            if (linkPrefab == null)
                linkPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MainchainPath);
            if (_ironBindMesh == null)
                _ironBindMesh = LoadLargestMesh(MainchainPath);
            if (_ironBindMesh == null && linkPrefab != null)
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(linkPrefab);
                if (!string.IsNullOrEmpty(path))
                    _ironBindMesh = LoadLargestMesh(path);
            }
#endif
            if (_ironBindMesh == null)
                _ironBindMesh = Resources.Load<Mesh>("CaliIronChainBind");
            if (_ironBindMesh == null && linkPrefab != null)
            {
                var filter = linkPrefab.GetComponentInChildren<MeshFilter>(true);
                if (filter != null)
                    _ironBindMesh = filter.sharedMesh;
            }
        }

#if UNITY_EDITOR
        static Mesh LoadLargestMesh(string assetPath)
        {
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Mesh bestMesh = null;
            int best = 0;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Mesh mesh && mesh.vertexCount > best)
                {
                    bestMesh = mesh;
                    best = mesh.vertexCount;
                }
            }

            return bestMesh;
        }
#endif

        void HideStretchedLinks()
        {
            if (_links == null)
                return;
            for (int i = 0; i < _links.Length; i++)
            {
                if (_links[i] != null)
                    _links[i].gameObject.SetActive(false);
            }
        }

        bool HasUsableLinks()
        {
            if (_links == null || _links.Length == 0)
                return false;
            for (int i = 0; i < _links.Length; i++)
            {
                if (_links[i] != null)
                    return true;
            }
            return false;
        }

        void EnsureLinks(int count)
        {
            if (linkPrefab == null)
                return;

            PrepareLinkMeshes();

            if (_linkRoot == null)
            {
                var go = new GameObject("ChainLinks");
                go.transform.SetParent(transform, false);
                _linkRoot = go.transform;
            }

            if (_links != null && _links.Length == count)
                return;

            if (_links != null)
            {
                for (int i = 0; i < _links.Length; i++)
                {
                    if (_links[i] != null)
                        Destroy(_links[i].gameObject);
                }
            }

            _links = new Transform[count];
            bool sliced = _sliceMeshes != null && _sliceMeshes.Length > 0;
            for (int i = 0; i < count; i++)
            {
                GameObject inst;
                if (sliced)
                {
                    inst = new GameObject("ChainLink_" + i);
                    inst.transform.SetParent(_linkRoot, false);
                    var mf = inst.AddComponent<MeshFilter>();
                    mf.sharedMesh = _sliceMeshes[i % _sliceMeshes.Length];
                    var mr = inst.AddComponent<MeshRenderer>();
                    if (_linkMaterials != null && _linkMaterials.Length > 0)
                        mr.sharedMaterials = _linkMaterials;
                }
                else
                {
                    inst = Instantiate(linkPrefab, _linkRoot);
                    inst.name = "ChainLink_" + i;
                    inst.SetActive(true);
                    foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                        col.enabled = false;
                }

                _links[i] = inst.transform;
            }
        }

        void PrepareLinkMeshes()
        {
            if (_sliceMeshes != null || linkPrefab == null)
                return;

            try
            {
                var source = CombinePrefabMesh(linkPrefab, out _linkMaterials);
                if (_linkMaterials == null || _linkMaterials.Length == 0 || _linkMaterials[0] == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit")
                                 ?? Shader.Find("Standard")
                                 ?? Shader.Find("Sprites/Default");
                    _linkMaterials = new[] { new Material(shader) { color = new Color(0.45f, 0.42f, 0.38f) } };
                }

                if (source == null || source.vertexCount < 3)
                {
                    _sliceMeshes = System.Array.Empty<Mesh>();
                    return;
                }

                Bounds b = source.bounds;
                Vector3 size = b.size;
                int axis = 0;
                if (size.y >= size.x && size.y >= size.z)
                    axis = 1;
                else if (size.z >= size.x && size.z >= size.y)
                    axis = 2;

                float nativeLen = Mathf.Max(0.01f, size[axis]);
                int pieces = meshSplitCount;
                if (pieces <= 0)
                {
                    if (nativeLen > linkMeshLength * 1.4f)
                        pieces = Mathf.Clamp(Mathf.RoundToInt(nativeLen / Mathf.Max(0.08f, linkMeshLength)), 2, 24);
                    else
                        pieces = 1;
                }

                var slices = SplitMeshAlongAxis(source, axis, pieces);
                var packed = new List<Mesh>(slices.Length);
                for (int i = 0; i < slices.Length; i++)
                {
                    if (slices[i] != null && slices[i].vertexCount >= 3 && slices[i].triangles.Length >= 3)
                        packed.Add(slices[i]);
                }

                if (packed.Count == 0)
                {
                    _sliceMeshes = System.Array.Empty<Mesh>();
                    _sliceLength = nativeLen;
                    return;
                }

                _sliceMeshes = packed.ToArray();
                _sliceLength = packed[0].bounds.size.z;
                if (_sliceLength < 0.01f)
                    _sliceLength = nativeLen / packed.Count;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SoftHorseChain] Could not slice chain mesh, using prefab copies. " + e.Message, this);
                _sliceMeshes = System.Array.Empty<Mesh>();
            }
        }

        static Mesh CombinePrefabMesh(GameObject prefab, out Material[] materials)
        {
            materials = null;
            var inst = Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            inst.SetActive(true);

            var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
                materials = renderers[0].sharedMaterials;

            var filters = inst.GetComponentsInChildren<MeshFilter>(true);
            var packed = new List<CombineInstance>(filters.Length);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null || filters[i].sharedMesh == null)
                    continue;

                packed.Add(new CombineInstance
                {
                    mesh = filters[i].sharedMesh,
                    transform = inst.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix
                });
            }

            Mesh mesh = null;
            if (packed.Count > 0)
            {
                mesh = new Mesh { name = prefab.name + "_Combined" };
                mesh.CombineMeshes(packed.ToArray(), true, true);
            }

            Destroy(inst);
            return mesh;
        }

        static Mesh[] SplitMeshAlongAxis(Mesh source, int axis, int pieces)
        {
            pieces = Mathf.Max(1, pieces);
            var verts = source.vertices;
            var tris = source.triangles;
            if (verts.Length == 0 || tris.Length < 3)
                return System.Array.Empty<Mesh>();

            var norms = source.normals;
            var uvs = source.uv;
            bool hasUv = uvs != null && uvs.Length == verts.Length;
            bool hasN = norms != null && norms.Length == verts.Length;

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float a = verts[i][axis];
                if (a < min) min = a;
                if (a > max) max = a;
            }

            float span = Mathf.Max(0.0001f, max - min);
            var buckets = new List<int>[pieces];
            for (int i = 0; i < pieces; i++)
                buckets[i] = new List<int>(64);

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                float u0 = (verts[i0][axis] - min) / span;
                float u1 = (verts[i1][axis] - min) / span;
                float u2 = (verts[i2][axis] - min) / span;
                int p0 = Mathf.Clamp(Mathf.FloorToInt(u0 * pieces), 0, pieces - 1);
                int p1 = Mathf.Clamp(Mathf.FloorToInt(u1 * pieces), 0, pieces - 1);
                int p2 = Mathf.Clamp(Mathf.FloorToInt(u2 * pieces), 0, pieces - 1);
                int lo = Mathf.Min(p0, Mathf.Min(p1, p2));
                int hi = Mathf.Max(p0, Mathf.Max(p1, p2));
                for (int p = lo; p <= hi; p++)
                {
                    buckets[p].Add(i0);
                    buckets[p].Add(i1);
                    buckets[p].Add(i2);
                }
            }

            var result = new Mesh[pieces];
            for (int p = 0; p < pieces; p++)
            {
                if (buckets[p].Count < 3)
                    continue;

                var map = new Dictionary<int, int>(buckets[p].Count);
                var nv = new List<Vector3>(buckets[p].Count);
                var nn = hasN ? new List<Vector3>(buckets[p].Count) : null;
                var nu = hasUv ? new List<Vector2>(buckets[p].Count) : null;
                var nt = new List<int>(buckets[p].Count);

                for (int i = 0; i < buckets[p].Count; i++)
                {
                    int old = buckets[p][i];
                    if (!map.TryGetValue(old, out int neu))
                    {
                        neu = nv.Count;
                        map[old] = neu;
                        nv.Add(AlignToForwardZ(verts[old], axis));
                        if (hasN)
                            nn.Add(AlignToForwardZ(norms[old], axis));
                        if (hasUv)
                            nu.Add(uvs[old]);
                    }
                    nt.Add(neu);
                }

                Vector3 center = Vector3.zero;
                for (int i = 0; i < nv.Count; i++)
                    center += nv[i];
                if (nv.Count > 0)
                    center /= nv.Count;
                for (int i = 0; i < nv.Count; i++)
                    nv[i] -= center;

                var mesh = new Mesh { name = source.name + "_Slice" + p };
                mesh.SetVertices(nv);
                mesh.SetTriangles(nt, 0);
                if (hasN)
                    mesh.SetNormals(nn);
                else
                    mesh.RecalculateNormals();
                if (hasUv)
                    mesh.SetUVs(0, nu);
                mesh.RecalculateBounds();
                result[p] = mesh;
            }

            return result;
        }

        static Vector3 AlignToForwardZ(Vector3 v, int axis)
        {
            if (axis == 1)
                return new Vector3(v.x, v.z, v.y);
            if (axis == 0)
                return new Vector3(-v.z, v.y, v.x);
            return v;
        }

        void TryAssignDefaultLinkPrefab()
        {
#if UNITY_EDITOR
            var iron = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MainchainPath);
            if (iron != null)
                linkPrefab = iron;
            if (ironMaterial == null)
                ironMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(IronMatPath);
#endif
            if (linkPrefab == null)
                linkPrefab = Resources.Load<GameObject>("CaliIronChain");
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var aAnimal = horseA != null ? horseA : FindAnimalByName("Horse Realistic");
            var bAnimal = horseB != null ? horseB : FindAnimalByName("Horse Unicorn");
            if (aAnimal != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(AttachPoint(aAnimal), 0.12f);
            }
            if (bAnimal != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(AttachPoint(bAnimal), 0.12f);
            }

            if (_pos == null || _pos.Length < 2)
                return;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < _pos.Length - 1; i++)
                Gizmos.DrawLine(_pos[i], _pos[i + 1]);
        }
#endif
    }
}
