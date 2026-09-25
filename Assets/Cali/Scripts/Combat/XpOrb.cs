using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cali.Combat
{
    /// <summary>
    /// Soft green XP pickup: small billboard glow (opaque center → transparent edge).
    /// </summary>
    public class XpOrb : MonoBehaviour
    {
        public float seekDelay = 0.35f;
        public float seekSpeed = 10f;
        public float arriveDistance = 0.35f;
        public int xpValue = 5;
        [Tooltip("HP restored on the horse that collected this orb.")]
        public float healAmount = 20f;
        public float lifetime = 8f;
        public float orbScale = 0.14f;

        Transform _targetA;
        Transform _targetB;
        Transform _activeTarget;
        float _spawnTime;
        bool _seeking;
        static Material _sharedOrbMat;
        static Texture2D _softTex;

        public static XpOrb Spawn(
            GameObject prefab,
            Vector3 position,
            int xp,
            Transform horseA,
            Transform horseB)
        {
            // Always build a soft glow orb — prefab spheres look too thick/solid.
            var go = new GameObject("XpOrb");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.14f;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            // Builtin quad path can fail — fallback to CreatePrimitive then strip.
            if (mf.sharedMesh == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mf.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(temp);
            }

            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = GetOrbMaterial();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.45f, 1f, 0.55f);
            light.intensity = 0.55f;
            light.range = 1.6f;

            var orb = go.AddComponent<XpOrb>();
            orb.xpValue = xp;
            orb.orbScale = 0.14f;
            orb.Configure(horseA, horseB);

            // If a prefab was provided, we still use soft orb visuals (ignore thick mesh).
            if (prefab != null)
                go.name = prefab.name;

            return orb;
        }

        static Material GetOrbMaterial()
        {
            if (_sharedOrbMat != null)
                return _sharedOrbMat;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            _sharedOrbMat = new Material(shader);

            var green = new Color(0.35f, 1f, 0.45f, 1f);
            if (_sharedOrbMat.HasProperty("_BaseColor"))
                _sharedOrbMat.SetColor("_BaseColor", green);
            if (_sharedOrbMat.HasProperty("_Color"))
                _sharedOrbMat.SetColor("_Color", green);
            _sharedOrbMat.color = green;

            var tex = GetSoftTexture();
            if (_sharedOrbMat.HasProperty("_BaseMap"))
                _sharedOrbMat.SetTexture("_BaseMap", tex);
            if (_sharedOrbMat.HasProperty("_MainTex"))
                _sharedOrbMat.SetTexture("_MainTex", tex);
            _sharedOrbMat.mainTexture = tex;

            if (_sharedOrbMat.HasProperty("_Surface"))
                _sharedOrbMat.SetFloat("_Surface", 1f);
            if (_sharedOrbMat.HasProperty("_Blend"))
                _sharedOrbMat.SetFloat("_Blend", 0f);
            if (_sharedOrbMat.HasProperty("_SrcBlend"))
                _sharedOrbMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (_sharedOrbMat.HasProperty("_DstBlend"))
                _sharedOrbMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (_sharedOrbMat.HasProperty("_ZWrite"))
                _sharedOrbMat.SetFloat("_ZWrite", 0f);
            if (_sharedOrbMat.HasProperty("_Cull"))
                _sharedOrbMat.SetFloat("_Cull", 0f);
            _sharedOrbMat.renderQueue = (int)RenderQueue.Transparent;
            _sharedOrbMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            return _sharedOrbMat;
        }

        static Texture2D GetSoftTexture()
        {
            if (_softTex != null)
                return _softTex;

            _softTex = Resources.Load<Texture2D>("Combat/BloodParticleGlow")
                       ?? Resources.Load<Texture2D>("Combat/BloodParticleSoft");
            if (_softTex != null)
                return _softTex;

            // Soft radial falloff: opaque center → transparent rim.
            const int size = 128;
            _softTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "XpOrbSoft",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Smoothstep edge so rim is fully transparent.
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // smoothstep
                    a *= a; // stronger soft falloff
                    _softTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            _softTex.Apply(false, true);
            return _softTex;
        }

        public void Configure(Transform horseA, Transform horseB)
        {
            _targetA = horseA;
            _targetB = horseB;
            _spawnTime = Time.time;
            transform.localScale = Vector3.one * orbScale;
            PickTarget();
        }

        void HealCollector()
        {
            if (_activeTarget == null || healAmount <= 0f)
                return;

            var health = _activeTarget.GetComponent<HorseHealth>()
                         ?? _activeTarget.GetComponentInParent<HorseHealth>();
            if (health == null)
            {
                var animal = _activeTarget.GetComponent<MAnimal>()
                             ?? _activeTarget.GetComponentInParent<MAnimal>();
                if (animal != null)
                    health = animal.GetComponent<HorseHealth>()
                             ?? animal.gameObject.AddComponent<HorseHealth>();
            }

            if (health != null)
                health.Heal(healAmount);
        }

        void PickTarget()
        {
            if (_targetA != null && _targetB != null)
                _activeTarget = Random.value < 0.5f ? _targetA : _targetB;
            else
                _activeTarget = _targetA != null ? _targetA : _targetB;
        }

        void LateUpdate()
        {
            // Billboard toward camera so the soft disc always reads as a glow.
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        void Update()
        {
            if (Time.time - _spawnTime > lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Gentle pulse.
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
            transform.localScale = Vector3.one * (orbScale * pulse);

            if (!_seeking)
            {
                if (Time.time - _spawnTime >= seekDelay)
                    _seeking = true;
                else
                {
                    transform.position += Vector3.up * (0.45f * Time.deltaTime);
                    return;
                }
            }

            if (_activeTarget == null)
            {
                PickTarget();
                if (_activeTarget == null)
                    return;
            }

            Vector3 aim = _activeTarget.position + Vector3.up * 1.1f;
            transform.position = Vector3.MoveTowards(
                transform.position, aim, seekSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, aim) <= arriveDistance)
            {
                PlayerXp.EnsureExists().AddXp(xpValue);
                HealCollector();
                Cali.Audio.CaliGameplayAudio.Play(Cali.Audio.CaliSoundIds.XpOrb);
                Destroy(gameObject);
            }
        }
    }
}
