using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Gameplay
{
    /// <summary>
    /// Horses never block each other. Walkable ground is the prefab mask plus a small set of
    /// extra environment layers (Default, Water, Item, and the unnamed layers parkour props
    /// often land on). It does not replace the mask with "everything", because that makes
    /// Malbers treat walls, foliage and interior mesh faces as floor and the horse starts
    /// to slide and tilt as if the controls changed.
    /// </summary>
    public static class HorsePhysics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyAll();

        public static void ApplyAll()
        {
            IgnoreHorseLayers();

            var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
                Apply(animals[i]);

            IgnoreHorsePairs(animals);
        }

        public static void Apply(MAnimal animal)
        {
            if (animal == null || IsCombatBody(animal))
                return;

            animal.groundLayer.Value |= WalkableExtras();
            ExcludeOtherHorses(animal.RB);
            SmoothMotion(animal);
            animal.UseSmoothVertical = false;
            animal.slideAmount = 0f;
            animal.slideThreshold = 90f;
            animal.SlopeDirectionSmooth = Vector3.zero;
            animal.UpInertia_Clear();
            ApplyWallStopMaterial(animal);
            HorseColliderStop.Ensure(animal);
        }

        /// <summary>
        /// A network proxy writes its transform from the snapshot every render frame and is
        /// kinematic, so Animate Physics never ticks the Animator. Normal update is what
        /// actually plays the gait the owner is publishing.
        /// </summary>
        public static void ApplyProxy(MAnimal animal)
        {
            if (animal == null || IsCombatBody(animal))
                return;

            var anim = animal.Anim;
            if (anim == null)
                return;

            if (!anim.enabled)
                anim.enabled = true;
            if (anim.updateMode != AnimatorUpdateMode.Normal)
                anim.updateMode = AnimatorUpdateMode.Normal;
            if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        static void IgnoreHorseLayers()
        {
            int animal = LayerMask.NameToLayer("Animal");
            int body = LayerMask.NameToLayer("BodyPart");

            if (animal >= 0)
                Physics.IgnoreLayerCollision(animal, animal, true);
            if (animal >= 0 && body >= 0)
            {
                Physics.IgnoreLayerCollision(animal, body, true);
                Physics.IgnoreLayerCollision(body, body, true);
            }
        }

        static void SmoothMotion(MAnimal animal)
        {
            var rb = animal.RB;
            bool simulated = animal.DisablePosition == false && (rb == null || !rb.isKinematic);

            if (rb != null && simulated)
            {
                if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                if (rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            var anim = animal.Anim;
            if (anim == null)
                return;

            // Simulated horses stay on Animate Physics + interpolated bodies. Proxies are
            // kinematic, so that mode never evaluates — they need a Normal update instead.
            var wanted = simulated ? AnimatorUpdateMode.Fixed : AnimatorUpdateMode.Normal;
            if (anim.updateMode != wanted)
                anim.updateMode = wanted;
            if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        static PhysicsMaterial _wallStop;

        static void ApplyWallStopMaterial(MAnimal animal)
        {
            var col = animal.MainCollider ?? animal.GetComponent<CapsuleCollider>();
            if (col == null)
                return;

            if (_wallStop == null)
            {
                _wallStop = new PhysicsMaterial("CaliHorseWallStop")
                {
                    dynamicFriction = 0.85f,
                    staticFriction = 0.95f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }

            col.sharedMaterial = _wallStop;
        }

        static void ExcludeOtherHorses(Rigidbody rb)
        {
            if (rb == null)
                return;

            LayerMask skip = 0;
            int animal = LayerMask.NameToLayer("Animal");
            int body = LayerMask.NameToLayer("BodyPart");
            if (animal >= 0)
                skip |= 1 << animal;
            if (body >= 0)
                skip |= 1 << body;

            rb.excludeLayers |= skip;
        }

        static void IgnoreHorsePairs(MAnimal[] animals)
        {
            if (animals == null)
                return;

            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] == null || IsCombatBody(animals[i]))
                    continue;
                var a = animals[i].GetComponentsInChildren<Collider>(true);

                for (int j = i + 1; j < animals.Length; j++)
                {
                    if (animals[j] == null || IsCombatBody(animals[j]))
                        continue;
                    var b = animals[j].GetComponentsInChildren<Collider>(true);
                    IgnoreSets(a, b);
                }
            }
        }

        static void IgnoreSets(Collider[] a, Collider[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || !a[i].enabled)
                    continue;
                for (int j = 0; j < b.Length; j++)
                {
                    if (b[j] == null || !b[j].enabled)
                        continue;
                    Physics.IgnoreCollision(a[i], b[j], true);
                }
            }
        }

        /// <summary>
        /// Layers to add, never a full replace. 6–13 are the unnamed user layers; FirstPark's
        /// castle pieces sit on 9.
        /// </summary>
        static int WalkableExtras()
        {
            int mask = 1 << 0; // Default
            int water = LayerMask.NameToLayer("Water");
            if (water >= 0)
                mask |= 1 << water;
            int item = LayerMask.NameToLayer("Item");
            if (item >= 0)
                mask |= 1 << item;
            for (int i = 6; i <= 13; i++)
                mask |= 1 << i;
            return mask;
        }

        static bool IsCombatBody(MAnimal animal)
        {
            int enemy = LayerMask.NameToLayer("Enemy");
            Transform t = animal.transform;
            while (t != null)
            {
                if (enemy >= 0 && t.gameObject.layer == enemy)
                    return true;
                if (t.name.StartsWith("CaliEnemy"))
                    return true;
                t = t.parent;
            }
            return false;
        }
    }
}
