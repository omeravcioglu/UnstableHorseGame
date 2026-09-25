using System.Collections.Generic;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Drop this on any object (or a parent) so the horse chain does not collide,
    /// rest, kill, push, or sweep it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cali/Chain Ignore")]
    public class ChainIgnore : MonoBehaviour
    {
        static readonly HashSet<Transform> Roots = new();

        void OnEnable()
        {
            Roots.Add(transform);
        }

        void OnDisable()
        {
            Roots.Remove(transform);
        }

        public static bool IsIgnored(Collider col)
        {
            if (col == null)
                return false;

            Transform t = col.transform;
            while (t != null)
            {
                if (Roots.Contains(t))
                    return true;
                t = t.parent;
            }

            return false;
        }
    }
}
