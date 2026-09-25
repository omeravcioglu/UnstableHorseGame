using UnityEngine;
using UnityEngine.AI;

namespace Cali.Gameplay
{
    /// <summary>
    /// Captive LPHorse (no MAnimal). Idles in the stall, then NavMesh-gallops to an outside point.
    /// </summary>
    [DisallowMultipleComponent]
    public class CaliNpcHorse : MonoBehaviour
    {
        const string ControllerResource = "Gameplay/CaliNpcHorse";
        static readonly int RunningId = Animator.StringToHash("Running");

        [Tooltip("Used if Release() is called without a target.")]
        public Transform defaultOutside;

        public float runSpeed = 9.5f;
        public float arriveDistance = 1.1f;
        public float sampleRadius = 4f;

        Animator _anim;
        NavMeshAgent _agent;
        Transform _outside;
        Vector3 _offset;
        bool _released;
        bool _arrived;
        float _delay;

        void Awake()
        {
            BindAnimator();
            PrepareAgent();
            SetRunning(false);
        }

        void BindAnimator()
        {
            var anims = GetComponentsInChildren<Animator>(true);
            Avatar avatar = null;
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null && anims[i].avatar != null)
                {
                    avatar = anims[i].avatar;
                    break;
                }
            }

            _anim = GetComponentInChildren<Animator>(true);
            if (_anim == null)
                _anim = gameObject.AddComponent<Animator>();

            if (_anim.avatar == null && avatar != null)
                _anim.avatar = avatar;

            _anim.applyRootMotion = false;
            _anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var legacy = GetComponentInChildren<Animation>(true);
            if (legacy != null)
                legacy.enabled = false;

            var controller = Resources.Load<RuntimeAnimatorController>(ControllerResource);
            if (controller != null)
                _anim.runtimeAnimatorController = controller;
            else
                Debug.LogWarning($"[CaliNpcHorse] Missing animator at Resources/{ControllerResource}.controller", this);
        }

        void PrepareAgent()
        {
            var rbs = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] != null)
                    Destroy(rbs[i]);
            }

            var cc = GetComponent<CharacterController>();
            if (cc != null)
                Destroy(cc);

            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }

            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
                _agent = gameObject.AddComponent<NavMeshAgent>();

            _agent.speed = runSpeed;
            _agent.acceleration = 14f;
            _agent.angularSpeed = 220f;
            _agent.stoppingDistance = arriveDistance;
            _agent.radius = 0.35f;
            _agent.height = 2f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            _agent.autoBraking = true;
            _agent.updatePosition = true;
            _agent.updateRotation = true;
            _agent.enabled = false;
        }

        public void Release(Transform doorway, Transform outside, Vector3 sideOffset, float stagger = 0f)
        {
            _outside = outside != null ? outside : defaultOutside;
            _offset = sideOffset;
            _delay = Mathf.Max(0f, stagger);
            _released = _outside != null;
            _arrived = false;
            SetRunning(false);

            if (!_released)
                return;

            if (_delay <= 0f)
                BeginPath();
        }

        public static void IgnoreEachOther(CaliNpcHorse[] horses)
        {
            if (horses == null)
                return;

            int pri = 30;
            for (int i = 0; i < horses.Length; i++)
            {
                var horse = horses[i];
                if (horse == null || horse._agent == null)
                    continue;
                horse._agent.avoidancePriority = pri++;
            }
        }

        void Update()
        {
            if (!_released || _arrived)
                return;

            if (_delay > 0f)
            {
                _delay -= Time.deltaTime;
                if (_delay > 0f)
                    return;
                BeginPath();
            }

            if (_agent == null || !_agent.enabled)
                return;

            bool moving = _agent.velocity.sqrMagnitude > 0.12f;
            bool done = !_agent.pathPending &&
                        _agent.hasPath &&
                        _agent.remainingDistance <= _agent.stoppingDistance + 0.15f;

            if (done)
            {
                _arrived = true;
                _agent.isStopped = true;
                _agent.ResetPath();
                SetRunning(false);
                return;
            }

            SetRunning(moving);
        }

        void BeginPath()
        {
            Vector3 dest = _outside != null ? _outside.position + _offset : transform.position;
            if (!TryOnNavMesh(transform.position, out Vector3 from))
            {
                Debug.LogWarning($"[CaliNpcHorse] '{name}' is not on a NavMesh. Bake stall floors and the doorway.", this);
                _arrived = true;
                SetRunning(false);
                return;
            }

            if (!TryOnNavMesh(dest, out Vector3 to))
            {
                Debug.LogWarning($"[CaliNpcHorse] '{name}' outside target is not on a NavMesh.", this);
                _arrived = true;
                SetRunning(false);
                return;
            }

            _agent.enabled = true;
            _agent.speed = runSpeed;
            _agent.stoppingDistance = arriveDistance;
            _agent.Warp(from);
            if (!_agent.SetDestination(to))
            {
                Debug.LogWarning($"[CaliNpcHorse] '{name}' could not path to the outside target.", this);
                _arrived = true;
                SetRunning(false);
                return;
            }

            SetRunning(true);
        }

        bool TryOnNavMesh(Vector3 pos, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, Mathf.Max(0.5f, sampleRadius), NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }

            snapped = pos;
            return false;
        }

        void SetRunning(bool running)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null)
                return;
            _anim.SetBool(RunningId, running);
        }
    }
}
