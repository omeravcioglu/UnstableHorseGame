using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Trigger volume: horse that enters becomes Realistic Pegasus for <see cref="duration"/> seconds.
    /// Host-authoritative when a Fusion session is running.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PegasusPowerupTrigger : MonoBehaviour
    {
        [Tooltip("How long the Pegasus form lasts.")]
        public float duration = 30f;

        [Tooltip("Optional. Auto-finds HorsePegasusForm in the scene.")]
        public HorsePegasusForm form;

        [Tooltip("Seconds before the same horse can pick up again.")]
        public float reentryCooldown = 1f;

        float _cooldownUntil;
        Collider _col;

        void Reset()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
                _col.isTrigger = true;
        }

        void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
                _col.isTrigger = true;

            if (form == null)
                form = FindFirstObjectByType<HorsePegasusForm>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (Time.time < _cooldownUntil)
                return;

            var animal = other.GetComponentInParent<MAnimal>();
            if (animal == null)
                return;

            // Ignore if already pegasus.
            if (animal.name.Contains("Pegasus"))
                return;

            if (Cali.Network.CoopSessionStarter.IsOnline)
            {
                var runner = Cali.Network.CoopSessionStarter.Runner;
                if (runner == null || !runner.IsSharedModeMasterClient)
                    return;

                var coop = Cali.Network.CoopGameController.Instance;
                if (coop != null)
                {
                    if (coop.ServerActivatePegasus(animal, duration))
                        _cooldownUntil = Time.time + reentryCooldown;
                    return;
                }
            }

            if (form == null)
                form = HorsePegasusForm.Instance ?? FindFirstObjectByType<HorsePegasusForm>();

            if (form != null && form.TryActivateFromAnimal(animal, duration))
                _cooldownUntil = Time.time + reentryCooldown;
        }
    }
}
