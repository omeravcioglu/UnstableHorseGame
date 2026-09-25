using Cali.Gameplay;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Wires XP and the chain kill mask. Does not spawn starter wolves or humanoids.
    /// </summary>
    public class ChainKillEncounterBootstrap : MonoBehaviour
    {
        public GameObject wolfPrefab;
        public GameObject humanoidPrefab;
        public GameObject xpOrbPrefab;
        public GameObject bloodPrefab;

        [Tooltip("How many of each kind to spawn if the scene has no ChainKillableEnemy yet.")]
        public int countPerKind = 0;

        public float spawnRadius = 8f;

        void Start()
        {
            PlayerXp.EnsureExists();
            TryAssignDefaultBlood();
            EnsureSoftChainMask();
            // Starter wolves / humanoids are not spawned. Scene enemies stay as authored.
        }

        Vector3 GetSpawnCenter()
        {
            var chain = FindFirstObjectByType<SoftHorseChain>();
            if (chain != null && chain.horseA != null)
                return chain.horseA.transform.position;
            return transform.position;
        }

        void SpawnFallbackCapsules(Vector3 center, int refStartId)
        {
            int id = refStartId;
            for (int i = 0; i < countPerKind; i++)
            {
                float ang = i / (float)countPerKind * Mathf.PI * 2f;
                SpawnCapsuleEnemy(
                    EnemyKind.Wolf,
                    center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spawnRadius,
                    ref id);
            }

            for (int i = 0; i < countPerKind; i++)
            {
                float ang = (i + 0.5f) / countPerKind * Mathf.PI * 2f;
                SpawnCapsuleEnemy(
                    EnemyKind.Humanoid,
                    center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spawnRadius,
                    ref id);
            }

            ChainKillableEnemy.AssignStableIds();
        }

        void SpawnCapsuleEnemy(EnemyKind kind, Vector3 pos, ref int id)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = kind == EnemyKind.Wolf ? $"CaliEnemy_Wolf_{id}" : $"CaliEnemy_Humanoid_{id}";
            go.transform.position = pos;
            go.transform.localScale = kind == EnemyKind.Wolf
                ? new Vector3(0.7f, 0.45f, 1.1f)
                : new Vector3(0.55f, 1f, 0.55f);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                       ?? Shader.Find("Standard"));
                mat.color = kind == EnemyKind.Wolf
                    ? new Color(0.35f, 0.28f, 0.22f)
                    : new Color(0.45f, 0.4f, 0.32f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", mat.color);
                rend.material = mat;
            }

            var enemy = go.AddComponent<ChainKillableEnemy>();
            enemy.kind = kind;
            enemy.enemyId = id++;
            enemy.useRandomPackFx = true;
            enemy.bloodPrefab = null;
            enemy.bloodDecalPrefab = null;
            enemy.spawnBloodDecal = true;
            enemy.xpOrbPrefab = xpOrbPrefab;
            enemy.xpReward = kind == EnemyKind.Wolf ? 20 : 25;
            enemy.orbCount = 4;
        }

        void ResolvePrefabs()
        {
            if (wolfPrefab == null)
                wolfPrefab = Resources.Load<GameObject>("Combat/CaliEnemy_Wolf");
            if (humanoidPrefab == null)
                humanoidPrefab = Resources.Load<GameObject>("Combat/CaliEnemy_Humanoid");
            if (xpOrbPrefab == null)
                xpOrbPrefab = Resources.Load<GameObject>("Combat/XpOrb");
            // bloodPrefab left null on purpose — EnemyDeathFx uses BloodFxLibrary random picks.
        }

        void TryAssignDefaultBlood()
        {
            // No default single splash. Random pack FX come from BloodFxLibrary.
        }

        void EnsureSoftChainMask()
        {
            var chain = FindFirstObjectByType<SoftHorseChain>();
            if (chain == null)
                return;

            if (chain.enemyHitMask.value == 0)
            {
                int layer = LayerMask.NameToLayer("Enemy");
                if (layer >= 0)
                    chain.enemyHitMask = 1 << layer;
            }
        }

        void SpawnRing(GameObject prefab, EnemyKind kind, Vector3 center, int count, ref int id, float phase = 0f)
        {
            if (prefab == null || count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                float ang = (i + phase) / count * Mathf.PI * 2f;
                Vector3 pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spawnRadius;
                pos.y = center.y;

                var go = Instantiate(prefab, pos, Quaternion.LookRotation(center - pos));
                go.name = $"{prefab.name}_{i + 1}";

                var enemy = go.GetComponent<ChainKillableEnemy>();
                if (enemy == null)
                    enemy = go.AddComponent<ChainKillableEnemy>();

                enemy.kind = kind;
                enemy.enemyId = id++;
                enemy.useRandomPackFx = true;
                enemy.bloodPrefab = null;
                enemy.bloodDecalPrefab = null;
                enemy.spawnBloodDecal = true;
                if (enemy.xpOrbPrefab == null)
                    enemy.xpOrbPrefab = xpOrbPrefab;
            }
        }
    }
}
