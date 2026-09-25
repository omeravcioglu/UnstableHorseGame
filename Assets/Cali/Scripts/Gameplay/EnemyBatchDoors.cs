using System.Collections;
using System.Collections.Generic;
using Cali.Audio;
using Cali.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace Cali.Gameplay
{
    /// <summary>
    /// Opens door 1 when every enemy in batch 1 is dead, then door 2, then door 3.
    /// Batches can optionally wait on a StableLatch mini-game before the story door opens.
    /// Parent each batch under an empty, drop this on a manager object, and assign the three roots + doors.
    /// </summary>
    public class EnemyBatchDoors : MonoBehaviour
    {
        [System.Serializable]
        public class Batch
        {
            public string name = "Batch";

            [Tooltip("All ChainKillableEnemy under this object count for the batch.")]
            public Transform enemiesRoot;

            [Tooltip("Optional extra enemies not parented under the root.")]
            public ChainKillableEnemy[] extraEnemies;

            [Tooltip("Door / gate that opens when this batch is cleared.")]
            public Transform door;

            [Tooltip("Swing the door on world Y. 0 = raise instead.")]
            public float swingYaw = 95f;

            [Tooltip("If swing yaw is 0, the door slides up by this many meters.")]
            public float raiseHeight = 4.5f;

            public float openSeconds = 1.2f;

            [Tooltip("If set, clearing this batch unlocks the stable latch. The story door waits until the latch mini-game finishes.")]
            public StableLatch latch;
        }

        struct HeldDoor
        {
            public Transform t;
            public Vector3 pos;
            public Quaternion rot;
        }

        [Tooltip("Cleared in order. Door N opens only after batches 1..N are all dead.")]
        public Batch[] batches = new Batch[]
        {
            new Batch { name = "Batch 1" },
            new Batch { name = "Batch 2" },
            new Batch { name = "Batch 3" }
        };

        public UnityEvent<int> onBatchCleared;

        List<ChainKillableEnemy>[] _enemies;
        readonly List<HeldDoor> _held = new();
        readonly HashSet<Transform> _openedDoors = new();
        bool[] _batchOpened;
        int _openThrough = -1;
        bool _opening;

        void Reset()
        {
            batches = new[]
            {
                new Batch { name = "Batch 1" },
                new Batch { name = "Batch 2" },
                new Batch { name = "Batch 3" }
            };
        }

        void OnEnable()
        {
            ChainKillableEnemy.Died += OnEnemyDied;
            HookLatches();
        }

        void OnDisable()
        {
            ChainKillableEnemy.Died -= OnEnemyDied;
            UnhookLatches();
        }

        void Start()
        {
            CollectEnemies();
            HookLatches();
            TryAdvance();
        }

        void LateUpdate()
        {
            for (int i = 0; i < _held.Count; i++)
            {
                var h = _held[i];
                if (h.t == null)
                    continue;
                h.t.SetPositionAndRotation(h.pos, h.rot);
            }
        }

        void CollectEnemies()
        {
            if (batches == null)
                return;

            _enemies = new List<ChainKillableEnemy>[batches.Length];
            _batchOpened = new bool[batches.Length];
            for (int i = 0; i < batches.Length; i++)
            {
                var list = new List<ChainKillableEnemy>();
                var batch = batches[i];
                if (batch != null && batch.enemiesRoot != null)
                    batch.enemiesRoot.GetComponentsInChildren(true, list);

                if (batch != null && batch.extraEnemies != null)
                {
                    for (int e = 0; e < batch.extraEnemies.Length; e++)
                    {
                        var extra = batch.extraEnemies[e];
                        if (extra != null && !list.Contains(extra))
                            list.Add(extra);
                    }
                }

                _enemies[i] = list;
            }
        }

        void OnEnemyDied(ChainKillableEnemy enemy)
        {
            if (enemy == null)
                return;
            TryAdvance();
        }

        void HookLatches()
        {
            if (batches == null)
                return;

            for (int i = 0; i < batches.Length; i++)
            {
                var latch = batches[i] != null ? batches[i].latch : null;
                if (latch == null)
                    continue;
                latch.Completed -= TryAdvance;
                latch.Completed += TryAdvance;
            }
        }

        void UnhookLatches()
        {
            if (batches == null)
                return;

            for (int i = 0; i < batches.Length; i++)
            {
                var latch = batches[i] != null ? batches[i].latch : null;
                if (latch == null)
                    continue;
                latch.Completed -= TryAdvance;
            }
        }

        void TryAdvance()
        {
            if (_opening || batches == null || _batchOpened == null)
                return;

            int next = _openThrough + 1;
            if (next >= batches.Length || _batchOpened[next] || !BatchCleared(next))
                return;

            var batch = batches[next];
            var latch = batch != null ? batch.latch : null;
            if (latch != null && !latch.IsCompleted)
            {
                latch.Unlock();
                return;
            }

            _batchOpened[next] = true;
            _openThrough = next;
            _opening = true;
            onBatchCleared?.Invoke(next);
            StartCoroutine(OpenDoor(batch));
        }

        bool BatchCleared(int index)
        {
            if (_enemies == null || index < 0 || index >= _enemies.Length)
                return false;

            var list = _enemies[index];
            if (list == null || list.Count == 0)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var enemy = list[i];
                if (enemy != null && !enemy.IsDead)
                    return false;
            }

            return true;
        }

        IEnumerator OpenDoor(Batch batch)
        {
            var door = batch != null ? batch.door : null;
            if (door == null || _openedDoors.Contains(door))
            {
                _opening = false;
                TryAdvance();
                yield break;
            }

            _openedDoors.Add(door);
            yield return AnimateSwingOrRaise(door, batch.swingYaw, batch.raiseHeight, batch.openSeconds);

            _held.Add(new HeldDoor
            {
                t = door,
                pos = door.position,
                rot = door.rotation
            });

            _opening = false;
            TryAdvance();
        }

        /// <param name="soundId">Sound group for this door. Defaults to the shared story-door sound.</param>
        public static IEnumerator AnimateSwingOrRaise(Transform door, float swingYaw, float raiseHeight,
            float openSeconds, string soundId = null)
        {
            if (door == null)
                yield break;

            FreezeDoor(door);
            CaliGameplayAudio.PlayAtFor(
                string.IsNullOrEmpty(soundId) ? CaliSoundIds.DoorOpen : soundId, door.position, 5f);

            Quaternion fromRot = door.rotation;
            Vector3 fromPos = door.position;
            bool swing = Mathf.Abs(swingYaw) > 0.01f;
            Quaternion toRot = Quaternion.AngleAxis(swingYaw, Vector3.up) * fromRot;
            Vector3 toPos = fromPos + Vector3.up * Mathf.Max(0.1f, raiseHeight);
            yield return AnimateDoor(door, fromPos, fromRot, toPos, toRot, swing, openSeconds);
        }

        /// <summary>Reverse of <see cref="AnimateSwingOrRaise"/>. Plays Door Close. Not used by current open-only batches.</summary>
        public static IEnumerator AnimateClose(Transform door, float swingYaw, float raiseHeight, float closeSeconds)
        {
            if (door == null)
                yield break;

            FreezeDoor(door);
            CaliGameplayAudio.PlayAt(CaliSoundIds.DoorClose, door.position);

            Quaternion fromRot = door.rotation;
            Vector3 fromPos = door.position;
            bool swing = Mathf.Abs(swingYaw) > 0.01f;
            Quaternion toRot = Quaternion.AngleAxis(-swingYaw, Vector3.up) * fromRot;
            Vector3 toPos = fromPos - Vector3.up * Mathf.Max(0.1f, raiseHeight);
            yield return AnimateDoor(door, fromPos, fromRot, toPos, toRot, swing, closeSeconds);
        }

        static IEnumerator AnimateDoor(
            Transform door,
            Vector3 fromPos,
            Quaternion fromRot,
            Vector3 toPos,
            Quaternion toRot,
            bool swing,
            float seconds)
        {
            float dur = Mathf.Max(0.05f, seconds);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                if (swing)
                    door.rotation = Quaternion.Slerp(fromRot, toRot, u);
                else
                    door.position = Vector3.Lerp(fromPos, toPos, u);
                yield return null;
            }

            if (swing)
                door.rotation = toRot;
            else
                door.position = toPos;
        }

        public static void FreezeDoor(Transform door)
        {
            var anims = door.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null)
                    anims[i].enabled = false;
            }

            var joints = door.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null)
                    Destroy(joints[i]);
            }

            var rbs = door.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                var rb = rbs[i];
                if (rb == null)
                    continue;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            var cols = door.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (batches == null)
                return;

            for (int i = 0; i < batches.Length; i++)
            {
                var b = batches[i];
                if (b == null)
                    continue;

                Gizmos.color = i == 0 ? Color.green : (i == 1 ? Color.yellow : Color.red);
                if (b.enemiesRoot != null)
                {
                    Gizmos.DrawWireSphere(b.enemiesRoot.position + Vector3.up, 0.6f);
                    Gizmos.DrawLine(transform.position, b.enemiesRoot.position);
                }

                if (b.door != null)
                {
                    Gizmos.DrawCube(b.door.position + Vector3.up * 1.2f, new Vector3(0.4f, 2.4f, 0.15f));
                    Gizmos.DrawLine(transform.position, b.door.position);
                }

                if (b.latch != null)
                {
                    Gizmos.color = new Color(1f, 0.6f, 0.1f);
                    Gizmos.DrawWireSphere(b.latch.transform.position + Vector3.up, 0.45f);
                    Gizmos.DrawLine(transform.position, b.latch.transform.position);
                }
            }
        }
    }
}
