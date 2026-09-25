using Cali.Network;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// One-shot streaming volume: first player horse that enters enables
    /// objectsToTurnOn and disables objectsToTurnOff.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cali/Object Toggle")]
    public class CoopObjectToggle : MonoBehaviour
    {
        [Header("Turn ON when a horse enters")]
        public GameObject[] objectsToTurnOn;

        [Header("Turn OFF when a horse enters")]
        public GameObject[] objectsToTurnOff;

        [Header("Trigger")]
        [Tooltip("Leave empty to auto-add a trigger box. Scale this object to cover the volume.")]
        public BoxCollider zone;

        [Tooltip("If on, the Turn ON list starts disabled until the trigger fires.")]
        public bool disableActivateOnAwake = true;

        bool _fired;

        void Reset()
        {
            EnsureTrigger();
        }

        void Awake()
        {
            EnsureTrigger();
            if (!disableActivateOnAwake)
                return;

            SetActive(objectsToTurnOn, false);
        }

        void OnTriggerEnter(Collider other)
        {
            TryFire(other);
        }

        void TryFire(Collider other)
        {
            if (_fired)
                return;
            if (!IsPlayerHorse(other))
                return;

            _fired = true;
            SetActive(objectsToTurnOn, true);
            SetActive(objectsToTurnOff, false);
        }

        static void SetActive(GameObject[] list, bool on)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null)
                    list[i].SetActive(on);
            }
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
            return n == "Horse Realistic" || n == "Horse Unicorn";
        }

        void EnsureTrigger()
        {
            if (zone == null)
                zone = GetComponent<BoxCollider>();
            if (zone == null)
                zone = gameObject.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            if (zone.size.sqrMagnitude < 0.01f)
                zone.size = Vector3.one;
        }
    }
}
