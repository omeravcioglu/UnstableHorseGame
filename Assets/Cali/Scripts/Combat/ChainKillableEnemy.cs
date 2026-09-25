using System.Collections.Generic;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Dies when hit by a fast SoftHorseChain segment. Spawns blood + XP orbs and collapses.
    /// </summary>
    public class ChainKillableEnemy : MonoBehaviour
    {
        static readonly Dictionary<int, ChainKillableEnemy> ById = new();
        static ChainKillableEnemy[] _stableList = System.Array.Empty<ChainKillableEnemy>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BindSceneIds()
        {
            AssignStableIds();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HorsesPassThroughEnemies()
        {
            int enemy = LayerMask.NameToLayer("Enemy");
            if (enemy < 0)
                return;

            int animal = LayerMask.NameToLayer("Animal");
            if (animal >= 0)
                Physics.IgnoreLayerCollision(animal, enemy, true);

            int body = LayerMask.NameToLayer("BodyPart");
            if (body >= 0)
                Physics.IgnoreLayerCollision(body, enemy, true);

            // Pushable crates live on Item so dead ragdolls cannot pin them in place.
            int item = LayerMask.NameToLayer("Item");
            if (item >= 0)
                Physics.IgnoreLayerCollision(enemy, item, true);
        }

        [Header("Identity")]
        public EnemyKind kind = EnemyKind.Wolf;

        [Tooltip("Stable id for networked death. Auto-assigned if 0.")]
        public int enemyId;

        [Header("Detect")]
        [Tooltip("Horses outside this XZ radius are ignored. 0 = wolf 14 / humanoid 22.")]
        public float detectRadius;

        [Header("Rewards")]
        public int xpReward = 20;
        public int orbCount = 4;
        public GameObject xpOrbPrefab;

        [Header("Death FX")]
        [Tooltip("If true, picks a random RVFX splash + decal from BloodFxLibrary on each kill.")]
        public bool useRandomPackFx = true;
        [Tooltip("Optional fixed splash when useRandomPackFx is off.")]
        public GameObject bloodPrefab;
        [Tooltip("Optional fixed ground decal when useRandomPackFx is off.")]
        public GameObject bloodDecalPrefab;
        public bool spawnBloodDecal = true;
        public float collapseDuration = 0.45f;
        [HideInInspector] public string deathAnimatorTrigger = "Death";
        [Tooltip("If true, death uses a physics ragdoll instead of a death clip.")]
        public bool useRagdollDeath = true;

        public bool IsDead { get; private set; }

        public static event System.Action<ChainKillableEnemy> Died;

        public float DetectRadius =>
            detectRadius > 0.01f ? detectRadius : DefaultDetectRadius(kind);

        public static float DefaultDetectRadius(EnemyKind enemyKind) =>
            enemyKind == EnemyKind.Humanoid ? 22f : 14f;

        Collider[] _colliders;
        Rigidbody _rb;
        MAnimal _animal;
        Animator _animator;
        EnemySpearThrower _thrower;
        EnemyTalkVoice _talk;
        SkinnedMeshRenderer[] _skins;
        EnemyLod _lod;
        bool _lodInit;
        Quaternion _startRot;
        Quaternion _endRot;
        float _collapseT = -1f;
        bool _fxPlayed;

        internal float CrowdDist;
        internal EnemyLod Lod => _lod;

        public static ChainKillableEnemy FindById(int id)
        {
            return ById.TryGetValue(id, out var e) ? e : null;
        }

        public static void ApplyRemoteDeath(int id, Vector3 hitPoint, Vector3 hitDir)
        {
            var enemy = FindById(id);
            if (enemy == null || enemy.IsDead)
                return;

            // Visuals only — host already owns XP via orbs / SharedXp.
            enemy.KillInternal(hitPoint, hitDir, replicate: false, spawnOrbs: true, grantXp: false);
        }

        /// <summary>
        /// Host simulates chase/throws. Clients follow networked poses so both see the same enemies.
        /// Offline always simulates locally.
        /// </summary>
        public static bool ShouldSimulateAi
        {
            get
            {
                var coop = Cali.Network.CoopGameController.Instance;
                if (coop == null || coop.Object == null || !coop.Object.IsValid || coop.Runner == null)
                    return true;
                return coop.HasStateAuthority;
            }
        }

        public static ChainKillableEnemy[] StableList => _stableList;

        public static void AssignStableIds()
        {
            var all = FindObjectsByType<ChainKillableEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(all, CompareStable);
            ById.Clear();
            _stableList = all;
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (e == null)
                    continue;
                e.enemyId = i + 1;
                ById[e.enemyId] = e;
            }
        }

        static int CompareStable(ChainKillableEnemy a, ChainKillableEnemy b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int n = string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform));
            if (n != 0)
                return n;
            return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        public void ApplyNetworkPose(Vector3 pos, float yaw)
        {
            if (IsDead)
                return;

            if (_animator != null && !_animator.enabled)
                _animator.enabled = true;

            float moved = (pos - transform.position).sqrMagnitude;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            SetMoveAnim(moved > 0.0004f);
        }

        public void PlayRemoteThrow(Vector3 origin, Vector3 dir)
        {
            var thrower = _thrower != null ? _thrower : GetComponent<EnemySpearThrower>();
            if (thrower != null)
                thrower.PlayRemoteThrow(origin, dir);
        }

        void SetMoveAnim(bool walking)
        {
            if (_animator == null || !_animator.enabled)
                return;

            float v = walking ? 1f : 0f;
            _animator.SetFloat("Speed", v);
            _animator.SetFloat("Vertical", v);
        }

        void Awake()
        {

            _colliders = GetComponentsInChildren<Collider>(true);
            _rb = GetComponent<Rigidbody>();
            _animal = GetComponent<MAnimal>();
            _animator = GetComponentInChildren<Animator>();

            if (GetComponent<EnemyChaseBrain>() == null)
                gameObject.AddComponent<EnemyChaseBrain>();

            var legacyBrain = GetComponent<EnemyBrainStub>();
            if (legacyBrain != null)
                Destroy(legacyBrain);

            if (kind == EnemyKind.Humanoid && GetComponent<EnemySpearThrower>() == null)
                gameObject.AddComponent<EnemySpearThrower>();

            if (kind == EnemyKind.Humanoid && GetComponent<EnemyTalkVoice>() == null)
                gameObject.AddComponent<EnemyTalkVoice>();

            if (detectRadius <= 0.01f)
                detectRadius = DefaultDetectRadius(kind);

            SetLayerRecursive(gameObject, LayerMask.NameToLayer("Enemy"));
            IgnoreHorsesAndPushables();
            StripHeavySystems();
            FixMagentaRenderers();

            _thrower = GetComponent<EnemySpearThrower>();
            _talk = GetComponent<EnemyTalkVoice>();
            _skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            ApplyCrowdLod(EnemyLod.Frozen);
        }

        void IgnoreHorsesAndPushables()
        {
            IgnoreHorsesAndPushables(_colliders);
        }

        internal static void IgnoreHorsesAndPushables(Collider[] cols)
        {
            if (cols == null || cols.Length == 0)
                return;

            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int a = 0; a < animals.Length; a++)
            {
                var animal = animals[a];
                if (animal == null || animal.GetComponentInParent<ChainKillableEnemy>() != null)
                    continue;

                var horseCols = animal.GetComponentsInChildren<Collider>(true);
                IgnorePairs(cols, horseCols);
            }

            var boxes = FindObjectsByType<Cali.Gameplay.ChainPushable>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int b = 0; b < boxes.Length; b++)
            {
                if (boxes[b] == null)
                    continue;
                IgnorePairs(cols, boxes[b].GetComponentsInChildren<Collider>(true));
            }
        }

        static void IgnorePairs(Collider[] a, Collider[] b)
        {
            if (a == null || b == null)
                return;

            for (int i = 0; i < a.Length; i++)
            {
                var ca = a[i];
                if (ca == null)
                    continue;
                for (int j = 0; j < b.Length; j++)
                {
                    var cb = b[j];
                    if (cb == null || cb == ca)
                        continue;
                    Physics.IgnoreCollision(ca, cb, true);
                }
            }
        }

        void OnEnable()
        {
            if (!IsDead)
                EnemyCrowdSim.Register(this);
        }

        void OnDisable()
        {
            EnemyCrowdSim.Unregister(this);
        }

        internal void ApplyCrowdLod(EnemyLod lod)
        {
            if (IsDead)
                lod = EnemyLod.Frozen;
            if (_lodInit && _lod == lod)
                return;

            _lodInit = true;
            _lod = lod;

            bool hot = lod == EnemyLod.Hot;
            bool animOn = !IsDead && lod <= EnemyLod.Warm;
            bool physicsOn = lod <= EnemyLod.Warm;
            bool simpleHuman = kind == EnemyKind.Humanoid;

            if (_animal != null && !simpleHuman)
            {
                if (hot)
                {
                    _animal.enabled = true;
                    _animal.Sleep = false;
                    _animal.UseCameraInput = false;
                }
                else
                {
                    _animal.Sleep = true;
                    _animal.enabled = false;
                }
            }

            if (_animator != null)
            {
                if (animOn)
                {
                    _animator.enabled = true;
                }
                else
                {
                    if (_animator.enabled)
                        _animator.Update(0f);
                    _animator.enabled = false;
                }
            }

            if (_rb != null)
            {
                if (simpleHuman)
                {
                    _rb.isKinematic = true;
                    _rb.linearVelocity = Vector3.zero;
                }
                else
                {
                    _rb.isKinematic = !physicsOn || IsDead;
                    if (!physicsOn)
                        _rb.linearVelocity = Vector3.zero;
                }
            }

            if (_thrower != null)
                _thrower.enabled = hot && !IsDead;
            if (_talk != null)
                _talk.enabled = hot && !IsDead;

            if (_skins != null)
            {
                var quality = lod == EnemyLod.Hot ? SkinQuality.Auto : SkinQuality.Bone2;
                bool motion = lod == EnemyLod.Hot;
                for (int i = 0; i < _skins.Length; i++)
                {
                    var s = _skins[i];
                    if (s == null)
                        continue;
                    s.updateWhenOffscreen = false;
                    s.quality = quality;
                    s.skinnedMotionVectors = motion;
                }
            }
        }

        void StripHeavySystems()
        {
            bool simpleHuman = kind == EnemyKind.Humanoid;

            var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null || b == this)
                    continue;
                if (b is EnemyChaseBrain || b is EnemySpearThrower || b is EnemyTalkVoice)
                    continue;

                string ns = b.GetType().Namespace ?? "";
                string n = b.GetType().Name;
                if (n.Contains("AnimalAI") ||
                    n.Contains("AIControl") ||
                    n.Contains("AIBrain") ||
                    n.Contains("MAnimalAI") ||
                    n == "LookAt" ||
                    n == "LookAtCamera" ||
                    n.Contains("LockOn") ||
                    n.Contains("MaterialChanger") ||
                    n.Contains("BlendShape") ||
                    (simpleHuman && (ns.StartsWith("Malbers") || n == "MAnimal")))
                {
                    b.enabled = false;
                }
            }

            if (simpleHuman)
            {
                if (_animal != null)
                {
                    _animal.enabled = false;
                    _animal = null;
                }

                var cc = GetComponent<CharacterController>();
                if (cc != null)
                    cc.enabled = false;

                if (_animator != null)
                {
                    _animator.applyRootMotion = false;
                    _animator.updateMode = AnimatorUpdateMode.Normal;
                    _animator.cullingMode = AnimatorCullingMode.CullCompletely;
                    var simple = Resources.Load<RuntimeAnimatorController>("Combat/CaliEnemyHumanoid");
                    if (simple != null)
                        _animator.runtimeAnimatorController = simple;
                }

                if (_rb != null)
                {
                    _rb.isKinematic = true;
                    _rb.useGravity = false;
                    _rb.constraints = RigidbodyConstraints.FreezeRotation;
                }
            }

            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var anim = animators[i];
                if (anim == null)
                    continue;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                anim.updateMode = AnimatorUpdateMode.Normal;
                if (simpleHuman && anim != _animator)
                    anim.enabled = false;
            }

            var particles = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                if (ps == null)
                    continue;
                var main = ps.main;
                if (main.loop && main.playOnAwake)
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.gameObject.SetActive(false);
                }
            }

            if (_animal != null && kind != EnemyKind.Humanoid)
                _animal.UseCameraInput = false;

            if (_rb != null)
            {
                _rb.interpolation = RigidbodyInterpolation.None;
                _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            SimplifyColliders();
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        void SimplifyColliders()
        {
            var cols = GetComponentsInChildren<Collider>(true);
            if (cols.Length <= 2)
                return;

            Collider keep = null;
            float best = -1f;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || !c.enabled || c.isTrigger)
                    continue;
                float vol = c.bounds.size.sqrMagnitude;
                if (vol > best)
                {
                    best = vol;
                    keep = c;
                }
            }

            if (keep == null)
                return;

            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || c == keep || c.isTrigger)
                    continue;
                c.enabled = false;
            }
        }

        public static bool HasAnyHorseInDetectRange()
        {
            EnemyCrowdSim.EnsureTicked();
            return EnemyCrowdSim.AnyInCombat;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.15f, 0.9f);
            DrawWireCircle(transform.position + Vector3.up * 0.05f, DetectRadius, 48);
        }

        static void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float step = Mathf.PI * 2f / segments;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = step * i;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        void FixMagentaRenderers()
        {
            // Capsule stand-ins / broken Built-in mats show magenta on URP.
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
                return;

            Color tint = kind == EnemyKind.Wolf
                ? new Color(0.35f, 0.28f, 0.22f)
                : new Color(0.45f, 0.4f, 0.32f);

            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null)
                    continue;

                var mats = rend.sharedMaterials;
                bool needsFix = mats == null || mats.Length == 0;
                if (!needsFix)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || mats[i].shader == null ||
                            mats[i].shader.name == "Hidden/InternalErrorShader" ||
                            mats[i].name.Contains("Default-Material"))
                        {
                            needsFix = true;
                            break;
                        }
                    }
                }

                if (!needsFix)
                    continue;

                var mat = new Material(urpLit);
                mat.color = tint;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", tint);
                rend.sharedMaterial = mat;
            }
        }

        void OnDestroy()
        {
            if (ById.TryGetValue(enemyId, out var e) && e == this)
                ById.Remove(enemyId);
        }

        void Update()
        {
            if (_collapseT < 0f)
                return;

            _collapseT += Time.deltaTime;
            float u = Mathf.Clamp01(_collapseT / Mathf.Max(0.01f, collapseDuration));
            transform.rotation = Quaternion.Slerp(_startRot, _endRot, u);
            if (u >= 1f)
                _collapseT = -1f;
        }

        /// <summary>Host/offline kill entry point from SoftHorseChain.</summary>
        public bool TryKill(Vector3 hitPoint, Vector3 hitDirection)
        {
            if (IsDead)
                return false;

            KillInternal(hitPoint, hitDirection, replicate: true, spawnOrbs: true, grantXp: true);
            return true;
        }

        void KillInternal(
            Vector3 hitPoint,
            Vector3 hitDirection,
            bool replicate,
            bool spawnOrbs,
            bool grantXp)
        {
            if (IsDead)
                return;

            IsDead = true;
            EnemyCrowdSim.Unregister(this);
            ApplyCrowdLod(EnemyLod.Frozen);

            PlayDeathPresentation(hitPoint, hitDirection, spawnOrbs, grantXp);
            Died?.Invoke(this);

            if (replicate && Cali.Network.CoopSessionStarter.IsOnline)
            {
                var coop = Cali.Network.CoopGameController.Instance;
                if (coop != null)
                    coop.ServerBroadcastEnemyDeath(enemyId, hitPoint, hitDirection);
            }
        }

        void PlayDeathPresentation(Vector3 hitPoint, Vector3 hitDirection, bool spawnOrbs, bool grantXp)
        {
            if (_fxPlayed)
                return;
            _fxPlayed = true;

            EnemyDeathFx.SpawnBlood(
                useRandomPackFx ? null : bloodPrefab,
                hitPoint,
                hitDirection,
                useRandomPackFx ? null : bloodDecalPrefab,
                spawnBloodDecal);

            CaliCameraShake.PlayKill();

            if (_animal != null)
            {
                _animal.enabled = false;
                if (_animal.RB != null)
                {
                    _animal.RB.isKinematic = true;
                    _animal.RB.linearVelocity = Vector3.zero;
                }
            }

            var thrower = GetComponent<EnemySpearThrower>();
            if (thrower != null)
                thrower.enabled = false;

            var brain = GetComponent<EnemyChaseBrain>();
            if (brain != null)
                brain.enabled = false;

            var talk = GetComponent<EnemyTalkVoice>();
            if (talk != null)
            {
                talk.StopTalk();
                talk.enabled = false;
            }

            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
            }

            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] != null)
                        _colliders[i].enabled = false;
                }
            }

            bool ragdolled = useRagdollDeath &&
                             EnemyDeathRagdoll.TryActivate(this, _animator, hitPoint, hitDirection);

            if (!ragdolled)
                PlayDeathAnimationOrCollapse(hitDirection);

            if (spawnOrbs)
                SpawnOrbs(hitPoint, grantXp);
        }

        void PlayDeathAnimationOrCollapse(Vector3 hitDirection)
        {
            bool playedAnim = false;
            if (_animator != null && !string.IsNullOrEmpty(deathAnimatorTrigger))
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Trigger &&
                        p.name == deathAnimatorTrigger)
                    {
                        _animator.enabled = true;
                        _animator.SetTrigger(deathAnimatorTrigger);
                        playedAnim = true;
                        break;
                    }
                }
            }

            if (playedAnim)
                return;

            _startRot = transform.rotation;
            Vector3 axis = Vector3.Cross(Vector3.up, hitDirection);
            if (axis.sqrMagnitude < 0.001f)
                axis = transform.right;
            _endRot = Quaternion.AngleAxis(90f, axis.normalized) * _startRot;
            _collapseT = 0f;
        }

        void SpawnOrbs(Vector3 origin, bool grantXp)
        {
            if (grantXp)
                PlayerXp.EnsureExists();

            Transform horseA = null;
            Transform horseB = null;
            var chain = FindFirstObjectByType<Cali.Gameplay.SoftHorseChain>();
            if (chain != null)
            {
                if (chain.horseA != null)
                    horseA = chain.horseA.transform;
                if (chain.horseB != null)
                    horseB = chain.horseB.transform;
            }

            int count = Mathf.Max(1, orbCount);
            int perOrb = grantXp ? Mathf.Max(1, xpReward / count) : 0;
            int remainder = grantXp ? Mathf.Max(0, xpReward - perOrb * count) : 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Random.insideUnitSphere * 0.6f;
                offset.y = Mathf.Abs(offset.y) + 0.4f;
                int xp = perOrb + (i == 0 ? remainder : 0);
                XpOrb.Spawn(xpOrbPrefab, origin + offset, xp, horseA, horseB);
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0)
                return;

            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }
    }
}
