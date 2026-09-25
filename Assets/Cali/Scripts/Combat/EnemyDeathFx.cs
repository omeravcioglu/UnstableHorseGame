using UnityEngine;
using UnityEngine.Rendering;

namespace Cali.Combat
{
    /// <summary>
    /// Kill FX: one airborne blood splash, plus a ground splat.
    /// Prefers a random RVFX pack splash; falls back to the procedural burst if the library is empty.
    /// </summary>
    public static class EnemyDeathFx
    {
        static readonly string[] DecalResourceNames =
        {
            "Combat/BloodDecal_01",
            "Combat/BloodDecal_02",
            "Combat/BloodDecal_03",
            "Combat/BloodDecal_04",
        };

        static readonly Color DarkBlood = new Color(0.32f, 0.03f, 0.03f, 1f);
        static readonly Color DarkBloodBright = new Color(0.42f, 0.04f, 0.035f, 1f);
        static Material _particleMat;
        static Material[] _decalMats;
        static Texture2D[] _decalTextures;
        static Texture2D _softTex;

        public static void SpawnBlood(
            GameObject bloodPrefabOverride,
            Vector3 hitPoint,
            Vector3 hitDirection,
            GameObject bloodDecalPrefabOverride = null,
            bool spawnDecal = true)
        {
            Vector3 dir = hitDirection.sqrMagnitude > 0.0001f
                ? hitDirection.normalized
                : (Vector3.up + Vector3.forward).normalized;

            var lib = BloodFxLibrary.Get();
            GameObject splash = bloodPrefabOverride;
            if (splash == null && lib != null)
                splash = lib.PickRandomSplash();

            if (splash != null)
                SpawnPackSplash(splash, hitPoint + Vector3.up * 0.35f, 1.35f);
            else
                SpawnSoftBloodBurst(hitPoint, dir);

            Cali.Audio.CaliGameplayAudio.PlayAt(Cali.Audio.CaliSoundIds.BloodSplash, hitPoint);

            if (!spawnDecal)
                return;

            SpawnGroundDecalFromTextures(hitPoint);
        }

        static void SpawnPackSplash(GameObject splash, Vector3 position, float scale)
        {
            if (splash == null)
                return;

            var pack = Object.Instantiate(splash, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            pack.transform.localScale *= scale;
            TintPackDarkRed(pack);
            PlayPackParticles(pack);
            Object.Destroy(pack, 5f);
        }

        static void PlayPackParticles(GameObject pack)
        {
            if (pack == null)
                return;

            pack.SetActive(true);
            var systems = pack.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null)
                    continue;
                var main = systems[i].main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 4f);
                main.startColor = DarkBloodBright;
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        public static void SpawnSimpleUrpBlood(Vector3 hitPoint, Vector3 hitDirection)
        {
            SpawnSoftBloodBurst(hitPoint, hitDirection);
        }

        public static void SpawnBloodDecal(Vector3 hitPoint, GameObject bloodDecalPrefab = null)
        {
            SpawnGroundDecalFromTextures(hitPoint);
            if (bloodDecalPrefab != null)
                SpawnPackDecal(hitPoint, bloodDecalPrefab);
        }

