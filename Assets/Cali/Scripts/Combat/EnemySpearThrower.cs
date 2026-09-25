using System.Collections;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Humanoid enemy: faces nearest horse, plays spear attack anim, then throws a projectile.
    /// </summary>
    public class EnemySpearThrower : MonoBehaviour
    {
        [Header("Throwing")]
        public float throwInterval = 3.2f;
        public float firstThrowDelay = 1.2f;
        public float range = 28f;
        public float spearSpeed = 22f;
        public float spawnHeight = 1.35f;
        public float spawnForward = 0.55f;

        [Tooltip("Delay after starting the attack anim before the spear spawns.")]
        public float throwReleaseDelay = 0.45f;

        [Tooltip("Optional. Uses Malbers Spear mesh if empty.")]
        public GameObject spearPrefab;

        [Header("Aim")]
        public float turnSpeed = 6f;
        public float aimHeight = 1.1f;

        [Header("Animation")]
        [HideInInspector] public int spearModeId = 101;
        [HideInInspector] public int spearAbilityIndex = 3;

        [Tooltip("How long chase stays locked after a throw starts.")]
        public float busyDuration = 1.1f;

        /// <summary>True while wind-up / throw anim should block walking.</summary>
        public bool IsBusy => Time.time < _busyUntil;

        ChainKillableEnemy _enemy;
        Animator _animator;
        float _nextThrowTime;
        float _busyUntil;
        bool _throwing;
        static GameObject _cachedSpearVisual;
        static readonly int AnimAttack = Animator.StringToHash("Attack");

        void Awake()
        {
            _enemy = GetComponent<ChainKillableEnemy>();
            _animator = GetComponentInChildren<Animator>();
            _nextThrowTime = Time.time + firstThrowDelay;

            if (spearPrefab == null)
                spearPrefab = Resources.Load<GameObject>("Combat/SpearProjectile");
        }

        void Update()
        {
            if (!ChainKillableEnemy.ShouldSimulateAi)
                return;

            if (_enemy != null && _enemy.IsDead)
            {
                enabled = false;
                return;
            }

            if (_throwing)
                return;

            float detect = _enemy != null ? _enemy.DetectRadius : range;
            var target = EnemyCombatTargets.FindNearestHorseInRange(transform.position, detect);
            if (target == null)
                return;

            Vector3 to = target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f)
            {
                var look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
            }

            if (Time.time < _nextThrowTime)
                return;
            if (to.magnitude > range)
                return;

            StartCoroutine(ThrowRoutine(target));
            _nextThrowTime = Time.time + throwInterval;
        }

        IEnumerator ThrowRoutine(Transform target)
        {
            _throwing = true;
            _busyUntil = Time.time + busyDuration;

            PlayThrowAnimation();

            float delay = Mathf.Max(0f, throwReleaseDelay);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (_enemy != null && _enemy.IsDead)
            {
                _throwing = false;
                yield break;
            }

            if (target != null)
                SpawnSpear(target, visualOnly: false);

            _throwing = false;
        }

        public void PlayRemoteThrow(Vector3 origin, Vector3 dir)
        {
            PlayThrowAnimation();
            SpawnSpearAt(origin, dir, visualOnly: true);
        }

        void PlayThrowAnimation()
        {
            if (_animator == null)
                return;

            if (HasParam(_animator, AnimAttack))
                _animator.SetTrigger(AnimAttack);
            else
                _animator.SetTrigger("Attack");
        }

        void SpawnSpear(Transform target, bool visualOnly)
        {
            Vector3 origin = transform.position + Vector3.up * spawnHeight + transform.forward * spawnForward;
            Vector3 aim = target.position + Vector3.up * aimHeight;
            Vector3 dir = (aim - origin).normalized;
            SpawnSpearAt(origin, dir, visualOnly);
        }

        void SpawnSpearAt(Vector3 origin, Vector3 dir, bool visualOnly)
        {
            var spearGo = CreateSpearInstance(origin, dir);
            var proj = spearGo.GetComponent<SpearProjectile>();
            if (proj == null)
                proj = spearGo.AddComponent<SpearProjectile>();

            proj.visualOnly = visualOnly;
            proj.Launch(dir, spearSpeed, thrower: transform);

            if (!visualOnly && _enemy != null && Cali.Network.CoopSessionStarter.IsOnline)
            {
                var coop = Cali.Network.CoopGameController.Instance;
                if (coop != null)
                    coop.ServerBroadcastEnemyThrow(_enemy.enemyId, origin, dir);
            }
        }

        static bool HasParam(Animator anim, int hash)
        {
            foreach (var p in anim.parameters)
            {
                if (p.nameHash == hash)
                    return true;
            }

            return false;
        }

        GameObject CreateSpearInstance(Vector3 origin, Vector3 dir)
        {
            GameObject go;
            if (spearPrefab != null)
            {
                go = Instantiate(spearPrefab, origin, Quaternion.LookRotation(dir));
            }
            else
            {
                go = CreateFallbackSpear(origin, dir);
            }

            if (go.GetComponent<Collider>() == null)
            {
                var tip = go.AddComponent<SphereCollider>();
                tip.isTrigger = true;
                tip.radius = 0.07f;
                tip.center = new Vector3(0f, 0f, 0.55f);
            }
            else
            {
                foreach (var c in go.GetComponentsInChildren<Collider>())
                    c.isTrigger = true;
            }

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            return go;
        }

        static GameObject CreateFallbackSpear(Vector3 origin, Vector3 dir)
        {
            if (_cachedSpearVisual == null)
            {
#if UNITY_EDITOR
                _cachedSpearVisual = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Malbers Animations/Common/Prefabs/Weapons/Spear.prefab");
#endif
                if (_cachedSpearVisual == null)
                    _cachedSpearVisual = Resources.Load<GameObject>("Combat/SpearProjectile");
            }

            if (_cachedSpearVisual != null)
            {
                var inst = Instantiate(_cachedSpearVisual, origin, Quaternion.LookRotation(dir));
                inst.name = "SpearProjectile";
                foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null)
                        continue;
                    string n = mb.GetType().Name;
                    if (n.StartsWith("M") || n.Contains("Weapon") || n.Contains("Holster") || n.Contains("Damage"))
                        Destroy(mb);
                }

                return inst;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "SpearProjectile";
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = new Vector3(0.07f, 0.07f, 1.2f);
            Object.Destroy(go.GetComponent<Collider>());

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                var wood = new Color(0.45f, 0.28f, 0.12f);
                mat.color = wood;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", wood);
                rend.sharedMaterial = mat;
            }

            return go;
        }
    }
}
