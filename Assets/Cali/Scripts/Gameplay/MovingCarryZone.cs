using System.Collections.Generic;
using Cali.Combat;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Trigger box on a moving object. A horse inside this boundary is carried
    /// with it after they have actually planted (not on the landing frame).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(850)]
    [AddComponentMenu("Cali/Moving Carry Zone")]
    public class MovingCarryZone : MonoBehaviour
    {
        [Tooltip("Extra size around the object. Keep this tight so nearby landings are not grabbed.")]
        public Vector3 padding = new Vector3(0.12f, 1.05f, 0.12f);

        [Tooltip("Grounded frames required before carry starts. Avoids yanking on the land frame.")]
        public int plantFrames = 2;

        [Tooltip("Leave empty to auto-build a trigger box.")]
        public BoxCollider zone;

        readonly Dictionary<MAnimal, int> _inside = new();
        readonly Dictionary<MAnimal, int> _planted = new();
        readonly HashSet<MAnimal> _riding = new();
        readonly HashSet<MAnimal> _skipCarry = new();
        readonly List<MAnimal> _scratch = new();
        Vector3 _lastPos;
        Quaternion _lastRot;

        public int PassengerCount => _riding.Count;

        void Awake()
        {
            EnsureZone();
        }

        void OnEnable()
        {
            IncludeOwnLayerInHorseGround();
            _lastPos = transform.position;
            _lastRot = transform.rotation;
        }

        void OnDisable()
        {
            _inside.Clear();
            _planted.Clear();
            _riding.Clear();
            _skipCarry.Clear();
        }

        void LateUpdate()
        {
            _scratch.Clear();
            _skipCarry.Clear();

            foreach (var pair in _inside)
            {
                var animal = pair.Key;
                if (animal == null)
                {
                    _scratch.Add(animal);
                    continue;
                }

                if (animal.Grounded)
                {
                    _planted.TryGetValue(animal, out int frames);
                    frames++;
                    _planted[animal] = frames;
                    if (frames >= Mathf.Max(1, plantFrames) && _riding.Add(animal))
                        _skipCarry.Add(animal);
                }
                else
                {
                    _planted[animal] = 0;
                    _riding.Remove(animal);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _inside.Remove(_scratch[i]);
                _planted.Remove(_scratch[i]);
                _riding.Remove(_scratch[i]);
            }

            CarryRiders();
            _lastPos = transform.position;
            _lastRot = transform.rotation;
        }

        public void NotifyEnter(Collider other)
        {
            var animal = Resolve(other);
            if (animal == null)
                return;
            _inside.TryGetValue(animal, out int n);
            _inside[animal] = n + 1;
        }

        public void NotifyExit(Collider other)
        {
            var animal = Resolve(other);
            if (animal == null)
                return;
            if (!_inside.TryGetValue(animal, out int n))
                return;
            n--;
            if (n <= 0)
            {
                _inside.Remove(animal);
                _planted.Remove(animal);
                _riding.Remove(animal);
            }
            else
            {
                _inside[animal] = n;
            }
        }

        void CarryRiders()
        {
            if (_riding.Count == 0)
                return;

            Vector3 deltaPos = transform.position - _lastPos;
            if (deltaPos.sqrMagnitude < 0.0000001f)
                return;

            _scratch.Clear();
            foreach (var animal in _riding)
            {
                if (animal == null || !_inside.ContainsKey(animal))
                {
                    _scratch.Add(animal);
                    continue;
                }
                if (_skipCarry.Contains(animal))
                    continue;

                animal.Position += deltaPos;
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _riding.Remove(_scratch[i]);
                _planted.Remove(_scratch[i]);
            }
        }

        void EnsureZone()
        {
            if (zone != null)
            {
                zone.isTrigger = true;
                return;
            }

            Transform existing = transform.Find("Carry Boundary");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                zone = go.GetComponent<BoxCollider>();
            }
            else
            {
                go = new GameObject("Carry Boundary");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;
                zone = go.AddComponent<BoxCollider>();
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
            }

            zone.isTrigger = true;
            ApplyBounds(zone);

            var relay = go.GetComponent<MovingCarryBoundary>();
            if (relay == null)
                relay = go.AddComponent<MovingCarryBoundary>();
            relay.zone = this;
        }

        void ApplyBounds(BoxCollider box)
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds mb = filter.sharedMesh.bounds;
                Vector3 size = mb.size + padding;
                size.x = Mathf.Max(0.35f, size.x);
                size.y = Mathf.Max(0.7f, size.y);
                size.z = Mathf.Max(0.35f, size.z);
                box.center = mb.center + new Vector3(0f, padding.y * 0.15f, 0f);
                box.size = size;
                return;
            }

            var col = GetComponent<Collider>();
            if (col != null && col != box)
            {
                Bounds b = col.bounds;
                Vector3 localCenter = transform.InverseTransformPoint(b.center);
                Vector3 localSize = transform.InverseTransformVector(b.size);
                localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
                box.center = localCenter + new Vector3(0f, padding.y * 0.15f, 0f);
                box.size = localSize + padding;
                return;
            }

            box.center = new Vector3(0f, padding.y * 0.5f, 0f);
            box.size = new Vector3(1.6f, padding.y, 1.6f) + padding;
        }

        static MAnimal Resolve(Collider other)
        {
            if (other == null)
                return null;
            var animal = other.GetComponentInParent<MAnimal>();
            if (animal == null || animal.GetComponentInParent<ChainKillableEnemy>() != null)
                return null;
            return animal;
        }

        void IncludeOwnLayerInHorseGround()
        {
            int extra = 1 << gameObject.layer;
            int item = LayerMask.NameToLayer("Item");
            if (item >= 0)
                extra |= 1 << item;

            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var animal = animals[i];
                if (animal == null || animal.GetComponentInParent<ChainKillableEnemy>() != null)
                    continue;
                animal.groundLayer.Value |= extra;
            }
        }

        void OnDrawGizmosSelected()
        {
            BoxCollider box = zone;
            if (box == null)
            {
                var t = transform.Find("Carry Boundary");
                if (t != null)
                    box = t.GetComponent<BoxCollider>();
            }
            if (box == null)
                return;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.25f);
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }

    [DisallowMultipleComponent]
    public class MovingCarryBoundary : MonoBehaviour
    {
        [HideInInspector]
        public MovingCarryZone zone;

        void OnTriggerEnter(Collider other)
        {
            if (zone != null)
                zone.NotifyEnter(other);
        }

        void OnTriggerExit(Collider other)
        {
            if (zone != null)
                zone.NotifyExit(other);
        }
    }
}
