using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Swaps a seat's horse with Horse Realistic Pegasus for a limited time,
    /// then restores the original. Updates chain, local input, and coop bindings.
    /// </summary>
    public class HorsePegasusForm : MonoBehaviour
    {
        public static HorsePegasusForm Instance { get; private set; }

        [Header("Horses")]
        [Tooltip("Leave empty to auto-find 'Horse Realistic Pegasus'.")]
        public MAnimal pegasus;

        [Tooltip("Leave empty to auto-find 'Horse Realistic'.")]
        public MAnimal horseRealistic;

        [Tooltip("Leave empty to auto-find 'Horse Unicorn'.")]
        public MAnimal horseUnicorn;

        [Header("Defaults")]
        public float defaultDuration = 30f;

        MAnimal _swappedOut;
        int _seat = -1;
        float _endsAt = -1f;
        bool _useLocalTimer = true;

        public bool IsActive => _swappedOut != null;
        public int ActiveSeat => _seat;

        void Awake()
        {
            Instance = this;
            ResolveRefs();
            if (pegasus != null && pegasus.gameObject.activeSelf)
                pegasus.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!_useLocalTimer || !IsActive)
                return;

            if (Time.time >= _endsAt)
                Restore();
        }

        public void ResolveRefs()
        {
            if (horseRealistic == null)
                horseRealistic = FindAnimalByName("Horse Realistic");
            if (horseUnicorn == null)
                horseUnicorn = FindAnimalByName("Horse Unicorn");
            if (pegasus == null)
                pegasus = FindAnimalByName("Horse Realistic Pegasus");
        }

        public bool TryActivateFromAnimal(MAnimal animal, float duration = -1f)
        {
            ResolveRefs();
            int seat = SeatOf(animal);
            if (seat < 0)
            {
                Debug.LogWarning($"[HorsePegasusForm] '{animal?.name}' is not a swappable seat horse.", this);
                return false;
            }

            return Activate(seat, duration);
        }

        public bool Activate(int seat, float duration = -1f, bool useLocalTimer = true)
        {
            ResolveRefs();
            if (pegasus == null)
            {
                Debug.LogWarning("[HorsePegasusForm] Missing Horse Realistic Pegasus in the scene.", this);
                return false;
            }

            if (duration < 0f)
                duration = defaultDuration;

            if (IsActive)
                Restore();

            var original = seat == 0 ? horseRealistic : horseUnicorn;
            if (original == null)
                return false;

            var pose = original.transform;
            pegasus.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (pegasus.RB != null)
            {
                pegasus.RB.isKinematic = original.RB != null && original.RB.isKinematic;
                pegasus.RB.linearVelocity = Vector3.zero;
            }

            original.gameObject.SetActive(false);
            pegasus.gameObject.SetActive(true);
            DisableMalbersInput(pegasus);
            BindSeat(seat, pegasus);

            _swappedOut = original;
            _seat = seat;
            _useLocalTimer = useLocalTimer;
            _endsAt = Time.time + duration;

            FocusIfLocal(seat, pegasus);

            Debug.Log($"[HorsePegasusForm] Seat {seat} → Pegasus for {duration:0.#}s", this);
            return true;
        }

        public void Restore()
        {
            if (!IsActive)
                return;

            int seat = _seat;
            var original = _swappedOut;
            var peg = pegasus;

            if (peg != null && original != null)
            {
                original.transform.SetPositionAndRotation(peg.transform.position, peg.transform.rotation);
                peg.gameObject.SetActive(false);
                original.gameObject.SetActive(true);
                BindSeat(seat, original);
                FocusIfLocal(seat, original);
            }

            Debug.Log($"[HorsePegasusForm] Seat {seat} restored to '{original?.name}'", this);

            _swappedOut = null;
            _seat = -1;
            _endsAt = -1f;
        }

        public int SeatOf(MAnimal animal)
        {
            if (animal == null)
                return -1;

            ResolveRefs();

            if (animal == pegasus)
                return _seat >= 0 ? _seat : -1;
            if (animal == horseRealistic || animal.name == "Horse Realistic")
                return 0;
            if (animal == horseUnicorn || animal.name == "Horse Unicorn")
                return 1;
            if (animal.name.Contains("Realistic") && !animal.name.Contains("Pegasus"))
                return 0;
            if (animal.name.Contains("Unicorn"))
                return 1;

            return -1;
        }

        static void FocusIfLocal(int seat, MAnimal animal)
        {
            if (animal == null)
                return;

            if (!Cali.Network.CoopSessionStarter.IsOnline)
            {
                Cali.Network.CoopCameraFocus.FocusOnHorse(animal);
                return;
            }

            var runner = Cali.Network.CoopSessionStarter.Runner;
            if (runner != null && Cali.Network.CoopInput.GetLocalSeat(runner) == seat)
                Cali.Network.CoopCameraFocus.FocusOnHorse(animal);
        }

        void BindSeat(int seat, MAnimal animal)
        {
            var local = FindFirstObjectByType<LocalDualHorseInput>();
            if (local != null)
            {
                if (seat == 0) local.horseA = animal;
                else local.horseB = animal;
            }

            var chain = FindFirstObjectByType<SoftHorseChain>();
            if (chain != null)
            {
                if (seat == 0) chain.SetHorseA(animal);
                else chain.SetHorseB(animal);
            }

            var coop = Cali.Network.CoopGameController.Instance;
            if (coop != null)
            {
                if (seat == 0) coop.SetHorseA(animal);
                else coop.SetHorseB(animal);
            }
        }

        static void DisableMalbersInput(MAnimal animal)
        {
            if (animal == null)
                return;

            var links = animal.GetComponentsInChildren<MalbersAnimations.InputSystem.MInputLink>(true);
            foreach (var link in links)
                link.enabled = false;

            var pis = animal.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
            foreach (var pi in pis)
                pi.enabled = false;
        }

        static MAnimal FindAnimalByName(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var animal in animals)
            {
                if (animal.gameObject.name == objectName)
                    return animal;
            }

            foreach (var animal in animals)
            {
                if (animal.gameObject.name.Contains(objectName))
                    return animal;
            }

            return null;
        }
    }
}