        static void SpawnSoftBloodBurst(Vector3 hitPoint, Vector3 hitDirection)
        {
            var go = new GameObject("CaliBloodBurst");
            go.transform.position = hitPoint + Vector3.up * 0.2f;
            if (hitDirection.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(hitDirection.normalized, Vector3.up);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = true;
            // Longer-lived droplets so the burst reads clearly.
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.65f);
            main.startColor = DarkBloodBright;
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 100;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)Random.Range(28, 44)),
                new ParticleSystem.Burst(0.08f, 14)
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 48f;
            shape.radius = 0.1f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.55f, 0.85f),
                    new Keyframe(1f, 0.05f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(DarkBloodBright, 0f),
                    new GradientColorKey(DarkBlood, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetParticleMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            Object.Destroy(go, 5.2f);
        }

        static void SpawnGroundDecalFromTextures(Vector3 hitPoint)
        {
            if (!TryFindGround(hitPoint, out Vector3 pos, out Vector3 normal))
            {
                // Last resort: drop straight down from hit, assume flat floor.
                pos = new Vector3(hitPoint.x, hitPoint.y - 1f, hitPoint.z);
                normal = Vector3.up;
            }

            // Very slight lift only — keeps splat glued to floor.
            pos += normal * 0.015f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "CaliBloodDecal";
            Object.Destroy(go.GetComponent<Collider>());

            // Flatten onto the ground: Unity Quad faces +Z, so +Z = surface normal.
            go.transform.position = pos;
            go.transform.rotation = FlatOnSurface(normal, Random.Range(0f, 360f));

            float size = Random.Range(1.6f, 2.6f);
            go.transform.localScale = new Vector3(size, size, 1f);

            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = GetDecalMaterial(Random.Range(0, 4));
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;

            Object.Destroy(go, 180f);
        }

        static void SpawnPackDecal(Vector3 hitPoint, GameObject prefab)
        {
            // Trail decals need motion; Static + SpriteSheet flatten onto the ground.
            if (prefab == null || prefab.name.Contains("Trail"))
                return;

            if (!TryFindGround(hitPoint, out Vector3 pos, out Vector3 normal))
                return;

            pos += normal * 0.02f;
            var inst = Object.Instantiate(prefab, pos, FlatOnSurface(normal, Random.Range(0f, 360f)));
            float s = Random.Range(1.0f, 1.6f);
            inst.transform.localScale = Vector3.Scale(inst.transform.localScale, new Vector3(s, s, s));
            TintPackDarkRed(inst);
            PlayPackParticles(inst);
            Object.Destroy(inst, 180f);
        }

        static Quaternion FlatOnSurface(Vector3 normal, float yawDegrees)
        {
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.up;
            normal.Normalize();

            // Build a basis where forward aligns with the surface normal (quad face).
            Vector3 tangent = Vector3.Cross(normal, Vector3.right);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(normal, Vector3.forward);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(tangent, normal).normalized;

            var surface = Quaternion.LookRotation(normal, bitangent);
            return surface * Quaternion.AngleAxis(yawDegrees, Vector3.forward);
        }

        static bool TryFindGround(Vector3 from, out Vector3 point, out Vector3 normal)
        {
            point = from;
            normal = Vector3.up;

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int mask = ~0;
            if (enemyLayer >= 0)
                mask &= ~(1 << enemyLayer);

            // Cast from well above the hit so we find the real floor, not the corpse.
            Vector3 origin = from + Vector3.up * 6f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 20f, mask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
            {
                // Retry without mask filtering in case ground is oddly layered.
                hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            }

            RaycastHit best = default;
            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                if (IsIgnoredGroundCollider(hit.collider))
                    continue;

                // Prefer near-horizontal surfaces (floors), lower is better among those.
                float flat = Mathf.Clamp01(hit.normal.y);
                if (flat < 0.35f)
                    continue;

                float score = flat * 10f - hit.distance * 0.01f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hit;
                    found = true;
                }
            }

            if (!found)
                return false;

            point = best.point;
            normal = best.normal.normalized;
            return true;
        }

        static bool IsIgnoredGroundCollider(Collider col)
        {
            if (col == null)
                return true;
            if (col.GetComponentInParent<ChainKillableEnemy>() != null)
                return true;
            if (col.GetComponentInParent<MalbersAnimations.Controller.MAnimal>() != null)
                return true;
            if (col.GetComponentInParent<XpOrb>() != null)
                return true;
            // Skip thin trigger volumes / powerups.
            if (col.isTrigger)
                return true;
            return false;
        }

        static void TintPackDarkRed(GameObject pack)
        {
            if (pack == null)
                return;

            var renderers = pack.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var rend = renderers[i];
                if (rend == null)
                    continue;

                var mats = rend.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null)
                        continue;
                    ApplyColor(mats[m], DarkBlood);
                    if (mats[m].HasProperty("_ColorIntensity"))
                        mats[m].SetFloat("_ColorIntensity", 0.72f);
                }

                rend.materials = mats;
            }
        }

        static Material GetParticleMaterial()
        {
            if (_particleMat != null)
                return _particleMat;

            var packMat = FindPackSplashMaterial();
            if (packMat != null)
            {
                _particleMat = new Material(packMat);
                return _particleMat;
            }

            var shader = Shader.Find("Cali/BloodFX_URP")
                         ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");

            _particleMat = new Material(shader);
            ApplyColor(_particleMat, DarkBlood);
            ApplyTexture(_particleMat, GetSoftTexture());
            ConfigureTransparent(_particleMat, cullOff: true);
            return _particleMat;
        }

        static Material FindPackSplashMaterial()
        {
            var lib = BloodFxLibrary.Get();
            if (lib == null || lib.splashPrefabs == null)
                return null;

            for (int i = 0; i < lib.splashPrefabs.Length; i++)
            {
                var prefab = lib.splashPrefabs[i];
                if (prefab == null)
                    continue;

                var renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null && renderers[r].sharedMaterial != null)
                        return renderers[r].sharedMaterial;
                }
            }

            return null;
        }

        static Material FindPackDecalMaterial()
        {
            var lib = BloodFxLibrary.Get();
            if (lib == null || lib.decalPrefabs == null)
                return null;

            for (int i = 0; i < lib.decalPrefabs.Length; i++)
            {
                var prefab = lib.decalPrefabs[i];
                if (prefab == null || prefab.name.Contains("Trail"))
                    continue;

                var renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null && renderers[r].sharedMaterial != null)
                        return renderers[r].sharedMaterial;
                }

                var meshRend = prefab.GetComponentInChildren<MeshRenderer>(true);
                if (meshRend != null && meshRend.sharedMaterial != null)
                    return meshRend.sharedMaterial;
            }

            return null;
        }

        static Material GetDecalMaterial(int index)
        {
            EnsureDecalCache();
            index = Mathf.Clamp(index, 0, _decalMats.Length - 1);
            if (_decalMats[index] != null)
                return _decalMats[index];

            var packDecalMat = FindPackDecalMaterial();
            Material mat;
            if (packDecalMat != null)
            {
                mat = new Material(packDecalMat);
            }
            else
            {
                var shader = Shader.Find("Cali/BloodFX_URP")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default");
                mat = new Material(shader);
                ApplyColor(mat, DarkBlood);
                ConfigureTransparent(mat, cullOff: true);
            }

            if (_decalTextures[index] != null)
                ApplyTexture(mat, _decalTextures[index]);
            else
                ApplyTexture(mat, GetSoftTexture());
            ApplyColor(mat, DarkBlood);
            mat.renderQueue = (int)RenderQueue.Transparent + 50;
            _decalMats[index] = mat;
            return mat;
        }

        static void EnsureDecalCache()
        {
            if (_decalMats != null)
                return;

            _decalMats = new Material[4];
            _decalTextures = new Texture2D[4];
            for (int i = 0; i < 4; i++)
                _decalTextures[i] = Resources.Load<Texture2D>(DecalResourceNames[i]);
        }

        static Texture2D GetSoftTexture()
        {
            if (_softTex != null)
                return _softTex;

            _softTex = Resources.Load<Texture2D>("Combat/BloodParticleSoft")
                       ?? Resources.Load<Texture2D>("Combat/BloodParticleGlow");

            if (_softTex == null)
                _softTex = GenerateSoftCircle(128);

            return _softTex;
        }

        static Texture2D GenerateSoftCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CaliBloodSoftCircle",
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
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply(false, true);
            return tex;
        }

        static void ApplyColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            mat.color = color;
        }

        static void ApplyTexture(Material mat, Texture tex)
        {
            if (tex == null)
                return;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
            mat.mainTexture = tex;
        }

        static void ConfigureTransparent(Material mat, bool cullOff)
        {
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (cullOff && mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f); // Off — visible from both sides
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }
    }
}
