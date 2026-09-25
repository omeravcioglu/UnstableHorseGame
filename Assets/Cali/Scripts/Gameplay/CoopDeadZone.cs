using Cali.Network;
using Cali.UI;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Trigger volume: any player horse that enters sends both horses to the last
    /// coop save for this scene, and both players see "You died".
    ///
    /// Place this on a GameObject with a collider (or use Cali → Create Dead Zone).
    /// Scale the object to cover the pit / out-of-bounds area.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cali/Coop Dead Zone")]
    public class CoopDeadZone : MonoBehaviour
    {
        const float RespawnLockSeconds = 1.25f;
        const float DeathPromptSeconds = 2.4f;

        [Tooltip("Any collider on this object or children. Leave empty to auto-find / add a box.")]
        public Collider zone;

        [Tooltip("Used only if there is no save for this scene yet.")]
        public Transform fallbackSpawnA;
        [Tooltip("Used only if there is no save for this scene yet.")]
        public Transform fallbackSpawnB;

        [Tooltip("Turn off mesh renderers in Play so the volume is invisible in-game.")]
        public bool hideVisualInPlay = true;

        static float _lockUntil;
        static float _promptUntil;

        void Reset()
        {
            EnsureTrigger();
        }

        void Awake()
        {
            EnsureTrigger();
            if (hideVisualInPlay)
            {
                var rends = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                    rends[i].enabled = false;
            }
        }

        void Update()
        {
            if (_promptUntil <= 0f)
                return;

            if (Time.unscaledTime < _promptUntil)
            {
                if (!CoopSavePoint.IsBusy)
                    StableTimingHud.Ensure().ShowPrompt("You died");
                return;
            }

            _promptUntil = 0f;
            if (!CoopSavePoint.IsBusy && !StableLatch.IsAnyPlaying())
                StableTimingHud.Ensure().Hide();
        }

        void OnTriggerEnter(Collider other)
        {
            TryKill(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryKill(other);
        }

        void TryKill(Collider other)
        {
            if (!HasGameplayAuthority())
                return;
            if (Time.unscaledTime < _lockUntil)
                return;
            if (!IsPlayerHorse(other))
                return;

            _lockUntil = Time.unscaledTime + RespawnLockSeconds;
            RespawnBoth();
            ShowDeathPrompt();
            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastPlayerDied();
        }

        /// <summary>HP death or a pit: both horses return together to the last checkpoint.</summary>
        public static void OnHorseDied()
        {
            if (!HasGameplayAuthorityStatic())
                return;
            if (Time.unscaledTime < _lockUntil)
                return;

            _lockUntil = Time.unscaledTime + RespawnLockSeconds;
            RespawnBoth();
            ShowDeathPrompt();
            var coop = CoopGameController.Instance;
            if (coop != null)
                coop.BroadcastPlayerDied();
        }

        static void RespawnBoth()
        {
            if (CoopSavePoint.TryRespawnAfterDeath())
                return;

            var zone = FindFirstObjectByType<CoopDeadZone>();
            if (zone != null)
                zone.ApplyFallbackSpawns();
            else
                CoopSavePoint.SnapChainAfterTeleport();
        }

        public static void ShowDeathPrompt()
        {
            _promptUntil = Time.unscaledTime + DeathPromptSeconds;
            StableTimingHud.Ensure().ShowPrompt("You died");
        }

        void ApplyFallbackSpawns()
        {
            Transform spawnA = fallbackSpawnA != null ? fallbackSpawnA : fallbackSpawnB;
            Transform spawnB = fallbackSpawnB != null ? fallbackSpawnB : fallbackSpawnA;
            if (spawnA == null)
            {
                Debug.LogWarning("[CoopDeadZone] No checkpoint for this scene and no fallback spawns.", this);
                return;
            }

            Vector3 posA = spawnA.position;
            Quaternion rotA = spawnA.rotation;
            Vector3 posB = spawnB.position;
            Quaternion rotB = spawnB.rotation;
            CoopSavePoint.FitPairToChain(posA, rotA, posB, rotB, out posA, out rotA, out posB, out rotB);
            CoopSavePoint.RestorePlayHorseHealth();

            var coop = CoopGameController.Instance;
            if (coop != null && coop.Object != null && coop.Object.IsValid && coop.HasStateAuthority)
            {
                coop.TeleportPlayHorses(posA, rotA, posB, rotB);
                CoopSavePoint.SnapChainAfterTeleport();
                return;
            }

            MAnimal horseA = coop != null ? coop.PlayHorseA : null;
            MAnimal horseB = coop != null ? coop.PlayHorseB : null;
            if (horseA == null)
                horseA = FindAnimalByName("Horse Realistic");
            if (horseB == null)
                horseB = FindAnimalByName("Horse Unicorn");
            CoopSavePoint.PlaceHorse(horseA, posA, rotA);
            CoopSavePoint.PlaceHorse(horseB, posB, rotB);
            CoopSavePoint.SnapChainAfterTeleport();
        }

        static bool IsPlayerHorse(Collider other)
        {
            if (other == null)
                return false;

            var animal = other.GetComponentInParent<MAnimal>();
            if (animal == null)
                return false;

            var coop = CoopGameController.Instance;
            if (coop != null && (animal == coop.PlayHorseA || animal == coop.PlayHorseB))
                return true;

            string n = animal.gameObject.name;
            return n == "Horse Realistic" || n == "Horse Unicorn"
                || n.Contains("Horse Realistic") || n.Contains("Horse Unicorn");
        }

        static bool HasGameplayAuthority()
        {
            return HasGameplayAuthorityStatic();
        }

        static bool HasGameplayAuthorityStatic()
        {
            var coop = CoopGameController.Instance;
            if (coop == null || coop.Object == null || !coop.Object.IsValid)
                return true;
            return coop.HasStateAuthority;
        }

        void EnsureTrigger()
        {
            if (zone == null)
                zone = GetComponent<Collider>();
            if (zone == null)
                zone = GetComponentInChildren<Collider>();
            if (zone == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                zone = box;
            }

            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (col == null)
                    continue;
                if (col is MeshCollider mesh)
                    mesh.convex = true;
                col.isTrigger = true;
            }

            // Parent messages only arrive if this object has a rigidbody when
            // the collider lives on a child.
            bool colliderOnChild = zone != null && zone.gameObject != gameObject;
            if (colliderOnChild && GetComponent<Rigidbody>() == null)
            {
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = true;
            }

            int animal = LayerMask.NameToLayer("Animal");
            int body = LayerMask.NameToLayer("BodyPart");
            if (gameObject.layer == animal || gameObject.layer == body)
                gameObject.layer = 0;
        }

        void OnDrawGizmos()
        {
            DrawZoneGizmo(new Color(0.85f, 0.12f, 0.12f, 0.18f));
        }

        void OnDrawGizmosSelected()
        {
            DrawZoneGizmo(new Color(0.95f, 0.2f, 0.15f, 0.35f));
        }

        void DrawZoneGizmo(Color fill)
        {
            var cols = GetComponentsInChildren<Collider>(true);
            if (cols.Length == 0)
                return;

            Gizmos.color = fill;
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (col == null)
                    continue;

                var box = col as BoxCollider;
                if (box != null)
                {
                    Gizmos.matrix = box.transform.localToWorldMatrix;
                    Gizmos.DrawCube(box.center, box.size);
                    Gizmos.color = new Color(fill.r, fill.g, fill.b, Mathf.Clamp01(fill.a + 0.35f));
                    Gizmos.DrawWireCube(box.center, box.size);
                    Gizmos.color = fill;
                    continue;
                }

                Bounds b = col.bounds;
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawCube(b.center, b.size);
            }
        }

        static MAnimal FindAnimalByName(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name == objectName)
                    return animals[i];
            }

            return null;
        }
    }
}
