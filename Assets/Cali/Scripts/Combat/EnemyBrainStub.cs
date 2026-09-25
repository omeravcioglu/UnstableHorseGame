using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Obsolete idle brain. Self-removes and ensures <see cref="EnemyChaseBrain"/> is present.
    /// </summary>
    [System.Obsolete("Replaced by EnemyChaseBrain")]
    public class EnemyBrainStub : MonoBehaviour
    {
        void Awake()
        {
            if (GetComponent<EnemyChaseBrain>() == null)
                gameObject.AddComponent<EnemyChaseBrain>();
            Destroy(this);
        }
    }
}
