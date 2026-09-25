using UnityEngine;

namespace Cali
{
    /// <summary>
    /// Destroys this GameObject after the given number of seconds.
    /// </summary>
    public class DestroyAfterSeconds : MonoBehaviour
    {
        [Tooltip("Seconds to wait before destroying this object.")]
        public float seconds = 5f;

        [Tooltip("If off, the timer starts when you call Play() instead of on Start.")]
        public bool destroyOnStart = true;

        void Start()
        {
            if (destroyOnStart)
                Play();
        }

        public void Play()
        {
            CancelInvoke(nameof(DoDestroy));
            Invoke(nameof(DoDestroy), Mathf.Max(0f, seconds));
        }

        void DoDestroy()
        {
            Destroy(gameObject);
        }
    }
}
