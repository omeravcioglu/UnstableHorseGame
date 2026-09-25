using System.Collections.Generic;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>Shared horse lookup for chase + spear throwers. Cached so enemies do not scan the scene every frame.</summary>
    public static class EnemyCombatTargets
    {
        static readonly List<Transform> Horses = new(4);
        static int _frame = -1;
        static float _nextScan;

        public static Transform FindNearestHorse(Vector3 from, float maxRange = float.PositiveInfinity)
        {
            return FindNearestHorseInRange(from, maxRange);
        }

        public static IReadOnlyList<Transform> HorsesCached
        {
            get
            {
                RefreshIfNeeded();
                return Horses;
            }
        }

        public static Transform FindNearestHorseInRange(Vector3 from, float detectRadius)
        {
            if (detectRadius <= 0.01f)
                return null;

            RefreshIfNeeded();

            Transform best = null;
            float bestSq = float.IsPositiveInfinity(detectRadius)
                ? float.PositiveInfinity
                : detectRadius * detectRadius;

            for (int i = 0; i < Horses.Count; i++)
            {
                var t = Horses[i];
                if (t == null)
                    continue;

                float dx = t.position.x - from.x;
                float dz = t.position.z - from.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = t;
                }
            }

            return best;
        }

        public static bool AnyHorseInCombat()
        {
            return ChainKillableEnemy.HasAnyHorseInDetectRange();
        }

        public static void Invalidate()
        {
            _frame = -1;
            _nextScan = 0f;
        }

        static void RefreshIfNeeded()
        {
            if (Time.frameCount == _frame)
                return;

            _frame = Time.frameCount;
            if (Time.time < _nextScan)
            {
                PruneNulls();
                return;
            }

            _nextScan = Time.time + 0.25f;
            Horses.Clear();

            var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var a = animals[i];
                if (a == null || !a.gameObject.activeInHierarchy)
                    continue;

                string n = a.name;
                if (n.IndexOf("Horse", System.StringComparison.Ordinal) < 0 &&
                    n.IndexOf("Pegasus", System.StringComparison.Ordinal) < 0 &&
                    n.IndexOf("Unicorn", System.StringComparison.Ordinal) < 0)
                    continue;

                if (a.GetComponent<ChainKillableEnemy>() != null)
                    continue;

                Horses.Add(a.transform);
            }
        }

        static void PruneNulls()
        {
            for (int i = Horses.Count - 1; i >= 0; i--)
            {
                if (Horses[i] == null)
                    Horses.RemoveAt(i);
            }
        }
    }
}
