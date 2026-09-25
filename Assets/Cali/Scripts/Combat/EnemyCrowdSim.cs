using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cali.Combat
{
    public enum EnemyLod : byte
    {
        Hot = 0,
        Warm = 1,
        Cold = 2,
        Frozen = 3
    }

    /// <summary>
    /// Caps how many enemies run full animation/AI so a large crowd can exist.
    /// Humanoids never use Malbers. Nearest fighters stay hot; the rest idle or cheap-walk.
    /// </summary>
    public static class EnemyCrowdSim
    {
        public const int MaxHot = 12;
        public const int MaxWarm = 24;
        public const float HotRange = 34f;
        public const float WarmRange = 48f;
        public const float ColdRange = 72f;

        public static bool AnyInCombat { get; private set; }

        static readonly List<ChainKillableEnemy> All = new(128);
        static readonly Comparison<ChainKillableEnemy> ByDist = CompareDist;
        static int _tickFrame = -1;
        static float _nextAssign;

        public static void Register(ChainKillableEnemy enemy)
        {
            if (enemy != null && !All.Contains(enemy))
                All.Add(enemy);
        }

        public static void Unregister(ChainKillableEnemy enemy)
        {
            All.Remove(enemy);
        }

        public static void EnsureTicked()
        {
            if (_tickFrame == Time.frameCount)
                return;
            _tickFrame = Time.frameCount;
            Tick();
        }

        static int CompareDist(ChainKillableEnemy a, ChainKillableEnemy b)
        {
            return a.CrowdDist.CompareTo(b.CrowdDist);
        }

        static void Tick()
        {
            var horses = EnemyCombatTargets.HorsesCached;
            AnyInCombat = false;

            for (int i = All.Count - 1; i >= 0; i--)
            {
                var e = All[i];
                if (e == null || e.IsDead)
                {
                    All.RemoveAt(i);
                    continue;
                }

                float best = float.PositiveInfinity;
                Vector3 p = e.transform.position;
                for (int h = 0; h < horses.Count; h++)
                {
                    var t = horses[h];
                    if (t == null)
                        continue;
                    float dx = t.position.x - p.x;
                    float dz = t.position.z - p.z;
                    float sq = dx * dx + dz * dz;
                    if (sq < best)
                        best = sq;
                }

                e.CrowdDist = best < float.PositiveInfinity ? Mathf.Sqrt(best) : 9999f;
                float detect = e.DetectRadius;
                if (e.CrowdDist <= detect)
                    AnyInCombat = true;
            }

            if (Time.time < _nextAssign)
                return;

            _nextAssign = Time.time + 0.12f;
            if (ChainKillableEnemy.ShouldSimulateAi)
                AssignLods();
        }

        static void AssignLods()
        {
            All.Sort(ByDist);

            int hot = 0;
            int warm = 0;
            for (int i = 0; i < All.Count; i++)
            {
                var e = All[i];
                if (e == null || e.IsDead)
                    continue;

                float d = e.CrowdDist;
                EnemyLod lod;
                if (d <= HotRange && hot < MaxHot)
                {
                    lod = EnemyLod.Hot;
                    hot++;
                }
                else if (d <= WarmRange && warm < MaxWarm)
                {
                    lod = EnemyLod.Warm;
                    warm++;
                }
                else if (d <= ColdRange)
                {
                    lod = EnemyLod.Cold;
                }
                else
                {
                    lod = EnemyLod.Frozen;
                }

                e.ApplyCrowdLod(lod);
            }
        }
    }
}
